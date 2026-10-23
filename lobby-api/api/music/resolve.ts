import { getStore } from '../../src/store.js';
import { sendJson, getClientIp, redisGet, redisSet, parseIsoDuration } from '../_utils.js';

interface MusicCandidate {
  videoId: string;
  title: string;
  channelTitle: string;
  durationSec: number;
  score: number;
}

export default async function handler(req: any, res: any) {
  if (req.method === 'OPTIONS') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Aura-Client');
    return res.status ? res.status(204).end() : (res.writeHead(204), res.end());
  }

  if (req.method !== 'GET') {
    return sendJson(res, 405, { error: 'Method Not Allowed' });
  }

  try {
    const store = getStore();
    const clientIp = getClientIp(req);

    // Rate limit: 60 requests per minute per IP
    const allowed = await store.checkRateLimit(`music:${clientIp}`, 60, 60);
    if (!allowed) {
      return sendJson(res, 429, { error: 'rate_limited', message: 'Too many requests' });
    }

    const url = new URL(req.url, 'http://localhost');
    const artist = (url.searchParams.get('artist') || '').trim();
    const title = (url.searchParams.get('title') || '').trim();
    const durationParam = url.searchParams.get('durationSec');
    const durationSec = durationParam ? parseFloat(durationParam) : 0;

    if (!artist || !title) {
      return sendJson(res, 400, { error: 'missing_parameters', message: 'artist and title are required' });
    }

    const normArtist = artist.toLowerCase();
    const normTitle = title.toLowerCase();
    const cacheKeyV2 = `music:resolve:v3:${normArtist}|${normTitle}`;

    // 1. Check Redis cache v2
    const cachedV2 = await redisGet(cacheKeyV2);
    if (cachedV2) {
      try {
        const parsed = JSON.parse(cachedV2);
        if (parsed && Array.isArray(parsed.candidates) && parsed.candidates.length > 0) {
          // Verify top candidates are not blocked
          const validCandidates: MusicCandidate[] = [];
          for (const cand of parsed.candidates) {
            const isBlocked = await redisGet(`music:embed-denied:v1:${cand.videoId}`);
            if (!isBlocked) {
              validCandidates.push(cand);
            }
          }

          if (validCandidates.length > 0) {
            res.setHeader('Cache-Control', 's-maxage=86400, max-age=3600');
            return sendJson(res, 200, {
              videoId: validCandidates[0].videoId,
              candidates: validCandidates
            });
          }
        }
      } catch {
        // Fallback to fresh resolve if parsing fails
      }
    }

    // 2. Check YouTube API Key
    const apiKey = process.env.YOUTUBE_API_KEY;
    if (!apiKey) {
      return sendJson(res, 503, { error: 'music_not_configured' });
    }

    // 3. Search YouTube Data API with broader scope (maxResults 15)
    // We search with queries designed to find Official, Topic, and Audio versions
    const searchQuery = `${artist} ${title}`;
    let searchUrl = `https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&videoEmbeddable=true&videoCategoryId=10&maxResults=15&q=${encodeURIComponent(searchQuery)}&key=${apiKey}`;

    let searchRes = await fetch(searchUrl);
    if (!searchRes.ok) {
      const errText = await searchRes.text();
      console.error('[YouTube search error]', searchRes.status, errText);
      if (searchRes.status === 403 || errText.includes('quotaExceeded')) {
        return sendJson(res, 503, { error: 'quota', detail: errText });
      }
      return sendJson(res, 503, { error: 'youtube_api_error', detail: errText });
    }

    let searchData: any = await searchRes.json();
    let items = searchData.items || [];

    // Fallback search without category filter if category 10 returned 0 results
    if (items.length === 0) {
      const fallbackSearchUrl = `https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&videoEmbeddable=true&maxResults=15&q=${encodeURIComponent(searchQuery)}&key=${apiKey}`;
      const fbRes = await fetch(fallbackSearchUrl);
      if (fbRes.ok) {
        const fbData: any = await fbRes.json();
        items = fbData.items || [];
      }
    }

    const videoIds = items.map((it: any) => it.id?.videoId).filter(Boolean);

    if (videoIds.length === 0) {
      return sendJson(res, 404, { error: 'not_found' });
    }

    // 4. Batch fetch contentDetails and status for exact duration and embeddable status
    const videosUrl = `https://www.googleapis.com/youtube/v3/videos?part=contentDetails,snippet,status&id=${videoIds.join(',')}&key=${apiKey}`;
    const videosRes = await fetch(videosUrl);
    if (!videosRes.ok) {
      const errText = await videosRes.text();
      console.error('[YouTube videos error]', videosRes.status, errText);
      if (videosRes.status === 403 || errText.includes('quotaExceeded')) {
        return sendJson(res, 503, { error: 'quota', detail: errText });
      }
      return sendJson(res, 503, { error: 'youtube_api_error', detail: errText });
    }

    const videosData: any = await videosRes.json();
    const videoDetails = videosData.items || [];

    if (videoDetails.length === 0) {
      return sendJson(res, 404, { error: 'not_found' });
    }

    // 5. Filter out non-embeddable videos and previously blocked IDs, then score
    const scoredList: MusicCandidate[] = [];

    for (const v of videoDetails) {
      const vid = v.id;
      if (!vid) continue;

      // Filter: Explicitly not embeddable according to YouTube API status
      if (v.status && v.status.embeddable === false) {
        continue;
      }

      // Filter: Check if previously reported as blocked by player
      const isBlocked = await redisGet(`music:embed-denied:v1:${vid}`);
      if (isBlocked) {
        continue;
      }

      const dur = parseIsoDuration(v.contentDetails?.duration || '');
      const channel = (v.snippet?.channelTitle || '').toLowerCase();
      const vidTitle = (v.snippet?.title || '').toLowerCase();

      const isTopic = channel.includes('- topic');
      const isOfficial = channel.includes('official') || channel.includes('vevo') || channel.includes(normArtist);
      const isAudio = vidTitle.includes('audio') || vidTitle.includes('lyric') || vidTitle.includes('soundtrack') || vidTitle.includes('аудио') || vidTitle.includes('трек');

      const diff = durationSec > 0 ? Math.abs(dur - durationSec) : 0;
      const within10 = durationSec > 0 ? diff <= 10 : true;

      let score = 50;

      // Duration scoring
      if (within10) {
        score += 100;
        if (durationSec > 0) {
          score += Math.max(0, (10 - diff) * 2);
        }
      } else {
        score -= Math.min(diff * 2, 80);
      }

      // Priority: Topic channels have the cleanest metadata and virtually never block embeds
      if (isTopic) score += 60;
      if (isAudio) score += 30;
      if (isOfficial) score += 20;

      // Title & artist keywords relevance
      if (vidTitle.includes(normTitle)) score += 25;
      if (vidTitle.includes(normArtist) || channel.includes(normArtist)) score += 25;

      scoredList.push({
        videoId: vid,
        title: v.snippet?.title || '',
        channelTitle: v.snippet?.channelTitle || '',
        durationSec: dur,
        score
      });
    }

    if (scoredList.length === 0) {
      return sendJson(res, 404, { error: 'no_embeddable_candidates' });
    }

    // Sort by score descending
    scoredList.sort((a, b) => b.score - a.score);

    // Limit to top 8 candidates
    const topCandidates = scoredList.slice(0, 8);
    const bestVideoId = topCandidates[0].videoId;

    const payload = {
      videoId: bestVideoId,
      candidates: topCandidates
    };

    // Cache in Redis for 30 days
    await redisSet(cacheKeyV2, JSON.stringify(payload), 30 * 86400);

    // Also update legacy v1 key for backward compatibility
    await redisSet(`music:resolve:${normArtist}|${normTitle}`, bestVideoId, 30 * 86400);

    res.setHeader('Cache-Control', 's-maxage=86400, max-age=3600');
    return sendJson(res, 200, payload);
  } catch (err: any) {
    return sendJson(res, 500, { error: 'internal_error', message: err?.message || 'Internal server error' });
  }
}

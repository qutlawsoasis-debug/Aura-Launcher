import { getStore } from '../../src/store.js';
import { sendJson, getClientIp, redisGet, redisSet, parseIsoDuration } from '../_utils.js';

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

    // Rate limit: 30 requests per minute per IP
    const allowed = await store.checkRateLimit(`music:${clientIp}`, 30, 60);
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
    const cacheKey = `music:resolve:${normArtist}|${normTitle}`;

    // 1. Check Redis cache (forever)
    const cachedVideoId = await redisGet(cacheKey);
    if (cachedVideoId) {
      res.setHeader('Cache-Control', 's-maxage=31536000, max-age=86400');
      return sendJson(res, 200, { videoId: cachedVideoId });
    }

    // 2. Check YouTube API Key
    const apiKey = process.env.YOUTUBE_API_KEY;
    if (!apiKey) {
      return sendJson(res, 503, { error: 'music_not_configured' });
    }

    // 3. Search YouTube Data API
    const searchQuery = `${artist} ${title} audio`;
    const searchUrl = `https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&videoEmbeddable=true&videoCategoryId=10&maxResults=5&q=${encodeURIComponent(searchQuery)}&key=${apiKey}`;

    const searchRes = await fetch(searchUrl);
    if (!searchRes.ok) {
      const errText = await searchRes.text();
      if (searchRes.status === 403 || errText.includes('quotaExceeded')) {
        return sendJson(res, 503, { error: 'quota' });
      }
      return sendJson(res, 503, { error: 'youtube_api_error' });
    }

    const searchData: any = await searchRes.json();
    const items = searchData.items || [];
    const videoIds = items.map((it: any) => it.id?.videoId).filter(Boolean);

    if (videoIds.length === 0) {
      return sendJson(res, 404, { error: 'not_found' });
    }

    // 4. Batch fetch contentDetails for exact duration
    const videosUrl = `https://www.googleapis.com/youtube/v3/videos?part=contentDetails,snippet&id=${videoIds.join(',')}&key=${apiKey}`;
    const videosRes = await fetch(videosUrl);
    if (!videosRes.ok) {
      const errText = await videosRes.text();
      if (videosRes.status === 403 || errText.includes('quotaExceeded')) {
        return sendJson(res, 503, { error: 'quota' });
      }
      return sendJson(res, 503, { error: 'youtube_api_error' });
    }

    const videosData: any = await videosRes.json();
    const videoDetails = videosData.items || [];

    if (videoDetails.length === 0) {
      return sendJson(res, 404, { error: 'not_found' });
    }

    // 5. Select best matching candidate: prefer ±7s difference, then - Topic and official channels
    let bestVideoId = videoDetails[0].id;
    let bestScore = -9999;

    for (const v of videoDetails) {
      const vid = v.id;
      const dur = parseIsoDuration(v.contentDetails?.duration || '');
      const channel = (v.snippet?.channelTitle || '').toLowerCase();
      const isTopic = channel.includes('- topic');
      const isOfficial = channel.includes('official') || channel.includes('vevo') || channel.includes(normArtist);

      const diff = durationSec > 0 ? Math.abs(dur - durationSec) : 0;
      const withinDelta = durationSec > 0 ? diff <= 7 : true;

      let score = 0;
      if (withinDelta) {
        score += 100;
        if (durationSec > 0) score += (7 - diff);
      } else {
        score -= Math.min(diff, 50);
      }

      if (isTopic) score += 50;
      else if (isOfficial) score += 25;

      if (score > bestScore) {
        bestScore = score;
        bestVideoId = vid;
      }
    }

    // 6. Save in Redis forever
    await redisSet(cacheKey, bestVideoId);

    res.setHeader('Cache-Control', 's-maxage=31536000, max-age=86400');
    return sendJson(res, 200, { videoId: bestVideoId });
  } catch (err: any) {
    return sendJson(res, 500, { error: 'internal_error', message: err?.message || 'Internal server error' });
  }
}

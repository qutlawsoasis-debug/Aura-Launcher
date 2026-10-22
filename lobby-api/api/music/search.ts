import { getStore } from '../../src/store.js';
import { sendJson, getClientIp, redisGet, redisSet, cleanTitle, cleanArtist, parseIsoDuration } from '../_utils.js';

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
    const q = (url.searchParams.get('q') || '').trim();
    const kind = (url.searchParams.get('kind') || 'tracks').trim().toLowerCase();

    if (!q) {
      return sendJson(res, 400, { error: 'missing_query', message: 'q is required' });
    }

    if (!['tracks', 'albums', 'artists'].includes(kind)) {
      return sendJson(res, 400, { error: 'invalid_kind', message: 'kind must be tracks, albums, or artists' });
    }

    const normQ = q.toLowerCase();
    const cacheKey = `music:search:${kind}:${normQ}`;

    // 1. Check Redis cache (7 days TTL)
    const cached = await redisGet(cacheKey);
    if (cached) {
      res.setHeader('Cache-Control', 's-maxage=86400');
      try {
        return sendJson(res, 200, JSON.parse(cached));
      } catch {
        // Fall through
      }
    }

    // 2. Check YouTube API Key
    const apiKey = process.env.YOUTUBE_API_KEY;
    if (!apiKey) {
      return sendJson(res, 503, { error: 'music_not_configured' });
    }

    let items: any[] = [];

    if (kind === 'tracks') {
      const searchUrl = `https://www.googleapis.com/youtube/v3/search?part=snippet&type=video&videoEmbeddable=true&videoCategoryId=10&maxResults=20&q=${encodeURIComponent(q)}&key=${apiKey}`;
      const searchRes = await fetch(searchUrl);
      if (!searchRes.ok) {
        const errText = await searchRes.text();
        if (searchRes.status === 403 || errText.includes('quotaExceeded')) {
          return sendJson(res, 503, { error: 'quota' });
        }
        return sendJson(res, 503, { error: 'youtube_api_error' });
      }

      const searchData: any = await searchRes.json();
      const rawItems = searchData.items || [];
      const videoIds = rawItems.map((it: any) => it.id?.videoId).filter(Boolean);

      const durationMap = new Map<string, number>();
      if (videoIds.length > 0) {
        const videosUrl = `https://www.googleapis.com/youtube/v3/videos?part=contentDetails&id=${videoIds.join(',')}&key=${apiKey}`;
        const videosRes = await fetch(videosUrl);
        if (videosRes.ok) {
          const videosData: any = await videosRes.json();
          for (const v of (videosData.items || [])) {
            durationMap.set(v.id, parseIsoDuration(v.contentDetails?.duration || ''));
          }
        }
      }

      items = rawItems.map((it: any) => {
        const vid = it.id?.videoId;
        const snip = it.snippet || {};
        const artwork = snip.thumbnails?.maxres?.url || snip.thumbnails?.high?.url || snip.thumbnails?.medium?.url || snip.thumbnails?.default?.url || '';
        return {
          id: vid,
          kind: 'track',
          title: cleanTitle(snip.title || ''),
          artist: cleanArtist(snip.channelTitle || '', snip.title || ''),
          artwork,
          durationSec: durationMap.get(vid) || 0
        };
      });
    } else if (kind === 'albums') {
      const searchUrl = `https://www.googleapis.com/youtube/v3/search?part=snippet&type=playlist&maxResults=20&q=${encodeURIComponent(q + ' album')}&key=${apiKey}`;
      const searchRes = await fetch(searchUrl);
      if (!searchRes.ok) {
        const errText = await searchRes.text();
        if (searchRes.status === 403 || errText.includes('quotaExceeded')) {
          return sendJson(res, 503, { error: 'quota' });
        }
        return sendJson(res, 503, { error: 'youtube_api_error' });
      }

      const searchData: any = await searchRes.json();
      const rawItems = searchData.items || [];

      items = rawItems.map((it: any) => {
        const pid = it.id?.playlistId;
        const snip = it.snippet || {};
        const artwork = snip.thumbnails?.maxres?.url || snip.thumbnails?.high?.url || snip.thumbnails?.medium?.url || snip.thumbnails?.default?.url || '';
        return {
          id: pid,
          kind: 'album',
          title: cleanTitle(snip.title || ''),
          artist: cleanArtist(snip.channelTitle || ''),
          artwork
        };
      });
    } else if (kind === 'artists') {
      const searchUrl = `https://www.googleapis.com/youtube/v3/search?part=snippet&type=channel&maxResults=15&q=${encodeURIComponent(q)}&key=${apiKey}`;
      const searchRes = await fetch(searchUrl);
      if (!searchRes.ok) {
        const errText = await searchRes.text();
        if (searchRes.status === 403 || errText.includes('quotaExceeded')) {
          return sendJson(res, 503, { error: 'quota' });
        }
        return sendJson(res, 503, { error: 'youtube_api_error' });
      }

      const searchData: any = await searchRes.json();
      const rawItems = searchData.items || [];

      items = rawItems.map((it: any) => {
        const cid = it.id?.channelId;
        const snip = it.snippet || {};
        const artwork = snip.thumbnails?.high?.url || snip.thumbnails?.medium?.url || snip.thumbnails?.default?.url || '';
        const name = cleanArtist(snip.channelTitle || snip.title || '');
        return {
          id: cid,
          kind: 'artist',
          title: name,
          artist: name,
          artwork
        };
      });
    }

    const payload = { items };

    // 3. Save in Redis (TTL: 7 days = 604800s)
    await redisSet(cacheKey, JSON.stringify(payload), 7 * 24 * 3600);

    res.setHeader('Cache-Control', 's-maxage=86400');
    return sendJson(res, 200, payload);
  } catch (err: any) {
    return sendJson(res, 500, { error: 'internal_error', message: err?.message || 'Internal server error' });
  }
}

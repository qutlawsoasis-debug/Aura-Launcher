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

    const allowed = await store.checkRateLimit(`music-album:${clientIp}`, 30, 60);
    if (!allowed) {
      return sendJson(res, 429, { error: 'rate_limited', message: 'Too many requests' });
    }

    const url = new URL(req.url, 'http://localhost');
    const id = (url.searchParams.get('id') || '').trim();
    if (!/^[a-zA-Z0-9_-]{12,64}$/.test(id)) {
      return sendJson(res, 400, { error: 'invalid_id', message: 'id must be a YouTube playlist id' });
    }

    const cacheKey = `music:album:${id}`;
    const cached = await redisGet(cacheKey);
    if (cached) {
      res.setHeader('Cache-Control', 's-maxage=86400');
      try {
        return sendJson(res, 200, JSON.parse(cached));
      } catch {
        // Fall through
      }
    }

    const apiKey = process.env.YOUTUBE_API_KEY;
    if (!apiKey) {
      return sendJson(res, 503, { error: 'music_not_configured' });
    }

    const listRes = await fetch(
      `https://www.googleapis.com/youtube/v3/playlists?part=snippet,contentDetails&id=${encodeURIComponent(id)}&key=${apiKey}`
    );
    if (!listRes.ok) {
      if (listRes.status === 403) return sendJson(res, 503, { error: 'quota' });
      return sendJson(res, 503, { error: 'youtube_api_error' });
    }
    const listData: any = await listRes.json();
    const meta = (listData.items || [])[0];
    if (!meta) {
      return sendJson(res, 404, { error: 'not_found', message: 'Playlist not found' });
    }

    const rawItems: any[] = [];
    let pageToken = '';
    do {
      const itemsUrl =
        `https://www.googleapis.com/youtube/v3/playlistItems?part=snippet,contentDetails&maxResults=100` +
        `&playlistId=${encodeURIComponent(id)}&key=${apiKey}` +
        (pageToken ? `&pageToken=${encodeURIComponent(pageToken)}` : '');
      const itemsRes = await fetch(itemsUrl);
      if (!itemsRes.ok) {
        if (itemsRes.status === 403) return sendJson(res, 503, { error: 'quota' });
        return sendJson(res, 503, { error: 'youtube_api_error' });
      }
      const itemsData: any = await itemsRes.json();
      rawItems.push(...(itemsData.items || []));
      pageToken = itemsData.nextPageToken || '';
    } while (pageToken && rawItems.length < 300);

    const videoIds = rawItems.map((it) => it.contentDetails?.videoId).filter(Boolean);
    const durationMap = new Map<string, number>();
    for (let i = 0; i < videoIds.length; i += 50) {
      const chunk = videoIds.slice(i, i + 50);
      const videosRes = await fetch(
        `https://www.googleapis.com/youtube/v3/videos?part=contentDetails&id=${chunk.join(',')}&key=${apiKey}`
      );
      if (videosRes.ok) {
        const videosData: any = await videosRes.json();
        for (const v of (videosData.items || [])) {
          durationMap.set(v.id, parseIsoDuration(v.contentDetails?.duration || ''));
        }
      }
    }

    const snip = meta.snippet || {};
    const artwork =
      snip.thumbnails?.maxres?.url || snip.thumbnails?.high?.url ||
      snip.thumbnails?.medium?.url || snip.thumbnails?.default?.url || '';

    const album = {
      id,
      kind: 'album',
      title: cleanTitle(snip.title || ''),
      artist: cleanArtist(snip.channelTitle || ''),
      artwork,
      year: snip.publishedAt ? new Date(snip.publishedAt).getFullYear() : 0,
      trackCount: meta.contentDetails?.itemCount || rawItems.length
    };

    const tracks = rawItems
      .filter((it) => it.contentDetails?.videoId)
      .map((it) => {
        const s = it.snippet || {};
        const videoId = it.contentDetails.videoId;
        const rawTitle = s.title || '';
        return {
          videoId,
          kind: 'track',
          title: cleanTitle(rawTitle),
          artist: cleanArtist(s.videoOwnerChannelTitle || s.channelTitle || '', rawTitle),
          artwork: s.thumbnails?.high?.url || s.thumbnails?.medium?.url || artwork,
          durationSec: durationMap.get(videoId) || 0
        };
      });

    const payload = { album, tracks };
    await redisSet(cacheKey, JSON.stringify(payload), 24 * 3600);
    res.setHeader('Cache-Control', 's-maxage=3600');
    return sendJson(res, 200, payload);
  } catch (err: any) {
    return sendJson(res, 500, { error: 'internal_error', message: err?.message || 'Internal server error' });
  }
}

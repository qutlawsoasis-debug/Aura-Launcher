import { getStore } from '../../src/store.js';
import { sendJson, getClientIp, redisGet, redisSet, cleanTitle, cleanArtist, parseIsoDuration } from '../_utils.js';
export default async function handler(req, res) {
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
        const id = (url.searchParams.get('id') || '').trim();
        const kind = (url.searchParams.get('kind') || 'playlist').trim().toLowerCase();
        if (!id) {
            return sendJson(res, 400, { error: 'missing_id', message: 'id is required' });
        }
        if (!['playlist', 'artist'].includes(kind)) {
            return sendJson(res, 400, { error: 'invalid_kind', message: 'kind must be playlist or artist' });
        }
        const cacheKey = `music:list:${kind}:${id}`;
        // 1. Check Redis cache (1 day TTL)
        const cached = await redisGet(cacheKey);
        if (cached) {
            res.setHeader('Cache-Control', 's-maxage=86400');
            try {
                return sendJson(res, 200, JSON.parse(cached));
            }
            catch {
                // Fall through
            }
        }
        // 2. Check YouTube API Key
        const apiKey = process.env.YOUTUBE_API_KEY;
        if (!apiKey) {
            return sendJson(res, 503, { error: 'music_not_configured' });
        }
        let targetPlaylistId = id;
        if (kind === 'artist') {
            const channelUrl = `https://www.googleapis.com/youtube/v3/channels?part=contentDetails&id=${encodeURIComponent(id)}&key=${apiKey}`;
            const channelRes = await fetch(channelUrl);
            if (!channelRes.ok) {
                const errText = await channelRes.text();
                if (channelRes.status === 403 || errText.includes('quotaExceeded')) {
                    return sendJson(res, 503, { error: 'quota' });
                }
                return sendJson(res, 503, { error: 'youtube_api_error' });
            }
            const channelData = await channelRes.json();
            const channelItems = channelData.items || [];
            if (channelItems.length === 0) {
                return sendJson(res, 404, { error: 'artist_not_found' });
            }
            const uploads = channelItems[0]?.contentDetails?.relatedPlaylists?.uploads;
            if (!uploads) {
                return sendJson(res, 404, { error: 'no_uploads_playlist' });
            }
            targetPlaylistId = uploads;
        }
        // Fetch playlist items
        const playlistUrl = `https://www.googleapis.com/youtube/v3/playlistItems?part=snippet,contentDetails&maxResults=50&playlistId=${encodeURIComponent(targetPlaylistId)}&key=${apiKey}`;
        const playlistRes = await fetch(playlistUrl);
        if (!playlistRes.ok) {
            const errText = await playlistRes.text();
            if (playlistRes.status === 403 || errText.includes('quotaExceeded')) {
                return sendJson(res, 503, { error: 'quota' });
            }
            return sendJson(res, 503, { error: 'youtube_api_error' });
        }
        const playlistData = await playlistRes.json();
        const rawItems = playlistData.items || [];
        const videoIds = [];
        for (const it of rawItems) {
            const vid = it.contentDetails?.videoId || it.snippet?.resourceId?.videoId;
            if (vid)
                videoIds.push(vid);
        }
        const durationMap = new Map();
        if (videoIds.length > 0) {
            const videosUrl = `https://www.googleapis.com/youtube/v3/videos?part=contentDetails&id=${videoIds.join(',')}&key=${apiKey}`;
            const videosRes = await fetch(videosUrl);
            if (videosRes.ok) {
                const videosData = await videosRes.json();
                for (const v of (videosData.items || [])) {
                    durationMap.set(v.id, parseIsoDuration(v.contentDetails?.duration || ''));
                }
            }
        }
        const items = rawItems.map((it) => {
            const vid = it.contentDetails?.videoId || it.snippet?.resourceId?.videoId;
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
        }).filter((it) => Boolean(it.id));
        const payload = { items };
        // 3. Save in Redis (TTL: 1 day = 86400s)
        await redisSet(cacheKey, JSON.stringify(payload), 86400);
        res.setHeader('Cache-Control', 's-maxage=86400');
        return sendJson(res, 200, payload);
    }
    catch (err) {
        return sendJson(res, 500, { error: 'internal_error', message: err?.message || 'Internal server error' });
    }
}

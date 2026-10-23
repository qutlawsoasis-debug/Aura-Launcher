import { getStore } from '../../src/store.js';
import { sendJson, parseJson, getClientIp, redisSet } from '../_utils.js';

export default async function handler(req: any, res: any) {
  if (req.method === 'OPTIONS') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Aura-Client');
    return res.status ? res.status(204).end() : (res.writeHead(204), res.end());
  }

  if (req.method !== 'POST') {
    return sendJson(res, 405, { error: 'Method Not Allowed' });
  }

  try {
    const store = getStore();
    const clientIp = getClientIp(req);

    // Rate limit: 60 reports per minute per IP
    const allowed = await store.checkRateLimit(`music:blocked:${clientIp}`, 60, 60);
    if (!allowed) {
      return sendJson(res, 429, { error: 'rate_limited', message: 'Too many requests' });
    }

    const body = await parseJson(req);
    const videoId = typeof body.videoId === 'string' ? body.videoId.trim() : '';

    if (body.code !== 101 && body.code !== 150) {
      return sendJson(res, 400, { error: 'invalid_block_code' });
    }

    if (!videoId || !/^[a-zA-Z0-9_-]{11}$/.test(videoId)) {
      return sendJson(res, 400, { error: 'invalid_video_id', message: 'Valid 11-char YouTube video ID required' });
    }

    const cacheKey = `music:embed-denied:v1:${videoId}`;
    // Store in Redis for 1 year (31536000 sec)
    await redisSet(cacheKey, '1', 31536000);

    return sendJson(res, 200, { ok: true, videoId, message: 'Video marked as blocked' });
  } catch (err: any) {
    return sendJson(res, 500, { error: 'internal_error', message: err?.message || 'Internal server error' });
  }
}

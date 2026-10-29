import { createHash } from 'node:crypto';
import { getStore } from '../src/store.js';
import { sendJson, getClientIp, redisGet, redisSet } from './_utils.js';

export default async function handler(req: any, res: any) {
  if (req.method === 'OPTIONS') return sendJson(res, 200, {});
  if (req.method !== 'GET') return sendJson(res, 405, { error: 'method_not_allowed' });
  const input = new URL(req.url, 'https://lobby-api.vercel.app');
  const artist = (input.searchParams.get('artist') || '').trim();
  const title = (input.searchParams.get('title') || '').trim();
  const duration = Number(input.searchParams.get('duration'));
  if (!artist || !title || artist.length > 200 || title.length > 300 || !Number.isFinite(duration) || duration < 1 || duration > 3600) {
    return sendJson(res, 400, { error: 'invalid_track_signature' });
  }
  try {
    if (!await getStore().checkRateLimit(`lyrics:${getClientIp(req)}`, 30, 60)) return sendJson(res, 429, { error: 'rate_limited' });
    const signature = JSON.stringify([artist.toLocaleLowerCase('en-US'), title.toLocaleLowerCase('en-US'), Math.round(duration)]);
    const key = `lyrics:synced:v1:${createHash('sha256').update(signature).digest('hex')}`;
    const cached = await redisGet(key);
    if (cached) {
      try { return sendJson(res, 200, JSON.parse(cached)); } catch { /* Fetch a valid record again. */ }
    }
    const upstream = new URL('https://lrclib.net/api/get');
    upstream.searchParams.set('artist_name', artist);
    upstream.searchParams.set('track_name', title);
    upstream.searchParams.set('duration', String(duration));
    const response = await fetch(upstream, { signal: AbortSignal.timeout(12000), headers: { 'User-Agent': 'Aura/1.0 (synced lyrics; https://github.com/qutlawsoasis-debug/Aura-Launcher)' } });
    if (response.status === 404) return sendJson(res, 404, { error: 'lyrics_not_found' });
    if (response.status === 429) {
      res.setHeader('Retry-After', response.headers.get('retry-after') || '60');
      return sendJson(res, 429, { error: 'lyrics_provider_rate_limited' });
    }
    if (!response.ok) return sendJson(res, 502, { error: 'lyrics_provider_unavailable' });
    const text = await response.text();
    if (text.length > 262144) return sendJson(res, 502, { error: 'lyrics_response_too_large' });
    const record = JSON.parse(text);
    const synced = typeof record.syncedLyrics === 'string' ? record.syncedLyrics : '';
    if (!synced && !record.instrumental) return sendJson(res, 404, { error: 'synced_lyrics_not_found' });
    const payload = { source: 'LRCLIB', id: record.id, artist, title, duration, instrumental: !!record.instrumental, syncedLyrics: synced };
    await redisSet(key, JSON.stringify(payload));
    res.setHeader('Cache-Control', 'public, max-age=3600, s-maxage=86400');
    return sendJson(res, 200, payload);
  } catch (error) {
    console.error('[lyrics] request failed', error instanceof Error ? error.name : 'unknown');
    return sendJson(res, 502, { error: 'lyrics_unavailable' });
  }
}
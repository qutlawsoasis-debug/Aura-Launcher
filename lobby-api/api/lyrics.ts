import { getStore } from '../src/store.js';
import { sendJson, getClientIp, redisGet, redisSet, cleanArtist, cleanTitle } from './_utils.js';
import { parseLrc, ParsedLyrics } from '../src/lyricsParser.js';
import { fetchLyricsFromLrclib } from '../src/lrclib.js';

export function normalizeLyricsCacheKey(artist: string, title: string): string {
  const normArtist = cleanArtist(artist)
    .toLowerCase()
    .trim()
    .replace(/\s+/g, ' ');
  const normTitle = cleanTitle(title)
    .toLowerCase()
    .trim()
    .replace(/\s+/g, ' ');
  return `lyrics:v1:${normArtist}|${normTitle}`;
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
    const allowed = await store.checkRateLimit(`lyrics:${clientIp}`, 60, 60);
    if (!allowed) {
      return sendJson(res, 429, { error: 'rate_limited', message: 'Too many requests' });
    }

    const url = new URL(req.url, 'http://localhost');
    const artist = (url.searchParams.get('artist') || '').trim();
    const title = (url.searchParams.get('title') || '').trim();
    const durationParam = url.searchParams.get('duration');
    const durationSec = durationParam ? parseFloat(durationParam) : 0;

    if (!artist || !title) {
      return sendJson(res, 400, {
        error: 'missing_parameters',
        message: 'artist and title are required'
      });
    }

    const cacheKey = normalizeLyricsCacheKey(artist, title);

    // 1. Check Redis cache
    const cached = await redisGet(cacheKey);
    if (cached) {
      try {
        const parsed = JSON.parse(cached);
        if (parsed.notFound) {
          res.setHeader('Cache-Control', 's-maxage=86400, max-age=3600');
          return sendJson(res, 404, { error: 'not_found', message: 'Lyrics not found' });
        }
        res.setHeader('Cache-Control', 's-maxage=86400, max-age=3600');
        return sendJson(res, 200, parsed);
      } catch {
        // Fallback to fetch if cached JSON is invalid
      }
    }

    // 2. Fetch from LRCLIB
    let lrclibRecord;
    try {
      lrclibRecord = await fetchLyricsFromLrclib(artist, title, durationSec);
    } catch (err: any) {
      if (err?.name === 'AbortError') {
        return sendJson(res, 504, { error: 'upstream_timeout', message: 'LRCLIB request timed out' });
      }
      throw err;
    }

    if (!lrclibRecord) {
      // Negative cache: 24 hours (86400 seconds)
      await redisSet(cacheKey, JSON.stringify({ notFound: true }), 86400);
      return sendJson(res, 404, { error: 'not_found', message: 'Lyrics not found' });
    }

    let result: ParsedLyrics;

    if (lrclibRecord.syncedLyrics) {
      const songDurationMs = durationSec > 0 ? durationSec * 1000 : undefined;
      result = parseLrc(lrclibRecord.syncedLyrics, songDurationMs);
    } else if (lrclibRecord.plainLyrics) {
      const plainLines = lrclibRecord.plainLyrics
        .split(/\r?\n/)
        .map(l => l.trim())
        .filter(l => l.length > 0);

      result = {
        source: 'lrclib',
        synced: false,
        lines: plainLines.map(text => ({
          t: 0,
          text,
          words: []
        }))
      };
    } else {
      // Negative cache: 24 hours
      await redisSet(cacheKey, JSON.stringify({ notFound: true }), 86400);
      return sendJson(res, 404, { error: 'not_found', message: 'Lyrics not found' });
    }

    // Positive cache in Redis permanently (no TTL)
    await redisSet(cacheKey, JSON.stringify(result));

    res.setHeader('Cache-Control', 's-maxage=86400, max-age=3600');
    return sendJson(res, 200, result);
  } catch (err: any) {
    console.error('[Lyrics handler error]', err);
    return sendJson(res, 502, { error: 'upstream_error', message: 'Failed to process lyrics' });
  }
}

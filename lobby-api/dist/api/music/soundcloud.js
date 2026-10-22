import { getStore } from '../../src/store.js';
import { sendJson, getClientIp, redisGet, redisSet, cleanTitle, cleanArtist } from '../_utils.js';
const USER_AGENT = 'Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/128.0.0.0 Safari/537.36';
const FALLBACK_CLIENT_ID = 'GytJkwrLebfCWmFfWOuDf7WObQo2BND9';
const CLIENT_ID_REDIS_KEY = 'music:soundcloud:client_id:v1';
const CLIENT_ID_TTL_SECONDS = 12 * 3600; // Автообновление каждые 12 часов
const SCRIPT_REGEX = /https:\/\/a-v2\.sndcdn\.com\/assets\/[^"]+\.js/g;
const CLIENT_ID_REGEX = /client_id:"([a-zA-Z0-9]{32})"/;
let memoryClientId = null;
let memoryExpiresAt = 0;
let refreshPromise = null;
export async function resolveSoundCloudClientId(forceRefresh = false) {
    const now = Date.now();
    if (!forceRefresh && memoryClientId && now < memoryExpiresAt) {
        return memoryClientId;
    }
    if (refreshPromise) {
        return refreshPromise;
    }
    refreshPromise = (async () => {
        try {
            if (!forceRefresh) {
                const cachedInRedis = await redisGet(CLIENT_ID_REDIS_KEY);
                if (cachedInRedis && cachedInRedis.trim().length === 32) {
                    memoryClientId = cachedInRedis.trim();
                    memoryExpiresAt = Date.now() + (CLIENT_ID_TTL_SECONDS * 1000);
                    return memoryClientId;
                }
            }
            const res = await fetch('https://soundcloud.com', {
                headers: {
                    'User-Agent': USER_AGENT,
                    'Accept': 'text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8'
                },
                signal: AbortSignal.timeout(8000)
            });
            if (res.ok) {
                const html = await res.text();
                const scriptMatches = [...html.matchAll(SCRIPT_REGEX)].map(m => m[0]);
                const reversedScripts = [...scriptMatches].reverse();
                for (let i = 0; i < Math.min(reversedScripts.length, 12); i++) {
                    const scriptUrl = reversedScripts[i];
                    try {
                        const sRes = await fetch(scriptUrl, {
                            headers: { 'User-Agent': USER_AGENT },
                            signal: AbortSignal.timeout(6000)
                        });
                        if (!sRes.ok)
                            continue;
                        const jsContent = await sRes.text();
                        const idMatch = jsContent.match(CLIENT_ID_REGEX);
                        if (idMatch && idMatch[1] && idMatch[1].length === 32) {
                            const freshId = idMatch[1];
                            memoryClientId = freshId;
                            memoryExpiresAt = Date.now() + (CLIENT_ID_TTL_SECONDS * 1000);
                            await redisSet(CLIENT_ID_REDIS_KEY, freshId, CLIENT_ID_TTL_SECONDS);
                            return freshId;
                        }
                    }
                    catch {
                        // Игнорируем ошибку отдельного скрипта и пробуем следующий
                    }
                }
            }
        }
        catch (err) {
            console.error('[SoundCloudResolver] Failed to resolve client_id:', err?.message || err);
        }
        // Если резолв не удался — используем fallback
        memoryClientId = FALLBACK_CLIENT_ID;
        memoryExpiresAt = Date.now() + (3600 * 1000); // 1 час перед повторной попыткой
        return FALLBACK_CLIENT_ID;
    })().finally(() => {
        refreshPromise = null;
    });
    return refreshPromise;
}
export async function searchSoundCloudTracks(query, limit = 20) {
    let clientId = await resolveSoundCloudClientId();
    const safeLimit = Math.min(Math.max(limit, 1), 50);
    const fetchSearch = async (cId) => {
        const searchUrl = `https://api-v2.soundcloud.com/search/tracks?q=${encodeURIComponent(query)}&limit=${safeLimit}&client_id=${cId}`;
        return await fetch(searchUrl, {
            headers: { 'User-Agent': USER_AGENT },
            signal: AbortSignal.timeout(8000)
        });
    };
    let searchRes = await fetchSearch(clientId);
    // Если ключ протух раньше времени (401), форсированно обновляем и повторяем 1 раз
    if (searchRes.status === 401) {
        clientId = await resolveSoundCloudClientId(true);
        searchRes = await fetchSearch(clientId);
    }
    if (!searchRes.ok) {
        throw new Error(`SoundCloud API returned status ${searchRes.status}`);
    }
    const data = await searchRes.json();
    const rawTracks = data.collection || [];
    return rawTracks.map((item) => {
        const artwork = item.artwork_url
            ? item.artwork_url.replace('-large', '-t500x500')
            : (item.user?.avatar_url || '');
        return {
            id: `sc_${item.id}`,
            scId: item.id,
            kind: 'track',
            title: cleanTitle(item.title || ''),
            artist: cleanArtist(item.user?.username || '', item.title || ''),
            artwork,
            durationSec: Math.round((item.duration || 0) / 1000),
            permalinkUrl: item.permalink_url,
            likesCount: item.likes_count || 0,
            playbackCount: item.playback_count || 0,
            source: 'soundcloud'
        };
    });
}
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
        // Rate limit: 60 requests per minute per IP
        const allowed = await store.checkRateLimit(`music:sc:${clientIp}`, 60, 60);
        if (!allowed) {
            return sendJson(res, 429, { error: 'rate_limited', message: 'Too many requests' });
        }
        const url = new URL(req.url, 'http://localhost');
        const action = (url.searchParams.get('action') || '').trim();
        const q = (url.searchParams.get('q') || '').trim();
        const limit = parseInt(url.searchParams.get('limit') || '20', 10);
        const force = url.searchParams.get('force') === 'true' || url.searchParams.get('force') === '1';
        // 1. Возврат актуального client_id (с ротацией)
        if (action === 'client_id') {
            const currentId = await resolveSoundCloudClientId(force);
            return sendJson(res, 200, {
                clientId: currentId,
                ttlSeconds: CLIENT_ID_TTL_SECONDS,
                expiresAt: memoryExpiresAt
            });
        }
        if (!q) {
            return sendJson(res, 400, { error: 'missing_query', message: 'q is required' });
        }
        const normQ = q.toLowerCase();
        const cacheKey = `music:sc:search:${normQ}:${limit}`;
        // 2. Проверяем кэш результатов поиска в Redis (12 часов)
        if (!force) {
            const cached = await redisGet(cacheKey);
            if (cached) {
                res.setHeader('Cache-Control', 's-maxage=21600');
                try {
                    return sendJson(res, 200, JSON.parse(cached));
                }
                catch {
                    // Fall through
                }
            }
        }
        // 3. Выполняем поиск
        const items = await searchSoundCloudTracks(q, limit);
        const payload = { items };
        // Кэшируем результат на 12 часов
        await redisSet(cacheKey, JSON.stringify(payload), CLIENT_ID_TTL_SECONDS);
        res.setHeader('Cache-Control', 's-maxage=21600');
        return sendJson(res, 200, payload);
    }
    catch (err) {
        console.error('[SoundCloudHandler] Error:', err);
        return sendJson(res, 500, {
            error: 'soundcloud_error',
            message: err?.message || 'Failed to search SoundCloud'
        });
    }
}

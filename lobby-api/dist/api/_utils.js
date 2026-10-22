export function parseJson(req) {
    if (req.body && typeof req.body === 'object') {
        return Promise.resolve(req.body);
    }
    return new Promise((resolve, reject) => {
        let body = '';
        req.on('data', (chunk) => {
            body += chunk;
            if (body.length > 1e6) {
                req.destroy();
                reject(new Error('Payload too large'));
            }
        });
        req.on('end', () => {
            if (!body)
                return resolve({});
            try {
                resolve(JSON.parse(body));
            }
            catch (err) {
                reject(err);
            }
        });
        req.on('error', reject);
    });
}
export function sendJson(res, statusCode, data) {
    if (typeof res.status === 'function' && typeof res.json === 'function') {
        res.setHeader('Access-Control-Allow-Origin', '*');
        res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Aura-Client, X-User-Id, X-User-Token');
        return res.status(statusCode).json(data);
    }
    const json = JSON.stringify(data);
    res.writeHead(statusCode, {
        'Content-Type': 'application/json; charset=utf-8',
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type, Authorization, X-Aura-Client, X-User-Id, X-User-Token'
    });
    res.end(json);
}
export function getClientIp(req) {
    const forwarded = req.headers ? req.headers['x-forwarded-for'] : undefined;
    if (forwarded) {
        const list = typeof forwarded === 'string' ? forwarded.split(',') : forwarded;
        return list[0].trim();
    }
    return req.socket?.remoteAddress || req.connection?.remoteAddress || '127.0.0.1';
}
const inMemoryCache = new Map();
export async function redisGet(key) {
    const upstashUrl = (process.env.UPSTASH_REDIS_REST_URL || process.env.KV_REST_API_URL || '').replace(/\/$/, '');
    const upstashToken = process.env.UPSTASH_REDIS_REST_TOKEN || process.env.KV_REST_API_TOKEN;
    if (upstashUrl && upstashToken) {
        try {
            const res = await fetch(`${upstashUrl}`, {
                method: 'POST',
                headers: {
                    Authorization: `Bearer ${upstashToken}`,
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(['GET', key])
            });
            if (res.ok) {
                const data = await res.json();
                return typeof data.result === 'string' ? data.result : (data.result ? JSON.stringify(data.result) : null);
            }
        }
        catch {
            // Fallback
        }
    }
    const cached = inMemoryCache.get(key);
    if (!cached)
        return null;
    if (cached.expiresAt && Date.now() > cached.expiresAt) {
        inMemoryCache.delete(key);
        return null;
    }
    return cached.value;
}
export async function redisSet(key, value, ttlSeconds) {
    const upstashUrl = (process.env.UPSTASH_REDIS_REST_URL || process.env.KV_REST_API_URL || '').replace(/\/$/, '');
    const upstashToken = process.env.UPSTASH_REDIS_REST_TOKEN || process.env.KV_REST_API_TOKEN;
    if (upstashUrl && upstashToken) {
        try {
            const cmd = ttlSeconds ? ['SET', key, value, 'EX', ttlSeconds.toString()] : ['SET', key, value];
            await fetch(`${upstashUrl}`, {
                method: 'POST',
                headers: {
                    Authorization: `Bearer ${upstashToken}`,
                    'Content-Type': 'application/json'
                },
                body: JSON.stringify(cmd)
            });
            return;
        }
        catch {
            // Fallback
        }
    }
    inMemoryCache.set(key, {
        value,
        expiresAt: ttlSeconds ? Date.now() + ttlSeconds * 1000 : undefined
    });
}
export function cleanTitle(raw) {
    if (!raw)
        return '';
    let t = raw;
    t = t.replace(/\s*[\(\[](?:Official\s*(?:Music\s*)?Video|Official\s*Audio|Official|Lyrics?|Lyric\s*Video|HD|4K|Clip\s*Officiel|Visualizer|Audio|Remastered|Music\s*Video|Video)[\)\]]/gi, '');
    return t.trim();
}
export function cleanArtist(channelTitle, rawTitle) {
    let a = channelTitle || '';
    a = a.replace(/\s*-\s*Topic$/i, '').replace(/\s*VEVO$/i, '');
    if (!a && rawTitle && rawTitle.includes(' - ')) {
        a = rawTitle.split(' - ')[0];
    }
    return a.trim() || 'Исполнитель';
}
export function parseIsoDuration(iso) {
    if (!iso)
        return 0;
    const match = iso.match(/PT(?:(\d+)H)?(?:(\d+)M)?(?:(\d+)S)?/);
    if (!match)
        return 0;
    const h = parseInt(match[1] || '0', 10);
    const m = parseInt(match[2] || '0', 10);
    const s = parseInt(match[3] || '0', 10);
    return h * 3600 + m * 60 + s;
}

import { getStore } from '../src/store.js';
import { sendJson } from './_utils.js';
export default async function handler(req, res) {
    if (req.method === 'OPTIONS') {
        res.setHeader('Access-Control-Allow-Origin', '*');
        res.setHeader('Access-Control-Allow-Methods', 'GET, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');
        return res.status ? res.status(204).end() : (res.writeHead(204), res.end());
    }
    if (req.method !== 'GET') {
        return sendJson(res, 405, { error: 'Method Not Allowed' });
    }
    try {
        const urlStr = req.url || '';
        const cleanUrl = urlStr.split('?')[0];
        // Check if this is a raw texture request: /csl/raw/{hash}.png or /api/csl/raw/{hash}.png
        const rawMatch = cleanUrl.match(/\/raw\/([a-fA-F0-9]+?)(?:\.png)?$/);
        if (rawMatch) {
            const sha1 = rawMatch[1].toLowerCase();
            const store = getStore();
            const pngBuffer = await store.getSkinByHash(sha1);
            if (!pngBuffer) {
                return sendJson(res, 404, { error: 'Texture not found' });
            }
            if (res.setHeader) {
                res.setHeader('Content-Type', 'image/png');
                res.setHeader('Cache-Control', 'public, max-age=31536000, immutable');
                res.setHeader('Access-Control-Allow-Origin', '*');
            }
            if (typeof res.status === 'function' && typeof res.send === 'function') {
                return res.status(200).send(pngBuffer);
            }
            res.writeHead(200, {
                'Content-Type': 'image/png',
                'Cache-Control': 'public, max-age=31536000, immutable',
                'Access-Control-Allow-Origin': '*'
            });
            return res.end(pngBuffer);
        }
        // Otherwise it's a CSL json request: /csl/{username}.json or /api/csl/{username}.json or query param ?username=...
        let username = req.query?.username || '';
        if (!username) {
            const match = cleanUrl.match(/\/csl\/([^/?]+?)(?:\.json)?$/);
            if (match) {
                username = match[1];
            }
        }
        if (username.endsWith('.json')) {
            username = username.slice(0, -5);
        }
        username = username.trim();
        if (!username) {
            return sendJson(res, 400, { error: 'Username is required' });
        }
        const store = getStore();
        const skinRecord = await store.getSkin(username.toLowerCase());
        if (!skinRecord) {
            return sendJson(res, 404, { error: 'Skin not found' });
        }
        const proto = req.headers['x-forwarded-proto'] || 'https';
        const host = req.headers['host'] || 'lobby-api.vercel.app';
        const baseUrl = `${proto}://${host}`;
        const textureUrl = `${baseUrl}/csl/raw/${skinRecord.sha1}.png`;
        const isSlim = skinRecord.model === 'slim';
        const cslResponse = {
            username: skinRecord.nickname,
            skins: {
                default: isSlim ? null : textureUrl,
                slim: isSlim ? textureUrl : null
            },
            cape: null
        };
        return sendJson(res, 200, cslResponse);
    }
    catch (err) {
        return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
    }
}

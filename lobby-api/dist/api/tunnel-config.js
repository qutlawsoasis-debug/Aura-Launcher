import { sendJson } from './_utils.js';
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
        const clientHeader = req.headers['x-aura-client'];
        if (clientHeader !== 'launcher') {
            return sendJson(res, 403, { error: 'Forbidden: invalid client' });
        }
        const secret = process.env.PLAYIT_SECRET;
        if (!secret) {
            return sendJson(res, 500, { error: 'PLAYIT_SECRET not configured on server' });
        }
        return sendJson(res, 200, {
            secret,
            publicAddress: 'pgsql-jill.tun.ply.gg',
            publicPort: 38062
        });
    }
    catch (err) {
        return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
    }
}

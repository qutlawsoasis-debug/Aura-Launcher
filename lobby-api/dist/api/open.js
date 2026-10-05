import { getStore } from '../src/store.js';
import { parseJson, sendJson } from './_utils.js';
export default async function handler(req, res) {
    if (req.method === 'OPTIONS') {
        res.setHeader('Access-Control-Allow-Origin', '*');
        res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization');
        return res.status ? res.status(204).end() : (res.writeHead(204), res.end());
    }
    if (req.method !== 'POST') {
        return sendJson(res, 405, { error: 'Method Not Allowed' });
    }
    try {
        const body = await parseJson(req);
        const code = (body.code || '').toUpperCase().trim();
        const hostToken = body.hostToken;
        const tunnelAddress = body.tunnelAddress;
        const store = getStore();
        const lobby = await store.get(code);
        if (!lobby) {
            return sendJson(res, 404, { error: 'Lobby not found' });
        }
        if (lobby.hostToken !== hostToken) {
            return sendJson(res, 403, { error: 'Unauthorized: invalid host token' });
        }
        if (!tunnelAddress) {
            return sendJson(res, 400, { error: 'tunnelAddress is required' });
        }
        lobby.status = 'open';
        lobby.tunnelAddress = tunnelAddress;
        lobby.lastHeartbeat = Date.now();
        await store.set(lobby, 60);
        return sendJson(res, 200, {
            success: true,
            code: lobby.code,
            status: lobby.status,
            tunnelAddress: lobby.tunnelAddress
        });
    }
    catch (err) {
        return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
    }
}

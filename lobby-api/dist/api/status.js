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
        const code = (req.query?.code || '').toUpperCase().trim();
        if (!code) {
            return sendJson(res, 400, { error: 'Query parameter code is required' });
        }
        const store = getStore();
        const lobby = await store.get(code);
        if (!lobby) {
            return sendJson(res, 404, { error: 'Lobby not found' });
        }
        return sendJson(res, 200, {
            code: lobby.code,
            status: lobby.status,
            tunnelAddress: lobby.tunnelAddress,
            playerCount: lobby.players.length,
            players: lobby.players,
            hostName: lobby.hostName,
            lastHeartbeat: lobby.lastHeartbeat
        });
    }
    catch (err) {
        return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
    }
}

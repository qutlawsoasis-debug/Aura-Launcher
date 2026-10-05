import crypto from 'crypto';
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
        const store = getStore();
        const code = await store.generateCode();
        const hostToken = crypto.randomUUID();
        const hostName = body.hostName || 'Host';
        const lobby = {
            code,
            hostToken,
            hostName,
            status: 'waiting',
            tunnelAddress: null,
            createdAt: Date.now(),
            lastHeartbeat: Date.now(),
            players: [hostName]
        };
        // 60 seconds TTL
        await store.set(lobby, 60);
        return sendJson(res, 201, {
            code,
            hostToken,
            status: lobby.status,
            createdAt: lobby.createdAt
        });
    }
    catch (err) {
        return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
    }
}

import { handleUserProfile } from '../../src/friendRoutes.js';
export default async function handler(req, res) {
    try {
        if (req.method === 'OPTIONS') {
            res.setHeader('Access-Control-Allow-Origin', '*');
            res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
            res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Aura-Client, X-User-Id, X-User-Token');
            return res.status ? res.status(204).end() : (res.writeHead(204), res.end());
        }
        return await handleUserProfile(req, res);
    }
    catch (err) {
        console.error('[lobby-api/api/user/profile] Uncaught handler error:', err);
        const headers = {
            'Content-Type': 'application/json',
            'Access-Control-Allow-Origin': '*'
        };
        if (res.status) {
            return res.status(500).json({ error: 'Internal Server Error' });
        }
        res.writeHead(500, headers);
        res.end(JSON.stringify({ error: 'Internal Server Error' }));
    }
}

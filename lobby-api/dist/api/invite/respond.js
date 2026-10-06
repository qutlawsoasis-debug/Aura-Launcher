import { handleInviteRespond } from '../../src/friendRoutes.js';
export default async function handler(req, res) {
    if (req.method === 'OPTIONS') {
        res.setHeader('Access-Control-Allow-Origin', '*');
        res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Aura-Client, X-User-Id, X-User-Token');
        return res.status ? res.status(204).end() : (res.writeHead(204), res.end());
    }
    return handleInviteRespond(req, res);
}

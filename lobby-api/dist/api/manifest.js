import { getStore, computeModManifestHash, computeLobbyModSync } from '../src/store.js';
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
        const playerName = (body.playerName || '').trim();
        if (!code || !playerName) {
            return sendJson(res, 400, { error: 'code and playerName required' });
        }
        const store = getStore();
        const lobby = await store.get(code);
        if (!lobby || lobby.status === 'closed') {
            return sendJson(res, 404, { error: 'Lobby not found or closed' });
        }
        const rawManifest = Array.isArray(body.manifest) ? body.manifest : [];
        const cleanManifest = rawManifest.slice(0, 400).map((m) => ({
            id: String(m.id || '').slice(0, 100),
            name: String(m.name || m.id || '').slice(0, 100),
            version: String(m.version || '').slice(0, 50),
            enabled: Boolean(m.enabled)
        }));
        const hash = computeModManifestHash(cleanManifest);
        lobby.manifests = lobby.manifests || {};
        lobby.manifestHashes = lobby.manifestHashes || {};
        lobby.manifests[playerName] = cleanManifest;
        lobby.manifestHashes[playerName] = hash;
        await store.set(lobby, 1800);
        const modSync = computeLobbyModSync(lobby);
        return sendJson(res, 200, {
            success: true,
            hash,
            modSync
        });
    }
    catch (err) {
        return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
    }
}

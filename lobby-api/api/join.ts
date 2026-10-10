import { getStore, computeModManifestHash } from '../src/store.js';
import { parseJson, sendJson } from './_utils.js';

export default async function handler(req: any, res: any) {
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
    const playerName = body.playerName || 'Guest';

    const store = getStore();
    const lobby = await store.get(code);

    if (!lobby || lobby.status === 'closed') {
      return sendJson(res, 404, { error: 'Lobby not found or closed' });
    }

    if (lobby.kickedPlayers && lobby.kickedPlayers.some((p: string) => p.toLowerCase() === playerName.toLowerCase())) {
      return sendJson(res, 403, { error: 'Вы были исключены из этого лобби' });
    }

    if (!lobby.players.includes(playerName)) {
      lobby.players.push(playerName);
    }

    lobby.playerHeartbeats = lobby.playerHeartbeats || {};
    lobby.playerHeartbeats[playerName.toLowerCase()] = Date.now();

    if (Array.isArray(body.manifest)) {
      lobby.manifests = lobby.manifests || {};
      lobby.manifestHashes = lobby.manifestHashes || {};
      const cleanManifest = body.manifest.slice(0, 400).map((m: any) => ({
        id: String(m.id || '').slice(0, 100),
        name: String(m.name || m.id || '').slice(0, 100),
        version: String(m.version || '').slice(0, 50),
        enabled: Boolean(m.enabled)
      }));
      lobby.manifests[playerName] = cleanManifest;
      lobby.manifestHashes[playerName] = computeModManifestHash(cleanManifest);
    }

    await store.set(lobby, 1800);

    return sendJson(res, 200, {
      success: true,
      code: lobby.code,
      status: lobby.status,
      tunnelAddress: lobby.tunnelAddress,
      playerCount: lobby.players.length
    });
  } catch (err: any) {
    return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
  }
}

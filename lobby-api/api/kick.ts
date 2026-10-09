import { getStore } from '../src/store.js';
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
    const hostToken = (body.hostToken || '').trim();
    const targetPlayer = (body.player || body.playerName || body.targetPlayer || '').trim();

    if (!code || !hostToken || !targetPlayer) {
      return sendJson(res, 400, { error: 'code, hostToken and player are required' });
    }

    const store = getStore();
    const lobby = await store.get(code);

    if (!lobby || lobby.status === 'closed') {
      return sendJson(res, 404, { error: 'Lobby not found or closed' });
    }

    if (lobby.hostToken !== hostToken) {
      return sendJson(res, 403, { error: 'Unauthorized: invalid host token' });
    }

    if (targetPlayer.toLowerCase() === lobby.hostName.toLowerCase()) {
      return sendJson(res, 400, { error: 'Cannot kick host from their own lobby' });
    }

    // Удаляем игрока из списка участников, манифестов и пульсов
    lobby.players = lobby.players.filter(p => p.toLowerCase() !== targetPlayer.toLowerCase());
    if (lobby.manifests) delete lobby.manifests[targetPlayer];
    if (lobby.manifestHashes) delete lobby.manifestHashes[targetPlayer];
    if (lobby.playerHeartbeats) delete lobby.playerHeartbeats[targetPlayer.toLowerCase()];

    // Заносим в список исключённых
    lobby.kickedPlayers = lobby.kickedPlayers || [];
    if (!lobby.kickedPlayers.some(p => p.toLowerCase() === targetPlayer.toLowerCase())) {
      lobby.kickedPlayers.push(targetPlayer);
    }

    await store.set(lobby, 1800);

    return sendJson(res, 200, {
      success: true,
      code: lobby.code,
      players: lobby.players,
      playerCount: lobby.players.length,
      kicked: targetPlayer
    });
  } catch (err: any) {
    return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
  }
}

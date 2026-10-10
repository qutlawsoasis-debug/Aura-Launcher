import { getStore, computeLobbyModSync } from '../src/store.js';
import { sendJson } from './_utils.js';

export default async function handler(req: any, res: any) {
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
    const code = ((req.query?.code as string) || '').toUpperCase().trim();
    if (!code) {
      return sendJson(res, 400, { error: 'Query parameter code is required' });
    }

    const store = getStore();
    const lobby = await store.get(code);

    if (!lobby) {
      return sendJson(res, 404, { error: 'Lobby not found' });
    }

    const playerName = ((req.query?.player as string) || '').trim();

    // 1. Проверка, не исключён ли запрашивающий игрок
    if (playerName && lobby.kickedPlayers && lobby.kickedPlayers.some(p => p.toLowerCase() === playerName.toLowerCase())) {
      return sendJson(res, 200, {
        code: lobby.code,
        status: 'closed',
        kicked: true,
        playerCount: lobby.players.length,
        players: lobby.players,
        hostName: lobby.hostName
      });
    }

    // 2. Обновление пульса гостя
    const now = Date.now();
    lobby.playerHeartbeats = lobby.playerHeartbeats || {};
    if (playerName) {
      lobby.playerHeartbeats[playerName.toLowerCase()] = now;
    }

    // 3. Авто-очистка гостей, от которых нет пульса более 25 секунд
    const hostLower = lobby.hostName.toLowerCase();
    let playersChanged = false;

    const activePlayers = lobby.players.filter(p => {
      const pLower = p.toLowerCase();
      if (pLower === hostLower) return true;
      if (!lobby.playerHeartbeats![pLower]) {
        lobby.playerHeartbeats![pLower] = now;
        playersChanged = true;
      }
      const lastBeat = lobby.playerHeartbeats![pLower];
      if (now - lastBeat > 25000) {
        playersChanged = true;
        delete lobby.playerHeartbeats![pLower];
        if (lobby.manifests) delete lobby.manifests[p];
        if (lobby.manifestHashes) delete lobby.manifestHashes[p];
        return false;
      }
      return true;
    });

    if (playersChanged) {
      lobby.players = activePlayers;
      await store.set(lobby, 1800);
    } else if (playerName) {
      await store.set(lobby, 1800);
    }

    const modSync = computeLobbyModSync(lobby);

    return sendJson(res, 200, {
      code: lobby.code,
      status: lobby.status,
      tunnelAddress: lobby.tunnelAddress,
      playerCount: lobby.players.length,
      players: lobby.players,
      hostName: lobby.hostName,
      lastHeartbeat: lobby.lastHeartbeat,
      modSync,
      kicked: false
    });
  } catch (err: any) {
    return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
  }
}

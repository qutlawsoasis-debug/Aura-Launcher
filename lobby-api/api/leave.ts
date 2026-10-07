import { getStore } from '../src/store.js';
import { parseJson, sendJson } from './_utils.js';

export default async function handler(req: any, res: any) {
  if (req.method === 'OPTIONS') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Aura-Client, X-User-Id, X-User-Token');
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
      return sendJson(res, 200, { success: true, message: 'Lobby closed or not found' });
    }

    lobby.players = lobby.players.filter(p => p.toLowerCase() !== playerName.toLowerCase());
    await store.set(lobby, 1800);

    return sendJson(res, 200, {
      success: true,
      code: lobby.code,
      players: lobby.players,
      playerCount: lobby.players.length
    });
  } catch (err: any) {
    return sendJson(res, 500, { error: err.message || 'Internal server error' });
  }
}

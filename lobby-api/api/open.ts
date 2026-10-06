import { getStore, Lobby } from '../src/store.js';
import { parseJson, sendJson } from './_utils.js';

export default async function handler(req: any, res: any) {
  if (req.method === 'OPTIONS') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-User-Id, X-User-Token');
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

    if (!code) {
      return sendJson(res, 400, { error: 'code is required' });
    }

    if (!hostToken) {
      return sendJson(res, 400, { error: 'hostToken is required' });
    }

    if (!tunnelAddress) {
      return sendJson(res, 400, { error: 'tunnelAddress is required' });
    }

    const store = getStore();
    let lobby = await store.get(code);

    if (lobby) {
      if (lobby.hostToken !== hostToken) {
        return sendJson(res, 403, { error: 'Unauthorized: invalid host token' });
      }
      lobby.status = 'open';
      lobby.tunnelAddress = tunnelAddress;
      lobby.lastHeartbeat = Date.now();
    } else {
      // Recreate lobby if it expired during slow world/tunnel startup
      lobby = {
        code,
        hostToken,
        hostName: body.hostName || 'Host',
        status: 'open',
        tunnelAddress,
        createdAt: Date.now(),
        lastHeartbeat: Date.now(),
        players: [body.hostName || 'Host']
      };
    }

    // 2 hours TTL for active game
    await store.set(lobby, 7200);

    return sendJson(res, 200, {
      success: true,
      code: lobby.code,
      status: lobby.status,
      tunnelAddress: lobby.tunnelAddress
    });
  } catch (err: any) {
    return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
  }
}


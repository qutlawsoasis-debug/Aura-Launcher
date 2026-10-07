import crypto from 'crypto';
import { getStore, Lobby, computeModManifestHash } from '../src/store.js';
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
    const store = getStore();
    const code = await store.generateCode();
    const hostToken = crypto.randomUUID();
    const hostName = body.hostName || 'Host';

    const lobby: Lobby = {
      code,
      hostToken,
      hostName,
      status: 'waiting',
      tunnelAddress: null,
      createdAt: Date.now(),
      lastHeartbeat: Date.now(),
      players: [hostName]
    };

    if (Array.isArray(body.manifest)) {
      const cleanManifest = body.manifest.slice(0, 400).map((m: any) => ({
        id: String(m.id || '').slice(0, 100),
        name: String(m.name || m.id || '').slice(0, 100),
        version: String(m.version || '').slice(0, 50),
        enabled: Boolean(m.enabled)
      }));
      lobby.manifests = { [hostName]: cleanManifest };
      lobby.manifestHashes = { [hostName]: computeModManifestHash(cleanManifest) };
    }

    // 30 minutes TTL while waiting
    await store.set(lobby, 1800);

    return sendJson(res, 201, {
      code,
      hostToken,
      status: lobby.status,
      createdAt: lobby.createdAt
    });
  } catch (err: any) {
    return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
  }
}

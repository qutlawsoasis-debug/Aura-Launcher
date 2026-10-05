import crypto from 'crypto';
import { getStore, SkinRecord } from '../src/store.js';
import { validatePngSkin, isValidNickname, getClientIp } from '../src/skinUtils.js';
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
    const store = getStore();

    // Rate limiting: 10 requests per minute per IP
    const clientIp = getClientIp(req);
    const allowed = await store.checkRateLimit(`skin:${clientIp}`, 10, 60);
    if (!allowed) {
      return sendJson(res, 429, { error: 'Rate limit exceeded: max 10 requests per minute' });
    }

    const body = await parseJson(req);
    const { nickname, skinBase64, model, ownerToken } = body;

    // Validate nickname
    if (!isValidNickname(nickname)) {
      return sendJson(res, 400, { error: 'Invalid nickname format (3-16 chars, a-zA-Z0-9_)' });
    }

    // Validate model
    const skinModel: 'default' | 'slim' = model === 'slim' ? 'slim' : 'default';

    // Validate PNG data & dimensions
    const validation = validatePngSkin(skinBase64);
    if (!validation.valid || !validation.buffer || !validation.sha1) {
      return sendJson(res, 400, { error: validation.error || 'Invalid skin PNG' });
    }

    const nickLower = nickname.toLowerCase();
    const existing = await store.getSkin(nickLower);

    let token = ownerToken;

    if (existing) {
      // Overwriting requires matching ownerToken
      if (!existing.ownerToken || existing.ownerToken !== ownerToken) {
        return sendJson(res, 403, { error: 'Этот ник уже занят другим игроком, выбери другой' });
      }
      token = existing.ownerToken;
    } else {
      // Generate new token on first upload if not supplied or new
      if (!token) {
        token = crypto.randomUUID();
      }
    }

    const skinRecord: SkinRecord = {
      nickname,
      ownerToken: token,
      model: skinModel,
      sha1: validation.sha1,
      skinBase64: validation.buffer.toString('base64'),
      updatedAt: Date.now()
    };

    // TTL 30 days
    const TTL_30_DAYS = 30 * 24 * 3600;
    await store.setSkin(skinRecord, TTL_30_DAYS);
    await store.setSkinByHash(validation.sha1, validation.buffer, TTL_30_DAYS);

    return sendJson(res, 200, {
      success: true,
      nickname,
      model: skinModel,
      sha1: validation.sha1,
      ownerToken: token
    });
  } catch (err: any) {
    return sendJson(res, 500, { error: err.message || 'Internal Server Error' });
  }
}

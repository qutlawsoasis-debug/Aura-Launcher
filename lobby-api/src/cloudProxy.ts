import crypto from 'crypto';
import { getStore } from './store.js';
import { getClientIp, sendJson } from '../api/_utils.js';

/**
 * Cloud worlds: the launcher talks to lobby-api, lobby-api talks to the world manager with the manager secret.
 * The secret lives only in the Vercel env (CLOUD_MANAGER_SECRET) and on the manager; the launcher has none.
 *
 * World uploads (zip, often > 4.5 MB, more than a Vercel function accepts) go straight to the manager with a
 * short-lived upload ticket signed here with CLOUD_UPLOAD_KEY, which the manager checks with the same key.
 */

export const MAX_BODY_BYTES = 64 * 1024;
export const UPLOAD_TICKET_SECONDS = 600;

/** Manager paths the proxy may reach, by lobby-api action. Nothing else is forwarded. */
export const MANAGER_ROUTES: Record<string, { method: 'GET' | 'POST'; path: string; timeoutMs: number }> = {
  worlds: { method: 'GET', path: 'worlds', timeoutMs: 15_000 },
  // Vercel Hobby stops a function at 60 s: answer before that
  create: { method: 'POST', path: 'lobbies/create', timeoutMs: 55_000 },
  stop: { method: 'POST', path: 'lobbies/stop', timeoutMs: 30_000 },
  share: { method: 'POST', path: 'lobbies/share', timeoutMs: 15_000 },
  delete: { method: 'POST', path: 'worlds/delete', timeoutMs: 30_000 },
};

export function managerUrl(): string {
  return (process.env.CLOUD_MANAGER_URL || '').replace(/\/+$/, '');
}

/** The upload ticket: hex HMAC-SHA256 over "owner\nworldName\nexpires" (raw, unescaped strings). */
export function signUploadTicket(key: string, owner: string, worldName: string, expires: number): string {
  return crypto.createHmac('sha256', key).update(`${owner}\n${worldName}\n${expires}`, 'utf8').digest('hex');
}

/** Reads the raw body with a size cap; resolves to the text, or null when it is too large. */
export function readBody(req: any, limit = MAX_BODY_BYTES): Promise<string | null> {
  if (req.body !== undefined && req.body !== null) {
    const text = typeof req.body === 'string' ? req.body : Buffer.isBuffer(req.body) ? req.body.toString('utf8') : JSON.stringify(req.body);
    return Promise.resolve(Buffer.byteLength(text, 'utf8') > limit ? null : text);
  }
  return new Promise((resolve, reject) => {
    const chunks: Buffer[] = [];
    let size = 0;
    let done = false;
    req.on('data', (c: any) => {
      if (done) return;
      const b = Buffer.isBuffer(c) ? c : Buffer.from(c);
      size += b.length;
      if (size > limit) {
        done = true;
        resolve(null);
        req.destroy?.();
        return;
      }
      chunks.push(b);
    });
    req.on('end', () => { if (!done) { done = true; resolve(Buffer.concat(chunks).toString('utf8')); } });
    req.on('error', (e: any) => { if (!done) { done = true; reject(e); } });
  });
}

function playerOf(req: any, bodyText: string): string {
  const q = req.query?.player;
  if (typeof q === 'string' && q) return q;
  try {
    const b = bodyText ? JSON.parse(bodyText) : {};
    return String(b.owner || b.player || '').trim();
  } catch {
    return '';
  }
}

function cors(res: any) {
  res.setHeader('Access-Control-Allow-Origin', '*');
  res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
  res.setHeader('Access-Control-Allow-Headers', 'Content-Type, X-Aura-Client');
}

/** Rate limit per IP + player: 60 requests a minute (create: 10). */
async function allowed(req: any, action: string, player: string): Promise<boolean> {
  try {
    const store = getStore();
    const max = action === 'create' || action === 'upload-ticket' ? 10 : 60;
    return await store.checkRateLimit(`cloud:${action}:${getClientIp(req)}:${player.toLowerCase()}`, max, 60);
  } catch {
    return true; // the store is down: do not take cloud worlds down with it
  }
}

/** Forwards one action to the manager and returns its answer as is (status, body). */
export async function proxyToManager(req: any, res: any, action: string) {
  if (req.method === 'OPTIONS') {
    cors(res);
    res.statusCode = 204;
    return res.end();
  }
  const route = MANAGER_ROUTES[action];
  if (!route) return sendJson(res, 404, { error: 'Unknown action' });
  if (req.method !== route.method) return sendJson(res, 405, { error: 'Method Not Allowed' });

  const base = managerUrl();
  const secret = process.env.CLOUD_MANAGER_SECRET || '';
  if (!base || !secret) return sendJson(res, 503, { error: 'Облачные миры не настроены' });

  let bodyText = '';
  if (route.method === 'POST') {
    const b = await readBody(req).catch(() => null);
    if (b === null) return sendJson(res, 413, { error: 'Слишком большой запрос' });
    bodyText = b;
  }
  const player = playerOf(req, bodyText);
  if (!(await allowed(req, action, player))) return sendJson(res, 429, { error: 'Слишком много запросов, попробуйте через минуту' });

  let url = `${base}/${route.path}`;
  if (route.method === 'GET') {
    const p = typeof req.query?.player === 'string' ? req.query.player : '';
    url += `?player=${encodeURIComponent(p)}`;
  }

  const ac = new AbortController();
  const timer = setTimeout(() => ac.abort(), route.timeoutMs);
  try {
    const r = await fetch(url, {
      method: route.method,
      headers: {
        'X-Aura-Secret': secret,
        ...(route.method === 'POST' ? { 'Content-Type': 'application/json' } : {}),
      },
      body: route.method === 'POST' ? bodyText : undefined,
      signal: ac.signal,
    });
    const text = await r.text();
    cors(res);
    res.statusCode = r.status;
    res.setHeader('Content-Type', r.headers.get('content-type') || 'application/json; charset=utf-8');
    return res.end(text);
  } catch (e: any) {
    const timeout = e?.name === 'AbortError';
    return sendJson(res, timeout ? 504 : 502, { error: timeout ? 'Менеджер миров не ответил вовремя' : 'Менеджер миров недоступен' });
  } finally {
    clearTimeout(timer);
  }
}

/** POST {owner, worldName}: a ticket for one direct upload to the manager, valid {@link UPLOAD_TICKET_SECONDS}. */
export async function uploadTicket(req: any, res: any) {
  if (req.method === 'OPTIONS') {
    cors(res);
    res.statusCode = 204;
    return res.end();
  }
  if (req.method !== 'POST') return sendJson(res, 405, { error: 'Method Not Allowed' });
  const key = process.env.CLOUD_UPLOAD_KEY || '';
  const base = managerUrl();
  if (!key || !base) return sendJson(res, 503, { error: 'Загрузка миров не настроена' });
  const text = await readBody(req).catch(() => null);
  if (text === null) return sendJson(res, 413, { error: 'Слишком большой запрос' });
  let body: any = {};
  try { body = text ? JSON.parse(text) : {}; } catch { return sendJson(res, 400, { error: 'Неверный JSON' }); }
  const owner = String(body.owner || '').trim();
  const worldName = String(body.worldName || '').trim();
  if (!owner || !worldName || owner.length > 64 || worldName.length > 128) return sendJson(res, 400, { error: 'Нужны owner и worldName' });
  if (!(await allowed(req, 'upload-ticket', owner))) return sendJson(res, 429, { error: 'Слишком много запросов, попробуйте через минуту' });
  const expires = Math.floor(Date.now() / 1000) + UPLOAD_TICKET_SECONDS;
  return sendJson(res, 200, {
    uploadUrl: `${base}/worlds/upload`,
    token: signUploadTicket(key, owner, worldName, expires),
    expires,
  });
}

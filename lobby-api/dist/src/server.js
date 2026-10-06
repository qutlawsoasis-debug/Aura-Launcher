import http from 'http';
import crypto from 'crypto';
import url from 'url';
import { getStore } from './store.js';
import { validatePngSkin, isValidNickname, getClientIp } from './skinUtils.js';
import { handleRegister, handleSync, handleFriendRequest, handleFriendRespond, handleFriendRemove, handleInvite, handleInviteRespond } from './friendRoutes.js';
import { generateLandingHtml } from './landing.js';
function parseBody(req) {
    return new Promise((resolve, reject) => {
        let body = '';
        req.on('data', chunk => {
            body += chunk;
            if (body.length > 1e6) {
                req.destroy();
                reject(new Error('Payload too large'));
            }
        });
        req.on('end', () => {
            if (!body)
                return resolve({});
            try {
                resolve(JSON.parse(body));
            }
            catch (err) {
                reject(err);
            }
        });
        req.on('error', reject);
    });
}
function sendJson(res, statusCode, data) {
    const json = JSON.stringify(data);
    res.writeHead(statusCode, {
        'Content-Type': 'application/json; charset=utf-8',
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type, Authorization, X-Aura-Client, X-User-Id, X-User-Token'
    });
    res.end(json);
}
export const server = http.createServer(async (req, res) => {
    if (req.method === 'OPTIONS') {
        res.writeHead(204, {
            'Access-Control-Allow-Origin': '*',
            'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
            'Access-Control-Allow-Headers': 'Content-Type, Authorization, X-Aura-Client, X-User-Id, X-User-Token'
        });
        res.end();
        return;
    }
    const parsedUrl = url.parse(req.url || '', true);
    const pathname = parsedUrl.pathname || '';
    console.log(`[LOBBY-API] ${req.method} ${pathname}`);
    try {
        const store = getStore();
        // Task 34: Landing pages for /j/:code, /f/:code, and /api/landing rewrite
        if (req.method === 'GET' && (pathname.startsWith('/j/') || pathname.startsWith('/f/') || pathname === '/api/landing')) {
            let isFriend = pathname.startsWith('/f/');
            let code = '';
            if (pathname === '/api/landing') {
                isFriend = parsedUrl.query.type === 'friend';
                code = typeof parsedUrl.query.code === 'string' ? parsedUrl.query.code : '';
            }
            else {
                code = pathname.substring(3);
            }
            const html = generateLandingHtml(isFriend ? 'friend' : 'join', code);
            if (!html) {
                res.writeHead(404, { 'Content-Type': 'text/plain; charset=utf-8' });
                res.end('Not Found');
                return;
            }
            res.writeHead(200, { 'Content-Type': 'text/html; charset=utf-8' });
            res.end(html);
            return;
        }
        // Task 32: Users & Friends & Invites
        if (pathname === '/api/user/register') {
            return await handleRegister(req, res);
        }
        if (pathname === '/api/sync') {
            return await handleSync(req, res);
        }
        if (pathname === '/api/friends/request') {
            return await handleFriendRequest(req, res);
        }
        if (pathname === '/api/friends/respond') {
            return await handleFriendRespond(req, res);
        }
        if (pathname === '/api/friends/remove') {
            return await handleFriendRemove(req, res);
        }
        if (pathname === '/api/invite' || pathname === '/api/invite/create') {
            return await handleInvite(req, res);
        }
        if (pathname === '/api/invite/respond') {
            return await handleInviteRespond(req, res);
        }
        // 0. GET /api/tunnel-config
        if (req.method === 'GET' && pathname === '/api/tunnel-config') {
            const clientHeader = req.headers['x-aura-client'];
            if (clientHeader !== 'launcher') {
                sendJson(res, 403, { error: 'Forbidden: invalid client' });
                return;
            }
            const rawSecret = process.env.PLAYIT_SECRET || '';
            const secret = rawSecret.replace(/^\ufeff/, '').trim();
            if (!secret) {
                sendJson(res, 500, { error: 'PLAYIT_SECRET not configured on server' });
                return;
            }
            sendJson(res, 200, {
                secret,
                publicAddress: 'pgsql-jill.tun.ply.gg',
                publicPort: 38062
            });
            return;
        }
        // 1. POST /api/lobby
        if (req.method === 'POST' && pathname === '/api/lobby') {
            const body = await parseBody(req);
            const code = await store.generateCode();
            const hostToken = crypto.randomUUID();
            const hostName = body.hostName || 'Host';
            const lobby = {
                code,
                hostToken,
                hostName,
                status: 'waiting',
                tunnelAddress: null,
                createdAt: Date.now(),
                lastHeartbeat: Date.now(),
                players: [hostName]
            };
            await store.set(lobby, 60);
            sendJson(res, 201, {
                code,
                hostToken,
                status: lobby.status,
                createdAt: lobby.createdAt
            });
            return;
        }
        // 2. POST /api/lobby/join
        if (req.method === 'POST' && (pathname === '/api/lobby/join' || pathname === '/api/join' || pathname === '/join')) {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const playerName = body.playerName || 'Guest';
            const lobby = await store.get(code);
            if (!lobby || lobby.status === 'closed') {
                sendJson(res, 404, { error: 'Lobby not found or closed' });
                return;
            }
            if (!lobby.players.includes(playerName)) {
                lobby.players.push(playerName);
                await store.set(lobby, 60);
            }
            sendJson(res, 200, {
                success: true,
                code: lobby.code,
                status: lobby.status,
                tunnelAddress: lobby.tunnelAddress,
                playerCount: lobby.players.length
            });
            return;
        }
        // 3. GET /api/lobby/status
        if (req.method === 'GET' && (pathname === '/api/lobby/status' || pathname === '/api/status' || pathname === '/status')) {
            const code = (parsedUrl.query.code || '').toUpperCase().trim();
            if (!code) {
                sendJson(res, 400, { error: 'Query parameter code is required' });
                return;
            }
            const lobby = await store.get(code);
            if (!lobby) {
                sendJson(res, 404, { error: 'Lobby not found' });
                return;
            }
            sendJson(res, 200, {
                code: lobby.code,
                status: lobby.status,
                tunnelAddress: lobby.tunnelAddress,
                playerCount: lobby.players.length,
                players: lobby.players,
                hostName: lobby.hostName,
                lastHeartbeat: lobby.lastHeartbeat
            });
            return;
        }
        // 4. POST /api/lobby/open
        if (req.method === 'POST' && (pathname === '/api/lobby/open' || pathname === '/api/open' || pathname === '/open')) {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const hostToken = body.hostToken;
            const tunnelAddress = body.tunnelAddress;
            const lobby = await store.get(code);
            if (!lobby) {
                sendJson(res, 404, { error: 'Lobby not found' });
                return;
            }
            if (lobby.hostToken !== hostToken) {
                sendJson(res, 403, { error: 'Unauthorized: invalid host token' });
                return;
            }
            if (!tunnelAddress) {
                sendJson(res, 400, { error: 'tunnelAddress is required' });
                return;
            }
            lobby.status = 'open';
            lobby.tunnelAddress = tunnelAddress;
            lobby.lastHeartbeat = Date.now();
            await store.set(lobby, 60);
            sendJson(res, 200, {
                success: true,
                code: lobby.code,
                status: lobby.status,
                tunnelAddress: lobby.tunnelAddress
            });
            return;
        }
        // 5. POST /api/lobby/heartbeat
        if (req.method === 'POST' && (pathname === '/api/lobby/heartbeat' || pathname === '/api/heartbeat' || pathname === '/heartbeat')) {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const hostToken = body.hostToken;
            const lobby = await store.get(code);
            if (!lobby) {
                sendJson(res, 404, { error: 'Lobby not found' });
                return;
            }
            if (lobby.hostToken !== hostToken) {
                sendJson(res, 403, { error: 'Unauthorized: invalid host token' });
                return;
            }
            lobby.lastHeartbeat = Date.now();
            await store.set(lobby, 60);
            sendJson(res, 200, {
                success: true,
                code: lobby.code,
                lastHeartbeat: lobby.lastHeartbeat
            });
            return;
        }
        // 6. POST /api/lobby/close
        if (req.method === 'POST' && (pathname === '/api/lobby/close' || pathname === '/api/close' || pathname === '/close')) {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const hostToken = body.hostToken;
            const lobby = await store.get(code);
            if (!lobby) {
                sendJson(res, 404, { error: 'Lobby not found' });
                return;
            }
            if (lobby.hostToken !== hostToken) {
                sendJson(res, 403, { error: 'Unauthorized: invalid host token' });
                return;
            }
            lobby.status = 'closed';
            lobby.lastHeartbeat = Date.now();
            await store.set(lobby, 10);
            sendJson(res, 200, {
                success: true,
                code: lobby.code,
                status: 'closed'
            });
            return;
        }
        // 7. POST /api/skin
        if (req.method === 'POST' && pathname === '/api/skin') {
            const clientIp = getClientIp(req);
            const allowed = await store.checkRateLimit(`skin:${clientIp}`, 10, 60);
            if (!allowed) {
                sendJson(res, 429, { error: 'Rate limit exceeded: max 10 requests per minute' });
                return;
            }
            const body = await parseBody(req);
            const { nickname, skinBase64, model, ownerToken } = body;
            if (!isValidNickname(nickname)) {
                sendJson(res, 400, { error: 'Invalid nickname format (3-16 chars, a-zA-Z0-9_)' });
                return;
            }
            const skinModel = model === 'slim' ? 'slim' : 'default';
            const validation = validatePngSkin(skinBase64);
            if (!validation.valid || !validation.buffer || !validation.sha1) {
                sendJson(res, 400, { error: validation.error || 'Invalid skin PNG' });
                return;
            }
            const nickLower = nickname.toLowerCase();
            const existing = await store.getSkin(nickLower);
            let token = ownerToken;
            if (existing) {
                if (!existing.ownerToken || existing.ownerToken !== ownerToken) {
                    sendJson(res, 403, { error: 'Этот ник уже занят другим игроком, выбери другой' });
                    return;
                }
                token = existing.ownerToken;
            }
            else {
                if (!token) {
                    token = crypto.randomUUID();
                }
            }
            const skinRecord = {
                nickname,
                ownerToken: token,
                model: skinModel,
                sha1: validation.sha1,
                skinBase64: validation.buffer.toString('base64'),
                updatedAt: Date.now()
            };
            const TTL_30_DAYS = 30 * 24 * 3600;
            await store.setSkin(skinRecord, TTL_30_DAYS);
            await store.setSkinByHash(validation.sha1, validation.buffer, TTL_30_DAYS);
            sendJson(res, 200, {
                success: true,
                nickname,
                model: skinModel,
                sha1: validation.sha1,
                ownerToken: token
            });
            return;
        }
        // 8. GET /csl/{username}.json
        const cslMatch = pathname.match(/^\/csl\/([^/]+?)(?:\.json)?$/);
        if (req.method === 'GET' && cslMatch && !pathname.startsWith('/csl/raw/') && !pathname.startsWith('/csl/textures/')) {
            const username = cslMatch[1].trim();
            const skinRecord = await store.getSkin(username.toLowerCase());
            if (!skinRecord) {
                sendJson(res, 404, { error: 'Skin not found' });
                return;
            }
            const isSlim = skinRecord.model === 'slim';
            const textureHash = skinRecord.sha1;
            sendJson(res, 200, {
                username: skinRecord.nickname,
                skins: {
                    default: isSlim ? null : textureHash,
                    slim: isSlim ? textureHash : null
                },
                cape: null
            });
            return;
        }
        // 9. GET /csl/raw/{sha1}.png or /csl/textures/{sha1}
        const rawMatch = pathname.match(/^\/csl\/(?:raw|textures)\/([a-fA-F0-9]+?)(?:\.png)?$/);
        if (req.method === 'GET' && rawMatch) {
            const sha1 = rawMatch[1].toLowerCase();
            const pngBuffer = await store.getSkinByHash(sha1);
            if (!pngBuffer) {
                sendJson(res, 404, { error: 'Texture not found' });
                return;
            }
            res.writeHead(200, {
                'Content-Type': 'image/png',
                'Cache-Control': 'public, max-age=31536000, immutable',
                'Access-Control-Allow-Origin': '*'
            });
            res.end(pngBuffer);
            return;
        }
        sendJson(res, 404, { error: 'Not Found' });
    }
    catch (err) {
        sendJson(res, 500, { error: err.message || 'Internal Server Error' });
    }
});
export default server;
const PORT = process.env.PORT ? parseInt(process.env.PORT, 10) : 3000;
if (process.env.NODE_ENV !== 'test' && !process.env.VERCEL) {
    server.listen(PORT, '0.0.0.0', () => {
        console.log(`Lobby API listening on http://127.0.0.1:${PORT}`);
    });
}

import http from 'http';
import crypto from 'crypto';
import url from 'url';
import { getStore, computeLobbyModSync, computeModManifestHash } from './store.js';
import { validatePngSkin, isValidNickname, getClientIp } from './skinUtils.js';
import { handleRegister, handleSync, handleFriendRequest, handleFriendRespond, handleFriendRemove, handleInvite, handleInviteRespond, handleUserNick } from './friendRoutes.js';
import handleReport from '../api/report.js';
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
        if (pathname === '/api/user/nick') {
            return await handleUserNick(req, res);
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
        if (pathname === '/api/report') {
            return await handleReport(req, res);
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
            if (Array.isArray(body.manifest)) {
                const cleanManifest = body.manifest.slice(0, 400).map((m) => ({
                    id: String(m.id || '').slice(0, 100),
                    name: String(m.name || m.id || '').slice(0, 100),
                    version: String(m.version || '').slice(0, 50),
                    enabled: Boolean(m.enabled)
                }));
                lobby.manifests = { [hostName]: cleanManifest };
                lobby.manifestHashes = { [hostName]: computeModManifestHash(cleanManifest) };
            }
            await store.set(lobby, 1800);
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
            if (lobby.kickedPlayers && lobby.kickedPlayers.some(p => p.toLowerCase() === playerName.toLowerCase())) {
                sendJson(res, 403, { error: 'Вы были исключены из этого лобби' });
                return;
            }
            if (!lobby.players.includes(playerName)) {
                lobby.players.push(playerName);
            }
            if (Array.isArray(body.manifest)) {
                lobby.manifests = lobby.manifests || {};
                lobby.manifestHashes = lobby.manifestHashes || {};
                const cleanManifest = body.manifest.slice(0, 400).map((m) => ({
                    id: String(m.id || '').slice(0, 100),
                    name: String(m.name || m.id || '').slice(0, 100),
                    version: String(m.version || '').slice(0, 50),
                    enabled: Boolean(m.enabled)
                }));
                lobby.manifests[playerName] = cleanManifest;
                lobby.manifestHashes[playerName] = computeModManifestHash(cleanManifest);
            }
            await store.set(lobby, 1800);
            sendJson(res, 200, {
                success: true,
                code: lobby.code,
                status: lobby.status,
                tunnelAddress: lobby.tunnelAddress,
                playerCount: lobby.players.length
            });
            return;
        }
        // 2b. POST /api/lobby/leave
        if (req.method === 'POST' && (pathname === '/api/lobby/leave' || pathname === '/api/leave' || pathname === '/leave')) {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const playerName = (body.playerName || '').trim();
            if (!code || !playerName) {
                sendJson(res, 400, { error: 'code and playerName required' });
                return;
            }
            const lobby = await store.get(code);
            if (!lobby || lobby.status === 'closed') {
                sendJson(res, 200, { success: true, message: 'Lobby closed or not found' });
                return;
            }
            lobby.players = lobby.players.filter(p => p.toLowerCase() !== playerName.toLowerCase());
            await store.set(lobby, 1800);
            sendJson(res, 200, {
                success: true,
                code: lobby.code,
                players: lobby.players,
                playerCount: lobby.players.length
            });
            return;
        }
        // 2bb. POST /api/lobby/kick
        if (req.method === 'POST' && (pathname === '/api/lobby/kick' || pathname === '/api/kick' || pathname === '/kick')) {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const hostToken = (body.hostToken || '').trim();
            const targetPlayer = (body.player || body.playerName || body.targetPlayer || '').trim();
            if (!code || !hostToken || !targetPlayer) {
                sendJson(res, 400, { error: 'code, hostToken and player required' });
                return;
            }
            const lobby = await store.get(code);
            if (!lobby || lobby.status === 'closed') {
                sendJson(res, 404, { error: 'Lobby not found or closed' });
                return;
            }
            if (lobby.hostToken !== hostToken) {
                sendJson(res, 403, { error: 'Unauthorized: invalid host token' });
                return;
            }
            if (targetPlayer.toLowerCase() === lobby.hostName.toLowerCase()) {
                sendJson(res, 400, { error: 'Cannot kick host from their own lobby' });
                return;
            }
            lobby.players = lobby.players.filter(p => p.toLowerCase() !== targetPlayer.toLowerCase());
            if (lobby.manifests)
                delete lobby.manifests[targetPlayer];
            if (lobby.manifestHashes)
                delete lobby.manifestHashes[targetPlayer];
            if (lobby.playerHeartbeats)
                delete lobby.playerHeartbeats[targetPlayer.toLowerCase()];
            lobby.kickedPlayers = lobby.kickedPlayers || [];
            if (!lobby.kickedPlayers.some(p => p.toLowerCase() === targetPlayer.toLowerCase())) {
                lobby.kickedPlayers.push(targetPlayer);
            }
            await store.set(lobby, 1800);
            sendJson(res, 200, {
                success: true,
                code: lobby.code,
                players: lobby.players,
                playerCount: lobby.players.length,
                kicked: targetPlayer
            });
            return;
        }
        // 2c. POST /api/lobby/manifest (обновление манифеста модов)
        if (req.method === 'POST' && (pathname === '/api/lobby/manifest' || pathname === '/api/manifest')) {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const playerName = (body.playerName || '').trim();
            if (!code || !playerName) {
                sendJson(res, 400, { error: 'code and playerName required' });
                return;
            }
            const lobby = await store.get(code);
            if (!lobby || lobby.status === 'closed') {
                sendJson(res, 404, { error: 'Lobby not found or closed' });
                return;
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
            sendJson(res, 200, {
                success: true,
                hash,
                modSync
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
            const playerName = (parsedUrl.query.player || '').trim();
            // Проверка на исключение игрока
            if (playerName && lobby.kickedPlayers && lobby.kickedPlayers.some(p => p.toLowerCase() === playerName.toLowerCase())) {
                sendJson(res, 200, {
                    code: lobby.code,
                    status: 'closed',
                    kicked: true,
                    playerCount: lobby.players.length,
                    players: lobby.players,
                    hostName: lobby.hostName
                });
                return;
            }
            // Обновление пульса
            const now = Date.now();
            lobby.playerHeartbeats = lobby.playerHeartbeats || {};
            if (playerName) {
                lobby.playerHeartbeats[playerName.toLowerCase()] = now;
            }
            // Авто-очистка неактивных гостей (> 25 сек)
            const hostLower = lobby.hostName.toLowerCase();
            let playersChanged = false;
            const activePlayers = lobby.players.filter(p => {
                const pLower = p.toLowerCase();
                if (pLower === hostLower)
                    return true;
                const lastBeat = lobby.playerHeartbeats[pLower] || lobby.createdAt;
                if (now - lastBeat > 25000) {
                    playersChanged = true;
                    delete lobby.playerHeartbeats[pLower];
                    if (lobby.manifests)
                        delete lobby.manifests[p];
                    if (lobby.manifestHashes)
                        delete lobby.manifestHashes[p];
                    return false;
                }
                return true;
            });
            if (playersChanged) {
                lobby.players = activePlayers;
                await store.set(lobby, 1800);
            }
            else if (playerName) {
                await store.set(lobby, 1800);
            }
            const modSync = computeLobbyModSync(lobby);
            sendJson(res, 200, {
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
            return;
        }
        // 4. POST /api/lobby/open
        if (req.method === 'POST' && (pathname === '/api/lobby/open' || pathname === '/api/open' || pathname === '/open')) {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const hostToken = body.hostToken;
            const tunnelAddress = body.tunnelAddress;
            if (!code) {
                sendJson(res, 400, { error: 'code is required' });
                return;
            }
            if (!hostToken) {
                sendJson(res, 400, { error: 'hostToken is required' });
                return;
            }
            if (!tunnelAddress) {
                sendJson(res, 400, { error: 'tunnelAddress is required' });
                return;
            }
            let lobby = await store.get(code);
            if (lobby) {
                if (lobby.hostToken !== hostToken) {
                    sendJson(res, 403, { error: 'Unauthorized: invalid host token' });
                    return;
                }
                lobby.status = 'open';
                lobby.tunnelAddress = tunnelAddress;
                lobby.lastHeartbeat = Date.now();
            }
            else {
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
            await store.set(lobby, 7200);
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
            await store.set(lobby, 1800);
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
            // Authenticate user via X-User-Id / X-User-Token if provided
            const userId = (req.headers['x-user-id'] || req.headers['X-User-Id']);
            const userToken = (req.headers['x-user-token'] || req.headers['X-User-Token']);
            let authedUser = null;
            if (userId && userToken) {
                const user = await store.getUser(userId.trim());
                if (!user) {
                    sendJson(res, 401, { error: 'Unauthorized: user not found' });
                    return;
                }
                const tokenHash = crypto.createHash('sha256').update(userToken.trim()).digest('hex');
                if (user.tokenHash !== tokenHash) {
                    sendJson(res, 401, { error: 'Unauthorized: invalid token' });
                    return;
                }
                authedUser = user;
            }
            const nickLower = nickname.toLowerCase();
            // Check if nickname belongs to another user
            const existingNickUser = await store.getUserByNick(nickLower);
            if (existingNickUser) {
                if (!authedUser || existingNickUser.id !== authedUser.id) {
                    sendJson(res, 409, { error: 'Этот ник уже занят другим игроком, выбери другой' });
                    return;
                }
            }
            const existing = await store.getSkin(nickLower);
            if (existing) {
                if (authedUser) {
                    if (existing.userId && existing.userId !== authedUser.id) {
                        sendJson(res, 409, { error: 'Этот ник уже занят другим игроком, выбери другой' });
                        return;
                    }
                }
                else if (existing.ownerToken && existing.ownerToken !== ownerToken) {
                    sendJson(res, 409, { error: 'Этот ник уже занят другим игроком, выбери другой' });
                    return;
                }
            }
            const skinModel = model === 'slim' ? 'slim' : 'default';
            const validation = validatePngSkin(skinBase64);
            if (!validation.valid || !validation.buffer || !validation.sha1) {
                sendJson(res, 400, { error: validation.error || 'Invalid skin PNG' });
                return;
            }
            let token = ownerToken || (authedUser ? authedUser.id : crypto.randomUUID());
            const skinRecord = {
                nickname,
                userId: authedUser ? authedUser.id : existing?.userId,
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
        // 8. GET/HEAD /csl/raw/{sha1}.png or /csl/textures/{sha1} or /textures/{sha1}
        const rawMatch = pathname.match(/^(?:\/(?:api\/)?csl)?\/+(?:raw|textures)\/+([a-fA-F0-9]+?)(?:\.png)?$/);
        if ((req.method === 'GET' || req.method === 'HEAD') && rawMatch) {
            const sha1 = rawMatch[1].toLowerCase();
            const pngBuffer = await store.getSkinByHash(sha1);
            if (!pngBuffer) {
                sendJson(res, 404, { error: 'Texture not found' });
                return;
            }
            res.writeHead(200, {
                'Content-Type': 'image/png',
                'Content-Length': pngBuffer.length,
                'Cache-Control': 'public, max-age=31536000, immutable',
                'Access-Control-Allow-Origin': '*'
            });
            if (req.method === 'HEAD') {
                res.end();
            }
            else {
                res.end(pngBuffer);
            }
            return;
        }
        // 9. GET/HEAD /csl/{username}.json
        const cslMatch = pathname.match(/^(?:\/api)?\/csl\/+([^/]+?)(?:\.json)?$/);
        if ((req.method === 'GET' || req.method === 'HEAD') && cslMatch && !pathname.includes('/textures/') && !pathname.includes('/raw/')) {
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

import http from 'http';
import crypto from 'crypto';
import url from 'url';
const lobbies = new Map();
function generateLobbyCode() {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    let code = '';
    for (let i = 0; i < 6; i++) {
        code += chars.charAt(Math.floor(Math.random() * chars.length));
    }
    return code;
}
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
        'Access-Control-Allow-Headers': 'Content-Type, Authorization'
    });
    res.end(json);
}
// Cleanup inactive lobbies older than 30 minutes
setInterval(() => {
    const now = Date.now();
    for (const [code, lobby] of lobbies.entries()) {
        if (now - lobby.lastHeartbeat > 30 * 60 * 1000) {
            lobbies.delete(code);
        }
    }
}, 60 * 1000);
export const server = http.createServer(async (req, res) => {
    if (req.method === 'OPTIONS') {
        res.writeHead(204, {
            'Access-Control-Allow-Origin': '*',
            'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
            'Access-Control-Allow-Headers': 'Content-Type, Authorization'
        });
        res.end();
        return;
    }
    const parsedUrl = url.parse(req.url || '', true);
    const pathname = parsedUrl.pathname || '';
    console.log(`[LOBBY-API] ${req.method} ${pathname}`);
    try {
        // 1. POST /api/lobby
        if (req.method === 'POST' && pathname === '/api/lobby') {
            const body = await parseBody(req);
            let code = generateLobbyCode();
            while (lobbies.has(code)) {
                code = generateLobbyCode();
            }
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
            lobbies.set(code, lobby);
            sendJson(res, 201, {
                code,
                hostToken,
                status: lobby.status,
                createdAt: lobby.createdAt
            });
            return;
        }
        // 2. POST /api/lobby/join
        if (req.method === 'POST' && pathname === '/api/lobby/join') {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const playerName = body.playerName || 'Guest';
            const lobby = lobbies.get(code);
            if (!lobby || lobby.status === 'closed') {
                sendJson(res, 404, { error: 'Lobby not found or closed' });
                return;
            }
            if (!lobby.players.includes(playerName)) {
                lobby.players.push(playerName);
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
        if (req.method === 'GET' && pathname === '/api/lobby/status') {
            const code = (parsedUrl.query.code || '').toUpperCase().trim();
            const lobby = lobbies.get(code);
            if (!lobby) {
                sendJson(res, 404, { error: 'Lobby not found' });
                return;
            }
            sendJson(res, 200, {
                code: lobby.code,
                status: lobby.status,
                tunnelAddress: lobby.tunnelAddress,
                playerCount: lobby.players.length,
                lastHeartbeat: lobby.lastHeartbeat
            });
            return;
        }
        // 4. POST /api/lobby/open
        if (req.method === 'POST' && pathname === '/api/lobby/open') {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const hostToken = body.hostToken;
            const tunnelAddress = body.tunnelAddress;
            const lobby = lobbies.get(code);
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
            sendJson(res, 200, {
                success: true,
                code: lobby.code,
                status: lobby.status,
                tunnelAddress: lobby.tunnelAddress
            });
            return;
        }
        // 5. POST /api/lobby/heartbeat
        if (req.method === 'POST' && pathname === '/api/lobby/heartbeat') {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const hostToken = body.hostToken;
            const lobby = lobbies.get(code);
            if (!lobby) {
                sendJson(res, 404, { error: 'Lobby not found' });
                return;
            }
            if (lobby.hostToken !== hostToken) {
                sendJson(res, 403, { error: 'Unauthorized: invalid host token' });
                return;
            }
            lobby.lastHeartbeat = Date.now();
            sendJson(res, 200, {
                success: true,
                code: lobby.code,
                lastHeartbeat: lobby.lastHeartbeat
            });
            return;
        }
        // 6. POST /api/lobby/close
        if (req.method === 'POST' && pathname === '/api/lobby/close') {
            const body = await parseBody(req);
            const code = (body.code || '').toUpperCase().trim();
            const hostToken = body.hostToken;
            const lobby = lobbies.get(code);
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
            sendJson(res, 200, {
                success: true,
                code: lobby.code,
                status: 'closed'
            });
            return;
        }
        sendJson(res, 404, { error: 'Not Found' });
    }
    catch (err) {
        sendJson(res, 500, { error: err.message || 'Internal Server Error' });
    }
});
const PORT = process.env.PORT ? parseInt(process.env.PORT, 10) : 3000;
if (process.env.NODE_ENV !== 'test') {
    server.listen(PORT, '0.0.0.0', () => {
        console.log(`Lobby API listening on http://127.0.0.1:${PORT}`);
    });
}

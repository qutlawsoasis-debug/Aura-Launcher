import crypto from 'crypto';
import { getStore } from './store.js';
export function parseJson(req) {
    if (req.body && typeof req.body === 'object') {
        return Promise.resolve(req.body);
    }
    return new Promise((resolve, reject) => {
        let body = '';
        req.on('data', (chunk) => {
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
export function sendJson(res, statusCode, data) {
    if (typeof res.status === 'function' && typeof res.json === 'function') {
        res.setHeader('Access-Control-Allow-Origin', '*');
        res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
        res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Aura-Client, X-User-Id, X-User-Token');
        return res.status(statusCode).json(data);
    }
    const json = JSON.stringify(data);
    res.writeHead(statusCode, {
        'Content-Type': 'application/json; charset=utf-8',
        'Access-Control-Allow-Origin': '*',
        'Access-Control-Allow-Methods': 'GET, POST, OPTIONS',
        'Access-Control-Allow-Headers': 'Content-Type, Authorization, X-Aura-Client, X-User-Id, X-User-Token'
    });
    res.end(json);
}
export function getClientIp(req) {
    const forwarded = req.headers['x-forwarded-for'];
    if (forwarded) {
        const list = Array.isArray(forwarded) ? forwarded[0] : forwarded;
        const ip = list.split(',')[0].trim();
        if (ip)
            return ip;
    }
    const realIp = req.headers['x-real-ip'];
    if (realIp) {
        return Array.isArray(realIp) ? realIp[0] : realIp;
    }
    return req.socket?.remoteAddress || '127.0.0.1';
}
export function getHeader(req, headerName) {
    const val = req.headers[headerName.toLowerCase()];
    if (!val)
        return undefined;
    if (Array.isArray(val))
        return val[0]?.trim();
    return typeof val === 'string' ? val.trim() : undefined;
}
export async function authenticate(req, res, store) {
    const userId = getHeader(req, 'x-user-id');
    const userToken = getHeader(req, 'x-user-token');
    if (!userId || !userToken) {
        sendJson(res, 401, { error: 'Unauthorized: missing X-User-Id or X-User-Token' });
        return null;
    }
    const user = await store.getUser(userId);
    if (!user) {
        sendJson(res, 401, { error: 'Unauthorized: user not found' });
        return null;
    }
    const tokenHash = crypto.createHash('sha256').update(userToken).digest('hex');
    if (user.tokenHash !== tokenHash) {
        sendJson(res, 401, { error: 'Unauthorized: invalid token' });
        return null;
    }
    return user;
}
// 1. POST /api/user/register
export async function handleRegister(req, res) {
    if (req.method !== 'POST')
        return sendJson(res, 405, { error: 'Method Not Allowed' });
    const store = getStore();
    const ip = getClientIp(req);
    const allowed = await store.checkRateLimit(`reg:${ip}`, 5, 3600);
    if (!allowed) {
        return sendJson(res, 429, { error: 'Слишком много регистраций. Попробуйте позже.' });
    }
    const body = await parseJson(req);
    const nick = typeof body.nick === 'string' ? body.nick.trim() : '';
    if (!nick || nick.length < 2 || nick.length > 24) {
        return sendJson(res, 400, { error: 'Некорректный никнейм (2-24 символа)' });
    }
    const userId = crypto.randomUUID();
    const userToken = crypto.randomBytes(32).toString('hex'); // 32 байта hex
    const tokenHash = crypto.createHash('sha256').update(userToken).digest('hex');
    const friendCode = await store.generateFriendCode();
    const user = {
        id: userId,
        nick,
        tokenHash,
        friendCode,
        createdAt: Date.now()
    };
    await store.createUser(user);
    sendJson(res, 200, {
        userId,
        userToken,
        friendCode
    });
}
// 2. POST /api/sync
export async function handleSync(req, res) {
    if (req.method !== 'POST')
        return sendJson(res, 405, { error: 'Method Not Allowed' });
    const store = getStore();
    const user = await authenticate(req, res, store);
    if (!user)
        return;
    const body = await parseJson(req);
    const nick = (typeof body.nick === 'string' && body.nick.trim()) ? body.nick.trim() : user.nick;
    if (nick && nick !== user.nick && /^[A-Za-z0-9_]{2,24}$/.test(nick)) {
        try {
            await store.updateUserNick(user.id, nick);
            user.nick = nick;
        }
        catch { }
    }
    const statusRaw = body.status;
    const status = (statusRaw === 'lobby' || statusRaw === 'playing' || statusRaw === 'offline') ? statusRaw : 'online';
    const lobbyCode = typeof body.lobbyCode === 'string' && body.lobbyCode.trim()
        ? body.lobbyCode.trim().toUpperCase()
        : undefined;
    const presence = {
        userId: user.id,
        nick,
        status,
        lobbyCode,
        lastSeen: Date.now()
    };
    const syncResult = await store.sync(user.id, presence);
    sendJson(res, 200, syncResult);
}
// 3. POST /api/friends/request
export async function handleFriendRequest(req, res) {
    if (req.method !== 'POST')
        return sendJson(res, 405, { error: 'Method Not Allowed' });
    const store = getStore();
    const user = await authenticate(req, res, store);
    if (!user)
        return;
    const allowed = await store.checkRateLimit(`friend_req:${user.id}`, 20, 3600);
    if (!allowed) {
        return sendJson(res, 429, { error: 'Лимит заявок превышен (максимум 20 в час)' });
    }
    const body = await parseJson(req);
    const friendCode = typeof body.friendCode === 'string' ? body.friendCode.trim().toUpperCase() : '';
    if (!friendCode) {
        return sendJson(res, 400, { error: 'Код не найден' });
    }
    const targetUser = await store.getUserByFriendCode(friendCode);
    if (!targetUser) {
        return sendJson(res, 404, { error: 'Код не найден' });
    }
    if (targetUser.id === user.id) {
        return sendJson(res, 400, { error: 'Это ваш код' });
    }
    const alreadyFriends = await store.isFriend(user.id, targetUser.id);
    if (alreadyFriends) {
        return sendJson(res, 400, { error: 'Уже в друзьях' });
    }
    const outgoingExists = await store.hasFriendRequest(user.id, targetUser.id);
    const incomingExists = await store.hasFriendRequest(targetUser.id, user.id);
    if (outgoingExists || incomingExists) {
        return sendJson(res, 400, { error: 'Заявка уже отправлена' });
    }
    await store.addFriendRequest(user.id, targetUser.id);
    sendJson(res, 200, { ok: true });
}
// 4. POST /api/friends/respond
export async function handleFriendRespond(req, res) {
    if (req.method !== 'POST')
        return sendJson(res, 405, { error: 'Method Not Allowed' });
    const store = getStore();
    const user = await authenticate(req, res, store);
    if (!user)
        return;
    const body = await parseJson(req);
    const fromId = typeof body.fromId === 'string' ? body.fromId.trim() : '';
    const accept = Boolean(body.accept);
    if (!fromId) {
        return sendJson(res, 400, { error: 'fromId обязателен' });
    }
    const hasReq = await store.hasFriendRequest(fromId, user.id);
    if (!hasReq) {
        return sendJson(res, 404, { error: 'Заявка не найдена' });
    }
    if (accept) {
        const myCount = await store.getFriendCount(user.id);
        if (myCount >= 100) {
            return sendJson(res, 400, { error: 'Превышен лимит друзей (максимум 100)' });
        }
        const theirCount = await store.getFriendCount(fromId);
        if (theirCount >= 100) {
            return sendJson(res, 400, { error: 'У пользователя превышен лимит друзей' });
        }
        await store.removeFriendRequest(fromId, user.id);
        await store.addFriend(user.id, fromId);
        return sendJson(res, 200, { ok: true });
    }
    else {
        await store.removeFriendRequest(fromId, user.id);
        return sendJson(res, 200, { ok: true });
    }
}
// 5. POST /api/friends/remove
export async function handleFriendRemove(req, res) {
    if (req.method !== 'POST')
        return sendJson(res, 405, { error: 'Method Not Allowed' });
    const store = getStore();
    const user = await authenticate(req, res, store);
    if (!user)
        return;
    const body = await parseJson(req);
    const friendId = typeof body.friendId === 'string' ? body.friendId.trim() : '';
    if (!friendId) {
        return sendJson(res, 400, { error: 'friendId обязателен' });
    }
    await store.removeFriend(user.id, friendId);
    sendJson(res, 200, { ok: true });
}
// 6. POST /api/invite
export async function handleInvite(req, res) {
    if (req.method !== 'POST')
        return sendJson(res, 405, { error: 'Method Not Allowed' });
    const store = getStore();
    const user = await authenticate(req, res, store);
    if (!user)
        return;
    const allowed = await store.checkRateLimit(`invite:${user.id}`, 10, 60);
    if (!allowed) {
        return sendJson(res, 429, { error: 'Лимит приглашений превышен (максимум 10 в минуту)' });
    }
    const body = await parseJson(req);
    const friendId = typeof body.friendId === 'string' ? body.friendId.trim() : '';
    const lobbyCode = typeof body.lobbyCode === 'string' ? body.lobbyCode.trim().toUpperCase() : '';
    const hostToken = typeof body.hostToken === 'string' ? body.hostToken.trim() : '';
    if (!friendId || !lobbyCode || !hostToken) {
        return sendJson(res, 400, { error: 'Не все поля заполнены (friendId, lobbyCode, hostToken)' });
    }
    // 1. Проверить, что друг в друзьях (иначе 403)
    const isFriend = await store.isFriend(user.id, friendId);
    if (!isFriend) {
        return sendJson(res, 403, { error: 'Пользователь не в списке друзей' });
    }
    // 2. Проверить, что лобби существует
    const lobby = await store.get(lobbyCode);
    if (!lobby || lobby.status === 'closed') {
        return sendJson(res, 404, { error: 'Лобби не найдено' });
    }
    // 3. hostToken совпадает с токеном хоста
    if (lobby.hostToken !== hostToken) {
        return sendJson(res, 403, { error: 'Неверный токен хоста' });
    }
    // 4. Приглашение живёт 120 с
    const inviteId = crypto.randomUUID();
    const now = Date.now();
    const invite = {
        inviteId,
        fromId: user.id,
        fromNick: user.nick,
        toId: friendId,
        lobbyCode,
        ts: now,
        expiresAt: now + 120_000
    };
    await store.createInvite(invite);
    const sentInvite = {
        inviteId,
        friendId,
        state: 'pending',
        lobbyCode,
        createdAt: now,
        expiresAt: now + 180_000 // 120s + 60s
    };
    await store.createSentInvite(user.id, sentInvite);
    sendJson(res, 200, { inviteId });
}
// 7. POST /api/invite/respond
export async function handleInviteRespond(req, res) {
    if (req.method !== 'POST')
        return sendJson(res, 405, { error: 'Method Not Allowed' });
    const store = getStore();
    const user = await authenticate(req, res, store);
    if (!user)
        return;
    const body = await parseJson(req);
    const inviteId = typeof body.inviteId === 'string' ? body.inviteId.trim() : '';
    const accept = Boolean(body.accept);
    if (!inviteId) {
        return sendJson(res, 400, { error: 'inviteId обязателен' });
    }
    const invite = await store.getInvite(user.id, inviteId);
    if (!invite || Date.now() > invite.expiresAt) {
        return sendJson(res, 404, { error: 'Приглашение не найдено или истекло' });
    }
    await store.removeInvite(user.id, inviteId);
    await store.updateSentInvite(invite.fromId, inviteId, accept ? 'accepted' : 'declined');
    if (accept) {
        return sendJson(res, 200, { lobbyCode: invite.lobbyCode });
    }
    else {
        return sendJson(res, 200, { ok: true });
    }
}
// 8. POST /api/user/nick (Task 39: Nickname change with skin re-binding and conflict check)
export async function handleUserNick(req, res) {
    if (req.method !== 'POST')
        return sendJson(res, 405, { error: 'Method Not Allowed' });
    const store = getStore();
    const user = await authenticate(req, res, store);
    if (!user)
        return;
    const body = await parseJson(req);
    const newNick = typeof body.nick === 'string' ? body.nick.trim() : '';
    // Validate ^[A-Za-z0-9_]{3,16}$
    const nickRegex = /^[A-Za-z0-9_]{3,16}$/;
    if (!nickRegex.test(newNick)) {
        return sendJson(res, 400, { error: 'Ник: от 3 до 16 символов, латиница, цифры и _.' });
    }
    const oldNick = user.nick;
    const oldNickLower = oldNick.toLowerCase();
    const newNickLower = newNick.toLowerCase();
    // If nick hasn't changed at all
    if (oldNickLower === newNickLower) {
        if (oldNick !== newNick) {
            await store.updateUserNick(user.id, newNick);
        }
        return sendJson(res, 200, { success: true, nick: newNick });
    }
    // Check if newNick is already taken by another registered user
    const existingUser = await store.getUserByNick(newNickLower);
    if (existingUser && existingUser.id !== user.id) {
        return sendJson(res, 409, { error: 'Этот ник уже занят другим игроком, выбери другой' });
    }
    // Check if newNick skin belongs to another user
    const existingSkin = await store.getSkin(newNickLower);
    if (existingSkin && existingSkin.userId && existingSkin.userId !== user.id) {
        return sendJson(res, 409, { error: 'Этот ник уже занят другим игроком, выбери другой' });
    }
    // Migrate skin if user has one under oldNick
    const oldSkin = await store.getSkin(oldNickLower);
    if (oldSkin) {
        // Rebind skin to newNick
        const newSkin = {
            ...oldSkin,
            nickname: newNick,
            userId: user.id,
            updatedAt: Date.now()
        };
        await store.setSkin(newSkin);
        await store.deleteSkin(oldNickLower);
    }
    // Update user's nickname in store
    await store.updateUserNick(user.id, newNick);
    sendJson(res, 200, { success: true, nick: newNick });
}

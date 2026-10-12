import crypto from 'crypto';
import {
  getStore,
  UserRecord,
  LobbyStore,
  InviteRecord,
  SentInviteRecord,
  UserPresence,
  UserProfileSnapshot,
  UserAchievementItem,
  UserWorldItem,
  UserPrivacySettings
} from './store.js';

export function parseJson(req: any): Promise<any> {
  if (req.body && typeof req.body === 'object') {
    return Promise.resolve(req.body);
  }
  return new Promise((resolve, reject) => {
    let body = '';
    req.on('data', (chunk: any) => {
      body += chunk;
      if (body.length > 1e6) {
        req.destroy();
        reject(new Error('Payload too large'));
      }
    });
    req.on('end', () => {
      if (!body) return resolve({});
      try {
        resolve(JSON.parse(body));
      } catch (err) {
        reject(err);
      }
    });
    req.on('error', reject);
  });
}

export function sendJson(res: any, statusCode: number, data: any) {
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

export function getClientIp(req: any): string {
  const forwarded = req.headers['x-forwarded-for'];
  if (forwarded) {
    const list = Array.isArray(forwarded) ? forwarded[0] : forwarded;
    const ip = list.split(',')[0].trim();
    if (ip) return ip;
  }
  const realIp = req.headers['x-real-ip'];
  if (realIp) {
    return Array.isArray(realIp) ? realIp[0] : realIp;
  }
  return req.socket?.remoteAddress || '127.0.0.1';
}

export function getHeader(req: any, headerName: string): string | undefined {
  const val = req.headers[headerName.toLowerCase()];
  if (!val) return undefined;
  if (Array.isArray(val)) return val[0]?.trim();
  return typeof val === 'string' ? val.trim() : undefined;
}

export async function authenticate(req: any, res: any, store: LobbyStore): Promise<UserRecord | null> {
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
export async function handleRegister(req: any, res: any) {
  if (req.method !== 'POST') return sendJson(res, 405, { error: 'Method Not Allowed' });

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

  const user: UserRecord = {
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
export async function handleSync(req: any, res: any) {
  if (req.method !== 'POST') return sendJson(res, 405, { error: 'Method Not Allowed' });

  const store = getStore();
  const user = await authenticate(req, res, store);
  if (!user) return;

  const body = await parseJson(req);
  const nick = (typeof body.nick === 'string' && body.nick.trim()) ? body.nick.trim() : user.nick;
  if (nick && nick !== user.nick && /^[A-Za-z0-9_]{2,24}$/.test(nick)) {
    try {
      await store.updateUserNick(user.id, nick);
      user.nick = nick;
    } catch { }
  }
  const statusRaw = body.status;
  const status: 'online' | 'lobby' | 'playing' | 'offline' =
    (statusRaw === 'lobby' || statusRaw === 'playing' || statusRaw === 'offline') ? statusRaw : 'online';
  const lobbyCode = typeof body.lobbyCode === 'string' && body.lobbyCode.trim()
    ? body.lobbyCode.trim().toUpperCase()
    : undefined;

  const presence: UserPresence = {
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
export async function handleFriendRequest(req: any, res: any) {
  if (req.method !== 'POST') return sendJson(res, 405, { error: 'Method Not Allowed' });

  const store = getStore();
  const user = await authenticate(req, res, store);
  if (!user) return;

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
export async function handleFriendRespond(req: any, res: any) {
  if (req.method !== 'POST') return sendJson(res, 405, { error: 'Method Not Allowed' });

  const store = getStore();
  const user = await authenticate(req, res, store);
  if (!user) return;

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
  } else {
    await store.removeFriendRequest(fromId, user.id);
    return sendJson(res, 200, { ok: true });
  }
}

// 5. POST /api/friends/remove
export async function handleFriendRemove(req: any, res: any) {
  if (req.method !== 'POST') return sendJson(res, 405, { error: 'Method Not Allowed' });

  const store = getStore();
  const user = await authenticate(req, res, store);
  if (!user) return;

  const body = await parseJson(req);
  const friendId = typeof body.friendId === 'string' ? body.friendId.trim() : '';
  if (!friendId) {
    return sendJson(res, 400, { error: 'friendId обязателен' });
  }

  await store.removeFriend(user.id, friendId);
  sendJson(res, 200, { ok: true });
}

// 6. POST /api/invite
export async function handleInvite(req: any, res: any) {
  if (req.method !== 'POST') return sendJson(res, 405, { error: 'Method Not Allowed' });

  const store = getStore();
  const user = await authenticate(req, res, store);
  if (!user) return;

  const allowed = await store.checkRateLimit(`invite:${user.id}`, 10, 60);
  if (!allowed) {
    return sendJson(res, 429, { error: 'Лимит приглашений превышен (максимум 10 в минуту)' });
  }

  const body = await parseJson(req);
  const friendId = typeof body.friendId === 'string' ? body.friendId.trim() : '';
  const lobbyCode = typeof body.lobbyCode === 'string' ? body.lobbyCode.trim().toUpperCase() : '';
  const hostToken = typeof body.hostToken === 'string' ? body.hostToken.trim() : '';

  if (!friendId || !lobbyCode) {
    return sendJson(res, 400, { error: 'Не все поля заполнены (friendId, lobbyCode)' });
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

  // 3. Проверить полномочия: отправитель должен быть либо хостом (с валидным hostToken),
  // либо участником лобби (user.nick есть в lobby.players)
  const isHost = Boolean(hostToken && lobby.hostToken === hostToken);
  const isMember = Boolean(user.nick && lobby.players && lobby.players.some((p: string) => p.toLowerCase() === user.nick.toLowerCase()));
  if (!isHost && !isMember) {
    return sendJson(res, 403, { error: 'Вы должны быть хостом или участником лобби, чтобы приглашать игроков' });
  }

  // 4. Приглашение живёт 120 с
  const inviteId = crypto.randomUUID();
  const now = Date.now();
  const invite: InviteRecord = {
    inviteId,
    fromId: user.id,
    fromNick: user.nick,
    toId: friendId,
    lobbyCode,
    ts: now,
    expiresAt: now + 120_000
  };

  await store.createInvite(invite);

  const sentInvite: SentInviteRecord = {
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
export async function handleInviteRespond(req: any, res: any) {
  if (req.method !== 'POST') return sendJson(res, 405, { error: 'Method Not Allowed' });

  const store = getStore();
  const user = await authenticate(req, res, store);
  if (!user) return;

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
  } else {
    return sendJson(res, 200, { ok: true });
  }
}

// 8. POST /api/user/nick (Task 39: Nickname change with skin re-binding and conflict check)
export async function handleUserNick(req: any, res: any) {
  if (req.method !== 'POST') return sendJson(res, 405, { error: 'Method Not Allowed' });

  const store = getStore();
  const user = await authenticate(req, res, store);
  if (!user) return;

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
    const newSkin: any = {
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

// 9. Profile Snapshot: POST /api/user/profile (Save own profile snapshot)
export async function handleSetProfile(req: any, res: any) {
  try {
    if (req.method !== 'POST') return sendJson(res, 405, { error: 'Method Not Allowed' });

    const store = getStore();
    const user = await authenticate(req, res, store);
    if (!user) return;

    const allowed = await store.checkRateLimit(`profile_set:${user.id}`, 30, 60);
    if (!allowed) {
      return sendJson(res, 429, { error: 'Слишком много запросов на обновление профиля (максимум 30 в минуту)' });
    }

    const body = await parseJson(req);

    // Validate playTimeSeconds (>= 0, <= 100_000_000)
    const playTimeSeconds = Math.max(0, Math.min(100_000_000, Math.floor(Number(body.playTimeSeconds) || 0)));
    const gameLaunches = Math.max(0, Math.min(10_000_000, Math.floor(Number(body.gameLaunches) || 0)));

    // Skin info
    let skinModel: 'default' | 'slim' = body.skinModel === 'slim' ? 'slim' : 'default';
    let skinSha1: string | undefined = undefined;
    if (typeof body.skinSha1 === 'string' && /^[a-fA-F0-9]{40}$/.test(body.skinSha1.trim())) {
      skinSha1 = body.skinSha1.trim().toLowerCase();
    }

    // Fallback to store's skin if not specified
    if (!skinSha1) {
      try {
        const existingSkin = await store.getSkin(user.nick.toLowerCase());
        if (existingSkin) {
          skinSha1 = existingSkin.sha1;
          skinModel = existingSkin.model;
        }
      } catch { }
    }

    // Validate achievements (max 100)
    const rawAchievements = Array.isArray(body.achievements) ? body.achievements : [];
    const cleanAchievements: UserAchievementItem[] = rawAchievements
      .slice(0, 100)
      .filter((a: any) => a && typeof a.id === 'string' && a.id.trim().length > 0)
      .map((a: any) => ({
        id: String(a.id).trim().slice(0, 64),
        unlockedAt: Math.max(0, Number(a.unlockedAt) || Date.now())
      }));

    // Validate worlds (max 50) - display name, lastPlayed, sizeBytes. NO paths or files!
    const rawWorlds = Array.isArray(body.worlds) ? body.worlds : [];
    const cleanWorlds: UserWorldItem[] = rawWorlds
      .slice(0, 50)
      .filter((w: any) => w && typeof w.name === 'string' && w.name.trim().length > 0)
      .map((w: any) => ({
        name: String(w.name).replace(/[\\/]/g, '').trim().slice(0, 64),
        lastPlayed: Math.max(0, Number(w.lastPlayed) || 0),
        sizeBytes: Math.max(0, Math.min(100_000_000_000, Number(w.sizeBytes) || 0))
      }));

    // Validate privacy settings
    const privacy: UserPrivacySettings = {
      showStats: body.privacy?.showStats !== false,
      showAchievements: body.privacy?.showAchievements !== false,
      showWorlds: body.privacy?.showWorlds !== false
    };

    const snapshot: UserProfileSnapshot = {
      userId: user.id,
      nick: user.nick,
      skinModel,
      skinSha1,
      playTimeSeconds,
      gameLaunches,
      achievements: cleanAchievements,
      worlds: cleanWorlds,
      privacy,
      updatedAt: Date.now()
    };

    await store.setUserProfile(snapshot);

    sendJson(res, 200, {
      success: true,
      updatedAt: snapshot.updatedAt
    });
  } catch (err: any) {
    console.error('[lobby-api] Error in handleSetProfile:', err);
    sendJson(res, 500, { error: 'Внутренняя ошибка сохранения профиля' });
  }
}

// 10. Profile Snapshot: GET /api/user/profile?friendId=... (Read friend's profile with server-side privacy truncation)
export async function handleGetProfile(req: any, res: any) {
  try {
    const store = getStore();
    const user = await authenticate(req, res, store);
    if (!user) return;

    const allowed = await store.checkRateLimit(`profile_get:${user.id}`, 60, 60);
    if (!allowed) {
      return sendJson(res, 429, { error: 'Слишком много запросов (максимум 60 в минуту)' });
    }

    // friendId or userId from query / body
    let targetId = '';
    if (req.url) {
      try {
        const q = new URL(req.url, 'http://localhost').searchParams;
        targetId = q.get('friendId') || q.get('userId') || '';
      } catch { }
    }
    if (!targetId && req.query) {
      targetId = req.query.friendId || req.query.userId || '';
    }
    if (!targetId && req.method === 'POST') {
      const body = await parseJson(req);
      targetId = body.friendId || body.userId || '';
    }
    targetId = targetId.trim();

    if (!targetId) {
      return sendJson(res, 400, { error: 'Параметр friendId обязателен' });
    }

    // If requesting another user's profile, verify friendship on server!
    const isSelf = targetId.toLowerCase() === user.id.toLowerCase();
    if (!isSelf) {
      const isFriend = await store.isFriend(user.id, targetId);
      if (!isFriend) {
        return sendJson(res, 403, { error: 'Доступ запрещён: пользователь не в списке друзей' });
      }
    }

    let profile = await store.getUserProfile(targetId);
    if (!profile) {
      // If user exists, provide fallback with basic info
      const targetUser = await store.getUser(targetId);
      if (!targetUser) {
        return sendJson(res, 404, { error: 'Пользователь не найден' });
      }

      let skinSha1: string | undefined = undefined;
      let skinModel: 'default' | 'slim' = 'default';
      try {
        const s = await store.getSkin(targetUser.nick.toLowerCase());
        if (s) {
          skinSha1 = s.sha1;
          skinModel = s.model;
        }
      } catch { }

      profile = {
        userId: targetUser.id,
        nick: targetUser.nick,
        skinModel,
        skinSha1,
        playTimeSeconds: 0,
        gameLaunches: 0,
        achievements: [],
        worlds: [],
        privacy: { showStats: true, showAchievements: true, showWorlds: true },
        updatedAt: targetUser.createdAt || 0
      };
    }

    // If skinSha1 is missing in profile, try to enrich from skin store
    if (!profile.skinSha1) {
      try {
        const s = await store.getSkin(profile.nick.toLowerCase());
        if (s) {
          profile.skinSha1 = s.sha1;
          profile.skinModel = s.model;
        }
      } catch { }
    }

    // If requesting self, return everything
    if (isSelf) {
      return sendJson(res, 200, profile);
    }

    // SERVER-SIDE PRIVACY TRUNCATION - DO NOT TRUST CLIENT
    const filtered: any = {
      userId: profile.userId,
      nick: profile.nick,
      skinModel: profile.skinModel || 'default',
      skinSha1: profile.skinSha1,
      privacy: profile.privacy,
      updatedAt: profile.updatedAt
    };

    if (profile.privacy.showStats) {
      filtered.playTimeSeconds = profile.playTimeSeconds;
      filtered.gameLaunches = profile.gameLaunches;
    }

    if (profile.privacy.showAchievements) {
      filtered.achievements = profile.achievements;
    }

    if (profile.privacy.showWorlds) {
      filtered.worlds = profile.worlds;
    }

    sendJson(res, 200, filtered);
  } catch (err: any) {
    console.error('[lobby-api] Error in handleGetProfile:', err);
    sendJson(res, 500, { error: 'Внутренняя ошибка загрузки профиля' });
  }
}

export async function handleUserProfile(req: any, res: any) {
  try {
    if (req.method === 'GET') {
      return await handleGetProfile(req, res);
    }
    if (req.method === 'POST') {
      return await handleSetProfile(req, res);
    }
    sendJson(res, 405, { error: 'Method Not Allowed' });
  } catch (err: any) {
    console.error('[lobby-api] Uncaught error in handleUserProfile dispatcher:', err);
    sendJson(res, 500, { error: 'Внутренняя ошибка обработчика профиля' });
  }
}


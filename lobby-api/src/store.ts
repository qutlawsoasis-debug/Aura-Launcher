import crypto from 'crypto';

export interface ModManifestItem {
  id: string;
  name?: string;
  version: string;
  enabled: boolean;
}

export interface ModMismatchItem {
  modId: string;
  modName: string;
  type: 'disabled' | 'missing' | 'extra' | 'version';
  typeRu: 'выключен' | 'нет' | 'лишний' | 'версия';
  playerVersion?: string;
  hostVersion?: string;
}

export interface PlayerModSyncInfo {
  hash: string | null;
  status: 'synced' | 'mismatch' | 'unverified';
  mismatches: ModMismatchItem[];
}

export interface Lobby {
  code: string;
  hostToken: string;
  hostName: string;
  status: 'waiting' | 'open' | 'closed';
  tunnelAddress: string | null;
  createdAt: number;
  lastHeartbeat: number;
  players: string[];
  manifests?: Record<string, ModManifestItem[]>;
  manifestHashes?: Record<string, string>;
}

export function computeModManifestHash(manifest: ModManifestItem[]): string {
  const normalized = [...manifest]
    .sort((a, b) => a.id.localeCompare(b.id))
    .map(m => `${m.id.toLowerCase()}:${m.version}:${m.enabled ? 1 : 0}`)
    .join('\n');
  return crypto.createHash('sha256').update(normalized).digest('hex').substring(0, 16);
}

export function computeLobbyModSync(lobby: Lobby): Record<string, PlayerModSyncInfo> {
  const result: Record<string, PlayerModSyncInfo> = {};
  const hostName = lobby.hostName;
  const hostManifest = lobby.manifests ? lobby.manifests[hostName] : undefined;
  const hostHash = lobby.manifestHashes ? lobby.manifestHashes[hostName] : undefined;

  const hostModsMap = new Map<string, ModManifestItem>();
  if (hostManifest) {
    for (const m of hostManifest) {
      hostModsMap.set(m.id.toLowerCase(), m);
    }
  }

  for (const player of lobby.players) {
    const playerManifest = lobby.manifests ? lobby.manifests[player] : undefined;
    const playerHash = lobby.manifestHashes ? lobby.manifestHashes[player] : undefined;

    // If host has no manifest or player has no manifest -> unverified
    if (!hostManifest || !playerManifest) {
      result[player] = {
        hash: playerHash || null,
        status: 'unverified',
        mismatches: []
      };
      continue;
    }

    // Host is always synced with self
    if (player.toLowerCase() === hostName.toLowerCase()) {
      result[player] = {
        hash: hostHash || computeModManifestHash(hostManifest),
        status: 'synced',
        mismatches: []
      };
      continue;
    }

    // If hashes match exactly, 0 mismatches
    if (playerHash && hostHash && playerHash === hostHash) {
      result[player] = {
        hash: playerHash,
        status: 'synced',
        mismatches: []
      };
      continue;
    }

    const playerModsMap = new Map<string, ModManifestItem>();
    for (const m of playerManifest) {
      playerModsMap.set(m.id.toLowerCase(), m);
    }

    const mismatches: ModMismatchItem[] = [];

    // 1. Check all mods that host has enabled
    for (const hostMod of hostManifest) {
      if (!hostMod.enabled) continue;

      const guestMod = playerModsMap.get(hostMod.id.toLowerCase());
      if (!guestMod) {
        mismatches.push({
          modId: hostMod.id,
          modName: hostMod.name || hostMod.id,
          type: 'missing',
          typeRu: 'нет',
          hostVersion: hostMod.version
        });
      } else if (!guestMod.enabled) {
        if (guestMod.version && hostMod.version && guestMod.version !== hostMod.version) {
          mismatches.push({
            modId: hostMod.id,
            modName: guestMod.name || hostMod.name || hostMod.id,
            type: 'version',
            typeRu: 'версия',
            playerVersion: guestMod.version,
            hostVersion: hostMod.version
          });
        } else {
          mismatches.push({
            modId: hostMod.id,
            modName: guestMod.name || hostMod.name || hostMod.id,
            type: 'disabled',
            typeRu: 'выключен',
            playerVersion: guestMod.version,
            hostVersion: hostMod.version
          });
        }
      } else {
        if (guestMod.version && hostMod.version && guestMod.version !== hostMod.version) {
          mismatches.push({
            modId: hostMod.id,
            modName: guestMod.name || hostMod.name || hostMod.id,
            type: 'version',
            typeRu: 'версия',
            playerVersion: guestMod.version,
            hostVersion: hostMod.version
          });
        }
      }
    }

    // 2. Check mods that guest has enabled: extra
    for (const guestMod of playerManifest) {
      if (!guestMod.enabled) continue;

      const hostMod = hostModsMap.get(guestMod.id.toLowerCase());
      if (!hostMod || !hostMod.enabled) {
        mismatches.push({
          modId: guestMod.id,
          modName: guestMod.name || guestMod.id,
          type: 'extra',
          typeRu: 'лишний',
          playerVersion: guestMod.version
        });
      }
    }

    result[player] = {
      hash: playerHash || computeModManifestHash(playerManifest),
      status: mismatches.length === 0 ? 'synced' : 'mismatch',
      mismatches
    };
  }

  return result;
}

export interface SkinRecord {
  nickname: string;
  userId?: string;
  ownerToken?: string;
  model: 'default' | 'slim';
  sha1: string;
  skinBase64: string;
  updatedAt: number;
}

export interface UserRecord {
  id: string; // uuid
  nick: string;
  tokenHash: string; // sha256(userToken)
  friendCode: string; // 8 chars (ABCDEFGHJKLMNPQRSTUVWXYZ23456789)
  createdAt: number;
}

export interface UserPresence {
  userId: string;
  nick: string;
  status: 'online' | 'lobby' | 'playing' | 'offline';
  lobbyCode?: string;
  lastSeen: number;
}

export interface FriendInfo {
  id: string;
  nick: string;
  online: boolean;
  status: string;
  lastSeen: number;
}

export interface UserSummary {
  id: string;
  nick: string;
}

export interface InviteRecord {
  inviteId: string;
  fromId: string;
  fromNick: string;
  toId: string;
  lobbyCode: string;
  ts: number;
  expiresAt: number;
}

export interface SentInviteRecord {
  inviteId: string;
  friendId: string;
  state: 'pending' | 'accepted' | 'declined' | 'expired';
  createdAt: number;
  expiresAt: number;
}

export interface IncomingInviteInfo {
  inviteId: string;
  fromId: string;
  fromNick: string;
  ts: number;
}

export interface SentInviteInfo {
  inviteId: string;
  friendId: string;
  state: 'pending' | 'accepted' | 'declined' | 'expired';
}

export interface SyncResult {
  friends: FriendInfo[];
  incomingRequests: UserSummary[];
  outgoingRequests: UserSummary[];
  invites: IncomingInviteInfo[];
  sentInvites: SentInviteInfo[];
}

export interface LobbyStore {
  // Lobbies
  get(code: string): Promise<Lobby | null>;
  set(lobby: Lobby, ttlSeconds?: number): Promise<void>;
  delete(code: string): Promise<void>;
  generateCode(): Promise<string>;

  // Skin storage
  getSkin(nickLower: string): Promise<SkinRecord | null>;
  setSkin(skin: SkinRecord, ttlSeconds?: number): Promise<void>;
  deleteSkin(nickLower: string): Promise<void>;
  getSkinByHash(sha1: string): Promise<Buffer | null>;
  setSkinByHash(sha1: string, buffer: Buffer, ttlSeconds?: number): Promise<void>;

  // Rate limiting (sliding / fixed window)
  checkRateLimit(key: string, limit: number, windowSeconds: number): Promise<boolean>;

  // Task 32/39: Users & Friends & Invites
  generateFriendCode(): Promise<string>;
  createUser(user: UserRecord): Promise<void>;
  getUser(userId: string): Promise<UserRecord | null>;
  getUserByNick(nick: string): Promise<UserRecord | null>;
  updateUserNick(userId: string, newNick: string): Promise<void>;
  getUserByFriendCode(code: string): Promise<UserRecord | null>;
  getUsers(userIds: string[]): Promise<Map<string, UserRecord>>;

  // Friends & Requests
  getFriends(userId: string): Promise<string[]>;
  isFriend(userId: string, friendId: string): Promise<boolean>;
  getFriendCount(userId: string): Promise<number>;
  addFriend(userId: string, friendId: string): Promise<void>;
  removeFriend(userId: string, friendId: string): Promise<void>;

  getIncomingRequests(userId: string): Promise<string[]>;
  getOutgoingRequests(userId: string): Promise<string[]>;
  hasFriendRequest(fromId: string, toId: string): Promise<boolean>;
  addFriendRequest(fromId: string, toId: string): Promise<void>;
  removeFriendRequest(fromId: string, toId: string): Promise<void>;

  // Invites
  createInvite(invite: InviteRecord): Promise<void>;
  createSentInvite(fromId: string, sentInvite: SentInviteRecord): Promise<void>;
  getInvite(toId: string, inviteId: string): Promise<InviteRecord | null>;
  removeInvite(toId: string, inviteId: string): Promise<void>;
  updateSentInvite(fromId: string, inviteId: string, state: 'accepted' | 'declined' | 'expired'): Promise<void>;

  // Unified Sync
  sync(userId: string, presence: UserPresence): Promise<SyncResult>;
}

export class InMemoryStore implements LobbyStore {
  private lobbies = new Map<string, { lobby: Lobby; expiresAt: number }>();
  private skins = new Map<string, { skin: SkinRecord; expiresAt: number }>();
  private textures = new Map<string, { data: Buffer; expiresAt: number }>();
  private rateLimits = new Map<string, { count: number; resetAt: number }>();

  // Task 32 in-memory state
  private users = new Map<string, UserRecord>();
  private nickUsers = new Map<string, string>(); // lowerNick -> userId
  private friendCodes = new Map<string, string>(); // code -> userId
  private presences = new Map<string, { presence: UserPresence; expiresAt: number }>();
  private lastSeens = new Map<string, number>();
  private friends = new Map<string, Set<string>>();
  private reqIn = new Map<string, Set<string>>();
  private reqOut = new Map<string, Set<string>>();
  private invites = new Map<string, Map<string, InviteRecord>>(); // toId -> (inviteId -> invite)
  private sentInvites = new Map<string, Map<string, SentInviteRecord>>(); // fromId -> (inviteId -> sentInvite)

  async get(code: string): Promise<Lobby | null> {
    const entry = this.lobbies.get(code);
    if (!entry) return null;
    if (Date.now() > entry.expiresAt) {
      this.lobbies.delete(code);
      return null;
    }
    return entry.lobby;
  }

  async set(lobby: Lobby, ttlSeconds: number = 60): Promise<void> {
    this.lobbies.set(lobby.code, {
      lobby,
      expiresAt: Date.now() + ttlSeconds * 1000
    });
  }

  async delete(code: string): Promise<void> {
    this.lobbies.delete(code);
  }

  async generateCode(): Promise<string> {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    for (let attempts = 0; attempts < 100; attempts++) {
      let code = '';
      for (let i = 0; i < 6; i++) {
        code += chars.charAt(crypto.randomInt(chars.length));
      }
      const existing = await this.get(code);
      if (!existing) return code;
    }
    return Math.random().toString(36).substring(2, 8).toUpperCase();
  }

  async getSkin(nickLower: string): Promise<SkinRecord | null> {
    const entry = this.skins.get(nickLower.toLowerCase());
    if (!entry) return null;
    if (Date.now() > entry.expiresAt) {
      this.skins.delete(nickLower.toLowerCase());
      return null;
    }
    return entry.skin;
  }

  async setSkin(skin: SkinRecord, ttlSeconds: number = 30 * 24 * 3600): Promise<void> {
    this.skins.set(skin.nickname.toLowerCase(), {
      skin,
      expiresAt: Date.now() + ttlSeconds * 1000
    });
  }

  async deleteSkin(nickLower: string): Promise<void> {
    this.skins.delete(nickLower.toLowerCase());
  }

  async getSkinByHash(sha1: string): Promise<Buffer | null> {
    const entry = this.textures.get(sha1.toLowerCase());
    if (!entry) return null;
    if (Date.now() > entry.expiresAt) {
      this.textures.delete(sha1.toLowerCase());
      return null;
    }
    return entry.data;
  }

  async setSkinByHash(sha1: string, buffer: Buffer, ttlSeconds: number = 30 * 24 * 3600): Promise<void> {
    this.textures.set(sha1.toLowerCase(), {
      data: buffer,
      expiresAt: Date.now() + ttlSeconds * 1000
    });
  }

  async checkRateLimit(key: string, limit: number, windowSeconds: number): Promise<boolean> {
    const now = Date.now();
    const entry = this.rateLimits.get(key);
    if (!entry || now > entry.resetAt) {
      this.rateLimits.set(key, { count: 1, resetAt: now + windowSeconds * 1000 });
      return true;
    }
    if (entry.count >= limit) {
      return false;
    }
    entry.count++;
    return true;
  }

  // --- Task 32 Users & Friends & Invites ---

  async generateFriendCode(): Promise<string> {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    for (let attempts = 0; attempts < 1000; attempts++) {
      let code = '';
      for (let i = 0; i < 8; i++) {
        code += chars.charAt(crypto.randomInt(chars.length));
      }
      if (!this.friendCodes.has(code)) return code;
    }
    return crypto.randomBytes(4).toString('hex').toUpperCase();
  }

  async createUser(user: UserRecord): Promise<void> {
    this.users.set(user.id, user);
    this.nickUsers.set(user.nick.toLowerCase(), user.id);
    this.friendCodes.set(user.friendCode.toUpperCase(), user.id);
  }

  async getUser(userId: string): Promise<UserRecord | null> {
    return this.users.get(userId) || null;
  }

  async getUserByNick(nick: string): Promise<UserRecord | null> {
    const id = this.nickUsers.get(nick.toLowerCase());
    if (id) {
      return this.users.get(id) || null;
    }
    for (const u of this.users.values()) {
      if (u.nick.toLowerCase() === nick.toLowerCase()) {
        this.nickUsers.set(nick.toLowerCase(), u.id);
        return u;
      }
    }
    return null;
  }

  async updateUserNick(userId: string, newNick: string): Promise<void> {
    const user = this.users.get(userId);
    if (user) {
      this.nickUsers.delete(user.nick.toLowerCase());
      user.nick = newNick;
      this.nickUsers.set(newNick.toLowerCase(), userId);
    }
  }

  async getUserByFriendCode(code: string): Promise<UserRecord | null> {
    const id = this.friendCodes.get(code.toUpperCase());
    if (!id) return null;
    return this.users.get(id) || null;
  }

  async getUsers(userIds: string[]): Promise<Map<string, UserRecord>> {
    const res = new Map<string, UserRecord>();
    for (const id of userIds) {
      const u = this.users.get(id);
      if (u) res.set(id, u);
    }
    return res;
  }

  async getFriends(userId: string): Promise<string[]> {
    return Array.from(this.friends.get(userId) || []);
  }

  async isFriend(userId: string, friendId: string): Promise<boolean> {
    return Boolean(this.friends.get(userId)?.has(friendId));
  }

  async getFriendCount(userId: string): Promise<number> {
    return this.friends.get(userId)?.size || 0;
  }

  async addFriend(userId: string, friendId: string): Promise<void> {
    if (!this.friends.has(userId)) this.friends.set(userId, new Set());
    if (!this.friends.has(friendId)) this.friends.set(friendId, new Set());
    this.friends.get(userId)!.add(friendId);
    this.friends.get(friendId)!.add(userId);
  }

  async removeFriend(userId: string, friendId: string): Promise<void> {
    this.friends.get(userId)?.delete(friendId);
    this.friends.get(friendId)?.delete(userId);
  }

  async getIncomingRequests(userId: string): Promise<string[]> {
    return Array.from(this.reqIn.get(userId) || []);
  }

  async getOutgoingRequests(userId: string): Promise<string[]> {
    return Array.from(this.reqOut.get(userId) || []);
  }

  async hasFriendRequest(fromId: string, toId: string): Promise<boolean> {
    return Boolean(this.reqOut.get(fromId)?.has(toId));
  }

  async addFriendRequest(fromId: string, toId: string): Promise<void> {
    if (!this.reqOut.has(fromId)) this.reqOut.set(fromId, new Set());
    if (!this.reqIn.has(toId)) this.reqIn.set(toId, new Set());
    this.reqOut.get(fromId)!.add(toId);
    this.reqIn.get(toId)!.add(fromId);
  }

  async removeFriendRequest(fromId: string, toId: string): Promise<void> {
    this.reqOut.get(fromId)?.delete(toId);
    this.reqIn.get(toId)?.delete(fromId);
  }

  async createInvite(invite: InviteRecord): Promise<void> {
    if (!this.invites.has(invite.toId)) this.invites.set(invite.toId, new Map());
    this.invites.get(invite.toId)!.set(invite.inviteId, invite);
  }

  async createSentInvite(fromId: string, sentInvite: SentInviteRecord): Promise<void> {
    if (!this.sentInvites.has(fromId)) this.sentInvites.set(fromId, new Map());
    this.sentInvites.get(fromId)!.set(sentInvite.inviteId, sentInvite);
  }

  async getInvite(toId: string, inviteId: string): Promise<InviteRecord | null> {
    const inv = this.invites.get(toId)?.get(inviteId);
    if (!inv) return null;
    if (Date.now() > inv.expiresAt) {
      this.invites.get(toId)?.delete(inviteId);
      return null;
    }
    return inv;
  }

  async removeInvite(toId: string, inviteId: string): Promise<void> {
    this.invites.get(toId)?.delete(inviteId);
  }

  async updateSentInvite(fromId: string, inviteId: string, state: 'accepted' | 'declined' | 'expired'): Promise<void> {
    const m = this.sentInvites.get(fromId);
    const sent = m?.get(inviteId);
    if (sent) {
      sent.state = state;
      sent.expiresAt = Date.now() + 60_000;
    }
  }

  async sync(userId: string, presence: UserPresence): Promise<SyncResult> {
    const now = Date.now();
    // 1. Обновляем присутствие (TTL 60 с) или удаляем при явном offline
    if (presence.status === 'offline') {
      this.presences.delete(userId);
    } else {
      this.presences.set(userId, { presence, expiresAt: now + 60_000 });
    }
    this.lastSeens.set(userId, presence.lastSeen || now);

    const user = this.users.get(userId);
    if (user && user.nick !== presence.nick) {
      user.nick = presence.nick;
    }

    // 2. Друзья
    const friendIds = Array.from(this.friends.get(userId) || []);
    const friends: FriendInfo[] = [];
    for (const fId of friendIds) {
      const fUser = this.users.get(fId);
      const fPres = this.presences.get(fId);
      const isOnline = Boolean(fPres && now <= fPres.expiresAt && fPres.presence.status !== 'offline');
      const recordedLastSeen = this.lastSeens.get(fId) || fPres?.presence?.lastSeen || fUser?.createdAt || 0;
      friends.push({
        id: fId,
        nick: fUser?.nick || 'Unknown',
        online: isOnline,
        status: isOnline ? fPres!.presence.status : 'offline',
        lastSeen: isOnline ? fPres!.presence.lastSeen : recordedLastSeen
      });
    }

    // 3. Входящие заявки
    const inIds = Array.from(this.reqIn.get(userId) || []);
    const incomingRequests: UserSummary[] = [];
    for (const id of inIds) {
      const u = this.users.get(id);
      incomingRequests.push({ id, nick: u?.nick || 'Unknown' });
    }

    // 4. Исходящие заявки
    const outIds = Array.from(this.reqOut.get(userId) || []);
    const outgoingRequests: UserSummary[] = [];
    for (const id of outIds) {
      const u = this.users.get(id);
      outgoingRequests.push({ id, nick: u?.nick || 'Unknown' });
    }

    // 5. Приглашения получателю (БЕЗ lobbyCode)
    const userInvitesMap = this.invites.get(userId);
    const invites: IncomingInviteInfo[] = [];
    if (userInvitesMap) {
      for (const [invId, inv] of userInvitesMap.entries()) {
        if (now > inv.expiresAt) {
          userInvitesMap.delete(invId);
        } else {
          invites.push({
            inviteId: inv.inviteId,
            fromId: inv.fromId,
            fromNick: inv.fromNick,
            ts: inv.ts
          });
        }
      }
    }

    // 6. Отправленные приглашения отправителя (ещё 60с после ответа/истечения)
    const userSentMap = this.sentInvites.get(userId);
    const sentInvites: SentInviteInfo[] = [];
    if (userSentMap) {
      for (const [invId, sent] of userSentMap.entries()) {
        if (sent.state === 'pending' && now > sent.createdAt + 120_000) {
          sent.state = 'expired';
          sent.expiresAt = Math.min(sent.expiresAt, now + 60_000);
        }
        if (now > sent.expiresAt) {
          userSentMap.delete(invId);
        } else {
          sentInvites.push({
            inviteId: sent.inviteId,
            friendId: sent.friendId,
            state: sent.state
          });
        }
      }
    }

    return {
      friends,
      incomingRequests,
      outgoingRequests,
      invites,
      sentInvites
    };
  }
}

function parseHGetAll(raw: any): Map<string, string> {
  const map = new Map<string, string>();
  if (Array.isArray(raw)) {
    for (let i = 0; i < raw.length; i += 2) {
      if (i + 1 < raw.length) {
        map.set(raw[i], raw[i + 1]);
      }
    }
  } else if (raw && typeof raw === 'object') {
    for (const [k, v] of Object.entries(raw)) {
      map.set(k, String(v));
    }
  }
  return map;
}

export class UpstashStore implements LobbyStore {
  private url: string;
  private token: string;
  private inMemoryFallback: InMemoryStore = new InMemoryStore();

  constructor(url: string, token: string) {
    this.url = url.replace(/\/$/, '');
    this.token = token;
  }

  private async fetchCommand(command: any[]): Promise<any> {
    const res = await fetch(`${this.url}`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${this.token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(command)
    });
    if (!res.ok) {
      const errText = await res.text();
      throw new Error(`Upstash error (${res.status}): ${errText}`);
    }
    const data = await res.json();
    return data.result;
  }

  private async fetchPipeline(commands: any[][]): Promise<any[]> {
    if (commands.length === 0) return [];
    const res = await fetch(`${this.url}/pipeline`, {
      method: 'POST',
      headers: {
        Authorization: `Bearer ${this.token}`,
        'Content-Type': 'application/json'
      },
      body: JSON.stringify(commands)
    });
    if (!res.ok) {
      const errText = await res.text();
      throw new Error(`Upstash pipeline error (${res.status}): ${errText}`);
    }
    const data = await res.json();
    return data.map((d: any) => d.result);
  }

  async get(code: string): Promise<Lobby | null> {
    const raw = await this.fetchCommand(['GET', `lobby:${code}`]);
    if (!raw) return null;
    try {
      return typeof raw === 'string' ? JSON.parse(raw) : raw;
    } catch {
      return null;
    }
  }

  async set(lobby: Lobby, ttlSeconds: number = 60): Promise<void> {
    const serialized = JSON.stringify(lobby);
    await this.fetchCommand(['SET', `lobby:${lobby.code}`, serialized, 'EX', ttlSeconds.toString()]);
  }

  async delete(code: string): Promise<void> {
    await this.fetchCommand(['DEL', `lobby:${code}`]);
  }

  async generateCode(): Promise<string> {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    for (let attempts = 0; attempts < 100; attempts++) {
      let code = '';
      for (let i = 0; i < 6; i++) {
        code += chars.charAt(crypto.randomInt(chars.length));
      }
      const existing = await this.get(code);
      if (!existing) return code;
    }
    return Math.random().toString(36).substring(2, 8).toUpperCase();
  }

  async getSkin(nickLower: string): Promise<SkinRecord | null> {
    const raw = await this.fetchCommand(['GET', `skin:${nickLower.toLowerCase()}`]);
    if (!raw) return null;
    try {
      return typeof raw === 'string' ? JSON.parse(raw) : raw;
    } catch {
      return null;
    }
  }

  async setSkin(skin: SkinRecord, ttlSeconds: number = 30 * 24 * 3600): Promise<void> {
    const serialized = JSON.stringify(skin);
    await this.fetchCommand(['SET', `skin:${skin.nickname.toLowerCase()}`, serialized, 'EX', ttlSeconds.toString()]);
  }

  async deleteSkin(nickLower: string): Promise<void> {
    await this.fetchCommand(['DEL', `skin:${nickLower.toLowerCase()}`]);
  }

  async getSkinByHash(sha1: string): Promise<Buffer | null> {
    const raw = await this.fetchCommand(['GET', `skinhash:${sha1.toLowerCase()}`]);
    if (!raw) return null;
    try {
      return Buffer.from(raw, 'base64');
    } catch {
      return null;
    }
  }

  async setSkinByHash(sha1: string, buffer: Buffer, ttlSeconds: number = 30 * 24 * 3600): Promise<void> {
    const b64 = buffer.toString('base64');
    await this.fetchCommand(['SET', `skinhash:${sha1.toLowerCase()}`, b64, 'EX', ttlSeconds.toString()]);
  }

  async checkRateLimit(key: string, limit: number, windowSeconds: number): Promise<boolean> {
    try {
      const redisKey = `ratelimit:${key}`;
      const count = await this.fetchCommand(['INCR', redisKey]);
      if (count === 1) {
        await this.fetchCommand(['EXPIRE', redisKey, windowSeconds.toString()]);
      }
      return count <= limit;
    } catch {
      return this.inMemoryFallback.checkRateLimit(key, limit, windowSeconds);
    }
  }

  // --- Task 32 Users & Friends & Invites ---

  async generateFriendCode(): Promise<string> {
    const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
    for (let attempts = 0; attempts < 100; attempts++) {
      let code = '';
      for (let i = 0; i < 8; i++) {
        code += chars.charAt(crypto.randomInt(chars.length));
      }
      const existing = await this.fetchCommand(['GET', `friendcode:${code}`]);
      if (!existing) return code;
    }
    return crypto.randomBytes(4).toString('hex').toUpperCase();
  }

  async createUser(user: UserRecord): Promise<void> {
    await this.fetchPipeline([
      ['SET', `user:${user.id}`, JSON.stringify(user)],
      ['SET', `nickuser:${user.nick.toLowerCase()}`, user.id],
      ['SET', `friendcode:${user.friendCode.toUpperCase()}`, user.id]
    ]);
  }

  async getUser(userId: string): Promise<UserRecord | null> {
    const raw = await this.fetchCommand(['GET', `user:${userId}`]);
    if (!raw) return null;
    try {
      return typeof raw === 'string' ? JSON.parse(raw) : raw;
    } catch {
      return null;
    }
  }

  async getUserByNick(nick: string): Promise<UserRecord | null> {
    const userId = await this.fetchCommand(['GET', `nickuser:${nick.toLowerCase()}`]);
    if (userId) {
      return this.getUser(userId);
    }
    return null;
  }

  async updateUserNick(userId: string, newNick: string): Promise<void> {
    const user = await this.getUser(userId);
    if (!user) return;
    const oldNick = user.nick;
    user.nick = newNick;
    await this.fetchPipeline([
      ['DEL', `nickuser:${oldNick.toLowerCase()}`],
      ['SET', `nickuser:${newNick.toLowerCase()}`, userId],
      ['SET', `user:${userId}`, JSON.stringify(user)]
    ]);
  }

  async getUserByFriendCode(code: string): Promise<UserRecord | null> {
    const userId = await this.fetchCommand(['GET', `friendcode:${code.toUpperCase()}`]);
    if (!userId) return null;
    return this.getUser(userId);
  }

  async getUsers(userIds: string[]): Promise<Map<string, UserRecord>> {
    const map = new Map<string, UserRecord>();
    if (userIds.length === 0) return map;
    const rawList = await this.fetchCommand(['MGET', ...userIds.map(id => `user:${id}`)]);
    if (Array.isArray(rawList)) {
      for (let i = 0; i < userIds.length; i++) {
        const raw = rawList[i];
        if (raw) {
          try {
            map.set(userIds[i], typeof raw === 'string' ? JSON.parse(raw) : raw);
          } catch { }
        }
      }
    }
    return map;
  }

  async getFriends(userId: string): Promise<string[]> {
    const res = await this.fetchCommand(['SMEMBERS', `friends:${userId}`]);
    return Array.isArray(res) ? res : [];
  }

  async isFriend(userId: string, friendId: string): Promise<boolean> {
    const res = await this.fetchCommand(['SISMEMBER', `friends:${userId}`, friendId]);
    return res === 1;
  }

  async getFriendCount(userId: string): Promise<number> {
    const res = await this.fetchCommand(['SCARD', `friends:${userId}`]);
    return typeof res === 'number' ? res : 0;
  }

  async addFriend(userId: string, friendId: string): Promise<void> {
    await this.fetchPipeline([
      ['SADD', `friends:${userId}`, friendId],
      ['SADD', `friends:${friendId}`, userId]
    ]);
  }

  async removeFriend(userId: string, friendId: string): Promise<void> {
    await this.fetchPipeline([
      ['SREM', `friends:${userId}`, friendId],
      ['SREM', `friends:${friendId}`, userId]
    ]);
  }

  async getIncomingRequests(userId: string): Promise<string[]> {
    const res = await this.fetchCommand(['SMEMBERS', `req_in:${userId}`]);
    return Array.isArray(res) ? res : [];
  }

  async getOutgoingRequests(userId: string): Promise<string[]> {
    const res = await this.fetchCommand(['SMEMBERS', `req_out:${userId}`]);
    return Array.isArray(res) ? res : [];
  }

  async hasFriendRequest(fromId: string, toId: string): Promise<boolean> {
    const res = await this.fetchCommand(['SISMEMBER', `req_out:${fromId}`, toId]);
    return res === 1;
  }

  async addFriendRequest(fromId: string, toId: string): Promise<void> {
    await this.fetchPipeline([
      ['SADD', `req_out:${fromId}`, toId],
      ['SADD', `req_in:${toId}`, fromId]
    ]);
  }

  async removeFriendRequest(fromId: string, toId: string): Promise<void> {
    await this.fetchPipeline([
      ['SREM', `req_out:${fromId}`, toId],
      ['SREM', `req_in:${toId}`, fromId]
    ]);
  }

  async createInvite(invite: InviteRecord): Promise<void> {
    await this.fetchPipeline([
      ['HSET', `invites:${invite.toId}`, invite.inviteId, JSON.stringify(invite)],
      ['EXPIRE', `invites:${invite.toId}`, '300']
    ]);
  }

  async createSentInvite(fromId: string, sentInvite: SentInviteRecord): Promise<void> {
    await this.fetchPipeline([
      ['HSET', `sent_invites:${fromId}`, sentInvite.inviteId, JSON.stringify(sentInvite)],
      ['EXPIRE', `sent_invites:${fromId}`, '300']
    ]);
  }

  async getInvite(toId: string, inviteId: string): Promise<InviteRecord | null> {
    const raw = await this.fetchCommand(['HGET', `invites:${toId}`, inviteId]);
    if (!raw) return null;
    try {
      const inv: InviteRecord = typeof raw === 'string' ? JSON.parse(raw) : raw;
      if (Date.now() > inv.expiresAt) {
        await this.removeInvite(toId, inviteId);
        return null;
      }
      return inv;
    } catch {
      return null;
    }
  }

  async removeInvite(toId: string, inviteId: string): Promise<void> {
    await this.fetchCommand(['HDEL', `invites:${toId}`, inviteId]);
  }

  async updateSentInvite(fromId: string, inviteId: string, state: 'accepted' | 'declined' | 'expired'): Promise<void> {
    const raw = await this.fetchCommand(['HGET', `sent_invites:${fromId}`, inviteId]);
    if (raw) {
      try {
        const sent: SentInviteRecord = typeof raw === 'string' ? JSON.parse(raw) : raw;
        sent.state = state;
        sent.expiresAt = Date.now() + 60_000;
        await this.fetchCommand(['HSET', `sent_invites:${fromId}`, inviteId, JSON.stringify(sent)]);
      } catch { }
    }
  }

  async sync(userId: string, presence: UserPresence): Promise<SyncResult> {
    const now = Date.now();

    // Пайплайн 1: обновление присутствия + получение всех связей и хешей
    const isOffline = presence.status === 'offline';
    const p1Commands: any[][] = [
      isOffline ? ['DEL', `presence:${userId}`] : ['SET', `presence:${userId}`, JSON.stringify(presence), 'EX', '60'],
      ['SET', `lastseen:${userId}`, String(presence.lastSeen || now)],
      ['SMEMBERS', `friends:${userId}`],
      ['SMEMBERS', `req_in:${userId}`],
      ['SMEMBERS', `req_out:${userId}`],
      ['HGETALL', `invites:${userId}`],
      ['HGETALL', `sent_invites:${userId}`]
    ];
    const p1Results = await this.fetchPipeline(p1Commands);

    const friendIds: string[] = Array.isArray(p1Results[2]) ? p1Results[2] : [];
    const inIds: string[] = Array.isArray(p1Results[3]) ? p1Results[3] : [];
    const outIds: string[] = Array.isArray(p1Results[4]) ? p1Results[4] : [];
    const rawInvites = p1Results[5];
    const rawSentInvites = p1Results[6];

    const allUserIds = Array.from(new Set([...friendIds, ...inIds, ...outIds]));

    const usersMap = new Map<string, UserRecord>();
    const presencesMap = new Map<string, UserPresence>();
    const lastSeenMap = new Map<string, number>();

    // Пайплайн 2: загрузка профилей и присутствия друзей (при наличии)
    if (allUserIds.length > 0) {
      const p2Commands: any[][] = [
        ['MGET', ...allUserIds.map(id => `user:${id}`)]
      ];
      if (friendIds.length > 0) {
        p2Commands.push(['MGET', ...friendIds.map(id => `presence:${id}`)]);
        p2Commands.push(['MGET', ...friendIds.map(id => `lastseen:${id}`)]);
      }
      const p2Results = await this.fetchPipeline(p2Commands);

      const rawUsers = p2Results[0];
      if (Array.isArray(rawUsers)) {
        for (let i = 0; i < allUserIds.length; i++) {
          const raw = rawUsers[i];
          if (raw) {
            try {
              usersMap.set(allUserIds[i], typeof raw === 'string' ? JSON.parse(raw) : raw);
            } catch { }
          }
        }
      }

      if (friendIds.length > 0) {
        const rawPresences = p2Results[1];
        if (Array.isArray(rawPresences)) {
          for (let i = 0; i < friendIds.length; i++) {
            const raw = rawPresences[i];
            if (raw) {
              try {
                presencesMap.set(friendIds[i], typeof raw === 'string' ? JSON.parse(raw) : raw);
              } catch { }
            }
          }
        }

        const rawLastSeens = p2Results[2];
        if (Array.isArray(rawLastSeens)) {
          for (let i = 0; i < friendIds.length; i++) {
            const raw = rawLastSeens[i];
            if (raw) {
              const num = Number(raw);
              if (!isNaN(num) && num > 0) {
                lastSeenMap.set(friendIds[i], num);
              }
            }
          }
        }
      }
    }

    // 1. Друзья (только друзья видят присутствие и статус)
    const friends: FriendInfo[] = [];
    for (const fId of friendIds) {
      const u = usersMap.get(fId);
      const pres = presencesMap.get(fId);
      const isOnline = Boolean(pres && pres.status !== 'offline');
      const recordedLastSeen = lastSeenMap.get(fId) || u?.createdAt || 0;
      friends.push({
        id: fId,
        nick: u?.nick || 'Unknown',
        online: isOnline,
        status: isOnline ? pres!.status : 'offline',
        lastSeen: isOnline ? pres!.lastSeen : recordedLastSeen
      });
    }

    // 2. Входящие заявки
    const incomingRequests: UserSummary[] = inIds.map(id => ({
      id,
      nick: usersMap.get(id)?.nick || 'Unknown'
    }));

    // 3. Исходящие заявки
    const outgoingRequests: UserSummary[] = outIds.map(id => ({
      id,
      nick: usersMap.get(id)?.nick || 'Unknown'
    }));

    // 4. Приглашения (БЕЗ lobbyCode)
    const invitesMap = parseHGetAll(rawInvites);
    const invites: IncomingInviteInfo[] = [];
    const expiredInviteIds: string[] = [];
    for (const [invId, val] of invitesMap.entries()) {
      try {
        const inv: InviteRecord = typeof val === 'string' ? JSON.parse(val) : val;
        if (now > inv.expiresAt) {
          expiredInviteIds.push(invId);
        } else {
          invites.push({
            inviteId: inv.inviteId,
            fromId: inv.fromId,
            fromNick: inv.fromNick,
            ts: inv.ts
          });
        }
      } catch { }
    }
    if (expiredInviteIds.length > 0) {
      this.fetchCommand(['HDEL', `invites:${userId}`, ...expiredInviteIds]).catch(() => {});
    }

    // 5. Отправленные приглашения
    const sentMap = parseHGetAll(rawSentInvites);
    const sentInvites: SentInviteInfo[] = [];
    const expiredSentIds: string[] = [];
    const updatedSent: SentInviteRecord[] = [];
    for (const [invId, val] of sentMap.entries()) {
      try {
        const sent: SentInviteRecord = typeof val === 'string' ? JSON.parse(val) : val;
        if (sent.state === 'pending' && now > sent.createdAt + 120_000) {
          sent.state = 'expired';
          sent.expiresAt = Math.min(sent.expiresAt, now + 60_000);
          updatedSent.push(sent);
        }
        if (now > sent.expiresAt) {
          expiredSentIds.push(invId);
        } else {
          sentInvites.push({
            inviteId: sent.inviteId,
            friendId: sent.friendId,
            state: sent.state
          });
        }
      } catch { }
    }
    if (expiredSentIds.length > 0) {
      this.fetchCommand(['HDEL', `sent_invites:${userId}`, ...expiredSentIds]).catch(() => {});
    }
    if (updatedSent.length > 0) {
      const updates = updatedSent.flatMap(s => [s.inviteId, JSON.stringify(s)]);
      this.fetchCommand(['HSET', `sent_invites:${userId}`, ...updates]).catch(() => {});
    }

    return {
      friends,
      incomingRequests,
      outgoingRequests,
      invites,
      sentInvites
    };
  }
}

let activeStore: LobbyStore | null = null;

export function getStore(): LobbyStore {
  if (activeStore) return activeStore;

  const upstashUrl = process.env.UPSTASH_REDIS_REST_URL || process.env.KV_REST_API_URL;
  const upstashToken = process.env.UPSTASH_REDIS_REST_TOKEN || process.env.KV_REST_API_TOKEN;

  if (upstashUrl && upstashToken) {
    activeStore = new UpstashStore(upstashUrl, upstashToken);
  } else {
    activeStore = new InMemoryStore();
  }

  return activeStore;
}

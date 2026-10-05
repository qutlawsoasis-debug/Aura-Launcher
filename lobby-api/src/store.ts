export interface Lobby {
  code: string;
  hostToken: string;
  hostName: string;
  status: 'waiting' | 'open' | 'closed';
  tunnelAddress: string | null;
  createdAt: number;
  lastHeartbeat: number;
  players: string[];
}

export interface SkinRecord {
  nickname: string;
  ownerToken: string;
  model: 'default' | 'slim';
  sha1: string;
  skinBase64: string;
  updatedAt: number;
}

export interface LobbyStore {
  get(code: string): Promise<Lobby | null>;
  set(lobby: Lobby, ttlSeconds?: number): Promise<void>;
  delete(code: string): Promise<void>;
  generateCode(): Promise<string>;

  // Skin storage
  getSkin(nickLower: string): Promise<SkinRecord | null>;
  setSkin(skin: SkinRecord, ttlSeconds?: number): Promise<void>;
  getSkinByHash(sha1: string): Promise<Buffer | null>;
  setSkinByHash(sha1: string, buffer: Buffer, ttlSeconds?: number): Promise<void>;

  // Rate limiting (sliding / fixed window)
  checkRateLimit(key: string, limit: number, windowSeconds: number): Promise<boolean>;
}

export class InMemoryStore implements LobbyStore {
  private lobbies = new Map<string, { lobby: Lobby; expiresAt: number }>();
  private skins = new Map<string, { skin: SkinRecord; expiresAt: number }>();
  private textures = new Map<string, { data: Buffer; expiresAt: number }>();
  private rateLimits = new Map<string, { count: number; resetAt: number }>();

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
        code += chars.charAt(Math.floor(Math.random() * chars.length));
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
}

export class UpstashStore implements LobbyStore {
  private url: string;
  private token: string;
  private inMemoryFallback: InMemoryStore = new InMemoryStore();

  constructor(url: string, token: string) {
    this.url = url.replace(/\/$/, '');
    this.token = token;
  }

  private async fetchCommand(command: string[]): Promise<any> {
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
        code += chars.charAt(Math.floor(Math.random() * chars.length));
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

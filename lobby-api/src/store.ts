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

export interface LobbyStore {
  get(code: string): Promise<Lobby | null>;
  set(lobby: Lobby, ttlSeconds?: number): Promise<void>;
  delete(code: string): Promise<void>;
  generateCode(): Promise<string>;
}

export class InMemoryStore implements LobbyStore {
  private lobbies = new Map<string, { lobby: Lobby; expiresAt: number }>();

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
}

export class UpstashStore implements LobbyStore {
  private url: string;
  private token: string;

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

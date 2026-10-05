export class InMemoryStore {
    lobbies = new Map();
    async get(code) {
        const entry = this.lobbies.get(code);
        if (!entry)
            return null;
        if (Date.now() > entry.expiresAt) {
            this.lobbies.delete(code);
            return null;
        }
        return entry.lobby;
    }
    async set(lobby, ttlSeconds = 60) {
        this.lobbies.set(lobby.code, {
            lobby,
            expiresAt: Date.now() + ttlSeconds * 1000
        });
    }
    async delete(code) {
        this.lobbies.delete(code);
    }
    async generateCode() {
        const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
        for (let attempts = 0; attempts < 100; attempts++) {
            let code = '';
            for (let i = 0; i < 6; i++) {
                code += chars.charAt(Math.floor(Math.random() * chars.length));
            }
            const existing = await this.get(code);
            if (!existing)
                return code;
        }
        return Math.random().toString(36).substring(2, 8).toUpperCase();
    }
}
export class UpstashStore {
    url;
    token;
    constructor(url, token) {
        this.url = url.replace(/\/$/, '');
        this.token = token;
    }
    async fetchCommand(command) {
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
    async get(code) {
        const raw = await this.fetchCommand(['GET', `lobby:${code}`]);
        if (!raw)
            return null;
        try {
            return typeof raw === 'string' ? JSON.parse(raw) : raw;
        }
        catch {
            return null;
        }
    }
    async set(lobby, ttlSeconds = 60) {
        const serialized = JSON.stringify(lobby);
        await this.fetchCommand(['SET', `lobby:${lobby.code}`, serialized, 'EX', ttlSeconds.toString()]);
    }
    async delete(code) {
        await this.fetchCommand(['DEL', `lobby:${code}`]);
    }
    async generateCode() {
        const chars = 'ABCDEFGHJKLMNPQRSTUVWXYZ23456789';
        for (let attempts = 0; attempts < 100; attempts++) {
            let code = '';
            for (let i = 0; i < 6; i++) {
                code += chars.charAt(Math.floor(Math.random() * chars.length));
            }
            const existing = await this.get(code);
            if (!existing)
                return code;
        }
        return Math.random().toString(36).substring(2, 8).toUpperCase();
    }
}
let activeStore = null;
export function getStore() {
    if (activeStore)
        return activeStore;
    const upstashUrl = process.env.UPSTASH_REDIS_REST_URL;
    const upstashToken = process.env.UPSTASH_REDIS_REST_TOKEN;
    if (upstashUrl && upstashToken) {
        activeStore = new UpstashStore(upstashUrl, upstashToken);
    }
    else {
        activeStore = new InMemoryStore();
    }
    return activeStore;
}

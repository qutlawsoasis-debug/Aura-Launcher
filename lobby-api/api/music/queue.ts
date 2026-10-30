import { sendJson, parseJson, getClientIp, redisGet, redisSet } from '../_utils.js';
import { getStore } from '../../src/store.js';

export interface QueueTrack {
  id: string;
  title: string;
  artist: string;
  art?: string;
  duration?: number;
  addedBy?: string;
  addedAt?: number;
}

export interface SharedQueueState {
  lobbyId: string;
  hostId?: string;
  current: QueueTrack | null;
  positionMs: number;
  isPlaying: boolean;
  updatedAt: number;
  queue: QueueTrack[];
  skipVotes: string[]; // user IDs who voted to skip
}

const QUEUE_TTL_SECONDS = 21600; // 6 часов

export default async function handler(req: any, res: any) {
  if (req.method === 'OPTIONS') {
    res.setHeader('Access-Control-Allow-Origin', '*');
    res.setHeader('Access-Control-Allow-Methods', 'GET, POST, OPTIONS');
    res.setHeader('Access-Control-Allow-Headers', 'Content-Type, Authorization, X-Aura-Client, X-User-Id');
    return res.status ? res.status(204).end() : (res.writeHead(204), res.end());
  }

  const store = getStore();
  const clientIp = getClientIp(req);

  // Rate limit: 60 запросов в минуту
  const allowed = await store.checkRateLimit(`queue:${clientIp}`, 60, 60);
  if (!allowed) {
    return sendJson(res, 429, { error: 'rate_limited', message: 'Слишком много запросов к очереди' });
  }

  if (req.method === 'GET') {
    const url = new URL(req.url, 'http://localhost');
    const lobbyId = (url.searchParams.get('lobbyId') || '').trim();

    if (!lobbyId) {
      return sendJson(res, 400, { error: 'missing_lobby_id', message: 'lobbyId is required' });
    }

    const key = `lobby:queue:${lobbyId}`;
    const raw = await redisGet(key);

    if (!raw) {
      const emptyState: SharedQueueState = {
        lobbyId,
        current: null,
        positionMs: 0,
        isPlaying: false,
        updatedAt: Date.now(),
        queue: [],
        skipVotes: []
      };
      return sendJson(res, 200, emptyState);
    }

    try {
      const state: SharedQueueState = JSON.parse(raw);
      // Рассчитываем актуальную позицию воспроизведения
      if (state.isPlaying && state.current) {
        const elapsed = Date.now() - state.updatedAt;
        state.positionMs += elapsed;
      }
      return sendJson(res, 200, state);
    } catch {
      return sendJson(res, 500, { error: 'parse_error', message: 'Ошибка чтения очереди из базы' });
    }
  }

  if (req.method === 'POST') {
    try {
      const body = await parseJson(req);
      const lobbyId = (body.lobbyId || '').trim();
      const action = (body.action || '').trim().toLowerCase();

      if (!lobbyId) {
        return sendJson(res, 400, { error: 'missing_lobby_id', message: 'lobbyId is required' });
      }

      if (!action) {
        return sendJson(res, 400, { error: 'missing_action', message: 'action is required' });
      }

      const key = `lobby:queue:${lobbyId}`;
      const raw = await redisGet(key);
      let state: SharedQueueState;

      if (raw) {
        try {
          state = JSON.parse(raw);
        } catch {
          state = {
            lobbyId,
            current: null,
            positionMs: 0,
            isPlaying: false,
            updatedAt: Date.now(),
            queue: [],
            skipVotes: []
          };
        }
      } else {
        state = {
          lobbyId,
          current: null,
          positionMs: 0,
          isPlaying: false,
          updatedAt: Date.now(),
          queue: [],
          skipVotes: []
        };
      }

      const userId = (body.userId || req.headers['x-user-id'] || 'anonymous').toString();
      const now = Date.now();

      switch (action) {
        case 'set_current': {
          // Хост или участник обновляет текущий трек
          state.current = body.track || null;
          state.positionMs = typeof body.positionMs === 'number' ? body.positionMs : 0;
          state.isPlaying = typeof body.isPlaying === 'boolean' ? body.isPlaying : true;
          state.updatedAt = now;
          state.skipVotes = [];
          if (body.hostId) state.hostId = body.hostId;
          break;
        }

        case 'add': {
          // Добавление трека в очередь
          if (!body.track || !body.track.title) {
            return sendJson(res, 400, { error: 'missing_track', message: 'track with title is required' });
          }
          if (state.queue.length >= 50) {
            return sendJson(res, 400, { error: 'queue_full', message: 'Очередь заполнена (максимум 50 треков)' });
          }
          const newTrack: QueueTrack = {
            id: body.track.id || `tr_${now}_${Math.random().toString(36).substring(2, 6)}`,
            title: body.track.title,
            artist: body.track.artist || '',
            art: body.track.art || '',
            duration: body.track.duration || 0,
            addedBy: body.addedBy || userId,
            addedAt: now
          };

          // Если сейчас ничего не играет — сразу запускаем добавленный трек
          if (!state.current) {
            state.current = newTrack;
            state.positionMs = 0;
            state.isPlaying = true;
            state.updatedAt = now;
            state.skipVotes = [];
          } else {
            state.queue.push(newTrack);
          }
          break;
        }

        case 'skip': {
          // Голосование за скип или мгновенный скип хостом
          const isHost = body.isHost || (state.hostId && state.hostId === userId);
          if (isHost || state.skipVotes.length >= 2) {
            // Переход к следующему треку
            state.current = state.queue.length > 0 ? state.queue.shift()! : null;
            state.positionMs = 0;
            state.isPlaying = state.current !== null;
            state.updatedAt = now;
            state.skipVotes = [];
          } else {
            if (!state.skipVotes.includes(userId)) {
              state.skipVotes.push(userId);
            }
          }
          break;
        }

        case 'remove': {
          const index = typeof body.index === 'number' ? body.index : -1;
          const trackId = body.trackId;
          if (index >= 0 && index < state.queue.length) {
            state.queue.splice(index, 1);
          } else if (trackId) {
            state.queue = state.queue.filter(t => t.id !== trackId);
          }
          break;
        }

        case 'reorder': {
          const from = body.from;
          const to = body.to;
          if (typeof from === 'number' && typeof to === 'number' &&
              from >= 0 && from < state.queue.length &&
              to >= 0 && to < state.queue.length) {
            const [item] = state.queue.splice(from, 1);
            state.queue.splice(to, 0, item);
          }
          break;
        }

        case 'clear': {
          state.queue = [];
          state.skipVotes = [];
          break;
        }

        default:
          return sendJson(res, 400, { error: 'unknown_action', message: `Unknown action: ${action}` });
      }

      await redisSet(key, JSON.stringify(state), QUEUE_TTL_SECONDS);
      return sendJson(res, 200, { ok: true, state });
    } catch (e: any) {
      return sendJson(res, 500, { error: 'server_error', message: e?.message || 'Server error' });
    }
  }

  return sendJson(res, 405, { error: 'Method Not Allowed' });
}

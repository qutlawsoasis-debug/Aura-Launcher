import test from 'node:test';
import assert from 'node:assert/strict';
import { fetchLyricsFromLrclib, LRCLIB_USER_AGENT } from '../dist/src/lrclib.js';
import handler, { normalizeLyricsCacheKey } from '../dist/api/lyrics.js';

test('normalizeLyricsCacheKey - cleans title/artist and lowercases', () => {
  const key1 = normalizeLyricsCacheKey('Queen', 'Bohemian Rhapsody');
  const key2 = normalizeLyricsCacheKey('Queen - Topic', 'Bohemian Rhapsody (Official Video)');
  assert.strictEqual(key1, 'lyrics:v1:queen|bohemian rhapsody');
  assert.strictEqual(key2, 'lyrics:v1:queen|bohemian rhapsody');
});

test('fetchLyricsFromLrclib - successful /api/get with User-Agent header', async () => {
  let capturedUrl = '';
  let capturedHeaders = null;

  const mockFetch = async (url, opts) => {
    capturedUrl = url;
    capturedHeaders = opts?.headers;
    return {
      ok: true,
      status: 200,
      json: async () => ({
        id: 101,
        trackName: 'Test Song',
        artistName: 'Test Artist',
        duration: 180,
        syncedLyrics: '[00:01.00] Hello world'
      })
    };
  };

  const res = await fetchLyricsFromLrclib('Test Artist', 'Test Song', 180, mockFetch);
  assert.ok(res);
  assert.strictEqual(res.id, 101);
  assert.ok(capturedUrl.includes('lrclib.net/api/get'));
  assert.strictEqual(capturedHeaders['User-Agent'], LRCLIB_USER_AGENT);
});

test('fetchLyricsFromLrclib - falls back to /api/search when /api/get misses (±3s match)', async () => {
  const calls = [];

  const mockFetch = async (url) => {
    calls.push(url);
    if (url.includes('/api/get')) {
      return { ok: false, status: 404 };
    }
    if (url.includes('/api/search')) {
      return {
        ok: true,
        status: 200,
        json: async () => [
          {
            id: 201,
            trackName: 'Candidate Far',
            artistName: 'Artist',
            duration: 200, // 200s != 150s (delta 50 > 3)
            syncedLyrics: '[00:01.00] Wrong track'
          },
          {
            id: 202,
            trackName: 'Candidate Match Plain',
            artistName: 'Artist',
            duration: 152, // |152 - 150| = 2 <= 3
            plainLyrics: 'Plain track lyrics'
          },
          {
            id: 203,
            trackName: 'Candidate Match Synced',
            artistName: 'Artist',
            duration: 149, // |149 - 150| = 1 <= 3, synced preferred!
            syncedLyrics: '[00:01.00] Best synced track'
          }
        ]
      };
    }
    return { ok: false, status: 500 };
  };

  const res = await fetchLyricsFromLrclib('Artist', 'Track', 150, mockFetch);
  assert.ok(res);
  assert.strictEqual(res.id, 203);
  assert.strictEqual(res.syncedLyrics, '[00:01.00] Best synced track');
  assert.ok(calls.some(u => u.includes('/api/get')));
  assert.ok(calls.some(u => u.includes('/api/search')));
});

test('fetchLyricsFromLrclib - rejects candidates outside ±3s window', async () => {
  const mockFetch = async (url) => {
    if (url.includes('/api/get')) {
      return { ok: false, status: 404 };
    }
    if (url.includes('/api/search')) {
      return {
        ok: true,
        status: 200,
        json: async () => [
          {
            id: 301,
            trackName: 'Candidate Outside Window',
            artistName: 'Artist',
            duration: 155, // |155 - 150| = 5 > 3
            syncedLyrics: '[00:01.00] Outside window'
          }
        ]
      };
    }
    return { ok: false, status: 500 };
  };

  const res = await fetchLyricsFromLrclib('Artist', 'Track', 150, mockFetch);
  assert.strictEqual(res, null);
});

test('handler - returns 400 on missing artist or title', async () => {
  let statusCode = 0;
  let jsonResult = null;
  const res = {
    setHeader: () => {},
    writeHead: (status) => { statusCode = status; },
    end: (str) => { jsonResult = JSON.parse(str); }
  };

  const req = {
    method: 'GET',
    url: '/api/lyrics?artist=&title=',
    headers: {}
  };

  await handler(req, res);
  assert.strictEqual(statusCode, 400);
  assert.strictEqual(jsonResult.error, 'missing_parameters');
});

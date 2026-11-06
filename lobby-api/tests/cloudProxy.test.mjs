import test from 'node:test';
import assert from 'node:assert/strict';
import crypto from 'node:crypto';
import { proxyToManager, uploadTicket, signUploadTicket, MAX_BODY_BYTES } from '../dist/src/cloudProxy.js';

function mockRes() {
  const r = { statusCode: 200, headers: {}, body: '' };
  r.setHeader = (k, v) => { r.headers[k.toLowerCase()] = v; };
  r.writeHead = (s, h) => { r.statusCode = s; Object.assign(r.headers, h || {}); };
  r.end = (b) => { r.body = b ?? ''; r.ended = true; };
  return r;
}

function withEnv(env, fn) {
  const old = { ...process.env };
  Object.assign(process.env, env);
  return Promise.resolve(fn()).finally(() => { process.env = old; });
}

const ENV = { CLOUD_MANAGER_URL: 'http://manager.test:25571/', CLOUD_MANAGER_SECRET: 'test-secret', CLOUD_UPLOAD_KEY: 'test-key' };

test('forwards a POST to the manager with the secret and returns its answer as is', async () => {
  const calls = [];
  const realFetch = globalThis.fetch;
  globalThis.fetch = async (url, init) => {
    calls.push({ url, init });
    return new Response('{"ok":true,"port":25601}', { status: 201, headers: { 'content-type': 'application/json' } });
  };
  try {
    await withEnv(ENV, async () => {
      const res = mockRes();
      await proxyToManager({ method: 'POST', headers: {}, body: { owner: 'Steve', worldName: 'Мир' } }, res, 'create');
      assert.equal(calls.length, 1);
      assert.equal(calls[0].url, 'http://manager.test:25571/lobbies/create');
      assert.equal(calls[0].init.headers['X-Aura-Secret'], 'test-secret');
      assert.deepEqual(JSON.parse(calls[0].init.body), { owner: 'Steve', worldName: 'Мир' });
      assert.equal(res.statusCode, 201);
      assert.equal(res.body, '{"ok":true,"port":25601}');
    });
  } finally { globalThis.fetch = realFetch; }
});

test('GET worlds keeps the player in the query', async () => {
  let seen = '';
  const realFetch = globalThis.fetch;
  globalThis.fetch = async (url) => { seen = url; return new Response('{"worlds":[]}', { status: 200 }); };
  try {
    await withEnv(ENV, async () => {
      const res = mockRes();
      await proxyToManager({ method: 'GET', headers: {}, query: { player: 'Алекс 1' } }, res, 'worlds');
      assert.equal(seen, 'http://manager.test:25571/worlds?player=' + encodeURIComponent('Алекс 1'));
      assert.equal(res.statusCode, 200);
    });
  } finally { globalThis.fetch = realFetch; }
});

test('refuses unknown actions, wrong methods, large bodies, and runs nothing without the env', async () => {
  const realFetch = globalThis.fetch;
  globalThis.fetch = async () => { throw new Error('must not be called'); };
  try {
    await withEnv(ENV, async () => {
      let res = mockRes();
      await proxyToManager({ method: 'POST', headers: {}, body: {} }, res, 'exec');
      assert.equal(res.statusCode, 404);
      res = mockRes();
      await proxyToManager({ method: 'GET', headers: {}, query: {} }, res, 'create');
      assert.equal(res.statusCode, 405);
      res = mockRes();
      await proxyToManager({ method: 'POST', headers: {}, body: 'x'.repeat(MAX_BODY_BYTES + 1) }, res, 'share');
      assert.equal(res.statusCode, 413);
    });
    await withEnv({ CLOUD_MANAGER_URL: '', CLOUD_MANAGER_SECRET: '' }, async () => {
      const res = mockRes();
      await proxyToManager({ method: 'POST', headers: {}, body: {} }, res, 'stop');
      assert.equal(res.statusCode, 503);
    });
  } finally { globalThis.fetch = realFetch; }
});

test('upload ticket is an HMAC the manager can check, and never contains the manager secret', async () => {
  await withEnv(ENV, async () => {
    const res = mockRes();
    await uploadTicket({ method: 'POST', headers: {}, body: { owner: 'Steve', worldName: 'Мой мир' } }, res);
    assert.equal(res.statusCode, 200);
    const t = JSON.parse(res.body);
    assert.equal(t.uploadUrl, 'http://manager.test:25571/worlds/upload');
    const expected = crypto.createHmac('sha256', 'test-key').update(`Steve\nМой мир\n${t.expires}`).digest('hex');
    assert.equal(t.token, expected);
    assert.equal(t.token, signUploadTicket('test-key', 'Steve', 'Мой мир', t.expires));
    assert.ok(!res.body.includes('test-secret'));
    assert.ok(t.expires > Date.now() / 1000);
  });
});

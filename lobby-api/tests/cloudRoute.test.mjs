import test from 'node:test';
import assert from 'node:assert/strict';
import http from 'node:http';

// cloud-route: on Vercel src/server.ts is the only function; /api/cloud/* must be routed by it.
process.env.NODE_ENV = 'test';

function listen(srv) {
  return new Promise((resolve) => srv.listen(0, '127.0.0.1', () => resolve(srv.address().port)));
}

test('server.ts routes /api/cloud/* to the manager proxy and the upload ticket', async (t) => {
  const seen = [];
  const manager = http.createServer((req, res) => {
    let body = '';
    req.on('data', (c) => { body += c; });
    req.on('end', () => {
      seen.push({ method: req.method, url: req.url, secret: req.headers['x-aura-secret'], body });
      res.writeHead(200, { 'Content-Type': 'application/json' });
      res.end(JSON.stringify({ ok: true, path: req.url }));
    });
  });
  const mport = await listen(manager);
  process.env.CLOUD_MANAGER_URL = `http://127.0.0.1:${mport}`;
  process.env.CLOUD_MANAGER_SECRET = 'route-secret';
  process.env.CLOUD_UPLOAD_KEY = 'route-key';

  const { server } = await import('../dist/src/server.js');
  const port = await listen(server);
  const base = `http://127.0.0.1:${port}`;
  t.after(() => { server.close(); manager.close(); });

  // GET worlds with the player in the query
  let r = await fetch(`${base}/api/cloud/worlds?player=coock0ld`);
  assert.equal(r.status, 200);
  assert.deepEqual(await r.json(), { ok: true, path: '/worlds?player=coock0ld' });

  // Each POST action reaches its manager path with the secret and the body as sent
  const routes = { create: '/lobbies/create', stop: '/lobbies/stop', share: '/lobbies/share', delete: '/worlds/delete' };
  for (const [action, path] of Object.entries(routes)) {
    r = await fetch(`${base}/api/cloud/${action}`, {
      method: 'POST', headers: { 'Content-Type': 'application/json' },
      body: JSON.stringify({ owner: 'coock0ld', worldName: 'Мир', worldId: 'w1' }),
    });
    assert.equal(r.status, 200, action);
    assert.equal((await r.json()).path, path, action);
  }
  assert.equal(seen.length, 5);
  for (const s of seen) assert.equal(s.secret, 'route-secret');
  assert.deepEqual(JSON.parse(seen[1].body), { owner: 'coock0ld', worldName: 'Мир', worldId: 'w1' });

  // Upload ticket: answered by lobby-api itself, the manager is not called
  r = await fetch(`${base}/api/cloud/upload-ticket`, {
    method: 'POST', headers: { 'Content-Type': 'application/json' },
    body: JSON.stringify({ owner: 'coock0ld', worldName: 'Мир' }),
  });
  assert.equal(r.status, 200);
  const ticket = await r.json();
  assert.equal(ticket.uploadUrl, `http://127.0.0.1:${mport}/worlds/upload`);
  assert.match(ticket.token, /^[0-9a-f]{64}$/);
  assert.equal(seen.length, 5);

  // Unknown cloud action and a trailing slash
  r = await fetch(`${base}/api/cloud/exec`, { method: 'POST', body: '{}' });
  assert.equal(r.status, 404);
  r = await fetch(`${base}/api/cloud/worlds/?player=a`);
  assert.equal(r.status, 200);

  // Other routes still answer as before
  r = await fetch(`${base}/api/nothing-here`);
  assert.equal(r.status, 404);
});

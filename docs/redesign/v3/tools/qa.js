/**
 * Визуальный QA-прогон v3 через Chrome DevTools Protocol.
 * Открывает страницу в настоящем Chrome, наводит мышь и кликает как пользователь,
 * сохраняет скриншоты и серии кадров анимаций, собирает ошибки консоли.
 *
 * node tools/qa.js <url> <папка-для-кадров> [сценарий.js]
 * Сценарий — модуль, экспортирующий async (qa) => {}; по умолчанию tools/qa-walkthrough.js.
 */

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const PORT = 9666;

async function launch(width, height) {
  const profile = path.join(__dirname, '..', '.chrome_qa_profile');
  const proc = spawn(CHROME, [
    '--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`,
    `--window-size=${width},${height}`, '--no-first-run', '--hide-scrollbars',
    '--use-angle=swiftshader', '--enable-unsafe-swiftshader', 'about:blank'
  ]);
  for (let i = 0; i < 50; i++) {
    await new Promise(r => setTimeout(r, 200));
    try {
      const res = await fetch(`http://127.0.0.1:${PORT}/json/version`);
      if (res.ok) return { proc, wsUrl: (await res.json()).webSocketDebuggerUrl };
    } catch (e) { /* Chrome ещё стартует */ }
  }
  proc.kill();
  throw new Error('Chrome не поднял CDP');
}

async function connect(wsUrl) {
  const ws = new WebSocket(wsUrl);
  let nextId = 1;
  const pending = new Map();
  const listeners = [];
  ws.onmessage = e => {
    const msg = JSON.parse(e.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      msg.error ? reject(new Error(msg.error.message)) : resolve(msg.result);
    } else {
      listeners.forEach(fn => fn(msg));
    }
  };
  await new Promise(r => (ws.onopen = r));
  const call = (method, params = {}, sessionId) => new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params, sessionId }));
  });
  return { ws, call, listeners };
}

async function main() {
  const [url, outDir, scenarioPath] = process.argv.slice(2);
  if (!url || !outDir) {
    console.error('node tools/qa.js <url> <out-dir> [scenario.js]');
    process.exit(2);
  }
  fs.mkdirSync(outDir, { recursive: true });
  const width = 1366, height = 768;
  const { proc, wsUrl } = await launch(width, height);
  const { ws, call, listeners } = await connect(wsUrl);

  const { targetId } = await call('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await call('Target.attachToTarget', { targetId, flatten: true });
  const s = (m, p) => call(m, p, sessionId);

  const errors = [];
  listeners.push(msg => {
    if (msg.sessionId !== sessionId) return;
    if (msg.method === 'Runtime.exceptionThrown') errors.push(msg.params.exceptionDetails.exception?.description || msg.params.exceptionDetails.text);
    if (msg.method === 'Runtime.consoleAPICalled' && msg.params.type === 'error') errors.push(msg.params.args.map(a => a.value ?? a.description).join(' '));
    if (msg.method === 'Network.responseReceived' && msg.params.response.status >= 400) errors.push(`HTTP ${msg.params.response.status} ${msg.params.response.url}`);
  });

  await s('Runtime.enable');
  await s('Network.enable');
  await s('Page.enable');
  await s('Network.setCacheDisabled', { cacheDisabled: true });
  await s('Emulation.setDeviceMetricsOverride', { width, height, deviceScaleFactor: 1, mobile: false });

  const wait = ms => new Promise(r => setTimeout(r, ms));
  const evaluate = async expr => {
    const r = await s('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };
  const center = async selector => {
    const box = await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) return null;
      const r = el.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width, h: r.height }; })()`);
    if (!box || box.w === 0) throw new Error(`Не найден или не виден: ${selector}`);
    return box;
  };
  const mouse = (type, x, y) => s('Input.dispatchMouseEvent', { type, x, y, button: type === 'mouseMoved' ? 'none' : 'left', clickCount: 1 });

  let shotNo = 0;
  const qa = {
    wait, evaluate, errors,
    async goto(u) { await s('Page.navigate', { url: u }); await wait(1500); },
    async hover(selector) { const b = await center(selector); await mouse('mouseMoved', b.x, b.y); },
    async hoverAt(x, y) { await mouse('mouseMoved', x, y); },
    async click(selector) {
      const b = await center(selector);
      await mouse('mouseMoved', b.x, b.y);
      await mouse('mousePressed', b.x, b.y);
      await mouse('mouseReleased', b.x, b.y);
    },
    async key(key) {
      await s('Input.dispatchKeyEvent', { type: 'keyDown', key, code: key });
      await s('Input.dispatchKeyEvent', { type: 'keyUp', key, code: key });
    },
    async shot(name, clip) {
      const file = path.join(outDir, `${String(++shotNo).padStart(2, '0')}-${name}.png`);
      const params = { format: 'png' };
      if (clip) params.clip = { ...clip, scale: 1 };
      const { data } = await s('Page.captureScreenshot', params);
      fs.writeFileSync(file, Buffer.from(data, 'base64'));
      return file;
    },
    // Серия кадров во время анимации: действие запускается, кадры пишутся с шагом stepMs
    async frames(name, action, count = 6, stepMs = 90, clip) {
      const files = [];
      const run = action();
      for (let i = 0; i < count; i++) {
        files.push(await qa.shot(`${name}-f${i}`, clip));
        await wait(stepMs);
      }
      await run;
      return files;
    }
  };

  const scenario = require(path.resolve(scenarioPath || path.join(__dirname, 'qa-walkthrough.js')));
  try {
    await qa.goto(url);
    await scenario(qa);
  } finally {
    console.log(`\nКадров: ${shotNo} → ${outDir}`);
    console.log(errors.length ? `Ошибки (${errors.length}):\n  ${errors.join('\n  ')}` : 'Ошибок консоли и сети нет');
    ws.close();
    proc.kill();
  }
}

main().catch(e => { console.error(e); process.exit(1); });

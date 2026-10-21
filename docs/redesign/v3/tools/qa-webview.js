/**
 * Подключение к реальному окну лаунчера (WebView2) по CDP.
 * Лаунчер запускается с WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS=--remote-debugging-port=<порт> и --webui.
 *
 * node tools/qa-webview.js <порт> <папка-для-кадров> <сценарий.js>
 * Сценарию передаётся тот же объект qa, что и в tools/qa.js (evaluate, click, shot, wait…).
 */

const fs = require('fs');
const path = require('path');

async function main() {
  const [port, outDir, scenarioPath] = process.argv.slice(2);
  fs.mkdirSync(outDir, { recursive: true });

  let page = null;
  for (let i = 0; i < 60 && !page; i++) {
    try {
      const list = await (await fetch(`http://127.0.0.1:${port}/json`)).json();
      page = list.find(t => t.type === 'page' && /aura\.local|localhost:5173/.test(t.url));
    } catch (e) { /* окно ещё поднимается */ }
    if (!page) await new Promise(r => setTimeout(r, 500));
  }
  if (!page) throw new Error('WebView2 с интерфейсом не найден');

  const ws = new WebSocket(page.webSocketDebuggerUrl);
  let nextId = 1;
  const pending = new Map();
  const errors = [];
  ws.onmessage = e => {
    const msg = JSON.parse(e.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      msg.error ? reject(new Error(msg.error.message)) : resolve(msg.result);
    } else if (msg.method === 'Runtime.exceptionThrown') {
      errors.push(msg.params.exceptionDetails.exception?.description || msg.params.exceptionDetails.text);
    } else if (msg.method === 'Runtime.consoleAPICalled' && msg.params.type === 'error') {
      errors.push(msg.params.args.map(a => a.value ?? a.description).join(' '));
    }
  };
  await new Promise(r => (ws.onopen = r));
  const s = (method, params = {}) => new Promise((resolve, reject) => {
    const id = nextId++;
    pending.set(id, { resolve, reject });
    ws.send(JSON.stringify({ id, method, params }));
  });
  await s('Runtime.enable');
  await s('Page.enable');

  const wait = ms => new Promise(r => setTimeout(r, ms));
  const evaluate = async expr => {
    const r = await s('Runtime.evaluate', { expression: expr, returnByValue: true, awaitPromise: true });
    if (r.exceptionDetails) throw new Error(r.exceptionDetails.exception?.description || r.exceptionDetails.text);
    return r.result.value;
  };
  const center = async selector => {
    const b = await evaluate(`(() => { const el = document.querySelector(${JSON.stringify(selector)}); if (!el) return null;
      const r = el.getBoundingClientRect(); return { x: r.left + r.width / 2, y: r.top + r.height / 2, w: r.width }; })()`);
    if (!b || !b.w) throw new Error(`Не найден: ${selector}`);
    return b;
  };
  const mouse = (type, x, y) => s('Input.dispatchMouseEvent', { type, x, y, button: type === 'mouseMoved' ? 'none' : 'left', clickCount: 1 });
  let n = 0;
  const qa = {
    wait, evaluate, errors,
    async hover(sel) { const b = await center(sel); await mouse('mouseMoved', b.x, b.y); },
    async click(sel) { const b = await center(sel); await mouse('mouseMoved', b.x, b.y); await mouse('mousePressed', b.x, b.y); await mouse('mouseReleased', b.x, b.y); },
    async key(key) { await s('Input.dispatchKeyEvent', { type: 'keyDown', key, code: key }); await s('Input.dispatchKeyEvent', { type: 'keyUp', key, code: key }); },
    async shot(name) {
      const { data } = await s('Page.captureScreenshot', { format: 'png' });
      const file = path.join(outDir, `${String(++n).padStart(2, '0')}-${name}.png`);
      fs.writeFileSync(file, Buffer.from(data, 'base64'));
      return file;
    }
  };

  try {
    await require(path.resolve(scenarioPath))(qa);
  } finally {
    console.log(errors.length ? `Ошибки JS (${errors.length}):\n  ${errors.join('\n  ')}` : 'Ошибок JS нет');
    ws.close();
  }
}

main().catch(e => { console.error(e.message); process.exit(1); });

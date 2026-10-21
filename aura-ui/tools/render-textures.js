/**
 * Рендер текстур экрана загрузки из tools/textures.html в src/main/resources/assets/aura-ui/textures/gui/.
 * Headless Chrome, масштаб ×2, прозрачный фон. Фон мира — копия bg_1-blur.jpg лаунчера, пересжатая в PNG.
 *
 * node tools/render-textures.js
 */
const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const PORT = 9677;
const ROOT = path.resolve(__dirname, '..');
const OUT = path.join(ROOT, 'src', 'main', 'resources', 'assets', 'aura-ui', 'textures', 'gui');
const SCALE = 2;
const SHOTS = ['capsule', 'ring', 'glow', 'shadow'];

async function main() {
  fs.mkdirSync(OUT, { recursive: true });
  const profile = path.join(require('os').tmpdir(), 'aura-ui-textures-chrome');
  const chrome = spawn(CHROME, ['--headless=new', `--remote-debugging-port=${PORT}`, `--user-data-dir=${profile}`,
    '--allow-file-access-from-files', '--hide-scrollbars', 'about:blank']);
  try {
    let wsUrl = null;
    for (let i = 0; i < 50 && !wsUrl; i++) {
      await new Promise(r => setTimeout(r, 200));
      try {
        const list = await (await fetch(`http://127.0.0.1:${PORT}/json`)).json();
        wsUrl = (list.find(t => t.type === 'page') || {}).webSocketDebuggerUrl;
      } catch (e) { /* ещё стартует */ }
    }
    if (!wsUrl) throw new Error('Chrome не поднялся');

    const ws = new WebSocket(wsUrl);
    let id = 0;
    const pending = new Map();
    ws.onmessage = e => {
      const m = JSON.parse(e.data);
      if (m.id && pending.has(m.id)) { pending.get(m.id)(m.result); pending.delete(m.id); }
    };
    await new Promise(r => (ws.onopen = r));
    const send = (method, params = {}) => new Promise(r => { const i = ++id; pending.set(i, r); ws.send(JSON.stringify({ id: i, method, params })); });

    await send('Page.enable');
    await send('Emulation.setDeviceMetricsOverride', { width: 800, height: 700, deviceScaleFactor: SCALE, mobile: false });
    await send('Emulation.setDefaultBackgroundColorOverride', { color: { r: 0, g: 0, b: 0, a: 0 } });
    await send('Page.navigate', { url: 'file:///' + path.join(__dirname, 'textures.html').replace(/\\/g, '/') });
    await new Promise(r => setTimeout(r, 1500));

    for (const name of SHOTS) {
      const { result } = await send('Runtime.evaluate', {
        expression: `JSON.stringify((r => ({ x: r.left, y: r.top, w: r.width, h: r.height }))(document.getElementById('${name}').getBoundingClientRect()))`,
        returnByValue: true
      });
      const b = JSON.parse(result.value);
      const { data } = await send('Page.captureScreenshot', { format: 'png', clip: { x: b.x, y: b.y, width: b.w, height: b.h, scale: 1 } });
      fs.writeFileSync(path.join(OUT, `${name}.png`), Buffer.from(data, 'base64'));
      console.log(`${name}.png  ${b.w * SCALE}×${b.h * SCALE}`);
    }

    // Фон мира: размытая копия bg_1, перекодированная в PNG через canvas
    const bgPath = path.resolve(ROOT, '..', 'docs', 'redesign', 'v3', 'public', 'assets', 'images', 'bg_1-blur.jpg');
    const bgB64 = fs.readFileSync(bgPath).toString('base64');
    const { result: bg } = await send('Runtime.evaluate', {
      expression: `new Promise(res => { const img = new Image(); img.onload = () => { const c = document.createElement('canvas');
        c.width = 1280; c.height = 720; c.getContext('2d').drawImage(img, 0, 0, 1280, 720); res(c.toDataURL('image/png').split(',')[1]); };
        img.src = 'data:image/jpeg;base64,${bgB64}'; })`,
      awaitPromise: true, returnByValue: true
    });
    fs.writeFileSync(path.join(OUT, 'background.png'), Buffer.from(bg.value, 'base64'));
    console.log('background.png  1280×720');
    ws.close();
  } finally {
    chrome.kill();
  }
}

main().catch(e => { console.error(e.message); process.exit(1); });

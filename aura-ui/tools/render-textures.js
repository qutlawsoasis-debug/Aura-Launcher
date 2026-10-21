const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const CHROME = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const PORT = 9677;
const ROOT = path.resolve(__dirname, '..');
const OUT = path.join(ROOT, 'src', 'main', 'resources', 'assets', 'aura-ui', 'textures', 'gui');
const SCALE = 2;
const SHOTS = ['wordmark', 'wordmark_mask', 'subtitle', 'label', 'tip1', 'tip2', 'tip3', 'tip4', 'vignette'];

async function main() {
  fs.mkdirSync(OUT, { recursive: true });

  for (const old of ['capsule.png', 'ring.png', 'glow.png', 'shadow.png', 'shine.png']) {
    const p = path.join(OUT, old);
    if (fs.existsSync(p)) fs.unlinkSync(p);
  }

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
      } catch (e) {}
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
    await send('Emulation.setDeviceMetricsOverride', { width: 1200, height: 1200, deviceScaleFactor: SCALE, mobile: false });
    await send('Emulation.setDefaultBackgroundColorOverride', { color: { r: 0, g: 0, b: 0, a: 0 } });
    await send('Page.navigate', { url: 'file:///' + path.join(__dirname, 'textures.html').replace(/\\/g, '/') });
    await new Promise(r => setTimeout(r, 2000));

    for (const name of SHOTS) {
      const { result } = await send('Runtime.evaluate', {
        expression: `JSON.stringify((r => ({ x: r.left, y: r.top, w: r.width, h: r.height }))(document.getElementById('${name}').getBoundingClientRect()))`,
        returnByValue: true
      });
      const b = JSON.parse(result.value);
      const { data } = await send('Page.captureScreenshot', { format: 'png', clip: { x: b.x, y: b.y, width: b.w, height: b.h, scale: 1 } });
      fs.writeFileSync(path.join(OUT, `${name}.png`), Buffer.from(data, 'base64'));
      console.log(`${name}.png  ${Math.round(b.w * SCALE)}×${Math.round(b.h * SCALE)}`);
    }

    // Резкий фон мира bg_1.jpg: открываем прямо в Chrome и снимаем скриншот 1920×1080
    const bgPath = path.resolve(ROOT, '..', 'docs', 'redesign', 'v3', 'public', 'assets', 'images', 'bg_1.jpg');
    await send('Emulation.setDeviceMetricsOverride', { width: 1920, height: 1080, deviceScaleFactor: 1, mobile: false });
    await send('Page.navigate', { url: 'file:///' + bgPath.replace(/\\/g, '/') });
    await new Promise(r => setTimeout(r, 1000));
    const { data: bgData } = await send('Page.captureScreenshot', { format: 'png', clip: { x: 0, y: 0, width: 1920, height: 1080, scale: 1 } });
    fs.writeFileSync(path.join(OUT, 'background.png'), Buffer.from(bgData, 'base64'));
    console.log('background.png  1920×1080');

    ws.close();
  } finally {
    chrome.kill();
  }
}

main().catch(e => { console.error(e.message); process.exit(1); });

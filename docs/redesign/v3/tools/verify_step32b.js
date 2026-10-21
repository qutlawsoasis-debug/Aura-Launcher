const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const TEMP_USER_DATA = path.join(__dirname, '..', '.chrome_audit_step32b');
const OUT_DIR = path.join(__dirname, '..', 'screenshots');

if (!fs.existsSync(OUT_DIR)) {
  fs.mkdirSync(OUT_DIR, { recursive: true });
}

async function run() {
  // Check CSS backdrop-filter count
  const screens = ['wardrobe.css', 'workshop.css', 'settings.css', 'map.css', 'lobby.css', 'friends.css', 'play.css'];
  let totalFilters = 0;
  for (const s of screens) {
    const content = fs.readFileSync(path.join(__dirname, '..', 'screens', s), 'utf-8');
    const matches = content.match(/backdrop-filter/gi);
    if (matches) {
      console.log(`⚠️ ${s} has ${matches.length} backdrop-filter instances`);
      totalFilters += matches.length;
    }
  }
  console.log(`Total backdrop-filter in screens/*.css: ${totalFilters}`);

  const chrome = spawn(CHROME_PATH, [
    '--headless=new',
    '--remote-debugging-port=9455',
    `--user-data-dir=${TEMP_USER_DATA}`,
    '--window-size=1366,768',
    '--disable-gpu',
    '--no-first-run',
    'about:blank'
  ]);

  let wsUrl = null;
  for (let i = 0; i < 40; i++) {
    await new Promise(r => setTimeout(r, 200));
    try {
      const res = await fetch('http://127.0.0.1:9455/json/version');
      if (res.ok) {
        const data = await res.json();
        wsUrl = data.webSocketDebuggerUrl;
        break;
      }
    } catch (e) {}
  }

  if (!wsUrl) {
    console.error('Failed to get CDP');
    chrome.kill();
    process.exit(1);
  }

  const ws = new WebSocket(wsUrl);
  let id = 1;
  const pending = new Map();

  ws.onmessage = (event) => {
    const msg = JSON.parse(event.data);
    if (msg.id && pending.has(msg.id)) {
      const { resolve, reject } = pending.get(msg.id);
      pending.delete(msg.id);
      if (msg.error) reject(msg.error);
      else resolve(msg.result);
    }
  };

  await new Promise(r => ws.onopen = r);

  function send(method, params = {}) {
    return new Promise((resolve, reject) => {
      const reqId = id++;
      pending.set(reqId, { resolve, reject });
      ws.send(JSON.stringify({ id: reqId, method, params }));
    });
  }

  const { targetId } = await send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await send('Target.attachToTarget', { targetId, flatten: true });

  function sendSession(method, params = {}) {
    return new Promise((resolve, reject) => {
      const reqId = id++;
      pending.set(reqId, { resolve, reject });
      ws.send(JSON.stringify({ id: reqId, sessionId, method, params }));
    });
  }

  const networkErrors = [];
  const consoleErrors = [];

  await sendSession('Network.enable');
  await sendSession('Runtime.enable');
  await sendSession('Page.enable');
  await sendSession('Emulation.setDeviceMetricsOverride', {
    width: 1366,
    height: 768,
    deviceScaleFactor: 1,
    mobile: false
  });

  // Listen to network & console via standard session if possible or evaluate
  await sendSession('Page.navigate', { url: 'http://localhost:8080/docs/redesign/v3/index.html' });
  await new Promise(r => setTimeout(r, 1500));

  async function capture(name) {
    const scr = await sendSession('Page.captureScreenshot', { format: 'png' });
    const buf = Buffer.from(scr.data, 'base64');
    const p = path.join(OUT_DIR, name);
    fs.writeFileSync(p, buf);
    console.log(`Saved screenshot: ${p}`);
  }

  // Check errors from window
  const errors = await sendSession('Runtime.evaluate', {
    expression: 'window.__errors || []',
    returnByValue: true
  });

  console.log('--- Checking Idle View ---');
  await capture('step32b-idle-1366x768.png');

  console.log('--- Opening Wardrobe ---');
  await sendSession('Runtime.evaluate', {
    expression: 'window.setIslandMode("wardrobe")',
  });
  await new Promise(r => setTimeout(r, 1000));
  await capture('step32b-wardrobe-1366x768.png');

  const wardrobeStatus = await sendSession('Runtime.evaluate', {
    expression: `({
      hasViewer: !!window.AuraWardrobe && !!window.AuraWardrobe.viewer,
      isPaused: window.AuraWardrobe && window.AuraWardrobe.viewer ? window.AuraWardrobe.viewer.renderPaused : null
    })`,
    returnByValue: true
  });
  console.log('Wardrobe open status:', wardrobeStatus.result.value);

  console.log('--- Opening Workshop ---');
  await sendSession('Runtime.evaluate', {
    expression: 'window.setIslandMode("workshop")',
  });
  await new Promise(r => setTimeout(r, 600));
  await capture('step32b-workshop-1366x768.png');

  console.log('--- Opening Settings ---');
  await sendSession('Runtime.evaluate', {
    expression: 'window.setIslandMode("settings")',
  });
  await new Promise(r => setTimeout(r, 600));
  await capture('step32b-settings-1366x768.png');

  console.log('--- Opening Map ---');
  await sendSession('Runtime.evaluate', {
    expression: 'window.setIslandMode("map")',
  });
  await new Promise(r => setTimeout(r, 800));
  await capture('step32b-map-1366x768.png');

  console.log('--- Closing section back to idle ---');
  await sendSession('Runtime.evaluate', {
    expression: 'window.setIslandMode("idle")',
  });
  await new Promise(r => setTimeout(r, 500));

  const wardrobeClosedStatus = await sendSession('Runtime.evaluate', {
    expression: `({
      isPaused: window.AuraWardrobe && window.AuraWardrobe.viewer ? window.AuraWardrobe.viewer.renderPaused : null
    })`,
    returnByValue: true
  });
  console.log('Wardrobe closed renderPaused status:', wardrobeClosedStatus.result.value);

  // Check backdrop filter on DOM
  const domBlurCount = await sendSession('Runtime.evaluate', {
    expression: `
      Array.from(document.querySelectorAll('*')).filter(el => {
        const style = window.getComputedStyle(el);
        return style.backdropFilter && style.backdropFilter !== 'none';
      }).map(el => el.id || el.className)
    `,
    returnByValue: true
  });
  console.log('DOM elements with backdropFilter:', domBlurCount.result.value);

  ws.close();
  chrome.kill();
  try { fs.rmSync(TEMP_USER_DATA, { recursive: true, force: true }); } catch (e) {}
}

run().catch(e => {
  console.error(e);
  process.exit(1);
});

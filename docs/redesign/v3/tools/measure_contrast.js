/**
 * Aura Launcher v3 — Comprehensive Contrast & Tone Verification Script
 * Measures contrast of text elements directly on world background and inside glass panels
 * for both Dark and Light background modes across all 6 screens at 1920x1080.
 */

const { spawn } = require('child_process');
const path = require('path');

const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const TEMP_USER_DATA = path.join(__dirname, '.chrome_verify_contrast_v2');

async function main() {
  const chrome = spawn(CHROME_PATH, [
    '--headless=new',
    '--remote-debugging-port=9888',
    `--user-data-dir=${TEMP_USER_DATA}`,
    '--disable-gpu',
    'about:blank'
  ]);

  let wsUrl = null;
  for (let i = 0; i < 30; i++) {
    await new Promise(r => setTimeout(r, 200));
    try {
      const res = await fetch('http://127.0.0.1:9888/json/version');
      if (res.ok) {
        const d = await res.json();
        wsUrl = d.webSocketDebuggerUrl;
        break;
      }
    } catch (e) {}
  }

  if (!wsUrl) {
    chrome.kill();
    throw new Error('Failed to connect to chrome on 9888');
  }

  const ws = new WebSocket(wsUrl);
  let id = 1;
  const pending = new Map();
  ws.onmessage = e => {
    const d = JSON.parse(e.data);
    if (d.id && pending.has(d.id)) {
      const { resolve } = pending.get(d.id);
      pending.delete(d.id);
      resolve(d.result);
    }
  };

  const send = (m, p = {}) => new Promise(res => {
    const reqId = id++;
    pending.set(reqId, { resolve: res });
    ws.send(JSON.stringify({ id: reqId, method: m, params: p }));
  });

  await new Promise(r => { ws.onopen = r; });

  const { targetId } = await send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await send('Target.attachToTarget', { targetId, flatten: true });
  const sendSession = (method, params = {}) => new Promise(res => {
    const reqId = id++;
    pending.set(reqId, { resolve: res });
    ws.send(JSON.stringify({ id: reqId, sessionId, method, params }));
  });

  const logs = [];
  const errors = [];
  ws.addEventListener('message', e => {
    const d = JSON.parse(e.data);
    if (d.method === 'Console.messageAdded' && d.params.message.level === 'error') {
      logs.push(d.params.message);
    }
    if (d.method === 'Runtime.exceptionThrown') {
      errors.push(d.params.exceptionDetails);
    }
  });

  await sendSession('Page.enable');
  await sendSession('Runtime.enable');

  await sendSession('Emulation.setDeviceMetricsOverride', {
    width: 1920,
    height: 1080,
    deviceScaleFactor: 1,
    mobile: false
  });

  const screens = ['play', 'lobby', 'friends', 'wardrobe', 'workshop', 'settings'];
  const testOutput = [];

  for (const bgMode of ['dark', 'light']) {
    const url = 'http://localhost:8080/docs/redesign/v3/index.html';
    await sendSession('Page.navigate', { url });
    await new Promise(r => setTimeout(r, 1200));

    if (bgMode === 'light') {
      // 1x1 white png data url
      const whitePngData = 'data:image/png;base64,iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8/5+hHgAHggJ/PchI7wAAAABJRU5ErkJggg==';
      await sendSession('Runtime.evaluate', {
        expression: `
          window.setWorldBackground("${whitePngData}");
        `
      });
      await new Promise(r => setTimeout(r, 500));
    }

    for (const s of screens) {
      await sendSession('Runtime.evaluate', { expression: `location.hash = "#/${s}"` });
      await new Promise(r => setTimeout(r, 400));

      const evalRes = await sendSession('Runtime.evaluate', {
        expression: `(() => {
          function getLum(r, g, b) {
            const a = [r, g, b].map(v => {
              v /= 255;
              return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
            });
            return a[0] * 0.2126 + a[1] * 0.7152 + a[2] * 0.0722;
          }

          function getContrast(rgb1, rgb2) {
            const l1 = getLum(rgb1[0], rgb1[1], rgb1[2]);
            const l2 = getLum(rgb2[0], rgb2[1], rgb2[2]);
            const lighter = Math.max(l1, l2);
            const darker = Math.min(l1, l2);
            return (lighter + 0.05) / (darker + 0.05);
          }

          function parseColor(str) {
            const m = str.match(/rgba?\\((\\d+),\\s*(\\d+),\\s*(\\d+)(?:,\\s*([\\d.]+))?\\)/);
            if (!m) return { r: 255, g: 255, b: 255, a: 1 };
            return {
              r: parseInt(m[1]),
              g: parseInt(m[2]),
              b: parseInt(m[3]),
              a: m[4] !== undefined ? parseFloat(m[4]) : 1
            };
          }

          function blend(fg, bg) {
            const a = fg.a;
            return [
              Math.round(fg.r * a + bg[0] * (1 - a)),
              Math.round(fg.g * a + bg[1] * (1 - a)),
              Math.round(fg.b * a + bg[2] * (1 - a))
            ];
          }

          const baseWorldBg = '${bgMode}' === 'light' ? [255, 255, 255] : [5, 11, 15];

          const targets = [
            { label: 'Header .logo-aura', selector: '.logo-aura' },
            { label: 'Nav .nav-pill-dock', selector: '.nav-pill-dock' },
            { label: 'Heading (world)', selector: '.page-title-unified, .lobby-main-heading, .friends-main-heading, .wardrobe-main-heading, .hero-tag' },
            { label: 'Subheading (world)', selector: '.page-subtitle-unified, .lobby-subheading, .friends-subheading, .wardrobe-subheading, .hero-spec' },
            { label: 'Tunnel Card (tunnel-status-card)', selector: '.tunnel-status-card' },
            { label: 'Main Glass Island (.glass-island)', selector: '#screen-${s} .glass-island' }
          ];

          const measurements = [];

          targets.forEach(t => {
            const el = document.querySelector(t.selector);
            if (!el) return;
            const cs = window.getComputedStyle(el);
            const textColor = parseColor(cs.color);

            // Determine effective background under text
            // Walk up parents to composite background-colors
            let cur = el;
            let layers = [];
            while (cur && cur !== document.documentElement) {
              const bg = window.getComputedStyle(cur).backgroundColor;
              if (bg && bg !== 'rgba(0, 0, 0, 0)' && bg !== 'transparent') {
                layers.unshift(parseColor(bg));
              }
              cur = cur.parentElement;
            }

            let compBg = baseWorldBg;
            layers.forEach(l => {
              compBg = blend(l, compBg);
            });

            // Also blend text color if alpha < 1
            let effectiveFg = [textColor.r, textColor.g, textColor.b];
            if (textColor.a < 1) {
              effectiveFg = blend(textColor, compBg);
            }

            const contrast = getContrast(effectiveFg, compBg);
            measurements.push({
              element: t.label,
              textColorStr: cs.color,
              computedBgStr: \`rgb(\${compBg.join(', ')})\`,
              contrast: Math.round(contrast * 100) / 100
            });
          });

          return {
            screen: '${s}',
            bgMode: '${bgMode}',
            htmlBgTone: document.documentElement.getAttribute('data-bg-tone'),
            measurements
          };
        })()`,
        returnByValue: true
      });

      testOutput.push(evalRes.result.value);
    }
  }

  console.log('MEASUREMENTS_JSON=' + JSON.stringify(testOutput, null, 2));
  console.log('CONSOLE_ERRORS=' + errors.length);

  ws.close();
  chrome.kill();
  process.exit(0);
}

main().catch(err => {
  console.error(err);
  process.exit(1);
});

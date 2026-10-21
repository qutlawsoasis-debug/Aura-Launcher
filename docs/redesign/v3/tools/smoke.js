/**
 * Smoke Test Suite for Aura Launcher v3 (index.html & aura-v3-standalone.html)
 * Tests at 1366x768 across effects modes (full & off).
 */

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const TEMP_USER_DATA = path.join(__dirname, '..', '.chrome_smoke_profile');
const BASE_URL = 'http://localhost:8080/docs/redesign/v3';

const targets = [
  { name: 'index.html (effects=full)', url: `${BASE_URL}/index.html?effects=full`, effects: 'full' },
  { name: 'index.html (effects=off)', url: `${BASE_URL}/index.html?effects=off`, effects: 'off' },
  { name: 'aura-v3-standalone.html (effects=full)', url: `${BASE_URL}/aura-v3-standalone.html?effects=full`, effects: 'full' },
  { name: 'aura-v3-standalone.html (effects=off)', url: `${BASE_URL}/aura-v3-standalone.html?effects=off`, effects: 'off' }
];

const SECTIONS = ['map', 'lobby', 'friends', 'wardrobe', 'workshop', 'settings'];

async function runSmokeForTarget(target) {
  const result = {
    target: target.name,
    consoleErrors: [],
    network404s: [],
    sectionsVisited: 0,
    buttonsClicked: 0,
    overlappedElements: 0,
    buttonsWithoutEffectOrCmd: 0,
    backdropLayersOk: false,
    mapCanvasSizeOk: false,
    skinview3dOk: false,
    playRunningOk: false,
    passed: false
  };

  const chrome = spawn(CHROME_PATH, [
    '--headless=new',
    '--remote-debugging-port=9555',
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
      const res = await fetch('http://127.0.0.1:9555/json/version');
      if (res.ok) {
        const data = await res.json();
        wsUrl = data.webSocketDebuggerUrl;
        break;
      }
    } catch (e) {}
  }

  if (!wsUrl) {
    chrome.kill();
    throw new Error('Failed to launch Chrome with CDP');
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

  // Network and console tracking
  await sendSession('Network.enable');
  await sendSession('Runtime.enable');
  await sendSession('Page.enable');
  await sendSession('Emulation.setDeviceMetricsOverride', {
    width: 1366,
    height: 768,
    deviceScaleFactor: 1,
    mobile: false
  });

  const sessionEventsHandler = (event) => {
    try {
      const msg = JSON.parse(event.data);
      if (msg.sessionId !== sessionId) return;
      if (msg.method === 'Runtime.consoleAPICalled') {
        if (msg.params.type === 'error') {
          result.consoleErrors.push(msg.params.args.map(a => a.value || a.description).join(' '));
        }
      } else if (msg.method === 'Runtime.exceptionThrown') {
        result.consoleErrors.push(msg.params.exceptionDetails.text + ' ' + (msg.params.exceptionDetails.exception?.description || ''));
      } else if (msg.method === 'Network.responseReceived') {
        if (msg.params.response.status === 404) {
          result.network404s.push(msg.params.response.url);
        }
      }
    } catch (e) {}
  };

  ws.addEventListener('message', sessionEventsHandler);

  await sendSession('Page.navigate', { url: target.url });
  await new Promise(r => setTimeout(r, 1200));

  // 1. Verify skinview3d object
  const skinRes = await sendSession('Runtime.evaluate', {
    expression: 'typeof skinview3d',
    returnByValue: true
  });
  result.skinview3dOk = (skinRes.result.value === 'object');

  // 2. Verify backdrop filter count
  const filterRes = await sendSession('Runtime.evaluate', {
    expression: `
      Array.from(document.querySelectorAll('*')).filter(el => {
        const s = window.getComputedStyle(el);
        return s.backdropFilter && s.backdropFilter !== 'none';
      }).length
    `,
    returnByValue: true
  });
  const filterCount = filterRes.result.value || 0;
  result.backdropLayersOk = (target.effects === 'off' ? filterCount === 0 : filterCount <= 1);

  // 3. Test sections iteration & buttons
  for (const sec of SECTIONS) {
    await sendSession('Runtime.evaluate', {
      expression: `window.setIslandMode("${sec}")`
    });
    // Wait for island transition to finish in full effects (0.75s transition)
    await new Promise(r => setTimeout(r, target.effects === 'off' ? 150 : 800));
    result.sectionsVisited++;

    // If map, check canvas size
    if (sec === 'map') {
      const mapCheck = await sendSession('Runtime.evaluate', {
        expression: `(() => {
          const cv = document.getElementById('mapCanvas');
          if (!cv) return null;
          const rect = cv.getBoundingClientRect();
          const dpr = window.devicePixelRatio || 1;
          return {
            cssWidth: Math.round(rect.width),
            cssHeight: Math.round(rect.height),
            bufWidth: cv.width,
            bufHeight: cv.height,
            isMatch: cv.width > 300 && cv.height > 200 && cv.width === Math.round(rect.width * dpr)
          };
        })()`,
        returnByValue: true
      });
      result.mapCanvasSizeOk = !!mapCheck.result.value?.isMatch;
    }

    // Inspect elements inside active .sec-body
    const inspectRes = await sendSession('Runtime.evaluate', {
      expression: `(() => {
        const body = document.querySelector('[data-l="${sec}"] .sec-body');
        if (!body) return { count: 0, overlapped: 0, badButtons: 0 };
        const clickable = Array.from(body.querySelectorAll('button, input, .toggle, .pick'));
        let overlapped = 0;
        let badButtons = 0;

        for (const el of clickable) {
          // If element is inside an un-opened reveal, skip bounds check
          const closedReveal = el.closest('.reveal:not(.open)');
          if (closedReveal) continue;

          const rect = el.getBoundingClientRect();
          if (rect.width === 0 || rect.height === 0 || el.hidden || window.getComputedStyle(el).display === 'none') {
            continue;
          }

          // Check if element is within body scroll viewport
          const bodyRect = body.getBoundingClientRect();
          const isVerticallyVisible = (rect.bottom > bodyRect.top && rect.top < bodyRect.bottom);
          if (isVerticallyVisible) {
            const cx = Math.max(rect.left + 5, Math.min(rect.right - 5, rect.left + rect.width / 2));
            const cy = Math.max(rect.top + 5, Math.min(rect.bottom - 5, rect.top + rect.height / 2));
            const topEl = document.elementFromPoint(cx, cy);
            if (!topEl || (!el.contains(topEl) && !topEl.contains(el))) {
              overlapped++;
            }
          }

          // Check if button has handler / data-cmd
          const hasKnownDataset = Boolean(
            el.dataset.cmd || el.dataset.open || el.dataset.play || el.dataset.anim ||
            el.dataset.model || el.dataset.cape || el.dataset.tab || el.dataset.presetIdx ||
            el.dataset.fx || ('copy' in el.dataset) || el.dataset.reveal || el.dataset.poi
          );
          const hasKnownClass = (
            el.classList.contains('pick') || el.classList.contains('btn-accept-req') ||
            el.classList.contains('btn-reject-req') || el.classList.contains('btn-invite-friend') ||
            el.classList.contains('cape')
          );
          const hasKnownId = (
            el.id === 'btnStartClearCache' || el.id === 'btnSubmitJoinCode' ||
            el.id === 'btnSubmitAddFriend' || el.id === 'zin' || el.id === 'zout' ||
            el.id === 'btnCopyLobbyCode' || el.id === 'btnCopyMyFriendCode'
          );

          if (el.tagName === 'BUTTON' && !hasKnownDataset && !hasKnownClass && !hasKnownId) {
            badButtons++;
          }
        }

        return { count: clickable.length, overlapped, badButtons };
      })()`,
      returnByValue: true
    });

    if (inspectRes.result.value) {
      result.buttonsClicked += inspectRes.result.value.count;
      result.overlappedElements += inspectRes.result.value.overlapped;
      result.buttonsWithoutEffectOrCmd += inspectRes.result.value.badButtons;
    }
  }

  // 4. Test Play -> Running state
  await sendSession('Runtime.evaluate', { expression: 'window.setIslandMode("idle")' });
  await new Promise(r => setTimeout(r, 400));

  const playRes = await sendSession('Runtime.evaluate', {
    expression: `(() => {
      const playBtn = document.querySelector('.bar .play');
      if (playBtn) {
        window.startGame(playBtn);
      }
      return !!playBtn;
    })()`,
    returnByValue: true
  });

  // Wait for launch completion (effects=off takes 1-2 frames, full ~1.8s)
  await new Promise(r => setTimeout(r, target.effects === 'off' ? 400 : 2000));
  const runningRes = await sendSession('Runtime.evaluate', {
    expression: `(() => ({
      running: typeof window.isGameRunning === 'function' && window.isGameRunning(),
      mode: document.getElementById('island')?.classList.contains('running')
    }))()`,
    returnByValue: true
  });

  result.playRunningOk = !!(runningRes.result.value?.running && runningRes.result.value?.mode);

  // Stop game
  await sendSession('Runtime.evaluate', { expression: 'if (window.stopGame) window.stopGame();' });

  ws.removeEventListener('message', sessionEventsHandler);
  ws.close();
  chrome.kill();
  try { fs.rmSync(TEMP_USER_DATA, { recursive: true, force: true }); } catch (e) {}

  result.passed = (
    result.consoleErrors.length === 0 &&
    result.network404s.length === 0 &&
    result.sectionsVisited === 6 &&
    result.overlappedElements === 0 &&
    result.buttonsWithoutEffectOrCmd === 0 &&
    result.backdropLayersOk &&
    result.mapCanvasSizeOk &&
    result.skinview3dOk &&
    result.playRunningOk
  );

  return result;
}

async function main() {
  console.log('========================================================================================');
  console.log('🚀 Running Smoke Test Suite (index.html & aura-v3-standalone.html across effects modes)');
  console.log('========================================================================================\n');

  const results = [];
  let allPass = true;

  for (const t of targets) {
    try {
      const res = await runSmokeForTarget(t);
      results.push(res);
      if (!res.passed) allPass = false;
    } catch (e) {
      console.error(`Error testing ${t.name}:`, e);
      allPass = false;
    }
  }

  // Print Table
  console.log('\n------------------------------------------------------------------------------------------------------------------------');
  console.log(
    'Target'.padEnd(38) + ' | ' +
    'Err/404'.padEnd(8) + ' | ' +
    'Sections'.padEnd(9) + ' | ' +
    'Overlaps'.padEnd(9) + ' | ' +
    'Backdrop'.padEnd(9) + ' | ' +
    'MapCanvas'.padEnd(10) + ' | ' +
    'Skin3D'.padEnd(7) + ' | ' +
    'Play'.padEnd(6) + ' | ' +
    'Result'
  );
  console.log('------------------------------------------------------------------------------------------------------------------------');

  for (const r of results) {
    const errStr = `${r.consoleErrors.length}/${r.network404s.length}`;
    console.log(
      r.target.padEnd(38) + ' | ' +
      errStr.padEnd(8) + ' | ' +
      `${r.sectionsVisited}/6`.padEnd(9) + ' | ' +
      `${r.overlappedElements}`.padEnd(9) + ' | ' +
      (r.backdropLayersOk ? 'OK' : 'FAIL').padEnd(9) + ' | ' +
      (r.mapCanvasSizeOk ? 'OK' : 'FAIL').padEnd(10) + ' | ' +
      (r.skinview3dOk ? 'OK' : 'FAIL').padEnd(7) + ' | ' +
      (r.playRunningOk ? 'OK' : 'FAIL').padEnd(6) + ' | ' +
      (r.passed ? '✅ PASS' : '❌ FAIL')
    );
    if (!r.passed) {
      if (r.consoleErrors.length) console.log(`   Console errors: ${JSON.stringify(r.consoleErrors)}`);
      if (r.network404s.length) console.log(`   404 URLs: ${JSON.stringify(r.network404s)}`);
      if (r.buttonsWithoutEffectOrCmd > 0) console.log(`   Buttons missing effect/cmd: ${r.buttonsWithoutEffectOrCmd}`);
    }
  }
  console.log('------------------------------------------------------------------------------------------------------------------------\n');

  if (!allPass) {
    console.error('❌ Smoke tests FAILED.');
    process.exit(1);
  } else {
    console.log('✅ All Smoke tests PASSED successfully!');
    process.exit(0);
  }
}

main();

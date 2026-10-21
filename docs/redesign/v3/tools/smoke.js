/**
 * Smoke Test Suite for Aura Launcher v3 (index.html & aura-v3-standalone.html)
 * Tests at 1366x768 across effects modes (full & off).
 * Real clicks on each visible interactive control in .sec-body.
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
    offTransitionsOk: false,
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

  // 2b. If effects=off, check that .toggle and .seg .lens have transition-duration > 0
  if (target.effects === 'off') {
    const offTransRes = await sendSession('Runtime.evaluate', {
      expression: `(() => {
        const toggle = document.querySelector('.toggle');
        const lens = document.querySelector('.seg .lens');
        const toggleDur = toggle ? parseFloat(window.getComputedStyle(toggle).transitionDuration) : 0;
        const lensDur = lens ? parseFloat(window.getComputedStyle(lens).transitionDuration) : 0;
        return (toggleDur > 0 && lensDur > 0);
      })()`,
      returnByValue: true
    });
    result.offTransitionsOk = Boolean(offTransRes.result.value);
  } else {
    result.offTransitionsOk = true;
  }
  for (const sec of SECTIONS) {
    await sendSession('Runtime.evaluate', {
      expression: `window.setIslandMode("${sec}")`
    });
    await new Promise(r => setTimeout(r, target.effects === 'off' ? 150 : 800));
    result.sectionsVisited++;

    // If map, check canvas size, wrap match and insets
    if (sec === 'map') {
      const mapCheck = await sendSession('Runtime.evaluate', {
        expression: `(() => {
          if (typeof window.renderMapNow === 'function') window.renderMapNow();
          const wrap = document.querySelector('.mapwrap');
          const cv = document.getElementById('mapCanvas');
          const list = document.querySelector('.map-list');
          const zoom = document.querySelector('.map-zoom');
          if (!wrap || !cv) return null;
          const wrapRect = wrap.getBoundingClientRect();
          const cvRect = cv.getBoundingClientRect();
          const dpr = window.devicePixelRatio || 1;

          const rectMatches = Math.abs(wrapRect.width - cvRect.width) <= 2 && Math.abs(wrapRect.height - cvRect.height) <= 2;
          const listInside = list ? (list.getBoundingClientRect().left >= wrapRect.left - 2) : true;
          const zoomInside = zoom ? (zoom.getBoundingClientRect().right <= wrapRect.right + 2) : true;

          return {
            rectMatches,
            listInside,
            zoomInside,
            isMatch: rectMatches && listInside && zoomInside && cv.width > 300 && cv.height > 200
          };
        })()`,
        returnByValue: true
      });
      result.mapCanvasSizeOk = !!mapCheck.result.value?.isMatch;
    }

    // If wardrobe, test that skinview3d renders non-transparent pixels
    if (sec === 'wardrobe') {
      const skinRenderCheck = await sendSession('Runtime.evaluate', {
        expression: `(() => {
          const cv = document.getElementById('skinCanvas3D');
          if (!cv) return false;
          const v = window.AuraWardrobe?.viewer;
          if (v && typeof v.render === 'function') v.render();
          const gl = cv.getContext('webgl2') || cv.getContext('webgl');
          if (!gl) return false;
          const pixels = new Uint8Array(4);
          gl.readPixels(Math.floor(cv.width / 2), Math.floor(cv.height / 2), 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
          // Check center or slightly below center
          if (pixels[3] > 0) return true;
          gl.readPixels(Math.floor(cv.width / 2), Math.floor(cv.height * 0.4), 1, 1, gl.RGBA, gl.UNSIGNED_BYTE, pixels);
          return pixels[3] > 0;
        })()`,
        returnByValue: true
      });
      result.skinview3dOk = result.skinview3dOk && Boolean(skinRenderCheck.result.value);
    }

    // Inspect and click elements inside active .sec-body
    const clickSummary = await sendSession('Runtime.evaluate', {
      expression: `(() => {
        const body = document.querySelector('[data-l="${sec}"] .sec-body');
        if (!body) return { count: 0, overlapped: 0, badButtons: 0 };
        const clickable = Array.from(body.querySelectorAll('button, input, .toggle, .pick'));
        let overlapped = 0;
        let badButtons = 0;

        for (const el of clickable) {
          const closedReveal = el.closest('.reveal:not(.open)');
          if (closedReveal) continue;

          const rect = el.getBoundingClientRect();
          if (rect.width === 0 || rect.height === 0 || el.hidden || window.getComputedStyle(el).display === 'none') {
            continue;
          }

          const bodyRect = body.getBoundingClientRect();
          const isVerticallyVisible = (rect.bottom > bodyRect.top && rect.top < bodyRect.bottom);
          if (isVerticallyVisible) {
            const cx = Math.max(rect.left + 5, Math.min(rect.right - 5, rect.left + rect.width / 2));
            const cy = Math.max(rect.top + 5, Math.min(rect.bottom - 5, rect.top + rect.height / 2));
            const topEl = document.elementFromPoint(cx, cy);
            if (!topEl || (!el.contains(topEl) && !topEl.contains(el))) {
              console.log('OVERLAP in ' + sec + ': el <' + el.tagName + ' class=\"' + el.className + '\">, topEl: <' + (topEl ? topEl.tagName + ' class=\"' + topEl.className + '\"' : 'null') + '>');
              overlapped++;
            }
          }

          // Test real click and detect DOM change / toast / command
          if (el.tagName === 'BUTTON' || el.classList.contains('toggle') || el.classList.contains('pick')) {
            const prevHtml = document.body.innerHTML;
            const prevToasts = document.querySelectorAll('.toast-item').length;
            
            try {
              el.click();
            } catch (e) {}

            const nextHtml = document.body.innerHTML;
            const nextToasts = document.querySelectorAll('.toast-item').length;
            const hasCmd = Boolean(el.dataset.cmd) || Boolean(el.getAttribute('onclick')) || Boolean(el.closest('[data-cmd]'));
            const hasEffect = (nextHtml !== prevHtml) || (nextToasts !== prevToasts) || hasCmd;

            if (!hasEffect) {
              console.log('NO EFFECT: <' + el.tagName + ' class=\"' + el.className + '\" text=\"' + (el.innerText || '').slice(0, 20) + '\"> in ' + sec);
              badButtons++;
            }
          }
        }

        return { count: clickable.length, overlapped, badButtons };
      })()`,
      returnByValue: true
    });

    if (clickSummary.result.value) {
      result.buttonsClicked += clickSummary.result.value.count;
      result.overlappedElements += clickSummary.result.value.overlapped;
      result.buttonsWithoutEffectOrCmd += clickSummary.result.value.badButtons;
    }
  }

  // 4. Test Play -> Running state via real click
  await sendSession('Runtime.evaluate', { expression: 'window.setIslandMode("idle")' });
  await new Promise(r => setTimeout(r, 400));

  await sendSession('Runtime.evaluate', {
    expression: `(() => {
      const playBtn = document.querySelector('.bar .play');
      if (playBtn) playBtn.click();
    })()`
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

  // Stop game via real click on stop button if running
  await sendSession('Runtime.evaluate', {
    expression: `(() => {
      const stopBtn = document.querySelector('[data-stop]');
      if (stopBtn) stopBtn.click();
      else if (window.stopGame) window.stopGame();
    })()`
  });

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
    result.playRunningOk &&
    result.offTransitionsOk
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

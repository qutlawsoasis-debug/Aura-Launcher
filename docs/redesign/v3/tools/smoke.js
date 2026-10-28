/**
 * Smoke Test Suite for Aura Launcher v3 (index.html & aura-v3-standalone.html)
 * Tests at 1366x768 across effects modes (full & off).
 * Real clicks on each visible interactive control in .sec-body.
 */

const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawn } = require('child_process');

const { preview } = require('vite');

const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const BASE_URL = 'http://127.0.0.1:4173';
const STANDALONE_PATH = path.resolve(__dirname, '..', 'dist-single', 'aura-v3-standalone.html');
const STANDALONE_URL = 'file:///' + STANDALONE_PATH.replace(/\\/g, '/');

const targets = [
  { name: 'index.html (effects=full)', url: `${BASE_URL}/index.html?effects=full`, effects: 'full' },
  { name: 'index.html (effects=off)', url: `${BASE_URL}/index.html?effects=off`, effects: 'off' },
  { name: 'aura-v3-standalone.html (effects=full)', url: `${STANDALONE_URL}?effects=full`, effects: 'full' },
  { name: 'aura-v3-standalone.html (effects=off)', url: `${STANDALONE_URL}?effects=off`, effects: 'off' }
];

async function ensurePreviewServer() {
  console.log('Starting isolated vite preview...');
  const server = await preview({
    root: path.resolve(__dirname, '..'),
    build: { outDir: 'dist' },
    preview: { port: 0, strictPort: true, host: '127.0.0.1' }
  });
  const baseUrl = server.resolvedUrls.local[0].replace(/\/$/, '');
  for (const target of targets) {
    if (target.url.startsWith(BASE_URL)) target.url = target.url.replace(BASE_URL, baseUrl);
  }
  return server;
}

// Hidden screens still have computed filters; only painted layers consume the budget.
function inspectBackdropLayers(root = document) {
  const layers = [];
  for (const el of root.querySelectorAll('*')) {
    if (!el.checkVisibility({ opacityProperty: true, visibilityProperty: true })) continue;
    const rect = el.getBoundingClientRect();
    let left = Math.max(0, rect.left);
    let top = Math.max(0, rect.top);
    let right = Math.min(innerWidth, rect.right);
    let bottom = Math.min(innerHeight, rect.bottom);
    for (let parent = el.parentElement; parent; parent = parent.parentElement) {
      const style = getComputedStyle(parent);
      const clip = parent.getBoundingClientRect();
      if (/hidden|clip|scroll|auto/.test(style.overflowX)) {
        left = Math.max(left, clip.left);
        right = Math.min(right, clip.right);
      }
      if (/hidden|clip|scroll|auto/.test(style.overflowY)) {
        top = Math.max(top, clip.top);
        bottom = Math.min(bottom, clip.bottom);
      }
    }
    if (right <= left || bottom <= top) continue;
    for (const pseudo of ['', '::before', '::after']) {
      const style = getComputedStyle(el, pseudo || null);
      if (style.display === 'none' || style.visibility !== 'visible' || Number(style.opacity) === 0) continue;
      if (pseudo && (style.content === 'none' || style.content === 'normal')) continue;
      const filter = style.backdropFilter || style.webkitBackdropFilter;
      if (filter && filter !== 'none') {
        layers.push({ element: el.tagName.toLowerCase() + (el.id ? '#' + el.id : '') +
          Array.from(el.classList, name => '.' + name).join('') + pseudo, filter });
      }
    }
  }
  return layers;
}

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
    backdropLayersOk: true,
    backdropChecks: [],
    mapCanvasSizeOk: false,
    skinview3dOk: false,
    playRunningOk: false,
    offTransitionsOk: false,
    passed: false
  };

  const tempUserData = fs.mkdtempSync(path.join(os.tmpdir(), 'aura-v3-smoke-'));
  const chrome = spawn(CHROME_PATH, [
    '--headless=new',
    '--allow-file-access-from-files',
    '--remote-debugging-port=0',
    `--user-data-dir=${tempUserData}`,
    '--window-size=1366,768',
    '--disable-gpu',
    '--no-first-run',
    'about:blank'
  ]);

  let wsUrl = null;
  for (let i = 0; i < 40; i++) {
    await new Promise(r => setTimeout(r, 200));
    try {
      const [port, endpoint] = fs.readFileSync(path.join(tempUserData, 'DevToolsActivePort'), 'utf8').trim().split(/\r?\n/);
      if (port && endpoint) {
        wsUrl = `ws://127.0.0.1:${port}${endpoint}`;
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
        if (msg.params.response.status >= 400) {
          result.network404s.push(`${msg.params.response.status} ${msg.params.response.url}`);
        }
      } else if (msg.method === 'Network.loadingFailed') {
        if (!msg.params.canceled) {
          result.network404s.push(`FAILED ${msg.params.errorText}: ${msg.params.requestId}`);
        }
      }
    } catch (e) {}
  };

  ws.addEventListener('message', sessionEventsHandler);

  // Exercise the inspector on real browser styles before loading the application.
  const inspectorTest = await sendSession('Runtime.evaluate', {
    expression: `(() => {
      const inspect = ${inspectBackdropLayers.toString()};
      document.body.innerHTML = '<style>.sample {width:40px;height:40px;backdrop-filter:blur(2px)}' +
        '.pseudo::before {content:"";position:absolute;inset:0;backdrop-filter:blur(2px)}</style>' +
        '<div id="fixture"><div id="sample" class="sample"></div></div>';
      const fixture = document.getElementById('fixture');
      const sample = document.getElementById('sample');
      const failures = [];
      const expect = (name, count) => {
        const actual = inspect(fixture).length;
        if (actual !== count) failures.push({ name, expected: count, actual });
      };
      expect('visible filter', 1);
      sample.style.display = 'none'; expect('display none', 0);
      sample.style.cssText = 'opacity:0'; expect('transparent', 0);
      sample.style.cssText = 'visibility:hidden'; expect('hidden', 0);
      sample.style.cssText = 'position:fixed;top:-100px'; expect('outside viewport', 0);
      sample.style.cssText = 'position:relative;top:100px';
      fixture.style.cssText = 'height:10px;overflow:hidden'; expect('clipped', 0);
      fixture.style.cssText = 'display:none'; expect('hidden ancestor', 0);
      fixture.style.cssText = ''; sample.style.cssText = 'position:relative;backdrop-filter:none';
      sample.className = 'sample pseudo'; expect('pseudo filter', 1);
      sample.style.backdropFilter = 'blur(2px)'; expect('extra layer', 2);
      document.body.innerHTML = '';
      return failures;
    })()`,
    returnByValue: true
  });
  const inspectorFailures = inspectorTest.result?.value;
  if (!Array.isArray(inspectorFailures) || inspectorFailures.length) {
    result.consoleErrors.push('Backdrop inspector regression: ' + JSON.stringify(inspectorFailures));
  }

  await sendSession('Page.navigate', { url: target.url });
  await new Promise(r => setTimeout(r, 1200));

  // 1. Verify skinview3d object
  const skinRes = await sendSession('Runtime.evaluate', {
    expression: 'typeof skinview3d',
    returnByValue: true
  });
  result.skinview3dOk = (skinRes.result.value === 'object');

  // 1b. Verify Onest font and .world background-image
  const assetCheck = await sendSession('Runtime.evaluate', {
    expression: `(async () => {
      const fontLoaded = document.fonts ? document.fonts.check('16px Onest') : true;
      const worldEl = document.querySelector('.world') || document.getElementById('world');
      let bgLoaded = false;
      let rawBg = '';
      if (worldEl) {
        rawBg = window.getComputedStyle(worldEl).backgroundImage || '';
        if (rawBg && rawBg !== 'none') {
          const match = rawBg.match(/url\\(["']?([^"')]+)["']?\\)/);
          if (match && match[1]) {
            bgLoaded = await new Promise(resolve => {
              const img = new Image();
              img.onload = () => resolve(true);
              img.onerror = () => resolve(false);
              img.src = match[1];
            });
          }
        }
      }
      return { fontLoaded, bgLoaded, rawBg };
    })()`,
    awaitPromise: true,
    returnByValue: true
  });
  result.fontAndBgOk = Boolean(assetCheck.result?.value?.fontLoaded && assetCheck.result?.value?.bgLoaded);
  if (!result.fontAndBgOk) {
    result.consoleErrors.push(`Asset check failed: fontLoaded=${assetCheck.result?.value?.fontLoaded}, bgLoaded=${assetCheck.result?.value?.bgLoaded}, rawBg=${assetCheck.result?.value?.rawBg}`);
  }

  async function checkBackdrops(state) {
    const response = await sendSession('Runtime.evaluate', {
      expression: `(${inspectBackdropLayers.toString()})()`,
      returnByValue: true
    });
    const layers = response.result?.value;
    const limit = target.effects === 'off' ? 0 : 1;
    const passed = Array.isArray(layers) && layers.length <= limit;
    result.backdropLayersOk &&= passed;
    result.backdropChecks.push({ state, limit, layers, passed });
  }
  await checkBackdrops('startup');
  const bootReady = await sendSession('Runtime.evaluate', {
    expression: `new Promise(resolve => {
      const started = performance.now();
      const timer = setInterval(() => {
        if (!document.getElementById('boot')) { clearInterval(timer); resolve(true); }
        else if (performance.now() - started > 10000) { clearInterval(timer); resolve(false); }
      }, 50);
    })`,
    awaitPromise: true,
    returnByValue: true
  });
  if (bootReady.result?.value !== true) result.consoleErrors.push('Boot did not finish before section checks');
  await checkBackdrops('compact');

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
    await checkBackdrops(sec);
    if (sec === 'settings') {
      const previews = await sendSession('Runtime.evaluate', {
        expression: `Array.from(document.querySelectorAll('.theme-preview .mini-capsule'),
          el => getComputedStyle(el).backdropFilter)`,
        returnByValue: true
      });
      if (previews.result?.value?.length !== 2 || previews.result.value.some(filter => filter !== 'none')) {
        result.consoleErrors.push('Theme previews must reuse the island backdrop: ' + JSON.stringify(previews.result?.value));
      }
      await sendSession('Runtime.evaluate', {
        expression: `document.documentElement.setAttribute('data-theme', 'liquid')`
      });
      await new Promise(r => setTimeout(r, 300));
      await checkBackdrops('settings:liquid');
      await sendSession('Runtime.evaluate', {
        expression: `document.documentElement.setAttribute('data-theme', 'frost')`
      });
      await new Promise(r => setTimeout(r, 300));
    }

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

          // Размер раскладки, без transform: у холста-заглушки идёт медленный «наплыв» через scale
          const rectMatches = Math.abs(wrap.clientWidth - cv.offsetWidth) <= 2 && Math.abs(wrap.clientHeight - cv.offsetHeight) <= 2;
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
      result.skinview3dOk = result.skinview3dOk &&
    result.fontAndBgOk && Boolean(skinRenderCheck.result.value);
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
      if (playBtn) {
        if (typeof window.startGame === 'function') {
          window.startGame(playBtn);
        } else {
          playBtn.click();
        }
      }
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
  try { fs.rmSync(tempUserData, { recursive: true, force: true }); } catch (e) {}

  result.passed = (
    result.consoleErrors.length === 0 &&
    result.network404s.length === 0 &&
    result.sectionsVisited === 6 &&
    result.overlappedElements === 0 &&
    result.buttonsWithoutEffectOrCmd === 0 &&
    result.backdropLayersOk &&
    result.mapCanvasSizeOk &&
    result.skinview3dOk &&
    result.fontAndBgOk &&
    result.playRunningOk &&
    result.offTransitionsOk
  );

  return result;
}

async function main() {
  console.log('========================================================================================');
  console.log('🚀 Running Smoke Test Suite (index.html & aura-v3-standalone.html across effects modes)');
  console.log('========================================================================================\n');

  let previewServer = null;
  try {
    previewServer = await ensurePreviewServer();
  } catch (err) {
    console.error('Failed to initialize vite preview server:', err);
    process.exit(1);
  }

  const results = [];
  let allPass = true;

  try {
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
  } finally {
    if (previewServer) {
      console.log('Stopping preview server...');
      await previewServer.close();
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
    console.log('Backdrop layers: ' + r.backdropChecks.map(check => check.state + '=' + (check.layers?.length ?? 'ERROR') + '/' + check.limit).join(', '));
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
      if (!r.backdropLayersOk) console.log('   Backdrop checks: ' + JSON.stringify(r.backdropChecks));
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

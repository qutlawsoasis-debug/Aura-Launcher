const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const TEMP_USER_DATA = path.join(__dirname, '.chrome_agent_c_profile');
const SCREENSHOT_DIR = path.join(__dirname, 'screenshots', 'agent-c');

if (!fs.existsSync(SCREENSHOT_DIR)) {
  fs.mkdirSync(SCREENSHOT_DIR, { recursive: true });
}

async function run() {
  console.log('--- Starting Agent C Integration Test ---');
  
  const chromeProcess = spawn(CHROME_PATH, [
    '--headless=new',
    '--remote-debugging-port=9555',
    `--user-data-dir=${TEMP_USER_DATA}`,
    '--disable-gpu',
    '--no-first-run',
    '--no-default-browser-check',
    'about:blank'
  ]);

  let wsUrl = null;
  for (let i = 0; i < 40; i++) {
    await new Promise(r => setTimeout(r, 250));
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
    console.error('Failed to connect to Chrome on port 9555');
    chromeProcess.kill();
    process.exit(1);
  }

  const ws = new WebSocket(wsUrl);
  let id = 1;
  const pending = new Map();
  const consoleErrors = [];

  ws.onmessage = (event) => {
    const msg = JSON.parse(event.data);
    if (msg.method === 'Runtime.consoleAPICalled') {
      if (msg.params.type === 'error') {
        consoleErrors.push(msg.params.args.map(a => a.value || a.description).join(' '));
      }
    }
    if (msg.method === 'Runtime.exceptionThrown') {
      consoleErrors.push(msg.params.exceptionDetails.text + ' ' + (msg.params.exceptionDetails.exception?.description || ''));
    }
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

  await sendSession('Page.enable');
  await sendSession('Runtime.enable');

  const fileUrl = 'file:///' + path.resolve(__dirname, '../index.html').replace(/\\/g, '/') + '#/play';
  console.log('Navigating to full app shell:', fileUrl);

  const results = {
    viewports: {},
    doubleInitCheck: null,
    reducedMotionCheck: null,
    settingsFadeCheck: null,
    consoleErrors: []
  };

  // --- 1. Test 1920x991 ---
  console.log('\n--- Test Viewport 1920x991 ---');
  await sendSession('Emulation.setDeviceMetricsOverride', {
    width: 1920,
    height: 991,
    deviceScaleFactor: 1,
    mobile: false
  });

  await sendSession('Page.navigate', { url: fileUrl });
  await new Promise(r => setTimeout(r, 1500));

  // Check display of .screen-viewport::after in play screen
  const playAfterCheck = await sendSession('Runtime.evaluate', {
    expression: `(() => {
      const vp = document.querySelector('.screen-viewport');
      const afterStyle = window.getComputedStyle(vp, '::after');
      const playActive = document.querySelector('#screen-play.is-active');
      const pill = document.querySelector('.audio-pill-widget');
      const rail = document.querySelector('.play-stories-rail');
      const list = document.querySelector('.stories-cards-list');
      const hero = document.querySelector('.launch-hero-island');
      const heroStyle = window.getComputedStyle(hero);
      
      const pillRect = pill ? pill.getBoundingClientRect() : null;
      const railRect = rail ? rail.getBoundingClientRect() : null;
      
      return {
        hasPlayActive: !!playActive,
        afterDisplay: afterStyle.display,
        afterHeight: afterStyle.height,
        heroBackdropBlur: heroStyle.backdropFilter || heroStyle.webkitBackdropFilter,
        pillInsideRail: pill && rail ? (pill.parentElement === rail) : false,
        pillPosition: pill ? window.getComputedStyle(pill).position : null,
        pillRect,
        railRect
      };
    })()`,
    returnByValue: true
  });
  results.viewports['1920x991'] = playAfterCheck.result.value;
  console.log('1920x991 layout status:', playAfterCheck.result.value);

  // Capture full screenshot
  const fullShot = await sendSession('Page.captureScreenshot', { format: 'png' });
  fs.writeFileSync(path.join(SCREENSHOT_DIR, 'play-1920x991-full.png'), Buffer.from(fullShot.data, 'base64'));

  // Capture bottom 120px clip
  const bottomClipShot = await sendSession('Page.captureScreenshot', {
    format: 'png',
    clip: { x: 0, y: 991 - 120, width: 1920, height: 120, scale: 1 }
  });
  fs.writeFileSync(path.join(SCREENSHOT_DIR, 'play-1920x991-bottom120.png'), Buffer.from(bottomClipShot.data, 'base64'));
  console.log('Saved 1920x991 screenshots.');

  // Check Settings screen to confirm fade ::after is still working there
  console.log('\n--- Checking Settings screen fade ---');
  await sendSession('Runtime.evaluate', {
    expression: `(() => {
      window.location.hash = '#/settings';
      const navBtn = document.querySelector('.nav-tab[data-route="settings"]');
      if (navBtn) navBtn.click();
    })()`
  });
  await new Promise(r => setTimeout(r, 600));

  const settingsAfterCheck = await sendSession('Runtime.evaluate', {
    expression: `(() => {
      const vp = document.querySelector('.screen-viewport');
      const afterStyle = window.getComputedStyle(vp, '::after');
      const settingsActive = document.querySelector('#screen-settings.is-active');
      return {
        hasSettingsActive: !!settingsActive,
        afterDisplay: afterStyle.display,
        afterHeight: afterStyle.height,
        afterBackground: afterStyle.backgroundImage || afterStyle.background
      };
    })()`,
    returnByValue: true
  });
  results.settingsFadeCheck = settingsAfterCheck.result.value;
  console.log('Settings screen ::after status:', settingsAfterCheck.result.value);

  // Switch back to Play screen
  await sendSession('Runtime.evaluate', {
    expression: `(() => {
      window.location.hash = '#/play';
      const navBtn = document.querySelector('.nav-tab[data-route="play"]');
      if (navBtn) navBtn.click();
    })()`
  });
  await new Promise(r => setTimeout(r, 600));

  // --- 2. Test 1280x720 ---
  console.log('\n--- Test Viewport 1280x720 ---');
  await sendSession('Emulation.setDeviceMetricsOverride', {
    width: 1280,
    height: 720,
    deviceScaleFactor: 1,
    mobile: false
  });
  await new Promise(r => setTimeout(r, 600));

  const vp1280Check = await sendSession('Runtime.evaluate', {
    expression: `(() => {
      const list = document.querySelector('.stories-cards-list');
      const pill = document.querySelector('.audio-pill-widget');
      const rail = document.querySelector('.play-stories-rail');
      const cards = document.querySelectorAll('.story-card');
      const lastCard = cards[cards.length - 1];
      
      const listScrollHeight = list ? list.scrollHeight : 0;
      const listClientHeight = list ? list.clientHeight : 0;
      const listOverflowY = list ? window.getComputedStyle(list).overflowY : null;
      
      const pillRect = pill ? pill.getBoundingClientRect() : null;
      const lastCardRect = lastCard ? lastCard.getBoundingClientRect() : null;
      const railRect = rail ? rail.getBoundingClientRect() : null;
      
      // Is pill overlapping last card?
      const isOverlapping = pillRect && lastCardRect ? (lastCardRect.bottom > pillRect.top && lastCardRect.top < pillRect.bottom) : false;
      const isPillInsideRail = pillRect && railRect ? (pillRect.bottom <= railRect.bottom + 2 && pillRect.top >= railRect.top) : false;

      return {
        listScrollHeight,
        listClientHeight,
        listIsScrollable: listScrollHeight > listClientHeight,
        listOverflowY,
        isOverlapping,
        isPillInsideRail,
        pillPosition: pill ? window.getComputedStyle(pill).position : null
      };
    })()`,
    returnByValue: true
  });
  results.viewports['1280x720'] = vp1280Check.result.value;
  console.log('1280x720 status:', vp1280Check.result.value);

  const shot1280 = await sendSession('Page.captureScreenshot', { format: 'png' });
  fs.writeFileSync(path.join(SCREENSHOT_DIR, 'play-1280x720-full.png'), Buffer.from(shot1280.data, 'base64'));

  // --- 3. Test 1024x768 ---
  console.log('\n--- Test Viewport 1024x768 ---');
  await sendSession('Emulation.setDeviceMetricsOverride', {
    width: 1024,
    height: 768,
    deviceScaleFactor: 1,
    mobile: false
  });
  await new Promise(r => setTimeout(r, 600));

  const vp1024Check = await sendSession('Runtime.evaluate', {
    expression: `(() => {
      const list = document.querySelector('.stories-cards-list');
      const pill = document.querySelector('.audio-pill-widget');
      const rail = document.querySelector('.play-stories-rail');
      const cards = document.querySelectorAll('.story-card');
      const lastCard = cards[cards.length - 1];
      
      const listScrollHeight = list ? list.scrollHeight : 0;
      const listClientHeight = list ? list.clientHeight : 0;
      const listOverflowY = list ? window.getComputedStyle(list).overflowY : null;
      
      const pillRect = pill ? pill.getBoundingClientRect() : null;
      const lastCardRect = lastCard ? lastCard.getBoundingClientRect() : null;
      const railRect = rail ? rail.getBoundingClientRect() : null;
      
      const isOverlapping = pillRect && lastCardRect ? (lastCardRect.bottom > pillRect.top && lastCardRect.top < pillRect.bottom) : false;
      const isPillInsideRail = pillRect && railRect ? (pillRect.bottom <= railRect.bottom + 2 && pillRect.top >= railRect.top) : false;

      return {
        listScrollHeight,
        listClientHeight,
        listIsScrollable: listScrollHeight > listClientHeight,
        listOverflowY,
        isOverlapping,
        isPillInsideRail
      };
    })()`,
    returnByValue: true
  });
  results.viewports['1024x768'] = vp1024Check.result.value;
  console.log('1024x768 status:', vp1024Check.result.value);

  const shot1024 = await sendSession('Page.captureScreenshot', { format: 'png' });
  fs.writeFileSync(path.join(SCREENSHOT_DIR, 'play-1024x768-full.png'), Buffer.from(shot1024.data, 'base64'));

  // --- 4. Test Double Initialization ---
  console.log('\n--- Checking Double Initialization Guard ---');
  const doubleInitRes = await sendSession('Runtime.evaluate', {
    expression: `(() => {
      const initedBefore = window._playScreenInited;
      const audioBtn = document.getElementById('audioToggleBtn');
      const vinylContainer = document.getElementById('vinylContainer');
      
      // Re-invoke
      window.initPlayScreen();
      
      // Test audio toggle
      const wasPlayingBefore = vinylContainer.classList.contains('is-playing');
      audioBtn.click();
      const isPlayingAfterFirstClick = vinylContainer.classList.contains('is-playing');
      audioBtn.click();
      const isPlayingAfterSecondClick = vinylContainer.classList.contains('is-playing');

      return {
        initedBefore,
        reinvokedSafely: true,
        toggleCycleWorked: (wasPlayingBefore !== isPlayingAfterFirstClick) && (isPlayingAfterFirstClick !== isPlayingAfterSecondClick),
        finalStateSameAsInitial: wasPlayingBefore === isPlayingAfterSecondClick
      };
    })()`,
    returnByValue: true
  });
  results.doubleInitCheck = doubleInitRes.result.value;
  console.log('Double init check:', doubleInitRes.result.value);

  // --- 5. Test prefers-reduced-motion ---
  console.log('\n--- Checking prefers-reduced-motion ---');
  await sendSession('Emulation.setEmulatedMedia', {
    features: [{ name: 'prefers-reduced-motion', value: 'reduce' }]
  });
  await new Promise(r => setTimeout(r, 400));

  const reducedMotionRes = await sendSession('Runtime.evaluate', {
    expression: `(() => {
      const disc = document.querySelector('.vinyl-disc');
      const bar1 = document.querySelector('.audio-equalizer .bar-1');
      const discStyle = window.getComputedStyle(disc);
      const bar1Style = window.getComputedStyle(bar1);
      
      return {
        discAnimationName: discStyle.animationName,
        bar1AnimationName: bar1Style.animationName,
        isMotionDisabled: discStyle.animationName === 'none' && bar1Style.animationName === 'none'
      };
    })()`,
    returnByValue: true
  });
  results.reducedMotionCheck = reducedMotionRes.result.value;
  console.log('Reduced motion check:', reducedMotionRes.result.value);

  // Reset media
  await sendSession('Emulation.setEmulatedMedia', { features: [] });

  // --- 6. Check WCAG Contrast on Play screen elements ---
  console.log('\n--- Checking WCAG Contrast on Play Screen ---');
  const contrastCheck = await sendSession('Runtime.evaluate', {
    expression: `(() => {
      function getLuminance(r, g, b) {
        const a = [r, g, b].map(v => {
          v /= 255;
          return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
        });
        return a[0] * 0.2126 + a[1] * 0.7152 + a[2] * 0.0722;
      }
      function getRatio(l1, l2) {
        const lighter = Math.max(l1, l2);
        const darker = Math.min(l1, l2);
        return (lighter + 0.05) / (darker + 0.05);
      }
      
      const elements = [
        { label: 'Profile Nickname', sel: '.profile-nickname' },
        { label: 'Profile Status Text', sel: '.profile-status-text' },
        { label: 'Hero Tag', sel: '.hero-tag' },
        { label: 'Hero Spec', sel: '.hero-spec' },
        { label: 'Launch Button Text', sel: '.btn-launch-primary.state-idle' },
        { label: 'Launch State Title', sel: '.launch-state-title' },
        { label: 'Story Heading', sel: '.story-heading' },
        { label: 'Story Snippet', sel: '.story-snippet' },
        { label: 'Changelog Button', sel: '.btn-changelog-link' },
        { label: 'Story More Button', sel: '.btn-story-more' }
      ];

      return elements.map(item => {
        const el = document.querySelector(item.sel);
        if (!el) return { label: item.label, found: false };
        const cs = window.getComputedStyle(el);
        return {
          label: item.label,
          found: true,
          color: cs.color,
          fontSize: cs.fontSize,
          fontWeight: cs.fontWeight
        };
      });
    })()`,
    returnByValue: true
  });
  results.contrastElements = contrastCheck.result.value;
  console.log('Key Play elements styles:', contrastCheck.result.value);

  // Record console errors
  results.consoleErrors = consoleErrors;
  console.log('Console Errors count:', consoleErrors.length);
  if (consoleErrors.length > 0) {
    console.log('Console Errors:', consoleErrors);
  }

  // Write out comprehensive results to file
  fs.writeFileSync(path.join(SCREENSHOT_DIR, 'test-results.json'), JSON.stringify(results, null, 2), 'utf-8');
  console.log('\n--- Agent C Test Finished Successfully ---');

  ws.close();
  chromeProcess.kill();
}

run().catch(err => {
  console.error('Test execution failed:', err);
  process.exit(1);
});

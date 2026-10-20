const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const TEMP_USER_DATA = path.join(__dirname, '.chrome_standalone_profile');
const BASE_DIR = __dirname;
const OUT_FILE = path.join(BASE_DIR, 'aura-v3-standalone.html');

async function getCompressedWebp(filePath, quality = 0.82) {
  const chromeProcess = spawn(CHROME_PATH, [
    '--headless=new',
    '--remote-debugging-port=9911',
    `--user-data-dir=${TEMP_USER_DATA}`,
    '--disable-gpu',
    'about:blank'
  ]);

  let wsUrl = null;
  for (let i = 0; i < 30; i++) {
    await new Promise(r => setTimeout(r, 200));
    try {
      const res = await fetch('http://127.0.0.1:9911/json/version');
      if (res.ok) {
        const data = await res.json();
        wsUrl = data.webSocketDebuggerUrl;
        break;
      }
    } catch (e) {}
  }

  if (!wsUrl) {
    chromeProcess.kill();
    throw new Error('Failed to connect to Chrome on port 9911');
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

  await sendSession('Runtime.enable');

  const fileBytes = fs.readFileSync(filePath);
  const mime = filePath.endsWith('.png') ? 'image/png' : 'image/jpeg';
  const base64 = fileBytes.toString('base64');

  const evalRes = await sendSession('Runtime.evaluate', {
    expression: `
      new Promise((resolve, reject) => {
        const img = new Image();
        img.onload = () => {
          const cv = document.createElement('canvas');
          cv.width = img.naturalWidth;
          cv.height = img.naturalHeight;
          const ctx = cv.getContext('2d');
          ctx.drawImage(img, 0, 0);
          const dataUrl = cv.toDataURL('image/webp', ${quality});
          resolve(dataUrl);
        };
        img.onerror = () => reject('Failed to load image in canvas');
        img.src = 'data:${mime};base64,${base64}';
      })
    `,
    awaitPromise: true,
    returnByValue: true
  });

  ws.close();
  chromeProcess.kill();
  try { fs.rmSync(TEMP_USER_DATA, { recursive: true, force: true }); } catch (e) {}

  return evalRes.result.value;
}

async function build() {
  console.log('1. Compressing and encoding background and assets to Data URLs...');
  const bgDataUrl = await getCompressedWebp(path.join(BASE_DIR, 'assets', 'images', 'bg_1.jpg'), 0.82);
  const bgSizeKb = Math.round((bgDataUrl.length * 0.75) / 1024);
  console.log(`   Background WebP Data URL size: ${bgSizeKb} KB (Target: <= 400 KB)`);

  const artistDataUrl = await getCompressedWebp(path.join(BASE_DIR, 'assets', 'audio', 'artist.jpg'), 0.85);
  const coock0ldBase64 = fs.readFileSync(path.join(BASE_DIR, 'assets', 'images', 'Coock0ld.png')).toString('base64');
  const coock0ldDataUrl = `data:image/png;base64,${coock0ldBase64}`;

  console.log('2. Base64 encoding used Manrope fonts (Regular, Medium, SemiBold, Bold, ExtraBold)...');
  const fonts = {
    'Manrope-Regular.ttf': fs.readFileSync(path.join(BASE_DIR, 'assets', 'fonts', 'Manrope-Regular.ttf')).toString('base64'),
    'Manrope-Medium.ttf': fs.readFileSync(path.join(BASE_DIR, 'assets', 'fonts', 'Manrope-Medium.ttf')).toString('base64'),
    'Manrope-SemiBold.ttf': fs.readFileSync(path.join(BASE_DIR, 'assets', 'fonts', 'Manrope-SemiBold.ttf')).toString('base64'),
    'Manrope-Bold.ttf': fs.readFileSync(path.join(BASE_DIR, 'assets', 'fonts', 'Manrope-Bold.ttf')).toString('base64'),
    'Manrope-ExtraBold.ttf': fs.readFileSync(path.join(BASE_DIR, 'assets', 'fonts', 'Manrope-ExtraBold.ttf')).toString('base64')
  };

  console.log('3. Concatenating and inlining CSS stylesheets...');
  let tokensCss = fs.readFileSync(path.join(BASE_DIR, 'tokens.css'), 'utf-8');
  // Replace background image URL with data URI
  tokensCss = tokensCss.replace(/url\(['"]?\.\.\/images\/bg_1\.jpg['"]?\)/g, `url('${bgDataUrl}')`);

  let coreCss = fs.readFileSync(path.join(BASE_DIR, 'assets', 'css', 'core.css'), 'utf-8');
  // Replace fonts URLs with data URIs
  for (const [fontFile, b64] of Object.entries(fonts)) {
    const re = new RegExp(`url\\(['"]?\\.\\./fonts/${fontFile}['"]?\\)\\s+format\\(['"]truetype['"]\\)`, 'g');
    coreCss = coreCss.replace(re, `url('data:font/truetype;charset=utf-8;base64,${b64}') format('truetype')`);
  }

  const screenCssFiles = ['play.css', 'lobby.css', 'friends.css', 'wardrobe.css', 'workshop.css', 'settings.css'];
  let screensCombinedCss = '';
  for (const sc of screenCssFiles) {
    let cssContent = fs.readFileSync(path.join(BASE_DIR, 'screens', sc), 'utf-8');
    screensCombinedCss += `\n/* === SCREEN CSS: ${sc} === */\n` + cssContent;
  }

  const combinedStyle = `
<style id="aura-inlined-styles">
/* === 1. TOKENS === */
${tokensCss}

/* === 2. CORE === */
${coreCss}

/* === 3. SCREENS === */
${screensCombinedCss}
</style>
  `;

  console.log('4. Reading screen HTML views...');
  const screenHtmlFiles = ['play.html', 'lobby.html', 'friends.html', 'wardrobe.html', 'workshop.html', 'settings.html'];
  let combinedViewsHtml = '';
  for (const sh of screenHtmlFiles) {
    let htmlContent = fs.readFileSync(path.join(BASE_DIR, 'screens', sh), 'utf-8');
    // Replace artist.jpg and Coock0ld.png image references with data URLs
    htmlContent = htmlContent.replace(/assets\/audio\/artist\.jpg/g, artistDataUrl);
    htmlContent = htmlContent.replace(/assets\/images\/Coock0ld\.png/g, coock0ldDataUrl);
    combinedViewsHtml += `\n<!-- === SCREEN VIEW: ${sh} === -->\n` + htmlContent;
  }

  console.log('5. Concatenating and inlining JavaScript modules...');
  const jsFiles = [
    path.join(BASE_DIR, 'assets', 'js', 'demo-data.js'),
    path.join(BASE_DIR, 'assets', 'js', 'core.js'),
    path.join(BASE_DIR, 'screens', 'play.js'),
    path.join(BASE_DIR, 'screens', 'lobby.js'),
    path.join(BASE_DIR, 'screens', 'friends.js'),
    path.join(BASE_DIR, 'screens', 'wardrobe.js'),
    path.join(BASE_DIR, 'screens', 'workshop.js'),
    path.join(BASE_DIR, 'screens', 'settings.js')
  ];

  let combinedJs = '';
  for (const jf of jsFiles) {
    let jsContent = fs.readFileSync(jf, 'utf-8');
    // Replace local asset paths with data URLs
    jsContent = jsContent.replace(/assets\/images\/Coock0ld\.png/g, coock0ldDataUrl);
    jsContent = jsContent.replace(/assets\/audio\/artist\.jpg/g, artistDataUrl);
    combinedJs += `\n// === MODULE: ${path.basename(jf)} ===\n` + jsContent;
  }

  const combinedScript = `
<script id="aura-inlined-scripts">
${combinedJs}
</script>
  `;

  console.log('6. Assembling single aura-v3-standalone.html...');
  const standaloneHtml = `<!-- GENERATED — не редактировать, правь screens/* -->
<!DOCTYPE html>
<html lang="ru" data-effects="full">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0, maximum-scale=1.0, user-scalable=no">
  <title>Aura Launcher — Next-Gen Client Shell (Standalone)</title>
  ${combinedStyle}
</head>
<body>

  <div class="app-viewport">
    <!-- Живой фоновый мир Minecraft с шейдерами и параллаксом -->
    <div class="world-bg" id="worldBg"></div>
    <div class="world-overlay"></div>
    <div class="sunbeam-light" id="sunbeamLight"></div>
    <canvas id="ambientCanvas"></canvas>

    <!-- 1. ВЕРХНЯЯ ПАРЯЩАЯ ЛЕНТА НАВИГАЦИИ (Apple Liquid Glass Capsule) -->
    <header class="top-ribbon" role="banner">
      <div class="brand-section">
        <a href="#/play" class="logo-aura" aria-label="Aura Launcher">
          <span>Aura</span>
        </a>
      </div>

      <!-- Центральная парящая капсула навигации -->
      <nav class="nav-pill-dock" data-glass role="navigation" aria-label="Основная навигация">
        <button type="button" class="nav-tab active" data-route="play" aria-current="page">
          <span>Играть</span>
        </button>
        <button type="button" class="nav-tab" data-route="lobby">
          <span>Лобби</span>
        </button>
        <button type="button" class="nav-tab" data-route="friends">
          <span>Друзья</span>
        </button>
        <button type="button" class="nav-tab" data-route="wardrobe">
          <span>Гардероб</span>
        </button>
        <button type="button" class="nav-tab" data-route="workshop">
          <span>Мастерская</span>
        </button>
        <button type="button" class="nav-tab" data-route="settings">
          <span>Настройки</span>
        </button>
      </nav>

      <!-- Оконные кнопки управления (минимизировать, развернуть, закрыть) -->
      <div class="top-actions">
        <div class="win-controls" aria-label="Управление окном">
          <button type="button" class="win-btn" title="Свернуть" aria-label="Свернуть окно">
            <svg class="icon" viewBox="0 0 24 24"><line x1="5" y1="12" x2="19" y2="12"></line></svg>
          </button>
          <button type="button" class="win-btn" title="Развернуть" aria-label="Развернуть окно">
            <svg class="icon" viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect></svg>
          </button>
          <button type="button" class="win-btn close" title="Закрыть" aria-label="Закрыть приложение">
            <svg class="icon" viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
          </button>
        </div>
      </div>
    </header>

    <!-- 2. ОСНОВНОЙ ВЬЮПОРТ ДЛЯ ЭКРАНОВ -->
    <main class="screen-viewport" id="screenViewport" role="main">
${combinedViewsHtml}
    </main>

    <!-- СИСТЕМНЫЙ ТОСТ-КОНТЕЙНЕР -->
    <div class="toast-container" id="toastContainer" aria-live="polite"></div>
  </div>

  ${combinedScript}

</body>
</html>
  `;

  fs.writeFileSync(OUT_FILE, standaloneHtml, 'utf-8');

  const stats = fs.statSync(OUT_FILE);
  const sizeKb = Math.round(stats.size / 1024);
  const sizeMb = (stats.size / (1024 * 1024)).toFixed(2);

  console.log('----------------------------------------------------');
  console.log(`✅ Standalone build completed successfully!`);
  console.log(`   Output file: ${OUT_FILE}`);
  console.log(`   Total file size: ${sizeKb} KB (${sizeMb} MB)`);
  console.log(`   Max limit: 8.00 MB -> ${stats.size <= 8 * 1024 * 1024 ? 'PASSED ✅' : 'FAILED ❌'}`);
  console.log('----------------------------------------------------');

  return { sizeKb, sizeMb, bytes: stats.size };
}

build().catch(err => {
  console.error('Build standalone failed:', err);
  process.exit(1);
});

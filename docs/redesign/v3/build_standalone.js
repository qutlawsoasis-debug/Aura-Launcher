/**
 * Standalone Single-File HTML Builder for Aura Launcher v3 (Island Concept)
 * Inlines Onest font, CSS stylesheets, screen components, and JS modules using shell.html.
 */

const fs = require('fs');
const path = require('path');
const { spawn } = require('child_process');

const BASE_DIR = path.resolve(__dirname);
const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const TEMP_USER_DATA = path.join(BASE_DIR, '.chrome_standalone_profile');

async function getCompressedWebp(filePath, quality = 0.8) {
  const chromeProcess = spawn(CHROME_PATH, [
    '--headless=new',
    '--remote-debugging-port=9444',
    `--user-data-dir=${TEMP_USER_DATA}`,
    '--disable-gpu',
    '--no-first-run',
    '--no-default-browser-check',
    'about:blank'
  ]);

  let wsUrl = null;
  for (let i = 0; i < 40; i++) {
    await new Promise(r => setTimeout(r, 200));
    try {
      const res = await fetch('http://127.0.0.1:9444/json/version');
      if (res.ok) {
        const data = await res.json();
        wsUrl = data.webSocketDebuggerUrl;
        break;
      }
    } catch (e) {}
  }

  if (!wsUrl) {
    console.warn('Chrome CDP not available, using raw base64');
    chromeProcess.kill();
    const bytes = fs.readFileSync(filePath);
    return `data:image/jpeg;base64,${bytes.toString('base64')}`;
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
  const bgPath = path.resolve(BASE_DIR, 'assets', 'images', 'bg_1.jpg');
  const bgDataUrl = await getCompressedWebp(bgPath, 0.82);
  const bgSizeKb = Math.round((bgDataUrl.length * 0.75) / 1024);
  console.log(`   Background WebP Data URL size: ${bgSizeKb} KB (Target: <= 400 KB)`);

  const steveBase64 = fs.readFileSync(path.join(BASE_DIR, 'assets', 'images', 'steve.png')).toString('base64');
  const steveDataUrl = `data:image/png;base64,${steveBase64}`;

  console.log('2. Base64 encoding Onest woff2 font...');
  const onestBase64 = fs.readFileSync(path.join(BASE_DIR, 'assets', 'fonts', 'Onest.woff2')).toString('base64');

  console.log('3. Concatenating and inlining CSS stylesheets...');
  const bgBlurPath = path.resolve(BASE_DIR, 'assets', 'images', 'bg_1-blur.jpg');
  const bgBlurBase64 = fs.readFileSync(bgBlurPath).toString('base64');
  const bgBlurDataUrl = `data:image/jpeg;base64,${bgBlurBase64}`;

  let tokensCss = fs.readFileSync(path.join(BASE_DIR, 'tokens.css'), 'utf-8');
  tokensCss = tokensCss.replace(/url\(['"]?assets\/images\/bg_1-blur\.jpg['"]?\)/g, `url('${bgBlurDataUrl}')`);

  let coreCss = fs.readFileSync(path.join(BASE_DIR, 'assets', 'css', 'core.css'), 'utf-8');
  coreCss = coreCss.replace(/url\(['"]?\.\.\/images\/bg_1\.jpg['"]?\)/g, `url('${bgDataUrl}')`);
  coreCss = coreCss.replace(/url\(['"]?\.\.\/images\/steve\.png['"]?\)/g, `url('${steveDataUrl}')`);
  coreCss = coreCss.replace(/url\(['"]?\.\.\/fonts\/Onest\.woff2['"]?\)\s+format\(['"]woff2['"]\)/g, `url('data:font/woff2;charset=utf-8;base64,${onestBase64}') format('woff2')`);

  const screenCssFiles = ['play.css', 'map.css', 'lobby.css', 'friends.css', 'wardrobe.css', 'workshop.css', 'settings.css'];
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

  console.log('4. Reading screen HTML views from shell.html...');
  const shellHtml = fs.readFileSync(path.join(BASE_DIR, 'shell.html'), 'utf-8');
  const mapHtml = fs.readFileSync(path.join(BASE_DIR, 'screens', 'map.html'), 'utf-8');
  const lobbyHtml = fs.readFileSync(path.join(BASE_DIR, 'screens', 'lobby.html'), 'utf-8');
  const friendsHtml = fs.readFileSync(path.join(BASE_DIR, 'screens', 'friends.html'), 'utf-8');
  const wardrobeHtml = fs.readFileSync(path.join(BASE_DIR, 'screens', 'wardrobe.html'), 'utf-8');
  const workshopHtml = fs.readFileSync(path.join(BASE_DIR, 'screens', 'workshop.html'), 'utf-8');
  const settingsHtml = fs.readFileSync(path.join(BASE_DIR, 'screens', 'settings.html'), 'utf-8');

  console.log('5. Concatenating and inlining JavaScript modules...');
  const jsFiles = [
    path.join(BASE_DIR, 'assets', 'vendor', 'skinview3d.bundle.js'),
    path.join(BASE_DIR, 'src', 'perf-hud.js'),
    path.join(BASE_DIR, 'src', 'bridge.js'),
    path.join(BASE_DIR, 'assets', 'js', 'demo-data.js'),
    path.join(BASE_DIR, 'assets', 'js', 'core.js'),
    path.join(BASE_DIR, 'screens', 'play.js'),
    path.join(BASE_DIR, 'screens', 'map.js'),
    path.join(BASE_DIR, 'screens', 'lobby.js'),
    path.join(BASE_DIR, 'screens', 'friends.js'),
    path.join(BASE_DIR, 'screens', 'wardrobe.js'),
    path.join(BASE_DIR, 'screens', 'workshop.js'),
    path.join(BASE_DIR, 'screens', 'settings.js')
  ];

  let combinedJs = '';
  for (const jf of jsFiles) {
    let jsContent = fs.readFileSync(jf, 'utf-8');
    combinedJs += `\n// === MODULE: ${path.basename(jf)} ===\n` + jsContent;
  }

  // Base64 inline capes if present in wardrobe.js
  const cyanCapeBase64 = fs.readFileSync(path.join(BASE_DIR, 'assets', 'images', 'cape_cyan.png')).toString('base64');
  const silverCapeBase64 = fs.readFileSync(path.join(BASE_DIR, 'assets', 'images', 'cape_silver.png')).toString('base64');
  combinedJs = combinedJs.replace(/assets\/images\/cape_cyan\.png/g, `data:image/png;base64,${cyanCapeBase64}`);
  combinedJs = combinedJs.replace(/assets\/images\/cape_silver\.png/g, `data:image/png;base64,${silverCapeBase64}`);

  const combinedScript = `
<script id="aura-inlined-scripts">
${combinedJs}
</script>
  `;

  console.log('6. Assembling single aura-v3-standalone.html from shell.html...');
  let standaloneHtml = shellHtml
    .replace('<!-- SHELL_STYLES_PLACEHOLDER -->', () => combinedStyle)
    .replace('<!-- SHELL_SCRIPTS_PLACEHOLDER -->', () => combinedScript)
    .replace('<!-- SCREEN_MAP_PLACEHOLDER -->', () => mapHtml)
    .replace('<!-- SCREEN_LOBBY_PLACEHOLDER -->', () => lobbyHtml)
    .replace('<!-- SCREEN_FRIENDS_PLACEHOLDER -->', () => friendsHtml)
    .replace('<!-- SCREEN_WARDROBE_PLACEHOLDER -->', () => wardrobeHtml)
    .replace('<!-- SCREEN_WORKSHOP_PLACEHOLDER -->', () => workshopHtml)
    .replace('<!-- SCREEN_SETTINGS_PLACEHOLDER -->', () => settingsHtml);

  const outPath = path.join(BASE_DIR, 'aura-v3-standalone.html');
  fs.writeFileSync(outPath, standaloneHtml, 'utf-8');

  const stats = fs.statSync(outPath);
  const sizeMb = (stats.size / (1024 * 1024)).toFixed(2);
  const sizeKb = Math.round(stats.size / 1024);

  console.log('----------------------------------------------------');
  console.log(`✅ Standalone build completed successfully!`);
  console.log(`   Output file: ${outPath}`);
  console.log(`   Total file size: ${sizeKb} KB (${sizeMb} MB)`);
  console.log(`   Max limit: 8.00 MB -> ${stats.size < 8 * 1024 * 1024 ? 'PASSED ✅' : 'FAILED ❌'}`);
  console.log('----------------------------------------------------');
}

build().catch(err => {
  console.error('Build failed with error:', err);
  process.exit(1);
});

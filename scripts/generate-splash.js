const fs = require('fs');
const path = require('path');
const os = require('os');
const { spawnSync } = require('child_process');

const REPO_ROOT = path.resolve(__dirname, '..');
const OUT_PNG = path.join(REPO_ROOT, 'splash.png');

// Discovery of a Chromium-based browser for headless rendering:
// explicit CHROME_PATH override, then Google Chrome (x64, x86, per-user),
// then Microsoft Edge (Chromium).
function findHeadlessBrowser() {
  const candidates = [
    process.env.CHROME_PATH,
    'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe',
    'C:\\Program Files (x86)\\Google\\Chrome\\Application\\chrome.exe',
    path.join(os.homedir(), 'AppData', 'Local', 'Google', 'Chrome', 'Application', 'chrome.exe'),
    'C:\\Program Files (x86)\\Microsoft\\Edge\\Application\\msedge.exe',
    'C:\\Program Files\\Microsoft\\Edge\\Application\\msedge.exe'
  ].filter(Boolean);
  for (const candidate of candidates) {
    if (fs.existsSync(candidate)) {
      return candidate;
    }
  }
  throw new Error(
    'No Chromium-based browser found (checked CHROME_PATH, Google Chrome, Microsoft Edge). ' +
    'Install Chrome or Edge, or set CHROME_PATH to chrome.exe/msedge.exe.'
  );
}

const BROWSER = findHeadlessBrowser();
// Temp HTML lives in the system temp dir, never inside the repository.
const TEMP_HTML = path.join(os.tmpdir(), 'aura-splash-' + process.pid + '.html');

function toBase64(relPath) {
  return fs.readFileSync(path.join(REPO_ROOT, relPath)).toString('base64');
}

const fontManropeBold = toBase64('Resources/Fonts/Manrope-Bold.ttf');
const fontManropeMedium = toBase64('Resources/Fonts/Manrope-Medium.ttf');
const fontManropeSemi = toBase64('Resources/Fonts/Manrope-SemiBold.ttf');
const fontOswald = toBase64('Resources/Fonts/Oswald.ttf');

const html = `<!DOCTYPE html>
<html>
<head>
<meta charset="utf-8">
<style>
  @font-face {
    font-family: 'Manrope';
    src: url('data:font/truetype;base64,${fontManropeMedium}') format('truetype');
    font-weight: 500;
  }
  @font-face {
    font-family: 'Manrope';
    src: url('data:font/truetype;base64,${fontManropeSemi}') format('truetype');
    font-weight: 600;
  }
  @font-face {
    font-family: 'Manrope';
    src: url('data:font/truetype;base64,${fontManropeBold}') format('truetype');
    font-weight: 700;
  }
  @font-face {
    font-family: 'Oswald';
    src: url('data:font/truetype;base64,${fontOswald}') format('truetype');
    font-weight: 600;
  }

  * { box-sizing: border-box; margin: 0; padding: 0; }
  html, body {
    width: 480px;
    height: 320px;
    overflow: hidden;
    background: #091015;
    color: #E8F0F8;
    font-family: 'Manrope', -apple-system, BlinkMacSystemFont, sans-serif;
    user-select: none;
  }

  .splash-container {
    width: 480px;
    height: 320px;
    position: relative;
    background: radial-gradient(circle at 50% 32%, #14212B 0%, #0C141A 55%, #091015 100%);
    border: 1px solid rgba(255, 255, 255, 0.09);
    display: flex;
    flex-direction: column;
    align-items: center;
    justify-content: center;
    padding-bottom: 24px;
  }

  /* Тонкое декоративное свечение вверху */
  .glow-top {
    position: absolute;
    top: 0;
    left: 20%;
    right: 20%;
    height: 1px;
    background: linear-gradient(90deg, transparent, rgba(242, 166, 60, 0.35), transparent);
  }

  /* Иконка-эмблема куба */
  .icon-wrapper {
    width: 76px;
    height: 76px;
    border-radius: 18px;
    background: linear-gradient(145deg, #182834, #0D171F);
    border: 1px solid rgba(255, 255, 255, 0.12);
    box-shadow: 0 10px 28px rgba(0, 0, 0, 0.45), inset 0 1px 0 rgba(255, 255, 255, 0.18);
    display: flex;
    align-items: center;
    justify-content: center;
    margin-bottom: 16px;
  }

  .cube-svg {
    width: 44px;
    height: 44px;
    filter: drop-shadow(0 0 12px rgba(100, 210, 255, 0.4));
  }

  /* Заголовок Aura */
  .brand-title {
    font-family: 'Oswald', 'Manrope', sans-serif;
    font-size: 38px;
    font-weight: 600;
    letter-spacing: 0.04em;
    color: #E8F0F8;
    line-height: 1;
    margin-bottom: 6px;
    text-shadow: 0 2px 14px rgba(0, 0, 0, 0.5);
  }

  /* Подзаголовок LAUNCHER */
  .brand-sub {
    font-family: 'Manrope', sans-serif;
    font-size: 11px;
    font-weight: 700;
    letter-spacing: 0.28em;
    color: #F2A63C;
    text-transform: uppercase;
    margin-bottom: 12px;
  }

  /* Фирменная риска из StartupWindow.xaml */
  .brand-dash {
    width: 28px;
    height: 2px;
    border-radius: 1px;
    background: #F2A63C;
    margin-bottom: 22px;
    opacity: 0.85;
  }

  /* Статусная строка */
  .status-row {
    display: flex;
    align-items: center;
    gap: 8px;
    font-size: 13px;
    font-weight: 500;
    color: #8A9BA8;
  }

  .status-dot {
    width: 6px;
    height: 6px;
    border-radius: 2px;
    background: #F2A63C;
    box-shadow: 0 0 6px rgba(242, 166, 60, 0.5);
  }

  /* Тонкая дорожка под полосу прогресса Velopack в самом низу (4px) */
  .progress-track {
    position: absolute;
    bottom: 0;
    left: 0;
    right: 0;
    height: 4px;
    background: #101B24;
    border-top: 1px solid rgba(255, 255, 255, 0.05);
  }
</style>
</head>
<body>
  <div class="splash-container">
    <div class="glow-top"></div>

    <div class="icon-wrapper">
      <svg class="cube-svg" viewBox="0 0 48 48" fill="none" xmlns="http://www.w3.org/2000/svg">
        <path d="M24 4L42 14.5V33.5L24 44L6 33.5V14.5L24 4Z" fill="#0C1B24" stroke="rgba(100, 210, 255, 0.35)" stroke-width="1.2"/>
        <path d="M24 5L40 14.5L24 24L8 14.5L24 5Z" fill="url(#topGrad)" opacity="0.95"/>
        <path d="M8 15.5L23.5 24.5V42.5L8 33.5V15.5Z" fill="url(#leftGrad)" opacity="0.9"/>
        <path d="M24.5 24.5L40 15.5V33.5L24.5 42.5V24.5Z" fill="url(#rightGrad)" opacity="0.95"/>
        <path d="M24 13L32 17.5L24 22L16 17.5L24 13Z" fill="#64D2FF" opacity="0.75"/>
        <path d="M16 18.5L23.5 22.5V31.5L16 27V18.5Z" fill="#38BDF8" opacity="0.7"/>
        <path d="M24.5 22.5L32 18.5V27L24.5 31.5V22.5Z" fill="#8EDEFF" opacity="0.8"/>

        <defs>
          <linearGradient id="topGrad" x1="8" y1="5" x2="40" y2="24" gradientUnits="userSpaceOnUse">
            <stop stop-color="#223E50"/>
            <stop offset="1" stop-color="#142634"/>
          </linearGradient>
          <linearGradient id="leftGrad" x1="8" y1="15" x2="24" y2="42" gradientUnits="userSpaceOnUse">
            <stop stop-color="#11222E"/>
            <stop offset="1" stop-color="#09141C"/>
          </linearGradient>
          <linearGradient id="rightGrad" x1="24" y1="15" x2="40" y2="42" gradientUnits="userSpaceOnUse">
            <stop stop-color="#172D3D"/>
            <stop offset="1" stop-color="#0E1C26"/>
          </linearGradient>
        </defs>
      </svg>
    </div>

    <div class="brand-title">Aura</div>
    <div class="brand-sub">Launcher</div>
    <div class="brand-dash"></div>

    <div class="status-row">
      <div class="status-dot"></div>
      <span>Установка Aura Launcher</span>
    </div>

    <div class="progress-track"></div>
  </div>
</body>
</html>`;

try {
  fs.writeFileSync(TEMP_HTML, html, 'utf8');

  const res = spawnSync(BROWSER, [
    '--headless=new',
    '--disable-gpu',
    '--no-sandbox',
    '--window-size=480,320',
    '--screenshot=' + OUT_PNG,
    'file:///' + TEMP_HTML.replace(/\\/g, '/')
  ]);

  if (res.status === 0) {
    console.log('splash.png generated cleanly at:', OUT_PNG, '(browser:', BROWSER + ')');
  } else {
    console.error('Failed to generate splash.png via', BROWSER, 'exit code:', res.status);
    if (res.stderr) {
      console.error(res.stderr.toString());
    }
    process.exit(res.status || 1);
  }
} finally {
  try {
    fs.unlinkSync(TEMP_HTML);
  } catch (e) {
    // best effort cleanup only
  }
}

// Запуск реального лаунчера (WebView2) с CDP-портом, открытие раздела
// «Мастерская -> Скриншоты» и захват окна лаунчера в PNG.
// Использование: node tools/qa-webview.js [выходной.png] [--offscreen]
// --offscreen (или AURA_OFFSCREEN=1): окно лаунчера выносится за экран, кадр только через
// CDP Page.captureScreenshot — рабочий стол пользователя не снимается.
const { spawn, execFileSync } = require('child_process');
const fs = require('fs');
const path = require('path');

const ROOT = path.resolve(__dirname, '..');
const CDP_PORT = 9233;
const OUT_DIR = path.join(ROOT, 'docs', 'redesign', 'qa', 'gallery');
const LOCK_FILE = path.join(__dirname, 'launcher-run.lock');

const argv = process.argv.slice(2);
const OFFSCREEN = argv.includes('--offscreen') || process.env.AURA_OFFSCREEN === '1';

function acquireLock() {
  try {
    const st = fs.statSync(LOCK_FILE);
    if (Date.now() - st.mtimeMs < 30 * 60 * 1000) {
      console.error(`launcher-run.lock занят: ${fs.readFileSync(LOCK_FILE, 'utf8').trim()}`);
      process.exit(2);
    }
  } catch {}
  const token = `${new Date().toISOString()} ${process.env.AURA_AGENT || 'unknown'}`;
  fs.writeFileSync(LOCK_FILE, token);
  return token;
}

function releaseLock(token) {
  try {
    if (fs.readFileSync(LOCK_FILE, 'utf8').trim() === token.trim()) fs.unlinkSync(LOCK_FILE);
  } catch {}
}

function moveWindowOffscreen(pid) {
  const ps = `
Add-Type -TypeDefinition @'
using System;
using System.Runtime.InteropServices;
public class WinMove {
    [DllImport("user32.dll")] public static extern bool SetWindowPos(IntPtr hWnd, IntPtr after, int x, int y, int cx, int cy, uint flags);
}
'@
$deadline = (Get-Date).AddSeconds(40)
while ((Get-Date) -lt $deadline) {
  $p = Get-Process -Id ${pid} -ErrorAction SilentlyContinue
  if ($p -and $p.MainWindowHandle -ne 0) {
    [WinMove]::SetWindowPos($p.MainWindowHandle, [IntPtr]::Zero, -32000, -32000, 0, 0, 0x0015) | Out-Null
    Write-Host "window moved offscreen: $($p.MainWindowHandle)"
    exit 0
  }
  Start-Sleep -Milliseconds 400
}
throw 'окно лаунчера не найдено для переноса за экран'
`;
  execFileSync('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', ps], { stdio: 'inherit' });
}

function findLauncherExe() {
  const base = path.join(ROOT, 'bin', 'Debug');
  if (!fs.existsSync(base)) throw new Error('Нет bin/Debug — собери лаунчер (dotnet build)');
  const hit = fs.readdirSync(base)
    .filter(d => d.startsWith('net8.0-windows'))
    .map(d => path.join(base, d, 'AuraLauncher.exe'))
    .find(fs.existsSync);
  if (!hit) throw new Error('AuraLauncher.exe не найден в bin/Debug');
  return hit;
}

const sleep = ms => new Promise(r => setTimeout(r, ms));

async function waitForPage(timeoutMs) {
  const deadline = Date.now() + timeoutMs;
  while (Date.now() < deadline) {
    try {
      const res = await fetch(`http://127.0.0.1:${CDP_PORT}/json/list`);
      const targets = await res.json();
      const page = targets.find(t => t.type === 'page' && /aura\.local|localhost/.test(t.url));
      if (page) return page;
    } catch {}
    await sleep(500);
  }
  throw new Error(`CDP-страница не появилась за ${timeoutMs} мс`);
}

function connect(wsUrl) {
  const ws = new WebSocket(wsUrl);
  let seq = 0;
  const pending = new Map();
  const events = [];
  ws.onmessage = ev => {
    const msg = JSON.parse(ev.data);
    if (msg.id && pending.has(msg.id)) {
      pending.get(msg.id)(msg);
      pending.delete(msg.id);
    } else if (msg.method) {
      events.push(msg);
      if (handler) handler(msg);
    }
  };
  let handler = null;
  const ready = new Promise((res, rej) => {
    ws.onopen = () => res();
    ws.onerror = e => rej(new Error('CDP WebSocket: ' + e.message));
  });
  return {
    ready,
    set onEvent(fn) { handler = fn; },
    send(method, params = {}) {
      return ready.then(() => new Promise(res => {
        const id = ++seq;
        pending.set(id, res);
        ws.send(JSON.stringify({ id, method, params }));
      }));
    },
    close: () => ws.close(),
  };
}

async function evalJs(cdp, expression, awaitPromise = false) {
  const r = await cdp.send('Runtime.evaluate', { expression, returnByValue: true, awaitPromise });
  if (r.result && r.result.exceptionDetails) throw new Error('evaluate: ' + JSON.stringify(r.result.exceptionDetails));
  return r.result?.result?.value;
}

function captureWindow(pid, outFile) {
  const ps = `
Add-Type -AssemblyName System.Drawing
Add-Type -TypeDefinition @'
using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
public class WinGrab {
    [DllImport("user32.dll")] public static extern bool PrintWindow(IntPtr hWnd, IntPtr hdcBlt, uint nFlags);
    [DllImport("user32.dll")] public static extern bool GetWindowRect(IntPtr hWnd, out RECT lpRect);
    [DllImport("user32.dll")] public static extern bool SetForegroundWindow(IntPtr hWnd);
    public struct RECT { public int Left; public int Top; public int Right; public int Bottom; }
    public static void Grab(IntPtr hwnd, string file) {
        SetForegroundWindow(hwnd);
        System.Threading.Thread.Sleep(600);
        RECT rc; GetWindowRect(hwnd, out rc);
        int w = rc.Right - rc.Left, h = rc.Bottom - rc.Top;
        using (Bitmap bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb))
        using (Graphics g = Graphics.FromImage(bmp)) {
            IntPtr hdc = g.GetHdc();
            PrintWindow(hwnd, hdc, 2);
            g.ReleaseHdc(hdc);
            bmp.Save(file, ImageFormat.Png);
        }
    }
}
'@ -ReferencedAssemblies System.Drawing
$deadline = (Get-Date).AddSeconds(40)
$hwnd = [IntPtr]::Zero
while ((Get-Date) -lt $deadline) {
  $p = Get-Process -Id ${pid} -ErrorAction SilentlyContinue
  if ($p -and $p.MainWindowHandle -ne 0) { $hwnd = $p.MainWindowHandle; break }
  Start-Sleep -Milliseconds 500
}
if ($hwnd -eq [IntPtr]::Zero) { throw 'окно лаунчера не найдено' }
[WinGrab]::Grab($hwnd, '${outFile.replace(/\\/g, '\\\\').replace(/'/g, "''")}')
Write-Host "captured $hwnd"
`;
  execFileSync('powershell', ['-NoProfile', '-ExecutionPolicy', 'Bypass', '-Command', ps], { stdio: 'inherit' });
}

async function main() {
  const exe = findLauncherExe();
  const cliArgs = process.argv.slice(2).filter(a => !a.startsWith('--'));
  const outMain = cliArgs[0] || path.join(OUT_DIR, 'launcher-gallery-r2.png');
  const outCdp = outMain.replace(/\.png$/, '-cdp.png');
  fs.mkdirSync(path.dirname(outMain), { recursive: true });

  const lockToken = acquireLock();

  const proc = spawn(exe, [], {
    cwd: path.dirname(exe),
    env: { ...process.env, WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS: `--remote-debugging-port=${CDP_PORT}`, AURA_OFFSCREEN: OFFSCREEN ? '1' : undefined },
    stdio: 'ignore',
  });
  console.log(`launcher pid=${proc.pid} (${exe}) offscreen=${OFFSCREEN}`);

  let cdp;
  try {
    if (OFFSCREEN) {
      await sleep(1500);
      moveWindowOffscreen(proc.pid);
    }
    const page = await waitForPage(60000);
    console.log(`cdp page: ${page.url}`);
    cdp = connect(page.webSocketDebuggerUrl);
    await cdp.ready;
    await cdp.send('Page.enable');
    await cdp.send('Network.enable').catch(() => {});
    const reqUrls = new Map();
    cdp.onEvent = m => {
      const p = m.params || {};
      if (m.method === 'Network.requestWillBeSent' && /shots\./.test(p.request?.url || '')) {
        reqUrls.set(p.requestId, p.request.url);
      }
      if (m.method === 'Network.responseReceived' && /shots\./.test(p.response.url)) {
        console.log(`net: ${p.response.status} ${p.response.url.slice(-50)}`);
      }
      if (m.method === 'Network.loadingFailed') {
        console.log(`net FAIL: ${reqUrls.get(p.requestId) || p.requestId} ${p.errorText} ${p.blockedReason || ''}`);
      }
    };
    await sleep(1000);

    const waitSelector = async sel => {
      for (let i = 0; i < 40; i++) {
        const found = await evalJs(cdp, `!!document.querySelector('${sel}')`);
        if (found) return;
        await sleep(500);
      }
      const dump = await evalJs(cdp, `[...document.querySelectorAll('button')].slice(0, 40)
        .map(b => (b.id || '') + '|' + (b.dataset.open || b.dataset.tab || '') + '|' + b.textContent.trim().slice(0, 20)).join('\\n')`);
      throw new Error(`селектор ${sel} не появился. Кнопки:\\n${dump}`);
    };

    await waitSelector('button[data-open="workshop"]');
    await evalJs(cdp, `document.querySelector('button[data-open="workshop"]').click(); 'ok'`);
    await waitSelector('#workshopSegmented button[data-tab="shots"]');
    await evalJs(cdp, `document.querySelector('#workshopSegmented button[data-tab="shots"]').click(); 'ok'`);

    let cards = -1, paneVisible = false;
    for (let i = 0; i < 20; i++) {
      await sleep(500);
      const st = await evalJs(cdp, `(() => {
        const pane = document.getElementById('paneShots');
        const grid = document.getElementById('shotsGrid');
        return JSON.stringify({
          visible: !!pane && !pane.hidden,
          cards: grid ? grid.querySelectorAll('img, .shot-card, .shot').length : -1,
          html: grid ? grid.innerHTML.slice(0, 80) : ''
        });
      })()`);
      const s = JSON.parse(st);
      paneVisible = s.visible;
      cards = s.cards;
      if (s.visible && s.cards > 0) break;
    }
    console.log(`paneShots visible=${paneVisible} cards=${cards}`);
    if (!paneVisible) throw new Error('paneShots не открылся');

    const imgStats = async () => await evalJs(cdp, `(() => {
      const imgs = [...document.querySelectorAll('#shotsGrid img')];
      const loaded = imgs.filter(i => i.complete && i.naturalWidth > 0).length;
      return JSON.stringify({ total: imgs.length, loaded, src0: imgs[0]?.src || null, src1: imgs[1]?.src || null });
    })()`);
    console.log('imgs before scroll:', await imgStats());
    await evalJs(cdp, `(() => {
      const scroller = document.querySelector('.sec-body') || document.scrollingElement;
      scroller.scrollTop = scroller.scrollHeight;
      return 'scrolled';
    })()`);
    await sleep(1200);
    console.log('imgs after scroll:', await imgStats());

    const shot = await cdp.send('Page.captureScreenshot', { format: 'png' });
    fs.writeFileSync(outCdp, Buffer.from(shot.result.data, 'base64'));
    console.log(`cdp shot: ${outCdp} (${shot.result.data.length} b64)`);

    if (OFFSCREEN) {
      console.log(`offscreen mode: frame is CDP-only -> ${outCdp}`);
    } else {
      captureWindow(proc.pid, outMain);
      console.log(`window shot: ${outMain}`);
    }
  } finally {
    cdp?.close();
    try { execFileSync('taskkill', ['/PID', String(proc.pid), '/T', '/F'], { stdio: 'ignore' }); } catch {}
    releaseLock(lockToken);
  }
}

main().catch(e => { console.error(e.message); process.exit(1); });

const { spawn } = require('child_process');
const path = require('path');

async function test() {
  const chrome = spawn('C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe', [
    '--headless=new',
    '--remote-debugging-port=9557',
    '--user-data-dir=' + path.join(__dirname, '.chrome_check'),
    'about:blank'
  ]);
  await new Promise(r => setTimeout(r, 1000));
  const res = await (await fetch('http://127.0.0.1:9557/json/version')).json();
  const ws = new WebSocket(res.webSocketDebuggerUrl);
  await new Promise(r => ws.onopen = r);
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

  const { targetId } = await send('Target.createTarget', { url: 'about:blank' });
  const { sessionId } = await send('Target.attachToTarget', { targetId, flatten: true });
  const sendSession = (method, params = {}) => new Promise(res => {
    const reqId = id++;
    pending.set(reqId, { resolve: res });
    ws.send(JSON.stringify({ id: reqId, sessionId, method, params }));
  });

  await sendSession('Page.enable');
  await sendSession('Runtime.enable');
  const fileUrl = 'file:///' + path.resolve(__dirname, 'index.html').replace(/\\/g, '/') + '#/play';
  await sendSession('Page.navigate', { url: fileUrl });
  await new Promise(r => setTimeout(r, 1200));

  for (const [w, h] of [[1920, 991], [1280, 720], [1024, 768]]) {
    await sendSession('Emulation.setDeviceMetricsOverride', { width: w, height: h, deviceScaleFactor: 1, mobile: false });
    await new Promise(r => setTimeout(r, 400));
    const geo = await sendSession('Runtime.evaluate', {
      expression: `(() => {
        const pill = document.querySelector('.audio-pill-widget');
        const list = document.querySelector('.stories-cards-list');
        const rail = document.querySelector('.play-stories-rail');
        const pRect = pill.getBoundingClientRect();
        const lRect = list.getBoundingClientRect();
        const rRect = rail.getBoundingClientRect();
        
        // Element at pill center
        const centerX = pRect.left + pRect.width / 2;
        const centerY = pRect.top + pRect.height / 2;
        const elAtCenter = document.elementFromPoint(centerX, centerY);
        const isPillAtPoint = pill.contains(elAtCenter);
        
        return {
          viewport: '${w}x${h}',
          listBottom: lRect.bottom,
          pillTop: pRect.top,
          pillBottom: pRect.bottom,
          railBottom: rRect.bottom,
          distanceListToPill: pRect.top - lRect.bottom,
          pillInsideRailBoundary: pRect.bottom <= rRect.bottom + 1,
          isPillOnTopNotCovered: isPillAtPoint,
          hoveredElement: elAtCenter ? (elAtCenter.className || elAtCenter.tagName) : null
        };
      })()`,
      returnByValue: true
    });
    console.log(geo.result.value);
  }

  ws.close();
  chrome.kill();
}

test().catch(console.error);

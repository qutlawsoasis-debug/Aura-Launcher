const { spawn } = require('child_process');
const fs = require('fs');
const path = require('path');

const CHROME_PATH = 'C:\\Program Files\\Google\\Chrome\\Application\\chrome.exe';
const TEMP_USER_DATA = path.join(__dirname, '.chrome_audit_profile');
const REPORT_ARG = process.argv[2] || 'audit-01.md';
const REPORT_PATH = path.join(__dirname, 'critique', REPORT_ARG);

const screens = ['play', 'lobby', 'friends', 'wardrobe', 'workshop', 'settings'];
const viewports = [
  { name: '1920x1080', width: 1920, height: 1080 },
  { name: '1366x768', width: 1366, height: 768 }
];

async function main() {
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
    await new Promise(r => setTimeout(r, 250));
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
    console.error('Failed to connect to Chrome on port 9444');
    chromeProcess.kill();
    process.exit(1);
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

  await sendSession('Page.enable');
  await sendSession('Runtime.enable');

  const fileUrl = 'file:///' + path.resolve(__dirname, 'index.html').replace(/\\/g, '/');

  // Audit runner in browser
  const inBrowserAudit = `
    (() => {
      function parseColor(str) {
        if (!str || str === 'transparent' || str === 'inherit') return null;
        const m = str.match(/rgba?\\((\\d+),\\s*(\\d+),\\s*(\\d+)(?:,\\s*([\\d.]+))?\\)/);
        if (m) {
          return {
            r: parseInt(m[1], 10),
            g: parseInt(m[2], 10),
            b: parseInt(m[3], 10),
            a: m[4] !== undefined ? parseFloat(m[4]) : 1.0
          };
        }
        return null;
      }

      function composite(fg, bg) {
        const a = fg.a + bg.a * (1 - fg.a);
        if (a === 0) return { r: 0, g: 0, b: 0, a: 0 };
        return {
          r: Math.round((fg.r * fg.a + bg.r * bg.a * (1 - fg.a)) / a),
          g: Math.round((fg.g * fg.a + bg.g * bg.a * (1 - fg.a)) / a),
          b: Math.round((fg.b * fg.a + bg.b * bg.a * (1 - fg.a)) / a),
          a: a
        };
      }

      function getEffectiveBg(el) {
        let cur = el;
        const stack = [];
        while (cur && cur !== document.documentElement) {
          const bg = parseColor(window.getComputedStyle(cur).backgroundColor);
          if (bg && bg.a > 0) {
            stack.push(bg);
          }
          cur = cur.parentElement;
        }
        let color = { r: 5, g: 11, b: 15, a: 1.0 }; // base background #050B0F
        for (let i = stack.length - 1; i >= 0; i--) {
          color = composite(stack[i], color);
        }
        return color;
      }

      function luminance(c) {
        const a = [c.r, c.g, c.b].map(v => {
          v /= 255;
          return v <= 0.03928 ? v / 12.92 : Math.pow((v + 0.055) / 1.055, 2.4);
        });
        return a[0] * 0.2126 + a[1] * 0.7152 + a[2] * 0.0722;
      }

      function contrastRatio(fg, bg) {
        const l1 = luminance(fg);
        const l2 = luminance(bg);
        return (Math.max(l1, l2) + 0.05) / (Math.min(l1, l2) + 0.05);
      }

      function getCssSelector(el) {
        if (!el || el.nodeType !== 1) return '';
        if (el.id) return '#' + el.id;
        let path = [];
        while (el && el.nodeType === 1 && el !== document.body) {
          let sel = el.nodeName.toLowerCase();
          if (el.className && typeof el.className === 'string') {
            const classes = el.className.trim().split(/\\s+/).filter(c => c && !c.includes(':')).slice(0, 2);
            if (classes.length) sel += '.' + classes.join('.');
          }
          path.unshift(sel);
          if (path.length >= 3) break;
          el = el.parentElement;
        }
        return path.join(' > ');
      }

      const results = {
        smallFonts: [],
        lowContrast: [],
        overflowIssues: [],
        glassAncestors: [],
        smallTapTargets: []
      };

      // 1. Text nodes check (font-size < 12px) & WCAG contrast
      const walker = document.createTreeWalker(document.body, NodeFilter.SHOW_ELEMENT, null, false);
      let el;
      const minTap = 38; // px

      while (el = walker.nextNode()) {
        const style = window.getComputedStyle(el);
        if (style.display === 'none' || style.visibility === 'hidden' || style.opacity === '0') continue;
        const rect = el.getBoundingClientRect();
        if (rect.width === 0 && rect.height === 0) continue;

        // Has direct text content?
        let hasDirectText = false;
        for (let child of el.childNodes) {
          if (child.nodeType === 3 && child.nodeValue.trim().length > 0) {
            hasDirectText = true;
            break;
          }
        }

        if (hasDirectText) {
          const fs = parseFloat(style.fontSize);
          if (fs < 12) {
            results.smallFonts.push({
              selector: getCssSelector(el),
              fontSize: fs + 'px',
              text: el.innerText.trim().slice(0, 30)
            });
          }

          // Contrast check
          const fg = parseColor(style.color);
          if (fg && fg.a > 0.1) {
            const bg = getEffectiveBg(el);
            const ratio = contrastRatio(fg, bg);
            if (ratio < 4.5 && fs < 18) {
              results.lowContrast.push({
                selector: getCssSelector(el),
                ratio: ratio.toFixed(2),
                fg: style.color,
                text: el.innerText.trim().slice(0, 30)
              });
            }
          }
        }

        // 3. Overflow issues (exceeding window boundaries without scroll)
        if (rect.right > window.innerWidth + 1 && !el.classList.contains('world-bg') && el.id !== 'worldBg') {
          results.overflowIssues.push({
            selector: getCssSelector(el),
            right: Math.round(rect.right),
            windowWidth: window.innerWidth
          });
        }

        // 5. Tap targets
        const isClickable = el.matches('button, a, input, select, textarea, [role="button"], .tab-btn, .toggle-switch, .chip, .segmented-btn');
        if (isClickable) {
          if (rect.width < minTap || rect.height < minTap) {
            results.smallTapTargets.push({
              selector: getCssSelector(el),
              width: Math.round(rect.width),
              height: Math.round(rect.height),
              text: el.innerText ? el.innerText.trim().slice(0, 20) : (el.getAttribute('aria-label') || el.tagName)
            });
          }
        }
      }

      // 4. Glass panel ancestors
      const glassPanels = document.querySelectorAll('.glass-panel, .glass-card, .floating-pill-dock');
      glassPanels.forEach(panel => {
        let cur = panel.parentElement;
        const violations = [];
        while (cur && cur !== document.documentElement) {
          const s = window.getComputedStyle(cur);
          const op = parseFloat(s.opacity);
          const filter = s.filter;
          const mask = s.webkitMask || s.mask;
          const clip = s.clipPath;
          const willChange = s.willChange;
          const blend = s.mixBlendMode;

          if (op < 1) violations.push('opacity: ' + op);
          if (filter && filter !== 'none') violations.push('filter: ' + filter);
          if (mask && mask !== 'none') violations.push('mask: ' + mask);
          if (clip && clip !== 'none') violations.push('clip-path: ' + clip);
          if (willChange && (willChange.includes('opacity') || willChange.includes('filter'))) violations.push('will-change: ' + willChange);
          if (blend && blend !== 'normal') violations.push('mix-blend-mode: ' + blend);

          if (violations.length > 0) {
            results.glassAncestors.push({
              panel: getCssSelector(panel),
              ancestor: getCssSelector(cur),
              violations: [...violations]
            });
            break;
          }
          cur = cur.parentElement;
        }
      });

      return results;
    })()
  `;

  let totalViolations = 0;
  let reportMd = `# Результаты автоматического аудита интерфейса: ${REPORT_ARG}\n\n`;
  reportMd += `Дата запуска: ${new Date().toISOString()}\n`;
  reportMd += `Целевые экраны: ${screens.join(', ')}\n`;
  reportMd += `Разрешения: ${viewports.map(v => v.name).join(', ')}\n\n---\n\n`;

  for (const vp of viewports) {
    reportMd += `## Разрешение: ${vp.name}\n\n`;
    await sendSession('Emulation.setDeviceMetricsOverride', {
      width: vp.width,
      height: vp.height,
      deviceScaleFactor: 1,
      mobile: false
    });

    for (const screen of screens) {
      reportMd += `### Экран: \`#/${screen}\`\n\n`;
      const url = `${fileUrl}#/${screen}`;
      await sendSession('Page.navigate', { url });
      await new Promise(r => setTimeout(r, 600));

      const evalRes = await sendSession('Runtime.evaluate', {
        expression: inBrowserAudit,
        returnByValue: true
      });

      const res = evalRes.result.value;
      const screenViolations = res.smallFonts.length + res.lowContrast.length + res.overflowIssues.length + res.glassAncestors.length + res.smallTapTargets.length;
      totalViolations += screenViolations;

      reportMd += `- **Шрифты < 12px**: ${res.smallFonts.length}\n`;
      if (res.smallFonts.length > 0) {
        res.smallFonts.forEach(item => {
          reportMd += `  - \`${item.selector}\` (${item.fontSize}): "${item.text}"\n`;
        });
      }

      reportMd += `- **Контраст < 4.5:1**: ${res.lowContrast.length}\n`;
      if (res.lowContrast.length > 0) {
        res.lowContrast.forEach(item => {
          reportMd += `  - \`${item.selector}\` (контраст ${item.ratio}:1, fg: ${item.fg}): "${item.text}"\n`;
        });
      }

      reportMd += `- **Элементы за границей вьюпорта**: ${res.overflowIssues.length}\n`;
      if (res.overflowIssues.length > 0) {
        res.overflowIssues.forEach(item => {
          reportMd += `  - \`${item.selector}\` (right: ${item.right}px > ${item.windowWidth}px)\n`;
        });
      }

      reportMd += `- **Разрыв backdrop-filter у предков .glass-panel**: ${res.glassAncestors.length}\n`;
      if (res.glassAncestors.length > 0) {
        res.glassAncestors.forEach(item => {
          reportMd += `  - Панель: \`${item.panel}\` -> Предок: \`${item.ancestor}\` (${item.violations.join(', ')})\n`;
        });
      }

      reportMd += `- **Кликабельные зоны < 38px**: ${res.smallTapTargets.length}\n`;
      if (res.smallTapTargets.length > 0) {
        res.smallTapTargets.forEach(item => {
          reportMd += `  - \`${item.selector}\` (${item.width}x${item.height}px): "${item.text}"\n`;
        });
      }

      reportMd += `\n`;
    }
  }

  reportMd += `---\n\n## Итого нарушений: ${totalViolations}\n`;
  if (totalViolations === 0) {
    reportMd += `\n✅ **0 нарушений! Интерфейс полностью соответствует требованиям.**\n`;
  }

  fs.writeFileSync(REPORT_PATH, reportMd, 'utf8');
  console.log(`Audit report written to ${REPORT_PATH}`);
  console.log(`Total violations: ${totalViolations}`);

  ws.close();
  chromeProcess.kill();
  try {
    fs.rmSync(TEMP_USER_DATA, { recursive: true, force: true });
  } catch (e) {}

  process.exit(totalViolations === 0 ? 0 : 2);
}

main().catch(err => {
  console.error('Audit failed:', err);
  process.exit(1);
});

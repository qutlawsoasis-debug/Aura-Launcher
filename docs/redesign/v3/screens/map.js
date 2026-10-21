/**
 * Aura Launcher v3 — Map Section Controller (Procedural Minecraft World Canvas)
 * Drag, wheel zoom, POI navigation, animation only when moving.
 */

(function () {
  'use strict';

  let cv = null;
  let ctx = null;
  const N = 256;
  const tiles = document.createElement('canvas');
  tiles.width = tiles.height = N;

  function hash(x, y) {
    const s = Math.sin(x * 127.1 + y * 311.7) * 43758.5453;
    return s - Math.floor(s);
  }

  function noise(x, y) {
    const xi = Math.floor(x), yi = Math.floor(y);
    const xf = x - xi, yf = y - yi;
    const u = xf * xf * (3 - 2 * xf);
    const v = yf * yf * (3 - 2 * yf);
    const a = hash(xi, yi), b = hash(xi + 1, yi);
    const c = hash(xi, yi + 1), d = hash(xi + 1, yi + 1);
    return a + (b - a) * u + (c - a) * v + (a - b - c + d) * u * v;
  }

  function fbm(x, y) {
    let s = 0, a = 0.5, f = 1;
    for (let i = 0; i < 5; i++) {
      s += a * noise(x * f, y * f);
      f *= 2;
      a *= 0.5;
    }
    return s;
  }

  // Pre-generate procedural terrain texture
  (() => {
    const tc = tiles.getContext('2d');
    const img = tc.createImageData(N, N);
    for (let y = 0; y < N; y++) {
      for (let x = 0; x < N; x++) {
        const h = fbm(x / 48, y / 48);
        const m = fbm(x / 30 + 50, y / 30 + 50);
        let c;
        if (h < 0.36) c = [30, 74, 120];
        else if (h < 0.40) c = [44, 98, 150];
        else if (h < 0.42) c = [214, 200, 150];
        else if (h < 0.62) c = m > 0.5 ? [52, 108, 46] : [86, 140, 58];
        else if (h < 0.70) c = [40, 84, 40];
        else if (h < 0.78) c = [118, 118, 118];
        else c = [236, 240, 245];

        const sh = (hash(x, y) - 0.5) * 14;
        const i = (y * N + x) * 4;
        img.data[i] = c[0] + sh;
        img.data[i + 1] = c[1] + sh;
        img.data[i + 2] = c[2] + sh;
        img.data[i + 3] = 255;
      }
    }
    tc.putImageData(img, 0, 0);
  })();

  const pois = [
    { x: 118, y: 132, name: 'Вы', me: 1 },
    { x: 168, y: 96, name: 'zxcursed' },
    { x: 96, y: 160, name: 'Спавн', spawn: 1 }
  ];

  let view = { x: 128, y: 128, z: 5 };
  let target = { ...view };
  let ticking = false;
  const skinImg = new Image();
  skinImg.src = 'assets/images/steve.png';

  function sizeMap() {
    if (!cv) return;
    const r = cv.getBoundingClientRect();
    const dpr = window.devicePixelRatio || 1;
    const w = Math.round(Math.max(1, r.width) * dpr);
    const h = Math.round(Math.max(1, r.height) * dpr);
    if (cv.width !== w || cv.height !== h) {
      cv.width = w;
      cv.height = h;
    }
    if (ctx) {
      ctx.setTransform(dpr, 0, 0, dpr, 0, 0);
    }
  }

  function drawMap() {
    if (!ctx || !cv) return;
    const dpr = window.devicePixelRatio || 1;
    ctx.setTransform(dpr, 0, 0, dpr, 0, 0);

    const cssW = cv.width / dpr;
    const cssH = cv.height / dpr;

    ctx.imageSmoothingEnabled = false;
    ctx.fillStyle = '#0b1622';
    ctx.fillRect(0, 0, cssW, cssH);

    const ox = cssW / 2 - view.x * view.z;
    const oy = cssH / 2 - view.y * view.z;
    ctx.drawImage(tiles, ox, oy, N * view.z, N * view.z);

    pois.forEach(p => {
      const sx = ox + p.x * view.z;
      const sy = oy + p.y * view.z;
      ctx.beginPath();
      ctx.arc(sx, sy, 28, 0, 7);
      ctx.fillStyle = 'rgba(100,210,255,.18)';
      ctx.fill();

      ctx.lineWidth = 3;
      ctx.strokeStyle = p.me ? '#64D2FF' : 'rgba(255,255,255,.9)';
      ctx.beginPath();
      ctx.roundRect(sx - 17, sy - 17, 34, 34, 10);

      if (p.spawn) {
        ctx.fillStyle = 'rgba(14,22,32,.85)';
        ctx.fill();
        ctx.stroke();
        ctx.fillStyle = '#64D2FF';
        ctx.font = '700 15px Onest, sans-serif';
        ctx.textAlign = 'center';
        ctx.fillText('S', sx, sy + 5);
      } else {
        ctx.save();
        ctx.clip();
        ctx.drawImage(skinImg, 8, 8, 8, 8, sx - 17, sy - 17, 34, 34);
        ctx.drawImage(skinImg, 40, 8, 8, 8, sx - 17, sy - 17, 34, 34);
        ctx.restore();
        ctx.stroke();
      }

      ctx.font = '600 14px Onest, sans-serif';
      ctx.textAlign = 'center';
      ctx.fillStyle = '#fff';
      ctx.shadowColor = 'rgba(0,0,0,.8)';
      ctx.shadowBlur = 6;
      ctx.fillText(p.name, sx, sy + 42);
      ctx.shadowBlur = 0;
    });

    const coords = document.getElementById('coords');
    if (coords) {
      coords.textContent = `X ${Math.round((view.x - 128) * 16)} · Z ${Math.round((view.y - 128) * 16)}`;
    }
  }

  // Карта всегда закрывает холст: минимальный масштаб «cover», сдвиг не выходит за край мира.
  function clampTarget() {
    if (!cv) return;
    const dpr = window.devicePixelRatio || 1;
    const cssW = cv.width / dpr;
    const cssH = cv.height / dpr;
    const minZ = Math.max(cssW, cssH) / N;
    target.z = Math.min(14, Math.max(minZ, target.z));
    const halfW = cssW / (2 * target.z);
    const halfH = cssH / (2 * target.z);
    target.x = Math.min(N - halfW, Math.max(halfW, target.x));
    target.y = Math.min(N - halfH, Math.max(halfH, target.y));
  }

  function tick() {
    clampTarget();
    let moved = false;
    for (const k of ['x', 'y', 'z']) {
      const d = target[k] - view[k];
      if (Math.abs(d) > 0.002) {
        view[k] += d * 0.14;
        moved = true;
      }
    }
    drawMap();
    const isMapOpen = document.body.classList.contains('is-open') && 
      document.querySelector('[data-l="map"]')?.classList.contains('on');
    ticking = moved && isMapOpen && !document.hidden;
    if (ticking) requestAnimationFrame(tick);
  }

  function kick() {
    if (!ticking) {
      ticking = true;
      requestAnimationFrame(tick);
    }
  }

  function initMap() {
    cv = document.getElementById('mapCanvas');
    if (!cv) return;
    ctx = cv.getContext('2d');

    sizeMap();
    drawMap();

    let drag = null;
    cv.addEventListener('mousedown', (e) => {
      drag = { x: e.clientX, y: e.clientY, vx: target.x, vy: target.y };
    });

    window.addEventListener('mouseup', () => {
      drag = null;
    });

    window.addEventListener('mousemove', (e) => {
      if (drag) {
        target.x = drag.vx - (e.clientX - drag.x) / view.z;
        target.y = drag.vy - (e.clientY - drag.y) / view.z;
        kick();
      }
    });

    cv.addEventListener('wheel', (e) => {
      e.preventDefault();
      target.z *= e.deltaY < 0 ? 1.15 : 0.87;
      kick();
    }, { passive: false });

    document.getElementById('zin')?.addEventListener('click', () => {
      target.z *= 1.3;
      kick();
    });

    document.getElementById('zout')?.addEventListener('click', () => {
      target.z /= 1.3;
      kick();
    });

    document.querySelectorAll('[data-poi]').forEach(r => {
      r.addEventListener('click', () => {
        const p = pois[parseInt(r.dataset.poi, 10)];
        if (p) {
          target.x = p.x;
          target.y = p.y;
          target.z = 9;
          kick();
        }
      });
    });

    window.addEventListener('resize', () => {
      sizeMap();
      clampTarget();
      view = { ...target };
      drawMap();
    });

    if (window.ResizeObserver && cv) {
      const ro = new ResizeObserver(() => {
        sizeMap();
        clampTarget();
        view = { ...target };
        drawMap();
      });
      ro.observe(cv);
    }
  }

  window.renderMapNow = function () {
    sizeMap();
    drawMap();
    kick();
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initMap);
  } else {
    initMap();
  }
})();

/**
 * Aura Launcher v3 — Theme System Registry & Controller
 * Unified registry for material themes: Frost (Матовое стекло) & Liquid (Жидкое стекло).
 * Handles instant bootstrap, safe persistence, host WebBridge sync, and keyboard-accessible UI.
 */

export const THEMES = [
  {
    id: 'frost',
    name: 'Матовое стекло',
    description: 'Глубокое размытие 44px и мягкие тени',
    previewClass: 'preview-frost'
  },
  {
    id: 'liquid',
    name: 'Жидкое стекло',
    description: 'Прозрачный преломляющий материал с акцентным ободком',
    previewClass: 'preview-liquid'
  }
];

export const DEFAULT_THEME_ID = 'frost';

/**
 * Checks if a given theme ID is registered in the allowlist.
 */
export function isValidThemeId(id) {
  if (typeof id !== 'string') return false;
  return THEMES.some(t => t.id === id);
}

/**
 * Returns the currently active theme ID from the DOM or default.
 */
export function getCurrentThemeId() {
  const current = document.documentElement.getAttribute('data-theme');
  return isValidThemeId(current) ? current : DEFAULT_THEME_ID;
}

/**
 * Returns the list of registered themes.
 */
export function getAvailableThemes() {
  return [...THEMES];
}

/**
 * Applies a theme by its ID.
 * @param {string} id
 * @param {object} [options]
 * @param {boolean} [options.save=true] - Save to localStorage
 * @param {boolean} [options.sendToHost=true] - Sync with C# host via WebBridge
 * @returns {string} The applied theme ID
 */
export function applyTheme(id, { save = true, sendToHost = true } = {}) {
  const targetId = isValidThemeId(id) ? id : DEFAULT_THEME_ID;
  const prevId = getCurrentThemeId();

  document.documentElement.setAttribute('data-theme', targetId);

  if (save) {
    try {
      localStorage.setItem('aura_theme', targetId);
    } catch (_) {}
  }

  // Update cursor specular tracker state
  updateSpecularTracker(targetId);

  // If liquid theme is applied, verify SVG filter support
  if (targetId === 'liquid') {
    verifySvgFilterSupport();
  }

  // Sync to host if bridge is available
  if (sendToHost && window.AuraBridge && typeof window.AuraBridge.send === 'function') {
    window.AuraBridge.send('config.set', { key: 'theme', value: targetId })
      .catch(err => {
        console.warn('[THEMES] WebBridge theme sync warning:', err);
      });
  }

  // Dispatch custom event for reactive components
  try {
    const event = new CustomEvent('aura:themechange', {
      detail: { theme: targetId, previousTheme: prevId }
    });
    window.dispatchEvent(event);
  } catch (_) {}

  return targetId;
}

/**
 * Verifies whether SVG backdrop filters are supported and functioning.
 * If SVG backdrop-filter fails (or SVG element is absent), sets class
 * 'no-svg-filter' on documentElement to activate the robust CSS cascade fallback.
 */
export function verifySvgFilterSupport() {
  if (typeof document === 'undefined') return;
  try {
    const isSupported = (typeof CSS !== 'undefined' && CSS.supports) && (
      CSS.supports('backdrop-filter', 'url("#auraLiquidFilter")') ||
      CSS.supports('-webkit-backdrop-filter', 'url("#auraLiquidFilter")')
    );
    const filterEl = document.getElementById('auraLiquidFilter');
    if (!isSupported || !filterEl) {
      document.documentElement.classList.add('no-svg-filter');
    } else {
      document.documentElement.classList.remove('no-svg-filter');
    }
  } catch (_) {
    document.documentElement.classList.add('no-svg-filter');
  }
}

/**
 * Initializes theme before first paint / early in page lifecycle.
 */
export function initThemeBootstrap() {
  let resolved = DEFAULT_THEME_ID;

  try {
    // 1. URL search params have highest immediate priority (e.g. ?theme=liquid)
    const urlParams = new URLSearchParams(window.location.search);
    const urlTheme = urlParams.get('theme');
    if (isValidThemeId(urlTheme)) {
      resolved = urlTheme;
    }
    // 2. Pre-navigation bootstrap injected by C# host
    else if (window.__AURA_BOOTSTRAP_THEME__ && isValidThemeId(window.__AURA_BOOTSTRAP_THEME__)) {
      resolved = window.__AURA_BOOTSTRAP_THEME__;
    }
    // 3. Stored user choice in localStorage
    else {
      const stored = localStorage.getItem('aura_theme');
      if (isValidThemeId(stored)) {
        resolved = stored;
      }
    }
  } catch (_) {}

  applyTheme(resolved, { save: true, sendToHost: false });
  verifySvgFilterSupport();
  initSpecularTracker();
  initLiquidDisplacement();
}

/* ================= 4. SPECULAR CURSOR TRACKING (LIQUID FULL EFFECTS) ================= */
let specularRafId = null;
let specularIslandEl = null;

function onIslandPointerMove(e) {
  if (!specularIslandEl) return;
  if (document.documentElement.getAttribute('data-theme') !== 'liquid') return;
  if (document.documentElement.getAttribute('data-effects') !== 'full') return;

  const rect = specularIslandEl.getBoundingClientRect();
  const x = e.clientX - rect.left;
  const y = e.clientY - rect.top;

  if (specularRafId) cancelAnimationFrame(specularRafId);
  specularRafId = requestAnimationFrame(() => {
    if (!specularIslandEl) return;
    specularIslandEl.style.setProperty('--cursor-x', `${Math.round(x)}px`);
    specularIslandEl.style.setProperty('--cursor-y', `${Math.round(y)}px`);
    specularIslandEl.style.setProperty('--specular-opacity', '1');
  });
}

function onIslandPointerLeave() {
  if (!specularIslandEl) return;
  if (specularRafId) cancelAnimationFrame(specularRafId);
  specularIslandEl.style.setProperty('--specular-opacity', '0');
}

function updateSpecularTracker(themeId) {
  if (!specularIslandEl) return;
  if (themeId !== 'liquid') {
    onIslandPointerLeave();
  }
}

function initSpecularTracker() {
  if (typeof document === 'undefined') return;

  const setup = () => {
    specularIslandEl = document.getElementById('island');
    if (!specularIslandEl) return;

    specularIslandEl.addEventListener('pointerenter', onIslandPointerMove, { passive: true });
    specularIslandEl.addEventListener('pointermove', onIslandPointerMove, { passive: true });
    specularIslandEl.addEventListener('pointerleave', onIslandPointerLeave, { passive: true });

    window.addEventListener('blur', onIslandPointerLeave, { passive: true });
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', setup, { once: true });
  } else {
    setup();
  }
}

/* ================= 5. LIQUID EDGE DISPLACEMENT MAP GENERATOR ================= */
let lastEdgeMapKey = '';
let liquidResizeObserver = null;

export function updateLiquidDisplacementMap(width, height, borderRadius) {
  if (typeof document === 'undefined') return;
  const feImage = document.getElementById('auraLiquidEdgeMap');
  if (!feImage) return;

  const w = Math.max(64, Math.round(width || 400));
  const h = Math.max(32, Math.round(height || 76));
  const r = Math.max(0, Math.min(Math.min(w, h) / 2, Math.round(borderRadius ?? 38)));
  const rim = 16;
  const key = `${w}x${h}r${r}`;
  if (key === lastEdgeMapKey) return;
  lastEdgeMapKey = key;

  try {
    const canvas = document.createElement('canvas');
    canvas.width = w;
    canvas.height = h;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    const imgData = ctx.createImageData(w, h);
    const buf32 = new Uint32Array(imgData.data.buffer);
    buf32.fill(0xFF808080); // Little-endian 32-bit: Alpha=255, Blue=128, Green=128, Red=128

    const setPixel = (x, y, nx, ny, dist) => {
      if (x < 0 || x >= w || y < 0 || y >= h) return;
      const factor = Math.max(0, 1 - dist / rim);
      const R = Math.round(128 + nx * 127 * factor);
      const G = Math.round(128 + ny * 127 * factor);
      buf32[y * w + x] = 0xFF000000 | (128 << 16) | (G << 8) | R;
    };

    // Straight edges:
    // Top: inward is downward (+y, ny = 1, nx = 0)
    // Bottom: inward is upward (-y, ny = -1, nx = 0)
    for (let x = r; x < w - r; x++) {
      for (let d = 0; d < rim; d++) {
        setPixel(x, d, 0, 1, d);
        setPixel(x, h - 1 - d, 0, -1, d);
      }
    }

    // Left: inward is rightward (+x, nx = 1, ny = 0)
    // Right: inward is leftward (-x, nx = -1, ny = 0)
    for (let y = r; y < h - r; y++) {
      for (let d = 0; d < rim; d++) {
        setPixel(d, y, 1, 0, d);
        setPixel(w - 1 - d, y, -1, 0, d);
      }
    }

    // 4 rounded corners:
    const corners = [
      { cx: r, cy: r },
      { cx: w - 1 - r, cy: r },
      { cx: r, cy: h - 1 - r },
      { cx: w - 1 - r, cy: h - 1 - r }
    ];

    for (const c of corners) {
      const minX = Math.max(0, c.cx - r);
      const maxX = Math.min(w - 1, c.cx + r);
      const minY = Math.max(0, c.cy - r);
      const maxY = Math.min(h - 1, c.cy + r);
      const isLeft = c.cx < w / 2;
      const isTop = c.cy < h / 2;

      for (let y = minY; y <= maxY; y++) {
        for (let x = minX; x <= maxX; x++) {
          const vx = x - c.cx;
          const vy = y - c.cy;
          if ((isLeft ? vx <= 0 : vx >= 0) && (isTop ? vy <= 0 : vy >= 0)) {
            const distCenter = Math.hypot(vx, vy);
            const distBorder = r - distCenter;
            if (distBorder >= 0 && distBorder < rim && distCenter > 0) {
              const nx = -vx / distCenter;
              const ny = -vy / distCenter;
              setPixel(x, y, nx, ny, distBorder);
            }
          }
        }
      }
    }

    ctx.putImageData(imgData, 0, 0);
    const dataUrl = canvas.toDataURL('image/png');
    feImage.setAttribute('href', dataUrl);
    feImage.setAttributeNS('http://www.w3.org/1999/xlink', 'xlink:href', dataUrl);
  } catch (err) {
    console.warn('[THEMES] updateLiquidDisplacementMap error:', err);
  }
}

export function initLiquidDisplacement() {
  if (typeof document === 'undefined') return;

  const refreshMap = () => {
    if (document.documentElement.getAttribute('data-theme') !== 'liquid') return;
    const island = document.getElementById('island');
    if (!island) return;
    const rect = island.getBoundingClientRect();
    const style = window.getComputedStyle(island);
    const br = parseFloat(style.borderRadius) || (island.classList.contains('open') ? 34 : 38);
    updateLiquidDisplacementMap(rect.width, rect.height, br);
  };

  const setup = () => {
    const island = document.getElementById('island');
    if (!island) return;

    if (typeof ResizeObserver !== 'undefined') {
      if (liquidResizeObserver) liquidResizeObserver.disconnect();
      liquidResizeObserver = new ResizeObserver(() => {
        refreshMap();
      });
      liquidResizeObserver.observe(island);
    }

    island.addEventListener('transitionend', (e) => {
      if (e.propertyName === 'width' || e.propertyName === 'height') {
        refreshMap();
      }
    });

    window.addEventListener('aura:themechange', (e) => {
      if (e.detail && e.detail.theme === 'liquid') {
        refreshMap();
      }
    });

    refreshMap();
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', setup, { once: true });
  } else {
    setup();
  }
}

// Automatically bootstrap theme immediately upon evaluation
if (typeof document !== 'undefined') {
  initThemeBootstrap();
}

// Expose globally on window for inline scripts and WebBridge integration
if (typeof window !== 'undefined') {
  window.AuraThemes = {
    THEMES,
    DEFAULT_THEME_ID,
    isValidThemeId,
    getCurrentThemeId,
    getAvailableThemes,
    applyTheme,
    initThemeBootstrap,
    verifySvgFilterSupport,
    updateLiquidDisplacementMap,
    initLiquidDisplacement
  };
}

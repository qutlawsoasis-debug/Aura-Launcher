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
    verifySvgFilterSupport
  };
}

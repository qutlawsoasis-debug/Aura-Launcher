/**
 * Aura Launcher v3 — Core JavaScript Architecture (Island Concept)
 * Provides: Island state machine, Router, Effects, Toast Dispatcher, Tone Detector.
 */

(function () {
  'use strict';

  // 1. Effects State & Initialization
  const DEFAULT_EFFECTS_MODE = 'full';
  const reducedMotionQuery = window.matchMedia('(prefers-reduced-motion: reduce)');

  function getQueryOrHashParam(name) {
    const searchParams = new URLSearchParams(window.location.search);
    if (searchParams.has(name)) return searchParams.get(name);
    const hash = window.location.hash.split('?')[1];
    if (hash) {
      const hashParams = new URLSearchParams(hash);
      return hashParams.get(name);
    }
    return null;
  }

  function resolveEffectsMode() {
    const param = getQueryOrHashParam('effects');
    if (param && ['full', 'low', 'off'].includes(param)) return param;
    if (reducedMotionQuery.matches) return 'off';
    const saved = localStorage.getItem('aura_effects_mode_v3');
    if (saved && ['full', 'low', 'off'].includes(saved)) return saved;
    return DEFAULT_EFFECTS_MODE;
  }

  let currentEffectsMode = resolveEffectsMode();

  window.setEffectsMode = function (mode) {
    if (!['full', 'low', 'off'].includes(mode)) return;
    currentEffectsMode = mode;
    document.documentElement.setAttribute('data-effects', mode);
    try {
      localStorage.setItem('aura_effects_mode_v3', mode);
    } catch (e) {}

    document.querySelectorAll('[data-effect-btn], [data-fx]').forEach(btn => {
      const btnMode = btn.dataset.effectBtn || btn.dataset.fx;
      const isActive = btnMode === mode;
      btn.classList.toggle('active', isActive);
      btn.classList.toggle('on', isActive);
      btn.setAttribute('aria-checked', isActive ? 'true' : 'false');
    });
  };

  // 2. Island State Machine & Layout Sizing
  const island = document.getElementById('island');
  const sections = ['map', 'lobby', 'friends', 'wardrobe', 'workshop', 'settings'];
  const names = { map: 'Карта', lobby: 'Лобби', friends: 'Друзья', wardrobe: 'Гардероб', workshop: 'Мастерская', settings: 'Настройки' };

  const sizes = {
    idle: [400, 76],
    nav: [840, 76],
    running: [340, 76],
    lobby: [700, 560],
    friends: [700, 640],
    wardrobe: [700, 520],
    workshop: [700, 580],
    settings: [700, 680],
    map: [() => window.innerWidth - 120, () => window.innerHeight - 150]
  };

  let islandMode = 'idle';

  function initSectionFooters() {
    document.querySelectorAll('.sec-foot').forEach(foot => {
      foot.innerHTML = sections.map(s => `<button type="button" data-open="${s}">${names[s]}</button>`).join('') +
        '<button type="button" class="play" data-play style="margin-left:auto"><div class="fill"></div><span><span class="tri"></span>Играть</span></button>';
    });
  }

  window.setIslandMode = function (m, updateHash = true) {
    if (!sizes[m]) return;
    islandMode = m;

    const [w, h] = sizes[m].map(v => typeof v === 'function' ? v() : v);
    const maxH = window.innerHeight - 110;
    const maxW = window.innerWidth - 60;

    if (island) {
      island.style.width = Math.min(w, maxW) + 'px';
      island.style.height = Math.min(h, maxH) + 'px';
      island.classList.toggle('open', sections.includes(m));
      island.classList.toggle('nav', m === 'nav');
      island.classList.toggle('running', m === 'running');
    }

    document.body.classList.toggle('is-open', sections.includes(m));
    document.body.classList.toggle('is-idle', m === 'idle');

    document.querySelectorAll('.layer').forEach(l => {
      l.classList.toggle('on', l.dataset.l === m);
    });

    document.querySelectorAll('.sec-foot button[data-open]').forEach(b => {
      b.classList.toggle('on', b.dataset.open === m);
    });

    if (updateHash) {
      if (sections.includes(m)) {
        window.location.hash = `#/${m}`;
      } else {
        window.location.hash = '#/';
      }
    }

    // Update segmented lens positions on layer change
    requestAnimationFrame(() => {
      document.querySelectorAll(`[data-l="${m}"] .seg`).forEach(window.placeLens);
    });
  };

  // Segmented lens & toggle controllers
  window.placeLens = function (seg) {
    const onBtn = seg.querySelector('button.on');
    const lens = seg.querySelector('.lens');
    if (onBtn && lens) {
      lens.style.left = onBtn.offsetLeft + 'px';
      lens.style.width = onBtn.offsetWidth + 'px';
    }
  };

  function initComponentInteractions() {
    // Toggles
    document.addEventListener('click', (e) => {
      const toggle = e.target.closest('.toggle');
      if (toggle) {
        toggle.classList.toggle('on');
        return;
      }

      // Reveals (e.g. joinBox, addBox)
      const revealBtn = e.target.closest('[data-reveal]');
      if (revealBtn) {
        const targetId = revealBtn.dataset.reveal;
        const targetEl = document.getElementById(targetId);
        if (targetEl) targetEl.classList.toggle('open');
        return;
      }

      // Segmented controls
      const segBtn = e.target.closest('.seg button');
      if (segBtn) {
        const seg = segBtn.closest('.seg');
        if (seg) {
          seg.querySelectorAll('button').forEach(b => b.classList.remove('on'));
          segBtn.classList.add('on');
          window.placeLens(seg);
        }
      }
    });

    window.addEventListener('resize', () => {
      document.querySelectorAll('.seg').forEach(window.placeLens);
    });
  }

  // 3. Router logic
  function handleHashChange() {
    const raw = window.location.hash.replace(/^#\/?/, '').split('?')[0].toLowerCase();
    if (sections.includes(raw)) {
      window.setIslandMode(raw, false);
    } else {
      const running = typeof window.isGameRunning === 'function' && window.isGameRunning();
      window.setIslandMode(running ? 'running' : 'idle', false);
    }
  }

  window.addEventListener('hashchange', handleHashChange);

  // 4. Click & Key event bindings
  function initIslandInteractions() {
    if (!island) return;

    island.addEventListener('mouseenter', () => {
      if (islandMode === 'idle') {
        window.setIslandMode('nav', false);
      }
    });

    island.addEventListener('mouseleave', () => {
      if (islandMode === 'nav') {
        window.setIslandMode('idle', false);
      }
    });

    island.addEventListener('click', (e) => {
      const openBtn = e.target.closest('[data-open]');
      if (openBtn) {
        window.setIslandMode(openBtn.dataset.open, true);
        return;
      }

      if (e.target.closest('[data-close]')) {
        const running = typeof window.isGameRunning === 'function' && window.isGameRunning();
        window.setIslandMode(running ? 'running' : 'idle', true);
        return;
      }

      const playBtn = e.target.closest('[data-play]');
      if (playBtn) {
        if (typeof window.startGame === 'function') {
          window.startGame(playBtn);
        }
        return;
      }

      const stopBtn = e.target.closest('[data-stop]');
      if (stopBtn) {
        if (typeof window.stopGame === 'function') {
          window.stopGame();
        }
        return;
      }

      const copyBtn = e.target.closest('[data-copy]');
      if (copyBtn) {
        window.showToast('Код скопирован в буфер обмена', 'success');
        return;
      }
    });

    window.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && sections.includes(islandMode)) {
        const running = typeof window.isGameRunning === 'function' && window.isGameRunning();
        window.setIslandMode(running ? 'running' : 'idle', true);
      }
    });

    window.addEventListener('resize', () => {
      window.setIslandMode(islandMode, false);
    });
  }

  // 5. Toast System
  window.showToast = function (message, type = 'info') {
    const container = document.getElementById('toastContainer');
    if (!container) return;

    const toast = document.createElement('div');
    toast.className = `toast-item toast-${type}`;
    toast.textContent = message;
    container.appendChild(toast);

    requestAnimationFrame(() => {
      toast.classList.add('is-visible');
    });

    setTimeout(() => {
      toast.classList.remove('is-visible');
      setTimeout(() => toast.remove(), 400);
    }, 3000);
  };

  // 6. Tone detector on Island
  let detectDebounceTimer = null;
  window.detectGlassTone = function () {
    const islandEl = document.getElementById('island');
    if (!islandEl) return;
    // Default to dark glass tone
    islandEl.setAttribute('data-tone', 'dark');
  };

  // 7. Initialization
  document.addEventListener('DOMContentLoaded', () => {
    window.setEffectsMode(currentEffectsMode);
    initSectionFooters();
    initIslandInteractions();
    initComponentInteractions();
    handleHashChange();
    window.detectGlassTone();
  });
})();

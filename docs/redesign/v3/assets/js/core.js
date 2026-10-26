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
    launching: [460, 76],
    running: [340, 76],
    lobby: [700, 560],
    friends: [700, 640],
    wardrobe: [700, 520],
    workshop: [700, 580],
    settings: [700, 680],
    map: [() => window.innerWidth - 120, () => window.innerHeight - 150]
  };

  let islandMode = 'idle';

  // Высота раздела по содержимому: заголовок + всё тело + нижняя строка, без лишней прокрутки.
  // Число в sizes — запас на случай, если раздел ещё не отрисован.
  const MIN_SECTION_H = 380;
  function naturalSectionHeight(m) {
    const layer = document.querySelector(`.layer[data-l="${m}"]`);
    const sec = layer && layer.querySelector('.sec');
    const head = sec && sec.querySelector('.sec-head');
    const body = sec && sec.querySelector('.sec-body');
    const foot = sec && sec.querySelector('.sec-foot');
    if (!sec || !head || !body || !foot) return 0;
    // .sec-body — сам скролл-контейнер (overflow-y:auto): прежний замер через
    // flex:none + height:auto мгновенно клампил scrollTop в 0, и любая прокрутка
    // раздела сбрасывалась при каждом fitIsland (фоновые обновления, resize).
    // Замер через padding-bottom: scrollHeight только растёт, клампа нет,
    // значение эквивалентно прежнему (scrollHeight - PROBE).
    const SCROLL_PROBE = 1048576;
    const prevPaddingBottom = body.style.paddingBottom;
    // Проба пишется inline поверх каскада, поэтому базой берём фактический computed
    // padding-bottom (например, 10px из stylesheet), а не inline-значение — иначе
    // замер теряет исходный паддинг контейнера.
    const computedPadBottom = parseFloat(getComputedStyle(body).paddingBottom) || 0;
    body.style.paddingBottom = computedPadBottom + SCROLL_PROBE + 'px';
    const bodyH = body.scrollHeight - SCROLL_PROBE;
    body.style.paddingBottom = prevPaddingBottom;
    const cs = getComputedStyle(sec);
    const headCs = getComputedStyle(head);
    return Math.ceil(parseFloat(cs.paddingTop) + head.offsetHeight + parseFloat(headCs.marginBottom) + bodyH + foot.offsetHeight + 4);
  }

  function initSectionFooters() {
    document.querySelectorAll('.sec-foot').forEach(foot => {
      foot.innerHTML = sections.map(s => `<button type="button" data-open="${s}">${names[s]}</button>`).join('') +
        '<button type="button" class="play" data-play style="margin-left:auto"><div class="fill"></div><span><span class="tri"></span>Играть</span></button>';
    });
  }

  // Подогнать высоту открытого раздела, когда его содержимое изменилось (вкладка, форма, список)
  window.fitIsland = function () {
    if (!island || !sections.includes(islandMode) || islandMode === 'map') return;
    const natural = naturalSectionHeight(islandMode);
    if (!natural) return;
    const h = Math.min(Math.max(MIN_SECTION_H, natural), window.innerHeight - 110);
    if (Math.abs(parseFloat(island.style.height) - h) > 1) island.style.height = h + 'px';
  };

  window.setIslandMode = function (m, updateHash = true) {
    if (!sizes[m]) return;
    islandMode = m;

    const [w, h] = sizes[m].map(v => typeof v === 'function' ? v() : v);
    const maxH = window.innerHeight - 110;
    const maxW = window.innerWidth - 60;

    // Prepare content-visibility & active layer before transition
    const targetLayer = document.querySelector(`.layer[data-l="${m}"]`);
    if (targetLayer) {
      targetLayer.style.contentVisibility = 'visible';
    }

    if (island) {
      let targetH = h;
      if (sections.includes(m) && m !== 'map') {
        const natural = naturalSectionHeight(m);
        if (natural > 0) targetH = Math.max(MIN_SECTION_H, natural);
      }
      island.style.width = Math.min(w, maxW) + 'px';
      island.style.height = Math.min(targetH, maxH) + 'px';
      island.classList.toggle('open', sections.includes(m));
      island.classList.toggle('nav', m === 'nav');
      island.classList.toggle('launching', m === 'launching');
      island.classList.toggle('running', m === 'running');
    }

    document.body.classList.toggle('is-open', sections.includes(m));
    document.body.classList.toggle('is-idle', m === 'idle');

    document.querySelectorAll('.layer').forEach(l => {
      const isOn = l.dataset.l === m;
      l.classList.toggle('on', isOn);
      if (isOn) {
        l.style.contentVisibility = 'visible';
        l.removeAttribute('inert');
        l.removeAttribute('aria-hidden');
      } else {
        l.setAttribute('inert', '');
        l.setAttribute('aria-hidden', 'true');
        // After fade-out transition, hide content-visibility to save layout/draw passes
        setTimeout(() => {
          if (!l.classList.contains('on')) {
            l.style.contentVisibility = 'hidden';
          }
        }, 300);
      }
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
      if (m === 'map' && typeof window.renderMapNow === 'function') {
        window.renderMapNow();
      }
    });
  };

  // Segmented lens & toggle controllers
  window.placeLens = function (seg) {
    if (!seg) return;
    const onBtn = seg.querySelector('button.on');
    const lens = seg.querySelector('.lens');
    if (onBtn && lens) {
      lens.style.left = onBtn.offsetLeft + 'px';
      lens.style.width = onBtn.offsetWidth + 'px';
    }
  };

  function initComponentInteractions() {
    // ResizeObserver for segmented controls to keep lens perfectly positioned
    if (typeof ResizeObserver !== 'undefined') {
      const segObserver = new ResizeObserver((entries) => {
        for (const entry of entries) {
          window.placeLens(entry.target);
        }
      });
      document.querySelectorAll('.seg').forEach(seg => segObserver.observe(seg));
    }

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
      if (raw !== '') {
        try {
          history.replaceState(null, '', '#/');
        } catch (e) {
          window.location.hash = '#/';
        }
      }
      const running = typeof window.isGameRunning === 'function' && window.isGameRunning();
      window.setIslandMode(running ? 'running' : 'idle', false);
    }
  }

  window.addEventListener('hashchange', handleHashChange);

  // 4. Click & Key event bindings
  function initIslandInteractions() {
    if (!island) return;

    // Содержимое раздела поменялось — подгоняем высоту капсулы сразу и после анимаций раскрытия
    let fitTimer = 0;
    let fitLate = 0;
    new MutationObserver(() => {
      if (!sections.includes(islandMode)) return;
      clearTimeout(fitTimer);
      clearTimeout(fitLate);
      fitTimer = setTimeout(window.fitIsland, 40);
      fitLate = setTimeout(window.fitIsland, 420);
    }).observe(island, { subtree: true, childList: true, attributes: true, attributeFilter: ['class', 'hidden', 'data-view'] });
    window.addEventListener('resize', () => window.fitIsland());

    function resolveTargetModeAfterClose() {
      if (typeof window.isGameRunning === 'function' && window.isGameRunning()) return 'running';
      if (typeof window.isGameLaunching === 'function' && window.isGameLaunching()) return 'launching';
      return 'idle';
    }

    // Меню раскрывается по задержке курсора на капсуле, но не над «Играть»:
    // иначе кнопка уезжает из-под курсора на ~220px и клик попадает в пункт меню.
    // Таймер запускается один раз и не сбрасывается от движения мыши — меню открывается,
    // даже пока курсор ещё едет. Уход за край закрывает с запасом: во время морфа край
    // капсулы «убегает» из-под курсора, и без запаса она дёргалась (закрыть → открыть).
    const OPEN_DELAY = 120;
    const CLOSE_GRACE = 200;
    let hoverIntent = 0;
    let leaveTimer = 0;
    const canExpand = () => {
      const isLaunching = typeof window.isGameLaunching === 'function' && window.isGameLaunching();
      const isRunning = typeof window.isGameRunning === 'function' && window.isGameRunning();
      return !isLaunching && !isRunning && islandMode === 'idle';
    };
    const cancelOpen = () => { clearTimeout(hoverIntent); hoverIntent = 0; };
    island.addEventListener('pointermove', (e) => {
      clearTimeout(leaveTimer);
      if (!canExpand()) { cancelOpen(); return; }
      if (e.target.closest('[data-play]')) { cancelOpen(); return; }
      if (hoverIntent) return;
      hoverIntent = setTimeout(() => {
        hoverIntent = 0;
        if (canExpand()) window.setIslandMode('nav', false);
      }, OPEN_DELAY);
    });

    island.addEventListener('pointerleave', () => {
      cancelOpen();
      clearTimeout(leaveTimer);
      leaveTimer = setTimeout(() => {
        if (islandMode === 'nav' && !island.matches(':hover')) {
          window.setIslandMode('idle', false);
        }
      }, CLOSE_GRACE);
    });

    island.addEventListener('click', (e) => {
      const openBtn = e.target.closest('[data-open]');
      if (openBtn) {
        window.setIslandMode(openBtn.dataset.open, true);
        return;
      }

      if (e.target.closest('[data-close]')) {
        window.setIslandMode(resolveTargetModeAfterClose(), true);
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
    });

    window.addEventListener('keydown', (e) => {
      if (e.key === 'Escape' && sections.includes(islandMode)) {
        window.setIslandMode(resolveTargetModeAfterClose(), true);
      }
    });

    window.addEventListener('resize', () => {
      window.setIslandMode(islandMode, false);
    });
  }

  // 4b. Window Chrome Controls & Dragging
  function initWindowControls() {
    const bridge = window.AuraBridge;
    const winControls = document.getElementById('winControls');
    const dragBar = document.getElementById('dragBar');

    function sendWinAction(action) {
      if (bridge && typeof bridge.send === 'function') {
        bridge.send('window.action', { action }).catch(() => {});
      }
    }

    if (winControls) {
      winControls.addEventListener('click', (e) => {
        const btn = e.target.closest('[data-win]');
        if (!btn) return;
        const action = btn.dataset.win;
        sendWinAction(action);
      });
    }

    if (dragBar) {
      dragBar.addEventListener('dblclick', () => {
        sendWinAction('maximize');
      });
    }

    if (bridge && typeof bridge.on === 'function') {
      bridge.on('window.state', (data) => {
        const state = (data?.state || '').toLowerCase();
        const maxBtn = document.querySelector('[data-win="maximize"]');
        if (maxBtn) {
          const isMax = state === 'maximized';
          const iconMax = maxBtn.querySelector('.icon-max');
          const iconRestore = maxBtn.querySelector('.icon-restore');
          if (iconMax) iconMax.style.display = isMax ? 'none' : 'block';
          if (iconRestore) iconRestore.style.display = isMax ? 'block' : 'none';
          maxBtn.setAttribute('aria-label', isMax ? 'Восстановить' : 'Развернуть');
        }
      });
    }
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

  // 5b. Плавная перестановка списка (FLIP): оставшиеся строки переезжают, ушедшие гаснут на месте,
  // новые проявляются по очереди. mutate() меняет видимость/порядок; followers — элементы под списком.
  window.animateListChange = function (rows, mutate, followers = []) {
    const effects = document.documentElement.dataset.effects;
    if (effects === 'off' || !Element.prototype.animate) { mutate(); return; }

    const tracked = [...rows, ...followers];
    const before = new Map();
    tracked.forEach(el => { if (!el.hidden) before.set(el, el.getBoundingClientRect()); });

    mutate();

    const ease = 'cubic-bezier(0.22, 1, 0.36, 1)';
    let entering = 0;
    tracked.forEach(el => {
      const was = before.get(el);
      if (el.hidden) {
        if (!was) return;
        // Призрак ушедшей строки гаснет там, где она была
        const ghost = el.cloneNode(true);
        ghost.hidden = false;
        ghost.removeAttribute('id');
        Object.assign(ghost.style, {
          position: 'fixed', left: `${was.left}px`, top: `${was.top}px`,
          width: `${was.width}px`, height: `${was.height}px`, margin: '0',
          pointerEvents: 'none', zIndex: '30'
        });
        document.body.appendChild(ghost);
        ghost.animate([{ opacity: 1, transform: 'scale(1)' }, { opacity: 0, transform: 'scale(0.98)' }],
          { duration: 180, easing: 'ease-out', fill: 'forwards' }).onfinish = () => ghost.remove();
        return;
      }
      const now = el.getBoundingClientRect();
      if (was) {
        const dy = was.top - now.top;
        if (Math.abs(dy) > 0.5) {
          el.animate([{ transform: `translateY(${dy}px)` }, { transform: 'translateY(0)' }],
            { duration: 420, easing: ease });
        }
      } else {
        el.animate([{ opacity: 0, transform: 'translateY(8px)' }, { opacity: 1, transform: 'translateY(0)' }],
          { duration: 320, easing: ease, delay: 60 + Math.min(entering++, 7) * 30, fill: 'backwards' });
      }
    });
  };

  // 5c. Смена панели внутри раздела: старая гаснет, новая проявляется строками по очереди
  window.swapPane = function (outPane, inPane) {
    if (!inPane || outPane === inPane) return;
    const animated = document.documentElement.dataset.effects !== 'off' && Element.prototype.animate;
    const showIn = () => {
      if (outPane) outPane.hidden = true;
      inPane.hidden = false;
      if (!animated) return;
      const items = inPane.children.length > 1 ? Array.from(inPane.children) : [inPane];
      items.forEach((el, i) => el.animate(
        [{ opacity: 0, transform: 'translateY(8px)' }, { opacity: 1, transform: 'translateY(0)' }],
        { duration: 320, easing: 'cubic-bezier(0.22, 1, 0.36, 1)', delay: Math.min(i, 7) * 30, fill: 'backwards' }));
    };
    if (!animated || !outPane || outPane.hidden) { showIn(); return; }
    outPane.animate([{ opacity: 1 }, { opacity: 0, transform: 'translateY(-4px)' }],
      { duration: 130, easing: 'ease-out', fill: 'forwards' }).onfinish = function () { this.cancel(); showIn(); };
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
    initWindowControls();
    handleHashChange();
    window.detectGlassTone();
  });
})();

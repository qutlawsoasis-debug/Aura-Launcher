/**
 * Aura Launcher v3 — Core JavaScript Architecture
 * Provides: Router, Effects Engine, Audio Visualizer, Modals & Toast Dispatcher
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

    // Update any UI segmented controls representing effects mode
    document.querySelectorAll('[data-effect-btn]').forEach(btn => {
      const isActive = btn.dataset.effectBtn === mode;
      btn.classList.toggle('active', isActive);
      btn.setAttribute('aria-checked', isActive ? 'true' : 'false');
    });

    if (mode === 'full' && !reducedMotionQuery.matches && !document.hidden) {
      startAmbientParticles();
    } else {
      stopAmbientParticles();
    }
  };

  // 2. Ambient Particles & Parallax
  const worldBg = document.getElementById('worldBg');
  const sunbeam = document.getElementById('sunbeamLight');
  const canvas = document.getElementById('ambientCanvas');
  let ctx = canvas ? canvas.getContext('2d') : null;
  let particles = [];
  let animFrameId = null;

  function resizeCanvas() {
    if (!canvas) return;
    canvas.width = window.innerWidth;
    canvas.height = window.innerHeight;
  }
  window.addEventListener('resize', resizeCanvas);
  resizeCanvas();

  function initParticles() {
    if (!canvas) return;
    particles = [];
    const count = 38;
    for (let i = 0; i < count; i++) {
      particles.push({
        x: Math.random() * canvas.width,
        y: Math.random() * canvas.height,
        radius: Math.random() * 2.2 + 0.8,
        vx: (Math.random() - 0.5) * 0.3,
        vy: -Math.random() * 0.45 - 0.15,
        alpha: Math.random() * 0.5 + 0.25,
        phase: Math.random() * Math.PI * 2
      });
    }
  }
  initParticles();

  function renderParticles() {
    if (!ctx || currentEffectsMode !== 'full' || document.hidden || reducedMotionQuery.matches) {
      animFrameId = null;
      return;
    }
    ctx.clearRect(0, 0, canvas.width, canvas.height);
    for (let p of particles) {
      p.x += p.vx;
      p.y += p.vy;
      p.phase += 0.02;

      if (p.y < 0) p.y = canvas.height;
      if (p.x < 0) p.x = canvas.width;
      if (p.x > canvas.width) p.x = 0;

      const currentAlpha = p.alpha * (0.6 + 0.4 * Math.sin(p.phase));
      ctx.beginPath();
      ctx.arc(p.x, p.y, p.radius, 0, Math.PI * 2);
      ctx.fillStyle = `rgba(242, 166, 60, ${currentAlpha})`;
      ctx.shadowBlur = 8;
      ctx.shadowColor = 'rgba(242, 166, 60, 0.4)';
      ctx.fill();
    }
    animFrameId = requestAnimationFrame(renderParticles);
  }

  function startAmbientParticles() {
    if (!animFrameId && ctx) {
      animFrameId = requestAnimationFrame(renderParticles);
    }
  }

  function stopAmbientParticles() {
    if (animFrameId) {
      cancelAnimationFrame(animFrameId);
      animFrameId = null;
    }
    if (ctx && canvas) {
      ctx.clearRect(0, 0, canvas.width, canvas.height);
    }
  }

  // Mouse Parallax
  window.addEventListener('mousemove', (e) => {
    if (currentEffectsMode !== 'full' || reducedMotionQuery.matches) return;
    const xRatio = (e.clientX / window.innerWidth) - 0.5;
    const yRatio = (e.clientY / window.innerHeight) - 0.5;

    if (worldBg) {
      worldBg.style.transform = `translate(${xRatio * -22}px, ${yRatio * -14}px) scale(1.03)`;
    }
    if (sunbeam) {
      sunbeam.style.transform = `translate(${e.clientX}px, ${e.clientY}px)`;
    }
  });

  document.addEventListener('visibilitychange', () => {
    if (document.hidden) {
      stopAmbientParticles();
    } else if (currentEffectsMode === 'full' && !reducedMotionQuery.matches) {
      startAmbientParticles();
    }
  });

  reducedMotionQuery.addEventListener('change', () => {
    if (reducedMotionQuery.matches) {
      window.setEffectsMode('off');
    } else {
      window.setEffectsMode(resolveEffectsMode());
    }
  });

  // 3. Hash Router
  const validScreens = ['play', 'lobby', 'friends', 'wardrobe', 'workshop', 'settings'];

  function navigateTo(targetRoute) {
    const route = validScreens.includes(targetRoute) ? targetRoute : 'play';
    
    // Update active nav-tab
    document.querySelectorAll('.nav-tab').forEach(tab => {
      const isCurrent = tab.dataset.route === route;
      tab.classList.toggle('active', isCurrent);
      if (isCurrent) {
        tab.setAttribute('aria-current', 'page');
      } else {
        tab.removeAttribute('aria-current');
      }
    });

    // Update active screen-view with transition
    document.querySelectorAll('.screen-view').forEach(screen => {
      const matches = screen.id === `screen-${route}`;
      if (matches) {
        screen.style.display = 'flex';
        // Trigger reflow for spring animation
        void screen.offsetHeight;
        screen.classList.add('is-active');
      } else {
        screen.classList.remove('is-active');
        setTimeout(() => {
          if (!screen.classList.contains('is-active')) {
            screen.style.display = 'none';
          }
        }, 300);
      }
    });

    window.location.hash = `#/${route}`;
  }

  function handleHashChange() {
    const rawHash = window.location.hash.replace(/^#\/?/, '').split('?')[0];
    navigateTo(rawHash || 'play');
  }

  window.addEventListener('hashchange', handleHashChange);

  // 4. Toast Notifications
  window.showToast = function (message, type = 'info') {
    const container = document.getElementById('toastContainer');
    if (!container) return;

    const toast = document.createElement('div');
    toast.className = `toast-item toast-${type}`;
    
    let iconSvg = '';
    if (type === 'success') {
      iconSvg = '<svg class="icon" style="color:var(--color-success)" viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"></polyline></svg>';
    } else if (type === 'error') {
      iconSvg = '<svg class="icon" style="color:var(--color-danger)" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>';
    } else {
      iconSvg = '<svg class="icon" style="color:var(--accent)" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="16" x2="12" y2="12"></line><line x1="12" y1="8" x2="12.01" y2="8"></line></svg>';
    }

    toast.innerHTML = `${iconSvg}<span>${message}</span>`;
    container.appendChild(toast);

    requestAnimationFrame(() => {
      toast.classList.add('is-visible');
    });

    setTimeout(() => {
      toast.classList.remove('is-visible');
      setTimeout(() => toast.remove(), 400);
    }, 3500);
  };

  // 5. Modals Management
  window.openModal = function (modalId) {
    const modal = document.getElementById(modalId);
    if (!modal) return;
    modal.classList.add('is-open');
    modal.setAttribute('aria-hidden', 'false');
  };

  window.closeModal = function (modalId) {
    const modal = document.getElementById(modalId);
    if (!modal) return;
    modal.classList.remove('is-open');
    modal.setAttribute('aria-hidden', 'true');
  };

  // Close modal on backdrop click or Escape
  document.addEventListener('click', (e) => {
    if (e.target.classList.contains('modal-backdrop')) {
      e.target.classList.remove('is-open');
      e.target.setAttribute('aria-hidden', 'true');
    }
  });

  document.addEventListener('keydown', (e) => {
    if (e.key === 'Escape') {
      document.querySelectorAll('.modal-backdrop.is-open').forEach(m => {
        m.classList.remove('is-open');
        m.setAttribute('aria-hidden', 'true');
      });
      document.querySelectorAll('.custom-dropdown.is-open').forEach(d => {
        d.classList.remove('is-open');
      });
    }
  });

  // 6. Global Dropdown Management
  document.addEventListener('click', (e) => {
    const trigger = e.target.closest('.custom-dropdown-trigger');
    const allDropdowns = document.querySelectorAll('.custom-dropdown');

    if (trigger) {
      e.stopPropagation();
      const parent = trigger.closest('.custom-dropdown');
      const willOpen = !parent.classList.contains('is-open');
      allDropdowns.forEach(d => d.classList.remove('is-open'));
      if (willOpen) {
        parent.classList.add('is-open');
        trigger.setAttribute('aria-expanded', 'true');
      } else {
        trigger.setAttribute('aria-expanded', 'false');
      }
      return;
    }

    // Click outside
    if (!e.target.closest('.custom-dropdown')) {
      allDropdowns.forEach(d => {
        d.classList.remove('is-open');
        const trig = d.querySelector('.custom-dropdown-trigger');
        if (trig) trig.setAttribute('aria-expanded', 'false');
      });
    }
  });

  // 7. Audio Widget Controller
  window.initAudioWidget = function () {
    const audioEl = document.getElementById('bgAudioElement');
    const playBtn = document.getElementById('audioPlayBtn');
    const bars = document.querySelectorAll('.audio-bar');
    let isPlaying = false;

    if (!audioEl || !playBtn) return;

    playBtn.addEventListener('click', () => {
      isPlaying = !isPlaying;
      if (isPlaying) {
        audioEl.play().catch(() => {});
        playBtn.classList.add('is-playing');
        bars.forEach(b => b.classList.add('animating'));
      } else {
        audioEl.pause();
        playBtn.classList.remove('is-playing');
        bars.forEach(b => b.classList.remove('animating'));
      }
    });
  };

  // 8. Adaptive Glass Tone Detection Engine
  const offscreenCanvas = document.createElement('canvas');
  offscreenCanvas.width = 64;
  offscreenCanvas.height = 36;
  const offscreenCtx = offscreenCanvas.getContext('2d', { willReadFrequently: true });
  let currentBgImage = null;
  let bgImgLoaded = false;
  let bgImgElement = null;
  let detectDebounceTimer = null;

  function getFallbackTone() {
    const rootStyle = getComputedStyle(document.documentElement);
    const toneVal = rootStyle.getPropertyValue('--bg-world-tone').trim();
    return (toneVal === 'light') ? 'light' : 'dark';
  }

  function extractBgImageUrl() {
    const bgElem = document.getElementById('worldBg');
    const rootStyle = getComputedStyle(document.documentElement);
    let bgUrl = bgElem ? getComputedStyle(bgElem).backgroundImage : '';
    if (!bgUrl || bgUrl === 'none') {
      bgUrl = rootStyle.getPropertyValue('--bg-world-image').trim();
    }
    const match = bgUrl.match(/url\(['"]?(.*?)['"]?\)/);
    return match ? match[1] : null;
  }

  function loadBgImage(url, callback) {
    if (!url) {
      bgImgLoaded = false;
      callback(false);
      return;
    }
    if (bgImgElement && currentBgImage === url && bgImgLoaded) {
      callback(true);
      return;
    }
    currentBgImage = url;
    bgImgLoaded = false;
    const img = new Image();
    img.crossOrigin = 'anonymous';
    img.onload = () => {
      bgImgElement = img;
      bgImgLoaded = true;
      if (offscreenCtx) {
        offscreenCtx.clearRect(0, 0, 64, 36);
        offscreenCtx.drawImage(img, 0, 0, 64, 36);
      }
      callback(true);
    };
    img.onerror = () => {
      bgImgLoaded = false;
      callback(false);
    };
    img.src = url;
  }

  window.setWorldBackground = function (url) {
    const bgElem = document.getElementById('worldBg');
    if (bgElem) {
      if (url) {
        bgElem.style.backgroundImage = `url("${url}")`;
      } else {
        bgElem.style.backgroundImage = 'none';
      }
    }
    if (url) {
      document.documentElement.style.setProperty('--bg-world-image', `url("${url}")`);
    }
    window.detectGlassTone();
  };

  window.detectGlassTone = function () {
    const glassElements = document.querySelectorAll('[data-glass]');

    const bgUrl = extractBgImageUrl();
    loadBgImage(bgUrl, (success) => {
      if (!success || !offscreenCtx) {
        const fallbackTone = getFallbackTone();
        document.documentElement.setAttribute('data-bg-tone', fallbackTone);
        if (glassElements.length) {
          glassElements.forEach(el => el.setAttribute('data-tone', fallbackTone));
        }
        return;
      }

      let imgData = null;
      try {
        imgData = offscreenCtx.getImageData(0, 0, 64, 36);
      } catch (e) {
        // SecurityError on file:/// origin
        const fallbackTone = getFallbackTone();
        document.documentElement.setAttribute('data-bg-tone', fallbackTone);
        if (glassElements.length) {
          glassElements.forEach(el => el.setAttribute('data-tone', fallbackTone));
        }
        return;
      }

      const pixels = imgData.data;

      // 1. Calculate overall background tone across the entire image
      let wholeL = 0;
      let totalPixels = 64 * 36;
      for (let i = 0; i < pixels.length; i += 4) {
        const r = pixels[i] / 255;
        const g = pixels[i + 1] / 255;
        const b = pixels[i + 2] / 255;
        wholeL += (0.2126 * r + 0.7152 * g + 0.0722 * b);
      }
      const overallAvgL = wholeL / totalPixels;
      const overallTone = (overallAvgL > 0.5) ? 'light' : 'dark';
      document.documentElement.setAttribute('data-bg-tone', overallTone);

      if (!glassElements.length) return;

      const winW = window.innerWidth || 1920;
      const winH = window.innerHeight || 1080;
      const imgW = bgImgElement ? bgImgElement.naturalWidth : 1920;
      const imgH = bgImgElement ? bgImgElement.naturalHeight : 1080;

      // Calculate background-size: cover scaling and crop offsets
      const scale = Math.max(winW / imgW, winH / imgH);
      const renderedW = imgW * scale;
      const renderedH = imgH * scale;
      const cropX = (renderedW - winW) / 2;
      const cropY = (renderedH - winH) / 2;

      glassElements.forEach(el => {
        // Skip hidden elements in inactive screen views
        const screenParent = el.closest('.screen-view');
        if (screenParent && !screenParent.classList.contains('is-active')) {
          return;
        }

        const rect = el.getBoundingClientRect();
        if (rect.width <= 0 || rect.height <= 0) return;

        // Map element bounding box into 64x36 canvas coordinates
        const normLeft = (rect.left + cropX) / renderedW;
        const normTop = (rect.top + cropY) / renderedH;
        const normRight = (rect.right + cropX) / renderedW;
        const normBottom = (rect.bottom + cropY) / renderedH;

        const startX = Math.max(0, Math.min(63, Math.floor(normLeft * 64)));
        const endX = Math.max(0, Math.min(63, Math.ceil(normRight * 64)));
        const startY = Math.max(0, Math.min(35, Math.floor(normTop * 36)));
        const endY = Math.max(0, Math.min(35, Math.ceil(normBottom * 36)));

        let totalL = 0;
        let count = 0;

        for (let y = startY; y <= endY; y++) {
          for (let x = startX; x <= endX; x++) {
            const idx = (y * 64 + x) * 4;
            const r = pixels[idx] / 255;
            const g = pixels[idx + 1] / 255;
            const b = pixels[idx + 2] / 255;
            // Relative luminance (W3C formula)
            const lum = 0.2126 * r + 0.7152 * g + 0.0722 * b;
            totalL += lum;
            count++;
          }
        }

        const avgL = count > 0 ? (totalL / count) : 0;
        const determinedTone = (avgL > 0.5) ? 'light' : 'dark';
        el.setAttribute('data-tone', determinedTone);
      });
    });
  };

  function scheduleGlassToneDetection() {
    if (detectDebounceTimer) clearTimeout(detectDebounceTimer);
    detectDebounceTimer = setTimeout(() => {
      window.detectGlassTone();
    }, 150);
  }

  function initGlassToneDetection() {
    window.detectGlassTone();
    window.addEventListener('resize', scheduleGlassToneDetection);

    // Re-detect when route changes (new screen becomes active)
    window.addEventListener('hashchange', () => {
      setTimeout(() => window.detectGlassTone(), 50);
    });

    // Observe background image changes on #worldBg if mutated
    const bgElem = document.getElementById('worldBg');
    if (bgElem && window.MutationObserver) {
      const observer = new MutationObserver(() => {
        scheduleGlassToneDetection();
      });
      observer.observe(bgElem, { attributes: true, attributeFilter: ['style', 'class'] });
    }

    // Observe changes to style attribute on <html> (:root style)
    if (window.MutationObserver) {
      const htmlObserver = new MutationObserver((mutations) => {
        for (const m of mutations) {
          if (m.type === 'attributes' && m.attributeName === 'style') {
            scheduleGlassToneDetection();
            break;
          }
        }
      });
      htmlObserver.observe(document.documentElement, { attributes: true, attributeFilter: ['style'] });
    }
  }

  // 9. Bootstrap on DOM ready
  document.addEventListener('DOMContentLoaded', () => {
    window.setEffectsMode(currentEffectsMode);
    handleHashChange();
    if (window.initAudioWidget) window.initAudioWidget();

    // Bind nav-tab clicks
    document.querySelectorAll('.nav-tab').forEach(tab => {
      tab.addEventListener('click', (e) => {
        e.preventDefault();
        navigateTo(tab.dataset.route);
      });
    });

    initGlassToneDetection();
  });

})();

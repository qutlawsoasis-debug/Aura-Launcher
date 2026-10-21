/**
 * Aura Launcher v3 — Wardrobe Section Controller (skinview3d 3D Viewer)
 * Interactive 3D Minecraft skin model with Walking/Idle animation, cape toggle, and pause management.
 */

(function () {
  'use strict';

  let viewer = null;
  let idleAnimation = null;
  let walkingAnimation = null;
  let runningAnimation = null;
  let activeAnimationMode = 'idle'; // 'idle', 'walk', 'run'
  let isHovered = false;

  const DEFAULT_SKIN = 'assets/images/steve.png';
  const CAPE_MAP = {
    cyan: 'assets/images/cape_cyan.png',
    silver: 'assets/images/cape_silver.png'
  };

  let turnBackTimer = null;
  let rotationTween = 0;

  // Плавный поворот модели вокруг вертикали. В режиме off — сразу и одним кадром.
  function rotateModelTo(target, duration) {
    if (!viewer || !viewer.playerObject) return;
    cancelAnimationFrame(rotationTween);
    const body = viewer.playerObject;
    if (document.documentElement.dataset.effects === 'off') {
      body.rotation.y = target;
      viewer.render();
      return;
    }
    const from = body.rotation.y;
    const start = performance.now();
    // easeOutBack с мягким овершутом — как --spring в CSS
    const ease = t => 1 + 2.2 * Math.pow(t - 1, 3) + 1.2 * Math.pow(t - 1, 2);
    const step = now => {
      const t = Math.min((now - start) / duration, 1);
      body.rotation.y = from + (target - from) * ease(t);
      if (t < 1) rotationTween = requestAnimationFrame(step);
    };
    rotationTween = requestAnimationFrame(step);
  }

  function turnModelBack() {
    if (!viewer || !viewer.playerObject) return;
    clearTimeout(turnBackTimer);
    viewer.autoRotate = false;
    rotateModelTo(Math.PI * 0.92, 650);

    turnBackTimer = setTimeout(() => {
      const effects = document.documentElement.dataset.effects;
      rotateModelTo(0, 750);
      if (effects !== 'low' && effects !== 'off' && !isHovered) {
        setTimeout(() => { if (viewer && !isHovered) viewer.autoRotate = true; }, 750);
      }
    }, 1800);
  }

  function initSkinview() {
    const canvas = document.getElementById('skinCanvas3D');
    if (!canvas || typeof skinview3d === 'undefined') return;

    if (viewer) {
      updateRenderPauseState();
      return;
    }

    const rect = canvas.getBoundingClientRect();
    const w = rect.width > 50 ? rect.width : 260;
    const h = rect.height > 50 ? rect.height : 380;

    // Create skinview3d viewer instance
    viewer = new skinview3d.SkinViewer({
      canvas: canvas,
      width: w,
      height: h,
      skin: DEFAULT_SKIN
    });

    if (viewer.renderer) {
      const cappedRatio = Math.min(window.devicePixelRatio || 1, 1.5);
      viewer.renderer.setPixelRatio(cappedRatio);
      if (viewer.composer) {
        viewer.composer.setPixelRatio(cappedRatio);
      }
    }

    // Set preserveDrawingBuffer to true or force initial render once skin loads
    viewer.loadSkin(DEFAULT_SKIN).then(() => {
      viewer.render();
    }).catch(() => {});

    viewer.fov = 70;
    viewer.zoom = 0.9;
    viewer.autoRotate = true;
    viewer.autoRotateSpeed = 0.55;

    // Load initial cape
    viewer.loadCape(CAPE_MAP.cyan).catch(() => {});

    // Animations setup
    idleAnimation = new skinview3d.IdleAnimation();
    idleAnimation.speed = 0.8;
    walkingAnimation = new skinview3d.WalkingAnimation();
    walkingAnimation.speed = 0.85;
    runningAnimation = new skinview3d.RunningAnimation();
    runningAnimation.speed = 1.0;

    viewer.animation = idleAnimation;

    // Throttled render loop with single-frame render in 'off' mode
    const originalDraw = viewer.draw.bind(viewer);
    let lastRenderTime = 0;
    viewer.draw = function () {
      const effects = document.documentElement.dataset.effects;
      if (effects === 'off') {
        originalDraw();
        return;
      }

      const now = performance.now();
      const targetFps = (activeAnimationMode === 'idle' && !isHovered) ? 30 : 60;
      const minInterval = 1000 / targetFps - 2;

      if (now - lastRenderTime >= minInterval) {
        lastRenderTime = now;
        originalDraw();
      } else {
        if (!viewer._renderPaused && !viewer._disposed) {
          viewer.animationID = window.requestAnimationFrame(() => viewer.draw());
        }
      }
    };

    // Orbit controls limits
    if (viewer.controls) {
      viewer.controls.enableZoom = true;
      viewer.controls.minDistance = 15;
      viewer.controls.maxDistance = 60;
    }

    // Resize observer on wardrobe container to update viewer size dynamically
    if (window.ResizeObserver) {
      const ro = new ResizeObserver(() => {
        if (viewer && canvas) {
          const r = canvas.getBoundingClientRect();
          if (r.width > 50 && r.height > 50) {
            viewer.setSize(r.width, r.height);
          }
        }
      });
      ro.observe(canvas);
    }

    // Hover walking animation transition
    canvas.addEventListener('mouseenter', () => {
      isHovered = true;
      viewer.autoRotate = false;
      if (activeAnimationMode === 'idle' && walkingAnimation) {
        viewer.animation = walkingAnimation;
      }
    });

    canvas.addEventListener('mouseleave', () => {
      isHovered = false;
      const effects = document.documentElement.dataset.effects;
      if (effects !== 'low' && effects !== 'off') {
        viewer.autoRotate = true;
      }
      applyAnimationMode(activeAnimationMode);
    });

    // Animation Segment buttons
    document.querySelectorAll('.skin-anim-seg button').forEach(btn => {
      btn.addEventListener('click', () => {
        const mode = btn.dataset.anim;
        activeAnimationMode = mode;
        applyAnimationMode(mode);
      });
    });

    // Model type
    document.querySelectorAll('#modelTypeSeg button').forEach(btn => {
      btn.addEventListener('click', () => {
        const model = btn.dataset.model || 'default';
        if (viewer && viewer.playerObject) {
          viewer.playerObject.skin.modelType = model;
          if (document.documentElement.dataset.effects === 'off') {
            viewer.render();
          }
        }
      });
    });

    // Cape selector with turnModelBack on hover and click
    document.querySelectorAll('#capeSelector .cape').forEach(btn => {
      btn.addEventListener('mouseenter', () => {
        turnModelBack();
      });

      btn.addEventListener('click', () => {
        document.querySelectorAll('#capeSelector .cape').forEach(b => b.classList.remove('on'));
        btn.classList.add('on');
        const capeType = btn.dataset.cape;
        if (capeType === 'none') {
          viewer.resetCape();
        } else if (CAPE_MAP[capeType]) {
          viewer.loadCape(CAPE_MAP[capeType]).catch(() => {});
        }
        turnModelBack();
        if (document.documentElement.dataset.effects === 'off') {
          setTimeout(() => viewer && viewer.render(), 100);
        }
      });
    });

    // Custom skin file upload with face preview update
    const skinFileInput = document.getElementById('skinFileInput');
    const skinNameLabel = document.getElementById('currentSkinFileName');
    const skinFacePreview = document.getElementById('skinFacePreview');

    if (skinFileInput) {
      skinFileInput.addEventListener('change', (e) => {
        const file = e.target.files && e.target.files[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = (ev) => {
          const dataUrl = ev.target.result;
          canvas.style.transition = 'opacity var(--dur-1) var(--ease-out)';
          canvas.style.opacity = '0.3';

          viewer.loadSkin(dataUrl).then(() => {
            canvas.style.opacity = '1';
            if (skinNameLabel) skinNameLabel.textContent = file.name;
            if (skinFacePreview) {
              skinFacePreview.style.backgroundImage = `url('${dataUrl}'), url('${dataUrl}')`;
            }
            if (document.documentElement.dataset.effects === 'off') {
              viewer.render();
            }
            if (typeof window.showToast === 'function') {
              window.showToast('Скин успешно загружен', 'success');
            }
          }).catch(() => {
            canvas.style.opacity = '1';
            if (typeof window.showToast === 'function') {
              window.showToast('Не удалось загрузить скин', 'error');
            }
          });
        };
        reader.readAsDataURL(file);
      });
    }

    // Nickname input toast feedback
    const nickInput = document.getElementById('inputPlayerNickname');
    if (nickInput) {
      nickInput.addEventListener('change', () => {
        if (typeof window.showToast === 'function') {
          window.showToast(`Ник изменён на ${nickInput.value}`, 'info');
        }
      });
    }

    updateRenderPauseState();
  }

  function applyAnimationMode(mode) {
    if (!viewer) return;
    const effects = document.documentElement.dataset.effects;
    if (effects === 'off') {
      viewer.animation = null;
      viewer.render();
      return;
    }
    if (mode === 'idle') viewer.animation = idleAnimation;
    else if (mode === 'walk') viewer.animation = walkingAnimation;
    else if (mode === 'run') viewer.animation = runningAnimation;
  }

  function updateRenderPauseState() {
    const isWardrobeOpen = document.body.classList.contains('is-open') && 
      document.querySelector('[data-l="wardrobe"]')?.classList.contains('on');
    const effects = document.documentElement.dataset.effects;
    const isHidden = document.hidden;

    if (isWardrobeOpen && !viewer) {
      initSkinview();
      return;
    }

    if (!viewer) return;

    if (!isWardrobeOpen || isHidden) {
      viewer.renderPaused = true;
    } else if (effects === 'off') {
      // In 'off' mode: static single frame render, no animations, no autoRotate
      viewer.renderPaused = true;
      viewer.autoRotate = false;
      viewer.animation = null;
      viewer.render();
    } else if (effects === 'low') {
      // In 'low' mode: animation active, autoRotate off
      viewer.renderPaused = false;
      viewer.autoRotate = false;
      applyAnimationMode(activeAnimationMode);
    } else {
      // In 'full' mode: full animations + autoRotate
      viewer.renderPaused = false;
      viewer.autoRotate = !isHovered;
      applyAnimationMode(activeAnimationMode);
    }
  }

  window.updateWardrobeRenderState = updateRenderPauseState;
  window.AuraWardrobe = {
    get viewer() { return viewer; },
    get isHovered() { return isHovered; },
    updateRenderPauseState: updateRenderPauseState
  };

  // Listen to layer changes, route changes, visibility and effects
  window.addEventListener('hashchange', updateRenderPauseState);
  document.addEventListener('visibilitychange', updateRenderPauseState);

  const islandEl = document.getElementById('island');
  if (islandEl && window.MutationObserver) {
    new MutationObserver(updateRenderPauseState).observe(islandEl, { attributes: true, attributeFilter: ['class', 'style'] });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSkinview);
  } else {
    initSkinview();
  }
})();

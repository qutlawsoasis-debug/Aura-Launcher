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

  function initSkinview() {
    const canvas = document.getElementById('skinCanvas3D');
    if (!canvas || typeof skinview3d === 'undefined') return;

    // Create skinview3d viewer instance with capped pixelRatio
    viewer = new skinview3d.SkinViewer({
      canvas: canvas,
      width: 260,
      height: 380,
      skin: DEFAULT_SKIN
    });

    if (viewer.renderer) {
      const cappedRatio = Math.min(window.devicePixelRatio || 1, 1.5);
      viewer.renderer.setPixelRatio(cappedRatio);
      if (viewer.composer) {
        viewer.composer.setPixelRatio(cappedRatio);
      }
    }

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

    // Throttled render loop: ~30 FPS for 'idle' (Стоит), 60 FPS for 'walk'/'run' (Идёт/Бежит)
    const originalDraw = viewer.draw.bind(viewer);
    let lastRenderTime = 0;
    viewer.draw = function () {
      const now = performance.now();
      const targetFps = (activeAnimationMode === 'idle' && !isHovered) ? 30 : 60;
      const minInterval = 1000 / targetFps - 2; // small epsilon for smooth vsync

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

    // Animation Segment buttons (Стоит / Идёт / Бежит)
    document.querySelectorAll('.skin-anim-seg button').forEach(btn => {
      btn.addEventListener('click', () => {
        const mode = btn.dataset.anim;
        activeAnimationMode = mode;
        applyAnimationMode(mode);
      });
    });

    // Model type (Steve / Alex slim)
    document.querySelectorAll('#modelTypeSeg button').forEach(btn => {
      btn.addEventListener('click', () => {
        const model = btn.dataset.model || 'default';
        if (viewer && viewer.playerObject) {
          viewer.playerObject.skin.modelType = model;
        }
      });
    });

    // Cape selector
    document.querySelectorAll('#capeSelector .cape').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('#capeSelector .cape').forEach(b => b.classList.remove('on'));
        btn.classList.add('on');
        const capeType = btn.dataset.cape;
        if (capeType === 'none') {
          viewer.resetCape();
        } else if (CAPE_MAP[capeType]) {
          viewer.loadCape(CAPE_MAP[capeType]).catch(() => {});
        }
      });
    });

    // Custom skin file upload
    const skinFileInput = document.getElementById('skinFileInput');
    const skinNameLabel = document.getElementById('currentSkinFileName');
    if (skinFileInput) {
      skinFileInput.addEventListener('change', (e) => {
        const file = e.target.files && e.target.files[0];
        if (!file) return;

        const reader = new FileReader();
        reader.onload = (ev) => {
          const dataUrl = ev.target.result;
          viewer.loadSkin(dataUrl).then(() => {
            if (skinNameLabel) skinNameLabel.textContent = file.name;
            if (typeof window.showToast === 'function') {
              window.showToast('Скин успешно загружен', 'success');
            }
          }).catch(() => {
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
    if (mode === 'idle') viewer.animation = idleAnimation;
    else if (mode === 'walk') viewer.animation = walkingAnimation;
    else if (mode === 'run') viewer.animation = runningAnimation;
  }

  function updateRenderPauseState() {
    if (!viewer) return;
    const isWardrobeOpen = document.body.classList.contains('is-open') && 
      document.querySelector('[data-l="wardrobe"]')?.classList.contains('on');
    const effects = document.documentElement.dataset.effects;
    const isHidden = document.hidden;

    if (!isWardrobeOpen || isHidden || effects === 'off') {
      viewer.renderPaused = true;
    } else {
      viewer.renderPaused = false;
      viewer.autoRotate = (effects !== 'low' && !isHovered);
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

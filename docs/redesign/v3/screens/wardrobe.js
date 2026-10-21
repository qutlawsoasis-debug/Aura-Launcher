/**
 * Aura Launcher v3 — Wardrobe Section Controller (skinview3d 3D Viewer)
 * 3D-модель скина: плавное смешивание анимаций, вращение с инерцией, плащи без подгрузки на лету,
 * рендер только пока раздел открыт.
 */

(function () {
  'use strict';

  let viewer = null;
  let activeAnimationMode = 'idle'; // 'idle', 'walk', 'run'
  let isHovered = false;

  const DEFAULT_SKIN = 'assets/images/steve.png';
  const CAPE_MAP = {
    cyan: 'assets/images/cape_cyan.png',
    silver: 'assets/images/cape_silver.png'
  };
  const capeImages = {};

  const effects = () => document.documentElement.dataset.effects;

  // Плащи грузим один раз: loadCape(Image) применяется сразу, без декодирования на клике
  function preloadCapes() {
    return Promise.all(Object.entries(CAPE_MAP).map(([key, src]) => new Promise(resolve => {
      const img = new Image();
      img.onload = () => { capeImages[key] = img; resolve(); };
      img.onerror = resolve;
      img.src = src;
    })));
  }

  /* ---------- Смешивание анимаций ----------
     Каждый кадр поза собирается из «покоя» + анимации. При смене анимации старая и новая
     считаются обе, и поза плавно переходит от одной к другой за BLEND_SEC. */
  const BLEND_SEC = 0.35;
  const animations = {};
  const blend = { from: null, to: null, t: 1 };
  let restPose = null;

  function poseParts(player) {
    const s = player.skin;
    return [player, s.head, s.body, s.leftArm, s.rightArm, s.leftLeg, s.rightLeg, player.cape, player.elytra].filter(Boolean);
  }

  function capturePose(player) {
    return poseParts(player).map(p => [p.position.x, p.position.y, p.position.z, p.rotation.x, p.rotation.y, p.rotation.z]);
  }

  function applyPose(player, pose) {
    poseParts(player).forEach((p, i) => {
      const v = pose[i];
      p.position.set(v[0], v[1], v[2]);
      p.rotation.set(v[3], v[4], v[5]);
    });
  }

  function lerpPose(a, b, w) {
    return a.map((va, i) => va.map((x, j) => x + (b[i][j] - x) * w));
  }

  function setAnimation(mode) {
    const next = animations[mode] || animations.idle;
    if (blend.to === next) return;
    if (!blend.to || effects() !== 'full' && effects() !== 'low') {
      blend.from = null; blend.to = next; blend.t = 1;
      return;
    }
    blend.from = blend.to;
    blend.to = next;
    blend.t = 0;
  }

  function makeBlendedAnimation() {
    return new skinview3d.FunctionAnimation((player, progress, delta) => {
      if (!restPose || !blend.to) return;
      if (blend.from && blend.t < 1) {
        applyPose(player, restPose);
        blend.from.update(player, delta);
        const a = capturePose(player);
        applyPose(player, restPose);
        blend.to.update(player, delta);
        const b = capturePose(player);
        blend.t = Math.min(1, blend.t + delta / BLEND_SEC);
        const w = 1 - Math.pow(1 - blend.t, 3); // ease-out
        applyPose(player, lerpPose(a, b, w));
        if (blend.t >= 1) blend.from = null;
      } else {
        applyPose(player, restPose);
        blend.to.update(player, delta);
      }
    });
  }

  /* ---------- Поворот к плащу ----------
     Крутим обёртку модели (её же крутит автоповорот) по кратчайшему пути. */
  let turnBackTimer = null;
  let rotationTween = 0;

  function rotateWrapperTo(target, duration) {
    if (!viewer) return;
    cancelAnimationFrame(rotationTween);
    const wrap = viewer.playerWrapper;
    const TAU = Math.PI * 2;
    const from = wrap.rotation.y;
    let to = target + Math.round((from - target) / TAU) * TAU;
    if (effects() === 'off') {
      wrap.rotation.y = to;
      viewer.render();
      return;
    }
    const start = performance.now();
    const ease = t => 1 + 2.2 * Math.pow(t - 1, 3) + 1.2 * Math.pow(t - 1, 2); // мягкий овершут, как --spring
    const step = now => {
      const t = Math.min((now - start) / duration, 1);
      wrap.rotation.y = from + (to - from) * ease(t);
      if (t < 1) rotationTween = requestAnimationFrame(step);
    };
    rotationTween = requestAnimationFrame(step);
  }

  function showCapeSide() {
    if (!viewer) return;
    clearTimeout(turnBackTimer);
    viewer.autoRotate = false;
    rotateWrapperTo(Math.PI, 700);
    turnBackTimer = setTimeout(() => {
      rotateWrapperTo(0, 800);
      setTimeout(() => { turnBackTimer = null; updateRenderPauseState(); }, 820);
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
    viewer = new skinview3d.SkinViewer({
      canvas: canvas,
      width: rect.width > 50 ? rect.width : 260,
      height: rect.height > 50 ? rect.height : 380,
      skin: DEFAULT_SKIN
    });
    viewer.renderer.setPixelRatio(Math.min(window.devicePixelRatio || 1, 1.5));

    viewer.fov = 70;
    viewer.zoom = 0.9;
    viewer.autoRotateSpeed = 0.55;

    // Вращение мышью с инерцией вместо жёсткой привязки к курсору
    if (viewer.controls) {
      viewer.controls.enableDamping = true;
      viewer.controls.dampingFactor = 0.08;
      viewer.controls.rotateSpeed = 0.7;
      viewer.controls.enableZoom = true;
      viewer.controls.enablePan = false;
      viewer.controls.minDistance = 15;
      viewer.controls.maxDistance = 60;
    }

    animations.idle = new skinview3d.IdleAnimation();
    animations.idle.speed = 0.8;
    animations.walk = new skinview3d.WalkingAnimation();
    animations.walk.speed = 0.85;
    animations.run = new skinview3d.RunningAnimation();
    animations.run.speed = 1.0;

    restPose = capturePose(viewer.playerObject);
    blend.to = animations.idle;
    viewer.animation = makeBlendedAnimation();

    viewer.loadSkin(DEFAULT_SKIN).then(() => viewer.render()).catch(() => {});
    preloadCapes().then(() => {
      if (capeImages.cyan) viewer.loadCape(capeImages.cyan);
      viewer.render();
    });

    if (window.ResizeObserver) {
      new ResizeObserver(() => {
        const r = canvas.getBoundingClientRect();
        if (r.width > 50 && r.height > 50) viewer.setSize(r.width, r.height);
      }).observe(canvas);
    }

    canvas.addEventListener('mouseenter', () => {
      isHovered = true;
      viewer.autoRotate = false;
      if (activeAnimationMode === 'idle') setAnimation('walk');
    });

    canvas.addEventListener('mouseleave', () => {
      isHovered = false;
      setAnimation(activeAnimationMode);
      updateRenderPauseState();
    });

    document.querySelectorAll('.skin-anim-seg button').forEach(btn => {
      btn.addEventListener('click', () => {
        activeAnimationMode = btn.dataset.anim;
        setAnimation(activeAnimationMode);
        if (effects() === 'off') viewer.render();
      });
    });

    document.querySelectorAll('#modelTypeSeg button').forEach(btn => {
      btn.addEventListener('click', () => {
        viewer.playerObject.skin.modelType = btn.dataset.model || 'default';
        if (effects() === 'off') viewer.render();
      });
    });

    document.querySelectorAll('#capeSelector .cape').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('#capeSelector .cape').forEach(b => b.classList.remove('on'));
        btn.classList.add('on');
        const capeType = btn.dataset.cape;
        if (capeType === 'none') viewer.resetCape();
        else if (capeImages[capeType]) viewer.loadCape(capeImages[capeType]);
        showCapeSide();
      });
    });

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
          canvas.style.transition = 'opacity var(--dur-2) var(--ease-out)';
          canvas.style.opacity = '0.3';

          viewer.loadSkin(dataUrl).then(() => {
            canvas.style.opacity = '1';
            if (skinNameLabel) skinNameLabel.textContent = file.name;
            if (skinFacePreview) skinFacePreview.style.backgroundImage = `url('${dataUrl}'), url('${dataUrl}')`;
            if (effects() === 'off') viewer.render();
            if (typeof window.showToast === 'function') window.showToast('Скин загружен', 'success');
          }).catch(() => {
            canvas.style.opacity = '1';
            if (typeof window.showToast === 'function') window.showToast('Не удалось загрузить скин', 'error');
          });
        };
        reader.readAsDataURL(file);
      });
    }

    const nickInput = document.getElementById('inputPlayerNickname');
    if (nickInput) {
      nickInput.addEventListener('change', () => {
        if (typeof window.showToast === 'function') window.showToast(`Ник изменён на ${nickInput.value}`, 'info');
      });
    }

    updateRenderPauseState();
  }

  // Рендер идёт каждый кадр, пока раздел открыт и окно видно; иначе полностью стоит.
  function updateRenderPauseState() {
    const isWardrobeOpen = document.body.classList.contains('is-open') &&
      document.querySelector('[data-l="wardrobe"]')?.classList.contains('on');

    if (isWardrobeOpen && !viewer) {
      initSkinview();
      return;
    }
    if (!viewer) return;

    const mode = effects();
    if (!isWardrobeOpen || document.hidden) {
      viewer.renderPaused = true;
    } else if (mode === 'off') {
      viewer.renderPaused = true;
      viewer.autoRotate = false;
      viewer.render();
    } else {
      viewer.renderPaused = false;
      viewer.autoRotate = mode === 'full' && !isHovered && turnBackTimer === null;
    }
  }

  window.updateWardrobeRenderState = updateRenderPauseState;
  window.AuraWardrobe = {
    get viewer() { return viewer; },
    get isHovered() { return isHovered; },
    updateRenderPauseState
  };

  window.addEventListener('hashchange', updateRenderPauseState);
  document.addEventListener('visibilitychange', updateRenderPauseState);

  const islandEl = document.getElementById('island');
  if (islandEl && window.MutationObserver) {
    new MutationObserver(updateRenderPauseState).observe(islandEl, { attributes: true, attributeFilter: ['class'] });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSkinview);
  } else {
    initSkinview();
  }
})();

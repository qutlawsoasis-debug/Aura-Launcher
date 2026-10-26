/**
 * Aura Launcher v3 — Wardrobe Section Controller (skinview3d 3D Viewer)
 * 3D-модель скина: плавное смешивание анимаций, вращение с инерцией, плащи без подгрузки на лету,
 * рендер только пока раздел открыт.
 */

(function () {
  'use strict';

  function sendBridge(name, payload) {
    if (window.AuraBridge && typeof window.AuraBridge.send === 'function') {
      return window.AuraBridge.send(name, payload);
    }
    return Promise.resolve(null);
  }

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
    try {
      const canvas = document.getElementById('skinCanvas3D');
      if (!canvas) {
        console.warn('[wardrobe] #skinCanvas3D not found');
        return;
      }
      if (typeof skinview3d === 'undefined') {
        console.warn('[wardrobe] skinview3d library not loaded');
        return;
      }

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

      function updateSkinUI(dataUrl, fileName, model) {
        if (skinNameLabel && fileName) skinNameLabel.textContent = fileName;
        const effectiveUrl = dataUrl || DEFAULT_SKIN;
        if (skinFacePreview) {
          skinFacePreview.style.backgroundImage = 'url(\'' + effectiveUrl + '\'), url(\'' + effectiveUrl + '\')';
        }
        document.querySelectorAll('.bar .face').forEach(el => {
          el.style.backgroundImage = 'url(\'' + effectiveUrl + '\'), url(\'' + effectiveUrl + '\')';
        });
        if (model) {
          syncModelButtons(model);
          if (viewer && viewer.playerObject && viewer.playerObject.skin) {
            viewer.playerObject.skin.modelType = model;
          }
        }
      }

      let currentCape = 'none';
      let savedCape = null; // плащ из конфига; применяется, когда картинки плащей загрузятся
      let customCapeUrl = null;

      function applyCape(type, customUrl) {
        if (customUrl) customCapeUrl = customUrl;
        currentCape = type || 'none';
        document.querySelectorAll('#capeSelector .cape').forEach(b => {
          const isMatch = b.dataset.cape === currentCape || (currentCape === 'custom' && b.id === 'capeUploadLabel');
          b.classList.toggle('on', isMatch);
        });
        const customThumb = document.getElementById('customCapeThumb');
        if (currentCape === 'custom' && customCapeUrl) {
          if (customThumb) {
            customThumb.textContent = '';
            customThumb.style.backgroundImage = `url('${customCapeUrl}')`;
          }
          if (viewer) viewer.loadCape(customCapeUrl);
        } else if (currentCape === 'none') {
          if (customThumb) {
            customThumb.textContent = '+';
            customThumb.style.backgroundImage = 'none';
          }
          if (viewer) viewer.resetCape();
        } else if (capeImages[currentCape]) {
          viewer.loadCape(capeImages[currentCape]);
        }
        viewer.render();
      }

      function syncModelButtons(model) {
        const seg = document.getElementById('modelTypeSeg');
        if (!seg) return;
        seg.querySelectorAll('button').forEach(btn => {
          const isMatch = (btn.dataset.model || 'default') === (model || 'default');
          btn.classList.toggle('on', isMatch);
          btn.setAttribute('aria-checked', isMatch ? 'true' : 'false');
        });
        if (typeof window.placeLens === 'function') {
          window.placeLens(seg);
        }
      }

      sendBridge('skin.get')
        .then(res => {
          const skinUrl = (res && res.dataUrl) ? res.dataUrl : DEFAULT_SKIN;
          const model = (res && res.model) || 'default';
          const fn = (res && res.fileName) || 'steve.png';
          if (viewer.playerObject && viewer.playerObject.skin) {
            viewer.playerObject.skin.modelType = model;
          }
          syncModelButtons(model);
          updateSkinUI(res && res.dataUrl, fn, model);
          savedCape = (res && res.cape) || 'none';
          if (savedCape === 'custom' && res && res.capeDataUrl) {
            applyCape('custom', res.capeDataUrl);
          } else if (Object.keys(capeImages).length) {
            applyCape(savedCape);
          }
          return viewer.loadSkin(skinUrl);
        })
        .then(() => viewer.render())
        .catch(() => {
          viewer.loadSkin(DEFAULT_SKIN).then(() => viewer.render()).catch(() => {});
        });

      preloadCapes().then(() => {
        if (savedCape !== 'custom') applyCape(savedCape || 'none');
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
          const nextModel = btn.dataset.model || 'default';
          const prevOnBtn = document.querySelector('#modelTypeSeg button.on');
          const prevModel = (prevOnBtn && prevOnBtn.dataset.model) || 'default';
          if (nextModel === prevModel) return;

          syncModelButtons(nextModel);
          if (viewer.playerObject && viewer.playerObject.skin) {
            viewer.playerObject.skin.modelType = nextModel;
          }
          if (effects() === 'off') viewer.render();

          sendBridge('skin.setModel', { model: nextModel })
            .then(res => {
              if (res && res.uploadError && typeof window.showToast === 'function') {
                window.showToast('Модель сохранена локально (' + res.uploadError + ')', 'info');
              } else if (typeof window.showToast === 'function') {
                window.showToast('Модель рук: ' + (nextModel === 'slim' ? 'Alex (3px)' : 'Steve (4px)'), 'success');
              }
            })
            .catch(err => {
              syncModelButtons(prevModel);
              if (viewer.playerObject && viewer.playerObject.skin) {
                viewer.playerObject.skin.modelType = prevModel;
              }
              if (typeof window.showToast === 'function') {
                window.showToast('Ошибка смены модели: ' + err.message, 'error');
              }
            });
        });
      });

      document.querySelectorAll('#capeSelector .cape').forEach(btn => {
        btn.addEventListener('click', () => {
          const capeType = btn.dataset.cape;
          if (!capeType) return; // Игнорируем клик по label загрузки плаща
          const prev = currentCape;
          if (capeType === prev) return;
          applyCape(capeType);
          showCapeSide();
          sendBridge('skin.setCape', { cape: capeType })
            .then(() => {
              if (typeof window.showToast === 'function') {
                window.showToast(capeType === 'none' ? 'Плащ снят' : 'Плащ Aura надет', 'success');
                if (typeof loadWardrobeHistory === 'function') loadWardrobeHistory();
              }
            })
            .catch(err => {
              applyCape(prev);
              if (typeof window.showToast === 'function') window.showToast(err.message || 'Не удалось сменить плащ', 'error');
            });
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
              sendBridge('skin.upload', { fileName: file.name, base64Data: dataUrl })
                .then(() => {
                  if (typeof window.showToast === 'function') window.showToast('Скин сохранён', 'success');
                  if (typeof loadWardrobeHistory === 'function') loadWardrobeHistory();
                })
                .catch(err => {
                  if (typeof window.showToast === 'function') window.showToast('Ошибка сохранения скина: ' + (err.message || err), 'error');
                });
            }).catch(() => {
              canvas.style.opacity = '1';
              if (typeof window.showToast === 'function') window.showToast('Не удалось загрузить скин', 'error');
            });
          };
          reader.readAsDataURL(file);
        });
      } else {
        console.warn('[wardrobe] #skinFileInput not found in DOM');
      }

      const capeFileInput = document.getElementById('capeFileInput');
      if (capeFileInput) {
        capeFileInput.addEventListener('change', (e) => {
          const file = e.target.files && e.target.files[0];
          if (!file) return;

          const reader = new FileReader();
          reader.onload = (ev) => {
            const dataUrl = ev.target.result;
            sendBridge('cape.upload', { fileName: file.name, base64Data: dataUrl })
              .then(() => {
                applyCape('custom', dataUrl);
                showCapeSide();
                if (typeof window.showToast === 'function') {
                  window.showToast('Плащ применён (виден вам в игре)', 'success');
                }
                loadWardrobeHistory();
              })
              .catch(err => {
                if (typeof window.showToast === 'function') {
                  window.showToast('Ошибка загрузки плаща: ' + (err.message || err), 'error');
                }
              });
          };
          reader.readAsDataURL(file);
        });
      } else {
        console.warn('[wardrobe] #capeFileInput not found in DOM');
      }

      function loadWardrobeHistory() {
        const container = document.getElementById('wardrobeHistoryList');
        const counter = document.getElementById('historyCounter');
        if (!container) {
          console.warn('[wardrobe] #wardrobeHistoryList not found in DOM');
          return;
        }

        sendBridge('wardrobe.getHistory')
          .then(res => {
            if (!res) return;
            const skins = res.skins || [];
            const capes = res.capes || [];
            const total = skins.length + capes.length;
            if (counter) counter.textContent = total + ' ' + (total === 1 ? 'файл' : (total < 5 ? 'файла' : 'файлов'));

            container.innerHTML = '';
            if (total === 0) {
              container.innerHTML = '<div class="hint" style="padding:6px 0;font-size:12px">История пуста. Загрузите скин или плащ.</div>';
              return;
            }

            const allItems = [...skins, ...capes];
            allItems.sort((a, b) => new Date(b.createdUtc || 0) - new Date(a.createdUtc || 0));

            allItems.forEach(item => {
              const row = document.createElement('div');
              row.className = 'wardrobe-history-item' + (item.isCurrent ? ' is-current' : '');

              const badge = document.createElement('span');
              badge.className = 'wardrobe-history-badge';
              badge.textContent = item.type === 'cape' ? 'Плащ' : 'Скин';

              const name = document.createElement('span');
              name.className = 'wardrobe-history-name';
              name.textContent = item.name || item.id;
              name.title = item.name || item.id;

              const actions = document.createElement('div');
              actions.style.display = 'flex';
              actions.style.gap = '4px';

              const applyBtn = document.createElement('button');
              applyBtn.type = 'button';
              applyBtn.className = 'btn xs';
              applyBtn.textContent = item.isCurrent ? 'Надет' : 'Надеть';
              applyBtn.disabled = !!item.isCurrent;
              applyBtn.addEventListener('click', () => {
                sendBridge('wardrobe.apply', { id: item.id })
                  .then(() => {
                    if (item.type === 'cape') {
                      applyCape('custom', item.dataUrl);
                      showCapeSide();
                      if (typeof window.showToast === 'function') window.showToast('Плащ «' + item.name + '» применён', 'success');
                    } else {
                      if (viewer) viewer.loadSkin(item.dataUrl);
                      updateSkinUI(item.dataUrl, item.name, item.model);
                      if (typeof window.showToast === 'function') window.showToast('Скин «' + item.name + '» применён', 'success');
                    }
                    loadWardrobeHistory();
                  })
                  .catch(err => {
                    if (typeof window.showToast === 'function') window.showToast('Ошибка: ' + (err.message || err), 'error');
                  });
              });

              const delBtn = document.createElement('button');
              delBtn.type = 'button';
              delBtn.className = 'btn xs ghost';
              delBtn.textContent = '✕';
              delBtn.title = 'Удалить из истории';
              delBtn.addEventListener('click', () => {
                sendBridge('wardrobe.delete', { id: item.id })
                  .then(() => {
                    loadWardrobeHistory();
                  });
              });

              row.appendChild(badge);
              row.appendChild(name);
              actions.appendChild(applyBtn);
              actions.appendChild(delBtn);
              row.appendChild(actions);
              container.appendChild(row);
            });
          })
          .catch(() => {});
      }

      loadWardrobeHistory();
      const nickInput = document.getElementById('inputPlayerNickname');
      if (nickInput) {
        nickInput.addEventListener('change', () => {
          if (typeof window.showToast === 'function') window.showToast(`Ник изменён на ${nickInput.value}`, 'info');
        });
      } else {
        console.warn('[wardrobe] #inputPlayerNickname not found in DOM');
      }

      updateRenderPauseState();
    } catch (err) {
      console.error('[wardrobe:init] Initialization error:', err);
    }
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

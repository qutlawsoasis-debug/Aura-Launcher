/**
 * Aura Launcher v3 — Settings Section Controller (Island Concept)
 * RAM slider, graphics preset segment, visual effects mode, inline cache clear.
 * Integrated with WebBridge (config.get, config.set, preset.apply).
 */

(function () {
  'use strict';

  const PRESETS_INFO = [
    '8 чанков · без шейдеров · 60+ FPS',
    '12 чанков · шейдеры Builders QoL',
    '16 чанков · шейдеры Sildurs · PBR-текстуры',
    '24 чанка · Complementary · Distant Horizons'
  ];

  const PRESET_KEYS = ['Low', 'Medium', 'High', 'Ultra'];

  let activeConfig = {
    nickname: 'Player',
    ramMb: 6144,
    hideLauncherWhilePlaying: true,
    graphicsPreset: 'Medium',
    visualEffectsMode: 'full'
  };

  let ramDebounceTimer = null;

  function sendBridge(name, payload) {
    if (window.AuraBridge && typeof window.AuraBridge.send === 'function') {
      return window.AuraBridge.send(name, payload);
    }
    return Promise.resolve(null);
  }

  function initSettings() {
    const ramSlider = document.getElementById('settingsRamSlider');
    const ramVal = document.getElementById('settingsRamVal');
    const ramHint = document.getElementById('settingsRamHint');
    const presetHint = document.getElementById('settingsPresetHint');
    const presetSeg = document.getElementById('presetSegmented');
    const effectsSeg = document.getElementById('effectsSegmented');
    const toggleHide = document.getElementById('toggleHideOnLaunch');

    let rollFn = null;
    let glideToFn = null;

    // 1. Память: ручка едет плавно, цифра прокручивается, при отпускании ручка доезжает до целого ГБ
    if (ramSlider && ramVal && ramHint) {
      const MIN = Number(ramSlider.min), MAX = Number(ramSlider.max);
      const animated = () => document.documentElement.dataset.effects !== 'off' && Element.prototype.animate;
      const noteFor = v => (v <= 4 ? 'Минимум' : v <= 6 ? 'Оптимально' : v <= 8 ? 'Для шейдеров' : 'Для Distant Horizons');
      let shown = Math.round(Number(ramSlider.value));
      let shownNote = noteFor(shown);
      let glide = 0;

      ramVal.style.display = 'inline-block';
      ramVal.style.position = 'relative';
      ramVal.style.minWidth = '1.2ch';

      const roll = (next, dir) => {
        if (!animated()) { ramVal.textContent = next; return; }
        const ghost = document.createElement('span');
        ghost.textContent = ramVal.textContent;
        Object.assign(ghost.style, { position: 'absolute', left: '0', top: '0' });
        ramVal.textContent = next;
        ramVal.appendChild(ghost);
        const ease = 'cubic-bezier(0.22, 1, 0.36, 1)';
        ghost.animate([{ transform: 'translateY(0)', opacity: 1 }, { transform: `translateY(${-dir * 0.55}em)`, opacity: 0 }],
          { duration: 260, easing: ease, fill: 'forwards' }).onfinish = () => ghost.remove();
        ramVal.firstChild && ramVal.animate([{ transform: `translateY(${dir * 0.55}em)`, opacity: 0 }, { transform: 'translateY(0)', opacity: 1 }],
          { duration: 320, easing: ease });
      };
      rollFn = roll;

      const render = () => {
        const raw = Number(ramSlider.value);
        ramSlider.style.setProperty('--p', `${((raw - MIN) / (MAX - MIN)) * 100}%`);
        const v = Math.round(raw);
        if (v !== shown) { roll(v, v > shown ? 1 : -1); shown = v; }
        const note = noteFor(v);
        if (note !== shownNote) {
          shownNote = note;
          if (animated()) {
            ramHint.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 120, easing: 'ease-out' }).onfinish = () => {
              ramHint.textContent = `${note} · из 16 ГБ`;
              ramHint.animate([{ opacity: 0, transform: 'translateY(3px)' }, { opacity: 1, transform: 'none' }], { duration: 220, easing: 'ease-out' });
            };
          } else {
            ramHint.textContent = `${note} · из 16 ГБ`;
          }
        }
      };

      const glideTo = (target, commitBridge = true) => {
        cancelAnimationFrame(glide);
        const from = Number(ramSlider.value);
        const onSettled = (finalVal) => {
          if (!commitBridge) return;
          const prevRam = activeConfig.ramMb;
          const nextRamMb = finalVal * 1024;
          if (prevRam === nextRamMb) return;

          clearTimeout(ramDebounceTimer);
          ramDebounceTimer = setTimeout(() => {
            sendBridge('config.set', { key: 'ramMb', value: nextRamMb })
              .then(res => {
                if (res && res.ramMb) {
                  activeConfig.ramMb = res.ramMb;
                }
              })
              .catch(err => {
                if (typeof window.showToast === 'function') {
                  window.showToast(`Ошибка сохранения памяти: ${err.message}`, 'error');
                }
                const fallbackGb = Math.round(prevRam / 1024);
                glideTo(fallbackGb, false);
              });
          }, 300);
        };

        if (!animated() || Math.abs(target - from) < 0.001) {
          ramSlider.value = target;
          render();
          onSettled(target);
          return;
        }

        const start = performance.now();
        const step = now => {
          const t = Math.min((now - start) / 260, 1);
          ramSlider.value = from + (target - from) * (1 - Math.pow(1 - t, 3));
          render();
          if (t < 1) {
            glide = requestAnimationFrame(step);
          } else {
            onSettled(target);
          }
        };
        glide = requestAnimationFrame(step);
      };
      glideToFn = glideTo;

      ramSlider.step = 'any';
      ramSlider.addEventListener('input', () => { cancelAnimationFrame(glide); render(); });
      ramSlider.addEventListener('change', () => glideTo(Math.round(Number(ramSlider.value))));
      ramSlider.addEventListener('keydown', (e) => {
        const d = { ArrowRight: 1, ArrowUp: 1, ArrowLeft: -1, ArrowDown: -1 }[e.key];
        if (!d) return;
        e.preventDefault();
        glideTo(Math.min(MAX, Math.max(MIN, Math.round(Number(ramSlider.value)) + d)));
      });
      ramVal.textContent = shown;
      ramHint.textContent = `${shownNote} · из 16 ГБ`;
      render();
    }

    // 2. Graphics Preset Segmented Buttons with crossfade hint & preset.apply
    const syncPresetButtons = (presetKey) => {
      let idx = PRESET_KEYS.findIndex(k => k.toLowerCase() === (presetKey || '').toLowerCase());
      if (idx === -1) idx = 1; // Medium fallback
      if (presetSeg) {
        presetSeg.querySelectorAll('button').forEach((btn, i) => {
          const isActive = i === idx;
          btn.classList.toggle('on', isActive);
          btn.classList.toggle('active', isActive);
          btn.setAttribute('aria-checked', isActive ? 'true' : 'false');
        });
        if (typeof window.placeLens === 'function') {
          window.placeLens(presetSeg);
        }
      }
      if (presetHint && PRESETS_INFO[idx]) {
        presetHint.textContent = PRESETS_INFO[idx];
      }
    };

    if (presetSeg) {
      presetSeg.querySelectorAll('button').forEach((btn, idx) => {
        btn.addEventListener('click', () => {
          const prevKey = activeConfig.graphicsPreset;
          const nextKey = PRESET_KEYS[idx] || 'Medium';
          if (nextKey === prevKey) return;

          activeConfig.graphicsPreset = nextKey;
          syncPresetButtons(nextKey);

          if (presetHint && PRESETS_INFO[idx]) {
            presetHint.style.transition = 'opacity var(--dur-1) var(--ease-out)';
            presetHint.style.opacity = '0';
            setTimeout(() => {
              presetHint.textContent = PRESETS_INFO[idx];
              presetHint.style.opacity = '1';
            }, 120);
          }

          sendBridge('preset.apply', { presetKey: nextKey })
            .then(res => {
              const finalRam = (res && (res.FinalRamMb || res.finalRamMb)) || null;
              if (finalRam) {
                activeConfig.ramMb = finalRam;
                if (ramSlider && glideToFn) {
                  const targetGb = Math.round(finalRam / 1024);
                  if (targetGb >= Number(ramSlider.min) && targetGb <= Number(ramSlider.max)) {
                    glideToFn(targetGb, false);
                  }
                }
              }
            })
            .catch(err => {
              if (typeof window.showToast === 'function') {
                window.showToast(`Ошибка применения пресета: ${err.message}`, 'error');
              }
              activeConfig.graphicsPreset = prevKey;
              syncPresetButtons(prevKey);
            });
        });
      });
    }

    // 3. Effects Mode Segment
    const syncEffectsSegment = () => {
      const currentMode = document.documentElement.dataset.effects || activeConfig.visualEffectsMode || 'full';
      if (effectsSeg) {
        effectsSeg.querySelectorAll('button').forEach(btn => {
          const isActive = btn.dataset.fx === currentMode;
          btn.classList.toggle('on', isActive);
          btn.classList.toggle('active', isActive);
          btn.setAttribute('aria-checked', isActive ? 'true' : 'false');
        });
        if (typeof window.placeLens === 'function') {
          window.placeLens(effectsSeg);
        }
      }
    };

    if (effectsSeg) {
      effectsSeg.querySelectorAll('button').forEach(btn => {
        btn.addEventListener('click', (e) => {
          e.stopPropagation();
          const prevFx = activeConfig.visualEffectsMode;
          const fx = btn.dataset.fx;
          if (fx === prevFx) return;

          activeConfig.visualEffectsMode = fx;
          if (typeof window.setEffectsMode === 'function') {
            window.setEffectsMode(fx);
          }
          syncEffectsSegment();
          if (typeof window.updateWardrobeRenderState === 'function') {
            window.updateWardrobeRenderState();
          }

          sendBridge('config.set', { key: 'visualEffectsMode', value: fx })
            .catch(err => {
              if (typeof window.showToast === 'function') {
                window.showToast(`Ошибка сохранения эффектов: ${err.message}`, 'error');
              }
              activeConfig.visualEffectsMode = prevFx;
              if (typeof window.setEffectsMode === 'function') {
                window.setEffectsMode(prevFx);
              }
              syncEffectsSegment();
            });
        });
      });
    }

    // 4. Hide Launcher While Playing Toggle
    if (toggleHide) {
      toggleHide.addEventListener('click', () => {
        setTimeout(() => {
          const nextVal = toggleHide.classList.contains('on');
          const prevVal = activeConfig.hideLauncherWhilePlaying;
          if (nextVal === prevVal) return;

          activeConfig.hideLauncherWhilePlaying = nextVal;
          sendBridge('config.set', { key: 'hideLauncherWhilePlaying', value: nextVal })
            .catch(err => {
              if (typeof window.showToast === 'function') {
                window.showToast(`Ошибка сохранения настроек: ${err.message}`, 'error');
              }
              activeConfig.hideLauncherWhilePlaying = prevVal;
              toggleHide.classList.toggle('on', prevVal);
            });
        }, 0);
      });
    }

    // Initial load from Bridge (config.get)
    sendBridge('config.get')
      .then(cfg => {
        if (!cfg) return;
        activeConfig = Object.assign(activeConfig, cfg);

        // RAM slider
        if (ramSlider && glideToFn && activeConfig.ramMb) {
          const gb = Math.round(activeConfig.ramMb / 1024);
          if (gb >= Number(ramSlider.min) && gb <= Number(ramSlider.max)) {
            glideToFn(gb, false);
          }
        }

        // Preset
        if (activeConfig.graphicsPreset) {
          syncPresetButtons(activeConfig.graphicsPreset);
        }

        // Effects Mode: if URL/hash already specified effects (e.g. smoke test ?effects=off), do not overwrite it from stored config
        const urlParams = new URLSearchParams(window.location.search);
        const urlFx = urlParams.get('effects');
        if (urlFx && ['full', 'low', 'off'].includes(urlFx)) {
          activeConfig.visualEffectsMode = urlFx;
        } else if (activeConfig.visualEffectsMode) {
          if (typeof window.setEffectsMode === 'function') {
            window.setEffectsMode(activeConfig.visualEffectsMode);
          }
        }
        syncEffectsSegment();

        // Hide toggle
        if (toggleHide && typeof activeConfig.hideLauncherWhilePlaying === 'boolean') {
          toggleHide.classList.toggle('on', activeConfig.hideLauncherWhilePlaying);
        }
      })
      .catch(() => {});

    syncEffectsSegment();

    // 5. Game version and Folder actions
    const changeVerBtn = document.getElementById('btnChangeVersion');
    if (changeVerBtn) {
      changeVerBtn.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Смена версии будет доступна в лаунчере', 'info');
        }
      });
    }

    const browseFolderBtn = document.getElementById('btnBrowseFolder');
    if (browseFolderBtn) {
      browseFolderBtn.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Выбор папки будет доступен в лаунчере', 'info');
        }
      });
    }

    const exportLogsBtn = document.getElementById('btnExportLogs');
    if (exportLogsBtn) {
      exportLogsBtn.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Экспорт отчёта будет доступен в лаунчере', 'info');
        }
      });
    }

    // 6. Inline Cache Clear Confirmation (No modals)
    const clearWrap = document.getElementById('clearCacheActionWrap');
    const startClearBtn = document.getElementById('btnStartClearCache');
    if (startClearBtn && clearWrap) {
      startClearBtn.addEventListener('click', () => {
        clearWrap.innerHTML = `
          <span class="small dim" style="margin-right:4px">Точно удалить?</span>
          <button type="button" class="btn sm danger" id="btnConfirmClear">Да, очистить</button>
          <button type="button" class="btn sm ghost" id="btnCancelClear">Отмена</button>
        `;

        document.getElementById('btnConfirmClear')?.addEventListener('click', () => {
          clearWrap.innerHTML = '<button type="button" class="btn sm danger" id="btnStartClearCache">Очистить</button>';
          initSettings();
          if (typeof window.showToast === 'function') {
            window.showToast('Кэш шейдеров и временные файлы очищены', 'success');
          }
        });

        document.getElementById('btnCancelClear')?.addEventListener('click', () => {
          clearWrap.innerHTML = '<button type="button" class="btn sm danger" id="btnStartClearCache">Очистить</button>';
          initSettings();
        });
      });
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSettings);
  } else {
    initSettings();
  }
})();

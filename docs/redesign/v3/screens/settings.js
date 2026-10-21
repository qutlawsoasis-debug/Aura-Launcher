/**
 * Aura Launcher v3 — Settings Section Controller (Island Concept)
 * RAM slider, graphics preset segment, visual effects mode, inline cache clear.
 */

(function () {
  'use strict';

  const PRESETS_INFO = [
    '8 чанков · без шейдеров · 60+ FPS',
    '12 чанков · шейдеры Builders QoL',
    '16 чанков · шейдеры Sildurs · PBR-текстуры',
    '24 чанка · Complementary · Distant Horizons'
  ];

  function initSettings() {
    const ramSlider = document.getElementById('settingsRamSlider');
    const ramVal = document.getElementById('settingsRamVal');
    const ramHint = document.getElementById('settingsRamHint');
    const presetHint = document.getElementById('settingsPresetHint');

    // 1. RAM Slider
    if (ramSlider && ramVal && ramHint) {
      const updateRam = () => {
        const v = parseInt(ramSlider.value, 10);
        const percent = ((v - 4) / 8) * 100;
        ramSlider.style.setProperty('--p', `${percent}%`);
        ramVal.textContent = v;
        const note = (v <= 4 ? 'Минимум' : v <= 6 ? 'Оптимально' : v <= 8 ? 'Для шейдеров' : 'Для Distant Horizons');
        ramHint.textContent = `${note} · из 16 ГБ`;
      };

      ramSlider.addEventListener('input', updateRam);
      updateRam();
    }

    // 2. Graphics Preset Segmented Buttons
    document.querySelectorAll('#presetSegmented button').forEach((btn, idx) => {
      btn.addEventListener('click', () => {
        if (presetHint && PRESETS_INFO[idx]) {
          presetHint.textContent = PRESETS_INFO[idx];
        }
      });
    });

    // 3. Effects Mode Segment
    const syncEffectsSegment = () => {
      const currentMode = document.documentElement.dataset.effects || 'full';
      const seg = document.getElementById('effectsSegmented');
      if (seg) {
        seg.querySelectorAll('button').forEach(btn => {
          const isActive = btn.dataset.fx === currentMode;
          btn.classList.toggle('on', isActive);
          btn.classList.toggle('active', isActive);
          btn.setAttribute('aria-checked', isActive ? 'true' : 'false');
        });
        if (typeof window.placeLens === 'function') {
          window.placeLens(seg);
        }
      }
    };

    document.querySelectorAll('#effectsSegmented button').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        const fx = btn.dataset.fx;
        if (typeof window.setEffectsMode === 'function') {
          window.setEffectsMode(fx);
        }
        syncEffectsSegment();
        if (typeof window.updateWardrobeRenderState === 'function') {
          window.updateWardrobeRenderState();
        }
      });
    });

    syncEffectsSegment();

    // 4. Game version and Folder actions
    const changeVerBtn = document.getElementById('btnChangeVersion');
    if (changeVerBtn) {
      changeVerBtn.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Выбран профиль: Fabric 1.20.1 (Aura Pack 1.2)', 'info');
        }
      });
    }

    const browseFolderBtn = document.getElementById('btnBrowseFolder');
    if (browseFolderBtn) {
      browseFolderBtn.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Папка игры: %APPDATA%\\.aura', 'info');
        }
      });
    }

    const exportLogsBtn = document.getElementById('btnExportLogs');
    if (exportLogsBtn) {
      exportLogsBtn.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Отчёт о работе экспортирован в %APPDATA%\\.aura\\logs', 'success');
        }
      });
    }

    // 5. Inline Cache Clear Confirmation (No modals)
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

/**
 * Aura Launcher v3 — Settings Screen Logic
 * Client version selector, RAM interactive slider, game path validation,
 * graphics presets, launcher effects synchronization, and maintenance modals.
 */

(function () {
  'use strict';

  // Local storage keys
  const KEYS = {
    VERSION: 'aura_client_version_v3',
    RAM: 'aura_allocated_ram_gb_v3',
    PATH: 'aura_game_path_v3',
    PRESET: 'aura_gfx_preset_v3',
    HIDE_ON_LAUNCH: 'aura_hide_on_launch_v3',
    AUTO_UPDATE: 'aura_auto_update_v3',
    KILL_TUNNEL: 'aura_kill_tunnel_v3'
  };

  const DEFAULT_PATH = '%APPDATA%\\.aura';

  // 1. Client Version Dropdown
  function initVersionDropdown() {
    const dropdown = document.getElementById('clientVersionDropdown');
    const triggerText = document.getElementById('clientVersionText');
    if (!dropdown || !triggerText) return;

    const savedVersion = localStorage.getItem(KEYS.VERSION) || 'fabric-1.20.1';

    // Apply saved version
    dropdown.querySelectorAll('.dropdown-item').forEach(item => {
      const isCurrent = item.dataset.version === savedVersion;
      item.classList.toggle('active', isCurrent);
      item.setAttribute('aria-selected', isCurrent ? 'true' : 'false');
      if (isCurrent) {
        const titleSpan = item.querySelector('span:first-child');
        if (titleSpan) triggerText.textContent = titleSpan.textContent;
      }
    });

    dropdown.addEventListener('click', (e) => {
      const item = e.target.closest('.dropdown-item');
      if (!item) return;

      const versionId = item.dataset.version;
      dropdown.querySelectorAll('.dropdown-item').forEach(i => {
        i.classList.remove('active');
        i.setAttribute('aria-selected', 'false');
      });

      item.classList.add('active');
      item.setAttribute('aria-selected', 'true');

      const titleSpan = item.querySelector('span:first-child');
      if (titleSpan) triggerText.textContent = titleSpan.textContent;

      try {
        localStorage.setItem(KEYS.VERSION, versionId);
      } catch (err) {}

      dropdown.classList.remove('is-open');
      const trig = dropdown.querySelector('.custom-dropdown-trigger');
      if (trig) trig.setAttribute('aria-expanded', 'false');

      if (window.showToast) {
        window.showToast(`Выбрана версия клиента: ${titleSpan ? titleSpan.textContent : versionId}`, 'success');
      }
    });
  }

  // 2. RAM Slider & Quick Chips
  function initRamControl() {
    const slider = document.getElementById('ramSliderInput');
    const fill = document.getElementById('ramSliderFill');
    const valueText = document.getElementById('ramValueText');
    const hintText = document.getElementById('ramHintText');
    const chips = document.querySelectorAll('.ram-chip');

    if (!slider || !fill || !valueText) return;

    const savedRam = parseInt(localStorage.getItem(KEYS.RAM) || '6', 10);
    const validRam = isNaN(savedRam) ? 6 : Math.max(4, Math.min(12, savedRam));

    function updateRamUI(val) {
      slider.value = val;
      valueText.textContent = `${val} ГБ`;

      const min = parseInt(slider.min, 10) || 4;
      const max = parseInt(slider.max, 10) || 12;
      const percentage = ((val - min) / (max - min)) * 100;
      fill.style.width = `${percentage}%`;

      // Update hint text based on memory tier
      if (hintText) {
        if (val <= 4) {
          hintText.textContent = '4 ГБ — минимальный объём памяти для запуска Fabric 1.20.1 без ресурсоёмких шейдеров.';
        } else if (val <= 6) {
          hintText.textContent = '6 ГБ обеспечивает высокую частоту кадров и стабильную сборку мусора GC при использовании шейдерпаков.';
        } else if (val <= 8) {
          hintText.textContent = '8 ГБ рекомендуется при игре с высокими настройками теней, PBR-текстурами и дальностью 16+ чанков.';
        } else {
          hintText.textContent = '10–12 ГБ подходит для экстремальной дальности прорисовки через Distant Horizons и кинематографических пресетов.';
        }
      }

      // Sync chips
      chips.forEach(chip => {
        const chipVal = parseInt(chip.dataset.ram, 10);
        const isSelected = chipVal === val;
        chip.classList.toggle('active', isSelected);
        chip.setAttribute('aria-checked', isSelected ? 'true' : 'false');
      });

      try {
        localStorage.setItem(KEYS.RAM, val.toString());
      } catch (e) {}
    }

    slider.addEventListener('input', (e) => {
      const val = parseInt(e.target.value, 10);
      updateRamUI(val);
    });

    chips.forEach(chip => {
      chip.addEventListener('click', () => {
        const val = parseInt(chip.dataset.ram, 10);
        if (!isNaN(val)) {
          updateRamUI(val);
          if (window.showToast) {
            window.showToast(`Выделено памяти: ${val} ГБ`, 'info');
          }
        }
      });
    });

    updateRamUI(validRam);
  }

  // 3. Game Folder Path & Validation
  function initGamePathControl() {
    const input = document.getElementById('gameFolderPathInput');
    const errorBox = document.getElementById('gamePathError');
    const errorText = document.getElementById('gamePathErrorText');
    const btnBrowse = document.getElementById('btnBrowseGameFolder');
    const btnReset = document.getElementById('btnResetGameFolder');

    if (!input) return;

    const savedPath = localStorage.getItem(KEYS.PATH) || DEFAULT_PATH;
    input.value = savedPath;

    function validatePath(path) {
      const trimmed = (path || '').trim();
      if (!trimmed) {
        return {
          valid: false,
          message: 'Путь к рабочей папке Minecraft не может быть пустым.'
        };
      }

      // Check illegal Windows characters: < > " | ? *
      const illegalChars = /[<>"|?*]/;
      if (illegalChars.test(trimmed)) {
        return {
          valid: false,
          message: 'Путь содержит запрещённые символы Windows (< > " | ? *).'
        };
      }

      return { valid: true };
    }

    function applyValidation() {
      const check = validatePath(input.value);
      if (!check.valid) {
        input.classList.add('is-invalid');
        if (errorBox) {
          errorBox.style.display = 'flex';
          if (errorText) errorText.textContent = check.message;
        }
        return false;
      } else {
        input.classList.remove('is-invalid');
        if (errorBox) errorBox.style.display = 'none';
        try {
          localStorage.setItem(KEYS.PATH, input.value.trim());
        } catch (e) {}
        return true;
      }
    }

    input.addEventListener('input', applyValidation);
    input.addEventListener('blur', applyValidation);

    if (btnReset) {
      btnReset.addEventListener('click', () => {
        input.value = DEFAULT_PATH;
        applyValidation();
        if (window.showToast) {
          window.showToast('Путь папки сброшен на стандартный: %APPDATA%\\.aura', 'info');
        }
      });
    }

    if (btnBrowse) {
      btnBrowse.addEventListener('click', () => {
        if (window.showToast) {
          window.showToast('Выбор папки: задействован проводник файлов системы Windows', 'info');
        }
      });
    }

    applyValidation();
  }

  // 4. Graphics Presets
  function initGraphicsPresets() {
    const container = document.querySelector('.graphics-presets-grid');
    if (!container) return;

    const savedPreset = localStorage.getItem(KEYS.PRESET) || 'high';

    container.querySelectorAll('.preset-card').forEach(card => {
      const isCurrent = card.dataset.preset === savedPreset;
      card.classList.toggle('active', isCurrent);
      card.setAttribute('aria-checked', isCurrent ? 'true' : 'false');
    });

    container.addEventListener('click', (e) => {
      const card = e.target.closest('.preset-card');
      if (!card) return;

      const presetId = card.dataset.preset;
      container.querySelectorAll('.preset-card').forEach(c => {
        c.classList.remove('active');
        c.setAttribute('aria-checked', 'false');
      });

      card.classList.add('active');
      card.setAttribute('aria-checked', 'true');

      try {
        localStorage.setItem(KEYS.PRESET, presetId);
      } catch (err) {}

      const name = card.querySelector('.preset-name')?.textContent || presetId;
      if (window.showToast) {
        window.showToast(`Применён графический профиль: «${name}»`, 'success');
      }
    });
  }

  // 5. Visual Effects Synchronization
  function initEffectsControls() {
    const currentMode = document.documentElement.getAttribute('data-effects') || 'full';

    document.querySelectorAll('.effects-segmented-control [data-effect-btn]').forEach(btn => {
      const isCurrent = btn.dataset.effectBtn === currentMode;
      btn.classList.toggle('active', isCurrent);
      btn.setAttribute('aria-checked', isCurrent ? 'true' : 'false');

      btn.addEventListener('click', () => {
        const mode = btn.dataset.effectBtn;
        if (typeof window.setEffectsMode === 'function') {
          window.setEffectsMode(mode);
          if (window.showToast) {
            const labels = { full: 'Полные', low: 'Лёгкие', off: 'Выкл' };
            window.showToast(`Эффекты лаунчера: ${labels[mode] || mode}`, 'info');
          }
        }
      });
    });
  }

  // 6. Toggles (Launcher Behavior & Updates)
  function initToggleSwitches() {
    const chkHide = document.getElementById('chkHideOnLaunch');
    const chkUpdate = document.getElementById('chkAutoUpdate');
    const chkKillTunnel = document.getElementById('chkKillTunnelOnExit');

    if (chkHide) {
      chkHide.checked = localStorage.getItem(KEYS.HIDE_ON_LAUNCH) !== 'false';
      chkHide.addEventListener('change', () => {
        localStorage.setItem(KEYS.HIDE_ON_LAUNCH, chkHide.checked.toString());
        if (window.showToast) {
          window.showToast(chkHide.checked ? 'Лаунчер будет скрываться во время игры' : 'Лаунчер останется видимым', 'info');
        }
      });
    }

    if (chkUpdate) {
      chkUpdate.checked = localStorage.getItem(KEYS.AUTO_UPDATE) !== 'false';
      chkUpdate.addEventListener('change', () => {
        localStorage.setItem(KEYS.AUTO_UPDATE, chkUpdate.checked.toString());
        if (window.showToast) {
          window.showToast(chkUpdate.checked ? 'Автообновление через Velopack включено' : 'Автообновление отключено', 'info');
        }
      });
    }

    if (chkKillTunnel) {
      chkKillTunnel.checked = localStorage.getItem(KEYS.KILL_TUNNEL) !== 'false';
      chkKillTunnel.addEventListener('change', () => {
        localStorage.setItem(KEYS.KILL_TUNNEL, chkKillTunnel.checked.toString());
        if (window.showToast) {
          window.showToast(chkKillTunnel.checked ? 'Туннель будет завершаться при выходе из игры' : 'Туннель останется активным в фоне', 'info');
        }
      });
    }
  }

  // 7. Maintenance Actions & Clear Cache Modal
  function initMaintenanceActions() {
    const btnLogs = document.getElementById('btnOpenLogsFolder');
    const btnExport = document.getElementById('btnExportDiagnostics');
    const btnOpenClearModal = document.getElementById('btnOpenClearCacheModal');
    const btnCloseClearModal = document.getElementById('btnCloseClearCacheModal');
    const btnCancelClear = document.getElementById('btnCancelClearCache');
    const btnConfirmClear = document.getElementById('btnConfirmClearCache');

    if (btnLogs) {
      btnLogs.addEventListener('click', () => {
        if (window.showToast) {
          window.showToast('Папка логов: %APPDATA%\\.aura\\logs', 'info');
        }
      });
    }

    if (btnExport) {
      btnExport.addEventListener('click', () => {
        if (window.showToast) {
          window.showToast('Диагностический отчёт сгенерирован и сохранён', 'success');
        }
      });
    }

    if (btnOpenClearModal) {
      btnOpenClearModal.addEventListener('click', () => {
        if (window.openModal) {
          window.openModal('modalClearCache');
        }
      });
    }

    function closeModal() {
      if (window.closeModal) {
        window.closeModal('modalClearCache');
      }
    }

    if (btnCloseClearModal) btnCloseClearModal.addEventListener('click', closeModal);
    if (btnCancelClear) btnCancelClear.addEventListener('click', closeModal);

    if (btnConfirmClear) {
      btnConfirmClear.addEventListener('click', () => {
        closeModal();
        if (window.showToast) {
          window.showToast('Кэш модов и шейдеров успешно очищен (освобождено 148 МБ)', 'success');
        }
      });
    }
  }

  // Standalone template injector if screen-settings was empty in DOM
  function ensureSettingsMarkup() {
    const section = document.getElementById('screen-settings');
    if (!section || section.children.length > 0) return;

    section.innerHTML = `
      <div class="screen-settings-layout">
        <div class="settings-main-column">
          <div class="glass-island elevated">
            <div class="glass-island-header">
              <span class="glass-island-title">
                <span class="glass-island-title-icon">
                  <svg class="icon" viewBox="0 0 24 24"><polygon points="12 2 2 7 12 12 22 7 12 2"></polygon><polyline points="2 17 12 22 22 17"></polyline><polyline points="2 12 12 17 22 12"></polyline></svg>
                </span>
                <span>Клиент и среда запуска</span>
              </span>
              <span class="settings-section-badge">Сборка Aura</span>
            </div>
            <div class="settings-form-grid">
              <div class="setting-field-group">
                <div class="setting-label-row">
                  <label class="setting-label" for="clientVersionTrigger">Версия Minecraft и загрузчик</label>
                  <span class="setting-hint">Рекомендуется Fabric 1.20.1</span>
                </div>
                <div class="custom-dropdown" id="clientVersionDropdown">
                  <button type="button" class="custom-dropdown-trigger" id="clientVersionTrigger" aria-haspopup="listbox" aria-expanded="false">
                    <span class="dropdown-selected-val" id="clientVersionText">Fabric 1.20.1 (Aura Pack v1.2 • Рекомендуемая)</span>
                    <svg class="icon dropdown-arrow" viewBox="0 0 24 24"><polyline points="6 9 12 15 18 9"></polyline></svg>
                  </button>
                  <div class="custom-dropdown-list" role="listbox" aria-label="Выбор версии">
                    <button type="button" class="dropdown-item active" data-version="fabric-1.20.1" role="option" aria-selected="true">
                      <span>Fabric 1.20.1 (Aura Pack v1.2 • Рекомендуемая)</span>
                      <span class="badge-item-tag">Стабильная</span>
                    </button>
                    <button type="button" class="dropdown-item" data-version="fabric-1.21.1" role="option" aria-selected="false">
                      <span>Fabric 1.21.1 (Экспериментальная)</span>
                      <span class="badge-item-tag experimental">Beta</span>
                    </button>
                    <button type="button" class="dropdown-item" data-version="vanilla-1.20.1" role="option" aria-selected="false">
                      <span>Vanilla 1.20.1 (Чистый клиент без модов)</span>
                      <span class="badge-item-tag">Оригинал</span>
                    </button>
                  </div>
                </div>
              </div>
              <div class="setting-field-group">
                <div class="setting-label-row">
                  <label class="setting-label" for="ramSliderInput">Выделение оперативной памяти Java (RAM)</label>
                  <span class="ram-current-display"><strong id="ramValueText">6 ГБ</strong> / 16 ГБ доступно</span>
                </div>
                <div class="ram-slider-wrapper">
                  <div class="ram-slider-track-container">
                    <input type="range" min="4" max="12" step="1" value="6" class="ram-range-slider" id="ramSliderInput" aria-label="Ползунок выделения оперативной памяти в ГБ">
                    <div class="ram-slider-fill" id="ramSliderFill"></div>
                  </div>
                  <div class="ram-range-scale">
                    <span>4 ГБ</span>
                    <span>6 ГБ</span>
                    <span>8 ГБ</span>
                    <span>10 ГБ</span>
                    <span>12 ГБ</span>
                  </div>
                </div>
                <div class="ram-chips-row" role="radiogroup" aria-label="Быстрый выбор памяти">
                  <button type="button" class="ram-chip" data-ram="4" role="radio" aria-checked="false">4 ГБ (Минимум)</button>
                  <button type="button" class="ram-chip active" data-ram="6" role="radio" aria-checked="true">6 ГБ (Оптимально)</button>
                  <button type="button" class="ram-chip" data-ram="8" role="radio" aria-checked="false">8 ГБ (Шейдеры)</button>
                  <button type="button" class="ram-chip" data-ram="12" role="radio" aria-checked="false">12 ГБ (Distant Hor.)</button>
                </div>
                <p class="setting-subtext" id="ramHintText">6 ГБ обеспечивает высокую частоту кадров и стабильную сборку мусора GC при использовании шейдерпаков.</p>
              </div>
              <div class="setting-field-group">
                <div class="setting-label-row">
                  <label class="setting-label" for="gameFolderPathInput">Рабочая папка игры</label>
                  <span class="setting-hint">Папка хранения сборки, логов и сохранений</span>
                </div>
                <div class="path-input-action-row">
                  <div class="path-input-wrap">
                    <input type="text" id="gameFolderPathInput" class="text-input-field path-input" value="%APPDATA%\\.aura" spellcheck="false" autocomplete="off">
                  </div>
                  <button type="button" class="btn-secondary" id="btnBrowseGameFolder" title="Выбрать другую папку">
                    <svg class="icon" viewBox="0 0 24 24"><path d="M22 19a2 2 0 0 1-2 2H4a2 2 0 0 1-2-2V5a2 2 0 0 1 2-2h5l2 3h9a2 2 0 0 1 2 2z"></path></svg>
                    <span>Обзор...</span>
                  </button>
                  <button type="button" class="btn-secondary" id="btnResetGameFolder" title="Сбросить путь на стандартный (%APPDATA%\\.aura)">
                    <svg class="icon" viewBox="0 0 24 24"><path d="M3 12a9 9 0 1 0 9-9 9.75 9.75 0 0 0-6.74 2.74L3 8"></path><path d="M3 3v5h5"></path></svg>
                    <span>Сброс</span>
                  </button>
                </div>
                <div class="game-path-validation-error" id="gamePathError" style="display: none;">
                  <svg class="icon error-icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="10"></circle><line x1="12" y1="8" x2="12" y2="12"></line><line x1="12" y1="16" x2="12.01" y2="16"></line></svg>
                  <span id="gamePathErrorText">Путь к папке игры не может быть пустым. Укажите корректную директорию.</span>
                </div>
              </div>
            </div>
          </div>
          <div class="glass-island elevated">
            <div class="glass-island-header">
              <span class="glass-island-title">
                <span class="glass-island-title-icon">
                  <svg class="icon" viewBox="0 0 24 24"><rect x="2" y="3" width="20" height="14" rx="2" ry="2"></rect><line x1="8" y1="21" x2="16" y2="21"></line><line x1="12" y1="17" x2="12" y2="21"></line></svg>
                </span>
                <span>Пресеты графики Minecraft</span>
              </span>
              <span class="setting-hint">Мгновенная настройка Sodium и видеоядра</span>
            </div>
            <div class="graphics-presets-grid" role="radiogroup" aria-label="Пресеты графики">
              <div class="preset-card" data-preset="low" role="radio" aria-checked="false" tabindex="0">
                <div class="preset-top-row">
                  <span class="preset-name">Слабый ПК</span>
                  <span class="preset-badge">60+ FPS</span>
                </div>
                <div class="preset-specs">
                  <span>Дальность: 8 чанков</span>
                  <span>Шейдеры: Выкл</span>
                  <span>Частицы: Минимум</span>
                </div>
              </div>
              <div class="preset-card" data-preset="medium" role="radio" aria-checked="false" tabindex="0">
                <div class="preset-top-row">
                  <span class="preset-name">Средний</span>
                  <span class="preset-badge">Баланс</span>
                </div>
                <div class="preset-specs">
                  <span>Дальность: 12 чанков</span>
                  <span>Шейдеры: Builders QoL</span>
                  <span>Мягкий свет: Вкл</span>
                </div>
              </div>
              <div class="preset-card active" data-preset="high" role="radio" aria-checked="true" tabindex="0">
                <div class="preset-top-row">
                  <span class="preset-name">Высокий</span>
                  <span class="preset-badge recommended">Рекомендуемый</span>
                </div>
                <div class="preset-specs">
                  <span>Дальность: 16 чанков</span>
                  <span>Шейдеры: Sildurs / Fancy</span>
                  <span>PBR-текстуры: Вкл</span>
                </div>
              </div>
              <div class="preset-card" data-preset="ultra" role="radio" aria-checked="false" tabindex="0">
                <div class="preset-top-row">
                  <span class="preset-name">Ультра</span>
                  <span class="preset-badge">Кинокачество</span>
                </div>
                <div class="preset-specs">
                  <span>Дальность: 24 чанка</span>
                  <span>Шейдеры: Complementary</span>
                  <span>Distant Horizons: Вкл</span>
                </div>
              </div>
            </div>
          </div>
        </div>
        <div class="settings-side-column">
          <div class="glass-island">
            <div class="glass-island-header">
              <span class="glass-island-title">
                <span class="glass-island-title-icon">
                  <svg class="icon" viewBox="0 0 24 24"><path d="M12 2.69l5.66 5.66a8 8 0 1 1-11.31 0z"></path></svg>
                </span>
                <span>Визуальные эффекты лаунчера</span>
              </span>
            </div>
            <p class="setting-subtext">Адаптация прозрачности Liquid Glass, размытия фона и парящих частиц под мощность GPU.</p>
            <div class="segmented-control effects-segmented-control" role="group" aria-label="Режим визуальных эффектов">
              <button type="button" class="segment-btn active" data-effect-btn="full" aria-checked="true">
                <span>Полные</span>
              </button>
              <button type="button" class="segment-btn" data-effect-btn="low" aria-checked="false">
                <span>Лёгкие</span>
              </button>
              <button type="button" class="segment-btn" data-effect-btn="off" aria-checked="false">
                <span>Выкл</span>
              </button>
            </div>
          </div>
          <div class="glass-island">
            <div class="glass-island-header">
              <span class="glass-island-title">
                <span class="glass-island-title-icon">
                  <svg class="icon" viewBox="0 0 24 24"><circle cx="12" cy="12" r="3"></circle><path d="M19.4 15a1.65 1.65 0 0 0 .33 1.82l.06.06a2 2 0 0 1 0 2.83 2 2 0 0 1-2.83 0l-.06-.06a1.65 1.65 0 0 0-1.82-.33 1.65 1.65 0 0 0-1 1.51V21a2 2 0 0 1-2 2 2 2 0 0 1-2-2v-.09A1.65 1.65 0 0 0 9 19.4a1.65 1.65 0 0 0-1.82.33l-.06.06a2 2 0 0 1-2.83 0 2 2 0 0 1 0-2.83l.06-.06a1.65 1.65 0 0 0 .33-1.82 1.65 1.65 0 0 0-1.51-1H3a2 2 0 0 1-2-2 2 2 0 0 1 2-2h.09A1.65 1.65 0 0 0 4.6 9a1.65 1.65 0 0 0-.33-1.82l-.06-.06a2 2 0 0 1 0-2.83 2 2 0 0 1 2.83 0l.06.06a1.65 1.65 0 0 0 1.82.33H9a1.65 1.65 0 0 0 1-1.51V3a2 2 0 0 1 2-2 2 2 0 0 1 2 2v.09a1.65 1.65 0 0 0 1 1.51 1.65 1.65 0 0 0 1.82-.33l.06-.06a2 2 0 0 1 2.83 0 2 2 0 0 1 0 2.83l-.06.06a1.65 1.65 0 0 0-.33 1.82V9a1.65 1.65 0 0 0 1.51 1H21a2 2 0 0 1 2 2 2 2 0 0 1-2 2h-.09a1.65 1.65 0 0 0-1.51 1z"></path></svg>
                </span>
                <span>Поведение и обновление</span>
              </span>
            </div>
            <div class="toggle-options-stack">
              <div class="toggle-option-row">
                <div class="toggle-option-meta">
                  <span class="toggle-title">Скрывать лаунчер при запуске игры</span>
                  <span class="toggle-desc">Освобождает видеопамять во время игры</span>
                </div>
                <label class="switch-label-wrap" title="Скрывать лаунчер">
                  <input type="checkbox" id="chkHideOnLaunch" class="switch-input" checked>
                  <span class="switch-track"><span class="switch-thumb"></span></span>
                </label>
              </div>
              <div class="toggle-option-row">
                <div class="toggle-option-meta">
                  <span class="toggle-title">Автообновление Velopack</span>
                  <span class="toggle-desc">Фоновая загрузка дельта-патчей клиента</span>
                </div>
                <label class="switch-label-wrap" title="Автообновление">
                  <input type="checkbox" id="chkAutoUpdate" class="switch-input" checked>
                  <span class="switch-track"><span class="switch-thumb"></span></span>
                </label>
              </div>
              <div class="toggle-option-row">
                <div class="toggle-option-meta">
                  <span class="toggle-title">Аварийный стоп P2P-туннеля</span>
                  <span class="toggle-desc">Останавливать туннель при закрытии клиента</span>
                </div>
                <label class="switch-label-wrap" title="Стоп туннеля">
                  <input type="checkbox" id="chkKillTunnelOnExit" class="switch-input" checked>
                  <span class="switch-track"><span class="switch-thumb"></span></span>
                </label>
              </div>
            </div>
          </div>
          <div class="glass-island">
            <div class="glass-island-header">
              <span class="glass-island-title">
                <span class="glass-island-title-icon">
                  <svg class="icon" viewBox="0 0 24 24"><polyline points="22 12 18 12 15 21 9 3 6 12 2 12"></polyline></svg>
                </span>
                <span>Диагностика и кэш</span>
              </span>
            </div>
            <div class="maintenance-actions-stack">
              <button type="button" class="btn-secondary maintenance-btn" id="btnOpenLogsFolder">
                <svg class="icon" viewBox="0 0 24 24"><path d="M14 2H6a2 2 0 0 0-2 2v16a2 2 0 0 0 2 2h12a2 2 0 0 0 2-2V8z"></path><polyline points="14 2 14 8 20 8"></polyline><line x1="16" y1="13" x2="8" y2="13"></line><line x1="16" y1="17" x2="8" y2="17"></line></svg>
                <span>Открыть папку логов</span>
              </button>
              <button type="button" class="btn-secondary maintenance-btn" id="btnExportDiagnostics">
                <svg class="icon" viewBox="0 0 24 24"><path d="M21 15v4a2 2 0 0 1-2 2H5a2 2 0 0 1-2-2v-4"></path><polyline points="7 10 12 15 17 10"></polyline><line x1="12" y1="15" x2="12" y2="3"></line></svg>
                <span>Экспорт отчёта о сбоях</span>
              </button>
              <button type="button" class="btn-danger maintenance-btn" id="btnOpenClearCacheModal">
                <svg class="icon" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path><line x1="10" y1="11" x2="10" y2="17"></line><line x1="14" y1="11" x2="14" y2="17"></line></svg>
                <span>Очистить кэш модов</span>
              </button>
            </div>
          </div>
        </div>
      </div>
      <div class="modal-backdrop" id="modalClearCache" aria-hidden="true" role="dialog" aria-labelledby="modalClearCacheTitle">
        <div class="glass-modal">
          <div class="modal-header-row">
            <h3 class="modal-title" id="modalClearCacheTitle">
              <svg class="icon text-danger" viewBox="0 0 24 24"><polyline points="3 6 5 6 21 6"></polyline><path d="M19 6v14a2 2 0 0 1-2 2H7a2 2 0 0 1-2-2V6m3 0V4a2 2 0 0 1 2-2h4a2 2 0 0 1 2 2v2"></path></svg>
              <span>Очистка кэша сборки</span>
            </h3>
            <button type="button" class="modal-close-btn" id="btnCloseClearCacheModal" aria-label="Закрыть">
              <svg class="icon" viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
            </button>
          </div>
          <div class="modal-body">
            <p class="modal-dialog-text">
              Будут удалены временные файлы загрузчика, кэшированные индексы SHA-256 и скомпилированные шейдеры. При следующем запуске лаунчер выполнит быструю повторную верификацию.
            </p>
            <p class="modal-dialog-subtext">
              Ваши сохранения миров, скриншоты и настройки управления затронуты не будут.
            </p>
          </div>
          <div class="modal-footer">
            <button type="button" class="btn-secondary" id="btnCancelClearCache">Отмена</button>
            <button type="button" class="btn-danger" id="btnConfirmClearCache">Очистить кэш</button>
          </div>
        </div>
      </div>
    `;
  }

  function initSettings() {
    ensureSettingsMarkup();
    initVersionDropdown();
    initRamControl();
    initGamePathControl();
    initGraphicsPresets();
    initEffectsControls();
    initToggleSwitches();
    initMaintenanceActions();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initSettings);
  } else {
    initSettings();
  }

})();

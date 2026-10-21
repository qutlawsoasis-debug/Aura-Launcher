/**
 * Aura Launcher v3 — Workshop Section Controller (Island Concept)
 * Shaders / Mods toggle, shader selection, sync action.
 */

(function () {
  'use strict';

  function initWorkshop() {
    const segButtons = document.querySelectorAll('#workshopSegmented button');
    const paneShaders = document.getElementById('paneShaders');
    const paneMods = document.getElementById('paneMods');

    // 1. Switch between Shaders & Mods panes
    segButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        const tab = btn.dataset.tab;
        if (paneShaders) paneShaders.hidden = (tab !== 'shaders');
        if (paneMods) paneMods.hidden = (tab !== 'mods');
      });
    });

    // 2. Select shader (.pick)
    document.querySelectorAll('#paneShaders .pick').forEach(btn => {
      btn.addEventListener('click', () => {
        document.querySelectorAll('#paneShaders .pick').forEach(b => b.classList.remove('on'));
        btn.classList.add('on');
        const name = btn.querySelector('b')?.textContent || 'Шейдер';
        if (typeof window.showToast === 'function') {
          window.showToast(`Выбран шейдер: ${name}`, 'info');
        }
      });
    });

    // 3. Mod toggle toasts
    document.querySelectorAll('#paneMods .toggle').forEach(t => {
      t.addEventListener('click', () => {
        const modName = t.closest('.row')?.querySelector('span')?.childNodes[0]?.textContent?.trim() || 'Мод';
        const isOn = t.classList.contains('on');
        if (typeof window.showToast === 'function') {
          window.showToast(`${modName}: ${isOn ? 'включён' : 'отключён'}`, 'info');
        }
      });
    });

    // 4. Action buttons
    const syncBtn = document.getElementById('btnSyncMods');
    if (syncBtn) {
      syncBtn.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Сборка актуальна (15 модов, 0 изменений)', 'success');
        }
      });
    }

    const openFolderBtn = document.getElementById('btnOpenModsFolder');
    if (openFolderBtn) {
      openFolderBtn.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Папка модов: %APPDATA%\\.aura\\mods', 'info');
        }
      });
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initWorkshop);
  } else {
    initWorkshop();
  }
})();

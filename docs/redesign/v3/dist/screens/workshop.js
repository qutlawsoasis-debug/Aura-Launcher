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

    // 1. Switch between Shaders & Mods panes with fade
    segButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        const tab = btn.dataset.tab;
        const outPane = tab === 'shaders' ? paneMods : paneShaders;
        const inPane = tab === 'shaders' ? paneShaders : paneMods;

        window.swapPane(outPane, inPane);
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
          window.showToast('Синхронизация сборок будет доступна в лаунчере', 'info');
        }
      });
    }

    const openFolderBtn = document.getElementById('btnOpenModsFolder');
    if (openFolderBtn) {
      openFolderBtn.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Открытие папки модов будет доступно в лаунчере', 'info');
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

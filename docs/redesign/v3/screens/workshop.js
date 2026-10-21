/**
 * Aura Launcher v3 — Workshop Section Controller (Island Concept)
 * Shaders / Mods dynamic lists from WebBridge (workshop.list, workshop.setShader, workshop.toggleMod, pack.sync, fs.openModsFolder).
 */

(function () {
  'use strict';

  let hasLoadedList = false;

  function sendBridge(name, payload) {
    if (window.AuraBridge && typeof window.AuraBridge.send === 'function') {
      return window.AuraBridge.send(name, payload);
    }
    return Promise.resolve(null);
  }

  function initWorkshop() {
    const segButtons = document.querySelectorAll('#workshopSegmented button');
    const paneShaders = document.getElementById('paneShaders');
    const paneMods = document.getElementById('paneMods');
    const syncBtn = document.getElementById('btnSyncMods');
    const openFolderBtn = document.getElementById('btnOpenModsFolder');
    const modsTabBtn = document.querySelector('#workshopSegmented button[data-tab="mods"]');

    // 1. Switch between Shaders & Mods panes with fade
    segButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        const tab = btn.dataset.tab;
        const outPane = tab === 'shaders' ? paneMods : paneShaders;
        const inPane = tab === 'shaders' ? paneShaders : paneMods;

        if (typeof window.swapPane === 'function') {
          window.swapPane(outPane, inPane);
        }
      });
    });

    // 2. Render functions
    function renderShaders(shaders) {
      if (!paneShaders) return;
      paneShaders.innerHTML = '';

      let hasActive = false;
      shaders.forEach(shader => {
        if (shader.isActive) hasActive = true;
        const btn = document.createElement('button');
        btn.type = 'button';
        btn.className = `pick ${shader.isActive ? 'on' : ''}`;
        btn.dataset.shader = shader.fileName;

        btn.innerHTML = `
          <span class="radio"></span>
          <span>
            <b>${shader.name || shader.fileName}</b>
            <span class="small dim">${shader.fileName}</span>
          </span>
        `;

        btn.addEventListener('click', () => {
          const prevActiveBtn = paneShaders.querySelector('.pick.on');
          paneShaders.querySelectorAll('.pick').forEach(b => b.classList.remove('on'));
          btn.classList.add('on');

          sendBridge('workshop.setShader', { fileName: shader.fileName })
            .then(() => {
              if (typeof window.showToast === 'function') {
                window.showToast(`Выбран шейдер: ${shader.name || shader.fileName}`, 'info');
              }
            })
            .catch(err => {
              if (typeof window.showToast === 'function') {
                window.showToast(`Ошибка выбора шейдера: ${err.message}`, 'error');
              }
              paneShaders.querySelectorAll('.pick').forEach(b => b.classList.remove('on'));
              if (prevActiveBtn) prevActiveBtn.classList.add('on');
            });
        });

        paneShaders.appendChild(btn);
      });

      // "Без шейдеров" option
      const noneBtn = document.createElement('button');
      noneBtn.type = 'button';
      noneBtn.className = `pick ${!hasActive ? 'on' : ''}`;
      noneBtn.dataset.shader = 'none';
      noneBtn.innerHTML = `
        <span class="radio"></span>
        <span>
          <b>Без шейдеров</b>
          <span class="small dim">Vanilla</span>
        </span>
      `;
      noneBtn.addEventListener('click', () => {
        const prevActiveBtn = paneShaders.querySelector('.pick.on');
        paneShaders.querySelectorAll('.pick').forEach(b => b.classList.remove('on'));
        noneBtn.classList.add('on');

        sendBridge('workshop.setShader', { fileName: '' })
          .then(() => {
            if (typeof window.showToast === 'function') {
              window.showToast('Шейдеры отключены', 'info');
            }
          })
          .catch(err => {
            if (typeof window.showToast === 'function') {
              window.showToast(`Ошибка отключения шейдеров: ${err.message}`, 'error');
            }
            paneShaders.querySelectorAll('.pick').forEach(b => b.classList.remove('on'));
            if (prevActiveBtn) prevActiveBtn.classList.add('on');
          });
      });
      paneShaders.appendChild(noneBtn);
    }

    function renderMods(mods) {
      if (!paneMods) return;
      paneMods.innerHTML = '';

      if (modsTabBtn) {
        modsTabBtn.textContent = `Моды · ${mods.length}`;
      }

      mods.forEach(mod => {
        const row = document.createElement('div');
        row.className = 'row';

        const nameSpan = document.createElement('span');
        nameSpan.textContent = mod.name || mod.fileName;

        if (mod.version) {
          const hintSpan = document.createElement('span');
          hintSpan.className = 'hint';
          hintSpan.textContent = `v${mod.version}`;
          nameSpan.appendChild(hintSpan);
        }

        const toggle = document.createElement('span');
        toggle.className = `toggle ${mod.enabled ? 'on' : ''}`;
        toggle.dataset.mod = mod.fileName;

        toggle.addEventListener('click', (e) => {
          e.stopPropagation();
          const nextState = !toggle.classList.contains('on');
          toggle.classList.toggle('on', nextState);

          sendBridge('workshop.toggleMod', { fileName: mod.fileName, enabled: nextState })
            .then(res => {
              if (res) {
                mod.enabled = res.enabled;
                toggle.classList.toggle('on', res.enabled);
                if (typeof window.showToast === 'function') {
                  window.showToast(`${mod.name}: ${res.enabled ? 'включён' : 'отключён'}`, 'info');
                }
              }
            })
            .catch(err => {
              toggle.classList.toggle('on', !nextState);
              if (typeof window.showToast === 'function') {
                window.showToast(`Ошибка переключения мода: ${err.message}`, 'error');
              }
            });
        });

        row.appendChild(nameSpan);
        row.appendChild(toggle);
        paneMods.appendChild(row);
      });
    }

    function loadWorkshopList() {
      return sendBridge('workshop.list')
        .then(data => {
          if (!data) return;
          hasLoadedList = true;
          if (Array.isArray(data.shaders)) {
            renderShaders(data.shaders);
          }
          if (Array.isArray(data.mods)) {
            renderMods(data.mods);
          }
        })
        .catch(err => {
          console.error('[Workshop] failed to load list:', err);
        });
    }

    // Load on open
    const secTrigger = document.querySelector('[data-open="workshop"]');
    if (secTrigger) {
      secTrigger.addEventListener('click', () => {
        if (!hasLoadedList) {
          loadWorkshopList();
        }
      });
    }

    // Or if workshop is already open at boot
    const workshopSec = document.querySelector('[data-l="workshop"]');
    if (workshopSec && workshopSec.classList.contains('active')) {
      loadWorkshopList();
    } else {
      // Also load initially so DOM elements exist for checks/smoke
      loadWorkshopList();
    }

    // 3. Action buttons
    if (syncBtn) {
      let isSyncing = false;
      const originalText = syncBtn.textContent;

      if (window.AuraBridge && typeof window.AuraBridge.on === 'function') {
        window.AuraBridge.on('pack.progress', (p) => {
          if (isSyncing && p) {
            syncBtn.textContent = `${p.percentage || 0}%`;
          }
        });
      }

      syncBtn.addEventListener('click', () => {
        if (isSyncing) return;
        isSyncing = true;
        syncBtn.disabled = true;
        syncBtn.textContent = 'Проверка...';

        sendBridge('pack.sync')
          .then(res => {
            if (typeof window.showToast === 'function') {
              const msg = res?.message || 'Синхронизация завершена';
              window.showToast(msg, 'success');
            }
            return loadWorkshopList();
          })
          .catch(err => {
            if (typeof window.showToast === 'function') {
              window.showToast(`Синхронизация отклонена: ${err.message}`, 'error');
            }
          })
          .finally(() => {
            isSyncing = false;
            syncBtn.disabled = false;
            syncBtn.textContent = originalText;
          });
      });
    }

    if (openFolderBtn) {
      openFolderBtn.addEventListener('click', () => {
        sendBridge('fs.openModsFolder')
          .catch(err => {
            if (typeof window.showToast === 'function') {
              window.showToast(`Не удалось открыть папку: ${err.message}`, 'error');
            }
          });
      });
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initWorkshop);
  } else {
    initWorkshop();
  }
})();

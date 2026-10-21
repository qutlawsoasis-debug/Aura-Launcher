/**
 * Aura Launcher v3 — Play / Launch Logic
 * Integrates with WebBridge for real C# launch / progress / status events.
 */

(function () {
  'use strict';

  let isRunning = false;
  let activePlayBtn = null;

  function getBridge() {
    return window.AuraBridge || null;
  }

  function setupBridgeListeners() {
    const bridge = getBridge();
    if (!bridge) {
      setTimeout(setupBridgeListeners, 50);
      return;
    }

    bridge.on('game.progress', (data) => {
      if (!activePlayBtn) {
        activePlayBtn = document.querySelector('.play[data-busy="1"]') || document.querySelector('.bar .play');
      }
      if (!activePlayBtn) return;

      const fill = activePlayBtn.querySelector('.fill');
      const label = activePlayBtn.querySelector('span');
      activePlayBtn.style.minWidth = '240px';

      const pct = Math.max(0, Math.min(100, data.percentage || 0));
      if (fill) fill.style.transform = `scaleX(${pct / 100})`;
      if (label) {
        const stageText = data.stage || (pct < 50 ? 'Проверка файлов' : 'Запуск');
        label.textContent = `${stageText} · ${Math.round(pct)}%`;
      }
    });

    bridge.on('game.status', (status) => {
      if (status.isRunning && !status.isLaunching) {
        isRunning = true;
        resetPlayButtonUi();
        if (typeof window.setIslandMode === 'function') {
          window.setIslandMode('running');
        }
        if (typeof window.showToast === 'function') {
          window.showToast('Minecraft запущен (Fabric 1.20.1)', 'success');
        }
      } else if (!status.isRunning) {
        isRunning = false;
        resetPlayButtonUi();
        if (typeof window.setIslandMode === 'function') {
          window.setIslandMode('idle');
        }
        if (status.error && typeof window.showToast === 'function') {
          window.showToast(`Ошибка запуска: ${status.error}`, 'error');
        } else if (typeof window.showToast === 'function') {
          window.showToast('Сессия игры завершена', 'info');
        }
      }
    });
  }

  function resetPlayButtonUi() {
    if (activePlayBtn) {
      delete activePlayBtn.dataset.busy;
      activePlayBtn.style.minWidth = '';
      const fill = activePlayBtn.querySelector('.fill');
      const label = activePlayBtn.querySelector('span');
      if (fill) fill.style.transform = 'scaleX(0)';
      if (label) label.innerHTML = '<span class="tri"></span>Играть';
      activePlayBtn = null;
    }
  }

  window.startGame = function (btn) {
    if (btn.dataset.busy) return;
    btn.dataset.busy = '1';
    activePlayBtn = btn;

    const label = btn.querySelector('span');
    btn.style.minWidth = '240px';
    if (label) label.textContent = 'Подготовка...';

    const bridge = getBridge();
    if (bridge && typeof bridge.send === 'function') {
      bridge.send('game.launch').catch(err => {
        resetPlayButtonUi();
        if (typeof window.showToast === 'function') {
          window.showToast(`Сбой запуска: ${err.message}`, 'error');
        }
      });
    } else {
      simulateLocalLaunch(btn);
    }
  };

  function simulateLocalLaunch(btn) {
    const fill = btn.querySelector('.fill');
    const label = btn.querySelector('span');
    let t = 0;
    const effects = document.documentElement.dataset.effects;

    const step = () => {
      t += effects === 'off' ? 1 : 0.02;
      if (fill) fill.style.transform = `scaleX(${Math.min(t, 1)})`;
      if (label) {
        label.textContent = t < 0.45 
          ? 'Проверка файлов · ' + Math.round(t * 100) + '%' 
          : 'Запуск · ' + Math.round(Math.min(t, 1) * 100) + '%';
      }

      if (t < 1) {
        requestAnimationFrame(step);
      } else {
        resetPlayButtonUi();
        isRunning = true;
        if (typeof window.setIslandMode === 'function') {
          window.setIslandMode('running');
        }
        if (typeof window.showToast === 'function') {
          window.showToast('Minecraft 1.20.1 запущен (Fabric 0.19.5)', 'success');
        }
      }
    };
    requestAnimationFrame(step);
  }

  window.stopGame = function () {
    const bridge = getBridge();
    if (bridge && typeof bridge.send === 'function') {
      bridge.send('game.stop').catch(() => {});
    }
    isRunning = false;
    resetPlayButtonUi();
    if (typeof window.setIslandMode === 'function') {
      window.setIslandMode('idle');
    }
    if (typeof window.showToast === 'function') {
      window.showToast('Сессия игры завершена', 'info');
    }
  };

  window.isGameRunning = function () {
    return isRunning;
  };

  setupBridgeListeners();
})();

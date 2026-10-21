/**
 * Aura Launcher v3 — Play / Launch Logic
 * Unified game state machine: 'idle' | 'launching' | 'running' | 'failed'
 * Integrates with WebBridge for real C# launch / progress / status events.
 */

(function () {
  'use strict';

  // Global Unified Game State: 'idle' | 'launching' | 'running'
  let gameState = 'idle';

  function getBridge() {
    return window.AuraBridge || null;
  }

  // Update all Play buttons on page consistently
  function updateAllPlayButtons(progressData) {
    const playButtons = document.querySelectorAll('.play');

    playButtons.forEach(btn => {
      // Don't modify the in-game stop button in .running layer
      if (btn.hasAttribute('data-stop')) return;

      const fill = btn.querySelector('.fill');
      const label = btn.querySelector('span');

      if (gameState === 'idle') {
        btn.removeAttribute('data-busy');
        btn.classList.remove('is-launching');
        if (fill) fill.style.transform = 'scaleX(0)';
        if (label) label.innerHTML = '<span class="tri"></span>Играть';
      } else if (gameState === 'launching') {
        btn.setAttribute('data-busy', '1');
        btn.classList.add('is-launching');

        if (progressData) {
          const pct = Math.max(0, Math.min(100, progressData.percentage || 0));
          if (fill) fill.style.transform = `scaleX(${pct / 100})`;
          if (label) {
            const stageText = progressData.stage || (pct < 50 ? 'Проверка файлов' : 'Запуск');
            label.textContent = `${stageText} · ${Math.round(pct)}%`;
          }
        } else {
          if (label) label.textContent = 'Подготовка...';
        }
      } else if (gameState === 'running') {
        btn.removeAttribute('data-busy');
        btn.classList.remove('is-launching');
        if (fill) fill.style.transform = 'scaleX(0)';
      }
    });
  }

  function setGameState(newState, payload = {}) {
    if (gameState === newState && !payload.force) return;
    gameState = newState;

    if (newState === 'launching') {
      updateAllPlayButtons(payload.progress);
      if (typeof window.setIslandMode === 'function') {
        window.setIslandMode('launching', false);
      }
    } else if (newState === 'running') {
      updateAllPlayButtons();
      if (typeof window.setIslandMode === 'function') {
        window.setIslandMode('running', false);
      }
      if (payload.notify && typeof window.showToast === 'function') {
        window.showToast('Minecraft запущен (Fabric 1.20.1)', 'success');
      }
    } else if (newState === 'idle') {
      updateAllPlayButtons();
      if (typeof window.setIslandMode === 'function') {
        window.setIslandMode('idle', false);
      }
      if (payload.error && typeof window.showToast === 'function') {
        window.showToast(`Ошибка запуска: ${payload.error}`, 'error');
      } else if (payload.notify && typeof window.showToast === 'function') {
        window.showToast('Сессия игры завершена', 'info');
      }
    }
  }

  function setupBridgeListeners() {
    const bridge = getBridge();
    if (!bridge) {
      setTimeout(setupBridgeListeners, 50);
      return;
    }

    bridge.on('game.progress', (data) => {
      if (gameState === 'launching') {
        updateAllPlayButtons(data);
      }
    });

    bridge.on('game.status', (status) => {
      const state = (status.state || (status.isRunning ? 'running' : 'idle')).toLowerCase();

      if (state === 'running' || status.isRunning) {
        setGameState('running', { notify: true });
      } else if (state === 'preparing' || state === 'launching' || status.isLaunching) {
        setGameState('launching');
      } else if (state === 'failed') {
        setGameState('idle', { error: status.error || 'Сбой запуска' });
      } else if (state === 'idle') {
        setGameState('idle', { notify: gameState === 'running' });
      }
    });
  }

  window.startGame = function (btn) {
    // Guard against repeated calls during launching or running
    if (gameState === 'launching' || gameState === 'running') {
      return;
    }

    setGameState('launching');

    const bridge = getBridge();
    if (bridge && typeof bridge.send === 'function') {
      bridge.send('game.launch')
        .then(res => {
          // Accepted by launcher backend
        })
        .catch(err => {
          setGameState('idle', { error: err.message });
        });
    }
  };

  window.stopGame = function () {
    const bridge = getBridge();
    if (bridge && typeof bridge.send === 'function') {
      bridge.send('game.stop').catch(() => {});
    }
    setGameState('idle', { notify: true });
  };

  window.isGameRunning = function () {
    return gameState === 'running';
  };

  window.isGameLaunching = function () {
    return gameState === 'launching';
  };

  window.getGameState = function () {
    return gameState;
  };

  setupBridgeListeners();
})();

/**
 * Aura Launcher v3 — Play / Launch Logic
 * Handles game launch progress and running state in Island buttons.
 */

(function () {
  'use strict';

  let isRunning = false;

  window.startGame = function (btn) {
    if (btn.dataset.busy) return;
    btn.dataset.busy = '1';
    
    const fill = btn.querySelector('.fill');
    const label = btn.querySelector('span');
    btn.style.minWidth = '240px';
    let t = 0;

    const effects = document.documentElement.dataset.effects;

    const step = () => {
      t += effects === 'off' ? 1 : 0.012;
      if (fill) fill.style.transform = `scaleX(${Math.min(t, 1)})`;
      if (label) {
        label.textContent = t < 0.45 
          ? 'Проверка файлов · ' + Math.round(t * 100) + '%' 
          : 'Запуск · ' + Math.round(Math.min(t, 1) * 100) + '%';
      }

      if (t < 1) {
        requestAnimationFrame(step);
      } else {
        delete btn.dataset.busy;
        btn.style.minWidth = '';
        if (fill) fill.style.transform = 'scaleX(0)';
        if (label) label.innerHTML = '<span class="tri"></span>Играть';
        
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
  };

  window.stopGame = function () {
    isRunning = false;
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
})();

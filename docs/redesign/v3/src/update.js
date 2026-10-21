/**
 * Aura Launcher v3 — уведомление «Вышло обновление лаунчера» и установка по кнопке.
 */

(function () {
  'use strict';

  function initUpdate() {
    const pill = document.getElementById('updatePill');
    const text = document.getElementById('updateText');
    const applyBtn = document.getElementById('btnApplyUpdate');
    const dismissBtn = document.getElementById('btnDismissUpdate');
    const bar = document.getElementById('updateBar');
    if (!pill || !window.AuraBridge) return;

    let dismissedVersion = null;
    let current = null;

    function toast(message, type) {
      if (typeof window.showToast === 'function') window.showToast(message, type || 'info');
    }

    function show(info) {
      current = info && info.available ? info : null;
      const visible = Boolean(current) && current.version !== dismissedVersion;
      if (visible) text.textContent = `Вышло обновление ${current.display || current.version}`;
      pill.classList.toggle('show', visible);
      document.body.classList.toggle('has-update', visible);
    }

    applyBtn.addEventListener('click', async () => {
      pill.classList.add('busy');
      bar.style.transform = 'scaleX(0)';
      text.textContent = 'Скачиваем обновление…';
      try {
        const res = await window.AuraBridge.send('launcher.applyUpdate');
        if (res && res.status === 'restarting') {
          bar.style.transform = 'scaleX(1)';
          text.textContent = 'Перезапускаем лаунчер…';
        }
      } catch (err) {
        pill.classList.remove('busy');
        show(current);
        toast((err && err.message) || 'Не удалось обновить', 'error');
      }
    });

    dismissBtn.addEventListener('click', () => {
      if (current) dismissedVersion = current.version;
      show(current);
    });

    window.AuraBridge.on('launcher.update', show);
    window.AuraBridge.on('launcher.updateProgress', p => {
      const pct = Math.max(0, Math.min(100, Number(p && p.percentage) || 0));
      bar.style.transform = `scaleX(${pct / 100})`;
      text.textContent = `Скачиваем обновление · ${Math.round(pct)}%`;
    });

    window.AuraBridge.send('launcher.updateGet').then(show).catch(() => {});
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initUpdate);
  } else {
    initUpdate();
  }
})();

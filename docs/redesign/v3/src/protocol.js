/**
 * Aura Launcher v3 — ссылки aura://join/КОД и aura://friend/КОД.
 * Мост разбирает ссылку, здесь — подтверждение и действие (вход в лобби / заявка в друзья).
 */

(function () {
  'use strict';

  function initProtocol() {
    if (!window.AuraBridge) return;
    const send = (n, p) => window.AuraBridge.send(n, p || {});
    const toast = (m, t) => typeof window.showToast === 'function' && window.showToast(m, t || 'info');

    // Пока идёт экран загрузки — ждём, чтобы окно не спряталось под ним
    const afterBoot = () => new Promise(resolve => {
      const check = () => (document.documentElement.classList.contains('booting') ? setTimeout(check, 200) : resolve());
      check();
    });

    async function prompt(p) {
      if (!p || !p.code) return;
      await afterBoot();
      const join = p.action === 'join';
      const sheet = document.createElement('div');
      sheet.className = 'ach-sheet';
      sheet.innerHTML = `<div class="ach-panel" style="width:min(460px,100%);text-align:center">
        <div class="boot-mark" style="margin:4px auto 14px"><span class="ring"></span><span class="core">A</span></div>
        <h2 style="margin:0;font-size:28px"></h2>
        <div class="small dim" style="margin:8px 0 20px"></div>
        <div style="display:flex;gap:10px;justify-content:center">
          <button type="button" class="btn primary" data-ok></button>
          <button type="button" class="btn" data-cancel>Отмена</button>
        </div>
      </div>`;
      sheet.querySelector('h2').textContent = join ? `Войти в лобби ${p.code}?` : `Добавить в друзья ${p.code}?`;
      sheet.querySelector('.small').textContent = join ? 'Подключиться к игре хоста по ссылке' : 'Отправить заявку в друзья по коду';
      const ok = sheet.querySelector('[data-ok]');
      ok.textContent = join ? 'Войти' : 'Отправить';

      const close = () => { sheet.classList.remove('show'); setTimeout(() => sheet.remove(), 320); };
      sheet.querySelector('[data-cancel]').addEventListener('click', close);
      sheet.addEventListener('click', e => { if (e.target === sheet) close(); });
      ok.addEventListener('click', async () => {
        ok.disabled = true;
        try {
          if (join) {
            await send('lobby.join', { code: p.code });
            if (typeof window.setIslandMode === 'function') window.setIslandMode('lobby');
            toast(`Вы в комнате ${p.code}`, 'success');
          } else {
            const res = await send('friends.add', { code: p.code });
            if (res && res.ok === false) throw new Error(res.error || 'Не удалось отправить заявку');
            if (typeof window.setIslandMode === 'function') window.setIslandMode('friends');
            toast('Заявка в друзья отправлена', 'success');
          }
          close();
        } catch (err) {
          ok.disabled = false;
          toast((err && err.message) || 'Ошибка', 'error');
        }
      });

      document.body.appendChild(sheet);
      requestAnimationFrame(() => sheet.classList.add('show'));
    }

    window.AuraBridge.on('protocol.prompt', prompt);
    send('protocol.pending').then(r => r && r.pending && prompt(r.pending)).catch(() => {});
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initProtocol);
  } else {
    initProtocol();
  }
})();

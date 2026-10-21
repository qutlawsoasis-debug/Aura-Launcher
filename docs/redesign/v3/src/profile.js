/**
 * Aura Launcher v3 — профиль игрока: ник в капсуле и поле «Ник» в Гардеробе.
 */

(function () {
  'use strict';

  const NICK_RE = /^[A-Za-z0-9_]{3,16}$/;
  const RULE = 'Ник: от 3 до 16 символов, латиница, цифры и _';

  function initProfile() {
    const input = document.getElementById('inputPlayerNickname');
    let current = '';

    function toast(message, type) {
      if (typeof window.showToast === 'function') window.showToast(message, type || 'info');
    }

    function send(name, payload) {
      if (window.AuraBridge && typeof window.AuraBridge.send === 'function') {
        return window.AuraBridge.send(name, payload || {});
      }
      return Promise.reject(new Error('bridge_unavailable'));
    }

    // Новый ник в капсуле «всплывает» снизу
    function showNick(nick) {
      current = nick || '';
      const text = current || 'Игрок';
      document.querySelectorAll('[data-nick]').forEach(el => {
        if (el.textContent === text) return;
        el.textContent = text;
        if (document.documentElement.dataset.effects !== 'off' && el.animate) {
          el.animate([{ opacity: 0, transform: 'translateY(8px)' }, { opacity: 1, transform: 'none' }],
            { duration: 320, easing: 'cubic-bezier(0.34, 1.3, 0.5, 1)' });
        }
      });
      if (input && document.activeElement !== input) input.value = current;
    }

    async function save() {
      if (!input) return;
      const nick = input.value.trim();
      if (nick === current) {
        input.removeAttribute('aria-invalid');
        return;
      }
      if (!NICK_RE.test(nick)) {
        input.setAttribute('aria-invalid', 'true');
        toast(RULE, 'error');
        return;
      }
      input.disabled = true;
      try {
        const res = await send('nickname.set', { nickname: nick });
        input.removeAttribute('aria-invalid');
        showNick(res && res.nickname ? res.nickname : nick);
        toast(`Теперь вы ${nick}`, 'success');
      } catch (err) {
        input.setAttribute('aria-invalid', 'true');
        input.value = current;
        toast((err && err.message) || 'Не удалось сменить ник', 'error');
      } finally {
        input.disabled = false;
      }
    }

    if (input) {
      input.addEventListener('input', () => input.removeAttribute('aria-invalid'));
      input.addEventListener('keydown', e => {
        if (e.key === 'Enter') { e.preventDefault(); input.blur(); }
        if (e.key === 'Escape') { input.value = current; input.blur(); }
      });
      input.addEventListener('blur', save);
    }

    window.AuraProfile = { getNick: () => current };

    send('config.get').then(cfg => showNick(cfg && cfg.nickname)).catch(() => {});
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initProfile);
  } else {
    initProfile();
  }
})();

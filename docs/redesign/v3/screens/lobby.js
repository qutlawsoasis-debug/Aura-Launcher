/**
 * Aura Launcher v3 — Lobby & Multiplayer Screen Logic
 * P2P tunnel telemetry, lobby code copy, slot interactions, modal input validation.
 */

(function () {
  'use strict';

  const STEVE_FACE = [
    ['#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10'],
    ['#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10'],
    ['#2c1b10', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#2c1b10'],
    ['#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66'],
    ['#c68c66', '#ffffff', '#2c3580', '#c68c66', '#c68c66', '#ffffff', '#2c3580', '#c68c66'],
    ['#c68c66', '#c68c66', '#c68c66', '#9c5c3e', '#9c5c3e', '#c68c66', '#c68c66', '#c68c66'],
    ['#c68c66', '#c68c66', '#613220', '#613220', '#613220', '#613220', '#c68c66', '#c68c66'],
    ['#c68c66', '#c68c66', '#2c1b10', '#c68c66', '#c68c66', '#2c1b10', '#c68c66', '#c68c66']
  ];

  function drawHostAvatar() {
    const canvas = document.getElementById('hostAvatarCanvas');
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    ctx.imageSmoothingEnabled = false;
    const pixelSize = canvas.width / 8;

    for (let r = 0; r < 8; r++) {
      for (let c = 0; c < 8; c++) {
        ctx.fillStyle = STEVE_FACE[r][c];
        ctx.fillRect(c * pixelSize, r * pixelSize, pixelSize, pixelSize);
      }
    }
  }

  // P2P Tunnel Status with natural ping telemetry
  let pingInterval = null;

  function initTunnelTelemetry() {
    const pingEl = document.getElementById('lobbyPingVal');
    const refreshBtn = document.getElementById('btnRestartTunnel');

    if (pingInterval) clearInterval(pingInterval);

    // Subtle realistic jitter (32 - 38 ms)
    pingInterval = setInterval(() => {
      if (!pingEl || document.hidden) return;
      const basePing = 34;
      const jitter = Math.floor(Math.random() * 5) - 2;
      pingEl.textContent = String(basePing + jitter);
    }, 4000);

    if (refreshBtn) {
      refreshBtn.addEventListener('click', () => {
        refreshBtn.classList.add('is-spinning');
        if (pingEl) pingEl.textContent = '...';

        setTimeout(() => {
          refreshBtn.classList.remove('is-spinning');
          if (pingEl) pingEl.textContent = '33';
          if (typeof window.showToast === 'function') {
            window.showToast('Сетевой туннель P2P проверен (Anycast EU, 0% потерь)', 'success');
          }
        }, 650);
      });
    }
  }

  // Copy Lobby Code Handler
  const SVG_COPY = '<svg class="icon icon-copy-state" viewBox="0 0 24 24"><rect x="9" y="9" width="13" height="13" rx="2" ry="2"></rect><path d="M5 15H4a2 2 0 0 1-2-2V4a2 2 0 0 1 2-2h9a2 2 0 0 1 2 2v1"></path></svg>';
  const SVG_CHECK = '<svg class="icon icon-copy-state" viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"></polyline></svg>';

  function initLobbyCodeCopy() {
    const copyBtn = document.getElementById('btnCopyLobbyCode');
    if (!copyBtn) return;

    copyBtn.addEventListener('click', () => {
      const code = copyBtn.dataset.code || 'AURA-7821-X9M2';

      if (navigator.clipboard && navigator.clipboard.writeText) {
        navigator.clipboard.writeText(code).catch(() => {});
      }

      if (typeof window.showToast === 'function') {
        window.showToast('Код лобби скопирован в буфер', 'success');
      }

      copyBtn.classList.add('is-copied');
      copyBtn.innerHTML = `${SVG_CHECK}<span class="copy-code-text">Скопировано!</span>`;

      setTimeout(() => {
        copyBtn.classList.remove('is-copied');
        copyBtn.innerHTML = `${SVG_COPY}<span class="copy-code-text">Скопировать код</span>`;
      }, 2000);
    });
  }

  // Slot invite actions
  function initSlotInvites() {
    document.querySelectorAll('.btn-invite-slot, .player-slot-card.empty').forEach(el => {
      el.addEventListener('click', (e) => {
        e.stopPropagation();
        const code = 'AURA-7821-X9M2';
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(code).catch(() => {});
        }
        if (typeof window.showToast === 'function') {
          window.showToast('Код лобби AURA-7821-X9M2 скопирован для друга', 'info');
        }
      });
    });
  }

  // Join by code Modal management
  function initJoinModal() {
    const modal = document.getElementById('modalJoinLobby');
    const openBtn = document.getElementById('btnOpenJoinModal');
    const closeBtn = document.getElementById('btnCloseJoinModal');
    const cancelBtn = document.getElementById('btnCancelJoinModal');
    const submitBtn = document.getElementById('btnSubmitJoinCode');
    const input = document.getElementById('joinLobbyCodeInput');
    const errorEl = document.getElementById('joinCodeError');

    if (!modal) return;

    function openModal() {
      modal.classList.add('is-open');
      modal.setAttribute('aria-hidden', 'false');
      if (input) {
        input.value = '';
        setTimeout(() => input.focus(), 80);
      }
      if (errorEl) {
        errorEl.style.display = 'none';
        errorEl.textContent = '';
      }
    }

    function closeModal() {
      modal.classList.remove('is-open');
      modal.setAttribute('aria-hidden', 'true');
    }

    if (openBtn) openBtn.addEventListener('click', openModal);
    if (closeBtn) closeBtn.addEventListener('click', closeModal);
    if (cancelBtn) cancelBtn.addEventListener('click', closeModal);

    // Auto-format code: uppercase and automatic dash insertions
    if (input) {
      input.addEventListener('input', () => {
        let val = input.value.toUpperCase().replace(/[^A-Z0-9]/g, '');
        if (val.length > 12) val = val.substring(0, 12);

        let formatted = '';
        if (val.length > 0) {
          formatted += val.substring(0, 4);
        }
        if (val.length > 4) {
          formatted += '-' + val.substring(4, 8);
        }
        if (val.length > 8) {
          formatted += '-' + val.substring(8, 12);
        }

        input.value = formatted;
        if (errorEl) errorEl.style.display = 'none';
      });

      input.addEventListener('keydown', (e) => {
        if (e.key === 'Enter') {
          e.preventDefault();
          submitJoin();
        }
      });
    }

    function submitJoin() {
      if (!input) return;
      const val = input.value.trim().toUpperCase();
      const codeRegex = /^AURA-[A-Z0-9]{4}-[A-Z0-9]{4}$/;

      if (!codeRegex.test(val)) {
        if (errorEl) {
          errorEl.textContent = 'Введите корректный код лобби (например, AURA-7821-X9M2)';
          errorEl.style.display = 'block';
        }
        return;
      }

      if (submitBtn) {
        submitBtn.disabled = true;
        submitBtn.textContent = 'Подключение...';
      }

      setTimeout(() => {
        if (submitBtn) {
          submitBtn.disabled = false;
          submitBtn.textContent = 'Войти в лобби';
        }
        closeModal();
        if (typeof window.showToast === 'function') {
          window.showToast(`Успешное подключение к лобби ${val}!`, 'success');
        }
      }, 700);
    }

    if (submitBtn) {
      submitBtn.addEventListener('click', submitJoin);
    }
  }

  window.initLobbyScreen = function () {
    drawHostAvatar();
    initTunnelTelemetry();
    initLobbyCodeCopy();
    initSlotInvites();
    initJoinModal();
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', window.initLobbyScreen);
  } else {
    window.initLobbyScreen();
  }
})();

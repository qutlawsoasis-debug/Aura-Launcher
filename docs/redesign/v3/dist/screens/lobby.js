/**
 * Aura Launcher v3 — Lobby Section Controller (Island Concept)
 * Handles lobby code copying, joining by friend code, and slot invitations.
 */

(function () {
  'use strict';

  function initLobby() {
    const copyBtn = document.getElementById('btnCopyLobbyCode');
    const codeDisplay = document.getElementById('lobbyRoomCodeDisplay');
    const inputJoin = document.getElementById('inputJoinCode');
    const submitJoin = document.getElementById('btnSubmitJoinCode');

    if (copyBtn && codeDisplay) {
      copyBtn.addEventListener('click', () => {
        const text = codeDisplay.textContent.trim();
        if (navigator.clipboard) {
          navigator.clipboard.writeText(text).catch(() => {});
        }
        codeDisplay.classList.add('flash');
        setTimeout(() => codeDisplay.classList.remove('flash'), 300);
        if (typeof window.showToast === 'function') {
          window.showToast('Код комнаты скопирован', 'success');
        }
      });
    }

    // 2. Interactive invite slots
    document.querySelectorAll('#lobbySlotsContainer .slot').forEach(slot => {
      slot.addEventListener('click', () => {
        if (!slot.classList.contains('full')) {
          slot.classList.remove('slot-invited');
          void slot.offsetWidth; // trigger reflow
          slot.classList.add('slot-invited');
          if (typeof window.showToast === 'function') {
            window.showToast('Приглашение отправлено другу', 'info');
          }
        }
      });
    });

    if (submitJoin && inputJoin) {
      submitJoin.addEventListener('click', () => {
        const code = inputJoin.value.trim().toUpperCase();
        if (!code) {
          if (typeof window.showToast === 'function') {
            window.showToast('Введите код комнаты', 'error');
          }
          return;
        }

        if (typeof window.showToast === 'function') {
          window.showToast(`Подключение к ${code}...`, 'info');
        }
        setTimeout(() => {
          if (typeof window.showToast === 'function') {
            window.showToast('Успешно подключено к комнате!', 'success');
          }
          inputJoin.value = '';
          const revealBox = document.getElementById('joinBox');
          if (revealBox) revealBox.classList.remove('open');
        }, 1000);
      });
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initLobby);
  } else {
    initLobby();
  }
})();

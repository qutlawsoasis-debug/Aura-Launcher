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

    // Свободный слот только открывает «Друзья» (data-open); само приглашение — кнопкой «Позвать» у друга.
    document.querySelectorAll('#lobbySlotsContainer .slot:not(.full)').forEach(slot => {
      slot.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Выберите друга и нажмите «Позвать»', 'info');
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

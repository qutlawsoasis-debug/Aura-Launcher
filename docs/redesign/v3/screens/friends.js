/**
 * Aura Launcher v3 — Friends Section Controller (Island Concept)
 * Handles tab filtering (All / In Game / Online), friend requests and code copying.
 */

(function () {
  'use strict';

  function initFriends() {
    const list = document.getElementById('friendList');
    const segButtons = document.querySelectorAll('#friendsSegmented button');
    const inputFriend = document.getElementById('inputFriendCode');
    const submitAdd = document.getElementById('btnSubmitAddFriend');
    const myCodeCopy = document.getElementById('btnCopyMyFriendCode');

    // 1. Filter Friends
    segButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        const tab = btn.dataset.tab;
        if (!list) return;

        const rows = list.querySelectorAll('.person');
        rows.forEach(r => {
          const st = r.dataset.st;
          if (tab === 'all') {
            r.hidden = false;
          } else if (tab === 'game') {
            r.hidden = st !== 'game';
          } else if (tab === 'online') {
            r.hidden = st !== 'game' && st !== 'online';
          }
        });
      });
    });

    // 2. Accept / Reject Requests
    document.querySelectorAll('.btn-accept-req').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const person = e.target.closest('.person');
        const name = person.querySelector('.name')?.textContent || 'Друг';
        person.remove();
        updateRequestsCount();
        if (typeof window.showToast === 'function') {
          window.showToast(`${name} добавлен в друзья`, 'success');
        }
      });
    });

    document.querySelectorAll('.btn-reject-req').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const person = e.target.closest('.person');
        person.remove();
        updateRequestsCount();
        if (typeof window.showToast === 'function') {
          window.showToast('Заявка отклонена', 'info');
        }
      });
    });

    function updateRequestsCount() {
      const container = document.getElementById('friendRequestsList');
      const title = document.getElementById('requestsTitle');
      if (container && title) {
        const count = container.querySelectorAll('.person').length;
        title.textContent = `Заявки · ${count}`;
        if (count === 0) {
          title.style.display = 'none';
        }
      }
    }

    // 3. Add Friend by Code
    if (submitAdd && inputFriend) {
      inputFriend.addEventListener('input', () => {
        inputFriend.removeAttribute('aria-invalid');
      });

      submitAdd.addEventListener('click', () => {
        const code = inputFriend.value.trim().toUpperCase();
        if (!code || !/^AURA-[A-Z0-9]{4}-[A-Z0-9]{4}$/i.test(code)) {
          inputFriend.setAttribute('aria-invalid', 'true');
          if (typeof window.showToast === 'function') {
            window.showToast(code ? 'Неверный формат кода друга (AURA-XXXX-XXXX)' : 'Введите код друга', 'error');
          }
          return;
        }

        inputFriend.removeAttribute('aria-invalid');
        if (typeof window.showToast === 'function') {
          window.showToast(`Заявка отправлена: ${code}`, 'success');
        }
        inputFriend.value = '';
        const addBox = document.getElementById('addBox');
        if (addBox) addBox.classList.remove('open');
      });
    }

    // 4. Invite button
    document.querySelectorAll('.btn-invite-friend').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const person = e.target.closest('.person');
        const name = person ? person.querySelector('.name')?.textContent : '';
        if (typeof window.showToast === 'function') {
          window.showToast(`Приглашение в лобби будет доступно в лаунчере (${name})`, 'info');
        }
      });
    });

    // 5. Copy my friend code
    if (myCodeCopy) {
      myCodeCopy.addEventListener('click', () => {
        const display = document.getElementById('myFriendCodeDisplay');
        const text = display ? display.textContent.trim() : 'AURA-7821-X9M2';
        if (navigator.clipboard) {
          navigator.clipboard.writeText(text).catch(() => {});
        }
        if (typeof window.showToast === 'function') {
          window.showToast('Ваш код скопирован', 'success');
        }
      });
    }
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initFriends);
  } else {
    initFriends();
  }
})();

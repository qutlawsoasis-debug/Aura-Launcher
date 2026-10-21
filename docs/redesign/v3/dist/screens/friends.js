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

    // 1. Filter Friends with smooth stagger transition
    segButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        const tab = btn.dataset.tab;
        if (!list) return;

        const rows = Array.from(list.querySelectorAll('.person'));
        const followers = list.parentElement
          ? Array.from(list.parentElement.children).filter(el => el !== list && list.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING)
          : [];
        window.animateListChange(rows, () => {
          rows.forEach(r => {
            const st = r.dataset.st;
            r.hidden = !(tab === 'all' || (tab === 'game' && st === 'game') || (tab === 'online' && (st === 'game' || st === 'online')));
          });
        }, followers);
      });
    });

    // 2. Accept / Reject Requests with collapse animation
    document.querySelectorAll('.btn-accept-req').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const person = e.target.closest('.person');
        if (!person) return;
        const name = person.querySelector('.name')?.textContent || 'Друг';
        person.classList.add('is-dismissing');
        setTimeout(() => {
          person.remove();
          updateRequestsCount();
          if (typeof window.showToast === 'function') {
            window.showToast(`${name} добавлен в друзья`, 'success');
          }
        }, 200);
      });
    });

    document.querySelectorAll('.btn-reject-req').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const person = e.target.closest('.person');
        if (!person) return;
        const name = person.querySelector('.name')?.textContent || 'Заявка';
        person.classList.add('is-dismissing');
        setTimeout(() => {
          person.remove();
          updateRequestsCount();
          if (typeof window.showToast === 'function') {
            window.showToast(`${name} отклонена`, 'info');
          }
        }, 200);
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

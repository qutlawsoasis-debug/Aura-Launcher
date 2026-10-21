/**
 * Aura Launcher v3 — Friends Section Controller (Island Concept)
 * Handles live friends list, friend requests, invite toasts and code copying via WebBridge.
 */

(function () {
  'use strict';

  function initFriends() {
    const list = document.getElementById('friendList');
    const requestsList = document.getElementById('friendRequestsList');
    const requestsTitle = document.getElementById('requestsTitle');
    const segButtons = document.querySelectorAll('#friendsSegmented button');
    const inputFriend = document.getElementById('inputFriendCode');
    const submitAdd = document.getElementById('btnSubmitAddFriend');
    const myCodeCopy = document.getElementById('btnCopyMyFriendCode');
    const myCodeDisplay = document.getElementById('myFriendCodeDisplay');
    const friendsLayer = document.querySelector('.layer[data-l="friends"]');

    let currentTab = 'all';
    let incomingRequests = [];
    let currentFriends = [];

    function sendBridge(name, payload) {
      if (typeof window.AuraBridge !== 'undefined' && typeof window.AuraBridge.send === 'function') {
        return window.AuraBridge.send(name, payload);
      }
      return Promise.resolve(null);
    }

    function escapeHtml(str) {
      if (!str) return '';
      return String(str)
        .replace(/&/g, '&amp;')
        .replace(/</g, '&lt;')
        .replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;')
        .replace(/'/g, '&#39;');
    }

    // 1. Filter Friends with smooth stagger transition
    segButtons.forEach(btn => {
      btn.addEventListener('click', () => {
        currentTab = btn.dataset.tab || 'all';
        applyFilter();
      });
    });

    function applyFilter() {
      if (!list) return;
      const rows = Array.from(list.querySelectorAll('.person'));
      const followers = list.parentElement
        ? Array.from(list.parentElement.children).filter(el => el !== list && list.compareDocumentPosition(el) & Node.DOCUMENT_POSITION_FOLLOWING)
        : [];

      if (typeof window.animateListChange === 'function') {
        window.animateListChange(rows, () => {
          rows.forEach(r => {
            const st = r.dataset.st;
            r.hidden = !(currentTab === 'all' || (currentTab === 'game' && st === 'game') || (currentTab === 'online' && (st === 'game' || st === 'online')));
          });
        }, followers);
      } else {
        rows.forEach(r => {
          const st = r.dataset.st;
          r.hidden = !(currentTab === 'all' || (currentTab === 'game' && st === 'game') || (currentTab === 'online' && (st === 'game' || st === 'online')));
        });
      }
    }

    function renderRequests() {
      if (!requestsList || !requestsTitle) return;
      requestsList.innerHTML = '';

      const count = incomingRequests.length;
      requestsTitle.textContent = `Заявки · ${count}`;
      requestsTitle.style.display = count > 0 ? '' : 'none';

      incomingRequests.forEach(req => {
        const item = document.createElement('div');
        item.className = 'person';
        item.dataset.requestId = req.id;
        item.innerHTML = `
          <div class="face"></div>
          <div style="flex:1">
            <div class="name">${escapeHtml(req.nick)}</div>
            <div class="small dim">хочет добавить вас</div>
          </div>
          <button type="button" class="btn sm primary btn-accept-req">Принять</button>
          <button type="button" class="btn sm ghost btn-reject-req" aria-label="Отклонить">✕</button>
        `;

        const acceptBtn = item.querySelector('.btn-accept-req');
        const rejectBtn = item.querySelector('.btn-reject-req');

        acceptBtn.addEventListener('click', () => handleRespond(req.id, req.nick, true, item));
        rejectBtn.addEventListener('click', () => handleRespond(req.id, req.nick, false, item));

        requestsList.appendChild(item);
      });
    }

    async function handleRespond(id, nick, accept, rowElement) {
      if (rowElement) {
        rowElement.classList.add('is-dismissing');
      }

      try {
        const res = await sendBridge('friends.respond', { fromId: id, accept: accept });
        incomingRequests = incomingRequests.filter(r => r.id !== id);
        setTimeout(() => {
          if (rowElement && rowElement.parentElement) {
            rowElement.remove();
          }
          renderRequests();
          if (typeof window.showToast === 'function') {
            if (accept) {
              window.showToast(`${nick} добавлен в друзья`, 'success');
            } else {
              window.showToast(`Заявка от ${nick} отклонена`, 'info');
            }
          }
        }, 200);
      } catch (err) {
        if (rowElement) {
          rowElement.classList.remove('is-dismissing');
        }
        if (typeof window.showToast === 'function') {
          window.showToast(`Ошибка: ${err.message || err}`, 'error');
        }
      }
    }

    function renderFriends(friends) {
      if (!list) return;
      currentFriends = friends || [];
      list.innerHTML = '';

      if (currentFriends.length === 0) {
        const empty = document.createElement('div');
        empty.className = 'small dim';
        empty.style.padding = '12px 4px';
        empty.textContent = 'Список друзей пуст';
        list.appendChild(empty);
        return;
      }

      currentFriends.forEach(f => {
        const item = document.createElement('div');
        item.className = 'person';

        let stCode = 'off';
        let statusBadge = '<span class="status off"></span>';
        let descText = 'Не в сети';

        if (f.online) {
          if (f.status === 'playing' || f.status === 'game') {
            stCode = 'game';
            statusBadge = '<span class="status"></span>';
            descText = f.lobbyCode ? `В игре · ${escapeHtml(f.lobbyCode)}` : 'В игре';
          } else if (f.status === 'lobby') {
            stCode = 'game';
            statusBadge = '<span class="status"></span>';
            descText = f.lobbyCode ? `В лобби · ${escapeHtml(f.lobbyCode)}` : 'В лобби';
          } else {
            stCode = 'online';
            statusBadge = '<span class="status idle"></span>';
            descText = 'В сети';
          }
        }

        item.dataset.st = stCode;
        if (!f.online) {
          item.style.opacity = '0.55';
        }

        let inviteBtnHtml = '';
        if (f.online && stCode !== 'game') {
          inviteBtnHtml = '<button type="button" class="btn sm btn-invite-friend" data-cmd="friends.invite">Позвать</button>';
        }

        item.innerHTML = `
          <div class="face"></div>
          <div style="flex:1">
            <div class="name">${escapeHtml(f.nick)}</div>
            <div class="small dim">${descText}</div>
          </div>
          ${inviteBtnHtml}
          ${statusBadge}
        `;

        const inviteBtn = item.querySelector('.btn-invite-friend');
        if (inviteBtn) {
          inviteBtn.addEventListener('click', () => {
            if (typeof window.showToast === 'function') {
              window.showToast(`Приглашение в лобби будет доступно в лаунчере (${f.nick})`, 'info');
            }
          });
        }

        list.appendChild(item);
      });

      applyFilter();
    }

    async function loadFriendsData() {
      try {
        const data = await sendBridge('friends.get');
        if (data) {
          if (myCodeDisplay) {
            myCodeDisplay.textContent = data.myCode || '—';
          }
          if (Array.isArray(data.friends)) {
            renderFriends(data.friends);
          }
        }
      } catch (err) {
        console.error('[Friends] Error loading data:', err);
      }
    }

    // 3. Add Friend by Code
    if (submitAdd && inputFriend) {
      inputFriend.addEventListener('input', () => {
        inputFriend.removeAttribute('aria-invalid');
      });

      submitAdd.addEventListener('click', async () => {
        const code = inputFriend.value.trim().toUpperCase();
        if (!code) {
          inputFriend.setAttribute('aria-invalid', 'true');
          if (typeof window.showToast === 'function') {
            window.showToast('Введите код друга', 'error');
          }
          return;
        }

        try {
          const res = await sendBridge('friends.add', { code: code });
          if (res && res.ok) {
            inputFriend.removeAttribute('aria-invalid');
            inputFriend.value = '';
            const addBox = document.getElementById('addBox');
            if (addBox) addBox.classList.remove('open');
            if (typeof window.showToast === 'function') {
              window.showToast(`Заявка отправлена: ${code}`, 'success');
            }
          } else {
            const errMsg = (res && res.error) ? res.error : 'Не удалось отправить заявку';
            inputFriend.setAttribute('aria-invalid', 'true');
            if (typeof window.showToast === 'function') {
              window.showToast(`Ошибка: ${errMsg}`, 'error');
            }
          }
        } catch (err) {
          inputFriend.setAttribute('aria-invalid', 'true');
          if (typeof window.showToast === 'function') {
            window.showToast(`Ошибка: ${err.message || err}`, 'error');
          }
        }
      });
    }

    // 4. Copy my friend code
    if (myCodeCopy) {
      myCodeCopy.addEventListener('click', () => {
        const text = myCodeDisplay ? myCodeDisplay.textContent.trim() : '';
        if (text && text !== '—') {
          if (navigator.clipboard) {
            navigator.clipboard.writeText(text).catch(() => {});
          }
          if (typeof window.showToast === 'function') {
            window.showToast('Ваш код скопирован', 'success');
          }
        } else {
          if (typeof window.showToast === 'function') {
            window.showToast('Код друга ещё не получен', 'info');
          }
        }
      });
    }

    // 5. Track section visibility for friends.setTabActive
    let isTabCurrentlyActive = false;

    function handleTabActiveChange(isActive) {
      if (isTabCurrentlyActive === isActive) return;
      isTabCurrentlyActive = isActive;
      sendBridge('friends.setTabActive', { active: isActive });
      if (isActive) {
        loadFriendsData();
      }
    }

    if (friendsLayer) {
      const observer = new MutationObserver(() => {
        const isActive = friendsLayer.classList.contains('on') || friendsLayer.classList.contains('active') || friendsLayer.classList.contains('is-current');
        handleTabActiveChange(isActive);
      });
      observer.observe(friendsLayer, { attributes: true, attributeFilter: ['class'] });

      // Check initial state
      const initialActive = friendsLayer.classList.contains('on') || friendsLayer.classList.contains('active') || friendsLayer.classList.contains('is-current');
      if (initialActive) {
        handleTabActiveChange(true);
      }
    }

    // 6. Push events from bridge
    if (typeof window.AuraBridge !== 'undefined' && typeof window.AuraBridge.on === 'function') {
      window.AuraBridge.on('friends.presence', (data) => {
        if (data && Array.isArray(data.friends)) {
          renderFriends(data.friends);
        }
      });

      window.AuraBridge.on('friends.requestReceived', (data) => {
        if (data && data.id) {
          if (!incomingRequests.some(r => r.id === data.id)) {
            incomingRequests.push(data);
            renderRequests();
          }
          if (typeof window.showToast === 'function') {
            window.showToast(`Новая заявка в друзья от ${data.nick || 'игрока'}`, 'info');
          }
        }
      });

      window.AuraBridge.on('friends.inviteReceived', (data) => {
        if (data) {
          const fromNick = data.fromNick || 'Друг';
          if (typeof window.showToast === 'function') {
            window.showToast(`${fromNick} приглашает вас в лобби`, 'info');
          }
        }
      });
    }

    // Initial fetch in case already active or standalone
    loadFriendsData();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initFriends);
  } else {
    initFriends();
  }
})();

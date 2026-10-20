/**
 * Aura Launcher v3 — Friends Screen Logic
 * Real-time search, status filtering, avatar pixel rendering, friend invite & request handling.
 */

(function () {
  'use strict';

  // Pixel face palettes for 8x8 avatars
  const AVATAR_PALETTES = {
    steve: [
      ['#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10'],
      ['#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10'],
      ['#2c1b10', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#2c1b10'],
      ['#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66'],
      ['#c68c66', '#ffffff', '#2c3580', '#c68c66', '#c68c66', '#ffffff', '#2c3580', '#c68c66'],
      ['#c68c66', '#c68c66', '#c68c66', '#9c5c3e', '#9c5c3e', '#c68c66', '#c68c66', '#c68c66'],
      ['#c68c66', '#c68c66', '#613220', '#613220', '#613220', '#613220', '#c68c66', '#c68c66'],
      ['#c68c66', '#c68c66', '#2c1b10', '#c68c66', '#c68c66', '#2c1b10', '#c68c66', '#c68c66']
    ],
    alex: [
      ['#a65825', '#a65825', '#a65825', '#a65825', '#a65825', '#a65825', '#a65825', '#a65825'],
      ['#a65825', '#a65825', '#a65825', '#a65825', '#a65825', '#a65825', '#a65825', '#a65825'],
      ['#a65825', '#d99d75', '#d99d75', '#d99d75', '#d99d75', '#d99d75', '#a65825', '#a65825'],
      ['#a65825', '#d99d75', '#d99d75', '#d99d75', '#d99d75', '#d99d75', '#a65825', '#a65825'],
      ['#d99d75', '#ffffff', '#1b6339', '#d99d75', '#d99d75', '#ffffff', '#1b6339', '#d99d75'],
      ['#d99d75', '#d99d75', '#d99d75', '#b36d4f', '#b36d4f', '#d99d75', '#d99d75', '#d99d75'],
      ['#d99d75', '#d99d75', '#c78263', '#a65825', '#a65825', '#c78263', '#d99d75', '#d99d75'],
      ['#a65825', '#d99d75', '#d99d75', '#d99d75', '#d99d75', '#d99d75', '#d99d75', '#a65825']
    ],
    notch: [
      ['#1c120c', '#1c120c', '#1c120c', '#1c120c', '#1c120c', '#1c120c', '#1c120c', '#1c120c'],
      ['#1c120c', '#1c120c', '#1c120c', '#1c120c', '#1c120c', '#1c120c', '#1c120c', '#1c120c'],
      ['#1c120c', '#cfa17e', '#cfa17e', '#cfa17e', '#cfa17e', '#cfa17e', '#cfa17e', '#1c120c'],
      ['#cfa17e', '#cfa17e', '#cfa17e', '#cfa17e', '#cfa17e', '#cfa17e', '#cfa17e', '#cfa17e'],
      ['#cfa17e', '#ffffff', '#1f2538', '#cfa17e', '#cfa17e', '#ffffff', '#1f2538', '#cfa17e'],
      ['#3b2214', '#3b2214', '#3b2214', '#a16543', '#a16543', '#3b2214', '#3b2214', '#3b2214'],
      ['#3b2214', '#3b2214', '#3b2214', '#3b2214', '#3b2214', '#3b2214', '#3b2214', '#3b2214'],
      ['#3b2214', '#3b2214', '#3b2214', '#3b2214', '#3b2214', '#3b2214', '#3b2214', '#3b2214']
    ],
    hunter: [
      ['#1a2b3c', '#1a2b3c', '#1a2b3c', '#1a2b3c', '#1a2b3c', '#1a2b3c', '#1a2b3c', '#1a2b3c'],
      ['#1a2b3c', '#33587a', '#33587a', '#33587a', '#33587a', '#33587a', '#33587a', '#1a2b3c'],
      ['#1a2b3c', '#e0b592', '#e0b592', '#e0b592', '#e0b592', '#e0b592', '#e0b592', '#1a2b3c'],
      ['#e0b592', '#e0b592', '#e0b592', '#e0b592', '#e0b592', '#e0b592', '#e0b592', '#e0b592'],
      ['#e0b592', '#ffffff', '#0094c6', '#e0b592', '#e0b592', '#ffffff', '#0094c6', '#e0b592'],
      ['#e0b592', '#e0b592', '#e0b592', '#b87c56', '#b87c56', '#e0b592', '#e0b592', '#e0b592'],
      ['#e0b592', '#e0b592', '#593924', '#593924', '#593924', '#593924', '#e0b592', '#e0b592'],
      ['#1a2b3c', '#e0b592', '#e0b592', '#e0b592', '#e0b592', '#e0b592', '#e0b592', '#1a2b3c']
    ],
    knight: [
      ['#191b22', '#2d323f', '#2d323f', '#2d323f', '#2d323f', '#2d323f', '#2d323f', '#191b22'],
      ['#2d323f', '#444c5f', '#444c5f', '#444c5f', '#444c5f', '#444c5f', '#444c5f', '#2d323f'],
      ['#2d323f', '#444c5f', '#c9a082', '#c9a082', '#c9a082', '#c9a082', '#444c5f', '#2d323f'],
      ['#2d323f', '#c9a082', '#c9a082', '#c9a082', '#c9a082', '#c9a082', '#c9a082', '#2d323f'],
      ['#444c5f', '#c9a082', '#ffffff', '#8a2be2', '#8a2be2', '#ffffff', '#c9a082', '#444c5f'],
      ['#444c5f', '#c9a082', '#c9a082', '#a67253', '#a67253', '#c9a082', '#c9a082', '#444c5f'],
      ['#2d323f', '#444c5f', '#444c5f', '#444c5f', '#444c5f', '#444c5f', '#444c5f', '#2d323f'],
      ['#191b22', '#2d323f', '#2d323f', '#2d323f', '#2d323f', '#2d323f', '#2d323f', '#191b22']
    ]
  };

  function drawPixelFace(canvas, paletteKey) {
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    ctx.imageSmoothingEnabled = false;
    const palette = AVATAR_PALETTES[paletteKey] || AVATAR_PALETTES.steve;
    const pixelSize = canvas.width / 8;

    for (let r = 0; r < 8; r++) {
      for (let c = 0; c < 8; c++) {
        ctx.fillStyle = palette[r][c];
        ctx.fillRect(c * pixelSize, r * pixelSize, pixelSize, pixelSize);
      }
    }
  }

  // Friends Database
  let friendsList = [
    {
      id: 'AURA-5491-MC88',
      nick: 'Steve_Miner',
      avatar: 'steve',
      status: 'in-game',
      statusText: 'В игре: Minecraft 1.20.1 · Aura',
      modpack: 'Aura Pack v1.2',
      ping: '28 ms',
      playtime: '184 ч',
      client: 'Fabric 1.20.1',
      invited: false
    },
    {
      id: 'AURA-3104-ALEX',
      nick: 'Alex_Crafter',
      avatar: 'alex',
      status: 'in-game',
      statusText: 'В игре: Minecraft 1.20.1 · Aura',
      modpack: 'Aura Pack v1.2',
      ping: '34 ms',
      playtime: '92 ч',
      client: 'Fabric 1.20.1',
      invited: false
    },
    {
      id: 'AURA-8820-NOTCH',
      nick: 'Notch_Fan',
      avatar: 'notch',
      status: 'online',
      statusText: 'В сети',
      modpack: 'Aura Pack v1.2',
      ping: '19 ms',
      playtime: '310 ч',
      client: 'Fabric 1.20.1',
      invited: false
    },
    {
      id: 'AURA-7731-MINE',
      nick: 'DiamondHunter',
      avatar: 'hunter',
      status: 'offline',
      statusText: 'Не в сети • был 2 ч назад',
      modpack: 'Aura Pack v1.2',
      ping: '—',
      playtime: '45 ч',
      client: 'Fabric 1.20.1',
      invited: false
    },
    {
      id: 'AURA-9014-DKNT',
      nick: 'DarkKnight',
      avatar: 'knight',
      status: 'offline',
      statusText: 'Не в сети • вчера',
      modpack: 'Aura Pack v1.2',
      ping: '—',
      playtime: '12 ч',
      client: 'Fabric 1.20.1',
      invited: false
    }
  ];

  let currentFilter = 'all';
  let searchQuery = '';
  let selectedFriend = null;

  // Render Friend Cards
  function renderFriends() {
    const container = document.getElementById('friendsCardsContainer');
    const emptyState = document.getElementById('friendsEmptyState');
    if (!container) return;

    container.innerHTML = '';

    const filtered = friendsList.filter(friend => {
      const matchesFilter =
        currentFilter === 'all' ||
        (currentFilter === 'in-game' && friend.status === 'in-game') ||
        (currentFilter === 'online' && friend.status === 'online') ||
        (currentFilter === 'offline' && friend.status === 'offline');

      const matchesSearch =
        searchQuery === '' ||
        friend.nick.toLowerCase().includes(searchQuery.toLowerCase());

      return matchesFilter && matchesSearch;
    });

    // Update Counters
    const countAll = friendsList.length;
    const countInGame = friendsList.filter(f => f.status === 'in-game').length;
    const countOnline = friendsList.filter(f => f.status === 'online').length;
    const countOffline = friendsList.filter(f => f.status === 'offline').length;

    const elAll = document.getElementById('counterAll');
    const elInGame = document.getElementById('counterInGame');
    const elOnline = document.getElementById('counterOnline');
    const elOffline = document.getElementById('counterOffline');

    if (elAll) elAll.textContent = String(countAll);
    if (elInGame) elInGame.textContent = String(countInGame);
    if (elOnline) elOnline.textContent = String(countOnline);
    if (elOffline) elOffline.textContent = String(countOffline);

    if (filtered.length === 0) {
      if (emptyState) emptyState.style.display = 'flex';
      return;
    } else {
      if (emptyState) emptyState.style.display = 'none';
    }

    filtered.forEach(friend => {
      const card = document.createElement('div');
      card.className = 'friend-card';
      card.dataset.id = friend.id;

      let inviteBtnHtml = '';
      if (friend.status === 'offline') {
        inviteBtnHtml = `
          <button type="button" class="btn-action-invite" disabled title="Игрок не в сети">
            <svg class="icon" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>
            <span>Пригласить</span>
          </button>
        `;
      } else if (friend.invited) {
        inviteBtnHtml = `
          <button type="button" class="btn-action-invite is-invited" disabled>
            <svg class="icon" viewBox="0 0 24 24"><polyline points="20 6 9 17 4 12"></polyline></svg>
            <span>Приглашён</span>
          </button>
        `;
      } else {
        inviteBtnHtml = `
          <button type="button" class="btn-action-invite btn-invite-trigger" data-id="${friend.id}">
            <svg class="icon" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>
            <span>Пригласить в лобби</span>
          </button>
        `;
      }

      card.innerHTML = `
        <div class="friend-card-left">
          <div class="friend-avatar-wrap">
            <canvas class="friend-avatar-canvas" id="canvas-${friend.id}" width="44" height="44" aria-label="Скин ${friend.nick}"></canvas>
          </div>
          <div class="friend-info">
            <div class="friend-nick-row">
              <span class="friend-nickname">${friend.nick}</span>
              <span class="friend-badge-mod">${friend.modpack}</span>
            </div>
            <div class="friend-status-row">
              <span class="status-dot ${friend.status}"></span>
              <span class="friend-status-text ${friend.status}">${friend.statusText}</span>
            </div>
          </div>
        </div>

        <div class="friend-card-actions">
          ${inviteBtnHtml}
          <button type="button" class="btn-action-profile btn-profile-trigger" data-id="${friend.id}">
            <svg class="icon" viewBox="0 0 24 24"><path d="M20 21v-2a4 4 0 0 0-4-4H8a4 4 0 0 0-4 4v2"></path><circle cx="12" cy="7" r="4"></circle></svg>
            <span>Профиль</span>
          </button>
        </div>
      `;

      container.appendChild(card);

      // Render Avatar
      const cvs = document.getElementById(`canvas-${friend.id}`);
      if (cvs) {
        drawPixelFace(cvs, friend.avatar);
      }
    });

    // Bind dynamic card buttons
    container.querySelectorAll('.btn-invite-trigger').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        const id = btn.dataset.id;
        handleInviteFriend(id);
      });
    });

    container.querySelectorAll('.btn-profile-trigger').forEach(btn => {
      btn.addEventListener('click', (e) => {
        e.stopPropagation();
        const id = btn.dataset.id;
        openFriendProfileModal(id);
      });
    });
  }

  // Invite Friend Action
  function handleInviteFriend(friendId) {
    const friend = friendsList.find(f => f.id === friendId);
    if (!friend) return;

    friend.invited = true;
    renderFriends();

    if (typeof window.showToast === 'function') {
      window.showToast(`Приглашение в лобби отправлено игроку ${friend.nick}`, 'success');
    }
  }

  // Open Friend Profile Modal
  function openFriendProfileModal(friendId) {
    const friend = friendsList.find(f => f.id === friendId);
    if (!friend) return;

    selectedFriend = friend;

    const modalNick = document.getElementById('modalFriendNick');
    const modalStatus = document.getElementById('modalFriendStatusText');
    const modalId = document.getElementById('modalFriendId');
    const modalPing = document.getElementById('modalFriendPing');
    const modalPlaytime = document.getElementById('modalFriendPlaytime');
    const modalClient = document.getElementById('modalFriendClient');
    const modalStatusDot = document.getElementById('modalFriendStatusDot');
    const modalCanvas = document.getElementById('modalFriendAvatarCanvas');
    const inviteBtn = document.getElementById('btnModalInviteLobby');

    if (modalNick) modalNick.textContent = friend.nick;
    if (modalStatus) modalStatus.textContent = friend.statusText;
    if (modalId) modalId.textContent = `ID профиля: ${friend.id}`;
    if (modalPing) modalPing.textContent = friend.ping;
    if (modalPlaytime) modalPlaytime.textContent = friend.playtime;
    if (modalClient) modalClient.textContent = friend.client;

    if (modalStatusDot) {
      modalStatusDot.className = `modal-status-badge ${friend.status}`;
    }

    if (modalCanvas) {
      drawPixelFace(modalCanvas, friend.avatar);
    }

    if (inviteBtn) {
      if (friend.status === 'offline') {
        inviteBtn.disabled = true;
        inviteBtn.style.opacity = 'var(--disabled-opacity)';
      } else {
        inviteBtn.disabled = false;
        inviteBtn.style.opacity = '1';
      }
    }

    if (typeof window.openModal === 'function') {
      window.openModal('modalFriendProfile');
    }
  }

  // Initialize Search & Filter Controls
  function initSearchAndFilters() {
    const searchInput = document.getElementById('friendsSearchInput');
    const clearBtn = document.getElementById('friendsSearchClear');
    const resetBtn = document.getElementById('btnResetSearch');

    if (searchInput) {
      searchInput.addEventListener('input', () => {
        searchQuery = searchInput.value.trim();
        if (clearBtn) {
          clearBtn.classList.toggle('is-visible', searchQuery.length > 0);
        }
        renderFriends();
      });
    }

    if (clearBtn && searchInput) {
      clearBtn.addEventListener('click', () => {
        searchInput.value = '';
        searchQuery = '';
        clearBtn.classList.remove('is-visible');
        renderFriends();
        searchInput.focus();
      });
    }

    if (resetBtn && searchInput) {
      resetBtn.addEventListener('click', () => {
        searchInput.value = '';
        searchQuery = '';
        currentFilter = 'all';
        if (clearBtn) clearBtn.classList.remove('is-visible');
        document.querySelectorAll('.filter-pill').forEach(pill => {
          const isAll = pill.dataset.filter === 'all';
          pill.classList.toggle('active', isAll);
          pill.setAttribute('aria-selected', isAll ? 'true' : 'false');
        });
        renderFriends();
      });
    }

    // Filter Pills
    document.querySelectorAll('.filter-pill').forEach(pill => {
      pill.addEventListener('click', () => {
        const filter = pill.dataset.filter;
        currentFilter = filter;

        document.querySelectorAll('.filter-pill').forEach(p => {
          const isActive = p === pill;
          p.classList.toggle('active', isActive);
          p.setAttribute('aria-selected', isActive ? 'true' : 'false');
        });

        renderFriends();
      });
    });
  }

  // Copy Friend Code & Link Handlers
  function initCodeCopy() {
    const copyCodeBtn = document.getElementById('btnCopyFriendCode');
    const copyLinkBtn = document.getElementById('btnCopyFriendLink');

    if (copyCodeBtn) {
      copyCodeBtn.addEventListener('click', () => {
        const code = copyCodeBtn.dataset.code || 'AURA-7821-X9M2';
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(code).catch(() => {});
        }
        copyCodeBtn.classList.add('is-copied');
        if (typeof window.showToast === 'function') {
          window.showToast('Код профиля скопирован в буфер', 'success');
        }
        setTimeout(() => {
          copyCodeBtn.classList.remove('is-copied');
        }, 1800);
      });
    }

    if (copyLinkBtn) {
      copyLinkBtn.addEventListener('click', () => {
        const link = copyLinkBtn.dataset.link || 'aura://invite/AURA-7821-X9M2';
        if (navigator.clipboard && navigator.clipboard.writeText) {
          navigator.clipboard.writeText(link).catch(() => {});
        }
        copyLinkBtn.classList.add('is-copied');
        if (typeof window.showToast === 'function') {
          window.showToast('Ссылка-приглашение скопирована в буфер', 'success');
        }
        setTimeout(() => {
          copyLinkBtn.classList.remove('is-copied');
        }, 1800);
      });
    }
  }

  // Incoming Requests Logic
  function initIncomingRequests() {
    const req1Canvas = document.getElementById('reqAvatar1');
    const req2Canvas = document.getElementById('reqAvatar2');
    if (req1Canvas) drawPixelFace(req1Canvas, 'hunter');
    if (req2Canvas) drawPixelFace(req2Canvas, 'alex');

    const updateBadge = () => {
      const activeCount = document.querySelectorAll('.request-item:not([style*="display: none"])').length;
      const badge = document.getElementById('requestsCountBadge');
      const emptyState = document.getElementById('requestsEmptyState');
      if (badge) badge.textContent = String(activeCount);
      if (emptyState) {
        emptyState.style.display = activeCount === 0 ? 'flex' : 'none';
      }
    };

    document.querySelectorAll('.btn-req-accept').forEach(btn => {
      btn.addEventListener('click', () => {
        const reqItem = btn.closest('.request-item');
        if (!reqItem) return;
        const nick = reqItem.dataset.user || 'Игрок';

        // Add to friends
        friendsList.unshift({
          id: `AURA-${Math.floor(1000 + Math.random() * 9000)}-NEW`,
          nick: nick,
          avatar: nick === 'RedstonePro' ? 'hunter' : 'alex',
          status: 'online',
          statusText: 'В сети',
          modpack: 'Aura Pack v1.2',
          ping: '31 ms',
          playtime: '1 ч',
          client: 'Fabric 1.20.1',
          invited: false
        });

        reqItem.style.display = 'none';
        updateBadge();
        renderFriends();

        if (typeof window.showToast === 'function') {
          window.showToast(`Заявка в друзья от ${nick} принята`, 'success');
        }
      });
    });

    document.querySelectorAll('.btn-req-decline').forEach(btn => {
      btn.addEventListener('click', () => {
        const reqItem = btn.closest('.request-item');
        if (!reqItem) return;
        const nick = reqItem.dataset.user || 'Игрок';

        reqItem.style.display = 'none';
        updateBadge();

        if (typeof window.showToast === 'function') {
          window.showToast(`Заявка от ${nick} отклонена`, 'info');
        }
      });
    });
  }

  // Add Friend Form
  function initAddFriendForm() {
    const form = document.getElementById('addFriendForm');
    const input = document.getElementById('addFriendCodeInput');

    if (form && input) {
      form.addEventListener('submit', (e) => {
        e.preventDefault();
        const code = input.value.trim().toUpperCase();

        if (!code || code.length < 6) {
          if (typeof window.showToast === 'function') {
            window.showToast('Введите корректный код друга (например, AURA-XXXX-XXXX)', 'error');
          }
          return;
        }

        if (code === 'AURA-7821-X9M2') {
          if (typeof window.showToast === 'function') {
            window.showToast('Нельзя добавить свой собственный код', 'error');
          }
          return;
        }

        // Success simulation
        input.value = '';
        if (typeof window.showToast === 'function') {
          window.showToast(`Запрос дружбы отправлен игроку с кодом ${code}`, 'success');
        }
      });
    }
  }

  // Modal actions
  function initModalActions() {
    const closeBtn = document.getElementById('btnCloseFriendProfile');
    const closeBtn2 = document.getElementById('btnCloseFriendProfileBtn');
    const removeBtn = document.getElementById('btnRemoveFriend');
    const inviteBtn = document.getElementById('btnModalInviteLobby');

    const closeModal = () => {
      if (typeof window.closeModal === 'function') {
        window.closeModal('modalFriendProfile');
      }
    };

    if (closeBtn) closeBtn.addEventListener('click', closeModal);
    if (closeBtn2) closeBtn2.addEventListener('click', closeModal);

    if (inviteBtn) {
      inviteBtn.addEventListener('click', () => {
        if (!selectedFriend) return;
        handleInviteFriend(selectedFriend.id);
        closeModal();
      });
    }

    if (removeBtn) {
      removeBtn.addEventListener('click', () => {
        if (!selectedFriend) return;
        const removedNick = selectedFriend.nick;
        friendsList = friendsList.filter(f => f.id !== selectedFriend.id);
        closeModal();
        renderFriends();
        if (typeof window.showToast === 'function') {
          window.showToast(`Игрок ${removedNick} удалён из списка друзей`, 'info');
        }
      });
    }
  }

  // Lifecycle initialization
  function initFriendsScreen() {
    renderFriends();
    initSearchAndFilters();
    initCodeCopy();
    initIncomingRequests();
    initAddFriendForm();
    initModalActions();
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initFriendsScreen);
  } else {
    initFriendsScreen();
  }

})();
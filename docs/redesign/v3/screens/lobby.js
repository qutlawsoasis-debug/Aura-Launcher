/**
 * Aura Launcher v3 — Lobby Section Controller (Island Concept)
 * Live lobby via WebBridge: create / join by code / open world / connect / kick / leave,
 * incoming invites from friends.
 */

(function () {
  'use strict';

  const SLOT_MIN = 4;

  function initLobby() {
    const stage = document.getElementById('lobbyStage');
    const roomView = stage ? stage.querySelector('.lobby-view[data-view="room"]') : null;
    const subStatus = document.getElementById('lobbySubStatus');
    const codeLabel = document.getElementById('lobbyCodeLabel');
    const codeDisplay = document.getElementById('lobbyRoomCodeDisplay');
    const copyBtn = document.getElementById('btnCopyLobbyCode');
    const statusBox = document.getElementById('lobbyStatus');
    const statusText = document.getElementById('lobbyStatusText');
    const slots = document.getElementById('lobbySlotsContainer');
    const countText = document.getElementById('lobbyPlayersCountText');
    const mainBtn = document.getElementById('btnLobbyMain');
    const leaveBtn = document.getElementById('btnLeaveLobby');
    const createBtn = document.getElementById('btnCreateLobby');
    const inputJoin = document.getElementById('inputJoinCode');
    const submitJoin = document.getElementById('btnSubmitJoinCode');
    const inviteCard = document.getElementById('lobbyInviteCard');
    const inviteFrom = document.getElementById('lobbyInviteFrom');
    const acceptInviteBtn = document.getElementById('btnAcceptInvite');
    const declineInviteBtn = document.getElementById('btnDeclineInvite');
    const layer = document.querySelector('.layer[data-l="lobby"]');

    let lobby = { role: 'none', state: 'idle', players: [], max: 8 };
    let shownCode = null;
    let shownPlayers = [];
    let openRequested = false;
    let pendingInvite = null;

    function toast(message, type) {
      if (typeof window.showToast === 'function') window.showToast(message, type || 'info');
    }

    function sendBridge(name, payload) {
      if (window.AuraBridge && typeof window.AuraBridge.send === 'function') {
        return window.AuraBridge.send(name, payload || {});
      }
      return Promise.reject(new Error('bridge_unavailable'));
    }

    function escapeHtml(str) {
      return String(str == null ? '' : str)
        .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
        .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
    }

    function errorText(err) {
      const raw = (err && err.message) || String(err || '');
      const map = {
        already_in_lobby: 'Вы уже в лобби',
        invalid_code: 'Введите код комнаты',
        not_in_lobby: 'Сначала создайте лобби',
        not_host: 'Это может сделать только хост',
        busy: 'Игра уже запускается',
        lobby_service_unavailable: 'Лобби недоступно',
        bridge_unavailable: 'Лобби недоступно'
      };
      return map[raw] || raw || 'Неизвестная ошибка';
    }

    // --- Код комнаты: каждый символ отдельно, чтобы проявлялись каскадом ---
    function renderCode(code) {
      if (!codeDisplay || code === shownCode) return;
      shownCode = code;
      codeDisplay.innerHTML = '';
      String(code || '').split('').forEach((ch, i) => {
        const span = document.createElement('span');
        span.className = ch === '-' ? 'ch sep' : 'ch';
        span.style.setProperty('--c', i);
        span.textContent = ch;
        codeDisplay.appendChild(span);
      });
    }

    // --- Слоты: новые игроки «всплывают», ушедшие — схлопываются ---
    function renderSlots() {
      if (!slots) return;
      const players = lobby.players || [];
      const isHost = lobby.role === 'host';
      const prevNicks = new Set(shownPlayers.map(p => p.nick));
      const nextNicks = new Set(players.map(p => p.nick));

      const leaving = Array.from(slots.querySelectorAll('.slot.full'))
        .filter(el => !nextNicks.has(el.dataset.nick));
      leaving.forEach(el => el.classList.add('leaving'));

      const build = () => {
        slots.innerHTML = '';
        players.forEach((p, idx) => {
          const el = document.createElement('div');
          el.className = 'slot full' + (p.isHost ? ' host' : '');
          el.dataset.nick = p.nick;
          // Анимация входа только у новых игроков
          if (prevNicks.has(p.nick)) el.style.animation = 'none';
          else el.style.setProperty('--i', idx);
          el.innerHTML =
            (p.isHost ? '<span class="crown">♛</span>' : '') +
            '<div class="ring"><div class="face"></div></div>' +
            `<span class="slot-name">${escapeHtml(p.nick)}</span>` +
            (isHost && !p.isHost ? `<button type="button" class="slot-kick" aria-label="Выгнать ${escapeHtml(p.nick)}">✕</button>` : '');
          const kick = el.querySelector('.slot-kick');
          if (kick) kick.addEventListener('click', () => kickPlayer(p.nick, el));
          slots.appendChild(el);
        });

        const free = Math.max(SLOT_MIN, Math.min((lobby.max || 8), players.length + 1)) - players.length;
        for (let i = 0; i < free; i++) {
          const btn = document.createElement('button');
          btn.type = 'button';
          btn.className = 'slot';
          btn.innerHTML = '<div class="ring">+</div>Пригласить';
          btn.addEventListener('click', () => {
            if (lobby.role !== 'host') {
              toast('Приглашать может только хост', 'info');
              return;
            }
            if (typeof window.setIslandMode === 'function') window.setIslandMode('friends');
            toast('Выберите друга и нажмите «Позвать»', 'info');
          });
          slots.appendChild(btn);
        }
        shownPlayers = players.map(p => ({ nick: p.nick }));
      };

      if (leaving.length && document.documentElement.dataset.effects !== 'off') setTimeout(build, 260);
      else build();

      if (countText) countText.textContent = `${players.length} из ${lobby.max || 8} игроков`;
    }

    // --- Статус и главная кнопка в зависимости от состояния ---
    function describe() {
      const s = lobby.state;
      const ping = lobby.pingMs != null ? ` · ${lobby.pingMs} мс` : '';
      if (lobby.role === 'host') {
        if (s === 'error') return { tone: 'err', text: lobby.lastError || 'Ошибка лобби', btn: 'Повторить', enabled: true, action: 'open' };
        if (s === 'active') return { tone: 'ok', text: `Туннель активен${ping} · друзья могут заходить`, btn: 'Мир открыт', enabled: false };
        if (s === 'tunnelStarting') return { tone: 'wait', text: 'Поднимаем туннель…', btn: 'Подключаем…', busy: true };
        if (openRequested || lobby.statusHint) {
          return { tone: 'wait', text: lobby.statusHint || 'Запускаем игру…', btn: 'Ждём мир…', busy: true, waiting: true };
        }
        return { tone: 'wait', text: 'Лобби создано. Откройте мир, чтобы друзья могли зайти', btn: 'Открыть мир', enabled: true, action: 'open', waiting: true };
      }
      if (lobby.role === 'guest') {
        if (s === 'error') return { tone: 'err', text: lobby.lastError || 'Ошибка лобби', btn: 'Подключиться', enabled: false };
        if (s === 'connecting') return { tone: 'wait', text: 'Запускаем игру и подключаемся…', btn: 'Подключаемся…', busy: true };
        if (s === 'readyToPlay') return { tone: 'ok', text: `Мир хоста открыт${ping}`, btn: 'Подключиться', enabled: true, action: 'play' };
        return { tone: 'wait', text: 'Ждём, пока хост откроет мир…', btn: 'Подключиться', enabled: false };
      }
      return null;
    }

    function setStatusText(text) {
      if (!statusText || statusText.textContent === text) return;
      if (document.documentElement.dataset.effects === 'off') {
        statusText.textContent = text;
        return;
      }
      statusText.classList.add('swap');
      setTimeout(() => {
        statusText.textContent = text;
        statusText.classList.remove('swap');
      }, 160);
    }

    function render() {
      const inLobby = lobby.role === 'host' || lobby.role === 'guest';
      if (stage) stage.dataset.view = inLobby ? 'room' : 'idle';

      if (!inLobby) {
        shownCode = null;
        shownPlayers = [];
        openRequested = false;
        if (subStatus) subStatus.textContent = 'Играйте вместе с друзьями';
        return;
      }

      renderCode(lobby.code);
      if (codeLabel) codeLabel.textContent = lobby.role === 'host' ? 'Код комнаты для друзей' : 'Вы в комнате';
      if (copyBtn) copyBtn.hidden = lobby.role !== 'host';
      if (subStatus) {
        subStatus.textContent = lobby.role === 'host'
          ? `Вы хост · ${(lobby.players || []).length} из ${lobby.max || 8}`
          : `Хост: ${lobby.hostName || '—'}`;
      }

      const d = describe();
      if (d) {
        if (statusBox) statusBox.dataset.tone = d.tone;
        setStatusText(d.text);
        if (mainBtn) {
          mainBtn.textContent = d.btn;
          mainBtn.dataset.action = d.action || '';
          mainBtn.disabled = !d.enabled && !d.busy;
          mainBtn.classList.toggle('busy', Boolean(d.busy));
        }
        if (roomView) roomView.dataset.waiting = d.waiting ? '1' : '0';
      }
      renderSlots();
    }

    function apply(snapshot) {
      if (!snapshot) return;
      lobby = Object.assign({ players: [], max: 8 }, snapshot);
      if (lobby.state === 'active' || lobby.state === 'error') openRequested = false;
      render();
    }

    // --- Действия ---
    async function createLobby() {
      if (createBtn.disabled) return;
      createBtn.disabled = true;
      try {
        apply(await sendBridge('lobby.create'));
        toast('Лобби создано', 'success');
      } catch (err) {
        toast(`Не удалось создать лобби: ${errorText(err)}`, 'error');
      } finally {
        createBtn.disabled = false;
      }
    }

    async function joinLobby() {
      const code = inputJoin.value.trim().toUpperCase();
      if (!code) {
        inputJoin.setAttribute('aria-invalid', 'true');
        toast('Введите код комнаты', 'error');
        return;
      }
      submitJoin.disabled = true;
      try {
        apply(await sendBridge('lobby.join', { code }));
        inputJoin.value = '';
        const box = document.getElementById('joinBox');
        if (box) box.classList.remove('open');
        toast('Вы в комнате', 'success');
      } catch (err) {
        inputJoin.setAttribute('aria-invalid', 'true');
        toast(errorText(err), 'error');
      } finally {
        submitJoin.disabled = false;
      }
    }

    async function mainAction() {
      const action = mainBtn.dataset.action;
      if (action === 'open') {
        openRequested = true;
        render();
        try {
          await sendBridge('lobby.openWorld');
        } catch (err) {
          openRequested = false;
          render();
          toast(errorText(err), 'error');
        }
      } else if (action === 'play') {
        try {
          await sendBridge('lobby.play');
        } catch (err) {
          toast(errorText(err), 'error');
        }
      }
    }

    async function leaveLobby() {
      leaveBtn.disabled = true;
      try {
        apply(await sendBridge('lobby.leave'));
      } catch (err) {
        toast(errorText(err), 'error');
      } finally {
        leaveBtn.disabled = false;
      }
    }

    async function kickPlayer(nick, el) {
      try {
        const res = await sendBridge('lobby.kick', { nick });
        if (res && res.ok) {
          el.classList.add('leaving');
          toast(`${nick} исключён`, 'info');
        }
      } catch (err) {
        toast(errorText(err), 'error');
      }
    }

    function showInvite(inv) {
      pendingInvite = inv;
      if (inviteFrom) inviteFrom.textContent = inv.fromNick || 'Друг';
      if (inviteCard) inviteCard.hidden = false;
    }

    async function respondInvite(accept) {
      const inv = pendingInvite;
      if (!inv) return;
      pendingInvite = null;
      if (inviteCard) inviteCard.hidden = true;
      try {
        const res = await sendBridge('friends.respondInvite', { inviteId: inv.inviteId, accept });
        if (accept) {
          if (res && res.joined) {
            apply(await sendBridge('lobby.get'));
            toast(`Вы в комнате ${inv.fromNick || ''}`.trim(), 'success');
          } else {
            toast(errorText(res && res.error ? new Error(res.error) : null), 'error');
          }
        }
      } catch (err) {
        toast(errorText(err), 'error');
      }
    }

    // --- Подписки ---
    if (createBtn) createBtn.addEventListener('click', createLobby);
    const heroCreate = document.getElementById('btnHeroCreate');
    if (heroCreate) heroCreate.addEventListener('click', createLobby);
    if (submitJoin) submitJoin.addEventListener('click', joinLobby);
    if (inputJoin) {
      inputJoin.addEventListener('input', () => inputJoin.removeAttribute('aria-invalid'));
      inputJoin.addEventListener('keydown', e => { if (e.key === 'Enter') joinLobby(); });
    }
    if (mainBtn) mainBtn.addEventListener('click', mainAction);
    if (leaveBtn) leaveBtn.addEventListener('click', leaveLobby);
    if (acceptInviteBtn) acceptInviteBtn.addEventListener('click', () => respondInvite(true));
    if (declineInviteBtn) declineInviteBtn.addEventListener('click', () => respondInvite(false));

    if (copyBtn && codeDisplay) {
      copyBtn.addEventListener('click', () => {
        const text = lobby.code || '';
        if (!text) return;
        if (navigator.clipboard) navigator.clipboard.writeText(text).catch(() => {});
        codeDisplay.classList.add('flash');
        setTimeout(() => codeDisplay.classList.remove('flash'), 300);
        toast('Код комнаты скопирован', 'success');
      });
    }

    if (window.AuraBridge && typeof window.AuraBridge.on === 'function') {
      window.AuraBridge.on('lobby.updated', apply);
      window.AuraBridge.on('lobby.kicked', () => {
        toast('Хост исключил вас из лобби', 'error');
        sendBridge('lobby.get').then(apply).catch(() => {});
      });
      window.AuraBridge.on('lobby.error', data => {
        if (data && data.error) toast(data.error, 'error');
      });
      window.AuraBridge.on('friends.inviteReceived', data => {
        if (data && data.inviteId && lobby.role === 'none') showInvite(data);
      });
    }

    // Обновляем состояние при каждом открытии раздела
    if (layer) {
      new MutationObserver(() => {
        if (layer.classList.contains('on')) sendBridge('lobby.get').then(apply).catch(() => {});
      }).observe(layer, { attributes: true, attributeFilter: ['class'] });
    }

    // Публичный хук для «Позвать» в разделе «Друзья»
    window.AuraLobby = {
      getState: () => lobby,
      invite: friendId => sendBridge('friends.invite', { friendId })
    };

    sendBridge('lobby.get').then(apply).catch(() => render());
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initLobby);
  } else {
    initLobby();
  }
})();

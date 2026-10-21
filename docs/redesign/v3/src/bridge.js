/**
 * Aura Launcher v3 — WebBridge JavaScript Client
 * Handles communication between Web UI and WPF Host via WebView2 WebMessage API.
 * Falls back to mock implementation when running outside WebView2.
 */

(function (root, factory) {
  const mod = factory();
  if (typeof window !== 'undefined') {
    window.AuraBridge = mod;
  }
  if (typeof globalThis !== 'undefined') {
    globalThis.AuraBridge = mod;
  }
  if (typeof module === 'object' && module.exports) {
    module.exports = mod;
  }
})(typeof globalThis !== 'undefined' ? globalThis : window, function () {
  'use strict';

  const isNative = Boolean(
    typeof window !== 'undefined' &&
    window.chrome &&
    window.chrome.webview &&
    typeof window.chrome.webview.postMessage === 'function'
  );

  const pendingRequests = new Map();
  const eventListeners = new Map();

  if (isNative) {
    window.chrome.webview.addEventListener('message', (event) => {
      let msg;
      try {
        msg = typeof event.data === 'string' ? JSON.parse(event.data) : event.data;
      } catch (e) {
        console.error('[Bridge] Failed to parse message:', e);
        return;
      }

      if (!msg || !msg.type) return;

      if (msg.type === 'response') {
        if (msg.id && pendingRequests.has(msg.id)) {
          const { resolve, reject } = pendingRequests.get(msg.id);
          pendingRequests.delete(msg.id);
          if (msg.error) {
            reject(new Error(msg.error));
          } else {
            resolve(msg.payload);
          }
        }
      } else if (msg.type === 'event') {
        const listeners = eventListeners.get(msg.name) || [];
        for (const cb of listeners) {
          try {
            cb(msg.payload);
          } catch (e) {
            console.error(`[Bridge] Listener error for ${msg.name}:`, e);
          }
        }
      }
    });
  }

  function send(name, payload = {}) {
    if (!isNative) {
      return handleMockCommand(name, payload);
    }

    return new Promise((resolve, reject) => {
      const id = 'req_' + Math.random().toString(36).substring(2, 9);
      pendingRequests.set(id, { resolve, reject });

      const message = {
        id,
        type: 'command',
        name,
        payload
      };

      if (window.chrome && window.chrome.webview) {
        window.chrome.webview.postMessage(message);
      }

      const timeoutMs = name === 'game.launch' ? 10000 : 30000;
      setTimeout(() => {
        if (pendingRequests.has(id)) {
          pendingRequests.delete(id);
          reject(new Error(`[Bridge] Timeout waiting for response to ${name}`));
        }
      }, timeoutMs);
    });
  }

  function on(name, callback) {
    if (!eventListeners.has(name)) {
      eventListeners.set(name, new Set());
    }
    eventListeners.get(name).add(callback);

    return () => {
      const set = eventListeners.get(name);
      if (set) {
        set.delete(callback);
      }
    };
  }

  function emit(name, payload) {
    const listeners = eventListeners.get(name) || [];
    for (const cb of listeners) {
      try {
        cb(payload);
      } catch (e) {
        console.error(`[Bridge] Mock emit error for ${name}:`, e);
      }
    }
  }

  let mockGameRunning = false;
  let mockLaunchTimer = null;

  function handleMockCommand(name, payload) {
    if (name === 'game.launch') {
      if (mockGameRunning) {
        return Promise.reject(new Error('already_launching'));
      }

      mockGameRunning = true;
      setTimeout(() => {
        emit('game.status', { state: 'preparing', isRunning: false });
      }, 0);

      let p = 0;
      const effects = document.documentElement.dataset.effects;
      const interval = effects === 'off' ? 10 : 50;

      clearInterval(mockLaunchTimer);
      mockLaunchTimer = setInterval(() => {
        p += effects === 'off' ? 50 : 15;
        if (p < 50) {
          emit('game.progress', {
            percentage: p,
            stage: 'Проверка файлов',
            detail: `${p}%`
          });
        } else if (p < 100) {
          emit('game.status', { state: 'launching', isRunning: false });
          emit('game.progress', {
            percentage: p,
            stage: 'Запуск',
            detail: `${p}%`
          });
        } else {
          clearInterval(mockLaunchTimer);
          emit('game.progress', { percentage: 100, stage: 'Запуск', detail: 'Готово' });
          emit('game.status', { state: 'running', isRunning: true, processId: 1337, exitCode: null });
        }
      }, interval);

      return Promise.resolve({ started: true, processId: 1337 });
    }

    if (name === 'game.stop') {
      clearInterval(mockLaunchTimer);
      mockGameRunning = false;
      emit('game.status', { state: 'idle', isRunning: false, processId: null, exitCode: 0 });
      return Promise.resolve({ stopped: true });
    }

    if (name === 'window.action') {
      return Promise.resolve({ action: payload.action, success: true });
    }

    if (name === 'config.get') {
      let cfg = {
        nickname: 'Player',
        ramMb: 6144,
        hideLauncherWhilePlaying: true,
        graphicsPreset: 'Medium',
        visualEffectsMode: 'full',
        selectedVersion: '1.20.1',
        gameDir: 'C:\\Users\\Player\\AppData\\Roaming\\.aura'
      };
      try {
        const stored = localStorage.getItem('aura_mock_config');
        if (stored) {
          cfg = Object.assign(cfg, JSON.parse(stored));
        }
      } catch (e) {}
      return Promise.resolve(cfg);
    }

    if (name === 'config.set') {
      const allowed = ['ramMb', 'hideLauncherWhilePlaying', 'visualEffectsMode'];
      if (!payload || !allowed.includes(payload.key)) {
        return Promise.reject(new Error('invalid_key'));
      }
      if (payload.key === 'ramMb') {
        const v = Number(payload.value);
        if (!Number.isInteger(v) || v < 2048 || v > 65536) {
          return Promise.reject(new Error('invalid_value'));
        }
      }
      if (payload.key === 'hideLauncherWhilePlaying') {
        if (typeof payload.value !== 'boolean') {
          return Promise.reject(new Error('invalid_value'));
        }
      }
      if (payload.key === 'visualEffectsMode') {
        if (!['full', 'low', 'off'].includes(payload.value)) {
          return Promise.reject(new Error('invalid_value'));
        }
      }
      let cfg = {
        nickname: 'Player',
        ramMb: 6144,
        hideLauncherWhilePlaying: true,
        graphicsPreset: 'Medium',
        visualEffectsMode: 'full'
      };
      try {
        const stored = localStorage.getItem('aura_mock_config');
        if (stored) {
          cfg = Object.assign(cfg, JSON.parse(stored));
        }
        cfg[payload.key] = payload.value;
        localStorage.setItem('aura_mock_config', JSON.stringify(cfg));
      } catch (e) {
        cfg[payload.key] = payload.value;
      }
      return Promise.resolve(cfg);
    }

    if (name === 'preset.apply') {
      const presetKey = payload && payload.presetKey;
      if (!presetKey) {
        return Promise.reject(new Error('invalid_preset'));
      }
      try {
        const stored = localStorage.getItem('aura_mock_config');
        const cfg = stored ? JSON.parse(stored) : {};
        cfg.graphicsPreset = presetKey;
        localStorage.setItem('aura_mock_config', JSON.stringify(cfg));
      } catch (e) {}
      return Promise.resolve({
        Success: true,
        PresetKey: presetKey,
        FinalRamMb: 6144,
        ChosenShader: 'MockShader'
      });
    }

    // Mock workshop data in memory
    const scope = typeof globalThis !== 'undefined' ? globalThis : window;
    if (!scope.__mockWorkshopData) {
      scope.__mockWorkshopData = {
        shaders: [
          { fileName: 'ComplementaryReimagined.zip', name: 'Complementary Reimagined', isActive: true },
          { fileName: 'SildursEnhancedDefault.zip', name: 'Sildurs Enhanced Default', isActive: false },
          { fileName: 'BuildersQoL.zip', name: 'Builders QoL', isActive: false }
        ],
        mods: [
          { fileName: 'sodium-fabric-0.5.8.jar', name: 'Sodium', version: '0.5.8', enabled: true },
          { fileName: 'iris-fabric-1.7.0.jar', name: 'Iris Shaders', version: '1.7.0', enabled: true },
          { fileName: 'distanthorizons-2.1.2.jar', name: 'Distant Horizons', version: '2.1.2', enabled: true },
          { fileName: 'voicechat-fabric-2.5.12.jar', name: 'Simple Voice Chat', version: '2.5.12', enabled: true },
          { fileName: 'ferritecore-6.0.1.jar', name: 'FerriteCore', version: '6.0.1', enabled: false }
        ]
      };
    }

    if (name === 'workshop.list') {
      return Promise.resolve({
        shaders: scope.__mockWorkshopData.shaders.map(s => Object.assign({}, s)),
        mods: scope.__mockWorkshopData.mods.map(m => Object.assign({}, m))
      });
    }

    if (name === 'workshop.setShader') {
      const fn = payload && payload.fileName;
      if (!fn || fn === 'off' || fn === 'none') {
        scope.__mockWorkshopData.shaders.forEach(s => s.isActive = false);
        return Promise.resolve({ fileName: '', active: false });
      }
      const target = scope.__mockWorkshopData.shaders.find(s => s.fileName === fn || s.name === fn);
      if (!target) {
        return Promise.reject(new Error('invalid_shader'));
      }
      scope.__mockWorkshopData.shaders.forEach(s => s.isActive = (s === target));
      return Promise.resolve({ fileName: target.fileName, active: true });
    }

    if (name === 'workshop.toggleMod') {
      const fn = payload && payload.fileName;
      const target = scope.__mockWorkshopData.mods.find(m => m.fileName === fn || m.name === fn);
      if (!target) {
        return Promise.reject(new Error('mod_not_found'));
      }
      target.enabled = Boolean(payload.enabled);
      return Promise.resolve({ fileName: target.fileName, enabled: target.enabled });
    }

    if (name === 'pack.sync') {
      setTimeout(() => {
        emit('pack.progress', { percentage: 50, detail: 'Проверка модов...' });
      }, 50);
      setTimeout(() => {
        emit('pack.progress', { percentage: 100, detail: 'Синхронизация завершена' });
      }, 150);
      return new Promise(resolve => {
        setTimeout(() => {
          resolve({ status: 'updated', message: 'Сборка актуальна', filesChanged: 0 });
        }, 200);
      });
    }

    if (name === 'fs.openModsFolder') {
      return Promise.resolve({ success: true, path: 'mock/mods' });
    }

    if (!scope.__mockSkinData) {
      scope.__mockSkinData = {
        nickname: 'Player',
        model: 'default',
        fileName: 'steve.png',
        dataUrl: null
      };
    }

    if (name === 'skin.get') {
      return Promise.resolve({
        nickname: scope.__mockSkinData.nickname,
        model: scope.__mockSkinData.model,
        fileName: scope.__mockSkinData.fileName,
        dataUrl: scope.__mockSkinData.dataUrl
      });
    }

    if (name === 'skin.upload') {
      const fn = (payload && payload.fileName) || 'custom_skin.png';
      const b64 = payload && payload.base64Data;
      if (!b64) {
        return Promise.reject(new Error('invalid_payload'));
      }
      const dataUrl = b64.startsWith('data:') ? b64 : `data:image/png;base64,${b64}`;
      scope.__mockSkinData.fileName = fn;
      scope.__mockSkinData.dataUrl = dataUrl;
      return Promise.resolve({
        ok: true,
        fileName: fn,
        uploadError: null
      });
    }

    if (name === 'skin.setModel') {
      const model = payload && payload.model;
      if (model !== 'default' && model !== 'slim') {
        return Promise.reject(new Error('invalid_value'));
      }
      scope.__mockSkinData.model = model;
      return Promise.resolve({
        ok: true,
        model: model,
        uploadError: null
      });
    }

    if (!scope.__mockFriendsData) {
      scope.__mockFriendsData = {
        myCode: 'AURA-7821-X9M2',
        registered: true,
        friends: [
          { id: 'f-1', nick: 'zxcursed', online: true, status: 'playing', lobbyCode: 'AURA-LOBBY-1' },
          { id: 'f-2', nick: 'Steve_Miner', online: true, status: 'online', lobbyCode: null },
          { id: 'f-3', nick: 'Lumi', online: false, status: 'online', lobbyCode: null }
        ],
        requests: [
          { id: 'req-1', nick: 'RedstonePro' }
        ]
      };
    }

    if (name === 'friends.get') {
      return Promise.resolve({
        myCode: scope.__mockFriendsData.myCode,
        registered: scope.__mockFriendsData.registered,
        friends: scope.__mockFriendsData.friends
      });
    }

    if (name === 'friends.setTabActive') {
      scope.__mockFriendsTabActive = Boolean(payload && payload.active);
      return Promise.resolve({ ok: true });
    }

    if (name === 'friends.add') {
      const code = payload && payload.code;
      if (!code || code === 'AURA-0000') {
        return Promise.resolve({ ok: false, error: 'Игрок с таким кодом не найден' });
      }
      return Promise.resolve({ ok: true });
    }

    if (name === 'friends.respond') {
      const fromId = payload && payload.fromId;
      const accept = Boolean(payload && payload.accept);
      scope.__mockFriendsData.requests = scope.__mockFriendsData.requests.filter(r => r.id !== fromId);
      return Promise.resolve({ ok: true });
    }

    if (name === 'game.setVersion') {
      const ver = payload && payload.version;
      if (ver !== '1.20.1' && ver !== '1.21.1') {
        return Promise.reject(new Error('invalid_value'));
      }
      let cfg = {
        nickname: 'Player',
        ramMb: 6144,
        hideLauncherWhilePlaying: true,
        graphicsPreset: 'Medium',
        visualEffectsMode: 'full',
        selectedVersion: '1.20.1',
        gameDir: 'C:\\Users\\Player\\AppData\\Roaming\\.aura'
      };
      try {
        const stored = localStorage.getItem('aura_mock_config');
        if (stored) {
          cfg = Object.assign(cfg, JSON.parse(stored));
        }
        cfg.selectedVersion = ver;
        localStorage.setItem('aura_mock_config', JSON.stringify(cfg));
      } catch (e) {
        cfg.selectedVersion = ver;
      }
      return Promise.resolve(cfg);
    }

    if (name === 'fs.pickGameFolder') {
      return Promise.resolve({ gameDir: 'C:\\MockGameFolder\\.aura' });
    }

    if (name === 'logs.export') {
      return Promise.resolve({ path: 'C:\\MockReports\\aura-report.zip' });
    }

    if (name === 'cache.clear') {
      return Promise.resolve({ freedBytes: 15728640 }); // 15 MB
    }

    if (name.startsWith('lobby.') || name === 'friends.invite' || name === 'friends.respondInvite') {
      return handleMockLobby(name, payload || {});
    }

    return Promise.resolve({ success: true, mock: true });
  }

  // --- Лобби (mock): тот же жизненный цикл, что у LobbySessionService ---
  const mockLobby = { role: 'none', state: 'idle', code: null, hostName: null, tunnelAddress: null, pingMs: null, statusHint: null, lastError: null, max: 8, players: [] };
  const mockLobbyTimers = [];

  function lobbySnapshot() {
    return JSON.parse(JSON.stringify(mockLobby));
  }

  function lobbyLater(ms, fn) {
    mockLobbyTimers.push(setTimeout(() => { fn(); emit('lobby.updated', lobbySnapshot()); }, ms));
  }

  function resetMockLobby() {
    mockLobbyTimers.splice(0).forEach(clearTimeout);
    Object.assign(mockLobby, { role: 'none', state: 'idle', code: null, hostName: null, tunnelAddress: null, pingMs: null, statusHint: null, lastError: null, players: [] });
  }

  function handleMockLobby(name, payload) {
    if (name === 'lobby.get') return Promise.resolve(lobbySnapshot());

    if (name === 'lobby.create') {
      if (mockLobby.role !== 'none') return Promise.reject(new Error('already_in_lobby'));
      Object.assign(mockLobby, { role: 'host', state: 'waitingForWorld', code: 'AURA-7821-X9M2', hostName: 'Player', players: [{ nick: 'Player', isHost: true, isReady: true }] });
      return Promise.resolve(lobbySnapshot());
    }

    if (name === 'lobby.openWorld') {
      if (mockLobby.role !== 'host') return Promise.reject(new Error('not_host'));
      lobbyLater(600, () => { mockLobby.statusHint = 'Зайдите в мир и откройте сеть (Esc → Открыть для сети)'; });
      lobbyLater(2600, () => { mockLobby.state = 'tunnelStarting'; mockLobby.statusHint = null; });
      lobbyLater(3800, () => { Object.assign(mockLobby, { state: 'active', tunnelAddress: 'aura-lobby.craft.ply.gg:25565', pingMs: 34 }); });
      lobbyLater(6000, () => { mockLobby.players.push({ nick: 'zxcursed', isHost: false, isReady: true }); });
      return Promise.resolve({ accepted: true });
    }

    if (name === 'lobby.join') {
      const code = String(payload.code || '').trim().toUpperCase();
      if (!code) return Promise.reject(new Error('invalid_code'));
      if (code === 'AURA-0000') return Promise.reject(new Error('Лобби не найдено'));
      Object.assign(mockLobby, { role: 'guest', state: 'joining', code, hostName: 'zxcursed', players: [{ nick: 'zxcursed', isHost: true, isReady: true }, { nick: 'Player', isHost: false, isReady: true }] });
      lobbyLater(1800, () => { Object.assign(mockLobby, { state: 'readyToPlay', tunnelAddress: 'aura-lobby.craft.ply.gg:25565', pingMs: 41 }); });
      return Promise.resolve(lobbySnapshot());
    }

    if (name === 'lobby.play') {
      if (mockLobby.role !== 'guest') return Promise.reject(new Error('not_guest'));
      lobbyLater(0, () => { mockLobby.state = 'connecting'; });
      lobbyLater(2500, () => { mockLobby.state = 'readyToPlay'; });
      return Promise.resolve({ accepted: true });
    }

    if (name === 'lobby.kick') {
      mockLobby.players = mockLobby.players.filter(p => p.nick !== payload.nick);
      setTimeout(() => emit('lobby.updated', lobbySnapshot()), 0);
      return Promise.resolve({ ok: true });
    }

    if (name === 'lobby.leave') {
      resetMockLobby();
      return Promise.resolve(lobbySnapshot());
    }

    if (name === 'friends.invite') {
      if (mockLobby.role !== 'host') return Promise.reject(new Error('not_in_lobby'));
      return Promise.resolve({ ok: true, inviteId: 'inv-mock' });
    }

    if (name === 'friends.respondInvite') {
      if (!payload.accept) return Promise.resolve({ ok: true, joined: false });
      return handleMockLobby('lobby.join', { code: 'AURA-LOBBY-1' }).then(() => ({ ok: true, joined: true, code: 'AURA-LOBBY-1' }));
    }

    return Promise.resolve({ success: true, mock: true });
  }

  return {
    send,
    on,
    isNative
  };
});

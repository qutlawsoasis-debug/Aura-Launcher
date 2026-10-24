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
      const allowed = ['ramMb', 'hideLauncherWhilePlaying', 'visualEffectsMode', 'autoUpdate', 'discordRpc'];
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
        dataUrl: scope.__mockSkinData.dataUrl,
        cape: scope.__mockSkinData.cape || 'cyan'
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

    if (name === 'skin.setCape') {
      const cape = payload && payload.cape;
      if (!['cyan', 'silver', 'none'].includes(cape)) return Promise.reject(new Error('invalid_value'));
      scope.__mockSkinData.cape = cape;
      return Promise.resolve({ cape });
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

    // Достижения: ?ach=1 — через 4 с приходит «получено»
    if (name === 'achievements.get') {
      const defs = [
        ['first_launch', 'Искра', 'Первый запуск игры', 1, 1], ['ten_launches', 'Постоялец', '10 запусков игры', 10, 7],
        ['hundred_hours', 'Сто часов', '100 часов в игре', 100, 23.4], ['marathon', 'Марафон', '5 часов в одной сессии', 5, 2.1],
        ['night_shift', 'Ночная смена', 'Запуск с 03:00 до 05:00', 1, 1], ['first_lobby', 'Хост', 'Создать первое лобби', 1, 1],
        ['full_table', 'Полный стол', '4 игрока в лобби', 4, 2], ['first_friend', 'Свой человек', 'Первый друг', 1, 1],
        ['five_friends', 'Компания', '5 друзей', 5, 3], ['first_backup', 'Страховка', 'Первый бэкап мира', 1, 0],
        ['photographer', 'Фотограф', '10 скриншотов', 10, 6], ['own_style', 'Свой стиль', 'Загрузить свой скин', 1, 1]
      ];
      const items = defs.map(([id, title, description, target, current]) => ({
        id, title, description, target, current, unlocked: current >= target, unlockedAt: current >= target ? '19.10.2026' : null
      }));
      if (typeof location !== 'undefined' && /[?&]ach=1/.test(location.search) && !scope.__achPopped) {
        scope.__achPopped = true;
        setTimeout(() => emit('achievement.unlocked', { id: 'first_backup', title: 'Страховка', description: 'Первый бэкап мира' }), 4000);
      }
      return Promise.resolve({ unlocked: items.filter(i => i.unlocked).length, total: items.length, items });
    }

    if (name.startsWith('cloud.') || name.startsWith('worlds.') || name.startsWith('screenshots.')) {
      return handleMockCloud(name, payload || {});
    }

    // Ссылки aura://: ?link=join-ABC123 | friend-ABC123
    if (name === 'protocol.pending') {
      const m = typeof location !== 'undefined' && location.search.match(/[?&]link=(join|friend)-([A-Z0-9]{6,8})/i);
      return Promise.resolve({ pending: m ? { action: m[1].toLowerCase(), code: m[2].toUpperCase() } : null });
    }

    // Экран загрузки: ?boot=update | updated | error
    if (name === 'startup.run' || name === 'startup.retry' || name === 'startup.skip' || name === 'changelog.get') {
      const mode = (typeof location !== 'undefined' && (location.search.match(/[?&]boot=(\w+)/) || [])[1]) || '';
      const changelog = [
        '[Загрузка] Новый экран запуска: парящая капсула, проверка обновления и «Что нового»',
        '[Лобби] Миры на сервере: запуск, подключение, доступ друзьям, создание и загрузка своего мира',
        '[Мастерская] Свои миры: бэкапы, восстановление, экспорт и импорт; галерея скриншотов',
        '[Достижения] Витрина в Гардеробе и всплывашки при получении',
        '[Гардероб] Плащ теперь видят все игроки в игре',
        '[Друзья] Настоящие аватарки игроков в лобби и списке друзей',
        '[Настройки] Статус в Discord, автообновление, «Что нового»',
        '[Ссылки] Приглашения aura:// открываются в новом окне'
      ];
      const st = (ms, stage, extra) => setTimeout(() => emit('startup.state', { stage, extra }), ms);
      if (name === 'changelog.get') return Promise.resolve({ version: 'beta 1.0.133', changelog });
      if (name === 'startup.skip') { st(100, 'ready'); return Promise.resolve({ ok: true }); }
      if (name === 'startup.retry') { st(50, 'checking'); st(900, 'upToDate'); st(1500, 'ready'); return Promise.resolve({ ok: true }); }
      if (/[?&]updated=1/.test(location.search)) {
        st(30, 'updated');
        st(60, 'ready');
        return Promise.resolve({ version: 'beta 1.0.136', updated: true, changelog });
      }
      st(30, 'checking');
      if (mode === 'error') {
        st(900, 'error', { text: 'Не удалось проверить обновления' });
      } else if (mode === 'update') {
        [0, 18, 41, 67, 88, 100].forEach((p, i) => st(700 + i * 450, 'downloading', { percentage: p, speed: '12.4 МБ/с', version: 'beta 1.0.134' }));
        st(3600, 'installing');
        st(5200, 'ready');
      } else {
        st(700, mode === 'updated' ? 'updated' : 'upToDate');
        st(1200, 'ready');
      }
      return Promise.resolve({ version: 'beta 1.0.133', updated: mode === 'updated', changelog: mode === 'updated' ? changelog : [] });
    }

    // Обновление лаунчера: в dev показываем плашку только с ?update=1
    if (name === 'launcher.updateGet') {
      const on = typeof location !== 'undefined' && /[?&]update=1/.test(location.search);
      return Promise.resolve(on
        ? { current: '1.2.140', available: true, version: '1.2.141', display: 'beta 1.0.133' }
        : { current: '1.2.140', available: false });
    }

    if (name === 'launcher.applyUpdate') {
      [20, 55, 85, 100].forEach((p, i) => setTimeout(() => emit('launcher.updateProgress', { percentage: p }), 300 * (i + 1)));
      return new Promise(resolve => setTimeout(() => resolve({ status: 'restarting' }), 1500));
    }

    if (name === 'nickname.set') {
      const nick = String((payload && payload.nickname) || '').trim();
      if (!/^[A-Za-z0-9_]{3,16}$/.test(nick)) {
        return Promise.reject(new Error('Ник: от 3 до 16 символов, латиница, цифры и _'));
      }
      try {
        const cfg = JSON.parse(localStorage.getItem('aura_mock_config') || '{}');
        cfg.nickname = nick;
        localStorage.setItem('aura_mock_config', JSON.stringify(cfg));
      } catch (e) { /* без localStorage — только в памяти */ }
      return Promise.resolve({ nickname: nick, uploadError: null });
    }

    if (name.startsWith('lobby.') || name === 'friends.invite' || name === 'friends.respondInvite') {
      return handleMockLobby(name, payload || {});
    }

    return Promise.resolve({ success: true, mock: true });
  }

  // --- Облачные миры (mock) ---
  const mockCloud = [
    { id: 'w1', name: 'Выживание с друзьями', owner: 'Player', isOwner: true, status: 'sleeping', progress: 0, port: null, online: [], max: 8, allowed: ['zxcursed'] },
    { id: 'w2', name: 'Стройка у озера', owner: 'zxcursed', isOwner: false, status: 'running', progress: 100, port: 25570, online: ['zxcursed', 'Lumi'], max: 8, allowed: [] }
  ];
  const mockLocalWorlds = [
    { folder: 'Новый мир', name: 'Новый мир', size: '48.2 МБ', lastPlayed: '20.10.2026', mode: 'Выживание', backups: [] },
    { folder: 'Aura сервер (копия)', name: 'Aura сервер (копия)', size: '312.7 МБ', lastPlayed: '21.10.2026', mode: 'Выживание', backups: [{ file: 'backup-1.zip', created: '21.10.2026 06:40', size: '300 МБ' }] }
  ];

  const mockShots = [1, 5, 9, 12, 3, 14].map((n, i) => ({
    file: `2026-10-2${i}_18.0${i}.png`, url: `assets/images/bg_${n}.jpg`, thumb: `assets/images/bg_${n}.jpg`, date: `2${i}.10.2026 18:0${i}`, size: '2.4 МБ'
  }));

  function handleMockWorlds(name, p) {
    if (name === 'screenshots.list') return Promise.resolve({ shots: mockShots });
    if (name === 'screenshots.delete') {
      const i = mockShots.findIndex(s => s.file === p.file);
      if (i >= 0) mockShots.splice(i, 1);
    }
    if (name === 'worlds.delete') {
      const i = mockLocalWorlds.findIndex(w => w.folder === p.folder);
      if (i >= 0) mockLocalWorlds.splice(i, 1);
    }
    if (name === 'worlds.backup') {
      const w = mockLocalWorlds.find(x => x.folder === p.folder);
      if (w) w.backups.unshift({ file: `backup-${Date.now()}.zip`, created: new Date().toLocaleString('ru-RU').slice(0, 17), size: w.size });
      return new Promise(r => setTimeout(() => r({ ok: true }), 700));
    }
    if (name === 'worlds.deleteBackup') {
      const w = mockLocalWorlds.find(x => x.folder === p.folder);
      if (w) w.backups = w.backups.filter(b => b.file !== p.file);
    }
    if (name === 'worlds.import') return Promise.resolve({ ok: true, name: 'Импортированный мир' });
    if (name === 'worlds.export') return Promise.resolve({ ok: true, path: 'C:\\mock\\world.zip' });
    return Promise.resolve({ ok: true });
  }

  function handleMockCloud(name, p) {
    const find = id => mockCloud.find(w => w.id === id);
    if (name === 'worlds.local') return Promise.resolve({ worlds: mockLocalWorlds });
    if (name.startsWith('worlds.') || name.startsWith('screenshots.')) return handleMockWorlds(name, p);
    if (name === 'cloud.list') {
      const active = mockCloud.filter(w => w.status !== 'sleeping').length;
      return Promise.resolve({ maxActive: 3, activeCount: active, worlds: JSON.parse(JSON.stringify(mockCloud)) });
    }
    if (name === 'cloud.create' || name === 'cloud.start' || name === 'cloud.upload') {
      let w = name === 'cloud.start' ? find(p.id) : null;
      if (!w) {
        const title = p.name || (p.folder ? p.folder : 'Мир');
        w = { id: 'w' + Date.now(), name: title, owner: 'Player', isOwner: true, status: 'sleeping', progress: 0, port: null, online: [], max: 8, allowed: [] };
        mockCloud.unshift(w);
      }
      if (name === 'cloud.upload') return Promise.resolve(Object.assign({}, w));
      Object.assign(w, { status: 'starting', progress: 10, stageText: 'Запуск виртуальной машины Java…', port: 25571 });
      [[1500, 45, 'Загрузка мира…'], [3000, 80, 'Подготовка спавна…']].forEach(([ms, pr, t]) => setTimeout(() => Object.assign(w, { progress: pr, stageText: t }), ms));
      setTimeout(() => Object.assign(w, { status: 'running', progress: 100, stageText: 'В сети' }), 4500);
      return Promise.resolve({ worldId: w.id, port: w.port, status: 'starting' });
    }
    if (name === 'cloud.connect') return Promise.resolve({ accepted: true, address: '89.168.82.200:' + ((find(p.id) || {}).port || 25565) });
    if (name === 'cloud.stop') {
      const w = find(p.id);
      if (w) { w.status = 'stopping'; setTimeout(() => Object.assign(w, { status: 'sleeping', online: [] }), 2000); }
      return Promise.resolve({ ok: true });
    }
    if (name === 'cloud.delete') {
      const i = mockCloud.findIndex(w => w.id === p.id);
      if (i >= 0) mockCloud.splice(i, 1);
      return Promise.resolve({ ok: true });
    }
    if (name === 'cloud.share') {
      const w = find(p.id);
      if (!w) return Promise.reject(new Error('Мир не найден'));
      w.allowed = p.add ? [...new Set([...w.allowed, p.nick])] : w.allowed.filter(n => n !== p.nick);
      return Promise.resolve({ ok: true, allowed: w.allowed });
    }
    return Promise.resolve({ ok: true });
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

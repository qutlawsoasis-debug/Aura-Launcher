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
        visualEffectsMode: 'full'
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

    return Promise.resolve({ success: true, mock: true });
  }

  return {
    send,
    on,
    isNative
  };
});

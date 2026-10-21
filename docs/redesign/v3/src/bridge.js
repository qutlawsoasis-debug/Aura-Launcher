/**
 * Aura Launcher v3 — WebBridge JavaScript Client
 * Handles communication between Web UI and WPF Host via WebView2 WebMessage API.
 * Falls back to mock implementation when running outside WebView2.
 */

(function (root, factory) {
  const mod = factory();
  if (typeof module === 'object' && module.exports) {
    module.exports = mod;
  } else {
    root.AuraBridge = mod;
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

      window.chrome.webview.postMessage(JSON.stringify(message));

      setTimeout(() => {
        if (pendingRequests.has(id)) {
          pendingRequests.delete(id);
          reject(new Error(`[Bridge] Timeout waiting for response to ${name}`));
        }
      }, 30000);
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
        return Promise.resolve({ started: false, reason: 'Already running' });
      }

      mockGameRunning = true;
      emit('game.status', { isRunning: true, isLaunching: true });

      let p = 0;
      const effects = document.documentElement.dataset.effects;
      const interval = effects === 'off' ? 10 : 60;

      clearInterval(mockLaunchTimer);
      mockLaunchTimer = setInterval(() => {
        p += effects === 'off' ? 50 : 10;
        if (p < 100) {
          emit('game.progress', {
            percentage: p,
            stage: p < 50 ? 'Проверка файлов' : 'Запуск',
            detail: `${p}%`
          });
        } else {
          clearInterval(mockLaunchTimer);
          emit('game.progress', { percentage: 100, stage: 'Запуск', detail: 'Готово' });
          emit('game.status', { isRunning: true, processId: 1337, exitCode: null });
        }
      }, interval);

      return Promise.resolve({ started: true, processId: 1337 });
    }

    if (name === 'game.stop') {
      clearInterval(mockLaunchTimer);
      mockGameRunning = false;
      emit('game.status', { isRunning: false, processId: null, exitCode: 0 });
      return Promise.resolve({ stopped: true });
    }

    if (name === 'window.action') {
      return Promise.resolve({ action: payload.action, success: true });
    }

    return Promise.resolve({ success: true, mock: true });
  }

  return {
    send,
    on,
    isNative
  };
});

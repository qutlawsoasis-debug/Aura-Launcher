/**
 * Aura Launcher v3 — скины игроков по нику (lobby-api, тот же источник, что у CustomSkinLoader в игре).
 * AuraSkins.apply(el, nick) ставит в .face текстуру скина; голову из неё вырезает CSS.
 */

(function () {
  'use strict';

  const API = 'https://lobby-api.vercel.app/csl/';
  const cache = new Map(); // ник → Promise<url | null>

  function get(nick) {
    const key = String(nick || '').trim().toLowerCase();
    // Без лаунчера (dev/smoke) ники демонстрационные — на сервер не ходим
    if (!key || !(window.AuraBridge && window.AuraBridge.isNative)) return Promise.resolve(null);
    if (!cache.has(key)) {
      cache.set(key, fetch(`${API}${encodeURIComponent(key)}.json`)
        .then(r => (r.ok ? r.json() : null))
        .then(j => {
          const hash = j && j.skins && (j.skins.default || j.skins.slim);
          return hash ? `${API}textures/${hash}` : null;
        })
        .catch(() => null));
    }
    return cache.get(key);
  }

  function apply(el, nick) {
    if (!el) return;
    get(nick).then(url => {
      if (!url || !el.isConnected) return;
      // Предзагрузка, чтобы лицо не мигало Стивом → скином
      const img = new Image();
      img.onload = () => { el.style.backgroundImage = `url('${url}'), url('${url}')`; };
      img.src = url;
    });
  }

  // Свой скин меняется в Гардеробе — сбрасываем кэш для своего ника
  function invalidate(nick) {
    cache.delete(String(nick || '').trim().toLowerCase());
  }

  window.AuraSkins = { get, apply, invalidate };
})();

/**
 * Aura Launcher v3 — Shared Initialization & Runtime Resilience
 * Provides safe DOM fallbacks and prevents cross-screen script cascading failures.
 */

(function () {
  'use strict';

  if (typeof window === 'undefined') return;

  // Safe global fallback for nickname input referenced across wardrobe/settings
  if (!('nickInput' in window)) {
    Object.defineProperty(window, 'nickInput', {
      get() {
        return (typeof document !== 'undefined' && (document.getElementById('inputPlayerNickname') || document.getElementById('nicknameInput'))) || null;
      },
      configurable: true,
      enumerable: true
    });
  }
})();

/**
 * Aura Launcher v3 — Performance HUD (FPS & Long Tasks Tracker)
 * Activates when URL has ?perf=1 or hash contains perf=1, or toggled via Ctrl+Shift+P.
 */

(function () {
  'use strict';

  function isPerfRequested() {
    const searchParams = new URLSearchParams(window.location.search);
    if (searchParams.get('perf') === '1') return true;
    const hash = window.location.hash.split('?')[1];
    if (hash) {
      const hashParams = new URLSearchParams(hash);
      if (hashParams.get('perf') === '1') return true;
    }
    return false;
  }

  let hudEl = null;
  let isActive = false;
  let fps = 60;
  let frameCount = 0;
  let lastTime = performance.now();
  let minFps = 60;
  let maxFps = 60;
  let longTasksCount = 0;
  let lastLongTaskDuration = 0;
  let animId = null;

  function createHud() {
    if (hudEl) return;
    hudEl = document.createElement('div');
    hudEl.id = 'perfHud';
    hudEl.style.cssText = `
      position: fixed;
      top: 14px;
      left: 14px;
      z-index: 999999;
      background: rgba(14, 18, 26, 0.95);
      border: 1px solid rgba(255, 255, 255, 0.18);
      border-radius: 10px;
      padding: 8px 14px;
      font-family: ui-monospace, SFMono-Regular, Menlo, Monaco, Consolas, monospace;
      font-size: 12px;
      line-height: 1.4;
      color: #FFFFFF;
      pointer-events: none;
      box-shadow: 0 4px 16px rgba(0, 0, 0, 0.4);
      user-select: none;
    `;
    document.body.appendChild(hudEl);

    // Setup PerformanceObserver for long tasks (> 50ms)
    if (typeof PerformanceObserver !== 'undefined') {
      try {
        const observer = new PerformanceObserver((list) => {
          for (const entry of list.getEntries()) {
            if (entry.duration >= 50) {
              longTasksCount++;
              lastLongTaskDuration = Math.round(entry.duration);
            }
          }
        });
        observer.observe({ entryTypes: ['longtask'] });
      } catch (e) {}
    }
  }

  function updateLoop() {
    frameCount++;
    const now = performance.now();
    const elapsed = now - lastTime;

    if (elapsed >= 500) {
      fps = Math.round((frameCount * 1000) / elapsed);
      frameCount = 0;
      lastTime = now;
      if (fps < minFps) minFps = fps;
      if (fps > maxFps) maxFps = fps;

      if (hudEl) {
        const fpsColor = fps >= 55 ? '#34C759' : fps >= 40 ? '#FFD60A' : '#FF453A';
        hudEl.innerHTML = `
          <div><b style="color:${fpsColor}">FPS: ${fps}</b> <span style="opacity:0.6">(min ${minFps} / max ${maxFps})</span></div>
          <div>Long Tasks (&gt;50ms): <b style="color:${longTasksCount > 0 ? '#FF9F0A' : '#34C759'}">${longTasksCount}</b> ${lastLongTaskDuration > 0 ? `(${lastLongTaskDuration}ms)` : ''}</div>
        `;
      }
    }

    if (isActive) {
      animId = requestAnimationFrame(updateLoop);
    }
  }

  function startHud() {
    if (isActive) return;
    isActive = true;
    createHud();
    lastTime = performance.now();
    frameCount = 0;
    animId = requestAnimationFrame(updateLoop);
  }

  function stopHud() {
    if (!isActive) return;
    isActive = false;
    if (animId) cancelAnimationFrame(animId);
    if (hudEl) {
      hudEl.remove();
      hudEl = null;
    }
  }

  function toggleHud() {
    if (isActive) stopHud();
    else startHud();
  }

  // Keyboard shortcut Ctrl+Shift+P
  window.addEventListener('keydown', (e) => {
    if (e.ctrlKey && e.shiftKey && (e.key === 'P' || e.key === 'p')) {
      e.preventDefault();
      toggleHud();
    }
  });

  // Check URL parameter on load
  if (isPerfRequested()) {
    if (document.readyState === 'loading') {
      document.addEventListener('DOMContentLoaded', startHud);
    } else {
      startHud();
    }
  }

  window.AuraPerfHud = {
    start: startHud,
    stop: stopHud,
    toggle: toggleHud,
    get fps() { return fps; },
    get minFps() { return minFps; },
    get longTasks() { return longTasksCount; }
  };
})();

/**
 * Aura Launcher v3 — Play / Overview Screen Logic
 * State machine for launcher button, audio player widget, changelog toast.
 */

(function () {
  'use strict';

  // Icons used in button state transitions
  const SVG_PLAY = '<svg class="icon icon-launch" viewBox="0 0 24 24" style="width: 20px; height: 20px; fill: currentColor;"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>';
  const SVG_SPINNER = '<svg class="icon icon-launch icon-spin" viewBox="0 0 24 24" style="width: 20px; height: 20px; stroke: currentColor; fill: none; stroke-width: 2;"><path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg>';
  const SVG_STOP = '<svg class="icon icon-launch" viewBox="0 0 24 24" style="width: 20px; height: 20px; fill: currentColor;"><rect x="5" y="5" width="14" height="14" rx="2"></rect></svg>';

  // Button lifecycle state machine
  const LaunchStateMachine = {
    currentState: 'idle',
    syncTimer: null,

    init() {
      const btn = document.getElementById('playLaunchBtn');
      if (!btn) return;

      btn.addEventListener('click', () => {
        if (this.currentState === 'idle') {
          this.transitionToSyncing();
        } else if (this.currentState === 'in-game') {
          this.transitionToStopping();
        }
      });

      this.applyState('idle');
    },

    transitionToSyncing() {
      this.applyState('syncing');

      const steps = [
        'Синхронизация...',
        'Проверка модов...',
        'Запуск Minecraft...'
      ];

      let currentStep = 0;
      clearInterval(this.syncTimer);

      this.syncTimer = setInterval(() => {
        if (currentStep < steps.length) {
          const text = document.getElementById('launchBtnText');
          if (text) text.textContent = steps[currentStep];
          currentStep++;
        } else {
          clearInterval(this.syncTimer);
          setTimeout(() => {
            this.transitionToInGame();
          }, 350);
        }
      }, 600);
    },

    transitionToInGame() {
      this.applyState('in-game');
      if (typeof window.showToast === 'function') {
        window.showToast('Minecraft 1.20.1 успешно запущен (PID: 14892)', 'success');
      }
    },

    transitionToStopping() {
      this.applyState('stopping');
      clearInterval(this.syncTimer);

      setTimeout(() => {
        this.applyState('idle');
        if (typeof window.showToast === 'function') {
          window.showToast('Процесс Minecraft остановлен', 'info');
        }
      }, 900);
    },

    applyState(state) {
      this.currentState = state;
      const btn = document.getElementById('playLaunchBtn');
      const icon = document.getElementById('launchBtnIcon');
      const text = document.getElementById('launchBtnText');

      if (!btn) return;

      btn.classList.remove('state-idle', 'state-syncing', 'state-in-game', 'state-stopping');
      btn.classList.add(`state-${state}`);

      switch (state) {
        case 'idle':
          if (icon) icon.innerHTML = SVG_PLAY;
          if (text) text.textContent = 'Играть';
          btn.disabled = false;
          btn.removeAttribute('aria-busy');
          btn.removeAttribute('aria-disabled');
          break;

        case 'syncing':
          if (icon) icon.innerHTML = SVG_SPINNER;
          if (text) text.textContent = 'Синхронизация...';
          btn.disabled = false;
          btn.setAttribute('aria-busy', 'true');
          btn.removeAttribute('aria-disabled');
          break;

        case 'in-game':
          if (icon) icon.innerHTML = SVG_STOP;
          if (text) text.textContent = 'Остановить';
          btn.disabled = false;
          btn.removeAttribute('aria-busy');
          btn.removeAttribute('aria-disabled');
          break;

        case 'stopping':
          if (icon) icon.innerHTML = SVG_SPINNER;
          if (text) text.textContent = 'Остановка...';
          btn.disabled = true;
          btn.setAttribute('aria-busy', 'true');
          btn.setAttribute('aria-disabled', 'true');
          break;
      }
    }
  };

  // Audio Widget Controller
  const SVG_AUDIO_PLAY = '<polygon points="5 3 19 12 5 21 5 3"></polygon>';
  const SVG_AUDIO_PAUSE = '<rect x="6" y="4" width="4" height="16"></rect><rect x="14" y="4" width="4" height="16"></rect>';

  function initAudioPlayer() {
    const audioEl = document.getElementById('screenPlayAudio');
    const widget = document.getElementById('audioPillWidget');
    const audioToggleBtn = document.getElementById('audioToggleBtn');
    const audioIcon = document.getElementById('audioStateIcon');

    let isPlaying = false;

    function setPlayingState(playing) {
      isPlaying = playing;
      if (widget) widget.classList.toggle('is-playing', playing);
      if (audioIcon) {
        audioIcon.innerHTML = playing ? SVG_AUDIO_PAUSE : SVG_AUDIO_PLAY;
      }
      if (audioToggleBtn) {
        audioToggleBtn.setAttribute('aria-label', playing ? 'Пауза' : 'Воспроизвести трек');
        audioToggleBtn.setAttribute('title', playing ? 'Пауза' : 'Воспроизвести трек');
      }
    }

    function togglePlayback() {
      if (!isPlaying) {
        if (audioEl) {
          audioEl.play().then(() => {
            setPlayingState(true);
          }).catch(() => {
            setPlayingState(true);
          });
        } else {
          setPlayingState(true);
        }
      } else {
        if (audioEl) audioEl.pause();
        setPlayingState(false);
      }
    }

    if (audioToggleBtn) {
      audioToggleBtn.addEventListener('click', (e) => {
        e.stopPropagation();
        togglePlayback();
      });
    }

    if (widget) {
      widget.addEventListener('click', () => {
        togglePlayback();
      });
      widget.addEventListener('keydown', (e) => {
        if (e.key === ' ' || e.key === 'Enter') {
          e.preventDefault();
          togglePlayback();
        }
      });
    }

    if (audioEl) {
      audioEl.addEventListener('ended', () => {
        setPlayingState(false);
      });
    }
  }

  // Changelog button handler
  function initChangelog() {
    const changelogBtn = document.getElementById('btnChangelog');
    if (changelogBtn) {
      changelogBtn.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Aura Pack v1.2: Fabric 0.19.5, 31 мод, шейдеры Complementary v5', 'info');
        }
      });
    }
  }

  window.initPlayScreen = function () {
    if (window._playScreenInited) return;
    window._playScreenInited = true;
    LaunchStateMachine.init();
    initAudioPlayer();
    initChangelog();
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', window.initPlayScreen);
  } else {
    window.initPlayScreen();
  }
})();

/**
 * Aura Launcher v3 — Play / Overview Screen Logic
 * State machine for launcher button, audio player widget, avatar renderer, story handlers.
 */

(function () {
  'use strict';

  // Steve head 8x8 pixel palette (Minecraft standard skin texture)
  const STEVE_FACE = [
    ['#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10'],
    ['#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10', '#2c1b10'],
    ['#2c1b10', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#2c1b10'],
    ['#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66', '#c68c66'],
    ['#c68c66', '#ffffff', '#2c3580', '#c68c66', '#c68c66', '#ffffff', '#2c3580', '#c68c66'],
    ['#c68c66', '#c68c66', '#c68c66', '#9c5c3e', '#9c5c3e', '#c68c66', '#c68c66', '#c68c66'],
    ['#c68c66', '#c68c66', '#613220', '#613220', '#613220', '#613220', '#c68c66', '#c68c66'],
    ['#c68c66', '#c68c66', '#2c1b10', '#c68c66', '#c68c66', '#2c1b10', '#c68c66', '#c68c66']
  ];

  function drawPlayerAvatar() {
    const canvas = document.getElementById('playerAvatarCanvas');
    if (!canvas) return;
    const ctx = canvas.getContext('2d');
    if (!ctx) return;

    ctx.imageSmoothingEnabled = false;
    const pixelSize = canvas.width / 8;

    for (let r = 0; r < 8; r++) {
      for (let c = 0; c < 8; c++) {
        ctx.fillStyle = STEVE_FACE[r][c];
        ctx.fillRect(c * pixelSize, r * pixelSize, pixelSize, pixelSize);
      }
    }
  }

  // Icons used in button state transitions
  const SVG_PLAY = '<svg class="icon icon-launch" viewBox="0 0 24 24"><polygon points="5 3 19 12 5 21 5 3"></polygon></svg>';
  const SVG_SPINNER = '<svg class="icon icon-launch icon-spin" viewBox="0 0 24 24"><path d="M21 12a9 9 0 1 1-6.219-8.56"></path></svg>';
  const SVG_STOP = '<svg class="icon icon-launch" viewBox="0 0 24 24"><rect x="5" y="5" width="14" height="14" rx="2"></rect></svg>';

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

      const progressBar = document.getElementById('launchProgressBar');
      const stateTitle = document.getElementById('launchStateTitle');
      const subtext = document.getElementById('launchSubtext');

      const steps = [
        { progress: 15, title: 'Синхронизация...', sub: 'Проверка manifest.json...' },
        { progress: 42, title: 'Синхронизация...', sub: 'Проверка целостности модов (38/93)...' },
        { progress: 78, title: 'Синхронизация...', sub: 'Верификация SHA-256 контрольных сумм...' },
        { progress: 95, title: 'Запуск Minecraft...', sub: 'Инициализация JVM (Fabric Loader 0.19.5)...' }
      ];

      let currentStep = 0;
      clearInterval(this.syncTimer);

      this.syncTimer = setInterval(() => {
        if (currentStep < steps.length) {
          const item = steps[currentStep];
          if (progressBar) progressBar.style.width = item.progress + '%';
          if (stateTitle) stateTitle.textContent = item.title;
          if (subtext) subtext.textContent = item.sub;
          currentStep++;
        } else {
          clearInterval(this.syncTimer);
          if (progressBar) progressBar.style.width = '100%';
          setTimeout(() => {
            this.transitionToInGame();
          }, 350);
        }
      }, 550);
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
      const stateTitle = document.getElementById('launchStateTitle');
      const subtext = document.getElementById('launchSubtext');
      const progressBar = document.getElementById('launchProgressBar');

      if (!btn) return;

      btn.classList.remove('state-idle', 'state-syncing', 'state-in-game', 'state-stopping');
      btn.classList.add(`state-${state}`);

      switch (state) {
        case 'idle':
          if (icon) icon.innerHTML = SVG_PLAY;
          if (text) text.textContent = 'Играть';
          if (stateTitle) stateTitle.textContent = 'Готово к запуску';
          if (subtext) subtext.textContent = 'Выделено 6 ГБ ОЗУ • Java 17 Temurin';
          if (progressBar) progressBar.style.width = '0%';
          btn.disabled = false;
          break;

        case 'syncing':
          if (icon) icon.innerHTML = SVG_SPINNER;
          if (text) text.textContent = 'Синхронизация...';
          btn.disabled = false;
          break;

        case 'in-game':
          if (icon) icon.innerHTML = SVG_STOP;
          if (text) text.textContent = 'Остановить';
          if (stateTitle) stateTitle.textContent = 'В игре (Minecraft 1.20.1)';
          if (subtext) subtext.textContent = 'PID: 14892 • Нажмите, чтобы остановить игру';
          btn.disabled = false;
          break;

        case 'stopping':
          if (icon) icon.innerHTML = SVG_SPINNER;
          if (text) text.textContent = 'Остановка...';
          if (stateTitle) stateTitle.textContent = 'Остановка процесса...';
          if (subtext) subtext.textContent = 'Завершение сессии игры...';
          btn.disabled = true;
          break;
      }
    }
  };

  // Audio Widget Controller
  const SVG_AUDIO_PLAY = '<polygon points="5 3 19 12 5 21 5 3"></polygon>';
  const SVG_AUDIO_PAUSE = '<rect x="6" y="4" width="4" height="16"></rect><rect x="14" y="4" width="4" height="16"></rect>';

  function initAudioPlayer() {
    const audioEl = document.getElementById('screenPlayAudio');
    const vinylContainer = document.getElementById('vinylContainer');
    const audioToggleBtn = document.getElementById('audioToggleBtn');
    const audioIcon = document.getElementById('audioStateIcon');
    const eq = document.getElementById('audioEqualizer');

    let isPlaying = false;

    function setPlayingState(playing) {
      isPlaying = playing;
      if (vinylContainer) vinylContainer.classList.toggle('is-playing', playing);
      if (eq) eq.classList.toggle('animating', playing);
      if (audioIcon) {
        audioIcon.innerHTML = playing ? SVG_AUDIO_PAUSE : SVG_AUDIO_PLAY;
      }
    }

    function togglePlayback() {
      if (!isPlaying) {
        if (audioEl) {
          audioEl.play().then(() => {
            setPlayingState(true);
          }).catch(() => {
            // Simulated state when autoplay or file format is restricted
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

    if (vinylContainer) {
      vinylContainer.addEventListener('click', () => {
        togglePlayback();
      });
    }

    if (audioEl) {
      audioEl.addEventListener('ended', () => {
        setPlayingState(false);
      });
    }
  }

  // Story news buttons & changelog link
  function initStoriesAndChangelog() {
    const changelogBtn = document.getElementById('btnChangelog');
    if (changelogBtn) {
      changelogBtn.addEventListener('click', () => {
        if (typeof window.showToast === 'function') {
          window.showToast('Aura Pack v1.2: Fabric 0.19.5, 93 мода, шейдеры Complementary v5', 'info');
        }
      });
    }

    document.querySelectorAll('.btn-story-more').forEach(btn => {
      btn.addEventListener('click', (e) => {
        const card = e.currentTarget.closest('.story-card');
        const heading = card ? card.querySelector('.story-heading')?.textContent : 'Событие';
        if (typeof window.showToast === 'function') {
          window.showToast(`Новость: ${heading}`, 'info');
        }
      });
    });
  }

  window.initPlayScreen = function () {
    drawPlayerAvatar();
    LaunchStateMachine.init();
    initAudioPlayer();
    initStoriesAndChangelog();
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', window.initPlayScreen);
  } else {
    window.initPlayScreen();
  }
})();

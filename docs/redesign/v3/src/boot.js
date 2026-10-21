/**
 * Aura Launcher v3 — экран загрузки.
 * Мост проверяет обновление (startup.run → события startup.state), экран показывает статус.
 * Когда всё готово, парящая капсула уплывает вниз на место настоящей и растворяется в ней.
 */

(function () {
  'use strict';

  const MIN_SHOW_MS = 1500;

  function initBoot() {
    const root = document.documentElement;
    const boot = document.getElementById('boot');
    if (!boot || !root.classList.contains('booting')) return;

    const floatBox = boot.querySelector('.boot-float');
    const capsule = document.getElementById('bootCapsule');
    const status = document.getElementById('bootStatus');
    const bar = document.getElementById('bootBar');
    const actions = document.getElementById('bootActions');
    const retryBtn = document.getElementById('bootRetry');
    const skipBtn = document.getElementById('bootSkip');
    const versionEl = document.getElementById('bootVersion');
    const news = document.getElementById('bootNews');
    const newsList = document.getElementById('bootNewsList');
    const newsVersion = document.getElementById('bootNewsVersion');
    const newsGo = document.getElementById('bootNewsGo');

    const startedAt = performance.now();
    const effectsOff = () => root.dataset.effects === 'off';
    let finished = false;
    let waitNews = null;

    const DOTS = '<span class="boot-dots"><i>.</i><i>.</i><i>.</i></span>';

    function setStatus(html, tone) {
      if (status.dataset.html === html && status.dataset.tone === (tone || '')) return;
      status.dataset.html = html;
      status.dataset.tone = tone || '';
      const apply = () => {
        status.innerHTML = html;
        status.classList.toggle('ok', tone === 'ok');
        status.classList.toggle('err', tone === 'err');
        status.classList.remove('swap');
      };
      if (effectsOff()) { apply(); return; }
      status.classList.add('swap');
      setTimeout(apply, 160);
    }

    function onState(s) {
      if (!s || finished) return;
      const x = s.extra || {};
      boot.classList.toggle('downloading', s.stage === 'downloading');
      actions.hidden = s.stage !== 'error';
      switch (s.stage) {
        case 'checking':
          setStatus('Проверка обновления' + DOTS);
          break;
        case 'upToDate':
          setStatus('Установлено последнее обновление', 'ok');
          break;
        case 'updated':
          setStatus('Обновление установлено', 'ok');
          break;
        case 'downloading': {
          const pct = Math.max(0, Math.min(100, Number(x.percentage) || 0));
          bar.style.transform = `scaleX(${pct / 100})`;
          setStatus(`Скачиваем ${x.version || 'обновление'} · ${Math.round(pct)}%${x.speed ? ' · ' + x.speed : ''}`);
          break;
        }
        case 'installing':
          bar.style.transform = 'scaleX(1)';
          setStatus('Установка и перезапуск' + DOTS);
          break;
        case 'error':
          setStatus(x.text || 'Ошибка', 'err');
          retryBtn.hidden = Boolean(x.skipOnly);
          break;
        case 'ready':
          finish();
          break;
      }
    }

    async function finish() {
      if (finished) return;
      finished = true;
      if (waitNews) await waitNews;

      const wait = MIN_SHOW_MS - (performance.now() - startedAt);
      if (wait > 0) await new Promise(r => setTimeout(r, wait));
      setStatus('Запуск' + DOTS);
      await new Promise(r => setTimeout(r, effectsOff() ? 0 : 450));

      const island = document.getElementById('island');
      if (effectsOff() || !island || !capsule.animate) {
        root.classList.remove('booting');
        boot.remove();
        return;
      }

      // Капсула плывёт из центра точно на место настоящей и растворяется в ней
      const from = capsule.getBoundingClientRect();
      const to = island.getBoundingClientRect();
      const dx = (to.left + to.width / 2) - (from.left + from.width / 2);
      const dy = (to.top + to.height / 2) - (from.top + from.height / 2);
      const current = getComputedStyle(floatBox).transform;
      floatBox.style.animation = 'none';
      boot.classList.add('leaving');

      // Одно плавное движение без промежуточных рывков
      const glide = floatBox.animate([
        { transform: current === 'none' ? 'none' : current },
        { transform: `translate(${dx}px, ${dy}px)` }
      ], { duration: 950, easing: 'cubic-bezier(0.65, 0, 0.25, 1)', fill: 'forwards' });
      await glide.finished.catch(() => {});

      // Прилетела ровно на место — перекрёстная смена с настоящей капсулой
      root.classList.remove('booting');
      await capsule.animate([{ opacity: 1 }, { opacity: 0 }], { duration: 320, easing: 'ease-out', fill: 'forwards' }).finished.catch(() => {});
      boot.remove();
    }

    function showNews(info) {
      const items = (info && info.changelog) || [];
      if (!info || !info.updated || !items.length) return;
      newsVersion.textContent = info.version || '';
      newsList.innerHTML = '';
      items.forEach((line, i) => {
        const li = document.createElement('li');
        li.style.setProperty('--i', i);
        li.textContent = line.replace(/^\[[^\]]+\]\s*/, '');
        newsList.appendChild(li);
      });
      news.hidden = false;
      waitNews = new Promise(resolve => newsGo.addEventListener('click', () => {
        news.hidden = true;
        resolve();
      }, { once: true }));
    }

    retryBtn.addEventListener('click', () => { actions.hidden = true; window.AuraBridge.send('startup.retry').catch(() => {}); });
    skipBtn.addEventListener('click', () => { actions.hidden = true; window.AuraBridge.send('startup.skip').catch(() => {}); });

    if (!window.AuraBridge) {
      finish();
      return;
    }
    window.AuraBridge.on('startup.state', onState);
    window.AuraBridge.send('startup.run')
      .then(info => {
        if (info && info.version) versionEl.textContent = info.version;
        showNews(info);
      })
      .catch(() => finish());

    // Страховка: если мост молчит, не держим пользователя на заставке
    setTimeout(() => { if (!finished && actions.hidden && !boot.classList.contains('downloading')) finish(); }, 20000);
  }

  // «Что нового» по кнопке в Настройках: стеклянное окно со списком изменений версии
  window.AuraChangelog = function (info) {
    const items = (info && info.changelog) || [];
    const sheet = document.createElement('div');
    sheet.className = 'ach-sheet';
    sheet.innerHTML = `<div class="ach-panel" style="width:min(560px,100%)">
      <div class="ach-head">
        <div style="flex:1"><h2 style="margin:0">Что нового</h2><div class="small dim"></div></div>
        <button type="button" class="close" aria-label="Закрыть">✕</button>
      </div>
      <ul class="changelog-list"></ul>
    </div>`;
    sheet.querySelector('.small').textContent = (info && info.version) || '';
    const list = sheet.querySelector('ul');
    (items.length ? items : ['Список изменений пуст']).forEach((line, i) => {
      const li = document.createElement('li');
      li.style.setProperty('--i', i);
      li.textContent = String(line).replace(/^\[[^\]]+\]\s*/, '');
      list.appendChild(li);
    });
    const close = () => {
      sheet.classList.remove('show');
      setTimeout(() => sheet.remove(), 320);
      document.removeEventListener('keydown', onKey, true);
    };
    const onKey = e => { if (e.key === 'Escape') { e.stopPropagation(); close(); } };
    sheet.querySelector('.close').addEventListener('click', close);
    sheet.addEventListener('click', e => { if (e.target === sheet) close(); });
    document.addEventListener('keydown', onKey, true);
    document.body.appendChild(sheet);
    requestAnimationFrame(() => sheet.classList.add('show'));
  };

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initBoot);
  } else {
    initBoot();
  }
})();

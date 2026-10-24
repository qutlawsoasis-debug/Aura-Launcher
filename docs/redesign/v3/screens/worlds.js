/**
 * Aura Launcher v3 — Мастерская: вкладки «Миры» и «Скриншоты».
 * Миры: играть, бэкап, восстановление, экспорт/импорт, удаление.
 * Скриншоты: галерея F2-снимков, просмотр, копирование в буфер, удаление.
 */

(function () {
  'use strict';

  function initWorlds() {
    const worldsList = document.getElementById('worldsList');
    const shotsGrid = document.getElementById('shotsGrid');
    const viewer = document.getElementById('shotViewer');
    const viewerImg = document.getElementById('shotViewerImg');
    const viewerInfo = document.getElementById('shotViewerInfo');
    if (!worldsList || !window.AuraBridge) return;
    // Капсула обрезает содержимое (contain: paint) — просмотрщик живёт поверх всего окна
    if (viewer) document.body.appendChild(viewer);

    const send = (name, payload) => window.AuraBridge.send(name, payload || {});
    const toast = (m, t) => typeof window.showToast === 'function' && window.showToast(m, t || 'info');
    const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const errText = e => (e && e.message) || String(e || 'Ошибка');

    const open = new Set();
    const confirming = new Map(); // ключ -> таймер двойного подтверждения
    let worlds = [];
    let shots = [];
    let current = null;

    // Двухшаговое подтверждение опасных действий: первый клик меняет текст, второй выполняет
    function confirmTwice(key, btn, label, action) {
      if (confirming.has(key)) {
        clearTimeout(confirming.get(key));
        confirming.delete(key);
        action();
        return;
      }
      const old = btn.textContent;
      btn.textContent = label;
      confirming.set(key, setTimeout(() => { confirming.delete(key); btn.textContent = old; }, 3000));
    }

    // ---------- Миры ----------
    function renderWorlds() {
      worldsList.innerHTML = '';
      if (!worlds.length) {
        worldsList.innerHTML = '<div class="cloud-empty small dim">Локальных миров пока нет — зайдите в одиночную игру или импортируйте архив.</div>';
        return;
      }
      worlds.forEach((w, i) => {
        const el = document.createElement('div');
        el.className = 'cw' + (open.has(w.folder) ? ' open' : '');
        el.style.setProperty('--i', i);
        const meta = [w.mode, w.size, w.lastPlayed].filter(Boolean).join(' · ');
        const backups = w.backups || [];
        el.innerHTML = `
          <div class="cw-row">
            <div class="cw-icon">${esc((w.name || '?').trim().charAt(0).toUpperCase())}</div>
            <div class="cw-main">
              <div class="cw-name">${esc(w.name)}</div>
              <div class="cw-status">${esc(meta)}${backups.length ? ` · бэкапов: ${backups.length}` : ''}</div>
            </div>
            <button type="button" class="btn sm primary ws-play">Играть</button>
            <button type="button" class="btn sm ghost cw-more-btn" aria-label="Ещё">⋯</button>
          </div>
          <div class="cw-panel"><div><div class="cw-panel-inner">
            <div class="cw-panel-actions" style="margin-top:12px;flex-wrap:wrap">
              <button type="button" class="btn sm ws-backup">Бэкап сейчас</button>
              <button type="button" class="btn sm ws-export">Экспорт .zip</button>
              <button type="button" class="btn sm ghost ws-folder">Папка</button>
              <button type="button" class="btn sm ghost danger ws-delete">Удалить мир</button>
            </div>
            ${backups.length ? `<div class="small dim" style="margin:14px 0 6px">Бэкапы</div>
              <div class="ws-backups">${backups.map(b => `
                <div class="ws-backup-row" data-file="${esc(b.file)}">
                  <span>${esc(b.created)}</span><span class="dim">${esc(b.size)}</span>
                  <button type="button" class="btn sm ws-restore">Восстановить</button>
                  <button type="button" class="btn sm ghost ws-bdel" aria-label="Удалить бэкап">✕</button>
                </div>`).join('')}</div>` : ''}
          </div></div></div>`;

        el.querySelector('.ws-play').addEventListener('click', () => playWorld(w));
        el.querySelector('.cw-more-btn').addEventListener('click', () => {
          if (open.has(w.folder)) open.delete(w.folder); else open.add(w.folder);
          el.classList.toggle('open');
        });
        el.querySelector('.ws-backup').addEventListener('click', e => act(e.currentTarget, 'worlds.backup', { folder: w.folder }, 'Бэкап сохранён', 'Сохраняем…'));
        el.querySelector('.ws-export').addEventListener('click', e => exportWorld(w, e.currentTarget));
        el.querySelector('.ws-folder').addEventListener('click', () => send('worlds.openFolder', { folder: w.folder }));
        el.querySelector('.ws-delete').addEventListener('click', e =>
          confirmTwice('del:' + w.folder, e.currentTarget, 'Точно удалить?', () => act(null, 'worlds.delete', { folder: w.folder }, `Мир «${w.name}» удалён`)));
        el.querySelectorAll('.ws-backup-row').forEach(row => {
          const file = row.dataset.file;
          row.querySelector('.ws-restore').addEventListener('click', e =>
            confirmTwice('res:' + file, e.currentTarget, 'Перезаписать мир?', () => act(e.currentTarget, 'worlds.restore', { folder: w.folder, file }, 'Мир восстановлен из бэкапа', 'Восстанавливаем…')));
          row.querySelector('.ws-bdel').addEventListener('click', e =>
            confirmTwice('bdel:' + file, e.currentTarget, 'Удалить?', () => act(null, 'worlds.deleteBackup', { folder: w.folder, file }, 'Бэкап удалён')));
        });
        worldsList.appendChild(el);
      });
    }

    async function loadWorlds() {
      try {
        const res = await send('worlds.local');
        worlds = (res && res.worlds) || [];
        renderWorlds();
      } catch (err) {
        worldsList.innerHTML = `<div class="cloud-empty small dim">${esc(errText(err))}</div>`;
      }
    }

    async function act(btn, cmd, payload, okText, busyText) {
      const old = btn ? btn.textContent : '';
      if (btn) { btn.disabled = true; if (busyText) btn.textContent = busyText; }
      try {
        await send(cmd, payload);
        toast(okText, 'success');
        await loadWorlds();
      } catch (err) {
        toast(errText(err), 'error');
        if (btn) { btn.disabled = false; btn.textContent = old; }
      }
    }

    async function exportWorld(w, btn) {
      btn.disabled = true;
      try {
        const res = await send('worlds.export', { folder: w.folder });
        if (res && !res.cancelled) toast('Мир сохранён в архив', 'success');
      } catch (err) {
        toast(errText(err), 'error');
      } finally {
        btn.disabled = false;
      }
    }

    async function playWorld(w) {
      try {
        await send('game.launch', { world: w.folder });
        if (typeof window.setIslandMode === 'function') window.setIslandMode('idle');
      } catch (err) {
        toast(errText(err), 'error');
      }
    }

    document.getElementById('btnWorldImport').addEventListener('click', async e => {
      const btn = e.currentTarget;
      btn.disabled = true;
      try {
        const res = await send('worlds.import');
        if (res && !res.cancelled) {
          toast(`Мир «${res.name}» импортирован`, 'success');
          loadWorlds();
        }
      } catch (err) {
        toast(errText(err), 'error');
      } finally {
        btn.disabled = false;
      }
    });
    document.getElementById('btnWorldsFolder').addEventListener('click', () => send('worlds.openFolder', { kind: 'saves' }));
    document.getElementById('btnBackupsFolder').addEventListener('click', () => send('worlds.openFolder', { kind: 'backups' }));

    // ---------- Скриншоты ----------
    function renderShots() {
      shotsGrid.innerHTML = '';
      if (!shots.length) {
        shotsGrid.innerHTML = '<div class="cloud-empty small dim" style="grid-column:1/-1">Скриншотов пока нет. Нажмите F2 в игре.</div>';
        return;
      }
      shots.forEach((s, i) => {
        const b = document.createElement('button');
        b.type = 'button';
        b.className = 'shot';
        b.style.setProperty('--i', Math.min(i, 12));
        b.innerHTML = `<img loading="lazy" src="${esc(s.thumb || s.url)}" alt=""><span>${esc(s.date)}</span>`;
        b.addEventListener('click', () => openViewer(s));
        shotsGrid.appendChild(b);
      });
    }

    async function loadShots() {
      try {
        const res = await send('screenshots.list');
        shots = (res && res.shots) || [];
        renderShots();
      } catch (err) {
        shotsGrid.innerHTML = `<div class="cloud-empty small dim" style="grid-column:1/-1">${esc(errText(err))}</div>`;
      }
    }

    function openViewer(s) {
      current = s;
      viewerImg.src = s.url;
      viewerInfo.textContent = `${s.date} · ${s.size}`;
      viewer.hidden = false;
      requestAnimationFrame(() => viewer.classList.add('show'));
    }

    function closeViewer() {
      viewer.classList.remove('show');
      setTimeout(() => { viewer.hidden = true; viewerImg.removeAttribute('src'); }, 220);
      current = null;
    }

    document.getElementById('shotClose').addEventListener('click', closeViewer);
    viewer.addEventListener('click', e => { if (e.target === viewer) closeViewer(); });
    document.addEventListener('keydown', e => {
      if (e.key === 'Escape' && !viewer.hidden) { e.stopPropagation(); closeViewer(); }
    }, true);
    document.getElementById('shotCopy').addEventListener('click', async () => {
      if (!current) return;
      try { await send('screenshots.copy', { file: current.file }); toast('Скриншот скопирован — вставьте в Discord', 'success'); }
      catch (err) { toast(errText(err), 'error'); }
    });
    document.getElementById('shotOpen').addEventListener('click', () => current && send('screenshots.open', { file: current.file }));
    document.getElementById('shotDelete').addEventListener('click', e => {
      if (!current) return;
      const s = current;
      const btn = e.currentTarget;
      confirmTwice('shot:' + s.file, btn, 'Точно удалить?', async () => {
        try {
          await send('screenshots.delete', { file: s.file });
          btn.textContent = 'Удалить';
          closeViewer();
          shots = shots.filter(x => x.file !== s.file);
          renderShots();
          toast('Скриншот удалён', 'info');
        } catch (err) { toast(errText(err), 'error'); }
      });
    });
    document.getElementById('btnShotsFolder').addEventListener('click', () => send('screenshots.openFolder'));

    document.addEventListener('workshop:tab', e => {
      if (e.detail === 'worlds') loadWorlds();
      if (e.detail === 'shots') loadShots();
    });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initWorlds);
  } else {
    initWorlds();
  }
})();

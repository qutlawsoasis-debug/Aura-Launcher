/**
 * Aura Launcher v3 — «Миры на сервере» в разделе Лобби.
 * Список облачных миров, «Играть» (будит мир и подключает), остановка, удаление,
 * доступ друзьям, создание и загрузка своего мира.
 */

(function () {
  'use strict';

  const FAST_POLL = 1500;
  const SLOW_POLL = 15000;

  function initCloud() {
    const seg = document.getElementById('lobbySegmented');
    const paneRoom = document.getElementById('paneRoom');
    const paneCloud = document.getElementById('paneCloud');
    const list = document.getElementById('cloudList');
    const slots = document.getElementById('cloudSlots');
    const layer = document.querySelector('.layer[data-l="lobby"]');
    const newName = document.getElementById('cloudNewName');
    const newSeed = document.getElementById('cloudNewSeed');
    const versionSeg = document.getElementById('cloudVersionSeg');
    const createBtn = document.getElementById('btnCloudCreate');
    const uploadOpen = document.getElementById('btnCloudUploadOpen');
    const uploadSelect = document.getElementById('cloudUploadSelect');
    const uploadBtn = document.getElementById('btnCloudUpload');
    const uploadStatus = document.getElementById('cloudUploadStatus');
    if (!seg || !paneCloud || !window.AuraBridge) return;

    let worlds = [];
    let friends = [];
    let pollTimer = null;
    let tab = 'room';
    const openIds = new Set();
    const connectWhenReady = new Set();
    const confirmDelete = new Set();
    const seenIds = new Set();

    const send = (name, payload) => window.AuraBridge.send(name, payload || {});
    const toast = (m, t) => typeof window.showToast === 'function' && window.showToast(m, t || 'info');
    const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const errText = e => (e && e.message) || String(e || 'Ошибка');

    // --- Вкладки «Комната / Миры на сервере» ---
    seg.querySelectorAll('button').forEach(btn => btn.addEventListener('click', () => {
      const next = btn.dataset.tab;
      if (next === tab) return;
      tab = next;
      window.swapPane(next === 'cloud' ? paneRoom : paneCloud, next === 'cloud' ? paneCloud : paneRoom);
      if (next === 'cloud') refresh(true);
      schedule();
    }));

    function statusText(w) {
      if (w.status === 'running') return `В сети · ${(w.online || []).length}/${w.max || 8}`;
      if (w.status === 'starting') return w.stageText || 'Запускается…';
      if (w.status === 'stopping') return 'Сохранение мира…';
      return 'Спит';
    }

    let hasLoadedOnce = false;

    function renderSkeleton() {
      list.innerHTML = `
        <div class="cw skeleton">
          <div class="cw-row">
            <div class="cw-icon"></div>
            <div class="cw-main">
              <div class="sk-text"></div>
              <div class="sk-sub"></div>
            </div>
            <div class="sk-btn"></div>
          </div>
        </div>
        <div class="cw skeleton">
          <div class="cw-row">
            <div class="cw-icon"></div>
            <div class="cw-main">
              <div class="sk-text" style="width:110px"></div>
              <div class="sk-sub" style="width:60px"></div>
            </div>
            <div class="sk-btn"></div>
          </div>
        </div>
        <div class="cloud-loading"><span class="small dim">Загружаю миры…</span></div>`;
    }

    function renderError(message) {
      list.innerHTML = `
        <div class="cloud-error-box">
          <div class="cloud-error-msg">${esc(message || 'Сервер не отвечает')}</div>
          <button type="button" class="btn sm primary" id="btnCloudRetry">Повторить</button>
        </div>`;
      const retryBtn = document.getElementById('btnCloudRetry');
      if (retryBtn) retryBtn.addEventListener('click', () => refresh(true));
    }

    function render() {
      list.innerHTML = '';
      if (!worlds.length) {
        list.innerHTML = '<div class="cloud-empty small dim">У вас пока нет миров — создайте первый или загрузите свой.</div>';
        return;
      }
      worlds.forEach((w, i) => {
        const el = document.createElement('div');
        el.className = 'cw' + (openIds.has(w.id) ? ' open' : '');
        el.dataset.status = w.status;
        // «Всплывают» только новые строки, при опросе список не дёргается
        if (seenIds.has(w.id)) el.style.animation = 'none';
        else el.style.setProperty('--i', i);
        seenIds.add(w.id);
        const busy = w.status === 'starting' || connectWhenReady.has(w.id);
        const progress = Math.max(5, Math.min(100, Number(w.progress) || 0)) / 100;
        el.innerHTML = `
          <div class="cw-row">
            <div class="cw-icon">${esc((w.name || '?').trim().charAt(0).toUpperCase())}</div>
            <div class="cw-main">
              <div class="cw-name">${esc(w.name)}${w.isOwner ? '' : ` <span class="cw-owner">· ${esc(w.owner)}</span>`}</div>
              <div class="cw-status">${esc(statusText(w))}</div>
              <div class="cw-bar"><i style="--p:${progress}"></i></div>
            </div>
            <button type="button" class="btn sm primary cw-play${busy ? ' busy' : ''}">${busy ? 'Запуск…' : 'Играть'}</button>
            ${w.isOwner ? '<button type="button" class="btn sm ghost cw-more-btn" aria-label="Ещё">⋯</button>' : ''}
          </div>
          ${w.isOwner ? `<div class="cw-panel"><div><div class="cw-panel-inner">
            <div class="small dim" style="margin-top:10px">Доступ друзьям</div>
            <div class="cw-access">${friends.length ? friends.map(f => {
              const on = (w.allowed || []).some(a => a.toLowerCase() === f.toLowerCase());
              return `<button type="button" class="cw-chip${on ? ' on' : ''}" data-nick="${esc(f)}">${on ? '✓ ' : '+ '}${esc(f)}</button>`;
            }).join('') : '<span class="small dim">Список друзей пуст</span>'}</div>
            <div class="cw-panel-actions">
              ${w.status === 'running' ? '<button type="button" class="btn sm cw-stop">Остановить</button>' : ''}
              <button type="button" class="btn sm ghost danger cw-delete">${confirmDelete.has(w.id) ? 'Точно удалить?' : 'Удалить мир'}</button>
            </div>
          </div></div></div>` : ''}`;

        el.querySelector('.cw-play').addEventListener('click', () => play(w));
        const more = el.querySelector('.cw-more-btn');
        if (more) more.addEventListener('click', () => {
          if (openIds.has(w.id)) openIds.delete(w.id); else openIds.add(w.id);
          el.classList.toggle('open');
        });
        el.querySelectorAll('.cw-chip').forEach(c => c.addEventListener('click', () => share(w, c)));
        const stop = el.querySelector('.cw-stop');
        if (stop) stop.addEventListener('click', () => stopWorld(w, stop));
        const del = el.querySelector('.cw-delete');
        if (del) del.addEventListener('click', () => deleteWorld(w));
        list.appendChild(el);
      });
    }

    async function refresh(withFriends) {
      if (!hasLoadedOnce || withFriends) {
        renderSkeleton();
      }
      try {
        if (withFriends) {
          const f = await send('friends.get').catch(() => null);
          friends = ((f && f.friends) || []).map(x => x.nick).filter(Boolean);
        }
        const timeoutPromise = new Promise((_, reject) => setTimeout(() => reject(new Error('Сервер не отвечает')), 8000));
        const res = await Promise.race([send('cloud.list'), timeoutPromise]);
        hasLoadedOnce = true;
        worlds = (res && res.worlds) || [];
        if (slots) slots.textContent = res && res.maxActive ? `Запущено ${res.activeCount} из ${res.maxActive}` : '';
        // Мир проснулся — подключаем, если игрок ждал
        const ready = worlds.filter(w => connectWhenReady.has(w.id) && w.status === 'running');
        ready.forEach(w => connectWhenReady.delete(w.id));
        render();
        ready.forEach(connect);
      } catch (err) {
        hasLoadedOnce = false;
        renderError(errText(err));
      }
      schedule();
    }

    function schedule() {
      clearTimeout(pollTimer);
      if (tab !== 'cloud' || !layer || !layer.classList.contains('on')) return;
      const fast = connectWhenReady.size > 0 || worlds.some(w => w.status === 'starting' || w.status === 'stopping');
      pollTimer = setTimeout(() => refresh(false), fast ? FAST_POLL : SLOW_POLL);
    }

    async function connect(w) {
      try {
        await send('cloud.connect', { id: w.id });
        toast(`Подключаемся к «${w.name}»`, 'success');
      } catch (err) {
        toast(errText(err), 'error');
      }
    }

    async function play(w) {
      if (w.status === 'running') return connect(w);
      connectWhenReady.add(w.id);
      render();
      try {
        await send('cloud.start', { id: w.id });
        refresh(false);
      } catch (err) {
        connectWhenReady.delete(w.id);
        render();
        toast(errText(err), 'error');
      }
    }

    async function stopWorld(w, btn) {
      btn.disabled = true;
      try {
        await send('cloud.stop', { id: w.id });
        toast(`«${w.name}» сохранён и уснул`, 'success');
        refresh(false);
      } catch (err) {
        btn.disabled = false;
        toast(errText(err), 'error');
      }
    }

    async function deleteWorld(w) {
      if (!confirmDelete.has(w.id)) {
        confirmDelete.add(w.id);
        render();
        setTimeout(() => { confirmDelete.delete(w.id); render(); }, 3000);
        return;
      }
      confirmDelete.delete(w.id);
      try {
        await send('cloud.delete', { id: w.id });
        openIds.delete(w.id);
        toast(`Мир «${w.name}» удалён`, 'info');
        refresh(false);
      } catch (err) {
        toast(errText(err), 'error');
      }
    }

    async function share(w, chip) {
      const nick = chip.dataset.nick;
      const add = !chip.classList.contains('on');
      chip.disabled = true;
      try {
        const res = await send('cloud.share', { id: w.id, nick, add });
        w.allowed = (res && res.allowed) || w.allowed;
        chip.classList.toggle('on', add);
        chip.textContent = (add ? '✓ ' : '+ ') + nick;
      } catch (err) {
        toast(errText(err), 'error');
      } finally {
        chip.disabled = false;
      }
    }

    // --- Выбор версии для облачного мира ---
    let selectedVersion = '1.21.1';
    if (versionSeg) {
      versionSeg.querySelectorAll('button').forEach(btn => btn.addEventListener('click', () => {
        versionSeg.querySelectorAll('button').forEach(b => b.classList.remove('on'));
        btn.classList.add('on');
        selectedVersion = btn.dataset.v || '1.21.1';
        if (window.updateSegmentedLens) window.updateSegmentedLens(versionSeg);
      }));
    }

    // --- Создать мир ---
    if (createBtn) createBtn.addEventListener('click', async () => {
      const name = newName.value.trim();
      if (!name) { newName.setAttribute('aria-invalid', 'true'); toast('Укажите название мира', 'error'); return; }
      createBtn.disabled = true;
      try {
        const res = await send('cloud.create', { name, seed: newSeed.value.trim(), version: selectedVersion });
        newName.value = ''; newSeed.value = '';
        document.getElementById('cloudCreateBox').classList.remove('open');
        toast(`Мир «${name}» создаётся`, 'success');
        if (res && res.worldId) connectWhenReady.add(res.worldId);
        refresh(false);
      } catch (err) {
        toast(errText(err), 'error');
      } finally {
        createBtn.disabled = false;
      }
    });
    if (newName) newName.addEventListener('input', () => newName.removeAttribute('aria-invalid'));

    // --- Загрузить свой мир ---
    if (uploadOpen) uploadOpen.addEventListener('click', async () => {
      uploadSelect.innerHTML = '<option>Загружаем список…</option>';
      try {
        const res = await send('worlds.local');
        const items = (res && res.worlds) || [];
        uploadSelect.innerHTML = items.length
          ? items.map(w => `<option value="${esc(w.folder)}">${esc(w.name)} · ${esc(w.size)}</option>`).join('')
          : '<option value="">Локальных миров нет</option>';
      } catch (err) {
        uploadSelect.innerHTML = `<option value="">${esc(errText(err))}</option>`;
      }
    });
    if (uploadBtn) uploadBtn.addEventListener('click', async () => {
      const folder = uploadSelect.value;
      if (!folder) return;
      uploadBtn.disabled = true;
      uploadStatus.textContent = 'Готовим мир…';
      try {
        const w = await send('cloud.upload', { folder });
        uploadStatus.textContent = '';
        document.getElementById('cloudUploadBox').classList.remove('open');
        toast(`Мир «${w && w.name}» загружен на сервер`, 'success');
        refresh(false);
      } catch (err) {
        uploadStatus.textContent = errText(err);
      } finally {
        uploadBtn.disabled = false;
      }
    });
    window.AuraBridge.on('cloud.uploadProgress', p => { if (p && p.text) uploadStatus.textContent = p.text; });

    if (layer) new MutationObserver(() => {
      if (layer.classList.contains('on') && tab === 'cloud') refresh(true); else clearTimeout(pollTimer);
    }).observe(layer, { attributes: true, attributeFilter: ['class'] });
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initCloud);
  } else {
    initCloud();
  }
})();

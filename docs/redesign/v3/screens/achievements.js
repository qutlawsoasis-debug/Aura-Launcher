/**
 * Aura Launcher v3 — достижения: витрина (открывается из Гардероба) и всплывашки при получении.
 */

(function () {
  'use strict';

  // Значки по id достижения; для новых — кубок
  const ICONS = {
    first_launch: '✨', ten_launches: '🏠', hundred_hours: '⏳', marathon: '🏃', night_shift: '🌙',
    first_lobby: '🏕️', full_table: '👥', reconnected: '🔌', first_friend: '🤝', five_friends: '🎉',
    first_backup: '💾', ten_backups: '🗄️', photographer: '📸', own_style: '🎨'
  };
  const icon = id => ICONS[id] || '🏆';

  function initAchievements() {
    const sheet = document.getElementById('achSheet');
    const grid = document.getElementById('achGrid');
    const sub = document.getElementById('achSub');
    const ringFg = document.getElementById('achRingFg');
    const ringText = document.getElementById('achRingText');
    const hint = document.getElementById('achHint');
    const pops = document.getElementById('achPops');
    if (!sheet || !window.AuraBridge) return;

    const esc = s => String(s == null ? '' : s).replace(/[&<>"']/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' }[c]));
    const fmt = n => (Math.round(n * 10) / 10).toString().replace('.', ',');

    async function load() {
      const res = await window.AuraBridge.send('achievements.get');
      if (hint && res) hint.textContent = `${res.unlocked} из ${res.total}`;
      return res;
    }

    function render(res) {
      const items = (res && res.items) || [];
      // Сначала полученные, потом самые близкие к получению
      items.sort((a, b) => (b.unlocked - a.unlocked) || (b.current / b.target - a.current / a.target));
      grid.innerHTML = items.map((a, i) => {
        const ratio = Math.max(0, Math.min(1, a.current / (a.target || 1)));
        return `<div class="ach${a.unlocked ? ' on' : ''}" style="--i:${i}">
          <div class="ach-icon">${a.unlocked ? icon(a.id) : '🔒'}</div>
          <div class="ach-body">
            <div class="ach-title">${esc(a.title)}</div>
            <div class="ach-desc">${esc(a.description)}</div>
            ${a.unlocked
              ? `<div class="ach-date">Получено ${esc(a.unlockedAt || '')}</div>`
              : (a.target > 1 ? `<div class="ach-prog"><i style="transform:scaleX(${ratio})"></i></div><div class="ach-desc">${fmt(a.current)} / ${a.target}</div>` : '')}
          </div>
        </div>`;
      }).join('');
      const total = res.total || items.length || 1;
      sub.textContent = `Получено ${res.unlocked} из ${total}`;
      ringText.textContent = res.unlocked;
      ringFg.style.strokeDashoffset = '119.4';
      requestAnimationFrame(() => requestAnimationFrame(() => {
        ringFg.style.strokeDashoffset = String(119.4 * (1 - res.unlocked / total));
      }));
    }

    async function open() {
      try {
        const res = await load();
        render(res);
        sheet.hidden = false;
        requestAnimationFrame(() => sheet.classList.add('show'));
      } catch (err) {
        if (typeof window.showToast === 'function') window.showToast((err && err.message) || 'Достижения недоступны', 'error');
      }
    }

    function close() {
      sheet.classList.remove('show');
      setTimeout(() => { sheet.hidden = true; }, 320);
    }

    function pop(a) {
      const el = document.createElement('div');
      el.className = 'ach-pop';
      el.innerHTML = `<div class="ach-icon">${icon(a.id)}</div>
        <div><div class="kicker">Достижение получено</div><div class="t">${esc(a.title)}</div><div class="d">${esc(a.description)}</div></div>`;
      el.addEventListener('click', () => { el.classList.add('out'); open(); });
      pops.appendChild(el);
      setTimeout(() => el.classList.add('out'), 5000);
      setTimeout(() => el.remove(), 5500);
      load().catch(() => {});
    }

    document.body.appendChild(sheet);
    document.getElementById('btnAchOpen').addEventListener('click', open);
    document.getElementById('achClose').addEventListener('click', close);
    sheet.addEventListener('click', e => { if (e.target === sheet) close(); });
    document.addEventListener('keydown', e => {
      if (e.key === 'Escape' && !sheet.hidden) { e.stopPropagation(); close(); }
    }, true);

    window.AuraBridge.on('achievement.unlocked', pop);
    load().catch(() => {});
  }

  if (document.readyState === 'loading') {
    document.addEventListener('DOMContentLoaded', initAchievements);
  } else {
    initAchievements();
  }
})();

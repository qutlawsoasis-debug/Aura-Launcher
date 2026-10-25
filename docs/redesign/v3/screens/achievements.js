/**
 * Aura Launcher v3 — достижения: витрина (открывается из Гардероба) и всплывашки при получении.
 */

(function () {
  'use strict';

  // Значки по id достижения; для новых — кубок. Только векторные иконки (DESIGN.md: эмодзи в UI запрещены)
  const svg = p => `<svg viewBox="0 0 24 24" fill="none" stroke="currentColor" stroke-width="1.8" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">${p}</svg>`;
  const ICONS = {
    first_launch: svg('<path d="M12 4l1.8 5.2L19 11l-5.2 1.8L12 18l-1.8-5.2L5 11l5.2-1.8L12 4z"/><path d="M18.5 16l.8 2.2 2.2.8-2.2.8-.8 2.2-.8-2.2-2.2-.8 2.2-.8.8-2.2z"/>'),
    ten_launches: svg('<path d="M4.5 10.5L12 4.5l7.5 6"/><path d="M6.5 9v10.5h11V9"/><path d="M10 19.5v-5h4v5"/>'),
    hundred_hours: svg('<path d="M7 3.5h10M7 20.5h10"/><path d="M8 3.5v2.5c0 3 2.5 4.5 4 6-1.5 1.5-4 3-4 6v2.5"/><path d="M16 3.5v2.5c0 3-2.5 4.5-4 6 1.5 1.5 4 3 4 6v2.5"/>'),
    marathon: svg('<path d="M6 21V3.5"/><path d="M6 4.5h10.5l-2.6 3.5 2.6 3.5H6"/>'),
    night_shift: svg('<path d="M20 13.2A8.2 8.2 0 1 1 10.8 4 6.4 6.4 0 0 0 20 13.2z"/>'),
    first_lobby: svg('<path d="M12 5L4 19.5h16L12 5z"/><path d="M12 12l-2.6 5.3h5.2L12 12z"/>'),
    full_table: svg('<circle cx="9" cy="8.5" r="3"/><path d="M3.5 19.5c.6-3 2.6-4.8 5.5-4.8s4.9 1.8 5.5 4.8"/><circle cx="16.8" cy="9.5" r="2.4"/><path d="M15.8 15c2.6.4 4.3 2 4.9 4.5"/>'),
    reconnected: svg('<path d="M20 12a8 8 0 1 1-2.3-5.7"/><path d="M20.5 3.5v4h-4"/>'),
    first_friend: svg('<circle cx="10" cy="8" r="3.4"/><path d="M4 19.5c.7-3.4 3-5.3 6-5.3 1.4 0 2.6.4 3.6 1.1"/><path d="M17.5 13.5v6M14.5 16.5h6"/>'),
    five_friends: svg('<path d="M9.5 4l1.5 4 4 1.5-4 1.5-1.5 4-1.5-4-4-1.5 4-1.5L9.5 4z"/><path d="M17 12.5l.9 2.3 2.3.9-2.3.9-.9 2.3-.9-2.3-2.3-.9 2.3-.9.9-2.3z"/><path d="M16.5 4.5l.6 1.6 1.6.6-1.6.6-.6 1.6-.6-1.6-1.6-.6 1.6-.6.6-1.6z"/>'),
    first_backup: svg('<path d="M4 8h16v11.5H4V8z"/><path d="M4 8l2-3.5h12L20 8"/><path d="M9.5 12.5h5"/>'),
    ten_backups: svg('<ellipse cx="12" cy="5.5" rx="7" ry="2.5"/><path d="M5 5.5v13c0 1.4 3.1 2.5 7 2.5s7-1.1 7-2.5v-13"/><path d="M5 12c0 1.4 3.1 2.5 7 2.5s7-1.1 7-2.5"/>'),
    photographer: svg('<rect x="3.5" y="7.5" width="17" height="11.5" rx="2"/><path d="M9 7.5l1.5-2.5h3L15 7.5"/><circle cx="12" cy="13" r="3.2"/>'),
    own_style: svg('<path d="M12 3.5a8.5 8.5 0 1 0 0 17c1.6 0 2.2-1.2 1.5-2.3-.9-1.4.2-2.7 1.7-2.7h1.8a4 4 0 0 0 4-4C21 7 17 3.5 12 3.5z"/><circle cx="8" cy="10" r=".9"/><circle cx="12" cy="7.5" r=".9"/><circle cx="16" cy="10" r=".9"/>'),
    trophy: svg('<path d="M8 4.5h8V10a4 4 0 0 1-8 0V4.5z"/><path d="M8 5.5H5a2.8 2.8 0 0 0 3 4.4M16 5.5h3a2.8 2.8 0 0 1-3 4.4"/><path d="M12 14v3.5"/><path d="M8.5 20.5h7l-.8-3h-5.4l-.8 3z"/>')
  };
  const ICON_LOCK = svg('<rect x="5.5" y="10.5" width="13" height="9" rx="2"/><path d="M8.5 10.5V8a3.5 3.5 0 0 1 7 0v2.5"/>');
  const icon = id => ICONS[id] || ICONS.trophy;

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
          <div class="ach-icon">${a.unlocked ? icon(a.id) : ICON_LOCK}</div>
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

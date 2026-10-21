/**
 * Полный обход интерфейса для tools/qa.js: каждое состояние капсулы, ховеры, клики,
 * серии кадров переходов. Скриншоты потом просматриваются глазами.
 */
module.exports = async function walkthrough(qa) {
  const { wait } = qa;
  const islandClip = async (pad = 24) => {
    const r = await qa.evaluate(`(() => { const b = document.getElementById('island').getBoundingClientRect();
      return { x: b.left, y: b.top, width: b.width, height: b.height }; })()`);
    return { x: Math.max(0, r.x - pad), y: Math.max(0, r.y - pad), width: r.width + pad * 2, height: r.height + pad * 2 };
  };
  const open = async section => { await qa.evaluate(`location.hash = '#/${section}'`); await wait(1100); };

  await qa.shot('idle');
  await qa.hover('#winControls');
  await wait(350);
  await qa.shot('wincontrols-hover', { x: 1366 - 140, y: 0, width: 140, height: 50 });
  await qa.hoverAt(683, 300);
  await wait(300);

  // Капсула: покой → меню по наведению
  await qa.frames('idle-to-nav', () => qa.hover('#island'), 6, 110, { x: 200, y: 600, width: 966, height: 168 });
  await wait(500);
  await qa.shot('nav', { x: 200, y: 600, width: 966, height: 168 });

  // Открытие раздела из меню
  await qa.frames('open-friends', () => qa.click('[data-l="nav"] [data-open="friends"]'), 8, 100);
  await wait(600);
  await qa.shot('friends');
  await qa.hover('#friendList .person:nth-child(2)');
  await wait(250);
  await qa.shot('friends-row-hover', await islandClip());
  await qa.frames('friends-filter', () => qa.click('[data-l="friends"] .seg button:nth-of-type(2)'), 7, 70, await islandClip());
  await wait(600);
  await qa.click('[data-l="friends"] .seg button:nth-of-type(1)');
  await wait(600);

  // Лобби
  await open('lobby');
  await qa.shot('lobby');
  await qa.click('#btnCopyLobbyCode');
  await wait(450);
  await qa.shot('lobby-copy-toasts');
  await wait(2500);
  await qa.click('#lobbySlotsContainer button.slot');
  await wait(1200);
  await qa.shot('lobby-slot-click');

  // Гардероб
  await open('wardrobe');
  await wait(900);
  await qa.shot('wardrobe');
  await qa.frames('cape-turn', () => qa.click('#capeSelector .cape[data-cape="silver"]'), 8, 110, await islandClip());
  await wait(2600);
  await qa.shot('wardrobe-after-turn', await islandClip());

  // Мастерская
  await open('workshop');
  await qa.shot('workshop');
  await qa.hover('[data-l="workshop"] .pick:nth-child(3)');
  await wait(250);
  await qa.shot('workshop-pick-hover', await islandClip());
  await qa.frames('workshop-tab', () => qa.click('[data-l="workshop"] .seg button:nth-of-type(2)'), 6, 80, await islandClip());
  await wait(500);

  // Настройки
  await open('settings');
  await qa.shot('settings');
  await qa.frames('settings-toggle', () => qa.click('[data-l="settings"] .toggle'), 6, 70, await islandClip());
  await qa.frames('settings-preset', () => qa.click('[data-l="settings"] .seg button:nth-of-type(1)'), 7, 80, await islandClip());
  await wait(400);

  // Карта
  await open('map');
  await wait(600);
  await qa.shot('map');
  await qa.hover('[data-l="map"] [data-poi="1"]');
  await wait(250);
  await qa.shot('map-list-hover', await islandClip());
  for (let i = 0; i < 5; i++) { await qa.click('#zout'); await wait(150); }
  await wait(1200);
  await qa.shot('map-zoomed-out');

  // Закрытие и запуск
  await qa.frames('close-esc', () => qa.key('Escape'), 7, 100);
  await wait(600);
  await qa.frames('play', () => qa.click('[data-l="idle"] [data-play]'), 6, 250, { x: 200, y: 600, width: 966, height: 168 });
  await wait(2500);
  await qa.shot('after-play', { x: 200, y: 600, width: 966, height: 168 });
};

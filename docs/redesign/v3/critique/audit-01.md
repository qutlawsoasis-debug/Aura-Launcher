# Результаты автоматического аудита интерфейса: audit-01.md

Дата запуска: 2026-10-20T13:55:30.082Z
Целевые экраны: play, lobby, friends, wardrobe, workshop, settings
Разрешения: 1920x1080, 1366x768

---

## Разрешение: 1920x1080

### Экран: `#/play`

- **Шрифты < 12px**: 12
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `div.profile-meta > div.profile-name-row > span.badge-fabric` (10px): "FABRIC 1.20.1"
  - `div.profile-quick-stats > div.stat-badge > span.stat-badge-label` (10px): "ВРЕМЯ В ИГРЕ"
  - `div.profile-quick-stats > div.stat-badge > span.stat-badge-label` (10px): "ЗАПУСКОВ"
  - `div.launch-hero-header > div.game-version-badge-group > span.hero-tag` (10px): "AURA PACK V1.2"
  - `aside.play-stories-rail.glass-island > div.stories-rail-header > span.stories-counter` (10px): "3 обновления"
  - `article.glass-subcard.story-card > div.story-tag-row > span.story-badge.accent` (10px): "СБОРКА"
  - `article.glass-subcard.story-card > div.story-tag-row > time.story-time` (10px): "Вчера, 18:30"
  - `article.glass-subcard.story-card > div.story-tag-row > span.story-badge.success` (10px): "СОБЫТИЕ"
  - `article.glass-subcard.story-card > div.story-tag-row > time.story-time` (10px): "Суббота, 19:00"
  - `article.glass-subcard.story-card > div.story-tag-row > span.story-badge` (10px): "СИСТЕМА"
  - `article.glass-subcard.story-card > div.story-tag-row > time.story-time` (10px): "14 окт"
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 8
  - `header.top-ribbon > div.brand-section > a.logo-aura` (106x32px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"
  - `#btnChangelog` (161x28px): "Список изменений"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"

### Экран: `#/lobby`

- **Шрифты < 12px**: 9
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `div.tunnel-info > div.tunnel-title-row > span.tunnel-ping-badge` (10px): "34
ms"
  - `#lobbyPingVal` (10px): "34"
  - `div.tunnel-info > div.tunnel-meta-row > span` (10px): "Playit.gg P2P • Anycast EU :25"
  - `div.room-card-header > div.room-header-left > span.room-badge-private` (10px): "ПРИВАТНОЕ ЛОББИ"
  - `div.room-code-banner > div.room-code-group > span.room-code-label` (10px): "КОД ПОДКЛЮЧЕНИЯ ДЛЯ ДРУЗЕЙ"
  - `#slotsCounterBadge` (10px): "1 / 8 игроков"
  - `div.slot-player-meta > div.slot-player-name-row > span.slot-host-badge` (10px): "ХОСТ"
  - `div.slot-player-meta > div.slot-player-status-row > span` (10px): "Готов к игре • 0 ms"
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 12
  - `header.top-ribbon > div.brand-section > a.logo-aura` (106x32px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"
  - `#btnRestartTunnel` (34x34px): "BUTTON"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"

### Экран: `#/friends`

- **Шрифты < 12px**: 14
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `div.my-friend-code-card > div.code-card-info > span.code-card-label` (10px): "МОЙ КОД ПРОФИЛЯ"
  - `#counterAll` (10px): "5"
  - `#counterInGame` (10px): "2"
  - `#counterOnline` (10px): "1"
  - `#counterOffline` (10px): "2"
  - `div.friend-info > div.friend-nick-row > span.friend-badge-mod` (10px): "Aura Pack v1.2"
  - `div.friend-info > div.friend-nick-row > span.friend-badge-mod` (10px): "Aura Pack v1.2"
  - `div.friend-info > div.friend-nick-row > span.friend-badge-mod` (10px): "Aura Pack v1.2"
  - `div.friend-info > div.friend-nick-row > span.friend-badge-mod` (10px): "Aura Pack v1.2"
  - `div.friend-info > div.friend-nick-row > span.friend-badge-mod` (10px): "Aura Pack v1.2"
  - `#requestsCountBadge` (10px): "2"
  - `div.request-user-info > div.request-names > span.request-code` (10px): "ID: #9240"
  - `div.request-user-info > div.request-names > span.request-code` (10px): "ID: #1184"
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 12
  - `header.top-ribbon > div.brand-section > a.logo-aura` (106x32px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"
  - `div.toolbar-left > div.status-filter-pills > button.filter-pill.active` (71x32px): "Все
5"
  - `div.toolbar-left > div.status-filter-pills > button.filter-pill` (100x32px): "В игре
2"
  - `div.toolbar-left > div.status-filter-pills > button.filter-pill` (98x32px): "В сети
1"
  - `div.toolbar-left > div.status-filter-pills > button.filter-pill` (117x32px): "Не в сети
2"
  - `div.request-item > div.request-actions > button.btn-req-accept` (32x32px): "Принять заявку"
  - `div.request-item > div.request-actions > button.btn-req-decline` (32x32px): "Отклонить заявку"
  - `div.request-item > div.request-actions > button.btn-req-accept` (32x32px): "Принять заявку"
  - `div.request-item > div.request-actions > button.btn-req-decline` (32x32px): "Отклонить заявку"

### Экран: `#/wardrobe`

- **Шрифты < 12px**: 11
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `div.wardrobe-player-badge > div.player-badge-meta > span.player-badge-id` (10px): "ID: AURA-7821-X9M2"
  - `div.wardrobe-top-row > div.wardrobe-player-badge > span.badge-tag` (10px): "Aura Founder"
  - `#hudAngleBadge` (10px): "19°"
  - `#hudCapeBadge` (10px): "Плащ Aura Founder"
  - `div.profile-edit-fields > div.form-field-group > span.field-hint` (10px): "От 3 до 16 символов (латиница,"
  - `div.profile-id-row > button.btn-copy-id > span.btn-copy-label` (10px): "Копировать"
  - `div.segment-pill-control > button.segment-option.active > span.segment-radio-sub` (10px): "Широкие руки (4 пикселя)"
  - `div.segment-pill-control > button.segment-option > span.segment-radio-sub` (10px): "Тонкие руки (3 пикселя)"
  - `#capeStatusTag` (10px): "Активен: Founder"
  - `div.glass-island > div.skin-format-note > span` (10px): "Поддерживаются форматы PNG 64×"
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 10
  - `header.top-ribbon > div.brand-section > a.logo-aura` (106x32px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"
  - `#btnToggleAutoRotate` (78x32px): "Авто"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle.active` (86x32px): "Спереди"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (53x32px): "3/4"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (70x32px): "Сбоку"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (70x32px): "Сзади"
  - `#btnCopyProfileId` (104x28px): "Копировать"

### Экран: `#/workshop`

- **Шрифты < 12px**: 48
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `#modCountBadge` (11px): "15 из 15 активно"
  - `#countAll` (10px): "15"
  - `#countOpt` (10px): "6"
  - `#countGfx` (10px): "5"
  - `#countGame` (10px): "4"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v0.5.13"
  - `div.mod-meta-block > div.mod-title-row > span.mod-badge-core` (10px): "БАЗОВЫЙ"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v1.7.6"
  - `div.mod-meta-block > div.mod-title-row > span.mod-badge-core` (10px): "БАЗОВЫЙ"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Графика"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v6.0.1"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v0.11.4"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v1.5.1"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Геймплей"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v2.5.2"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Геймплей"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v5.9.18"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Геймплей"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v2.4.1"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v1.5.5"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v1.11.2"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v6.3.9"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Геймплей"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v3.0.0"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Графика"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v3.3.11"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Графика"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v7.2.5"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Графика"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v2.15.2"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Графика"
  - `div.glass-island.elevated > div.glass-island-header > span.shader-engine-badge` (10px): "Sodium Core"
  - `div.shader-card-meta > div.shader-name-row > span.shader-tag.recommended` (10px): "Выбор Aura"
  - `div.shader-card-meta > div.shader-name-row > span.shader-tag` (10px): "Легковесный"
  - `div.shader-card-meta > div.shader-name-row > span.shader-tag` (10px): "Макс. FPS"
  - `div.shader-card-meta > div.shader-name-row > span.shader-tag.vanilla` (10px): "Vanilla"
  - `div.glass-island.build-info-island > div.glass-island-header > span.status-indicator-badge.live` (10px): "Синхронизировано"
  - `div.build-stats-grid > div.build-stat-box > span.build-stat-label` (10px): "МАНИФЕСТ"
  - `div.build-stats-grid > div.build-stat-box > span.build-stat-label` (10px): "ЦЕЛОСТНОСТЬ"
  - `div.build-stats-grid > div.build-stat-box > span.build-stat-label` (10px): "РАЗМЕР ФАЙЛОВ"
  - `div.build-stats-grid > div.build-stat-box > span.build-stat-label` (10px): "ЗАГРУЗЧИК"
- **Контраст < 4.5:1**: 6
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Настройки"
  - `#countAll` (контраст 3.47:1, fg: rgb(242, 166, 60)): "15"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 4
  - `header.top-ribbon > div.brand-section > a.logo-aura` (106x32px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"

### Экран: `#/settings`

- **Шрифты < 12px**: 23
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `div.glass-island.elevated > div.glass-island-header > span.settings-section-badge` (10px): "СБОРКА AURA"
  - `div.ram-slider-wrapper > div.ram-range-scale > span` (10px): "4 ГБ"
  - `div.ram-slider-wrapper > div.ram-range-scale > span` (10px): "6 ГБ"
  - `div.ram-slider-wrapper > div.ram-range-scale > span` (10px): "8 ГБ"
  - `div.ram-slider-wrapper > div.ram-range-scale > span` (10px): "10 ГБ"
  - `div.ram-slider-wrapper > div.ram-range-scale > span` (10px): "12 ГБ"
  - `div.preset-card > div.preset-top-row > span.preset-badge` (10px): "60+ FPS"
  - `div.preset-card > div.preset-specs > span` (10px): "Дальность: 8 чанков"
  - `div.preset-card > div.preset-specs > span` (10px): "Шейдеры: Выкл"
  - `div.preset-card > div.preset-specs > span` (10px): "Частицы: Минимум"
  - `div.preset-card > div.preset-top-row > span.preset-badge` (10px): "Баланс"
  - `div.preset-card > div.preset-specs > span` (10px): "Дальность: 12 чанков"
  - `div.preset-card > div.preset-specs > span` (10px): "Шейдеры: Builders QoL"
  - `div.preset-card > div.preset-specs > span` (10px): "Мягкий свет: Вкл"
  - `div.preset-card.active > div.preset-top-row > span.preset-badge.recommended` (10px): "Рекомендуемый"
  - `div.preset-card.active > div.preset-specs > span` (10px): "Дальность: 16 чанков"
  - `div.preset-card.active > div.preset-specs > span` (10px): "Шейдеры: Sildurs / Fancy"
  - `div.preset-card.active > div.preset-specs > span` (10px): "PBR-текстуры: Вкл"
  - `div.preset-card > div.preset-top-row > span.preset-badge` (10px): "Кинокачество"
  - `div.preset-card > div.preset-specs > span` (10px): "Дальность: 24 чанка"
  - `div.preset-card > div.preset-specs > span` (10px): "Шейдеры: Complementary"
  - `div.preset-card > div.preset-specs > span` (10px): "Distant Horizons: Вкл"
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Мастерская"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 5
  - `header.top-ribbon > div.brand-section > a.logo-aura` (106x32px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"
  - `#ramSliderInput` (1358x6px): "Ползунок выделения оперативной памяти в ГБ"

## Разрешение: 1366x768

### Экран: `#/play`

- **Шрифты < 12px**: 12
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `div.profile-meta > div.profile-name-row > span.badge-fabric` (10px): "FABRIC 1.20.1"
  - `div.profile-quick-stats > div.stat-badge > span.stat-badge-label` (10px): "ВРЕМЯ В ИГРЕ"
  - `div.profile-quick-stats > div.stat-badge > span.stat-badge-label` (10px): "ЗАПУСКОВ"
  - `div.launch-hero-header > div.game-version-badge-group > span.hero-tag` (10px): "AURA PACK V1.2"
  - `aside.play-stories-rail.glass-island > div.stories-rail-header > span.stories-counter` (10px): "3 обновления"
  - `article.glass-subcard.story-card > div.story-tag-row > span.story-badge.accent` (10px): "СБОРКА"
  - `article.glass-subcard.story-card > div.story-tag-row > time.story-time` (10px): "Вчера, 18:30"
  - `article.glass-subcard.story-card > div.story-tag-row > span.story-badge.success` (10px): "СОБЫТИЕ"
  - `article.glass-subcard.story-card > div.story-tag-row > time.story-time` (10px): "Суббота, 19:00"
  - `article.glass-subcard.story-card > div.story-tag-row > span.story-badge` (10px): "СИСТЕМА"
  - `article.glass-subcard.story-card > div.story-tag-row > time.story-time` (10px): "14 окт"
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 8
  - `header.top-ribbon > div.brand-section > a.logo-aura` (97x28px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"
  - `#btnChangelog` (161x28px): "Список изменений"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"

### Экран: `#/lobby`

- **Шрифты < 12px**: 9
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `div.tunnel-info > div.tunnel-title-row > span.tunnel-ping-badge` (10px): "33
ms"
  - `#lobbyPingVal` (10px): "33"
  - `div.tunnel-info > div.tunnel-meta-row > span` (10px): "Playit.gg P2P • Anycast EU :25"
  - `div.room-card-header > div.room-header-left > span.room-badge-private` (10px): "ПРИВАТНОЕ ЛОББИ"
  - `div.room-code-banner > div.room-code-group > span.room-code-label` (10px): "КОД ПОДКЛЮЧЕНИЯ ДЛЯ ДРУЗЕЙ"
  - `#slotsCounterBadge` (10px): "1 / 8 игроков"
  - `div.slot-player-meta > div.slot-player-name-row > span.slot-host-badge` (10px): "ХОСТ"
  - `div.slot-player-meta > div.slot-player-status-row > span` (10px): "Готов к игре • 0 ms"
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 12
  - `header.top-ribbon > div.brand-section > a.logo-aura` (97x28px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"
  - `#btnRestartTunnel` (34x34px): "BUTTON"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"

### Экран: `#/friends`

- **Шрифты < 12px**: 14
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `div.my-friend-code-card > div.code-card-info > span.code-card-label` (10px): "МОЙ КОД ПРОФИЛЯ"
  - `#counterAll` (10px): "5"
  - `#counterInGame` (10px): "2"
  - `#counterOnline` (10px): "1"
  - `#counterOffline` (10px): "2"
  - `div.friend-info > div.friend-nick-row > span.friend-badge-mod` (10px): "Aura Pack v1.2"
  - `div.friend-info > div.friend-nick-row > span.friend-badge-mod` (10px): "Aura Pack v1.2"
  - `div.friend-info > div.friend-nick-row > span.friend-badge-mod` (10px): "Aura Pack v1.2"
  - `div.friend-info > div.friend-nick-row > span.friend-badge-mod` (10px): "Aura Pack v1.2"
  - `div.friend-info > div.friend-nick-row > span.friend-badge-mod` (10px): "Aura Pack v1.2"
  - `#requestsCountBadge` (10px): "2"
  - `div.request-user-info > div.request-names > span.request-code` (10px): "ID: #9240"
  - `div.request-user-info > div.request-names > span.request-code` (10px): "ID: #1184"
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 27
  - `header.top-ribbon > div.brand-section > a.logo-aura` (97x28px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"
  - `#btnCopyFriendCode` (72x36px): "Код"
  - `#btnCopyFriendLink` (94x36px): "Ссылка"
  - `#friendsSearchInput` (240x36px): "Поиск по нику"
  - `div.toolbar-left > div.status-filter-pills > button.filter-pill.active` (71x32px): "Все
5"
  - `div.toolbar-left > div.status-filter-pills > button.filter-pill` (100x32px): "В игре
2"
  - `div.toolbar-left > div.status-filter-pills > button.filter-pill` (98x32px): "В сети
1"
  - `div.toolbar-left > div.status-filter-pills > button.filter-pill` (117x32px): "Не в сети
2"
  - `#addFriendCodeInput` (190x36px): "Код друга"
  - `#btnAddFriendSubmit` (118x36px): "Добавить"
  - `div.friend-card > div.friend-card-actions > button.btn-action-invite.btn-invite-trigger` (176x36px): "Пригласить в лобби"
  - `div.friend-card > div.friend-card-actions > button.btn-action-profile.btn-profile-trigger` (111x36px): "Профиль"
  - `div.friend-card > div.friend-card-actions > button.btn-action-invite.btn-invite-trigger` (176x36px): "Пригласить в лобби"
  - `div.friend-card > div.friend-card-actions > button.btn-action-profile.btn-profile-trigger` (111x36px): "Профиль"
  - `div.friend-card > div.friend-card-actions > button.btn-action-invite.btn-invite-trigger` (176x36px): "Пригласить в лобби"
  - `div.friend-card > div.friend-card-actions > button.btn-action-profile.btn-profile-trigger` (111x36px): "Профиль"
  - `div.friend-card > div.friend-card-actions > button.btn-action-invite` (129x36px): "Пригласить"
  - `div.friend-card > div.friend-card-actions > button.btn-action-profile.btn-profile-trigger` (111x36px): "Профиль"
  - `div.friend-card > div.friend-card-actions > button.btn-action-invite` (129x36px): "Пригласить"
  - `div.friend-card > div.friend-card-actions > button.btn-action-profile.btn-profile-trigger` (111x36px): "Профиль"
  - `div.request-item > div.request-actions > button.btn-req-accept` (32x32px): "Принять заявку"
  - `div.request-item > div.request-actions > button.btn-req-decline` (32x32px): "Отклонить заявку"
  - `div.request-item > div.request-actions > button.btn-req-accept` (32x32px): "Принять заявку"
  - `div.request-item > div.request-actions > button.btn-req-decline` (32x32px): "Отклонить заявку"

### Экран: `#/wardrobe`

- **Шрифты < 12px**: 11
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `div.wardrobe-player-badge > div.player-badge-meta > span.player-badge-id` (10px): "ID: AURA-7821-X9M2"
  - `div.wardrobe-top-row > div.wardrobe-player-badge > span.badge-tag` (10px): "Aura Founder"
  - `#hudAngleBadge` (10px): "50°"
  - `#hudCapeBadge` (10px): "Плащ Aura Founder"
  - `div.profile-edit-fields > div.form-field-group > span.field-hint` (10px): "От 3 до 16 символов (латиница,"
  - `div.profile-id-row > button.btn-copy-id > span.btn-copy-label` (10px): "Копировать"
  - `div.segment-pill-control > button.segment-option.active > span.segment-radio-sub` (10px): "Широкие руки (4 пикселя)"
  - `div.segment-pill-control > button.segment-option > span.segment-radio-sub` (10px): "Тонкие руки (3 пикселя)"
  - `#capeStatusTag` (10px): "Активен: Founder"
  - `div.glass-island > div.skin-format-note > span` (10px): "Поддерживаются форматы PNG 64×"
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 11
  - `header.top-ribbon > div.brand-section > a.logo-aura` (97x28px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"
  - `#btnToggleAutoRotate` (78x32px): "Авто"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle.active` (86x32px): "Спереди"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (53x32px): "3/4"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (70x32px): "Сбоку"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (70x32px): "Сзади"
  - `#btnCopyProfileId` (104x28px): "Копировать"
  - `#btnTriggerSkinUpload` (207x36px): "Загрузить скин (.png"

### Экран: `#/workshop`

- **Шрифты < 12px**: 48
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `#modCountBadge` (11px): "15 из 15 активно"
  - `#countAll` (10px): "15"
  - `#countOpt` (10px): "6"
  - `#countGfx` (10px): "5"
  - `#countGame` (10px): "4"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v0.5.13"
  - `div.mod-meta-block > div.mod-title-row > span.mod-badge-core` (10px): "БАЗОВЫЙ"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v1.7.6"
  - `div.mod-meta-block > div.mod-title-row > span.mod-badge-core` (10px): "БАЗОВЫЙ"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Графика"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v6.0.1"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v0.11.4"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v1.5.1"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Геймплей"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v2.5.2"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Геймплей"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v5.9.18"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Геймплей"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v2.4.1"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v1.5.5"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v1.11.2"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Оптимизация"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v6.3.9"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Геймплей"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v3.0.0"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Графика"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v3.3.11"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Графика"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v7.2.5"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Графика"
  - `div.mod-meta-block > div.mod-title-row > span.mod-version-tag` (10px): "v2.15.2"
  - `div.mod-item-card > div.mod-item-right > span.mod-category-badge` (10px): "Графика"
  - `div.glass-island.elevated > div.glass-island-header > span.shader-engine-badge` (10px): "Sodium Core"
  - `div.shader-card-meta > div.shader-name-row > span.shader-tag.recommended` (10px): "Выбор Aura"
  - `div.shader-card-meta > div.shader-name-row > span.shader-tag` (10px): "Легковесный"
  - `div.shader-card-meta > div.shader-name-row > span.shader-tag` (10px): "Макс. FPS"
  - `div.shader-card-meta > div.shader-name-row > span.shader-tag.vanilla` (10px): "Vanilla"
  - `div.glass-island.build-info-island > div.glass-island-header > span.status-indicator-badge.live` (10px): "Синхронизировано"
  - `div.build-stats-grid > div.build-stat-box > span.build-stat-label` (10px): "МАНИФЕСТ"
  - `div.build-stats-grid > div.build-stat-box > span.build-stat-label` (10px): "ЦЕЛОСТНОСТЬ"
  - `div.build-stats-grid > div.build-stat-box > span.build-stat-label` (10px): "РАЗМЕР ФАЙЛОВ"
  - `div.build-stats-grid > div.build-stat-box > span.build-stat-label` (10px): "ЗАГРУЗЧИК"
- **Контраст < 4.5:1**: 6
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Настройки"
  - `#countAll` (контраст 3.47:1, fg: rgb(242, 166, 60)): "15"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 8
  - `header.top-ribbon > div.brand-section > a.logo-aura` (97x28px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"
  - `div.workshop-controls-row > div.workshop-category-tabs > button.category-pill-tab.active` (118x36px): "Все моды
15"
  - `div.workshop-controls-row > div.workshop-category-tabs > button.category-pill-tab` (138x36px): "Оптимизация
6"
  - `div.workshop-controls-row > div.workshop-category-tabs > button.category-pill-tab` (173x36px): "Графика и шейдеры
5"
  - `div.workshop-controls-row > div.workshop-category-tabs > button.category-pill-tab` (153x36px): "Геймплей и звук
4"

### Экран: `#/settings`

- **Шрифты < 12px**: 23
  - `div.brand-section > a.logo-aura > span.logo-badge` (10px): "V3"
  - `div.glass-island.elevated > div.glass-island-header > span.settings-section-badge` (10px): "СБОРКА AURA"
  - `div.ram-slider-wrapper > div.ram-range-scale > span` (10px): "4 ГБ"
  - `div.ram-slider-wrapper > div.ram-range-scale > span` (10px): "6 ГБ"
  - `div.ram-slider-wrapper > div.ram-range-scale > span` (10px): "8 ГБ"
  - `div.ram-slider-wrapper > div.ram-range-scale > span` (10px): "10 ГБ"
  - `div.ram-slider-wrapper > div.ram-range-scale > span` (10px): "12 ГБ"
  - `div.preset-card > div.preset-top-row > span.preset-badge` (10px): "60+ FPS"
  - `div.preset-card > div.preset-specs > span` (10px): "Дальность: 8 чанков"
  - `div.preset-card > div.preset-specs > span` (10px): "Шейдеры: Выкл"
  - `div.preset-card > div.preset-specs > span` (10px): "Частицы: Минимум"
  - `div.preset-card > div.preset-top-row > span.preset-badge` (10px): "Баланс"
  - `div.preset-card > div.preset-specs > span` (10px): "Дальность: 12 чанков"
  - `div.preset-card > div.preset-specs > span` (10px): "Шейдеры: Builders QoL"
  - `div.preset-card > div.preset-specs > span` (10px): "Мягкий свет: Вкл"
  - `div.preset-card.active > div.preset-top-row > span.preset-badge.recommended` (10px): "Рекомендуемый"
  - `div.preset-card.active > div.preset-specs > span` (10px): "Дальность: 16 чанков"
  - `div.preset-card.active > div.preset-specs > span` (10px): "Шейдеры: Sildurs / Fancy"
  - `div.preset-card.active > div.preset-specs > span` (10px): "PBR-текстуры: Вкл"
  - `div.preset-card > div.preset-top-row > span.preset-badge` (10px): "Кинокачество"
  - `div.preset-card > div.preset-specs > span` (10px): "Дальность: 24 чанка"
  - `div.preset-card > div.preset-specs > span` (10px): "Шейдеры: Complementary"
  - `div.preset-card > div.preset-specs > span` (10px): "Distant Horizons: Вкл"
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.07:1, fg: rgba(245, 248, 255, 0.72)): "Мастерская"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 5
  - `header.top-ribbon > div.brand-section > a.logo-aura` (97x28px): "Aura
V3"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Свернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn` (34x34px): "Развернуть окно"
  - `div.top-actions > div.win-controls > button.win-btn.close` (34x34px): "Закрыть приложение"
  - `#ramSliderInput` (830x6px): "Ползунок выделения оперативной памяти в ГБ"

---

## Итого нарушений: 430

# Результаты автоматического аудита интерфейса: audit-02.md

Дата запуска: 2026-10-20T14:47:04.510Z
Целевые экраны: play, lobby, friends, wardrobe, workshop, settings
Разрешения: 1920x1080, 1366x768

---

## Разрешение: 1920x1080

### Экран: `#/play`

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 5
  - `header.top-ribbon > div.brand-section > a.logo-aura` (71x32px): "Aura"
  - `#btnChangelog` (161x28px): "Список изменений"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"

### Экран: `#/lobby`

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 9
  - `header.top-ribbon > div.brand-section > a.logo-aura` (71x32px): "Aura"
  - `#btnRestartTunnel` (38x38px): "BUTTON"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"

### Экран: `#/friends`

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 6
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Настройки"
  - `#counterAll` (контраст 3.61:1, fg: rgb(242, 166, 60)): "5"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 5
  - `header.top-ribbon > div.brand-section > a.logo-aura` (71x32px): "Aura"
  - `div.request-item > div.request-actions > button.btn-req-accept` (32x32px): "Принять заявку"
  - `div.request-item > div.request-actions > button.btn-req-decline` (32x32px): "Отклонить заявку"
  - `div.request-item > div.request-actions > button.btn-req-accept` (32x32px): "Принять заявку"
  - `div.request-item > div.request-actions > button.btn-req-decline` (32x32px): "Отклонить заявку"

### Экран: `#/wardrobe`

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 7
  - `header.top-ribbon > div.brand-section > a.logo-aura` (71x32px): "Aura"
  - `#btnToggleAutoRotate` (78x32px): "Авто"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle.active` (86x32px): "Спереди"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (53x32px): "3/4"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (70x32px): "Сбоку"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (70x32px): "Сзади"
  - `#btnCopyProfileId` (116x28px): "Копировать"

### Экран: `#/workshop`

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 8
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Настройки"
  - `div.workshop-category-tabs > button.category-pill-tab.active > span` (контраст 4.45:1, fg: rgb(242, 166, 60)): "Все моды"
  - `#countAll` (контраст 2.83:1, fg: rgb(242, 166, 60)): "15"
  - `div.shader-card-meta > div.shader-name-row > span.shader-tag.recommended` (контраст 4.03:1, fg: rgb(242, 166, 60)): "Выбор Aura"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 1
  - `header.top-ribbon > div.brand-section > a.logo-aura` (71x32px): "Aura"

### Экран: `#/settings`

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 6
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Мастерская"
  - `div.preset-card.active > div.preset-top-row > span.preset-badge.recommended` (контраст 4.03:1, fg: rgb(242, 166, 60)): "Рекомендуемый"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 2037px > 1920px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 2
  - `header.top-ribbon > div.brand-section > a.logo-aura` (71x32px): "Aura"
  - `#ramSliderInput` (670x6px): "Ползунок выделения оперативной памяти в ГБ"

## Разрешение: 1366x768

### Экран: `#/play`

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 5
  - `header.top-ribbon > div.brand-section > a.logo-aura` (62x28px): "Aura"
  - `#btnChangelog` (161x28px): "Список изменений"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"
  - `div.stories-cards-list > article.glass-subcard.story-card > button.btn-story-more` (89x18px): "Подробнее"

### Экран: `#/lobby`

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 8
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Настройки"
  - `div.room-code-group > div.room-code-display > span.code-segment` (контраст 4.49:1, fg: rgb(242, 166, 60)): "AURA"
  - `div.room-code-group > div.room-code-display > span.code-segment` (контраст 4.49:1, fg: rgb(242, 166, 60)): "7821"
  - `div.room-code-group > div.room-code-display > span.code-segment` (контраст 4.49:1, fg: rgb(242, 166, 60)): "X9M2"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 8
  - `header.top-ribbon > div.brand-section > a.logo-aura` (62x28px): "Aura"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"
  - `div.player-slot-card.empty > div.slot-empty-meta > button.btn-invite-slot` (127x18px): "Пригласить друга"

### Экран: `#/friends`

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 6
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Настройки"
  - `#counterAll` (контраст 3.61:1, fg: rgb(242, 166, 60)): "5"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 20
  - `header.top-ribbon > div.brand-section > a.logo-aura` (62x28px): "Aura"
  - `#btnCopyFriendCode` (72x36px): "Код"
  - `#btnCopyFriendLink` (94x36px): "Ссылка"
  - `#friendsSearchInput` (240x36px): "Поиск по нику"
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

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 5
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Мастерская"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Настройки"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 8
  - `header.top-ribbon > div.brand-section > a.logo-aura` (62x28px): "Aura"
  - `#btnToggleAutoRotate` (78x32px): "Авто"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle.active` (86x32px): "Спереди"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (53x32px): "3/4"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (70x32px): "Сбоку"
  - `div.stage-footer > div.quick-angle-buttons > button.btn-angle` (70x32px): "Сзади"
  - `#btnCopyProfileId` (116x28px): "Копировать"
  - `#btnTriggerSkinUpload` (207x36px): "Загрузить скин (.png"

### Экран: `#/workshop`

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 8
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Настройки"
  - `div.workshop-category-tabs > button.category-pill-tab.active > span` (контраст 4.45:1, fg: rgb(242, 166, 60)): "Все моды"
  - `#countAll` (контраст 2.83:1, fg: rgb(242, 166, 60)): "15"
  - `div.shader-card-meta > div.shader-name-row > span.shader-tag.recommended` (контраст 4.03:1, fg: rgb(242, 166, 60)): "Выбор Aura"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 5
  - `header.top-ribbon > div.brand-section > a.logo-aura` (62x28px): "Aura"
  - `div.workshop-controls-row > div.workshop-category-tabs > button.category-pill-tab.active` (120x36px): "Все моды
15"
  - `div.workshop-controls-row > div.workshop-category-tabs > button.category-pill-tab` (139x36px): "Оптимизация
6"
  - `div.workshop-controls-row > div.workshop-category-tabs > button.category-pill-tab` (174x36px): "Графика и шейдеры
5"
  - `div.workshop-controls-row > div.workshop-category-tabs > button.category-pill-tab` (154x36px): "Геймплей и звук
4"

### Экран: `#/settings`

- **Шрифты < 12px**: 0
- **Контраст < 4.5:1**: 6
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Играть"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Лобби"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Друзья"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Гардероб"
  - `nav.nav-pill-dock > button.nav-tab > span` (контраст 1.14:1, fg: rgba(255, 255, 255, 0.88)): "Мастерская"
  - `div.preset-card.active > div.preset-top-row > span.preset-badge.recommended` (контраст 4.03:1, fg: rgb(242, 166, 60)): "Рекомендуемый"
- **Элементы за границей вьюпорта**: 1
  - `#worldBg` (right: 1449px > 1366px)
- **Разрыв backdrop-filter у предков .glass-panel**: 0
- **Кликабельные зоны < 38px**: 2
  - `header.top-ribbon > div.brand-section > a.logo-aura` (62x28px): "Aura"
  - `#ramSliderInput` (670x6px): "Ползунок выделения оперативной памяти в ГБ"

---

## Итого нарушений: 162

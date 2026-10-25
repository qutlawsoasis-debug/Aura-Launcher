/**
 * Aura Launcher v3 — Демонстрационные данные (DEMO DATA)
 * Все тестовые сущности изолированы в этом файле с явной пометкой // DEMO.
 * Единый источник правды для чисел, версий и идентификаторов прототипа.
 */

window.AURA_DEMO_DATA = (function () {
  'use strict';

  return {
    isDemo: true, // DEMO: флаг тестового окружения

    // 1. Профиль активного игрока
    profile: {
      nickname: 'Coock0ld', // Реальный профиль лаунчера
      status: 'online',
      accountType: 'Aura Core',
      stats: {
        timePlayed: '42 ч 18 мин', // DEMO: статистика наигранного времени
        launchesCount: 64          // DEMO: счётчик запусков
      }
    },

    // 2. Параметры игры и модпака (строго по Aura-Pack)
    game: {
      minecraftVersion: '1.20.1',
      fabricVersion: '0.19.5',
      packVersion: 'v1.2',
      modsCount: 31,              // Сверено с C:\Users\magne\Documents\GitHub\Aura-Pack\manifest.json
      allocatedRamGb: 6,
      minRamGb: 4,
      maxRamGb: 12,
      javaRuntime: 'Java 17 Temurin',
      defaultGamePath: '%APPDATA%\\.aura'
    },

    // 3. Сетевое лобби (Playit P2P)
    lobby: {
      code: 'AURA-7731-PLAY',     // Формат реального лобби AURA-XXXX-XXXX
      region: 'EU Anycast',
      pingMs: 34,
      maxSlots: 8
    },

    // 4. Демонстрационный список друзей (соответствует IFriendService)
    // DEMO: mock data for prototype rendering
    friends: [
      {
        id: 'f-1',
        nick: 'Nursultan_Dev',
        status: 'in-game',
        activity: 'Aura Pack 1.20.1 • В мире',
        friendCode: 'AURA-4819-NURS',
        pingMs: 24,
        invited: false
      },
      {
        id: 'f-2',
        nick: 'CelestialPVP',
        status: 'in-game',
        activity: 'Aura Pack 1.20.1 • В лобби',
        friendCode: 'AURA-9921-CELE',
        pingMs: 38,
        invited: false
      },
      {
        id: 'f-3',
        nick: 'AuraMaster',
        status: 'online',
        activity: 'В лаунчере',
        friendCode: 'AURA-3312-MAST',
        pingMs: 19,
        invited: false
      },
      {
        id: 'f-4',
        nick: 'Steve_Miner',
        status: 'offline',
        activity: 'Был в сети вчера',
        friendCode: 'AURA-7714-STEV',
        pingMs: 0,
        invited: false
      },
      {
        id: 'f-5',
        nick: 'Alex_Explorer',
        status: 'offline',
        activity: 'Был в сети 3 дня назад',
        friendCode: 'AURA-1209-ALEX',
        pingMs: 0,
        invited: false
      }
    ],

    // 5. Входящие запросы в друзья
    // DEMO: mock friend requests
    incomingRequests: [
      {
        id: 'req-1',
        fromNick: 'Kitsune_7',
        fromCode: '#9240',
        mutualFriends: 2,
        timeAgo: '15 минут назад'
      },
      {
        id: 'req-2',
        fromNick: 'DistantGamer',
        fromCode: '#1184',
        mutualFriends: 1,
        timeAgo: '2 часа назад'
      }
    ],

    // 6. Реальные обновления лаунчера (вместо выдуманных рейдов)
    updates: [
      {
        id: 'upd-1',
        type: 'Сборка',
        tagClass: 'accent',
        time: 'Вчера, 18:30',
        title: 'Обновление Aura-Pack v1.2',
        text: 'Синхронизация Fabric 0.19.5, обновление Sodium 0.5.11 и Iris 1.7.5. Улучшена стабильность генерации чанков.'
      },
      {
        id: 'upd-2',
        type: 'Шейдеры',
        tagClass: 'success',
        time: '18 окт',
        title: 'Поддержка Complementary Shaders v5',
        text: 'Оптимизирован профиль освещения и рендеринг воды под видеокарты AMD и NVIDIA.'
      },
      {
        id: 'upd-3',
        type: 'Система',
        tagClass: '',
        time: '14 окт',
        title: 'Локальный рендеринг скинов',
        text: 'Интеграция прямого предпросмотра моделей Steve (4px) и Alex (3px) на базе WebGL/Canvas.'
      }
    ]
  };
})();

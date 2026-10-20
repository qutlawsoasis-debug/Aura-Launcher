const fs = require('fs');
const path = require('path');

const baseDir = path.join(__dirname);
const screensDir = path.join(baseDir, 'screens');

const playHtml = fs.readFileSync(path.join(screensDir, 'play.html'), 'utf-8');
const lobbyHtml = fs.readFileSync(path.join(screensDir, 'lobby.html'), 'utf-8');
const friendsHtml = fs.readFileSync(path.join(screensDir, 'friends.html'), 'utf-8');
const wardrobeHtml = fs.readFileSync(path.join(screensDir, 'wardrobe.html'), 'utf-8');
const workshopHtml = fs.readFileSync(path.join(screensDir, 'workshop.html'), 'utf-8');
const settingsHtml = fs.readFileSync(path.join(screensDir, 'settings.html'), 'utf-8');

const template = `<!DOCTYPE html>
<html lang="ru" data-effects="full">
<head>
  <meta charset="UTF-8">
  <meta name="viewport" content="width=device-width, initial-scale=1.0">
  <title>Aura Launcher v3 — Next-Gen Client Shell</title>

  <!-- Дизайн-токены и ядро стилей -->
  <link rel="stylesheet" href="tokens.css">
  <link rel="stylesheet" href="assets/css/core.css">

  <!-- Стили модулей экранов -->
  <link rel="stylesheet" href="screens/play.css">
  <link rel="stylesheet" href="screens/lobby.css">
  <link rel="stylesheet" href="screens/friends.css">
  <link rel="stylesheet" href="screens/wardrobe.css">
  <link rel="stylesheet" href="screens/workshop.css">
  <link rel="stylesheet" href="screens/settings.css">
</head>
<body>

  <div class="app-viewport">
    <!-- Живой фоновый мир Minecraft с шейдерами и параллаксом -->
    <div class="world-bg" id="worldBg"></div>
    <div class="world-overlay"></div>
    <div class="sunbeam-light" id="sunbeamLight"></div>
    <canvas id="ambientCanvas"></canvas>

    <!-- 1. ВЕРХНЯЯ ПАРЯЩАЯ ЛЕНТА НАВИГАЦИИ (Apple Liquid Glass Capsule) -->
    <header class="top-ribbon" role="banner">
      <div class="brand-section">
        <a href="#/play" class="logo-aura" aria-label="Aura Launcher">
          <span>Aura</span>
          <span class="logo-badge">v3</span>
        </a>
      </div>

      <!-- Центральный стеклянный навигационный док -->
      <nav class="nav-pill-dock" aria-label="Главная навигация">
        <button type="button" class="nav-tab active" data-route="play" aria-current="page">
          <span class="nav-tab-indicator"></span>
          <span>Играть</span>
        </button>
        <button type="button" class="nav-tab" data-route="lobby">
          <span>Лобби</span>
        </button>
        <button type="button" class="nav-tab" data-route="friends">
          <span>Друзья</span>
        </button>
        <button type="button" class="nav-tab" data-route="wardrobe">
          <span>Гардероб</span>
        </button>
        <button type="button" class="nav-tab" data-route="workshop">
          <span>Мастерская</span>
        </button>
        <button type="button" class="nav-tab" data-route="settings">
          <span>Настройки</span>
        </button>
      </nav>

      <!-- Оконные кнопки управления (минимизировать, развернуть, закрыть) -->
      <div class="top-actions">
        <div class="win-controls" aria-label="Управление окном">
          <button type="button" class="win-btn" title="Свернуть" aria-label="Свернуть окно">
            <svg class="icon" viewBox="0 0 24 24"><line x1="5" y1="12" x2="19" y2="12"></line></svg>
          </button>
          <button type="button" class="win-btn" title="Развернуть" aria-label="Развернуть окно">
            <svg class="icon" viewBox="0 0 24 24"><rect x="3" y="3" width="18" height="18" rx="2" ry="2"></rect></svg>
          </button>
          <button type="button" class="win-btn close" title="Закрыть" aria-label="Закрыть приложение">
            <svg class="icon" viewBox="0 0 24 24"><line x1="18" y1="6" x2="6" y2="18"></line><line x1="6" y1="6" x2="18" y2="18"></line></svg>
          </button>
        </div>
      </div>
    </header>

    <!-- 2. ОСНОВНОЙ ВЬЮПОРТ ДЛЯ ЭКРАНОВ -->
    <main class="screen-viewport" id="screenViewport" role="main">
${playHtml}
${lobbyHtml}
${friendsHtml}
${wardrobeHtml}
${workshopHtml}
${settingsHtml}
    </main>

    <!-- СИСТЕМНЫЙ ТОСТ-КОНТЕЙНЕР -->
    <div class="toast-container" id="toastContainer" aria-live="polite"></div>
  </div>

  <!-- Базовые скрипты ядра и модулей -->
  <script src="assets/js/core.js"></script>
  <script src="screens/play.js"></script>
  <script src="screens/lobby.js"></script>
  <script src="screens/friends.js"></script>
  <script src="screens/wardrobe.js"></script>
  <script src="screens/workshop.js"></script>
  <script src="screens/settings.js"></script>

</body>
</html>
`;

fs.writeFileSync(path.join(baseDir, 'index.html'), template, 'utf-8');
console.log('Successfully compiled index.html! Bytes:', template.length);

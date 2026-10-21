const fs = require('fs');
const path = require('path');

const baseDir = path.join(__dirname);
const screensDir = path.join(baseDir, 'screens');

// Read screen contents
const mapHtml = fs.readFileSync(path.join(screensDir, 'map.html'), 'utf-8');
const lobbyHtml = fs.readFileSync(path.join(screensDir, 'lobby.html'), 'utf-8');
const friendsHtml = fs.readFileSync(path.join(screensDir, 'friends.html'), 'utf-8');
const wardrobeHtml = fs.readFileSync(path.join(screensDir, 'wardrobe.html'), 'utf-8');
const workshopHtml = fs.readFileSync(path.join(screensDir, 'workshop.html'), 'utf-8');
const settingsHtml = fs.readFileSync(path.join(screensDir, 'settings.html'), 'utf-8');

// Read shell
let shellHtml = fs.readFileSync(path.join(baseDir, 'shell.html'), 'utf-8');

// Styles and scripts references for index.html
const stylesBlock = `
  <!-- Дизайн-токены и ядро стилей -->
  <link rel="stylesheet" href="tokens.css">
  <link rel="stylesheet" href="assets/css/core.css">

  <!-- Стили экранов -->
  <link rel="stylesheet" href="screens/play.css">
  <link rel="stylesheet" href="screens/map.css">
  <link rel="stylesheet" href="screens/lobby.css">
  <link rel="stylesheet" href="screens/friends.css">
  <link rel="stylesheet" href="screens/wardrobe.css">
  <link rel="stylesheet" href="screens/workshop.css">
  <link rel="stylesheet" href="screens/settings.css">
`;

const scriptsBlock = `
  <!-- Сторонние библиотеки (skinview3d UMD) -->
  <script src="assets/vendor/skinview3d.bundle.js"></script>

  <!-- Базовые скрипты ядра и модулей -->
  <script src="src/perf-hud.js"></script>
  <script src="src/bridge.js"></script>
  <script src="assets/js/demo-data.js"></script>
  <script src="assets/js/core.js"></script>
  <script src="screens/play.js"></script>
  <script src="screens/map.js"></script>
  <script src="screens/lobby.js"></script>
  <script src="screens/friends.js"></script>
  <script src="screens/wardrobe.js"></script>
  <script src="screens/workshop.js"></script>
  <script src="screens/settings.js"></script>
`;

let compiled = shellHtml
  .replace('<!-- SHELL_STYLES_PLACEHOLDER -->', () => stylesBlock)
  .replace('<!-- SHELL_SCRIPTS_PLACEHOLDER -->', () => scriptsBlock)
  .replace('<!-- SCREEN_MAP_PLACEHOLDER -->', () => mapHtml)
  .replace('<!-- SCREEN_LOBBY_PLACEHOLDER -->', () => lobbyHtml)
  .replace('<!-- SCREEN_FRIENDS_PLACEHOLDER -->', () => friendsHtml)
  .replace('<!-- SCREEN_WARDROBE_PLACEHOLDER -->', () => wardrobeHtml)
  .replace('<!-- SCREEN_WORKSHOP_PLACEHOLDER -->', () => workshopHtml)
  .replace('<!-- SCREEN_SETTINGS_PLACEHOLDER -->', () => settingsHtml);

fs.writeFileSync(path.join(baseDir, 'index.html'), compiled, 'utf-8');
console.log('Successfully compiled index.html from shell.html! Bytes:', compiled.length);

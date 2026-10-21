# Карта интеграции Aura Launcher v3 («Остров») ↔ C# (.NET 8 WPF)

Документ фиксирует соответствие между интерфейсом v3, существующими C#-сервисами лаунчера и контрактом моста на базе Microsoft Edge WebView2.

---

## 1. Таблица связей UI ↔ C# по разделам

| Действие / Данные в UI | Файл и строка в v3 | C#-интерфейс, метод / свойство / событие | Что передать / вернуть | Статус |
| :--- | :--- | :--- | :--- | :--- |
| **Капсула / Играть** | | | | |
| Запуск игры (клик «Играть») | [play.js:11](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/play.js#L11) | `Services/Interfaces/IGameLaunchService.cs` `Task<Process> LaunchGameAsync(...)` | Передать: `LauncherConfig`; Возврат: `Process` | **Есть** |
| Прогресс подготовки и запуска | [play.js:23](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/play.js#L23) | `Services/Interfaces/IGameLaunchService.cs` `IProgress<InstallProgressReport>` | Возврат: `{ phase, percentage, message }` | **Есть** |
| Статус «в игре» (IsGameRunning) | [play.js:62](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/play.js#L62) | `Services/Interfaces/IGameLaunchService.cs` `bool IsGameRunning { get; }` | Возврат: `boolean` | **Есть** |
| Остановка игры (клик «Стоп») | [play.js:52](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/play.js#L52) | `Services/Interfaces/IGameLaunchService.cs` `Task<bool> StopGameProcessAsync(int timeoutMs)` | Возврат: `boolean` | **Есть** |
| Событие завершения процесса игры | [play.js:52](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/play.js#L52) | `Services/Interfaces/IGameLaunchService.cs` `event EventHandler<int>? GameExited` | Событие: `exitCode (int)` | **Есть** |
| Сворачивание лаунчера при запуске | [core.js:89](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/assets/js/core.js#L89) | `Services/Interfaces/IConfigService.cs` `LauncherConfig.HideLauncherWhilePlaying` | Чтение/запись `bool` | **Есть** |
| Оконные кнопки (Свернуть / Закрыть) | [shell.html:15](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/shell.html#L15) | `System.Windows.Window` `WindowState = Minimized`, `Close()` | Команды окна WPF | **Есть** |
| Динамический фон мира | [core.js:277](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/assets/js/core.js#L277) | `Services/Interfaces/IBackgroundService.cs` `CurrentImage`, `event BackgroundChanged` | Путь к изображению / data URI | **Есть** |
| **Лобби** | | | | |
| Создание лобби как хост | [lobby.html:1](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/lobby.html#L1) | `Services/Interfaces/ILobbyService.cs` `Task<string?> CreateLobbyAsHostAsync(string hostName, ...)` | Передать: `hostName`; Возврат: `lobbyCode` | **Есть** |
| Открытие мира для сети (Playit) | [core.js:193](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/assets/js/core.js#L193) | `Services/Interfaces/ILobbyService.cs` `Task<bool> HostOpenWorldAsync(...)` | Передать: `customTunnel, port`; Возврат: `bool` | **Есть** |
| Детекция LAN-мира из latest.log | [demo-data.js:46](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/assets/js/demo-data.js#L46) | `Services/Interfaces/ILanWorldWatcher.cs` `event Action<int>? WorldOpened`, `WorldClosed` | Событие: `port (int)` | **Есть** |
| Статус туннеля (Active, адрес, пинг) | [demo-data.js:48](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/assets/js/demo-data.js#L48) | `Services/Interfaces/ITunnelProvider.cs` `TunnelInfo CurrentInfo`, `event StatusChanged` | Данные: `{ publicAddress, publicPort, status }` | **Есть** |
| Подключение к комнате по коду | [lobby.js:28](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/lobby.js#L28) | `Services/Interfaces/ILobbyService.cs` `Task<bool> JoinLobbyAsGuestAsync(string code, string playerName, ...)` | Передать: `code, playerName`; Возврат: `bool` | **Есть** |
| Список игроков и синхронизация модов | [lobby.html:12](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/lobby.html#L12) | `Services/Interfaces/ILobbyService.cs` `IReadOnlyDictionary<string, PlayerModSyncInfo>? CurrentModSync` | Данные: `{ [playerNick]: { isReady, mismatchedMods } }` | **Есть** |
| Закрытие / выход из лобби | [lobby.js:45](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/lobby.js#L45) | `Services/Interfaces/ILobbyService.cs` `Task CloseLobbyAsHostAsync()`, `void LeaveLobby()` | Вызов метода | **Есть** |
| **Друзья** | | | | |
| Список друзей и онлайн-статусы | [friends.js:10](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/friends.js#L10) | `Services/Interfaces/IFriendService.cs` `IReadOnlyList<FriendPresenceItem> CachedFriends`, `event FriendsListUpdated` | Данные: `FriendPresenceItem[]` (ник, статус, игра) | **Есть** |
| Мой код друга для добавления | [friends.html:7](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/friends.html#L7) | `Services/Interfaces/IFriendService.cs` `string? CurrentFriendCode { get; }` | Данные: `string` | **Есть** |
| Добавление друга по коду | [friends.js:73](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/friends.js#L73) | `Services/Interfaces/IFriendService.cs` `Task<(bool, string?)> SendFriendRequestAsync(string friendCode, ...)` | Передать: `friendCode`; Возврат: `{ success, error }` | **Есть** |
| Ответ на входящую заявку | [friends.js:37](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/friends.js#L37) | `Services/Interfaces/IFriendService.cs` `Task<bool> RespondFriendRequestAsync(string fromId, bool accept, ...)` | Передать: `fromId, accept`; Возврат: `bool` | **Есть** |
| Отправка приглашения в лобби | [friends.js:93](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/friends.js#L93) | `Services/Interfaces/IFriendService.cs` `Task<(bool, string?, string?)> SendInviteAsync(...)` | Передать: `friendId, lobbyCode, hostToken` | **Есть** |
| Входящие приглашения и заявки | [friends.js:61](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/friends.js#L61) | `Services/Interfaces/IFriendService.cs` `event Action<IncomingInviteItem>? InviteReceived`, `FriendRequestReceived` | События: структуры инвайтов/заявок | **Есть** |
| **Гардероб** | | | | |
| Текстура текущего скина (64x64) | [wardrobe.js:16](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/wardrobe.js#L16) | `Services/Interfaces/ISkinService.cs` `GetSkinTextureForPlayerAsync(string nickname, ...)` | Данные: PNG data URL текстуры, `isSlim` | **Есть** |
| Загрузка файла скина (.png) | [wardrobe.js:114](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/wardrobe.js#L114) | `Services/Interfaces/ISkinService.cs` `ValidateSkinFile(string?)`, `SyncSkinToGameAsync(...)` | Передать: путь к файлу; Возврат: `SkinValidationResult` | **Есть** |
| Загрузка скина в lobby-api | [wardrobe.js:121](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/wardrobe.js#L121) | `Services/Interfaces/ISkinService.cs` `Task<SkinUploadResult> UploadSkinToLobbyApiAsync(...)` | Передать: `skinPath, nick, model, ownerToken` | **Есть** |
| Смена типа модели (Steve / Alex) | [wardrobe.js:82](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/wardrobe.js#L82) | `Services/Interfaces/IConfigService.cs` `LauncherConfig.SkinModel ("default" / "slim")` | Запись в конфиг | **Есть** |
| Смена никнейма игрока | [wardrobe.js:137](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/wardrobe.js#L137) | `Services/Interfaces/IFriendService.cs` `ChangeNicknameAsync(string newNick, ...)` + `IConfigService` | Передать: `newNick`; Возврат: `{ success, error }` | **Есть** |
| Выбор и переключение плаща | [wardrobe.js:98](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/wardrobe.js#L98) | Нет отдельного метода для кастомных плащей в `ISkinService` (сейчас плащи в C# не загружаются в CSL) | Передать: тип плаща / текстуру | **Частично** |
| **Мастерская** | | | | |
| Список доступных шейдеров | [workshop.html:6](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/workshop.html#L6) | `Services/Interfaces/IWorkshopService.cs` `Task<List<ShaderPackItem>> GetShaderPacksAsync(string gameDir)` | Возврат: `ShaderPackItem[]` (имя, активен) | **Есть** |
| Активация выбранного шейдера | [workshop.js:24](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/workshop.js#L24) | `Services/Interfaces/IWorkshopService.cs` `void SetActiveShaderPack(string gameDir, string shaderFileName)` | Передать: `shaderFileName` | **Есть** |
| Список установленных модов | [workshop.html:15](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/workshop.html#L15) | `Services/Interfaces/IWorkshopService.cs` `Task<List<ModItem>> GetModsAsync(string gameDir)` | Возврат: `ModItem[]` (имя, версия, включён) | **Есть** |
| Включение / отключение мода | [workshop.js:36](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/workshop.js#L36) | `Services/Interfaces/IWorkshopService.cs` `bool ToggleMod(ModItem mod, bool enable)` | Передать: `modId, enable`; Возврат: `bool` | **Есть** |
| Синхронизация сборки модов | [workshop.js:47](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/workshop.js#L47) | `Services/Interfaces/IPackUpdateService.cs` `Task<PackUpdateResult> CheckAndApplyAsync(...)` | Возврат: `PackUpdateResult` (FilesChanged, Status) | **Есть** |
| Открытие папки модов в Проводнике | [workshop.js:56](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/workshop.js#L56) | `System.Diagnostics.Process.Start("explorer.exe", modsDir)` | Команда вызова ОС | **Есть** |
| **Настройки** | | | | |
| Выделение памяти RAM (слайдер) | [settings.js:24](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/settings.js#L24) | `Services/Interfaces/IConfigService.cs` `LauncherConfig.AllocatedRamMb` | Чтение/запись `int` (МБ) | **Есть** |
| Пресеты графики (Слабая..Ультра) | [settings.js:38](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/settings.js#L38) | `Services/Interfaces/IGraphicsPresetService.cs` `ApplyPreset(string gameDir, string presetKey)` | Передать: `presetKey`; Возврат: `PresetApplyResult` | **Есть** |
| Режим визуальных эффектов (full/low/off) | [settings.js:63](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/settings.js#L63) | `Services/Interfaces/IConfigService.cs` (сохранение в `LauncherConfig.VisualEffectsMode`) | Чтение/запись строки `"full" | "low" | "off"` | **Частично** (в конфиге пока сохраняется через localStorage) |
| Тумблеры: скрыть в игре, автообновление | [settings.html:31](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/settings.html#L31) | `Services/Interfaces/IConfigService.cs` `HideLauncherWhilePlaying`, `AutoUpdateEnabled` | Чтение/запись `bool` | **Есть** |
| Экспорт логов и отчёта о сбое | [settings.js:80](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/settings.js#L80) | `Services/Interfaces/IReportService.cs` `Task<string> GenerateReportZipAsync(...)` | Возврат: путь к созданному `.zip` | **Есть** |
| Очистка кэша модов и шейдеров | [settings.js:98](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/settings.js#L98) | Удаление `.aura/mods/.cache`, `.aura/shaderpacks/.cache` | Команда файловой системы | **Есть** |
| Выбор папки игры (Диалог обзора) | [settings.js:69](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/settings.js#L69) | `Microsoft.Win32.OpenFolderDialog` / `IConfigService.CurrentConfig.CustomGameDirectory` | Вызов Win32 API диалога | **Есть** |
| **Карта** | | | | |
| Отображение карты мира (холст) | [map.js:42](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/map.js#L42) | Нет сервиса JourneyMap/тайлов (в прототипе процедурный генератор шума Перлина) | Передача тайлов из `.aura/journeymap/data/sp/` | **Нет** |
| Координаты и метки друзей на карте | [map.js:176](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/map.js#L176) | Нет трекинга игровых координат (требуется свой Fabric-мод на клиенте) | Потоковые координаты `{ x, z, player }` | **Нет** |

---

## 2. Разрывы функциональности (UI vs C#)

### А. Есть в UI, но отсутствует / не завершено в C#
1. **Интеграция с картой мира (JourneyMap / e4mc)**:
   - В UI [map.js](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/map.js) реализован интерактивный холст с панорамированием, зумом и точками интереса (база, спавн, шахта).
   - В C# нет парсера тайлов JourneyMap (`.aura/journeymap/data/`) и нет канала телеметрии внутриигровых координат друзей.
2. **Кастомные плащи игрока**:
   - В UI [wardrobe.html](file:///c:/Users/magne/Documents/GitHub/Aura-Launcher/docs/redesign/v3/screens/wardrobe.html) есть выбор плащей («Лазурный», «Серебряный», «Нет»).
   - В C# `ISkinService` управляет только текстурой скина (`skins/<nick>.png`). Поддержка плащей (`capes/<nick>.png`) в CustomSkinLoader не реализована.

### Б. Есть в C#, но отсутствует в текущем UI v3 («Остров»)
1. **Аудиоплеер / Гимн Aura**:
   - В C# полноценно функционируют `IAudioService` и `IAnthemService` (воспроизведение `anthem.mp3`, управление громкостью, плейлист, пауза при запуске игры). В UI v3 аудиоплеер вынесен за пределы основного острова.
2. **Система достижений**:
   - В C# реализован `IAchievementService` с хранилищем `achievements.json`, трекингом событий и всплывающими тостами разблокировки. В UI «Остров» отдельный экран достижений отсутствует.
3. **Discord Rich Presence**:
   - В C# `IDiscordRpcService` транслирует статусы («В лаунчере», «В лобби: 3/8», «В игре»). В UI v3 нет переключателя активности Discord RPC.
4. **Галерея скриншотов**:
   - В C# `IScreenshotWatcherService` перехватывает новые F2-снимки из папки `.aura/screenshots` и генерирует уведомления. В UI v3 просмотр галереи пока не интегрирован в Мастерскую.
5. **Журнал изменений (Release Notes)**:
   - В C# `IReleaseNotesService` загружает changelog из репозитория. В v3 changelog не выводится.

---

## 3. Архитектура и договор моста (JS ↔ C#)

### Сравнение механизмов взаимодействия WebView2

| Критерий | `CoreWebView2.PostWebMessageAsJson` / `WebMessageReceived` | `CoreWebView2.AddHostObjectToScript` (COM interop) |
| :--- | :--- | :--- |
| **Связность** | **Слабая (Loose coupling)**: обмен асинхронными JSON-сообщениями по шине | **Жёсткая**: прямая привязка к COM-объектам и типам .NET |
| **Безопасность** | Высокая: строгая сериализация, отсутствие утечек COM-памяти | Риск утечек RCW-ссылок и зависания UI-потока при блокировках |
| **Тестируемость** | Идеальная: JS-код может работать в браузере без WebView2 через мок-шину | Требует наличия `window.chrome.webview.hostObjects` |
| **Поддержка async**| Нативная поддержка двусторонних промисов и очередей событий | Требует `await hostObject.MethodAsync()`, сложная обработка ошибок |

> [!TIP]
> **Выбор**: `PostWebMessageAsJson` / `WebMessageReceived`. Этот подход обеспечивает чистый контракт в стиле RPC/Event-Bus, абсолютную изоляцию потоков WPF и возможность независимой отладки фронтенда в любом обычном браузере.

### Спецификация контракта сообщений

Все сообщения передаются в формате JSON с базовой структурой:
```json
{
  "id": "uuid-v4-optional",
  "type": "command | event | response",
  "name": "namespace.actionName",
  "payload": {}
}
```

#### Команды (JS → C#)
1. **`game.launch`**: запрос запуска игры с выбранными параметрами.
   - Payload: `{ "ramGb": 6, "model": "default", "nickname": "Coock0ld" }`
2. **`game.stop`**: запрос принудительной остановки процесса игры.
   - Payload: `{}`
3. **`lobby.create`**: создание нового сетевого лобби.
   - Payload: `{ "hostName": "Coock0ld" }`
4. **`lobby.join`**: подключение к существующей комнате.
   - Payload: `{ "code": "AURA-7731-PLAY", "playerName": "Coock0ld" }`
5. **`lobby.leave`**: выход из текущего лобби.
   - Payload: `{}`
6. **`skin.upload`**: загрузка локального скина игрока.
   - Payload: `{ "fileName": "steve.png", "base64Data": "..." }`
7. **`preset.apply`**: применение выбранного пресета графики.
   - Payload: `{ "presetKey": "high" }`
8. **`workshop.toggleMod`**: включение/выключение мода.
   - Payload: `{ "modId": "sodium", "enabled": true }`
9. **`window.action`**: управление окном WPF.
   - Payload: `{ "action": "minimize | maximize | close | drag" }`

#### События (C# → JS)
1. **`game.progress`**: потоковый статус проверки и скачивания файлов.
   - Payload: `{ "stage": "downloading", "file": "assets.jar", "percentage": 45 }`
2. **`game.status`**: изменение жизненного цикла игры.
   - Payload: `{ "isRunning": true, "processId": 14208, "exitCode": null }`
3. **`tunnel.status`**: состояние сетевого туннеля Playit.
   - Payload: `{ "status": "active", "address": "eu.aura.playit.gg", "port": 25565, "pingMs": 34 }`
4. **`friends.presence`**: обновление списка и статусов друзей.
   - Payload: `[{ "id": "f_1", "nickname": "Notch", "status": "game", "lobbyCode": "AURA-7731" }]`
5. **`notification.toast`**: вывод системного всплывающего сообщения.
   - Payload: `{ "type": "info | success | error", "title": "Лобби", "message": "Игрок Alex подключился" }`

---

## 4. Хостинг страницы и окружение WebView2

### Механизм хостинга: `SetVirtualHostNameToFolderMapping` против `file://`

| Параметр | `SetVirtualHostNameToFolderMapping` (Выбрано) | `file://` |
| :--- | :--- | :--- |
| **Домен** | `https://aura.local/` (настоящий origin) | `file:///C:/Users/...` (null origin) |
| **CORS & Fetch** | Разрешены fetch/XHR к локальным ассетам и текстурам | Блокируется SecurityError при доступе к canvas/шейдерам |
| **Кэширование** | Контролируемое через ServiceWorker / MemoryCache | Нестабильное, частые блокировки файлов Windows |
| **Пути** | Изолированные виртуальные пути без раскрытия диска | Полные абсолютные системные пути пользователя |

> [!IMPORTANT]
> **Решение**: Регистрация виртуального хоста через:
> `CoreWebView2.SetVirtualHostNameToFolderMapping("aura.local", localAssetsPath, CoreWebView2HostResourceAccessKind.Allow)`.
> Приложение открывает `https://aura.local/index.html`.

### Расположение ассетов и скинов
- Статические файлы UI (`shell.html`, `screens/`, `tokens.css`, `assets/fonts/Onest.woff2`, `assets/vendor/skinview3d.bundle.js`) компилируются и разворачиваются в `%LOCALAPPDATA%\Aura\ui\`.
- Пользовательские скины и кэш аватаров читаются через специальный виртуальный эндпоинт `https://aura.local/skins/{nick}.png`, который мапится на `%APPDATA%\.aura\CustomSkinLoader\LocalSkin\skins\`.

### Управление окном без системной рамки (Custom Chrome)
- Окно WPF настраивается как `WindowStyle="None"`, `AllowsTransparency="False"` (WebView2 не поддерживает прозрачность при `AllowsTransparency="True"` в старых версиях .NET; в .NET 8 с `DefaultBackgroundColor = Color.Transparent` прозрачность поддерживается через композицию Windows).
- Для перетаскивания окна (`Window.DragMove`) в HTML-разметке шапки добавляется CSS-свойство:
  `-webkit-app-region: drag;`
  Для интерактивных кнопок (закрыть, свернуть):
  `-webkit-app-region: no-drag;`
- Клик по кнопкам окна отправляет в C# команду `window.action { action: "minimize" | "close" }`, которая выполняется в потоке WPF `Dispatcher`.

---

## 5. Оценка необходимости Vite / Tailwind v4

### Сравнение текущего скрипта `build.ps1` и связки `Vite + Tailwind v4`

| Параметр | Текущий пайплайн (`assemble.js` + `build.ps1`) | Стек `Vite + Tailwind v4` |
| :--- | :--- | :--- |
| **Зависимости** | 0 npm-зависимостей, только встроенный Node.js | ~150 МБ `node_modules`, lockfile, Node/npm toolchain |
| **Скорость сборки** | **1.8 секунды** (мгновенно в PowerShell) | 3–6 секунд с генерацией AST и CSS-парсингом |
| **Размер бандла** | `index.html` 18.5 КБ + vendor 477 КБ | ~500–600 КБ после tree-shaking |
| **Соответствие DESIGN.md** | 100% прямое совпадение с дизайн-токенами Apple Glass | Риск конфликта утилит Tailwind с токенами `tokens.css` |
| **Сложность внедрения** | Готово и проверено в тестах | Потребует полной переделки путей и сборки |

> [!TIP]
> **Рекомендация по Vite**: На текущем этапе переход на Vite/Tailwind **не требуется**. Сборка `build.ps1` (`assemble.js` + `build_standalone.js`) работает со скоростью <2 с, не требует `node_modules`, формирует чистый валидный HTML и полностью закрывает потребности проекта без лишних зависимостей.

---

## 6. Анализ рисков и отказоустойчивость

1. **Отсутствие WebView2 Runtime на ПК пользователя**:
   - В Windows 10 (начиная с 20H2) и Windows 11 компонент WebView2 Runtime предустановлен в системе по умолчанию.
   - Для редких сборок LTSC / урезанных ОС в инсталлятор Velopack или `App.xaml.cs` встраивается проверка `CoreWebView2Environment.GetAvailableBrowserVersionString()`. При отсутствии рантайма лаунчер автоматически скачивает и запускает официальный `MicrosoftEdgeWebview2Setup.exe --silent --install`.
2. **Производительность WebGL и размытия на слабых ПК**:
   - WebGL-рендер `skinview3d` автоматически ставится на паузу (`renderPaused = true`), когда Гардероб закрыт или лаунчер находится в фоне.
   - Режим эффектов `data-effects="off"` полностью снимает `backdrop-filter`, отключает CSS-анимации и останавливает автовращение 3D-модели, обеспечивая 60 FPS даже на слабых встроенных видеокартах Intel HD Graphics.
3. **Офлайн-режим (Offline-first)**:
   - Все библиотеки (`skinview3d.bundle.js`), шрифты (`Onest.woff2`), иконки и текстуры хранятся локально на диске. Никаких внешних CDN-запросов нет.
   - При отсутствии интернета лаунчер успешно открывает интерфейс из локальной директории и позволяет играть в офлайн-режиме одиночной игры.

# План выноса логики лобби из `LobbyViewModel` в `LobbySessionService`

## 1. Карта `LobbyViewModel.cs` (3798 строк)

| Группа методов / логики | Строки | Назначение | Где логика, а где чистый UI |
|---|---|---|---|
| **Хост: создание, открытие мира, закрытие** | `962–1447` (`CreateLobbyAsync`, `OpenWorldAsHostAsync`, `OnLanWorldOpened`, `CompleteHostOpenWorldAsync`, `OnLanWorldClosed`) | Регистрация комнаты в `ILobbyService`, ожидание LAN-порта через `ILanWorldWatcher`, запуск/перезапуск туннеля, апдейт статуса `open` | **Логика (Сервис)**: вызовы API, туннель, watcher порта, смена состояний. **UI**: прогресс-бары, текст статуса, всплывающие плашки. |
| **Туннель Playit: клейм и мониторинг** | `1120–1168`, `1504–1575` (`ClaimPersonalTunnelAsync`, `OnTunnelStatusChanged`) | Получение/привязка персонального токена, отслеживание пинга и статуса туннеля | **Логика (Сервис)**: `ITunnelProvider`, `PlayitTunnelProvider`, токен DPAPI. **UI**: иконка пинга, бейджи «Туннель активен / 34 мс». |
| **Гость: подключение по коду, выход** | `1576–1823`, `1965–2024` (`JoinLobbyAsync`, `JoinByCodeAsync`, `LeaveLobby`, `UpdateGuestStatus`) | Вход гостем по коду `AURA-XXXX`, поллинг `RefreshGuestStatusAsync`, отслеживание кика, выход/сброс | **Логика (Сервис)**: API-поллинг лобби, отслеживание статуса `open`, очистка состояния. **UI**: поле ввода, валидация кода, анимация слотов. |
| **Подключение к игре (Launch/Connect)** | `1824–1923` (`ConnectToGameAsync`, `ExecuteConnectToGameAsync`, `ReconnectAsync`, `OnHostGameStarted`) | Автозапуск игры с аргументом `--quickPlayMultiplayer <tunnelAddress>` через `IGameLaunchService` | **Логика (Сервис)**: формирование `LaunchOptions`, координация с `GameSessionService`. **UI**: блокировка кнопки «Играть», тосты. |
| **Синхронизация игроков и модов** | `2025–2057`, `2277–2408`, `2562–2988` (`KickPlayerAsync`, `RefreshLobbyPlayersAsync`, `OnModSyncUpdated`, `FixAndProceedAsync`) | Сверка хешей модов гостя и хоста, исключение игроков (`kick`), автоскачивание недостающих модов | **Логика (Сервис)**: манифест модов, сравнение хэшей, кик через API, загрузка `.jar`. **UI**: диалог `ShowMismatchDialog`, список несовпадений, статус синхронизации. |
| **Облачные миры (Cloud Worlds)** | `2989–3452`, `3579–3798` (`RefreshWorldsAsync`, `CreateNewWorldAsync`, `WakeAndConnectWorldAsync`, `UploadLocalWorldAsync`, `ToggleFriendAccessAsync`) | Управление удаленными серверами/мирами, импорт сохранений | **Вынесено в отдельный этап («потом»)**. Полностью изолировано от основного функционала P2P-лобби. |
| **UI-состояние и визуализация сцены** | `1–961`, `2409–2561`, `3453–3578` (`StagePositions`, `LobbyPlayerItem`, ячейки кода, таймеры пинга, скины 3D) | Отрисовка подиума игроков, расчет координат WPF 3D/Canvas, анимации | **Чистый UI (WPF VM)**: остается в `LobbyViewModel` и транслируется в разметку/DOM. |

---

## 2. Предложенный `ILobbySessionService`

По аналогии с `IGameSessionService`, сервис инкапсулирует весь жизненный цикл лобби и туннеля без привязки к WPF.

```csharp
public enum LobbySessionRole { None, Host, Guest }

public enum LobbySessionState
{
    Idle,
    Creating,
    WaitingForWorld, // Хост в лобби, ждем открытия мира в Minecraft
    TunnelStarting,
    Active,          // Мир открыт, туннель работает, игроки могут подключаться
    Joining,         // Гость подключается по коду
    ReadyToPlay,     // Гость готов играть (туннель получен, моды совпадают)
    ConnectingToGame,
    Error
}

public record LobbyPlayerItemDto(string Nickname, bool IsHost, bool IsReady = true, string? Status = null);

public record LobbyStatusDto(
    string Code,
    LobbySessionRole Role,
    LobbySessionState State,
    string? HostName,
    string? TunnelAddress,
    int? PingMs,
    IReadOnlyList<LobbyPlayerItemDto> Players
);

public interface ILobbySessionService : IDisposable
{
    LobbySessionRole Role { get; }
    LobbySessionState State { get; }
    string? CurrentLobbyCode { get; }
    string? CurrentTunnelAddress { get; }
    int? CurrentTunnelPingMs { get; }
    string? HostNick { get; }
    string? LastError { get; }
    IReadOnlyList<LobbyPlayerItemDto> Players { get; }
    IReadOnlyDictionary<string, PlayerModSyncInfo>? ModSync { get; }

    event EventHandler<LobbySessionState>? StateChanged;
    event EventHandler<LobbyStatusDto>? LobbyUpdated;
    event EventHandler<string>? TunnelAddressReady;
    event EventHandler<int>? TunnelPingUpdated;
    event EventHandler<string>? PlayerKicked;
    event EventHandler<string>? ErrorOccurred;

    Task<string?> CreateLobbyAsync(string hostName, CancellationToken cancellationToken = default);
    Task<bool> JoinByCodeAsync(string code, string guestNick, CancellationToken cancellationToken = default);
    Task OpenWorldAsync(int? localPort = null, CancellationToken cancellationToken = default);
    Task ConnectToGameAsync(CancellationToken cancellationToken = default);
    Task<bool> KickPlayerAsync(string playerNick, CancellationToken cancellationToken = default);
    Task LeaveLobbyAsync(CancellationToken cancellationToken = default);
}
```

---

## 3. Делегирование из `LobbyViewModel`

Старое WPF-окно сохраняет стабильность:
1. `LobbyViewModel` получает `ILobbySessionService` в конструктор через DI.
2. Подписывается на события `StateChanged`, `LobbyUpdated`, `TunnelPingUpdated`, `PlayerKicked` и перенаправляет изменения в UI через существующий `Dispatch(() => { ... })`.
3. Методы `CreateLobbyAsync()`, `JoinByCodeAsync()`, `OpenWorldAsHostAsync()`, `LeaveLobby()`, `KickPlayerAsync()` становятся тонкими обертками из 3–5 строк, вызывающими методы сервиса.
4. Вся тяжелая логика (поллинг таймеров, обработка `ILanWorldWatcher`, запуск `playit`, обращение к `lobby-api`) уходит из VM в сервис.

---

## 4. Команды и события WebBridge для раздела «Лобби»

### Команды (JS → C#):
- `lobby.get` → `{ inLobby, role, state, code, tunnelAddress, pingMs, hostName, players: [{ nick, isHost, isReady, status }] }`
- `lobby.create` → `{ code }`
- `lobby.join { code }` → `{ success, code, hostName }` (или ошибка `invalid_code`, `lobby_not_found`, `version_mismatch`)
- `lobby.leave` → `{ ok: true }`
- `lobby.openWorld { port? }` → `{ ok: true, tunnelAddress }`
- `lobby.kick { playerNick }` → `{ ok: true }`
- `lobby.play` → запуск игры/подключение к туннелю (вызов `ConnectToGameAsync`)

### События (C# → JS push):
- `lobby.statusChanged` → `{ role, state, code, tunnelAddress, pingMs }`
- `lobby.playersUpdated` → `{ count, max: 8, players: [...] }`
- `lobby.kicked` → `{ reason }` (выводит тост и сбрасывает гостя в `idle`)

---

## 5. Риски и нерешенные места

1. **Dispatcher и UI-потоки**: `LobbyViewModel` содержит свыше 50 вызовов `Dispatch(...)` и обращений к `ObservableCollection<LobbyPlayerItem>`. При выносе в сервис все внутренние коллекции должны быть потокобезопасными (`lock` / immutable DTO), а события — вызываться на фоновом пуле задач.
2. **Таймеры**: В VM работает реальный таймер `_cloudLobbyRefreshTimer` (опрос облачных миров). Для поллинга статуса гостей лобби в сервисе используется легковесный `PeriodicTimer` с единым `CancellationTokenSource`, не нагружающий UI-поток.
3. **Использование `Core\LobbyStateManager.cs`**: Готовая доменная модель управления жизненным циклом LAN-мира и туннеля используется напрямую внутри `LobbySessionService`, исключая дублирование логики переходов состояний портов и процессов.
4. **Открытие мира из `latest.log`**: `ILanWorldWatcher` парсит строки в логе Minecraft (`[Render thread/INFO]: [CHAT] Local game hosted on port XXXXX`). Важно, чтобы служба читала лог независимо от открытой вкладки и активности окна лаунчера.
5. **Тестовое покрытие**: В `AuraLauncher.Tests` практически нет тестов на связку `PlayitTunnelProvider` + гостевой поллинг `RefreshGuestStatusAsync`. Новый сервис позволит покрыть эту цепочку модульными тестами с моками.

---

## 5а. Сверка сценария хоста: `LobbyViewModel` → `LobbySessionService` (коммит f1bd724)

| Ветка VM (строки) | Место в сервисе | Примечание |
|---|---|---|
| `ClaimPersonalTunnelAsync` (1120–1168) | `ClaimPersonalTunnelAsync` (239) | открытие браузера вынесено в колбэк `onBrowserUrl` |
| `OpenWorldAsHostAsync`: `AURA_FAKE_TUNNEL` / `UseFakeTunnel` (1176–1190) | `OpenWorldAsync` (~358) | сразу `HostOpenWorldAsync("fake-test.e4mc.link")` |
| авто-бэкап мира (1193–1198) | `OpenWorldAsync` (~390) | только если игра не запущена |
| `_domainTcs` + `_worldWatcher.Start(latest.log)` (1200–1204) | `OpenWorldAsync` (399–400) | |
| e4mc: домен уже в watcher (1208–1216) | `OpenWorldAsync` (404–409) | |
| мир уже слушает 25565 (1219–1226) | `OpenWorldAsync` (415–418) | `PlayitTunnelProvider.TestTcpConnectAsync` |
| подсказка «Esc → Открыть для сети» (1230–1233) | `StatusHint` + `WaitingForWorld` | |
| `HostLaunchRequested` (1235–1239) | `IGameSessionService.LaunchAsync` (430) | VM отдавал запуск MainViewModel |
| `OnLanDomainAssigned` (1253) | `OnLanDomainAssigned` (439) | |
| `OnLanWorldOpened` (1265–1382) | `OnLanWorldOpened` (451) | `_lobbyService.IsHost` → `Role == Host` |
| `CompleteHostOpenWorldAsync` (1383–1447) | `CompleteHostOpenWorldAsync` (537) | `_hostOpenGate`, `_lastTunnelAddress` |
| `OnLanWorldClosed` (1448–1474) | `OnLanWorldClosed` (580) | |
| `OnGameExited` (1475–1503) | `OnGameExited` (900) | через `LobbyStateManager.OnGameExited` |

**Не перенесено:** UI-поля (`HostStatusText`, `StatusIcon`, `IsBusy`, `OnPropertyChanged`), облачные миры, 3D-сцена, `ShowMismatchDialog`.

**Тесты:** классы, которые трогают `AURA_FAKE_TUNNEL`, `LobbyService.UseE4allTunnel` и `FakeTunnelProvider`, объединены в коллекцию `LobbyGlobalState` без параллельного запуска. Причина прежней нестабильности: `FakeTunnelProvider.DefaultHost` читает `AURA_FAKE_TUNNEL` при создании, и параллельный `LobbySessionServiceTests` подменял хост на `127.0.0.1`.

---

## 6. План реализации по шагам

- **Шаг 1 (Сервис + Тесты)**:
  - Создание `ILobbySessionService` и `LobbySessionService` (на базе `LobbyStateManager`).
  - Регистрация в DI (`App.xaml.cs`).
  - Модульные тесты на создание, вход, смену состояний, кик и очистку ресурсов.
  - Коммит: `feat(lobby): extract ILobbySessionService with state machine`.
- **Шаг 2 (Рефакторинг `LobbyViewModel`)**:
  - Внедрение `ILobbySessionService` в `LobbyViewModel`, перевод команд VM на делегирование в сервис.
  - Проверка старого интерфейса WPF (регрессионные тесты `FriendsAndLobbyScenarioTests`).
  - Коммит: `refactor(lobby): delegate LobbyViewModel operations to ILobbySessionService`.
- **Шаг 3 (Подключение к WebBridge и UI v3)**:
  - Регистрация команд `lobby.*` в `WebBridge.cs`.
  - Интеграция `docs/redesign/v3/screens/lobby.js` (код комнаты, слоты игроков, индикатор туннеля, кнопка входа).
  - Проверка в окне `--webui`.
  - Коммит: `feat(bridge): connect lobby section to ILobbySessionService`.

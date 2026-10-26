using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using Microsoft.Win32;
using Microsoft.Web.WebView2.Core;

namespace AuraLauncher.Core.WebBridge;

public partial class WebBridge : IDisposable
{
    private readonly Window? _window;
    private readonly IGameSessionService _gameSessionService;
    private readonly IConfigService _configService;
    private readonly IGraphicsPresetService? _presetService;
    private readonly IWorkshopService? _workshopService;
    private readonly IPackUpdateService? _packUpdateService;
    private readonly IGameLaunchService? _launchService;
    private readonly ISkinService? _skinService;
    private readonly IFriendService? _friendService;
    private readonly IReportService? _reportService;
    private readonly ILobbySessionService? _lobbySession;
    private readonly ILauncherUpdateService? _launcherUpdateService;
    private readonly ICloudLobbyService? _cloudLobbyService;
    private readonly IAchievementService? _achievementService;
    private readonly IDiscordRpcService? _discordRpcService;
    private readonly IPrewarmGameService? _prewarmService;
    private readonly Action<string> _logger;
    private CoreWebView2? _coreWebView2;
    private CancellationTokenSource? _gameLaunchCts;
    private CancellationTokenSource? _packSyncCts;
    private bool _isSyncing;
    private IReadOnlyList<FriendPresenceItem> _cachedFriends = Array.Empty<FriendPresenceItem>();

    public WebBridge(
        Window? window,
        IGameSessionService gameSessionService,
        IConfigService configService,
        IGraphicsPresetService? presetService = null,
        IWorkshopService? workshopService = null,
        IPackUpdateService? packUpdateService = null,
        IGameLaunchService? launchService = null,
        ISkinService? skinService = null,
        IFriendService? friendService = null,
        IReportService? reportService = null,
        ILobbySessionService? lobbySession = null,
        ILauncherUpdateService? launcherUpdateService = null,
        ICloudLobbyService? cloudLobbyService = null,
        IAchievementService? achievementService = null,
        IDiscordRpcService? discordRpcService = null,
        IPrewarmGameService? prewarmService = null,
        Action<string>? logger = null)
    {
        _window = window;
        _gameSessionService = gameSessionService;
        _configService = configService;
        _presetService = presetService;
        _workshopService = workshopService;
        _packUpdateService = packUpdateService;
        _launchService = launchService;
        _skinService = skinService;
        _friendService = friendService;
        _reportService = reportService;
        _prewarmService = prewarmService;
        _logger = logger ?? FabricGameLaunchService.LogLauncherEvent;

        _gameSessionService.StateChanged += OnSessionStateChanged;
        _gameSessionService.GameExited += OnSessionGameExited;
        if (_window != null)
        {
            _window.StateChanged += OnWindowStateChanged;
        }

        if (_friendService != null)
        {
            _cachedFriends = _friendService.CachedFriends ?? Array.Empty<FriendPresenceItem>();
            _friendService.FriendsListUpdated += OnFriendsListUpdated;
            _friendService.FriendRequestReceived += OnFriendRequestReceived;
            _friendService.InviteReceived += OnInviteReceived;
        }

        _lobbySession = lobbySession;
        SubscribeLobby();
        _achievementService = achievementService;
        SubscribeAchievements();
        _discordRpcService = discordRpcService;
        _launcherUpdateService = launcherUpdateService;
        _cloudLobbyService = cloudLobbyService;
    }

    private void OnWindowStateChanged(object? sender, EventArgs e)
    {
        SendWindowState();
    }

    public void SendWindowState()
    {
        if (_window == null) return;
        string state = _window.WindowState switch
        {
            WindowState.Maximized => "maximized",
            WindowState.Minimized => "minimized",
            _ => "normal"
        };
        SendEvent("window.state", new { state });
    }

    public void Attach(CoreWebView2 coreWebView2)
    {
        _coreWebView2 = coreWebView2;
        _coreWebView2.WebMessageReceived += OnWebMessageReceived;

        // Хосты скриншотов регистрируем сразу, до первых запросов превью из галереи
        RegisterShotsHosts(ScreenshotsDir());

        // Отправка начального статуса окна
        SendWindowState();
        StartUpdatePolling();
        StartPresence();

        // Отправка начального статуса
        SendEvent("game.status", new
        {
            state = MapState(_gameSessionService.State),
            isRunning = _gameSessionService.IsGameRunning,
            version = _gameSessionService.RunningGameVersion,
            processId = _gameSessionService.CurrentProcess?.Id,
            exitCode = (int?)null
        });
    }

    private static string MapState(GameSessionState state) => state switch
    {
        GameSessionState.Idle => "idle",
        GameSessionState.Preparing => "preparing",
        GameSessionState.Launching => "launching",
        GameSessionState.Running => "running",
        GameSessionState.Failed => "failed",
        _ => "idle"
    };

    private void OnSessionStateChanged(object? sender, GameSessionState state)
    {
        _logger($"[WEBBRIDGE] StateChanged -> {state}");
        SendEvent("game.status", new
        {
            state = MapState(state),
            isRunning = state == GameSessionState.Running,
            version = _gameSessionService.RunningGameVersion,
            processId = _gameSessionService.CurrentProcess?.Id,
            exitCode = (int?)null
        });
    }

    private void OnSessionGameExited(object? sender, GameExitedEventArgs e)
    {
        _logger($"[WEBBRIDGE] GameExited -> exitCode={e.ExitCode}");
        ReportAchievement("game_exit");
        UpdatePresence();
        SendEvent("game.status", new
        {
            state = e.ExitCode == 0 ? "idle" : "failed",
            isRunning = false,
            version = (string?)null,
            processId = (int?)null,
            exitCode = e.ExitCode,
            error = e.FailureReason
        });
    }

    private async void OnWebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        string rawInput;
        try
        {
            rawInput = e.TryGetWebMessageAsString();
            if (string.IsNullOrEmpty(rawInput))
            {
                rawInput = e.WebMessageAsJson;
            }
        }
        catch
        {
            rawInput = e.WebMessageAsJson;
        }

        await ProcessRawMessageAsync(rawInput);
    }

    public async Task ProcessRawMessageAsync(string? rawInput)
    {
        var (msg, parseError) = BridgeMessageParser.Parse(rawInput);
        if (parseError != null || msg == null)
        {
            _logger($"[WEBBRIDGE: ERROR] JSON parse failed: {parseError}");
            SendError(null, "parse.error", parseError ?? "unknown_parse_error");
            return;
        }

        _logger($"[WEBBRIDGE] Command received: {msg.Name} (id={msg.Id})");

        try
        {
            switch (msg.Name)
            {
                case "game.launch":
                    await HandleGameLaunchAsync(msg);
                    break;

                case "game.stop":
                    await HandleGameStopAsync(msg);
                    break;

                case "window.action":
                    HandleWindowAction(msg);
                    break;

                case "config.get":
                    HandleConfigGet(msg);
                    break;

                case "config.set":
                    await HandleConfigSetAsync(msg);
                    break;

                case "preset.apply":
                    HandlePresetApply(msg);
                    break;

                case "workshop.list":
                    await HandleWorkshopListAsync(msg);
                    break;

                case "workshop.setShader":
                    await HandleWorkshopSetShaderAsync(msg);
                    break;

                case "workshop.toggleMod":
                    await HandleWorkshopToggleModAsync(msg);
                    break;

                case "pack.sync":
                    await HandlePackSyncAsync(msg);
                    break;

                case "fs.openModsFolder":
                    HandleFsOpenModsFolder(msg);
                    break;

                case "fs.openGameFolder":
                    HandleFsOpenGameFolder(msg);
                    break;

                case "skin.get":
                    HandleSkinGet(msg);
                    break;

                case "skin.upload":
                    await HandleSkinUploadAsync(msg);
                    break;

                case "skin.setModel":
                    await HandleSkinSetModelAsync(msg);
                    break;

                case "cape.upload":
                    await HandleCapeUploadAsync(msg);
                    break;

                case "wardrobe.getHistory":
                    HandleWardrobeGetHistory(msg);
                    break;

                case "wardrobe.apply":
                    await HandleWardrobeApplyAsync(msg);
                    break;

                case "wardrobe.delete":
                    await HandleWardrobeDeleteAsync(msg);
                    break;

                case "friends.get":
                    HandleFriendsGet(msg);
                    break;

                case "friends.setTabActive":
                    HandleFriendsSetTabActive(msg);
                    break;

                case "friends.add":
                    await HandleFriendsAddAsync(msg);
                    break;

                case "friends.respond":
                    await HandleFriendsRespondAsync(msg);
                    break;

                case "game.setVersion":
                    await HandleGameSetVersionAsync(msg);
                    break;

                case "fs.pickGameFolder":
                    await HandleFsPickGameFolderAsync(msg);
                    break;

                case "logs.export":
                    await HandleLogsExportAsync(msg);
                    break;

                case "achievements.get":
                    HandleAchievementsGet(msg);
                    break;

                case "cloud.list":
                    await HandleCloudListAsync(msg);
                    break;

                case "cloud.create":
                    await HandleCloudCreateAsync(msg);
                    break;

                case "cloud.start":
                    await HandleCloudStartAsync(msg);
                    break;

                case "cloud.connect":
                    await HandleCloudConnectAsync(msg);
                    break;

                case "cloud.stop":
                    await HandleCloudStopAsync(msg);
                    break;

                case "cloud.delete":
                    await HandleCloudDeleteAsync(msg);
                    break;

                case "cloud.share":
                    await HandleCloudShareAsync(msg);
                    break;

                case "cloud.upload":
                    await HandleCloudUploadAsync(msg);
                    break;

                case "worlds.local":
                    await HandleWorldsLocalAsync(msg);
                    break;

                case "worlds.backup":
                    await HandleWorldsBackupAsync(msg);
                    break;

                case "worlds.restore":
                    await HandleWorldsRestoreAsync(msg);
                    break;

                case "worlds.deleteBackup":
                    await HandleWorldsDeleteBackupAsync(msg);
                    break;

                case "worlds.delete":
                    await HandleWorldsDeleteAsync(msg);
                    break;

                case "worlds.export":
                    await HandleWorldsExportAsync(msg);
                    break;

                case "worlds.import":
                    await HandleWorldsImportAsync(msg);
                    break;

                case "worlds.openFolder":
                    await HandleWorldsOpenFolderAsync(msg);
                    break;

                case "screenshots.list":
                    await HandleScreenshotsListAsync(msg);
                    break;

                case "screenshots.copy":
                    HandleScreenshotsCopy(msg);
                    break;

                case "screenshots.open":
                    HandleScreenshotsOpen(msg);
                    break;

                case "screenshots.delete":
                    HandleScreenshotsDelete(msg);
                    break;

                case "screenshots.openFolder":
                    HandleScreenshotsOpenFolder(msg);
                    break;

                case "startup.run":
                    HandleStartupRun(msg);
                    break;

                case "startup.retry":
                    HandleStartupChoice(msg, retry: true);
                    break;

                case "startup.skip":
                    HandleStartupChoice(msg, retry: false);
                    break;

                case "changelog.get":
                    HandleChangelogGet(msg);
                    break;

                case "launcher.updateGet":
                    HandleLauncherUpdateGet(msg);
                    break;

                case "launcher.applyUpdate":
                    await HandleLauncherApplyUpdateAsync(msg);
                    break;

                case "protocol.pending":
                    HandleProtocolPending(msg);
                    break;

                case "skin.setCape":
                    await HandleSkinSetCapeAsync(msg);
                    break;

                case "nickname.set":
                    await HandleNicknameSetAsync(msg);
                    break;

                case "lobby.get":
                    HandleLobbyGet(msg);
                    break;

                case "lobby.create":
                    await HandleLobbyCreateAsync(msg);
                    break;

                case "lobby.openWorld":
                    HandleLobbyOpenWorld(msg);
                    break;

                case "lobby.join":
                    await HandleLobbyJoinAsync(msg);
                    break;

                case "lobby.play":
                    HandleLobbyPlay(msg);
                    break;

                case "lobby.kick":
                    await HandleLobbyKickAsync(msg);
                    break;

                case "lobby.leave":
                    await HandleLobbyLeaveAsync(msg);
                    break;

                case "friends.invite":
                    await HandleFriendsInviteAsync(msg);
                    break;

                case "friends.respondInvite":
                    await HandleFriendsRespondInviteAsync(msg);
                    break;

                case "cache.clear":
                    await HandleCacheClearAsync(msg);
                    break;

                default:
                    _logger($"[WEBBRIDGE: WARN] Unknown command: {msg.Name}");
                    SendError(msg.Id, "unknown.command", $"Command '{msg.Name}' is not implemented yet");
                    break;
            }
        }
        catch (Exception ex)
        {
            _logger($"[WEBBRIDGE: ERROR] Error handling {msg.Name}: {ex.Message}");
            SendError(msg.Id, msg.Name, ex.Message);
        }
    }

    private Task HandleGameLaunchAsync(BridgeMessage msg)
    {
        if (_gameSessionService.State != GameSessionState.Idle && _gameSessionService.State != GameSessionState.Failed)
        {
            _logger("[WEBBRIDGE: WARN] game.launch rejected: already_launching");
            SendError(msg.Id, msg.Name, "already_launching");
            return Task.CompletedTask;
        }

        string nickname = _configService.CurrentConfig.Nickname?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(nickname) || !NicknameValidator.Validate(nickname).IsValid)
        {
            _logger("[WEBBRIDGE: WARN] game.launch rejected: invalid_nickname");
            SendError(msg.Id, msg.Name, "invalid_nickname");
            return Task.CompletedTask;
        }

        // Сразу в мир (Мастерская) или на сервер (облачный мир) — как MainViewModel.LaunchGameAsync
        string world = ReadString(msg, "world");
        string server = ReadString(msg, "server");
        LaunchOptions? options = null;
        if (!string.IsNullOrWhiteSpace(world)) options = new LaunchOptions { QuickPlaySingleplayer = world };
        else if (!string.IsNullOrWhiteSpace(server)) options = new LaunchOptions { QuickPlayMultiplayer = server };

        // Асинхронное подтверждение приема команды на запуск
        SendResponse(msg.Id, msg.Name, new { accepted = true });
        StartGameLaunch(options);
        return Task.CompletedTask;
    }

    /// <summary>Запуск игры с прогрессом в интерфейс (game.progress / game.status).</summary>
    private void StartGameLaunch(LaunchOptions? options)
    {
        _gameLaunchCts?.Cancel();
        _gameLaunchCts = new CancellationTokenSource();

        var progress = new Progress<LaunchProgress>(p =>
        {
            SendEvent("game.progress", new
            {
                stage = p.Stage ?? string.Empty,
                percentage = p.Percentage,
                detail = p.DetailText ?? string.Empty,
                speed = p.FormattedSpeed ?? string.Empty,
                remaining = p.FormattedRemainingTime ?? string.Empty
            });
        });

        _ = Task.Run(async () =>
        {
            try
            {
                _logger("[WEBBRIDGE] Starting GameSessionService.LaunchAsync...");
                var process = await _gameSessionService.LaunchAsync(
                    options: options,
                    progress: progress,
                    cancellationToken: _gameLaunchCts.Token);

                _logger($"[WEBBRIDGE] Game launched successfully! PID={process.Id}");
            }
            catch (Exception ex)
            {
                _logger($"[WEBBRIDGE: ERROR] Launch failed: {ex.Message}");
                SendEvent("game.status", new
                {
                    state = "failed",
                    isRunning = false,
                    version = (string?)null,
                    processId = (int?)null,
                    exitCode = -1,
                    error = ex.Message
                });
            }
        });
    }

    private async Task HandleGameStopAsync(BridgeMessage msg)
    {
        _logger("[WEBBRIDGE] game.stop requested");
        _gameLaunchCts?.Cancel();
        bool stopped = await _gameSessionService.StopAsync(5000);
        _logger($"[WEBBRIDGE] game.stop completed: stopped={stopped}");
        SendResponse(msg.Id, msg.Name, new { stopped });
    }

    private void HandleWindowAction(BridgeMessage msg)
    {
        string action = string.Empty;
        if (msg.Payload is JsonElement el && el.TryGetProperty("action", out var prop))
        {
            action = prop.GetString() ?? string.Empty;
        }

        _logger($"[WEBBRIDGE] window.action: {action}");

        if (_window != null)
        {
            _window.Dispatcher.Invoke(() =>
            {
                switch (action)
                {
                    case "minimize":
                        _window.WindowState = WindowState.Minimized;
                        break;
                    case "maximize":
                        _window.WindowState = _window.WindowState == WindowState.Maximized
                            ? WindowState.Normal
                            : WindowState.Maximized;
                        break;
                    case "close":
                        _window.Close();
                        break;
                    case "drag":
                        try { _window.DragMove(); } catch { }
                        break;
                }
            });
        }

        SendResponse(msg.Id, msg.Name, new { action, success = true });
    }

    private void HandleConfigGet(BridgeMessage msg)
    {
        var cfg = _configService.CurrentConfig;
        string normVer = GameVersionProfiles.NormalizeVersion(cfg.SelectedVersion);
        _logger($"[WEBBRIDGE] config.get: ram={cfg.RamMb}, preset={cfg.GraphicsPreset}, fx={cfg.VisualEffectsMode}, ver={normVer}, dir={cfg.GameDir}");
        SendResponse(msg.Id, msg.Name, new
        {
            nickname = cfg.Nickname,
            ramMb = cfg.RamMb,
            hideLauncherWhilePlaying = cfg.HideLauncherWhilePlaying,
            fastStartEnabled = cfg.FastStartEnabled,
            graphicsPreset = cfg.GraphicsPreset,
            visualEffectsMode = cfg.VisualEffectsMode,
            theme = string.IsNullOrWhiteSpace(cfg.Theme) ? "frost" : cfg.Theme,
            selectedVersion = normVer,
            gameDir = cfg.GameDir,
            autoUpdate = cfg.AutoUpdateEnabled,
            discordRpc = cfg.DiscordRpcEnabled
        });
    }

    private async Task HandleConfigSetAsync(BridgeMessage msg)
    {
        if (msg.Payload is not JsonElement el || el.ValueKind != JsonValueKind.Object)
        {
            SendError(msg.Id, msg.Name, "invalid_payload");
            return;
        }

        if (!el.TryGetProperty("key", out var keyProp) || keyProp.ValueKind != JsonValueKind.String)
        {
            SendError(msg.Id, msg.Name, "invalid_key");
            return;
        }

        string key = keyProp.GetString() ?? string.Empty;
        if (!el.TryGetProperty("value", out var valProp))
        {
            SendError(msg.Id, msg.Name, "invalid_value");
            return;
        }

        switch (key)
        {
            case "ramMb":
            {
                if (valProp.ValueKind != JsonValueKind.Number || !valProp.TryGetInt32(out int ramVal))
                {
                    _logger($"[WEBBRIDGE: WARN] config.set ramMb rejected: not an integer");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                int maxRam = GraphicsPresetService.GetPhysicalRamMb();
                if (ramVal < 2048 || ramVal > maxRam)
                {
                    _logger($"[WEBBRIDGE: WARN] config.set ramMb rejected: {ramVal} outside 2048..{maxRam}");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                await _configService.UpdateConfigAsync(c => c.RamMb = ramVal);
                _logger($"[WEBBRIDGE] config.set ramMb -> {ramVal}");
                break;
            }

            case "hideLauncherWhilePlaying":
            {
                if (valProp.ValueKind != JsonValueKind.True && valProp.ValueKind != JsonValueKind.False)
                {
                    _logger($"[WEBBRIDGE: WARN] config.set hideLauncherWhilePlaying rejected: not a bool");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                bool hideVal = valProp.GetBoolean();
                await _configService.UpdateConfigAsync(c => c.HideLauncherWhilePlaying = hideVal);
                _logger($"[WEBBRIDGE] config.set hideLauncherWhilePlaying -> {hideVal}");
                break;
            }

            case "fastStartEnabled":
            {
                if (valProp.ValueKind != JsonValueKind.True && valProp.ValueKind != JsonValueKind.False)
                {
                    _logger($"[WEBBRIDGE: WARN] config.set fastStartEnabled rejected: not a bool");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                bool fastVal = valProp.GetBoolean();
                await _configService.UpdateConfigAsync(c => c.FastStartEnabled = fastVal);
                _logger($"[WEBBRIDGE] config.set fastStartEnabled -> {fastVal}");
                if (!fastVal)
                {
                    _prewarmService?.StopPrewarm();
                }
                else
                {
                    _ = _prewarmService?.StartPrewarmAsync();
                }
                break;
            }

            case "visualEffectsMode":
            {
                if (valProp.ValueKind != JsonValueKind.String)
                {
                    _logger($"[WEBBRIDGE: WARN] config.set visualEffectsMode rejected: not a string");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                string fxVal = valProp.GetString() ?? string.Empty;
                if (fxVal != "full" && fxVal != "low" && fxVal != "off")
                {
                    _logger($"[WEBBRIDGE: WARN] config.set visualEffectsMode rejected: unknown '{fxVal}'");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                await _configService.UpdateConfigAsync(c => c.VisualEffectsMode = fxVal);
                _logger($"[WEBBRIDGE] config.set visualEffectsMode -> {fxVal}");
                break;
            }

            case "theme":
            {
                if (valProp.ValueKind != JsonValueKind.String)
                {
                    _logger($"[WEBBRIDGE: WARN] config.set theme rejected: not a string");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                string themeVal = valProp.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
                if (string.IsNullOrEmpty(themeVal) || !System.Text.RegularExpressions.Regex.IsMatch(themeVal, "^[a-z0-9_-]{3,32}$"))
                {
                    _logger($"[WEBBRIDGE: WARN] config.set theme rejected: invalid identifier '{themeVal}'");
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                await _configService.UpdateConfigAsync(c => c.Theme = themeVal);
                _logger($"[WEBBRIDGE] config.set theme -> {themeVal}");
                break;
            }

            case "autoUpdate":
            case "discordRpc":
            {
                if (valProp.ValueKind != JsonValueKind.True && valProp.ValueKind != JsonValueKind.False)
                {
                    SendError(msg.Id, msg.Name, "invalid_value");
                    return;
                }

                bool on = valProp.GetBoolean();
                if (key == "autoUpdate")
                {
                    await _configService.UpdateConfigAsync(c => c.AutoUpdateEnabled = on);
                    if (on && _updatePollCts == null) StartUpdatePolling();
                    if (!on) StopUpdatePolling();
                }
                else
                {
                    await _configService.UpdateConfigAsync(c => c.DiscordRpcEnabled = on);
                    _discordRpcService?.UpdateSettings();
                    if (on) UpdatePresence();
                }
                _logger($"[WEBBRIDGE] config.set {key} -> {on}");
                break;
            }

            default:
                _logger($"[WEBBRIDGE: WARN] config.set rejected: unknown key '{key}'");
                SendError(msg.Id, msg.Name, "invalid_key");
                return;
        }

        var cfg = _configService.CurrentConfig;
        SendResponse(msg.Id, msg.Name, new
        {
            nickname = cfg.Nickname,
            ramMb = cfg.RamMb,
            hideLauncherWhilePlaying = cfg.HideLauncherWhilePlaying,
            graphicsPreset = cfg.GraphicsPreset,
            visualEffectsMode = cfg.VisualEffectsMode,
            theme = cfg.Theme,
            autoUpdate = cfg.AutoUpdateEnabled,
            discordRpc = cfg.DiscordRpcEnabled
        });
    }

    private string GetInstanceDir()
    {
        var cfg = _configService.CurrentConfig;
        if (_launchService != null)
        {
            return _launchService.ResolveInstanceDirectory(cfg.GameDir, cfg.SelectedVersion);
        }
        return cfg.GameDir;
    }

    private void HandlePresetApply(BridgeMessage msg)
    {
        if (_presetService == null)
        {
            _logger("[WEBBRIDGE: ERROR] preset.apply failed: IGraphicsPresetService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        string presetKey = string.Empty;
        if (msg.Payload is JsonElement el && el.TryGetProperty("presetKey", out var prop) && prop.ValueKind == JsonValueKind.String)
        {
            presetKey = prop.GetString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(presetKey))
        {
            _logger("[WEBBRIDGE: WARN] preset.apply rejected: empty presetKey");
            SendError(msg.Id, msg.Name, "invalid_preset");
            return;
        }

        _logger($"[WEBBRIDGE] preset.apply: {presetKey}");

        string instanceDir = GetInstanceDir();
        var result = _presetService.ApplyPreset(instanceDir, presetKey, persistConfig: true);

        if (result.Success && _prewarmService?.PrewarmedProcess != null)
        {
            _ = _prewarmService.RestartPrewarmAsync();
        }

        _logger($"[WEBBRIDGE] preset.apply result: success={result.Success}, ram={result.FinalRamMb}, shader={result.ChosenShader}");
        SendResponse(msg.Id, msg.Name, result);
    }

    public void SendEvent(string eventName, object payload)
    {
        Post(new BridgeMessage
        {
            Type = "event",
            Name = eventName,
            Payload = payload
        });
    }

    public void SendResponse(string? id, string name, object payload)
    {
        Post(new BridgeMessage
        {
            Id = id,
            Type = "response",
            Name = name,
            Payload = payload
        });
    }

    public void SendError(string? id, string name, string error)
    {
        Post(new BridgeMessage
        {
            Id = id,
            Type = "response",
            Name = name,
            Error = error
        });
    }

    private async Task HandleWorkshopListAsync(BridgeMessage msg)
    {
        if (_workshopService == null)
        {
            _logger("[WEBBRIDGE: ERROR] workshop.list failed: IWorkshopService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        string instanceDir = GetInstanceDir();
        _logger($"[WEBBRIDGE] workshop.list requesting for {instanceDir}");

        var shaderPacks = await _workshopService.GetShaderPacksAsync(instanceDir);
        var mods = await _workshopService.GetModsAsync(instanceDir);

        var shaderList = shaderPacks.Select(s => new
        {
            fileName = s.FileName,
            name = s.Name,
            isActive = s.IsActive
        }).ToList();

        var modList = mods.Select(m => new
        {
            fileName = m.FileName,
            name = m.DisplayName,
            version = m.Version,
            enabled = m.IsEnabled
        }).ToList();

        _logger($"[WEBBRIDGE] workshop.list response: {shaderList.Count} shaders, {modList.Count} mods");
        SendResponse(msg.Id, msg.Name, new
        {
            shaders = shaderList,
            mods = modList
        });
    }

    private async Task HandleWorkshopSetShaderAsync(BridgeMessage msg)
    {
        if (_workshopService == null)
        {
            _logger("[WEBBRIDGE: ERROR] workshop.setShader failed: IWorkshopService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        string fileName = string.Empty;
        if (msg.Payload is JsonElement el && el.TryGetProperty("fileName", out var fnProp) && fnProp.ValueKind == JsonValueKind.String)
        {
            fileName = fnProp.GetString() ?? string.Empty;
        }

        string instanceDir = GetInstanceDir();
        _logger($"[WEBBRIDGE] workshop.setShader fileName='{fileName}'");

        if (string.IsNullOrWhiteSpace(fileName) || fileName.Equals("none", StringComparison.OrdinalIgnoreCase) || fileName.Equals("off", StringComparison.OrdinalIgnoreCase))
        {
            _workshopService.SetActiveShaderPack(instanceDir, "OFF");
            _logger("[WEBBRIDGE] workshop.setShader disabled shaders (OFF)");
            SendResponse(msg.Id, msg.Name, new { fileName = "", active = false });
            return;
        }

        var packs = await _workshopService.GetShaderPacksAsync(instanceDir);
        var targetPack = packs.FirstOrDefault(p => string.Equals(p.FileName, fileName, StringComparison.OrdinalIgnoreCase) || string.Equals(p.Name, fileName, StringComparison.OrdinalIgnoreCase));
        if (targetPack == null)
        {
            _logger($"[WEBBRIDGE: WARN] workshop.setShader rejected: invalid_shader '{fileName}'");
            SendError(msg.Id, msg.Name, "invalid_shader");
            return;
        }

        _workshopService.SetActiveShaderPack(instanceDir, targetPack.FileName);
        _logger($"[WEBBRIDGE] workshop.setShader activated '{targetPack.FileName}'");
        SendResponse(msg.Id, msg.Name, new { fileName = targetPack.FileName, active = true });
    }

    private async Task HandleWorkshopToggleModAsync(BridgeMessage msg)
    {
        if (_workshopService == null)
        {
            _logger("[WEBBRIDGE: ERROR] workshop.toggleMod failed: IWorkshopService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        string fileName = string.Empty;
        bool enabled = false;

        if (msg.Payload is JsonElement el)
        {
            if (el.TryGetProperty("fileName", out var fnProp) && fnProp.ValueKind == JsonValueKind.String)
            {
                fileName = fnProp.GetString() ?? string.Empty;
            }
            if (el.TryGetProperty("enabled", out var enProp) && (enProp.ValueKind == JsonValueKind.True || enProp.ValueKind == JsonValueKind.False))
            {
                enabled = enProp.GetBoolean();
            }
            else
            {
                _logger("[WEBBRIDGE: WARN] workshop.toggleMod rejected: missing enabled");
                SendError(msg.Id, msg.Name, "invalid_payload");
                return;
            }
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            _logger("[WEBBRIDGE: WARN] workshop.toggleMod rejected: empty fileName");
            SendError(msg.Id, msg.Name, "invalid_payload");
            return;
        }

        string instanceDir = GetInstanceDir();
        _logger($"[WEBBRIDGE] workshop.toggleMod fileName='{fileName}', targetEnabled={enabled}");

        static string GetBaseName(string name)
        {
            if (name.EndsWith(".disabled", StringComparison.OrdinalIgnoreCase))
            {
                return name[..^9];
            }
            return name;
        }

        string baseSearchName = GetBaseName(fileName);

        var mods = await _workshopService.GetModsAsync(instanceDir);
        var mod = mods.FirstOrDefault(m => string.Equals(m.FileName, fileName, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(GetBaseName(m.FileName), baseSearchName, StringComparison.OrdinalIgnoreCase)
                                        || string.Equals(m.DisplayName, fileName, StringComparison.OrdinalIgnoreCase));

        if (mod == null)
        {
            _logger($"[WEBBRIDGE: WARN] workshop.toggleMod rejected: mod_not_found '{fileName}'");
            SendError(msg.Id, msg.Name, "mod_not_found");
            return;
        }

        bool success = _workshopService.ToggleMod(mod, enabled);
        if (!success)
        {
            _logger($"[WEBBRIDGE: ERROR] workshop.toggleMod failed to toggle '{fileName}' to {enabled}");
            SendError(msg.Id, msg.Name, "toggle_failed");
            return;
        }

        _logger($"[WEBBRIDGE] workshop.toggleMod toggled: '{mod.FileName}', enabled={mod.IsEnabled}");
        SendResponse(msg.Id, msg.Name, new { fileName = mod.FileName, enabled = mod.IsEnabled });
    }

    private async Task HandlePackSyncAsync(BridgeMessage msg)
    {
        if (_packUpdateService == null)
        {
            _logger("[WEBBRIDGE: ERROR] pack.sync failed: IPackUpdateService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        if (_isSyncing || _gameSessionService.State == GameSessionState.Preparing || _gameSessionService.State == GameSessionState.Launching)
        {
            _logger("[WEBBRIDGE: WARN] pack.sync rejected: busy");
            SendError(msg.Id, msg.Name, "busy");
            return;
        }

        _isSyncing = true;
        _packSyncCts = new CancellationTokenSource();
        _logger("[WEBBRIDGE] pack.sync started");

        _ = Task.Run(async () =>
        {
            try
            {
                var progress = new Progress<DownloadProgressReport>(report =>
                {
                    SendEvent("pack.progress", new
                    {
                        percentage = Math.Round(report.Percentage, 1),
                        detail = string.IsNullOrWhiteSpace(report.CurrentFileName) ? report.StatusText : $"{report.StatusText} ({report.CurrentFileName})"
                    });
                });

                var result = await _packUpdateService.CheckAndApplyAsync(progress, _packSyncCts.Token);
                _logger($"[WEBBRIDGE] pack.sync finished: status={result.Status}, filesChanged={result.FilesChanged}, msg={result.Message}");

                SendResponse(msg.Id, msg.Name, new
                {
                    status = result.Status.ToString().ToLowerInvariant(),
                    message = result.Message,
                    filesChanged = result.FilesChanged
                });
            }
            catch (Exception ex)
            {
                _logger($"[WEBBRIDGE: ERROR] pack.sync failed: {ex.Message}");
                SendError(msg.Id, msg.Name, ex.Message);
            }
            finally
            {
                _isSyncing = false;
            }
        });

        await Task.CompletedTask;
    }

    private void HandleFsOpenModsFolder(BridgeMessage msg)
    {
        string instanceDir = GetInstanceDir();
        string modsDir = Path.Combine(instanceDir, "mods");
        Directory.CreateDirectory(modsDir);

        _logger($"[WEBBRIDGE] fs.openModsFolder: {modsDir}");

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{modsDir}\"",
                UseShellExecute = true
            });
            SendResponse(msg.Id, msg.Name, new { success = true, path = modsDir });
        }
        catch (Exception ex)
        {
            _logger($"[WEBBRIDGE: ERROR] fs.openModsFolder failed: {ex.Message}");
            SendError(msg.Id, msg.Name, ex.Message);
        }
    }

    private void HandleFsOpenGameFolder(BridgeMessage msg)
    {
        string? gameDir = _configService?.CurrentConfig?.GameDir;
        if (string.IsNullOrWhiteSpace(gameDir) || !Directory.Exists(gameDir))
        {
            gameDir = _skinService?.GetProfileDataDir() ?? _configService?.ProfileDataDir ?? Environment.GetEnvironmentVariable("AURA_PROFILE_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura");
        }
        Directory.CreateDirectory(gameDir);

        _logger($"[WEBBRIDGE] fs.openGameFolder: {gameDir}");

        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "explorer.exe",
                Arguments = $"\"{gameDir}\"",
                UseShellExecute = true
            });
            SendResponse(msg.Id, msg.Name, new { success = true, path = gameDir });
        }
        catch (Exception ex)
        {
            _logger($"[WEBBRIDGE: ERROR] fs.openGameFolder failed: {ex.Message}");
            SendError(msg.Id, msg.Name, ex.Message);
        }
    }

    private string CopySkinToPersistentStorage(string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return sourcePath;

            var baseDir = _skinService?.GetProfileDataDir() ?? _configService?.ProfileDataDir ?? Environment.GetEnvironmentVariable("AURA_PROFILE_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura");
            var skinsDir = Path.Combine(baseDir, "skins");
            Directory.CreateDirectory(skinsDir);

            var fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "skin.png";

            var targetPath = Path.Combine(skinsDir, fileName);
            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
            }
            return targetPath;
        }
        catch
        {
            return sourcePath;
        }
    }

    private void HandleSkinGet(BridgeMessage msg)
    {
        var cfg = _configService.CurrentConfig;
        string? dataUrl = null;
        string? capeDataUrl = null;

        if (!string.IsNullOrWhiteSpace(cfg.SkinPath) && File.Exists(cfg.SkinPath))
        {
            try
            {
                byte[] bytes = File.ReadAllBytes(cfg.SkinPath);
                dataUrl = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
            }
            catch (Exception ex)
            {
                _logger($"[WEBBRIDGE: WARN] skin.get failed to read SkinPath: {ex.Message}");
            }
        }

        if (cfg.CapeId == "custom" && !string.IsNullOrWhiteSpace(cfg.CapePath) && File.Exists(cfg.CapePath))
        {
            try
            {
                byte[] capeBytes = File.ReadAllBytes(cfg.CapePath);
                capeDataUrl = $"data:image/png;base64,{Convert.ToBase64String(capeBytes)}";
            }
            catch (Exception ex)
            {
                _logger($"[WEBBRIDGE: WARN] skin.get failed to read CapePath: {ex.Message}");
            }
        }

        string fileName = !string.IsNullOrWhiteSpace(cfg.SkinOriginalName)
            ? cfg.SkinOriginalName
            : (!string.IsNullOrWhiteSpace(cfg.SkinPath) ? Path.GetFileName(cfg.SkinPath) : "steve.png");

        _logger($"[WEBBRIDGE] skin.get: nick={cfg.Nickname}, model={cfg.SkinModel}, file={fileName}, hasSkin={dataUrl != null}, cape={cfg.CapeId}");
        SendResponse(msg.Id, msg.Name, new
        {
            nickname = cfg.Nickname,
            model = cfg.SkinModel,
            fileName = fileName,
            dataUrl = dataUrl,
            cape = string.IsNullOrWhiteSpace(cfg.CapeId) ? "none" : cfg.CapeId,
            capePath = cfg.CapePath,
            capeOriginalName = cfg.CapeOriginalName,
            capeDataUrl = capeDataUrl
        });
    }

    private async Task HandleSkinUploadAsync(BridgeMessage msg)
    {
        if (_skinService == null)
        {
            _logger("[WEBBRIDGE: ERROR] skin.upload failed: ISkinService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        string fileName = string.Empty;
        string base64Data = string.Empty;

        if (msg.Payload is JsonElement el)
        {
            if (el.TryGetProperty("fileName", out var fnProp) && fnProp.ValueKind == JsonValueKind.String)
                fileName = fnProp.GetString() ?? string.Empty;

            if (el.TryGetProperty("base64Data", out var b64Prop) && b64Prop.ValueKind == JsonValueKind.String)
                base64Data = b64Prop.GetString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(base64Data))
        {
            _logger("[WEBBRIDGE: WARN] skin.upload rejected: empty base64Data");
            SendError(msg.Id, msg.Name, "invalid_payload");
            return;
        }

        int commaIdx = base64Data.IndexOf(',');
        if (commaIdx >= 0)
        {
            base64Data = base64Data.Substring(commaIdx + 1);
        }

        byte[] rawBytes;
        try
        {
            rawBytes = Convert.FromBase64String(base64Data);
        }
        catch
        {
            _logger("[WEBBRIDGE: WARN] skin.upload rejected: invalid base64");
            SendError(msg.Id, msg.Name, "invalid_base64");
            return;
        }

        if (rawBytes.Length > 1024 * 1024)
        {
            _logger($"[WEBBRIDGE: WARN] skin.upload rejected: payload exceeds 1MB ({rawBytes.Length} bytes)");
            SendError(msg.Id, msg.Name, "file_too_large");
            return;
        }

        if (rawBytes.Length < 8 ||
            rawBytes[0] != 0x89 || rawBytes[1] != 0x50 || rawBytes[2] != 0x4E || rawBytes[3] != 0x47 ||
            rawBytes[4] != 0x0D || rawBytes[5] != 0x0A || rawBytes[6] != 0x1A || rawBytes[7] != 0x0A)
        {
            _logger("[WEBBRIDGE: WARN] skin.upload rejected: not a PNG file");
            SendError(msg.Id, msg.Name, "invalid_format");
            return;
        }

        var validation = _skinService.ValidateSkinBytes(rawBytes);
        if (!validation.IsValid)
        {
            string errMsg = validation.ErrorMessage ?? "Неподдерживаемый файл скина.";
            _logger($"[WEBBRIDGE: WARN] skin.upload validation failed: {errMsg}");
            SendError(msg.Id, msg.Name, errMsg);
            return;
        }

        string safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            safeFileName = $"skin_{Guid.NewGuid():N}.png";

        // Сохранение в managed storage профиля с SHA-256 дедупликацией
        var (persistentPath, hash) = await _skinService.SaveToManagedStorageAsync(rawBytes, "skin", safeFileName);

        bool isSlim = SkinService.DetectIsSlim(rawBytes);
        string detectedModel = isSlim ? "slim" : "default";

        var cfg = _configService.CurrentConfig;
        cfg.SkinPath = persistentPath;
        cfg.SkinOriginalName = safeFileName;
        cfg.SkinModel = detectedModel;

        // Обновляем историю гардероба
        cfg.WardrobeHistory ??= new List<WardrobeItem>();
        string itemId = $"skin_{hash}";
        cfg.WardrobeHistory.RemoveAll(x => string.Equals(x.Hash, hash, StringComparison.OrdinalIgnoreCase) && x.Type == "skin");

        var historyItem = new WardrobeItem
        {
            Id = itemId,
            Type = "skin",
            Name = safeFileName,
            Hash = hash,
            Model = detectedModel,
            RelativePath = Path.Combine("skins", $"{hash}.png").Replace('\\', '/'),
            CreatedUtc = DateTime.UtcNow
        };
        cfg.WardrobeHistory.Insert(0, historyItem);

        await _configService.SaveConfigAsync(cfg);

        await _skinService.SyncSkinToGameAsync(persistentPath, cfg.Nickname, cfg.GameDir);
        _skinService.EnsureCustomSkinLoaderConfig(cfg.GameDir);

        string? uploadError = null;
        try
        {
            var uploadRes = await _skinService.UploadSkinToLobbyApiAsync(
                cfg.SkinPath,
                cfg.Nickname,
                cfg.SkinModel,
                cfg.SkinOwnerToken,
                cfg.LobbyApiBaseUrl);

            if (uploadRes.Success)
            {
                if (!string.IsNullOrWhiteSpace(uploadRes.OwnerToken))
                {
                    cfg.SkinOwnerToken = uploadRes.OwnerToken;
                    await _configService.SaveConfigAsync(cfg);
                }
                _skinService.EnsureCustomSkinLoaderConfig(cfg.GameDir);
                _skinService.ClearCustomSkinLoaderCache(cfg.GameDir);
            }
            else
            {
                uploadError = uploadRes.ErrorMessage ?? "Ошибка загрузки скина в lobby-api";
                _logger($"[WEBBRIDGE: WARN] skin.upload lobby-api error: {uploadError}");
            }
        }
        catch (Exception ex)
        {
            uploadError = ex.Message;
            _logger($"[WEBBRIDGE: WARN] skin.upload lobby-api exception: {ex.Message}");
        }

        ReportAchievement("custom_skin");
        _logger($"[WEBBRIDGE] skin.upload finished: ok=true, fileName={safeFileName}, uploadError={uploadError}");
        SendResponse(msg.Id, msg.Name, new
        {
            ok = true,
            id = itemId,
            fileName = safeFileName,
            model = detectedModel,
            uploadError = uploadError
        });
    }

    private async Task HandleSkinSetModelAsync(BridgeMessage msg)
    {
        string model = string.Empty;
        if (msg.Payload is JsonElement el && el.TryGetProperty("model", out var mProp) && mProp.ValueKind == JsonValueKind.String)
        {
            model = mProp.GetString()?.Trim().ToLowerInvariant() ?? string.Empty;
        }

        if (model != "default" && model != "slim")
        {
            _logger($"[WEBBRIDGE: WARN] skin.setModel rejected: invalid_value '{model}'");
            SendError(msg.Id, msg.Name, "invalid_value");
            return;
        }

        var cfg = _configService.CurrentConfig;
        _logger($"[WEBBRIDGE] skin.setModel: {cfg.SkinModel} -> {model}");
        cfg.SkinModel = model;
        await _configService.SaveConfigAsync(cfg);

        string? uploadError = null;
        if (_skinService != null && !string.IsNullOrWhiteSpace(cfg.SkinPath) && File.Exists(cfg.SkinPath))
        {
            try
            {
                var res = await _skinService.UploadSkinToLobbyApiAsync(
                    cfg.SkinPath,
                    cfg.Nickname,
                    cfg.SkinModel,
                    cfg.SkinOwnerToken,
                    cfg.LobbyApiBaseUrl);

                if (res.Success)
                {
                    if (!string.IsNullOrWhiteSpace(res.OwnerToken))
                    {
                        cfg.SkinOwnerToken = res.OwnerToken;
                        await _configService.SaveConfigAsync(cfg);
                    }
                    _skinService.EnsureCustomSkinLoaderConfig(cfg.GameDir);
                    _skinService.ClearCustomSkinLoaderCache(cfg.GameDir);
                }
                else
                {
                    uploadError = res.ErrorMessage;
                }
            }
            catch (Exception ex)
            {
                uploadError = ex.Message;
            }
        }

        SendResponse(msg.Id, msg.Name, new
        {
            ok = true,
            model = cfg.SkinModel,
            uploadError = uploadError
        });
    }

    public event Action<BridgeMessage>? MessagePosted;

    private void Post(BridgeMessage msg)
    {
        MessagePosted?.Invoke(msg);
        if (_coreWebView2 == null) return;
        _window?.Dispatcher.Invoke(() =>
        {
            try
            {
                string json = JsonSerializer.Serialize(msg);
                _coreWebView2?.PostWebMessageAsJson(json);
            }
            catch { }
        });
    }

    public void Dispose()
    {
        _gameSessionService.StateChanged -= OnSessionStateChanged;
        _gameSessionService.GameExited -= OnSessionGameExited;
        if (_window != null)
        {
            _window.StateChanged -= OnWindowStateChanged;
        }
        if (_friendService != null)
        {
            _friendService.FriendsListUpdated -= OnFriendsListUpdated;
            _friendService.FriendRequestReceived -= OnFriendRequestReceived;
            _friendService.InviteReceived -= OnInviteReceived;
        }
        UnsubscribeLobby();
        UnsubscribeAchievements();
        StopUpdatePolling();
        if (_coreWebView2 != null)
        {
            _coreWebView2.WebMessageReceived -= OnWebMessageReceived;
        }
        _gameLaunchCts?.Cancel();
        _gameLaunchCts?.Dispose();
        _prewarmService?.StopPrewarm();
    }

    private void OnFriendsListUpdated(IReadOnlyList<FriendPresenceItem> friends)
    {
        _cachedFriends = friends ?? Array.Empty<FriendPresenceItem>();
        ReportAchievement("friends_count", _cachedFriends.Count);
        SendEvent("friends.presence", new
        {
            friends = _cachedFriends.Select(f => new
            {
                id = f.Id,
                nick = f.Nick,
                online = f.Online,
                status = f.Status,
                lobbyCode = f.LobbyCode
            })
        });
    }

    private void OnFriendRequestReceived(FriendRequestItem req)
    {
        SendEvent("friends.requestReceived", new
        {
            id = req.Id,
            nick = req.Nick
        });
    }

    private void OnInviteReceived(IncomingInviteItem inv)
    {
        SendEvent("friends.inviteReceived", new
        {
            inviteId = inv.InviteId,
            fromId = inv.FromId,
            fromNick = inv.FromNick
        });
    }

    private void HandleFriendsGet(BridgeMessage msg)
    {
        string? myCode = _friendService?.CurrentFriendCode;
        bool registered = _friendService?.IsRegistered ?? false;

        SendResponse(msg.Id, msg.Name, new
        {
            myCode = myCode,
            registered = registered,
            friends = _cachedFriends.Select(f => new
            {
                id = f.Id,
                nick = f.Nick,
                online = f.Online,
                status = f.Status,
                lobbyCode = f.LobbyCode
            })
        });
    }

    private void HandleFriendsSetTabActive(BridgeMessage msg)
    {
        bool active = false;
        if (msg.Payload is JsonElement elem)
        {
            if (elem.TryGetProperty("active", out var activeProp))
            {
                active = activeProp.GetBoolean();
            }
        }

        _logger($"[WEBBRIDGE] friends.setTabActive: active={active}");

        if (_friendService != null)
        {
            _friendService.IsFriendsTabActive = active;
        }

        SendResponse(msg.Id, msg.Name, new { ok = true });
    }

    private async Task HandleFriendsAddAsync(BridgeMessage msg)
    {
        if (_friendService == null)
        {
            SendError(msg.Id, msg.Name, "friend_service_unavailable");
            return;
        }

        string code = string.Empty;
        if (msg.Payload is JsonElement elem && elem.TryGetProperty("code", out var codeProp))
        {
            code = codeProp.GetString()?.Trim() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(code))
        {
            SendError(msg.Id, msg.Name, "invalid_code");
            return;
        }

        var (success, error) = await _friendService.SendFriendRequestAsync(code);
        if (success)
        {
            SendResponse(msg.Id, msg.Name, new { ok = true });
        }
        else
        {
            SendResponse(msg.Id, msg.Name, new { ok = false, error = error ?? "unknown_error" });
        }
    }

    private async Task HandleFriendsRespondAsync(BridgeMessage msg)
    {
        if (_friendService == null)
        {
            SendError(msg.Id, msg.Name, "friend_service_unavailable");
            return;
        }

        string fromId = string.Empty;
        bool accept = false;

        if (msg.Payload is JsonElement elem)
        {
            if (elem.TryGetProperty("fromId", out var idProp))
            {
                fromId = idProp.GetString() ?? string.Empty;
            }
            if (elem.TryGetProperty("accept", out var acceptProp))
            {
                accept = acceptProp.GetBoolean();
            }
        }

        if (string.IsNullOrWhiteSpace(fromId))
        {
            SendError(msg.Id, msg.Name, "invalid_from_id");
            return;
        }

        bool success = await _friendService.RespondFriendRequestAsync(fromId, accept);
        SendResponse(msg.Id, msg.Name, new { ok = success });
    }

    private async Task HandleGameSetVersionAsync(BridgeMessage msg)
    {
        if (_gameSessionService.State != GameSessionState.Idle && _gameSessionService.State != GameSessionState.Failed)
        {
            _logger("[WEBBRIDGE: WARN] game.setVersion rejected: busy");
            SendError(msg.Id, msg.Name, "busy");
            return;
        }

        string? ver = null;
        if (msg.Payload is JsonElement elem && elem.TryGetProperty("version", out var verProp))
        {
            ver = verProp.GetString()?.Trim();
        }

        if (ver != GameVersionProfiles.Version120 && ver != GameVersionProfiles.Version121)
        {
            _logger($"[WEBBRIDGE: WARN] game.setVersion rejected: invalid_value ({ver})");
            SendError(msg.Id, msg.Name, "invalid_value");
            return;
        }

        await _configService.UpdateConfigAsync(c => c.SelectedVersion = ver);
        if (_prewarmService?.PrewarmedProcess != null)
        {
            _ = _prewarmService.RestartPrewarmAsync();
        }
        var cfg = _configService.CurrentConfig;
        string normVer = GameVersionProfiles.NormalizeVersion(cfg.SelectedVersion);
        _logger($"[WEBBRIDGE] game.setVersion -> {normVer}");

        SendResponse(msg.Id, msg.Name, new
        {
            nickname = cfg.Nickname,
            ramMb = cfg.RamMb,
            hideLauncherWhilePlaying = cfg.HideLauncherWhilePlaying,
            fastStartEnabled = cfg.FastStartEnabled,
            graphicsPreset = cfg.GraphicsPreset,
            visualEffectsMode = cfg.VisualEffectsMode,
            theme = string.IsNullOrWhiteSpace(cfg.Theme) ? "frost" : cfg.Theme,
            selectedVersion = normVer,
            gameDir = cfg.GameDir,
            autoUpdate = cfg.AutoUpdateEnabled,
            discordRpc = cfg.DiscordRpcEnabled
        });
    }

    private async Task HandleFsPickGameFolderAsync(BridgeMessage msg)
    {
        _logger("[WEBBRIDGE] fs.pickGameFolder requested");
        string currentDir = _configService.CurrentConfig.GameDir;
        string? chosenFolder = null;
        bool cancelled = true;

        if (_window != null)
        {
            await _window.Dispatcher.InvokeAsync(() =>
            {
                var dialog = new OpenFolderDialog
                {
                    Title = "Выберите рабочую папку игры",
                    Multiselect = false,
                    InitialDirectory = Directory.Exists(currentDir) ? currentDir : Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData)
                };

                if (dialog.ShowDialog(_window) == true && !string.IsNullOrWhiteSpace(dialog.FolderName))
                {
                    chosenFolder = dialog.FolderName;
                    cancelled = false;
                }
            });
        }

        if (!cancelled && !string.IsNullOrWhiteSpace(chosenFolder))
        {
            await _configService.UpdateConfigAsync(c => c.GameDir = chosenFolder);
            _logger($"[WEBBRIDGE] fs.pickGameFolder selected: {chosenFolder}");
            SendResponse(msg.Id, msg.Name, new { gameDir = chosenFolder });
        }
        else
        {
            _logger("[WEBBRIDGE] fs.pickGameFolder cancelled");
            SendResponse(msg.Id, msg.Name, new { cancelled = true });
        }
    }

    private async Task HandleLogsExportAsync(BridgeMessage msg)
    {
        _logger("[WEBBRIDGE] logs.export requested");
        if (_reportService == null)
        {
            SendError(msg.Id, msg.Name, "report_service_unavailable");
            return;
        }

        string reportPath = await _reportService.GenerateReportZipAsync();
        _logger($"[WEBBRIDGE] logs.export generated: {reportPath}");
        SendResponse(msg.Id, msg.Name, new { path = reportPath });
    }

    private async Task HandleCacheClearAsync(BridgeMessage msg)
    {
        _logger("[WEBBRIDGE] cache.clear requested");
        string instanceDir = GetInstanceDir();

        long freedBytes = await Task.Run(() =>
        {
            long total = 0;
            var logsDir = Path.Combine(instanceDir, "logs");
            if (Directory.Exists(logsDir))
            {
                foreach (var f in Directory.GetFiles(logsDir, "*.log.gz"))
                {
                    try
                    {
                        var len = new FileInfo(f).Length;
                        File.Delete(f);
                        total += len;
                    }
                    catch { }
                }
            }

            var crashDir = Path.Combine(instanceDir, "crash-reports");
            if (Directory.Exists(crashDir))
            {
                foreach (var f in Directory.GetFiles(crashDir, "*.*"))
                {
                    try
                    {
                        var fi = new FileInfo(f);
                        if (fi.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-7))
                        {
                            total += fi.Length;
                            fi.Delete();
                        }
                    }
                    catch { }
                }
            }

            return total;
        });

        _logger($"[WEBBRIDGE] cache.clear freed {freedBytes} bytes in {instanceDir}");
        SendResponse(msg.Id, msg.Name, new { freedBytes = freedBytes });
    }
}

using System;
using AuraLauncher.Services.Interfaces;
using DiscordRPC;
using DiscordRPC.Logging;

namespace AuraLauncher.Services.Implementations;

public class DiscordRpcService : IDiscordRpcService
{
    private readonly IConfigService _configService;
    private DiscordRpcClient? _client;
    private string? _currentClientAppId;
    private DateTime? _gameStartTime;
    private string _currentActivity = "launcher";
    private int _lobbyPlayerCount = 1;

    public DiscordRpcService(IConfigService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public void Initialize()
    {
        UpdateSettings();
    }

    public void UpdateSettings()
    {
        var config = _configService.CurrentConfig;
        string? appId = config?.DiscordAppId?.Trim();
        bool enabled = (config?.DiscordRpcEnabled ?? true) && !string.IsNullOrWhiteSpace(appId);

        if (!enabled)
        {
            ShutdownClient();
            return;
        }

        if (_client != null && string.Equals(_currentClientAppId, appId, StringComparison.OrdinalIgnoreCase))
        {
            ApplyCurrentPresence();
            return;
        }

        ShutdownClient();

        try
        {
            _currentClientAppId = appId;
            _client = new DiscordRpcClient(appId);
            _client.Initialize();
            ApplyCurrentPresence();
            FabricGameLaunchService.LogLauncherEvent($"[DISCORD RPC] Initialized with AppId: {appId}");
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[DISCORD RPC: ERROR] {ex.Message}");
            ShutdownClient();
        }
    }

    private string? _lobbyHostName;
    private string? _lobbyCode;
    private string? _playingWorldName;

    public void SetInLauncher()
    {
        _currentActivity = "launcher";
        _gameStartTime = null;
        ApplyCurrentPresence();
    }

    public void SetInLobby(int playerCount, string? hostName = null, string? lobbyCode = null)
    {
        _currentActivity = "lobby";
        _lobbyPlayerCount = Math.Max(1, playerCount);
        _lobbyHostName = hostName;
        _lobbyCode = lobbyCode;
        _gameStartTime = null;
        ApplyCurrentPresence();
    }

    public void SetPlayingGame(string? worldName = null, DateTime? startTime = null)
    {
        _currentActivity = "playing";
        _playingWorldName = worldName;
        _gameStartTime = startTime ?? _gameStartTime ?? DateTime.UtcNow;
        ApplyCurrentPresence();
    }

    private void ApplyCurrentPresence()
    {
        if (_client == null || !_client.IsInitialized) return;

        try
        {
            RichPresence presence;
            var buttons = new Button[]
            {
                new Button
                {
                    Label = "Скачать Aura Launcher",
                    Url = "https://github.com/qutlawsoasis-debug/Aura-Launcher"
                }
            };

            switch (_currentActivity)
            {
                case "playing":
                    presence = new RichPresence
                    {
                        Details = !string.IsNullOrWhiteSpace(_playingWorldName)
                            ? $"В мире: {_playingWorldName}"
                            : "В мире Minecraft (Fabric 1.20.1)",
                        State = "Сборка Aura Pack • 98 модов",
                        Timestamps = new Timestamps { Start = _gameStartTime ?? DateTime.UtcNow },
                        Assets = new Assets
                        {
                            LargeImageKey = "aura_logo",
                            LargeImageText = "Aura Launcher",
                            SmallImageKey = "minecraft",
                            SmallImageText = "Minecraft 1.20.1"
                        },
                        Buttons = buttons
                    };
                    break;

                case "lobby":
                    string lobbyDetails = !string.IsNullOrWhiteSpace(_lobbyCode)
                        ? $"В лобби #{_lobbyCode}"
                        : "В лобби мультиплеера";

                    string lobbyState = !string.IsNullOrWhiteSpace(_lobbyHostName)
                        ? $"Игроков: {_lobbyPlayerCount}/8 • Хост: {_lobbyHostName}"
                        : $"Игроков: {_lobbyPlayerCount}/8";

                    presence = new RichPresence
                    {
                        Details = lobbyDetails,
                        State = lobbyState,
                        Assets = new Assets
                        {
                            LargeImageKey = "aura_logo",
                            LargeImageText = "Aura Launcher",
                            SmallImageKey = "online",
                            SmallImageText = "P2P Multiplayer"
                        },
                        Buttons = buttons
                    };
                    break;

                case "launcher":
                default:
                    presence = new RichPresence
                    {
                        Details = "В главном меню",
                        State = "Выбирает режим игры",
                        Assets = new Assets
                        {
                            LargeImageKey = "aura_logo",
                            LargeImageText = "Aura Launcher"
                        },
                        Buttons = buttons
                    };
                    break;
            }

            _client.SetPresence(presence);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[DISCORD RPC: ERROR SetPresence] {ex.Message}");
        }
    }

    private void ShutdownClient()
    {
        if (_client != null)
        {
            try
            {
                _client.ClearPresence();
                _client.Dispose();
            }
            catch { }
            _client = null;
            _currentClientAppId = null;
        }
    }

    public void Dispose()
    {
        ShutdownClient();
    }
}

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

    public void SetInLauncher()
    {
        _currentActivity = "launcher";
        _gameStartTime = null;
        ApplyCurrentPresence();
    }

    public void SetInLobby(int playerCount)
    {
        _currentActivity = "lobby";
        _lobbyPlayerCount = Math.Max(1, playerCount);
        _gameStartTime = null;
        ApplyCurrentPresence();
    }

    public void SetPlayingGame(DateTime? startTime = null)
    {
        _currentActivity = "playing";
        _gameStartTime = startTime ?? _gameStartTime ?? DateTime.UtcNow;
        ApplyCurrentPresence();
    }

    private void ApplyCurrentPresence()
    {
        if (_client == null || !_client.IsInitialized) return;

        try
        {
            RichPresence presence;
            switch (_currentActivity)
            {
                case "playing":
                    presence = new RichPresence
                    {
                        Details = "Играет в Aura",
                        Timestamps = new Timestamps { Start = _gameStartTime ?? DateTime.UtcNow },
                        Assets = new Assets
                        {
                            LargeImageKey = "aura_logo",
                            LargeImageText = "Aura Launcher"
                        }
                    };
                    break;

                case "lobby":
                    presence = new RichPresence
                    {
                        Details = "В лобби",
                        State = $"Игроков: {_lobbyPlayerCount}",
                        Assets = new Assets
                        {
                            LargeImageKey = "aura_logo",
                            LargeImageText = "Aura Launcher"
                        }
                    };
                    break;

                case "launcher":
                default:
                    presence = new RichPresence
                    {
                        Details = "В лаунчере",
                        Assets = new Assets
                        {
                            LargeImageKey = "aura_logo",
                            LargeImageText = "Aura Launcher"
                        }
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

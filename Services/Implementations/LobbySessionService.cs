using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class LobbySessionService : ILobbySessionService
{
    private readonly ILobbyService _lobbyService;
    private readonly ITunnelProvider _tunnelProvider;
    private readonly ILanWorldWatcher _worldWatcher;
    private readonly IGameLaunchService _launchService;
    private readonly IGameSessionService _gameSessionService;
    private readonly IConfigService _configService;
    private readonly IServerListSyncService? _serverListSyncService;
    private readonly IMinecraftPingService? _pingService;
    private readonly IDiscordRpcService? _discordRpcService;
    private readonly IFriendService? _friendService;
    private readonly Action<string> _logger;

    private readonly object _stateLock = new();
    private readonly LobbyStateManager _stateManager;

    private LobbySessionRole _role = LobbySessionRole.None;
    private LobbySessionState _state = LobbySessionState.Idle;
    private string? _currentLobbyCode;
    private string? _currentTunnelAddress;
    private int? _currentTunnelPingMs;
    private string? _hostNick;
    private string? _lastError;
    private List<LobbyPlayerItemDto> _players = new();
    private IReadOnlyDictionary<string, PlayerModSyncInfo>? _modSync;

    private CancellationTokenSource? _pollCts;
    private Task? _pollTask;

    public LobbySessionRole Role
    {
        get { lock (_stateLock) return _role; }
        private set { lock (_stateLock) _role = value; }
    }

    public LobbySessionState State
    {
        get { lock (_stateLock) return _state; }
        private set
        {
            LobbySessionState old;
            lock (_stateLock)
            {
                if (_state == value) return;
                old = _state;
                _state = value;
            }
            _logger($"[LOBBY-SESSION] State: {old} -> {value}");
            StateChanged?.Invoke(this, value);
        }
    }

    public string? CurrentLobbyCode
    {
        get { lock (_stateLock) return _currentLobbyCode; }
        private set { lock (_stateLock) _currentLobbyCode = value; }
    }

    public string? CurrentTunnelAddress
    {
        get { lock (_stateLock) return _currentTunnelAddress; }
        private set { lock (_stateLock) _currentTunnelAddress = value; }
    }

    public int? CurrentTunnelPingMs
    {
        get { lock (_stateLock) return _currentTunnelPingMs; }
        private set { lock (_stateLock) _currentTunnelPingMs = value; }
    }

    public string? HostNick
    {
        get { lock (_stateLock) return _hostNick; }
        private set { lock (_stateLock) _hostNick = value; }
    }

    public string? LastError
    {
        get { lock (_stateLock) return _lastError; }
        private set { lock (_stateLock) _lastError = value; }
    }

    public IReadOnlyList<LobbyPlayerItemDto> Players
    {
        get { lock (_stateLock) return _players.ToList(); }
    }

    public IReadOnlyDictionary<string, PlayerModSyncInfo>? ModSync
    {
        get { lock (_stateLock) return _modSync; }
        private set { lock (_stateLock) _modSync = value; }
    }

    public event EventHandler<LobbySessionState>? StateChanged;
    public event EventHandler<LobbyStatusDto>? LobbyUpdated;
    public event EventHandler<string>? TunnelAddressReady;
    public event EventHandler<int>? TunnelPingUpdated;
    public event EventHandler<string>? PlayerKicked;
    public event EventHandler<string>? ErrorOccurred;

    public LobbySessionService(
        ILobbyService lobbyService,
        ITunnelProvider tunnelProvider,
        ILanWorldWatcher worldWatcher,
        IGameLaunchService launchService,
        IGameSessionService gameSessionService,
        IConfigService configService,
        IServerListSyncService? serverListSyncService = null,
        IMinecraftPingService? pingService = null,
        IDiscordRpcService? discordRpcService = null,
        IFriendService? friendService = null,
        Action<string>? logger = null)
    {
        _lobbyService = lobbyService ?? throw new ArgumentNullException(nameof(lobbyService));
        _tunnelProvider = tunnelProvider ?? throw new ArgumentNullException(nameof(tunnelProvider));
        _worldWatcher = worldWatcher ?? throw new ArgumentNullException(nameof(worldWatcher));
        _launchService = launchService ?? throw new ArgumentNullException(nameof(launchService));
        _gameSessionService = gameSessionService ?? throw new ArgumentNullException(nameof(gameSessionService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _serverListSyncService = serverListSyncService;
        _pingService = pingService;
        _discordRpcService = discordRpcService;
        _friendService = friendService;
        _logger = logger ?? FabricGameLaunchService.LogLauncherEvent;

        _stateManager = new LobbyStateManager(_worldWatcher, _tunnelProvider);
        _stateManager.StateChanged += OnLobbyStateChanged;

        _lobbyService.TunnelAddressReady += OnLobbyTunnelAddressReady;
        _lobbyService.LobbyStatusUpdated += OnLobbyStatusUpdated;
        _lobbyService.ModSyncUpdated += OnLobbyModSyncUpdated;
        _lobbyService.KickedFromLobby += OnLobbyKicked;

        _gameSessionService.GameExited += OnGameExited;
    }

    private void SetError(string error)
    {
        LastError = error;
        State = LobbySessionState.Error;
        ErrorOccurred?.Invoke(this, error);
    }

    private string GetGameDir()
    {
        var cfg = _configService.CurrentConfig;
        return _launchService.ResolveInstanceDirectory(cfg.GameDir, cfg.SelectedVersion);
    }

    public async Task<string?> CreateLobbyAsync(string hostName, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostName))
        {
            hostName = _configService.CurrentConfig.Nickname ?? "Player";
        }

        await LeaveLobbyAsync(cancellationToken).ConfigureAwait(false);

        State = LobbySessionState.Creating;
        LastError = null;

        try
        {
            string? code = await _lobbyService.CreateLobbyAsHostAsync(hostName, cancellationToken).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(code))
            {
                SetError("Не удалось создать лобби на сервере");
                return null;
            }

            lock (_stateLock)
            {
                _role = LobbySessionRole.Host;
                _currentLobbyCode = code;
                _hostNick = hostName;
                _players = new List<LobbyPlayerItemDto>
                {
                    new(hostName, IsHost: true, IsReady: true, Status: "host")
                };
            }

            _friendService?.UpdateLobbyState(true, code);
            _discordRpcService?.SetInLobby(1);

            State = LobbySessionState.WaitingForWorld;
            RaiseLobbyUpdated();

            try
            {
                string gameDir = GetGameDir();
                string logPath = Path.Combine(gameDir, "logs", "latest.log");
                _worldWatcher.Start(logPath, readFromEnd: true, scanRecentTail: _launchService.IsGameRunning);
            }
            catch (Exception ex)
            {
                _logger($"[LOBBY-SESSION: WARN] Failed to start log watcher: {ex.Message}");
            }

            StartPolling(isHost: true);
            return code;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            return null;
        }
    }

    public async Task<bool> JoinByCodeAsync(string code, string guestNick, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            SetError("Код лобби не указан");
            return false;
        }

        string cleanCode = code.Trim().ToUpperInvariant();
        if (cleanCode.Length < 6)
        {
            SetError("Неверный формат кода");
            return false;
        }

        if (string.IsNullOrWhiteSpace(guestNick))
        {
            guestNick = _configService.CurrentConfig.Nickname ?? "Player";
        }

        await LeaveLobbyAsync(cancellationToken).ConfigureAwait(false);

        State = LobbySessionState.Joining;
        LastError = null;

        try
        {
            bool joined = await _lobbyService.JoinLobbyAsGuestAsync(cleanCode, guestNick, cancellationToken).ConfigureAwait(false);
            if (!joined)
            {
                SetError($"Лобби с кодом {cleanCode} не найдено");
                return false;
            }

            lock (_stateLock)
            {
                _role = LobbySessionRole.Guest;
                _currentLobbyCode = cleanCode;
                _players = new List<LobbyPlayerItemDto>
                {
                    new(guestNick, IsHost: false, IsReady: true, Status: "joined")
                };
            }

            _friendService?.UpdateLobbyState(true, cleanCode);
            _discordRpcService?.SetInLobby(1);

            var status = await _lobbyService.RefreshGuestStatusAsync(cancellationToken).ConfigureAwait(false);
            if (status != null)
            {
                ApplyStatusResponse(status);
            }

            State = !string.IsNullOrWhiteSpace(CurrentTunnelAddress) ? LobbySessionState.ReadyToPlay : LobbySessionState.Joining;
            RaiseLobbyUpdated();

            StartPolling(isHost: false);
            return true;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            return false;
        }
    }

    public async Task OpenWorldAsync(int? localPort = null, CancellationToken cancellationToken = default)
    {
        if (Role != LobbySessionRole.Host)
        {
            SetError("Только хост может открывать мир");
            return;
        }

        State = LobbySessionState.TunnelStarting;
        LastError = null;

        try
        {
            bool started = await _stateManager.StartTunnelAsync(cancellationToken).ConfigureAwait(false);
            if (!started)
            {
                SetError(_stateManager.LastError ?? "Не удалось запустить туннель");
                return;
            }

            string? pubAddr = _stateManager.PublicAddress;
            int? pubPort = _stateManager.PublicPort;

            string fullAddress = (pubPort.HasValue && pubPort.Value > 0)
                ? $"{pubAddr}:{pubPort.Value}"
                : (pubAddr ?? string.Empty);

            if (string.IsNullOrWhiteSpace(fullAddress))
            {
                SetError("Публичный адрес туннеля не получен");
                return;
            }

            int port = localPort ?? _worldWatcher.CurrentPort ?? 25565;
            bool registered = await _lobbyService.HostOpenWorldAsync(fullAddress, port, cancellationToken).ConfigureAwait(false);
            if (!registered)
            {
                SetError("Не удалось зарегистрировать адрес лобби на сервере");
                return;
            }

            lock (_stateLock)
            {
                _currentTunnelAddress = fullAddress;
            }

            State = LobbySessionState.Active;
            TunnelAddressReady?.Invoke(this, fullAddress);
            RaiseLobbyUpdated();
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
        }
    }

    public async Task ConnectToGameAsync(CancellationToken cancellationToken = default)
    {
        string? address = CurrentTunnelAddress;
        if (string.IsNullOrWhiteSpace(address))
        {
            SetError("Адрес сервера ещё не доступен");
            return;
        }

        State = LobbySessionState.ConnectingToGame;

        try
        {
            if (_serverListSyncService != null)
            {
                string gameDir = GetGameDir();
                await _serverListSyncService.UpsertActiveLobbyServerAsync(gameDir, address, HostNick, CurrentLobbyCode).ConfigureAwait(false);
            }

            var options = new LaunchOptions
            {
                QuickPlayMultiplayer = address
            };

            await _gameSessionService.LaunchAsync(options, null, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            SetError($"Ошибка запуска игры: {ex.Message}");
        }
    }

    public async Task<bool> KickPlayerAsync(string playerNick, CancellationToken cancellationToken = default)
    {
        if (Role != LobbySessionRole.Host)
        {
            SetError("Только хост может исключать игроков");
            return false;
        }

        if (string.IsNullOrWhiteSpace(playerNick)) return false;

        try
        {
            bool kicked = await _lobbyService.KickPlayerAsync(playerNick, cancellationToken).ConfigureAwait(false);
            if (kicked)
            {
                lock (_stateLock)
                {
                    _players.RemoveAll(p => string.Equals(p.Nickname, playerNick, StringComparison.OrdinalIgnoreCase));
                }
                RaiseLobbyUpdated();
            }
            return kicked;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            return false;
        }
    }

    public async Task LeaveLobbyAsync(CancellationToken cancellationToken = default)
    {
        StopPolling();

        try
        {
            if (Role == LobbySessionRole.Host)
            {
                await _lobbyService.CloseLobbyAsHostAsync(cancellationToken).ConfigureAwait(false);
                await _stateManager.StopTunnelAsync(cancellationToken).ConfigureAwait(false);
            }
            else if (Role == LobbySessionRole.Guest)
            {
                _lobbyService.LeaveLobby();
            }
        }
        catch (Exception ex)
        {
            _logger($"[LOBBY-SESSION: WARN] Error during LeaveLobby: {ex.Message}");
        }

        try
        {
            if (_serverListSyncService != null)
            {
                string gameDir = GetGameDir();
                await _serverListSyncService.RemoveActiveLobbyServerAsync(gameDir).ConfigureAwait(false);
            }
        }
        catch { }

        _worldWatcher.Stop();

        lock (_stateLock)
        {
            _role = LobbySessionRole.None;
            _currentLobbyCode = null;
            _currentTunnelAddress = null;
            _currentTunnelPingMs = null;
            _hostNick = null;
            _lastError = null;
            _players.Clear();
            _modSync = null;
        }

        _friendService?.UpdateLobbyState(false, null);
        _discordRpcService?.SetInLauncher();

        State = LobbySessionState.Idle;
        RaiseLobbyUpdated();
    }

    private void StartPolling(bool isHost)
    {
        StopPolling();
        _pollCts = new CancellationTokenSource();
        var token = _pollCts.Token;

        _pollTask = Task.Run(async () =>
        {
            using var timer = new PeriodicTimer(TimeSpan.FromSeconds(3));
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await timer.WaitForNextTickAsync(token).ConfigureAwait(false);
                    if (token.IsCancellationRequested) break;

                    if (!isHost)
                    {
                        var status = await _lobbyService.RefreshGuestStatusAsync(token).ConfigureAwait(false);
                        if (status != null)
                        {
                            ApplyStatusResponse(status);
                            RaiseLobbyUpdated();
                        }
                    }

                    // Ping check if tunnel address is known
                    string? addr = CurrentTunnelAddress;
                    if (!string.IsNullOrWhiteSpace(addr) && _pingService != null)
                    {
                        var pingRes = await _pingService.PingServerAsync(addr, token).ConfigureAwait(false);
                        if (pingRes.HasPing && pingRes.PingMs.HasValue)
                        {
                            int ms = pingRes.PingMs.Value;
                            CurrentTunnelPingMs = ms;
                            TunnelPingUpdated?.Invoke(this, ms);
                        }
                    }
                }
                catch (OperationCanceledException) { break; }
                catch (Exception ex)
                {
                    _logger($"[LOBBY-SESSION: WARN] Polling tick error: {ex.Message}");
                }
            }
        }, token);
    }

    private void StopPolling()
    {
        if (_pollCts != null)
        {
            _pollCts.Cancel();
            _pollCts.Dispose();
            _pollCts = null;
        }
    }

    private void ApplyStatusResponse(LobbyStatusResponse status)
    {
        lock (_stateLock)
        {
            if (!string.IsNullOrWhiteSpace(status.HostName))
            {
                _hostNick = status.HostName;
            }

            if (!string.IsNullOrWhiteSpace(status.TunnelAddress))
            {
                _currentTunnelAddress = status.TunnelAddress;
                if (_role == LobbySessionRole.Guest && _state == LobbySessionState.Joining)
                {
                    _state = LobbySessionState.ReadyToPlay;
                }
            }

            var list = new List<LobbyPlayerItemDto>();
            if (!string.IsNullOrWhiteSpace(status.HostName))
            {
                list.Add(new LobbyPlayerItemDto(status.HostName, IsHost: true, IsReady: true, Status: "host"));
            }

            if (status.Players != null)
            {
                foreach (var p in status.Players)
                {
                    if (string.Equals(p, status.HostName, StringComparison.OrdinalIgnoreCase)) continue;
                    list.Add(new LobbyPlayerItemDto(p, IsHost: false, IsReady: true, Status: "player"));
                }
            }

            _players = list;
            _modSync = status.ModSync;
        }
    }

    private void RaiseLobbyUpdated()
    {
        LobbyStatusDto dto;
        lock (_stateLock)
        {
            dto = new LobbyStatusDto(
                Code: _currentLobbyCode ?? string.Empty,
                Role: _role,
                State: _state,
                HostName: _hostNick,
                TunnelAddress: _currentTunnelAddress,
                PingMs: _currentTunnelPingMs,
                Players: _players.ToList()
            );
        }
        LobbyUpdated?.Invoke(this, dto);
    }

    private void OnLobbyStateChanged(LobbyState state)
    {
        if (Role != LobbySessionRole.Host) return;

        switch (state)
        {
            case LobbyState.WorldOpen:
                if (State == LobbySessionState.WaitingForWorld || State == LobbySessionState.Idle)
                {
                    State = LobbySessionState.WaitingForWorld;
                }
                break;
            case LobbyState.TunnelStarting:
                State = LobbySessionState.TunnelStarting;
                break;
            case LobbyState.TunnelActive:
                State = LobbySessionState.Active;
                break;
            case LobbyState.Error:
                SetError(_stateManager.LastError ?? "Сбой туннеля");
                break;
        }
    }

    private void OnLobbyTunnelAddressReady(string address)
    {
        lock (_stateLock)
        {
            _currentTunnelAddress = address;
            if (_role == LobbySessionRole.Guest && _state == LobbySessionState.Joining)
            {
                _state = LobbySessionState.ReadyToPlay;
            }
        }
        TunnelAddressReady?.Invoke(this, address);
        RaiseLobbyUpdated();
    }

    private void OnLobbyStatusUpdated(LobbyStatusResponse status)
    {
        ApplyStatusResponse(status);
        RaiseLobbyUpdated();
    }

    private void OnLobbyModSyncUpdated(IReadOnlyDictionary<string, PlayerModSyncInfo> modSync)
    {
        lock (_stateLock)
        {
            _modSync = modSync;
        }
        RaiseLobbyUpdated();
    }

    private void OnLobbyKicked()
    {
        _logger("[LOBBY-SESSION] Kicked from lobby");
        string code = CurrentLobbyCode ?? string.Empty;
        _ = LeaveLobbyAsync();
        PlayerKicked?.Invoke(this, code);
    }

    private void OnGameExited(object? sender, GameExitedEventArgs e)
    {
        if (Role == LobbySessionRole.Host)
        {
            _stateManager.OnGameExited();
        }
        else if (Role == LobbySessionRole.Guest)
        {
            if (State == LobbySessionState.ConnectingToGame)
            {
                State = LobbySessionState.ReadyToPlay;
            }
        }
    }

    public void Dispose()
    {
        StopPolling();
        _stateManager.StateChanged -= OnLobbyStateChanged;
        _stateManager.Dispose();

        _lobbyService.TunnelAddressReady -= OnLobbyTunnelAddressReady;
        _lobbyService.LobbyStatusUpdated -= OnLobbyStatusUpdated;
        _lobbyService.ModSyncUpdated -= OnLobbyModSyncUpdated;
        _lobbyService.KickedFromLobby -= OnLobbyKicked;

        _gameSessionService.GameExited -= OnGameExited;
    }
}

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
    private readonly IWorkshopService? _workshopService;
    private readonly Action<string> _logger;

    private readonly object _stateLock = new();
    private readonly SemaphoreSlim _hostOpenGate = new(1, 1);
    private readonly LobbyStateManager _stateManager;

    private LobbySessionRole _role = LobbySessionRole.None;
    private LobbySessionState _state = LobbySessionState.Idle;
    private string? _currentLobbyCode;
    private string? _currentTunnelAddress;
    private int? _currentTunnelPingMs;
    private string? _hostNick;
    private string? _lastError;
    private string? _statusHint;
    private bool _hasPersonalTunnel;
    private string? _lastTunnelAddress;
    private List<LobbyPlayerItemDto> _players = new();
    private IReadOnlyDictionary<string, PlayerModSyncInfo>? _modSync;

    private TaskCompletionSource<string>? _domainTcs;
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

    public string? CurrentHostToken => _lobbyService.CurrentHostToken;

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

    public string? StatusHint
    {
        get { lock (_stateLock) return _statusHint; }
        private set { lock (_stateLock) _statusHint = value; }
    }

    public bool HasPersonalTunnel
    {
        get { lock (_stateLock) return _hasPersonalTunnel; }
        private set { lock (_stateLock) _hasPersonalTunnel = value; }
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
        IWorkshopService? workshopService = null,
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
        _workshopService = workshopService;
        _logger = logger ?? FabricGameLaunchService.LogLauncherEvent;

        _stateManager = new LobbyStateManager(_worldWatcher, _tunnelProvider);
        _stateManager.StateChanged += OnLobbyStateChanged;

        _lobbyService.TunnelAddressReady += OnLobbyTunnelAddressReady;
        _lobbyService.LobbyStatusUpdated += OnLobbyStatusUpdated;
        _lobbyService.ModSyncUpdated += OnLobbyModSyncUpdated;
        _lobbyService.KickedFromLobby += OnLobbyKicked;

        _worldWatcher.WorldOpened += OnLanWorldOpened;
        _worldWatcher.WorldClosed += OnLanWorldClosed;
        _worldWatcher.DomainAssigned += OnLanDomainAssigned;

        _gameSessionService.GameExited += OnGameExited;
    }

    private void SetError(string error)
    {
        LastError = error;
        StatusHint = error;
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
        StatusHint = null;

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

// Log watcher starts when OpenWorldAsync is invoked

            StartPolling(isHost: true);
            return code;
        }
        catch (Exception ex)
        {
            SetError(ex.Message);
            return null;
        }
    }

    public async Task<string?> ClaimPersonalTunnelAsync(Action<string>? onBrowserUrl = null, CancellationToken cancellationToken = default)
    {
        var playit = _tunnelProvider as PlayitTunnelProvider;
        if (playit == null)
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var toolsDir = Path.Combine(appData, ".aura", "tools");
            playit = new PlayitTunnelProvider(null, toolsDir);
        }

        try
        {
            StatusHint = "Открываем браузер: подтвердите привязку туннеля...";
            PlayitTunnelProvider.LogTunnel("[CLAIM] Запуск привязки персонального туннеля хоста...");

            var secret = await playit.ClaimTunnelAsync(url =>
            {
                StatusHint = "Подтвердите привязку в браузере (кнопка Add agent)...";
                onBrowserUrl?.Invoke(url);
            }).ConfigureAwait(false);

            if (!string.IsNullOrWhiteSpace(secret))
            {
                HasPersonalTunnel = true;
                StatusHint = "Личный туннель успешно привязан! Ваши миры изолированы.";
                PlayitTunnelProvider.LogTunnel("[CLAIM] Персональный туннель успешно привязан.");
                return secret;
            }
            return null;
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[CLAIM: ERROR] {ex.Message}");
            StatusHint = $"Ошибка привязки: {ex.Message}";
            SetError($"Ошибка привязки: {ex.Message}");
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
        StatusHint = null;

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

        LastError = null;
        StatusHint = "Запуск мира...";

        try
        {
            bool isFakeTunnel = string.Equals(Environment.GetEnvironmentVariable("AURA_FAKE_TUNNEL"), "1", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(Environment.GetEnvironmentVariable("UseFakeTunnel"), "true", StringComparison.OrdinalIgnoreCase);

            if (isFakeTunnel)
            {
                var success = await _lobbyService.HostOpenWorldAsync(customTunnelAddress: "fake-test.e4mc.link", localPort: localPort ?? 25565, cancellationToken: cancellationToken).ConfigureAwait(false);
                if (success)
                {
                    lock (_stateLock)
                    {
                        _currentTunnelAddress = "fake-test.e4mc.link";
                    }
                    StatusHint = "Мир открыт, друзья могут подключиться";
                    State = LobbySessionState.Active;
                    TunnelAddressReady?.Invoke(this, "fake-test.e4mc.link");
                    RaiseLobbyUpdated();
                }
                else
                {
                    SetError("Не удалось зарегистрировать адрес лобби на сервере");
                }
                return;
            }

            var gameDir = GetGameDir();
            bool isGameRunning = _launchService.IsGameRunning || _gameSessionService.IsGameRunning;

            if (_workshopService != null && !isGameRunning)
            {
                StatusHint = "Резервная копия мира...";
                try
                {
                    await _workshopService.CreateAutoBackupLatestWorldAsync(gameDir).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger($"[LOBBY-SESSION: WARN] Backup failed: {ex.Message}");
                }
            }

            var logPath = Path.Combine(gameDir, "logs", "latest.log");
            _domainTcs = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
            _worldWatcher.Start(logPath, readFromEnd: true, scanRecentTail: isGameRunning);

            if (isGameRunning)
            {
                if (LobbyService.UseE4allTunnel)
                {
                    if (!string.IsNullOrWhiteSpace(_worldWatcher.CurrentDomain))
                    {
                        _domainTcs.TrySetResult(_worldWatcher.CurrentDomain);
                        _ = CompleteHostOpenWorldAsync(_worldWatcher.CurrentDomain, localPort);
                        return;
                    }
                }
                else
                {
                    bool alreadyListening = await PlayitTunnelProvider.TestTcpConnectAsync("127.0.0.1", 25565, 500, cancellationToken).ConfigureAwait(false);
                    if (alreadyListening)
                    {
                        OnLanWorldOpened(25565);
                        return;
                    }
                }
            }

            StatusHint = "Игра запущена. Зайдите в мир и откройте сеть (Esc → Открыть для сети)";
            State = LobbySessionState.WaitingForWorld;
            RaiseLobbyUpdated();

            if (!isGameRunning)
            {
                await _gameSessionService.LaunchAsync(new LaunchOptions(), null, cancellationToken).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            SetError($"Ошибка: {ex.Message}");
        }
    }

    private void OnLanDomainAssigned(string domain)
    {
        if (!LobbyService.UseE4allTunnel) return;
        if (string.IsNullOrWhiteSpace(domain)) return;
        _domainTcs?.TrySetResult(domain);

        if (Role == LobbySessionRole.Host && State != LobbySessionState.Active)
        {
            _ = CompleteHostOpenWorldAsync(domain);
        }
    }

    private void OnLanWorldOpened(int port)
    {
        if (Role != LobbySessionRole.Host) return;

        _ = Task.Run(async () =>
        {
            try
            {
                if (State == LobbySessionState.Active) return;

                if (port != 25565)
                {
                    var msg = $"Мир открыт на порту {port}, а нужен 25565";
                    PlayitTunnelProvider.LogTunnel($"[LOBBY: ERROR] {msg}");
                    SetError(msg);
                    return;
                }

                if (LobbyService.UseE4allTunnel)
                {
                    string? domain = _worldWatcher.CurrentDomain;
                    if (string.IsNullOrWhiteSpace(domain) && _domainTcs != null)
                    {
                        StatusHint = "Мир открыт в сети. Ожидание назначения P2P-адреса...";
                        var completedTask = await Task.WhenAny(_domainTcs.Task, Task.Delay(TimeSpan.FromSeconds(15))).ConfigureAwait(false);
                        if (completedTask == _domainTcs.Task)
                        {
                            domain = await _domainTcs.Task.ConfigureAwait(false);
                        }
                    }

                    if (string.IsNullOrWhiteSpace(domain))
                    {
                        domain = _worldWatcher.CurrentDomain;
                    }

                    if (!string.IsNullOrWhiteSpace(domain))
                    {
                        await CompleteHostOpenWorldAsync(domain, port).ConfigureAwait(false);
                    }
                    else
                    {
                        var msg = "Не удалось получить адрес e4mc из лога игры за 15 секунд. Убедитесь, что мод e4all установлен.";
                        PlayitTunnelProvider.LogTunnel($"[LOBBY: ERROR] {msg}");
                        SetError(msg);
                    }
                    return;
                }

                // Playit tunnel flow:
                StatusHint = "Мир открыт на порту 25565! Подключение туннеля...";
                State = LobbySessionState.TunnelStarting;

                var success = await _lobbyService.HostOpenWorldAsync(localPort: 25565).ConfigureAwait(false);
                if (success)
                {
                    var tInfo = _tunnelProvider.CurrentInfo;
                    string? fullAddress = _lobbyService.CurrentTunnelAddress ?? ((tInfo.PublicPort.HasValue && tInfo.PublicPort.Value > 0) ? $"{tInfo.PublicAddress}:{tInfo.PublicPort.Value}" : tInfo.PublicAddress);
                    lock (_stateLock)
                    {
                        _currentTunnelAddress = fullAddress;
                    }

                    StatusHint = "Мир готов, друзья могут подключаться";
                    State = LobbySessionState.Active;
                    if (!string.IsNullOrWhiteSpace(fullAddress))
                    {
                        TunnelAddressReady?.Invoke(this, fullAddress);
                    }
                    RaiseLobbyUpdated();
                }
                else
                {
                    var reason = _tunnelProvider?.CurrentInfo.ErrorMessage ?? "Туннель playit не смог запуститься или подтвердить подключение.";
                    PlayitTunnelProvider.LogTunnel($"[LOBBY: ERROR] HostOpenWorldAsync returned false: {reason}");
                    SetError($"Туннель не поднялся: {reason}");
                }
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[EXCEPTION] OnLanWorldOpened failed: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                SetError($"Ошибка: {ex.Message}");
            }
        });
    }

    private async Task CompleteHostOpenWorldAsync(string domain, int? port = null)
    {
        if (string.IsNullOrWhiteSpace(domain)) return;
        if (Role != LobbySessionRole.Host) return;

        await _hostOpenGate.WaitAsync().ConfigureAwait(false);
        try
        {
            if (State == LobbySessionState.Active && string.Equals(_lastTunnelAddress, domain, StringComparison.OrdinalIgnoreCase)) return;

            StatusHint = $"Получен адрес {domain}. Регистрация...";
            int localPort = port ?? _worldWatcher.CurrentPort ?? 25565;
            var success = await _lobbyService.HostOpenWorldAsync(customTunnelAddress: domain, localPort: localPort).ConfigureAwait(false);
            if (success)
            {
                _lastTunnelAddress = domain;
                lock (_stateLock)
                {
                    _currentTunnelAddress = domain;
                }
                StatusHint = $"Мир открыт ({domain}), друзья могут подключиться";
                State = LobbySessionState.Active;
                TunnelAddressReady?.Invoke(this, domain);
                RaiseLobbyUpdated();
            }
            else
            {
                var reason = "Не удалось зарегистрировать адрес лобби на сервере.";
                PlayitTunnelProvider.LogTunnel($"[LOBBY: ERROR] HostOpenWorldAsync returned false: {reason}");
                SetError(reason);
            }
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[EXCEPTION] CompleteHostOpenWorldAsync failed: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            SetError($"Ошибка: {ex.Message}");
        }
        finally
        {
            _hostOpenGate.Release();
        }
    }

    private void OnLanWorldClosed()
    {
        if (Role != LobbySessionRole.Host) return;

        PlayitTunnelProvider.LogTunnel("[LOBBY] World closed detected in latest.log. Resetting lobby to waiting/closed.");
        _ = Task.Run(async () =>
        {
            try
            {
                await _stateManager.StopTunnelAsync(CancellationToken.None).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[EXCEPTION] OnLanWorldClosed StopTunnel: {ex.Message}");
            }
            finally
            {
                lock (_stateLock)
                {
                    _currentTunnelAddress = null;
                    _lastTunnelAddress = null;
                }
                StatusHint = "Мир был закрыт хостом. Ожидание повторного открытия мира.";
                State = LobbySessionState.WaitingForWorld;
                RaiseLobbyUpdated();
            }
        });
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
            _statusHint = null;
            _lastTunnelAddress = null;
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
                Players: _players.ToList(),
                StatusHint: _statusHint
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
            _worldWatcher.Stop();
            _ = Task.Run(async () =>
            {
                try
                {
                    await _lobbyService.CloseLobbyAsHostAsync().ConfigureAwait(false);
                    await _stateManager.StopTunnelAsync().ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    _logger($"[LOBBY-SESSION: WARN] GameExited cleanup failed: {ex.Message}");
                }
                finally
                {
                    await LeaveLobbyAsync().ConfigureAwait(false);
                }
            });
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

        _worldWatcher.WorldOpened -= OnLanWorldOpened;
        _worldWatcher.WorldClosed -= OnLanWorldClosed;
        _worldWatcher.DomainAssigned -= OnLanDomainAssigned;

        _gameSessionService.GameExited -= OnGameExited;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class LobbyService : ILobbyService, IDisposable
{
    private readonly ILobbyApiClient _apiClient;
    private readonly ITunnelProvider _tunnelProvider;
    private readonly IConfigService? _configService;
    private readonly IModManifestService _manifestService;
    private readonly IGameLaunchService? _launchService;
    private CancellationTokenSource? _pollCts;
    private string? _currentGuestPlayerName;
    private string? _currentHostPlayerName;
    private FileSystemWatcher? _modsWatcher;
    private Timer? _modsDebounceTimer;

    private int _operationVersion;

    public string? CurrentLobbyCode { get; private set; }
    public string? CurrentHostToken { get; private set; }
    public string? CurrentTunnelAddress { get; private set; }
    public string CurrentStatus { get; private set; } = "idle";
    public bool IsHost { get; private set; }
    public bool IsReadyToPlay => CurrentStatus.Equals("open", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(CurrentTunnelAddress);
    public IReadOnlyDictionary<string, PlayerModSyncInfo>? CurrentModSync { get; private set; }

    public event Action<string>? StatusChanged;
    public event Action<string>? TunnelAddressReady;
    public event Action<LobbyStatusResponse>? LobbyStatusUpdated;
    public event Action<IReadOnlyDictionary<string, PlayerModSyncInfo>>? ModSyncUpdated;

    public LobbyService(
        ILobbyApiClient apiClient,
        ITunnelProvider? tunnelProvider = null,
        IConfigService? configService = null,
        IModManifestService? manifestService = null,
        IGameLaunchService? launchService = null)
    {
        _apiClient = apiClient ?? throw new ArgumentNullException(nameof(apiClient));
        _tunnelProvider = tunnelProvider ?? (configService != null ? new PlayitTunnelProvider() : new FakeTunnelProvider());
        _configService = configService;
        _manifestService = manifestService ?? new ModManifestService();
        _launchService = launchService;
    }

    private string GetGameDir()
    {
        string? configuredDir = _configService?.CurrentConfig?.GameDir;
        if (_launchService != null && !string.IsNullOrWhiteSpace(configuredDir))
        {
            return _launchService.ResolveMinecraftDirectory(configuredDir);
        }
        if (!string.IsNullOrWhiteSpace(configuredDir))
        {
            return Path.GetFullPath(configuredDir);
        }
        return AppDomain.CurrentDomain.BaseDirectory;
    }

    public async Task<string?> CreateLobbyAsHostAsync(string hostName, CancellationToken cancellationToken = default)
    {
        LeaveLobby();
        int opVersion = Interlocked.Increment(ref _operationVersion);

        _currentHostPlayerName = hostName;
        string gameDir = GetGameDir();
        var manifest = _manifestService.BuildModManifest(gameDir);

        var response = await _apiClient.CreateLobbyAsync(hostName, manifest, cancellationToken);
        if (response == null) return null;

        if (_operationVersion != opVersion)
        {
            if (!string.IsNullOrWhiteSpace(response.Code) && !string.IsNullOrWhiteSpace(response.HostToken))
            {
                _ = Task.Run(async () =>
                {
                    try { await _apiClient.CloseLobbyAsync(response.Code, response.HostToken); } catch { }
                });
            }
            return null;
        }

        CurrentLobbyCode = response.Code;
        CurrentHostToken = response.HostToken;
        CurrentStatus = response.Status;
        IsHost = true;

        StatusChanged?.Invoke(CurrentStatus);
        StartModsWatcher(gameDir);
        StartGuestPolling();
        return CurrentLobbyCode;
    }

    public async Task<bool> HostOpenWorldAsync(string? customTunnelAddress = null, int localPort = 25565, CancellationToken cancellationToken = default)
    {
        if (!IsHost || string.IsNullOrWhiteSpace(CurrentLobbyCode) || string.IsNullOrWhiteSpace(CurrentHostToken))
        {
            PlayitTunnelProvider.LogTunnel("[HOST-OPEN: WARN] HostOpenWorldAsync called but IsHost=false or lobby code/token empty.");
            return false;
        }

        try
        {
            bool isFakeTunnel = string.Equals(Environment.GetEnvironmentVariable("AURA_FAKE_TUNNEL"), "1", StringComparison.OrdinalIgnoreCase) ||
                                string.Equals(Environment.GetEnvironmentVariable("UseFakeTunnel"), "true", StringComparison.OrdinalIgnoreCase);

            if (!isFakeTunnel)
            {
                bool tcpOk = await CheckLocalTcpPortAsync("127.0.0.1", localPort, timeoutMs: 3000, cancellationToken);
                if (!tcpOk)
                {
                    PlayitTunnelProvider.LogTunnel($"[HOST-OPEN: ERROR] Local TCP connect to 127.0.0.1:{localPort} failed. World is not open or not accepting connections.");
                    return false;
                }
            }

            string tunnelAddr;
            if (!string.IsNullOrWhiteSpace(customTunnelAddress))
            {
                tunnelAddr = customTunnelAddress;
            }
            else
            {
                var tunnelResult = await _tunnelProvider.StartAsync(localPort, cancellationToken);
                if (tunnelResult.Status != TunnelStatus.Active || string.IsNullOrWhiteSpace(tunnelResult.PublicAddress))
                {
                    PlayitTunnelProvider.LogTunnel($"[HOST-OPEN: ERROR] Tunnel StartAsync failed: status={tunnelResult.Status}, error={tunnelResult.ErrorMessage}");
                    return false;
                }

                if (!_tunnelProvider.IsProcessRunning && !isFakeTunnel)
                {
                    PlayitTunnelProvider.LogTunnel("[HOST-OPEN: ERROR] Playit agent process is not running.");
                    return false;
                }

                tunnelAddr = tunnelResult.PublicPort.HasValue 
                    ? $"{tunnelResult.PublicAddress}:{tunnelResult.PublicPort.Value}" 
                    : tunnelResult.PublicAddress;
            }

            PlayitTunnelProvider.LogTunnel($"[HOST-OPEN] Registering tunnel address '{tunnelAddr}' in lobby-api for code {CurrentLobbyCode}...");
            var success = await _apiClient.OpenLobbyAsync(CurrentLobbyCode, CurrentHostToken, tunnelAddr, cancellationToken);
            if (success)
            {
                CurrentStatus = "open";
                CurrentTunnelAddress = tunnelAddr;
                StatusChanged?.Invoke(CurrentStatus);
                TunnelAddressReady?.Invoke(tunnelAddr);
                PlayitTunnelProvider.LogTunnel($"[HOST-OPEN] Lobby {CurrentLobbyCode} opened successfully with tunnel {tunnelAddr}.");
                return true;
            }

            PlayitTunnelProvider.LogTunnel($"[HOST-OPEN: ERROR] lobby-api OpenLobbyAsync returned false for code {CurrentLobbyCode}.");
            return false;
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[EXCEPTION] HostOpenWorldAsync: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            return false;
        }
    }

    private static async Task<bool> CheckLocalTcpPortAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        try
        {
            using var client = new System.Net.Sockets.TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completedTask = await Task.WhenAny(connectTask, Task.Delay(timeoutMs, ct)).ConfigureAwait(false);
            if (completedTask == connectTask)
            {
                await connectTask.ConfigureAwait(false);
                return client.Connected;
            }
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[HOST-OPEN: DEBUG] TCP check to {host}:{port} failed: {ex.Message}");
        }
        return false;
    }

    public async Task CloseLobbyAsHostAsync(CancellationToken cancellationToken = default)
    {
        var codeToClose = CurrentLobbyCode;
        var hostTokenToClose = CurrentHostToken;
        bool wasHost = IsHost && !string.IsNullOrWhiteSpace(codeToClose) && !string.IsNullOrWhiteSpace(hostTokenToClose);

        ResetStateInternal();

        if (wasHost && codeToClose != null && hostTokenToClose != null)
        {
            try
            {
                PlayitTunnelProvider.LogTunnel($"[LOBBY] Closing lobby {codeToClose} as host via API...");
                await _apiClient.CloseLobbyAsync(codeToClose, hostTokenToClose, cancellationToken);
                PlayitTunnelProvider.LogTunnel($"[LOBBY] Lobby {codeToClose} closed via API.");
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[EXCEPTION] CloseLobbyAsync API call failed: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            }

            try
            {
                await _tunnelProvider.StopAsync(cancellationToken);
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[EXCEPTION] CloseLobbyAsHostAsync StopAsync: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            }
        }
    }

    public async Task<bool> JoinLobbyAsGuestAsync(string code, string playerName, CancellationToken cancellationToken = default)
    {
        LeaveLobby();
        int opVersion = Interlocked.Increment(ref _operationVersion);

        var cleanCode = code?.Trim().ToUpperInvariant() ?? string.Empty;
        string gameDir = GetGameDir();
        var manifest = _manifestService.BuildModManifest(gameDir);

        var response = await _apiClient.JoinLobbyAsync(cleanCode, playerName, manifest, cancellationToken);
        if (response == null || !response.Success) return false;

        if (_operationVersion != opVersion)
        {
            _ = Task.Run(async () =>
            {
                try { await _apiClient.LeaveLobbyAsync(cleanCode, playerName); } catch { }
            });
            return false;
        }

        CurrentLobbyCode = !string.IsNullOrWhiteSpace(response.Code) ? response.Code : cleanCode;
        CurrentStatus = response.Status;
        CurrentTunnelAddress = response.TunnelAddress;
        _currentGuestPlayerName = playerName;
        IsHost = false;

        StatusChanged?.Invoke(CurrentStatus);
        if (!string.IsNullOrWhiteSpace(CurrentTunnelAddress))
        {
            TunnelAddressReady?.Invoke(CurrentTunnelAddress);
        }

        StartModsWatcher(gameDir);
        StartGuestPolling();
        return true;
    }

    public async Task<LobbyStatusResponse?> RefreshGuestStatusAsync(CancellationToken cancellationToken = default)
    {
        var code = CurrentLobbyCode;
        if (string.IsNullOrWhiteSpace(code)) return null;
        int opVersion = _operationVersion;

        var status = await _apiClient.GetStatusAsync(code, cancellationToken);
        if (_operationVersion != opVersion || !string.Equals(CurrentLobbyCode, code, StringComparison.OrdinalIgnoreCase))
        {
            return null;
        }

        if (status == null && !IsHost)
        {
            var detailed = await _apiClient.GetStatusDetailedAsync(code, cancellationToken);
            if (_operationVersion != opVersion || !string.Equals(CurrentLobbyCode, code, StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            if (detailed.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                bool wasClosed = string.Equals(CurrentStatus, "closed", StringComparison.OrdinalIgnoreCase);
                CurrentStatus = "closed";
                if (!wasClosed)
                {
                    StatusChanged?.Invoke(CurrentStatus);
                }
                return null;
            }
        }

        if (status != null)
        {
            bool statusChanged = !string.Equals(CurrentStatus, status.Status, StringComparison.OrdinalIgnoreCase);
            bool tunnelChanged = !string.Equals(CurrentTunnelAddress, status.TunnelAddress, StringComparison.OrdinalIgnoreCase);

            CurrentStatus = status.Status;
            CurrentTunnelAddress = status.TunnelAddress;

            if (statusChanged)
            {
                StatusChanged?.Invoke(CurrentStatus);
            }

            if (tunnelChanged && !string.IsNullOrWhiteSpace(CurrentTunnelAddress))
            {
                TunnelAddressReady?.Invoke(CurrentTunnelAddress);
            }

            CurrentModSync = status.ModSync;
            if (status.ModSync != null)
            {
                ModSyncUpdated?.Invoke(status.ModSync);
            }

            LobbyStatusUpdated?.Invoke(status);
        }
        return status;
    }

    public async Task UpdateManifestAsync(CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(CurrentLobbyCode)) return;

        string myNick = IsHost ? (_currentHostPlayerName ?? "Host") : (_currentGuestPlayerName ?? "Guest");
        string gameDir = GetGameDir();
        var manifest = _manifestService.BuildModManifest(gameDir);

        await _apiClient.UpdateManifestAsync(CurrentLobbyCode, myNick, manifest, cancellationToken);
        await RefreshGuestStatusAsync(cancellationToken);
    }

    private void StartModsWatcher(string gameDir)
    {
        StopModsWatcher();
        string modsDir = Path.Combine(gameDir, "mods");
        if (!Directory.Exists(modsDir))
        {
            try { Directory.CreateDirectory(modsDir); } catch { return; }
        }

        try
        {
            _modsWatcher = new FileSystemWatcher(modsDir)
            {
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite,
                EnableRaisingEvents = true,
                IncludeSubdirectories = false
            };
            _modsWatcher.Changed += OnModsDirectoryChanged;
            _modsWatcher.Created += OnModsDirectoryChanged;
            _modsWatcher.Deleted += OnModsDirectoryChanged;
            _modsWatcher.Renamed += (s, e) => OnModsDirectoryChanged(s, e);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[MOD-SYNC] FileSystemWatcher failed: {ex.Message}");
        }
    }

    private void OnModsDirectoryChanged(object sender, FileSystemEventArgs e)
    {
        // 1.5 сек задержка (debounce)
        _modsDebounceTimer?.Dispose();
        _modsDebounceTimer = new Timer(async _ =>
        {
            try
            {
                await UpdateManifestAsync();
            }
            catch { }
        }, null, 1500, Timeout.Infinite);
    }

    private void StopModsWatcher()
    {
        _modsDebounceTimer?.Dispose();
        _modsDebounceTimer = null;
        if (_modsWatcher != null)
        {
            try
            {
                _modsWatcher.EnableRaisingEvents = false;
                _modsWatcher.Dispose();
            }
            catch { }
            _modsWatcher = null;
        }
    }

    private void StartGuestPolling()
    {
        _pollCts?.Cancel();
        _pollCts = new CancellationTokenSource();
        var ct = _pollCts.Token;

        _ = Task.Run(async () =>
        {
            try
            {
                int heartbeatTick = 0;
                while (!ct.IsCancellationRequested)
                {
                    try
                    {
                        await Task.Delay(1500, ct);
                        if (ct.IsCancellationRequested) break;

                        heartbeatTick++;
                        if (IsHost && !string.IsNullOrWhiteSpace(CurrentLobbyCode) && !string.IsNullOrWhiteSpace(CurrentHostToken) && heartbeatTick % 10 == 0)
                        {
                            try
                            {
                                await _apiClient.HeartbeatAsync(CurrentLobbyCode, CurrentHostToken, ct);
                            }
                            catch { }
                        }

                        await RefreshGuestStatusAsync(ct);
                        if (CurrentStatus.Equals("closed", StringComparison.OrdinalIgnoreCase))
                        {
                            break;
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                    catch (Exception loopEx)
                    {
                        // Transient poll error, log and continue
                        PlayitTunnelProvider.LogTunnel($"[GUEST-POLL: WARN] RefreshGuestStatusAsync transient failure: {loopEx.Message}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Normal exit
            }
            catch (Exception ex)
            {
                PlayitTunnelProvider.LogTunnel($"[EXCEPTION] GuestPolling: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            }
        }, ct);
    }

    private void ResetStateInternal()
    {
        Interlocked.Increment(ref _operationVersion);
        StopModsWatcher();
        _pollCts?.Cancel();
        _pollCts = null;
        CurrentLobbyCode = null;
        CurrentHostToken = null;
        CurrentTunnelAddress = null;
        _currentGuestPlayerName = null;
        _currentHostPlayerName = null;
        CurrentStatus = "idle";
        IsHost = false;
        CurrentModSync = null;
        StatusChanged?.Invoke(CurrentStatus);
    }

    public void LeaveLobby()
    {
        var codeToLeave = CurrentLobbyCode;
        var hostTokenToClose = CurrentHostToken;
        var guestName = _currentGuestPlayerName;
        bool wasHost = IsHost && !string.IsNullOrWhiteSpace(codeToLeave) && !string.IsNullOrWhiteSpace(hostTokenToClose);
        bool wasGuest = !IsHost && !string.IsNullOrWhiteSpace(codeToLeave) && !string.IsNullOrWhiteSpace(guestName);

        ResetStateInternal();

        if (wasHost && codeToLeave != null && hostTokenToClose != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _apiClient.CloseLobbyAsync(codeToLeave, hostTokenToClose);
                }
                catch { }
            });
        }
        else if (wasGuest && codeToLeave != null && guestName != null)
        {
            _ = Task.Run(async () =>
            {
                try
                {
                    await _apiClient.LeaveLobbyAsync(codeToLeave, guestName);
                }
                catch { }
            });
        }
    }

    public void Dispose()
    {
        Interlocked.Increment(ref _operationVersion);
        StopModsWatcher();
        _pollCts?.Cancel();
        _pollCts?.Dispose();
    }
}

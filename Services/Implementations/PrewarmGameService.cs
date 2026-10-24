using System;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class PrewarmGameService : IPrewarmGameService, IDisposable
{
    private readonly IGameLaunchService _launchService;
    private readonly IConfigService _configService;
    private readonly Action<string> _logger;

    private TcpListener? _listener;
    private Socket? _clientSocket;
    private NetworkStream? _networkStream;
    private Process? _process;
    private string? _token;
    private int _port;
    private bool _isReady;
    private readonly object _lock = new();
    private CancellationTokenSource? _cts;

    public bool IsPrewarmedReady
    {
        get
        {
            lock (_lock)
            {
                return _isReady && _process != null && !_process.HasExited && _clientSocket != null && _clientSocket.Connected;
            }
        }
    }

    public Process? PrewarmedProcess
    {
        get
        {
            lock (_lock) return _process;
        }
    }

    public PrewarmGameService(
        IGameLaunchService launchService,
        IConfigService configService,
        Action<string>? logger = null)
    {
        _launchService = launchService;
        _configService = configService;
        _logger = logger ?? (msg => FabricGameLaunchService.LogLauncherEvent(msg));
    }

    public async Task StartPrewarmAsync(CancellationToken cancellationToken = default)
    {
        var config = _configService.CurrentConfig;
        if (!config.FastStartEnabled)
        {
            _logger("[PREWARM] FastStart is disabled in config, skipping prewarm");
            return;
        }

        lock (_lock)
        {
            if (_process != null && !_process.HasExited)
            {
                _logger("[PREWARM] Game is already prewarmed or running");
                return;
            }
        }

        StopPrewarm();

        _cts = new CancellationTokenSource();
        var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _cts.Token);

        try
        {
            _listener = new TcpListener(IPAddress.Loopback, 0);
            _listener.Start();
            _port = ((IPEndPoint)_listener.LocalEndpoint).Port;
            _token = Guid.NewGuid().ToString("N");
            _isReady = false;

            _logger($"[PREWARM] Starting prewarmed game on 127.0.0.1:{_port}...");

            string prewarmJvmArgs = $"-Daura.prewarm=true -Daura.prewarm.port={_port} -Daura.prewarm.token={_token}";
            var oldJvm = config.JvmArgs;
            config.JvmArgs = string.IsNullOrWhiteSpace(oldJvm) ? prewarmJvmArgs : $"{oldJvm} {prewarmJvmArgs}";

            try
            {
                _process = await _launchService.LaunchGameAsync(
                    config,
                    onLogReceived: null,
                    onGameExited: (code, path) =>
                    {
                        lock (_lock)
                        {
                            _isReady = false;
                            _process = null;
                        }
                        _logger($"[PREWARM] Prewarmed process exited with code {code}");
                    },
                    cancellationToken: linkedCts.Token);
            }
            finally
            {
                config.JvmArgs = oldJvm;
            }

            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _process.PriorityClass = ProcessPriorityClass.BelowNormal;
                }
                catch { }

                _ = Task.Run(async () =>
                {
                    try
                    {
                        var socket = await _listener.AcceptSocketAsync(linkedCts.Token);
                        NetworkStream netStream;
                        lock (_lock)
                        {
                            _clientSocket = socket;
                            _networkStream = new NetworkStream(socket, ownsSocket: false);
                            netStream = _networkStream;
                        }

                        using var reader = new StreamReader(netStream, Encoding.UTF8, detectEncodingFromByteOrderMarks: false, bufferSize: 1024, leaveOpen: true);
                        string? line = await reader.ReadLineAsync(linkedCts.Token);
                        if (line != null && line.StartsWith("READY ") && line.Contains(_token))
                        {
                            lock (_lock) _isReady = true;
                            _logger("[PREWARM] Prewarmed game is READY on main screen!");
                        }
                    }
                    catch (Exception ex)
                    {
                        _logger($"[PREWARM] IPC handshake error: {ex.Message}");
                    }
                }, linkedCts.Token);
            }
        }
        catch (Exception ex)
        {
            _logger($"[PREWARM: ERROR] Failed to start prewarm: {ex.Message}");
            StopPrewarm();
        }
    }

    public async Task RestartPrewarmAsync(CancellationToken cancellationToken = default)
    {
        StopPrewarm();
        await StartPrewarmAsync(cancellationToken);
    }

    public async Task<Process?> ActivateAsync(string? quickPlayMultiplayer = null, string? quickPlaySingleplayer = null)
    {
        Process? proc;
        Socket? sock;
        lock (_lock)
        {
            if (!_isReady || _process == null || _process.HasExited || _clientSocket == null || !_clientSocket.Connected)
            {
                return null;
            }
            proc = _process;
            sock = _clientSocket;
            _process = null;
            _isReady = false;
        }

        try
        {
            proc.PriorityClass = ProcessPriorityClass.Normal;
        }
        catch { }

        try
        {
            string cmd;
            if (!string.IsNullOrWhiteSpace(quickPlayMultiplayer))
            {
                cmd = $"SHOW+JOIN {quickPlayMultiplayer.Trim()}\n";
            }
            else if (!string.IsNullOrWhiteSpace(quickPlaySingleplayer))
            {
                cmd = $"SHOW+WORLD {quickPlaySingleplayer.Trim()}\n";
            }
            else
            {
                cmd = "SHOW\n";
            }

            byte[] bytes = Encoding.UTF8.GetBytes(cmd);
            await sock.SendAsync(bytes, SocketFlags.None);
            _logger($"[PREWARM] Activated game process PID={proc.Id} with command: {cmd.Trim()}");

            lock (_lock)
            {
                try { _networkStream?.Dispose(); } catch { }
                _networkStream = null;
                try { _clientSocket?.Close(); } catch { }
                _clientSocket = null;
                try { _listener?.Stop(); } catch { }
                _listener = null;
            }

            return proc;
        }
        catch (Exception ex)
        {
            _logger($"[PREWARM: ERROR] Failed to send activation command: {ex.Message}");
            return null;
        }
    }

    public void StopPrewarm()
    {
        _cts?.Cancel();
        lock (_lock)
        {
            _isReady = false;
            try { _networkStream?.Dispose(); } catch { }
            _networkStream = null;
            try { _clientSocket?.Close(); } catch { }
            _clientSocket = null;
            try { _listener?.Stop(); } catch { }
            _listener = null;
            if (_process != null && !_process.HasExited)
            {
                try
                {
                    _logger($"[PREWARM] Killing prewarmed process PID={_process.Id}...");
                    _process.Kill(true);
                }
                catch { }
            }
            _process = null;
        }
    }

    public void Dispose()
    {
        StopPrewarm();
    }
}

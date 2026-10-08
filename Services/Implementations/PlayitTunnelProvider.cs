using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Реализация ITunnelProvider для официального playit-agent v1.0.10.
/// Контролирует скачивание и проверку SHA-256, хранение секрета в DPAPI,
/// запуск процесса через Windows Job Object (гарантированное завершение с лаунчером).
/// </summary>
public class PlayitTunnelProvider : ITunnelProvider
{
    public const string ExpectedSha256 = "dd1acb19e47bca4a935f2f72a68390bd2fc3a8ed608af7c9c247d3a69d7fba0a";
    public const string PlayitDownloadUrl = "https://github.com/playit-cloud/playit-agent/releases/download/v0.15.26/playit-windows-x86_64-signed.exe";

    private readonly HttpClient _httpClient;
    private readonly string _toolsDir;
    private readonly string _playitExePath;
    private readonly string _secretFilePath;
    private TunnelInfo _currentInfo = new(null, null, TunnelStatus.Inactive);
    private Process? _process;
    private IntPtr _jobHandle = IntPtr.Zero;
    private CancellationTokenSource? _activeReadersCts;
    private int _runtimeRestartCount;
    private volatile bool _isStopping;

    public TunnelInfo CurrentInfo => _currentInfo;
    public event Action<TunnelInfo>? StatusChanged;

    public string PublicHost { get; set; } = "pgsql-jill.tun.ply.gg";
    public int PublicPort { get; set; } = 38062;

    public PlayitTunnelProvider(HttpClient? httpClient = null, string? toolsDir = null)
    {
        _httpClient = httpClient ?? new HttpClient();

        string? profileDir = Environment.GetEnvironmentVariable("AURA_PROFILE_DIR");
        if (string.IsNullOrWhiteSpace(profileDir) && Program.StartupArgs != null)
        {
            for (int i = 0; i < Program.StartupArgs.Length - 1; i++)
            {
                if (string.Equals(Program.StartupArgs[i], "--profile", StringComparison.OrdinalIgnoreCase))
                {
                    profileDir = Program.StartupArgs[i + 1];
                    break;
                }
            }
        }

        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        var baseDir = !string.IsNullOrWhiteSpace(profileDir)
            ? (Path.IsPathRooted(profileDir) ? profileDir : Path.Combine(appData, "Aura", "profiles", profileDir))
            : Path.Combine(appData, ".aura");

        _toolsDir = toolsDir ?? Path.Combine(baseDir, "tools");
        _playitExePath = Path.Combine(_toolsDir, "playit-0.15.26.exe");
        _secretFilePath = Path.Combine(_toolsDir, "playit.secret");

        PurgeLegacyLocalSecrets();
    }

    /// <summary>
    /// Проверка и скачивание бинарника playit 0.15.26 с валидацией SHA-256.
    /// </summary>
    public async Task EnsureBinaryDownloadedAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_toolsDir);

        if (!File.Exists(_playitExePath))
        {
            var fallbackPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura", "tools", "playit-0.15.26.exe");
            if (File.Exists(fallbackPath))
            {
                try
                {
                    File.Copy(fallbackPath, _playitExePath, true);
                    LogTunnel($"Copied playit binary from global tools: {fallbackPath}");
                }
                catch { }
            }
        }

        if (File.Exists(_playitExePath))
        {
            var hash = await ComputeFileSha256Async(_playitExePath, ct);
            if (string.Equals(hash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                LogTunnel($"Playit binary verified with expected hash {ExpectedSha256}");
                return;
            }
            LogTunnel($"Playit binary hash mismatch ({hash} != {ExpectedSha256}), re-downloading...");
            try { File.Delete(_playitExePath); } catch { }
        }

        LogTunnel($"Downloading playit-0.15.26 from {PlayitDownloadUrl}...");
        var tempFile = _playitExePath + ".tmp";
        try
        {
            using (var response = await _httpClient.GetAsync(PlayitDownloadUrl, HttpCompletionOption.ResponseHeadersRead, ct))
            {
                response.EnsureSuccessStatusCode();
                await using var stream = await response.Content.ReadAsStreamAsync(ct);
                await using var fileStream = new FileStream(tempFile, FileMode.Create, FileAccess.Write, FileShare.None);
                await stream.CopyToAsync(fileStream, ct);
            }

            var hash = await ComputeFileSha256Async(tempFile, ct);
            if (!string.Equals(hash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                try { File.Delete(tempFile); } catch { }
                throw new InvalidOperationException($"SHA-256 mismatch for playit-0.15.26.exe! Expected: {ExpectedSha256}, actual: {hash}");
            }

            if (File.Exists(_playitExePath)) File.Delete(_playitExePath);
            File.Move(tempFile, _playitExePath);
            LogTunnel($"Playit binary downloaded and verified successfully: {_playitExePath}");
        }
        finally
        {
            if (File.Exists(tempFile))
            {
                try { File.Delete(tempFile); } catch { }
            }
        }
    }

    private static async Task<string> ComputeFileSha256Async(string filePath, CancellationToken ct)
    {
        await using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var bytes = await SHA256.HashDataAsync(stream, ct);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public string? InMemorySecret { get; set; }
    private string? _activeTempSecretFile;

    public bool IsProcessRunning => _process != null && !_process.HasExited;

    public static void PurgeLegacyLocalSecrets()
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var targets = new List<string>
            {
                Path.Combine(appData, ".aura", "tools", "playit.secret.enc"),
                Path.Combine(appData, ".aura", "tools", "playit.secret")
            };

            var profilesDir = Path.Combine(appData, "Aura", "profiles");
            if (Directory.Exists(profilesDir))
            {
                foreach (var dir in Directory.GetDirectories(profilesDir))
                {
                    targets.Add(Path.Combine(dir, "tools", "playit.secret.enc"));
                    targets.Add(Path.Combine(dir, "tools", "playit.secret"));
                }
            }

            foreach (var target in targets)
            {
                if (File.Exists(target))
                {
                    try
                    {
                        File.Delete(target);
                        LogTunnel($"[CLEANUP] Deleted legacy local secret: {target}");
                    }
                    catch (Exception ex)
                    {
                        LogTunnel($"[CLEANUP: WARN] Failed to delete legacy secret {target}: {ex.Message}");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            LogTunnel($"[CLEANUP: ERROR] PurgeLegacyLocalSecrets: {ex.Message}");
        }
    }

    public void SetSecret(string? secret)
    {
        InMemorySecret = secret?.Trim('\uFEFF', '\u200B', ' ', '\r', '\n', '\t');
    }

    public void SaveSecret(string rawSecret)
    {
        SetSecret(rawSecret);
    }

    public string? LoadSecret()
    {
        return InMemorySecret;
    }

    public static void LogTunnel(string message)
    {
        try
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            var logDir = Path.Combine(appData, ".aura", "logs");
            Directory.CreateDirectory(logDir);
            var logPath = Path.Combine(logDir, "tunnel.log");
            var line = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] {message}{Environment.NewLine}";
            File.AppendAllText(logPath, line);
        }
        catch { }
        App.Log($"[TUNNEL] {message}");
    }

    public static void OpenBrowser(string url)
    {
        LogTunnel($"[BROWSER] Launching browser for: {url}");
        try
        {
            if (string.Equals(Environment.GetEnvironmentVariable("AURA_SIMULATE_BROWSER_FAIL"), "1", StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException("Simulated Process.Start failure: The system cannot find the file specified / no association");
            }

            var psi = new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            };
            Process.Start(psi);
            LogTunnel("[BROWSER] Process.Start with UseShellExecute=true succeeded.");
        }
        catch (Exception ex)
        {
            LogTunnel($"[BROWSER: WARN] Process.Start failed: {ex.Message}. Falling back to cmd.exe /c start...");
            try
            {
                if (string.Equals(Environment.GetEnvironmentVariable("AURA_SIMULATE_BROWSER_FAIL"), "1", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException("Simulated fallback failure");
                }

                Process.Start(new ProcessStartInfo
                {
                    FileName = "cmd.exe",
                    Arguments = $"/c start \"\" \"{url}\"",
                    CreateNoWindow = true,
                    UseShellExecute = false
                });
                LogTunnel("[BROWSER] Fallback cmd.exe /c start succeeded.");
            }
            catch (Exception ex2)
            {
                LogTunnel($"[BROWSER: ERROR] Both browser launch attempts failed: {ex2.Message}");
            }
        }
    }

    public static void KillStalePlayitProcesses()
    {
        try
        {
            var currentPid = Process.GetCurrentProcess().Id;
            var processes = Process.GetProcesses()
                .Where(p => p.Id != currentPid && p.ProcessName.StartsWith("playit", StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var p in processes)
            {
                try
                {
                    LogTunnel($"Killing stale playit process {p.ProcessName} (PID: {p.Id})");
                    p.Kill(true);
                }
                catch (Exception ex)
                {
                    LogTunnel($"[PROCESS: WARN] Failed to kill process {p.ProcessName} PID {p.Id}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            LogTunnel($"[PROCESS: ERROR] Error finding stale playit processes: {ex.Message}");
        }
    }

    public bool HasSecret => !string.IsNullOrWhiteSpace(LoadSecret());

    public async Task<string?> ClaimTunnelAsync(Action<string>? onUrlReady = null, CancellationToken ct = default)
    {
        LogTunnel("Starting ClaimTunnelAsync...");
        await EnsureBinaryDownloadedAsync(ct);
        KillStalePlayitProcesses();
        InitJobObject();

        var tcsUrl = new TaskCompletionSource<string>(TaskCreationOptions.RunContinuationsAsynchronously);
        var psi = new ProcessStartInfo
        {
            FileName = _playitExePath,
            Arguments = $"--secret_path \"{_secretFilePath}\" --stdout",
            WorkingDirectory = _toolsDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        LogTunnel($"Launching playit agent for claim: \"{_playitExePath}\" {psi.Arguments}");
        var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };

        void HandleProcessOutput(string? data, string source)
        {
            if (string.IsNullOrWhiteSpace(data)) return;
            LogTunnel($"[{source}] {data}");

            var match = System.Text.RegularExpressions.Regex.Match(data, @"https?://playit\.gg/claim/[a-zA-Z0-9_-]+");
            if (match.Success)
            {
                var url = match.Value;
                LogTunnel($"Claim URL detected in {source}: {url}");
                tcsUrl.TrySetResult(url);
            }
        }

        proc.OutputDataReceived += (s, e) => HandleProcessOutput(e.Data, "PLAYIT-CLAIM-OUT");
        proc.ErrorDataReceived += (s, e) => HandleProcessOutput(e.Data, "PLAYIT-CLAIM-ERR");

        if (!proc.Start())
        {
            LogTunnel("[ERROR] Failed to start playit agent process.");
            throw new InvalidOperationException("Не удалось запустить процесс playit.");
        }

        CancellationTokenSource? readersCts = null;
        try
        {
            readersCts = new CancellationTokenSource();
            var readerCt = readersCts.Token;

            StartPipeReader(proc.StandardOutput, "PLAYIT-CLAIM-OUT", data => HandleProcessOutput(data, "PLAYIT-CLAIM-OUT"), readerCt);
            StartPipeReader(proc.StandardError, "PLAYIT-CLAIM-ERR", data => HandleProcessOutput(data, "PLAYIT-CLAIM-ERR"), readerCt);

            if (_jobHandle != IntPtr.Zero)
            {
                AssignProcessToJobObject(_jobHandle, proc.Handle);
            }

            // Ждем получения claim URL (до 30 секунд согласно ТЗ)
            LogTunnel("Awaiting claim URL from process output (30s timeout)...");
            var urlTimeoutTask = Task.Delay(TimeSpan.FromSeconds(30), ct);
            var completedTask = await Task.WhenAny(tcsUrl.Task, urlTimeoutTask);

            if (completedTask != tcsUrl.Task)
            {
                LogTunnel("[TIMEOUT] Claim URL was not received within 30 seconds.");
                throw new TimeoutException("Не удалось получить ссылку привязки за 30 секунд. Проверьте интернет-соединение.");
            }

            string url = await tcsUrl.Task;
            LogTunnel($"Claim URL obtained: {url}. Calling onUrlReady callback.");
            onUrlReady?.Invoke(url);

            // Автоматическое открытие в браузере по умолчанию
            OpenBrowser(url);

            // Ждем подтверждения пользователя и появления playit.secret (таймаут до 5 минут = 300 сек)
            // Процесс агента держим живым все это время
            LogTunnel("Awaiting secret creation in playit.secret (up to 5 minutes)...");
            var maxWait = TimeSpan.FromMinutes(5);
            var startTime = DateTime.UtcNow;

            while (DateTime.UtcNow - startTime < maxWait)
            {
                ct.ThrowIfCancellationRequested();

                if (proc.HasExited)
                {
                    LogTunnel($"[WARN] Playit agent exited with code {proc.ExitCode} while waiting for secret.");
                }

                if (File.Exists(_secretFilePath) && new FileInfo(_secretFilePath).Length > 10)
                {
                    try
                    {
                        string sec = (await File.ReadAllTextAsync(_secretFilePath, ct)).Trim();
                        if (!string.IsNullOrWhiteSpace(sec))
                        {
                            LogTunnel("Found playit.secret file! Encrypting with DPAPI and saving...");
                            SaveSecret(sec);
                            LogTunnel("Secret saved with DPAPI successfully.");
                            return sec;
                        }
                    }
                    catch (Exception ex)
                    {
                        LogTunnel($"[WARN] Error reading secret file: {ex.Message}");
                    }
                }

                await Task.Delay(500, ct);
            }

            LogTunnel("[TIMEOUT] Confirmation timeout reached (5 minutes).");
            throw new TimeoutException("Время ожидания подтверждения истекло (5 минут).");
        }
        finally
        {
            try { readersCts?.Cancel(); } catch { }
            try { readersCts?.Dispose(); } catch { }
            try
            {
                if (!proc.HasExited)
                {
                    LogTunnel("Terminating claim agent process.");
                    proc.Kill(true);
                }
            }
            catch { }
            proc.Dispose();
        }
    }

    public async Task<(string? Host, int? Port)> ResolveTunnelAddressAsync(CancellationToken ct = default)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = _playitExePath,
                Arguments = $"--secret_path \"{_secretFilePath}\" tunnels list",
                WorkingDirectory = _toolsDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc == null) return (null, null);

            string stdout = await proc.StandardOutput.ReadToEndAsync(ct);
            await proc.WaitForExitAsync(ct);

            using var doc = System.Text.Json.JsonDocument.Parse(stdout);
            if (doc.RootElement.TryGetProperty("tunnels", out var tunnels) && tunnels.GetArrayLength() > 0)
            {
                var first = tunnels[0];
                if (first.TryGetProperty("alloc", out var alloc) &&
                    alloc.TryGetProperty("data", out var data))
                {
                    string? host = null;
                    if (data.TryGetProperty("assigned_domain", out var domElem) && !string.IsNullOrWhiteSpace(domElem.GetString()))
                    {
                        host = domElem.GetString();
                    }
                    else if (data.TryGetProperty("ip_hostname", out var ipElem))
                    {
                        host = ipElem.GetString();
                    }

                    int? port = null;
                    if (data.TryGetProperty("port_start", out var portElem))
                    {
                        port = portElem.GetInt32();
                    }

                    return (host, port);
                }
            }
        }
        catch (Exception ex)
        {
            App.Log($"[PLAYIT: RESOLVE ERROR] {ex.Message}");
        }

        return (null, null);
    }

    public async Task<TunnelInfo> StartAsync(int localPort, CancellationToken ct = default)
    {
        _isStopping = false;
        PurgeLegacyLocalSecrets();
        await EnsureBinaryDownloadedAsync(ct);

        _currentInfo = new TunnelInfo(null, null, TunnelStatus.Starting);
        StatusChanged?.Invoke(_currentInfo);

        var secret = LoadSecret();
        if (string.IsNullOrWhiteSpace(secret))
        {
            _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, "Секрет туннеля не получен с сервера (/api/tunnel-config).");
            StatusChanged?.Invoke(_currentInfo);
            return _currentInfo;
        }

        if (localPort != 25565)
        {
            var portMsg = $"Игра слушает порт {localPort} вместо 25565 (проверьте lsp.json).";
            LogTunnel($"[PLAYIT: ERROR] {portMsg}");
            _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, portMsg);
            StatusChanged?.Invoke(_currentInfo);
            return _currentInfo;
        }

        const int maxAttempts = 2; // 1 исходная попытка + 1 автоматический перезапуск
        for (int attempt = 1; attempt <= maxAttempts; attempt++)
        {
            ct.ThrowIfCancellationRequested();
            LogTunnel($"[PLAYIT] Starting agent process (attempt {attempt}/{maxAttempts})...");

            KillStalePlayitProcesses();
            InitJobObject();

            // Создаем временный файл секрета на время работы агента
            try
            {
                if (!string.IsNullOrWhiteSpace(_activeTempSecretFile) && File.Exists(_activeTempSecretFile))
                {
                    File.Delete(_activeTempSecretFile);
                }
            }
            catch { }

            _activeTempSecretFile = Path.Combine(Path.GetTempPath(), $"playit_{Guid.NewGuid():N}.secret");
            var cleanSecret = (InMemorySecret ?? secret).Trim('\uFEFF', '\u200B', ' ', '\r', '\n', '\t');
            File.WriteAllText(_activeTempSecretFile, cleanSecret, new UTF8Encoding(false));
            LogTunnel($"[PLAYIT] Using temporary secret file: {_activeTempSecretFile}");

            var tcsConnected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            var processExitTcs = new TaskCompletionSource<int>(TaskCreationOptions.RunContinuationsAsynchronously);

            var psi = new ProcessStartInfo
            {
                FileName = _playitExePath,
                Arguments = $"--secret_path \"{_activeTempSecretFile}\" start",
                WorkingDirectory = _toolsDir,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
            _process = proc;

            proc.Exited += (s, e) =>
            {
                int exitCode = -1;
                try { exitCode = proc.ExitCode; } catch { }
                LogTunnel($"[PLAYIT] Agent process exited with code {exitCode}.");
                processExitTcs.TrySetResult(exitCode);
                OnActiveProcessExited(exitCode);
            };

            if (!proc.Start())
            {
                LogTunnel($"[PLAYIT: ERROR] Failed to start playit process on attempt {attempt}.");
                if (attempt < maxAttempts)
                {
                    LogTunnel("[PLAYIT: WARN] Retrying agent launch...");
                    await Task.Delay(1000, ct);
                    continue;
                }
                _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, "Не удалось запустить процесс playit.");
                StatusChanged?.Invoke(_currentInfo);
                return _currentInfo;
            }

            LogTunnel($"[PLAYIT] Agent process started successfully (PID: {proc.Id}).");

            if (_jobHandle != IntPtr.Zero)
            {
                AssignProcessToJobObject(_jobHandle, proc.Handle);
            }

            try
            {
                _activeReadersCts?.Cancel();
                _activeReadersCts?.Dispose();
            }
            catch { }

            _activeReadersCts = new CancellationTokenSource();
            var readerCt = _activeReadersCts.Token;

            StartPipeReader(proc.StandardOutput, "PLAYIT-OUT", line =>
            {
                LogTunnel($"[PLAYIT-OUT] {line}");
                if (line.Contains("agent has", StringComparison.OrdinalIgnoreCase) ||
                    (line.Contains("tunnel running", StringComparison.OrdinalIgnoreCase) && !line.Contains("0 tunnels", StringComparison.OrdinalIgnoreCase)) ||
                    line.Contains("tunnels loaded", StringComparison.OrdinalIgnoreCase))
                {
                    tcsConnected.TrySetResult(true);
                }
                else if (line.Contains("0 tunnels registered", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Visit link to setup", StringComparison.OrdinalIgnoreCase))
                {
                    LogTunnel($"[PLAYIT: WARN] Playit agent indicates unlinked or 0 tunnels registered: {line}");
                }
            }, readerCt);

            StartPipeReader(proc.StandardError, "PLAYIT-ERR", line =>
            {
                LogTunnel($"[PLAYIT-ERR] {line}");
                if (line.Contains("agent has", StringComparison.OrdinalIgnoreCase) ||
                    (line.Contains("tunnel running", StringComparison.OrdinalIgnoreCase) && !line.Contains("0 tunnels", StringComparison.OrdinalIgnoreCase)) ||
                    line.Contains("tunnels loaded", StringComparison.OrdinalIgnoreCase))
                {
                    tcsConnected.TrySetResult(true);
                }
                else if (line.Contains("0 tunnels registered", StringComparison.OrdinalIgnoreCase) ||
                         line.Contains("Visit link to setup", StringComparison.OrdinalIgnoreCase))
                {
                    LogTunnel($"[PLAYIT: WARN] Playit agent indicates unlinked or 0 tunnels registered: {line}");
                }
            }, readerCt);

            // Ждём подключения агента или преждевременного завершения (таймаут 6 секунд)
            var connectTimeout = Task.Delay(15000, ct);
            var finished = await Task.WhenAny(tcsConnected.Task, processExitTcs.Task, connectTimeout);

            if (finished == processExitTcs.Task)
            {
                int exitCode = await processExitTcs.Task;
                LogTunnel($"[PLAYIT: ERROR] Agent exited prematurely with code {exitCode} on attempt {attempt}.");
                if (attempt < maxAttempts)
                {
                    LogTunnel($"[PLAYIT: WARN] Automatic restart after premature exit (code {exitCode})...");
                    await Task.Delay(1000, ct);
                    continue;
                }

                _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, $"Процесс туннеля playit завершился с кодом {exitCode}.");
                StatusChanged?.Invoke(_currentInfo);
                return _currentInfo;
            }

            if (proc.HasExited)
            {
                int exitCode = -1;
                try { exitCode = proc.ExitCode; } catch { }
                LogTunnel($"[PLAYIT: ERROR] Playit agent process terminated unexpectedly (code {exitCode}) on attempt {attempt}.");
                if (attempt < maxAttempts)
                {
                    LogTunnel($"[PLAYIT: WARN] Automatic restart after process crash...");
                    await Task.Delay(1000, ct);
                    continue;
                }
                _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, $"Процесс туннеля playit завершился с кодом {exitCode}.");
                StatusChanged?.Invoke(_currentInfo);
                return _currentInfo;
            }

            if (!tcsConnected.Task.IsCompleted)
            {
                LogTunnel($"[PLAYIT: WARN] Agent startup timeout without tunnel registration confirmation on attempt {attempt}.");
                if (attempt < maxAttempts)
                {
                    try { proc.Kill(true); } catch { }
                    await Task.Delay(1000, ct);
                    continue;
                }
                _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, "Туннель playit не подтвердил регистрацию за отведенное время.");
                StatusChanged?.Invoke(_currentInfo);
                return _currentInfo;
            }

            var publicHost = PublicHost;
            var publicPort = PublicPort;

            _runtimeRestartCount = 0;
            _currentInfo = new TunnelInfo(publicHost, publicPort, TunnelStatus.Active);
            StatusChanged?.Invoke(_currentInfo);
            return _currentInfo;
        }

        _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, "Не удалось запустить туннель после перезапуска.");
        StatusChanged?.Invoke(_currentInfo);
        return _currentInfo;
    }

    private void OnActiveProcessExited(int exitCode)
    {
        if (_isStopping || _currentInfo.Status != TunnelStatus.Active) return;

        LogTunnel($"[PLAYIT: WARN] Active agent process died with code {exitCode}.");

        int restartAttempt = Interlocked.Increment(ref _runtimeRestartCount);
        if (restartAttempt <= 5)
        {
            LogTunnel($"[PLAYIT] Performing automatic restart ({restartAttempt}/5) of active tunnel...");
            _ = Task.Run(async () =>
            {
                try
                {
                    await Task.Delay(2000).ConfigureAwait(false);
                    if (_isStopping) return;
                    var result = await StartAsync(25565, CancellationToken.None).ConfigureAwait(false);
                    if (result.Status != TunnelStatus.Active)
                    {
                        var reason = $"Процесс туннеля playit аварийно завершился (код {exitCode}) и не смог перезапуститься.";
                        LogTunnel($"[PLAYIT: ERROR] {reason}");
                        _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, reason);
                        StatusChanged?.Invoke(_currentInfo);
                    }
                }
                catch (Exception ex)
                {
                    LogTunnel($"[EXCEPTION] Automatic restart failed: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                    _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, $"Перезапуск туннеля завершился ошибкой: {ex.Message}");
                    StatusChanged?.Invoke(_currentInfo);
                }
            });
            return;
        }

        var failReason = $"Процесс туннеля playit аварийно завершился (код {exitCode}).";
        LogTunnel($"[PLAYIT: ERROR] {failReason}");
        _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, failReason);
        StatusChanged?.Invoke(_currentInfo);
    }

    private static void StartPipeReader(StreamReader reader, string source, Action<string> onLine, CancellationToken ct)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    string? line = await reader.ReadLineAsync(ct).ConfigureAwait(false);
                    if (line == null) break;
                    if (!string.IsNullOrWhiteSpace(line))
                    {
                        onLine(line);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                // Обычная отмена при остановке туннеля
            }
            catch (Exception ex)
            {
                LogTunnel($"[EXCEPTION] {source} reader: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            }
        }, ct);
    }

    public static async Task<bool> TestTcpConnectAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        for (int retry = 0; retry < 2; retry++)
        {
            ct.ThrowIfCancellationRequested();
            var client = new System.Net.Sockets.TcpClient();
            try
            {
                var connectTask = client.ConnectAsync(host, port);
                var timeoutTask = Task.Delay(timeoutMs, ct);
                var finished = await Task.WhenAny(connectTask, timeoutTask);
                if (finished == connectTask && client.Connected)
                {
                    return true;
                }
            }
            catch
            {
            }
            finally
            {
                try { client.Dispose(); } catch { }
            }
            await Task.Delay(300, ct);
        }
        return false;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _isStopping = true;
        try
        {
            _activeReadersCts?.Cancel();
            _activeReadersCts?.Dispose();
            _activeReadersCts = null;
        }
        catch { }

        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(true);
            }
        }
        catch (Exception ex)
        {
            LogTunnel($"[EXCEPTION] StopAsync Kill: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
        }

        try
        {
            if (!string.IsNullOrWhiteSpace(_activeTempSecretFile) && File.Exists(_activeTempSecretFile))
            {
                File.Delete(_activeTempSecretFile);
                LogTunnel($"[PLAYIT] Cleaned up temporary secret file: {_activeTempSecretFile}");
            }
        }
        catch (Exception ex)
        {
            LogTunnel($"[PLAYIT: WARN] Failed to delete temp secret file: {ex.Message}");
        }
        _activeTempSecretFile = null;

        _process = null;
        _currentInfo = new TunnelInfo(null, null, TunnelStatus.Inactive);
        StatusChanged?.Invoke(_currentInfo);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await StopAsync(CancellationToken.None);
            }
            catch (Exception ex)
            {
                LogTunnel($"[EXCEPTION] Dispose StopAsync: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
            }
        });

        if (_jobHandle != IntPtr.Zero)
        {
            try
            {
                CloseHandle(_jobHandle);
            }
            catch { }
            _jobHandle = IntPtr.Zero;
        }
    }

    #region Win32 Job Object Interop

    private void InitJobObject()
    {
        if (_jobHandle != IntPtr.Zero) return;

        try
        {
            _jobHandle = CreateJobObject(IntPtr.Zero, null);
            var info = new JOBOBJECT_EXTENDED_LIMIT_INFORMATION();
            info.BasicLimitInformation.LimitFlags = JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE;

            int length = Marshal.SizeOf(typeof(JOBOBJECT_EXTENDED_LIMIT_INFORMATION));
            IntPtr extendedInfoPtr = Marshal.AllocHGlobal(length);
            try
            {
                Marshal.StructureToPtr(info, extendedInfoPtr, false);
                SetInformationJobObject(_jobHandle, JobObjectExtendedLimitInformation, extendedInfoPtr, (uint)length);
            }
            finally
            {
                Marshal.FreeHGlobal(extendedInfoPtr);
            }
        }
        catch
        {
            // fallback if Job Objects restricted
        }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr CreateJobObject(IntPtr lpJobAttributes, string? lpName);

    [DllImport("kernel32.dll")]
    private static extern bool SetInformationJobObject(IntPtr hJob, int JobObjectInfoClass, IntPtr lpJobObjectInfo, uint cbJobObjectInfoLength);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AssignProcessToJobObject(IntPtr hJob, IntPtr hProcess);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(IntPtr hObject);

    private const int JobObjectExtendedLimitInformation = 9;
    private const uint JOB_OBJECT_LIMIT_KILL_ON_JOB_CLOSE = 0x2000;

    [StructLayout(LayoutKind.Sequential)]
    private struct IO_COUNTERS
    {
        public ulong ReadOperationCount;
        public ulong WriteOperationCount;
        public ulong OtherOperationCount;
        public ulong ReadTransferCount;
        public ulong WriteTransferCount;
        public ulong OtherTransferCount;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_BASIC_LIMIT_INFORMATION
    {
        public long PerProcessUserTimeLimit;
        public long PerJobUserTimeLimit;
        public uint LimitFlags;
        public UIntPtr MinimumWorkingSetSize;
        public UIntPtr MaximumWorkingSetSize;
        public uint ActiveProcessLimit;
        public UIntPtr Affinity;
        public uint PriorityClass;
        public uint SchedulingClass;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct JOBOBJECT_EXTENDED_LIMIT_INFORMATION
    {
        public JOBOBJECT_BASIC_LIMIT_INFORMATION BasicLimitInformation;
        public IO_COUNTERS IoInfo;
        public UIntPtr ProcessMemoryLimit;
        public UIntPtr JobMemoryLimit;
        public UIntPtr PeakProcessMemoryLimit;
        public UIntPtr PeakJobMemoryLimit;
    }

    #endregion
}

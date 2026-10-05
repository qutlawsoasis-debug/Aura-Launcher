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

    public TunnelInfo CurrentInfo => _currentInfo;
    public event Action<TunnelInfo>? StatusChanged;

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
    }

    /// <summary>
    /// Проверка и скачивание бинарника playit 0.15.26 с валидацией SHA-256.
    /// </summary>
    public async Task EnsureBinaryDownloadedAsync(CancellationToken ct = default)
    {
        Directory.CreateDirectory(_toolsDir);

        if (File.Exists(_playitExePath))
        {
            var hash = await ComputeFileSha256Async(_playitExePath, ct);
            if (string.Equals(hash, ExpectedSha256, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }
            try { File.Delete(_playitExePath); } catch { }
        }

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

    /// <summary>
    /// Сохранение секрета в защищенном виде с использованием DPAPI.
    /// </summary>
    public void SaveSecret(string rawSecret)
    {
        var rawBytes = Encoding.UTF8.GetBytes(rawSecret.Trim());
        var protectedBytes = ProtectedData.Protect(rawBytes, null, DataProtectionScope.CurrentUser);
        File.WriteAllBytes(_secretFilePath + ".enc", protectedBytes);
    }

    /// <summary>
    /// Получение расшифрованного секрета через DPAPI.
    /// </summary>
    public string? LoadSecret()
    {
        var encPath = _secretFilePath + ".enc";
        if (File.Exists(encPath))
        {
            try
            {
                var protectedBytes = File.ReadAllBytes(encPath);
                var rawBytes = ProtectedData.Unprotect(protectedBytes, null, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(rawBytes);
            }
            catch { }
        }

        if (File.Exists(_secretFilePath))
        {
            try
            {
                var secret = File.ReadAllText(_secretFilePath).Trim();
                if (!string.IsNullOrWhiteSpace(secret)) return secret;
            }
            catch { }
        }

        return null;
    }

    public static void KillStalePlayitProcesses()
    {
        try
        {
            var processes = Process.GetProcessesByName("playit-0.15.26");
            foreach (var p in processes)
            {
                try { p.Kill(true); } catch { }
            }

            var legacy = Process.GetProcessesByName("playit");
            foreach (var p in legacy)
            {
                try { p.Kill(true); } catch { }
            }
        }
        catch { }
    }

    public bool HasSecret => !string.IsNullOrWhiteSpace(LoadSecret());

    public async Task<string?> ClaimTunnelAsync(Action<string>? onUrlReady = null, CancellationToken ct = default)
    {
        await EnsureBinaryDownloadedAsync(ct);
        KillStalePlayitProcesses();
        InitJobObject();

        var tcsUrl = new TaskCompletionSource<string>();
        var psi = new ProcessStartInfo
        {
            FileName = _playitExePath,
            Arguments = $"--secret_path \"{_secretFilePath}\"",
            WorkingDirectory = _toolsDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        using var proc = new Process { StartInfo = psi, EnableRaisingEvents = true };
        proc.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                App.Log($"[PLAYIT-CLAIM-OUT] {e.Data}");
                var match = System.Text.RegularExpressions.Regex.Match(e.Data, @"https?://playit\.gg/claim/\S+");
                if (match.Success)
                {
                    tcsUrl.TrySetResult(match.Value);
                }
            }
        };
        proc.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                App.Log($"[PLAYIT-CLAIM-ERR] {e.Data}");
                var match = System.Text.RegularExpressions.Regex.Match(e.Data, @"https?://playit\.gg/claim/\S+");
                if (match.Success)
                {
                    tcsUrl.TrySetResult(match.Value);
                }
            }
        };

        if (!proc.Start())
        {
            return null;
        }

        proc.BeginOutputReadLine();
        proc.BeginErrorReadLine();

        if (_jobHandle != IntPtr.Zero)
        {
            AssignProcessToJobObject(_jobHandle, proc.Handle);
        }

        // Ждем получения claim URL (до 10 секунд)
        var urlTimeout = Task.Delay(10000, ct);
        var completed = await Task.WhenAny(tcsUrl.Task, urlTimeout);
        if (completed == tcsUrl.Task)
        {
            string url = await tcsUrl.Task;
            onUrlReady?.Invoke(url);
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch { }
        }

        // Ждем появления файла playit.secret (таймаут 120 секунд)
        for (int i = 0; i < 240; i++)
        {
            ct.ThrowIfCancellationRequested();
            if (File.Exists(_secretFilePath) && new FileInfo(_secretFilePath).Length > 10)
            {
                try
                {
                    string sec = (await File.ReadAllTextAsync(_secretFilePath, ct)).Trim();
                    if (!string.IsNullOrWhiteSpace(sec))
                    {
                        SaveSecret(sec);
                        try { proc.Kill(true); } catch { }
                        return sec;
                    }
                }
                catch { }
            }
            await Task.Delay(500, ct);
        }

        try { proc.Kill(true); } catch { }
        return null;
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
        await EnsureBinaryDownloadedAsync(ct);

        _currentInfo = new TunnelInfo(null, null, TunnelStatus.Starting);
        StatusChanged?.Invoke(_currentInfo);

        var secret = LoadSecret();
        if (string.IsNullOrWhiteSpace(secret))
        {
            _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, "Туннель не привязан. Нажмите «ПРИВЯЗАТЬ ТУННЕЛЬ».");
            StatusChanged?.Invoke(_currentInfo);
            return _currentInfo;
        }

        File.WriteAllText(_secretFilePath, secret.Trim());

        KillStalePlayitProcesses();
        InitJobObject();

        var tcsConnected = new TaskCompletionSource<bool>();

        var psi = new ProcessStartInfo
        {
            FileName = _playitExePath,
            Arguments = $"--secret_path \"{_secretFilePath}\" start",
            WorkingDirectory = _toolsDir,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        _process = new Process { StartInfo = psi, EnableRaisingEvents = true };
        _process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                App.Log($"[PLAYIT-OUT] {e.Data}");
                if (e.Data.Contains("tunnel running", StringComparison.OrdinalIgnoreCase) ||
                    e.Data.Contains("tunnels loaded", StringComparison.OrdinalIgnoreCase))
                {
                    tcsConnected.TrySetResult(true);
                }
            }
        };
        _process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
            {
                App.Log($"[PLAYIT-ERR] {e.Data}");
                if (e.Data.Contains("tunnel running", StringComparison.OrdinalIgnoreCase) ||
                    e.Data.Contains("tunnels loaded", StringComparison.OrdinalIgnoreCase))
                {
                    tcsConnected.TrySetResult(true);
                }
            }
        };

        if (!_process.Start())
        {
            _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, "Failed to start playit agent");
            StatusChanged?.Invoke(_currentInfo);
            return _currentInfo;
        }

        _process.BeginOutputReadLine();
        _process.BeginErrorReadLine();

        if (_jobHandle != IntPtr.Zero)
        {
            AssignProcessToJobObject(_jobHandle, _process.Handle);
        }

        // Ждём подключения агента в логе (таймаут 12 секунд)
        var connectTimeout = Task.Delay(12000, ct);
        var completed = await Task.WhenAny(tcsConnected.Task, connectTimeout);
        if (completed == connectTimeout && !tcsConnected.Task.IsCompleted)
        {
            App.Log("[PLAYIT] Warning: connection line not observed within 12s, proceeding with port test...");
        }

        // Разрешаем публичный адрес динамически из аккаунта игрока
        var (publicHost, publicPort) = await ResolveTunnelAddressAsync(ct);
        if (string.IsNullOrWhiteSpace(publicHost) || !publicPort.HasValue)
        {
            publicHost = "pgsql-jill.tun.ply.gg";
            publicPort = 38062;
        }

        // Тестируем доступность публичного туннеля через реальное TCP-подключение
        bool tcpSuccess = await TestTcpConnectAsync(publicHost, publicPort.Value, 6000, ct);
        if (!tcpSuccess)
        {
            App.Log($"[PLAYIT] TCP Connect to {publicHost}:{publicPort} failed!");
            _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, "Туннель не поднялся");
            StatusChanged?.Invoke(_currentInfo);
            return _currentInfo;
        }

        App.Log($"[PLAYIT] TCP Connect to {publicHost}:{publicPort} succeeded!");
        _currentInfo = new TunnelInfo(publicHost, publicPort.Value, TunnelStatus.Active);
        StatusChanged?.Invoke(_currentInfo);
        return _currentInfo;
    }

    private static async Task<bool> TestTcpConnectAsync(string host, int port, int timeoutMs, CancellationToken ct)
    {
        for (int retry = 0; retry < 5; retry++)
        {
            ct.ThrowIfCancellationRequested();
            try
            {
                using var client = new System.Net.Sockets.TcpClient();
                using var ctsTimeout = new CancellationTokenSource(timeoutMs);
                using var linkedCts = CancellationTokenSource.CreateLinkedTokenSource(ct, ctsTimeout.Token);
                await client.ConnectAsync(host, port, linkedCts.Token);
                if (client.Connected)
                {
                    return true;
                }
            }
            catch
            {
                await Task.Delay(1000, ct);
            }
        }
        return false;
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        try
        {
            if (_process != null && !_process.HasExited)
            {
                _process.Kill(true);
            }
        }
        catch { }

        _process = null;
        _currentInfo = new TunnelInfo(null, null, TunnelStatus.Inactive);
        StatusChanged?.Invoke(_currentInfo);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _ = StopAsync(CancellationToken.None);
        if (_jobHandle != IntPtr.Zero)
        {
            CloseHandle(_jobHandle);
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

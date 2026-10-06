using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Threading;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class AnthemService : IAnthemService
{
    private readonly IConfigService _configService;
    private Thread? _playerThread;
    private Dispatcher? _playerDispatcher;
    private MediaPlayer? _player;
    private readonly TaskCompletionSource<bool> _initTcs = new();

    private int _volumePercent = 100;
    private bool _isMuted = false;
    private bool _isPlaying = false;
    private bool _disposed = false;
    private string? _resolvedAudioPath;

    public int VolumePercent
    {
        get => _volumePercent;
        set => SetVolume(value);
    }

    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            if (_isMuted != value)
            {
                _isMuted = value;
                ApplyVolume();
                SaveSettings();
            }
        }
    }

    public bool IsPlaying => _isPlaying;

    public AnthemService(IConfigService configService)
    {
        _configService = configService;
        StartPlayerThread();
    }

    private void StartPlayerThread()
    {
        _playerThread = new Thread(() =>
        {
            try
            {
                _player = new MediaPlayer();
                _player.MediaEnded += (s, e) =>
                {
                    _isPlaying = false;
                };
                _player.MediaFailed += (s, e) =>
                {
                    _isPlaying = false;
                    FabricGameLaunchService.LogLauncherEvent($"[ANTHEM] Media failed: {e.ErrorException?.Message}");
                };

                _playerDispatcher = Dispatcher.CurrentDispatcher;
                _initTcs.TrySetResult(true);
                Dispatcher.Run();
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[ANTHEM] Thread exception: {ex.Message}");
                _initTcs.TrySetException(ex);
            }
        })
        {
            IsBackground = true,
            Name = "AnthemAudioThread"
        };

        _playerThread.SetApartmentState(ApartmentState.STA);
        _playerThread.Start();
    }

    public void Initialize()
    {
        LauncherConfig config;
        try
        {
            config = _configService.LoadConfigAsync().GetAwaiter().GetResult();
        }
        catch
        {
            config = _configService.CurrentConfig;
        }

        _volumePercent = Math.Clamp(config.AnthemVolume, 0, 100);
        _isMuted = config.AnthemMuted;

        _resolvedAudioPath = ResolveAudioFilePath(config);

        bool isUpdateRestart = IsUpdateRestart();
        bool isSecondInstance = IsSecondInstance();
        bool isTestMode = IsTestMode();

        FabricGameLaunchService.LogLauncherEvent($"[ANTHEM] Audio path: '{_resolvedAudioPath}', Vol: {_volumePercent}%, Muted: {_isMuted}, UpdateRestart: {isUpdateRestart}, SecondInstance: {isSecondInstance}, TestMode: {isTestMode}");

        if (isUpdateRestart)
        {
            FabricGameLaunchService.LogLauncherEvent("[ANTHEM] Anthem skipped (update restart)");
            return;
        }

        if (isSecondInstance)
        {
            FabricGameLaunchService.LogLauncherEvent("[ANTHEM] Anthem skipped (second instance)");
            return;
        }

        if (_isMuted)
        {
            FabricGameLaunchService.LogLauncherEvent("[ANTHEM] Anthem skipped (muted)");
            return;
        }

        if (_volumePercent <= 0)
        {
            FabricGameLaunchService.LogLauncherEvent("[ANTHEM] Anthem skipped (volume 0%)");
            return;
        }

        if (isTestMode)
        {
            FabricGameLaunchService.LogLauncherEvent("[ANTHEM] Anthem skipped (automated UI test mode)");
            return;
        }

        PlayAnthem(force: false);
    }

    public void PlayAnthem(bool force = false)
    {
        if (string.IsNullOrWhiteSpace(_resolvedAudioPath) || !File.Exists(_resolvedAudioPath))
        {
            _resolvedAudioPath = ResolveAudioFilePath(_configService.CurrentConfig);
            if (string.IsNullOrWhiteSpace(_resolvedAudioPath) || !File.Exists(_resolvedAudioPath))
            {
                FabricGameLaunchService.LogLauncherEvent("[ANTHEM: ERROR] Audio file not found.");
                return;
            }
        }

        _ = Task.Run(async () =>
        {
            await _initTcs.Task.ConfigureAwait(false);
            if (_playerDispatcher == null || _player == null || _disposed) return;

            await _playerDispatcher.InvokeAsync(() =>
            {
                try
                {
                    _player.Open(new Uri(_resolvedAudioPath, UriKind.Absolute));
                    _player.Volume = _isMuted ? 0.0 : (_volumePercent / 100.0);
                    _player.Play();
                    _isPlaying = true;
                    FabricGameLaunchService.LogLauncherEvent($"[ANTHEM] Started playback (vol: {_volumePercent}%, muted: {_isMuted}, pos: {_player.Position})");
                }
                catch (Exception ex)
                {
                    FabricGameLaunchService.LogLauncherEvent($"[ANTHEM: ERROR] Playback failed: {ex.Message}");
                }
            });
        });
    }

    public void ToggleMute()
    {
        _isMuted = !_isMuted;
        if (!_isMuted && _volumePercent == 0)
        {
            _volumePercent = 50;
        }

        ApplyVolume();
        SaveSettings();
    }

    public void SetVolume(int volumePercent)
    {
        int clamped = Math.Clamp(volumePercent, 0, 100);
        if (_volumePercent != clamped)
        {
            _volumePercent = clamped;
            if (_volumePercent > 0 && _isMuted)
            {
                // Note: user explicitly dragging volume above 0 un-mutes
                _isMuted = false;
            }
            ApplyVolume();
            SaveSettings();
        }
    }

    private void ApplyVolume()
    {
        if (_playerDispatcher != null && _player != null && !_disposed)
        {
            _playerDispatcher.InvokeAsync(() =>
            {
                try
                {
                    _player.Volume = _isMuted ? 0.0 : (_volumePercent / 100.0);
                }
                catch { }
            });
        }
    }

    private void SaveSettings()
    {
        _ = Task.Run(async () =>
        {
            try
            {
                await _configService.UpdateConfigAsync(c =>
                {
                    c.AnthemVolume = _volumePercent;
                    c.AnthemMuted = _isMuted;
                }).ConfigureAwait(false);
            }
            catch { }
        });
    }

    public void Stop()
    {
        _isPlaying = false;
        if (_playerDispatcher != null && _player != null && !_disposed)
        {
            try
            {
                _playerDispatcher.Invoke(() =>
                {
                    try
                    {
                        _player.Stop();
                        _player.Close();
                    }
                    catch { }
                });
            }
            catch { }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Stop();
        if (_playerDispatcher != null)
        {
            try
            {
                _playerDispatcher.InvokeShutdown();
            }
            catch { }
        }
    }

    private string? ResolveAudioFilePath(LauncherConfig config)
    {
        // 1. Custom path from user config
        if (!string.IsNullOrWhiteSpace(config.AnthemCustomPath) && File.Exists(config.AnthemCustomPath))
        {
            return Path.GetFullPath(config.AnthemCustomPath);
        }

        // 2. BaseDirectory Resources/Audio/anthem.mp3
        string candidate1 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Audio", "anthem.mp3");
        if (File.Exists(candidate1))
        {
            return Path.GetFullPath(candidate1);
        }

        // 3. CurrentDirectory Resources/Audio/anthem.mp3
        string candidate2 = Path.Combine(Environment.CurrentDirectory, "Resources", "Audio", "anthem.mp3");
        if (File.Exists(candidate2))
        {
            return Path.GetFullPath(candidate2);
        }

        // 4. Extract from embedded manifest resource to %APPDATA%\Aura\anthem.mp3
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string targetDir = Path.Combine(appData, "Aura");
            Directory.CreateDirectory(targetDir);
            string targetFile = Path.Combine(targetDir, "anthem.mp3");

            var asm = Assembly.GetExecutingAssembly();
            string? resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("anthem.mp3", StringComparison.OrdinalIgnoreCase));

            if (resName != null)
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream != null)
                {
                    using var fs = new FileStream(targetFile, FileMode.Create, FileAccess.Write);
                    stream.CopyTo(fs);
                    return Path.GetFullPath(targetFile);
                }
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[ANTHEM] Extraction error: {ex.Message}");
        }

        return candidate1;
    }

    private static bool IsUpdateRestart()
    {
        if (string.Equals(Environment.GetEnvironmentVariable("AURA_UPDATED_RESTART"), "1", StringComparison.OrdinalIgnoreCase))
            return true;

        if (Program.StartupArgs != null)
        {
            return Program.StartupArgs.Any(a =>
                a.Equals("--updated-restart", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--veloapp-install", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--veloapp-updated", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }

    private static bool IsSecondInstance()
    {
        try
        {
            var cur = Process.GetCurrentProcess();
            var sameNameProcesses = Process.GetProcessesByName(cur.ProcessName);
            if (sameNameProcesses.Length > 1)
                return true;

            var launcherProcesses = Process.GetProcessesByName("AuraLauncher");
            if (launcherProcesses.Length > 1)
                return true;
        }
        catch { }

        return false;
    }

    private static bool IsTestMode()
    {
        if (Program.StartupArgs != null)
        {
            if (Program.StartupArgs.Any(a => a.Equals("--selftest-anthem", StringComparison.OrdinalIgnoreCase)))
                return false;

            return Program.StartupArgs.Any(a =>
                a.Equals("--capture-shots", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--selftest", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--selftest-shots", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--selftest-lobby", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--selftest-lifecycle", StringComparison.OrdinalIgnoreCase) ||
                a.Equals("--selftest-kill-playit", StringComparison.OrdinalIgnoreCase));
        }

        return false;
    }
}

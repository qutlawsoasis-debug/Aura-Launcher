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

    private readonly System.Collections.Generic.List<string> _playlist = new();
    private int _currentTrackIndex = 0;
    private string _currentTrackTitle = "Aura Cyberpunk Anthem";

    public string CurrentTrackTitle => _currentTrackTitle;
    public event EventHandler<string>? TrackChanged;

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
        _volumePercent = Math.Clamp(_configService.CurrentConfig.AnthemVolume, 0, 100);
        _isMuted = _configService.CurrentConfig.AnthemMuted;
        _configService.ConfigChanged += (s, cfg) =>
        {
            int vol = Math.Clamp(cfg.AnthemVolume, 0, 100);
            bool muted = cfg.AnthemMuted;
            if (_volumePercent != vol || _isMuted != muted)
            {
                _volumePercent = vol;
                _isMuted = muted;
                ApplyVolume();
            }
        };
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
        LauncherConfig config = _configService.CurrentConfig;

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

        if (!config.MusicOnStartup)
        {
            FabricGameLaunchService.LogLauncherEvent("[ANTHEM] Anthem skipped (startup music disabled in settings)");
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
        EnsurePlaylistLoaded();

        if (_playlist.Count == 0)
        {
            FabricGameLaunchService.LogLauncherEvent("[ANTHEM: ERROR] Audio file not found.");
            return;
        }

        string currentPath = _playlist[_currentTrackIndex];
        UpdateTrackTitle(currentPath);

        _ = Task.Run(async () =>
        {
            await _initTcs.Task.ConfigureAwait(false);
            if (_playerDispatcher == null || _player == null || _disposed) return;

            await _playerDispatcher.InvokeAsync(() =>
            {
                try
                {
                    _player.Open(new Uri(currentPath, UriKind.Absolute));
                    _player.Volume = _isMuted ? 0.0 : (_volumePercent / 100.0);
                    _player.Play();
                    _isPlaying = true;
                    FabricGameLaunchService.LogLauncherEvent($"[ANTHEM] Started playback track '{_currentTrackTitle}' (vol: {_volumePercent}%, muted: {_isMuted})");
                }
                catch (Exception ex)
                {
                    FabricGameLaunchService.LogLauncherEvent($"[ANTHEM: ERROR] Playback failed: {ex.Message}");
                }
            });
        });
    }

    public void TogglePlayPause()
    {
        if (_playerDispatcher == null || _player == null || _disposed) return;

        _playerDispatcher.InvokeAsync(() =>
        {
            try
            {
                if (_isPlaying)
                {
                    _player.Pause();
                    _isPlaying = false;
                }
                else
                {
                    if (_playlist.Count == 0)
                    {
                        PlayAnthem(true);
                    }
                    else
                    {
                        _player.Play();
                        _isPlaying = true;
                    }
                }
            }
            catch { }
        });
    }

    public void NextTrack()
    {
        EnsurePlaylistLoaded();
        if (_playlist.Count <= 1) return;

        _currentTrackIndex = (_currentTrackIndex + 1) % _playlist.Count;
        PlayCurrentTrack();
    }

    public void PreviousTrack()
    {
        EnsurePlaylistLoaded();
        if (_playlist.Count <= 1) return;

        _currentTrackIndex = (_currentTrackIndex - 1 + _playlist.Count) % _playlist.Count;
        PlayCurrentTrack();
    }

    private void PlayCurrentTrack()
    {
        if (_playlist.Count == 0) return;
        string track = _playlist[_currentTrackIndex];
        UpdateTrackTitle(track);

        _playerDispatcher?.InvokeAsync(() =>
        {
            try
            {
                _player?.Open(new Uri(track, UriKind.Absolute));
                if (_player != null)
                {
                    _player.Volume = _isMuted ? 0.0 : (_volumePercent / 100.0);
                    _player.Play();
                    _isPlaying = true;
                }
            }
            catch { }
        });
    }

    private void UpdateTrackTitle(string path)
    {
        string fileName = Path.GetFileNameWithoutExtension(path);
        _currentTrackTitle = string.Equals(fileName, "anthem", StringComparison.OrdinalIgnoreCase)
            ? "Aura Cyberpunk Anthem"
            : fileName;
        TrackChanged?.Invoke(this, _currentTrackTitle);
    }

    private void EnsurePlaylistLoaded()
    {
        if (_playlist.Count > 0) return;

        if (string.IsNullOrWhiteSpace(_resolvedAudioPath) || !File.Exists(_resolvedAudioPath))
        {
            _resolvedAudioPath = ResolveAudioFilePath(_configService.CurrentConfig);
        }

        if (!string.IsNullOrWhiteSpace(_resolvedAudioPath) && File.Exists(_resolvedAudioPath))
        {
            _playlist.Add(_resolvedAudioPath);
        }

        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string musicDir = Path.Combine(appData, "Aura", "Music");
            if (Directory.Exists(musicDir))
            {
                var files = Directory.GetFiles(musicDir, "*.*")
                    .Where(f => f.EndsWith(".mp3", StringComparison.OrdinalIgnoreCase) ||
                                f.EndsWith(".wav", StringComparison.OrdinalIgnoreCase));
                foreach (var f in files)
                {
                    if (!_playlist.Contains(f, StringComparer.OrdinalIgnoreCase))
                    {
                        _playlist.Add(f);
                    }
                }
            }
        }
        catch { }
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
        _configService.CurrentConfig.AnthemVolume = _volumePercent;
        _configService.CurrentConfig.AnthemMuted = _isMuted;
        _ = Task.Run(async () =>
        {
            try
            {
                await _configService.SaveConfigAsync(_configService.CurrentConfig).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[ANTHEM: SAVE ERROR] {ex.Message}");
            }
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

        // 4. Check already extracted file in %APPDATA%\.aura\anthem.mp3
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string targetDir = Path.Combine(appData, ".aura");
        string targetFile = Path.Combine(targetDir, "anthem.mp3");
        if (File.Exists(targetFile) && new FileInfo(targetFile).Length > 10000)
        {
            return Path.GetFullPath(targetFile);
        }

        // 5. Extract from WPF Pack Resource (Application.GetResourceStream)
        try
        {
            var uris = new[]
            {
                new Uri("pack://application:,,,/Resources/Audio/anthem.mp3", UriKind.Absolute),
                new Uri("pack://application:,,,/AuraLauncher;component/Resources/Audio/anthem.mp3", UriKind.Absolute)
            };

            foreach (var uri in uris)
            {
                try
                {
                    var streamInfo = System.Windows.Application.GetResourceStream(uri);
                    if (streamInfo?.Stream != null)
                    {
                        Directory.CreateDirectory(targetDir);
                        using (var fs = new FileStream(targetFile, FileMode.Create, FileAccess.Write))
                        {
                            streamInfo.Stream.CopyTo(fs);
                        }
                        FabricGameLaunchService.LogLauncherEvent($"[ANTHEM] Successfully extracted pack resource to '{targetFile}'");
                        return Path.GetFullPath(targetFile);
                    }
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[ANTHEM] Pack stream extraction error: {ex.Message}");
        }

        // 6. Extract from embedded manifest resource
        try
        {
            var asm = Assembly.GetExecutingAssembly();
            string? resName = asm.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith("anthem.mp3", StringComparison.OrdinalIgnoreCase));

            if (resName != null)
            {
                using var stream = asm.GetManifestResourceStream(resName);
                if (stream != null)
                {
                    Directory.CreateDirectory(targetDir);
                    using var fs = new FileStream(targetFile, FileMode.Create, FileAccess.Write);
                    stream.CopyTo(fs);
                    FabricGameLaunchService.LogLauncherEvent($"[ANTHEM] Successfully extracted manifest stream to '{targetFile}'");
                    return Path.GetFullPath(targetFile);
                }
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[ANTHEM] Manifest extraction error: {ex.Message}");
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

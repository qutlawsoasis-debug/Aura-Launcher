using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Threading;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public sealed class AudioService : IAudioService
{
    private static readonly string[] SupportedExtensions = [".mp3", ".wav", ".m4a", ".flac", ".ogg", ".aac"];

    private readonly IConfigService _configService;
    private readonly MediaPlayer _player;
    private readonly DispatcherTimer _progressTimer;

    private readonly List<AudioTrack> _playlist = new();
    private int _currentTrackIndex = 0;
    private bool _isPlaying = false;
    private bool _isMuted = false;
    private int _volumePercent = 100;
    private TimeSpan _duration = TimeSpan.Zero;
    private bool _disposed = false;

    public IReadOnlyList<AudioTrack> Playlist => _playlist;
    public AudioTrack? CurrentTrack => (_currentTrackIndex >= 0 && _currentTrackIndex < _playlist.Count) ? _playlist[_currentTrackIndex] : null;
    public int CurrentTrackIndex => _currentTrackIndex;
    public bool HasTracks => _playlist.Count > 0;

    public TimeSpan Position
    {
        get => _player.Position;
        set
        {
            if (_disposed) return;
            _player.Position = value;
            PositionChanged?.Invoke(this, value);
        }
    }

    public TimeSpan Duration => _duration;
    public bool IsPlaying => _isPlaying;

    public int VolumePercent
    {
        get => _volumePercent;
        set
        {
            int clamped = Math.Clamp(value, 0, 100);
            if (_volumePercent != clamped)
            {
                _volumePercent = clamped;
                ApplyVolume();
                SaveConfig();
            }
        }
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
                SaveConfig();
            }
        }
    }

    public event EventHandler? MediaOpened;
    public event EventHandler? MediaEnded;
    public event EventHandler<bool>? PlayStateChanged;
    public event EventHandler<AudioTrack?>? TrackChanged;
    public event EventHandler<TimeSpan>? PositionChanged;

    public AudioService(IConfigService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));

        _volumePercent = Math.Clamp(_configService.CurrentConfig.AnthemVolume, 0, 100);
        _isMuted = _configService.CurrentConfig.AnthemMuted;

        _player = new MediaPlayer();
        _player.MediaOpened += OnPlayerMediaOpened;
        _player.MediaEnded += OnPlayerMediaEnded;
        _player.MediaFailed += OnPlayerMediaFailed;

        _progressTimer = new DispatcherTimer(DispatcherPriority.Background, Application.Current?.Dispatcher ?? Dispatcher.CurrentDispatcher)
        {
            Interval = TimeSpan.FromMilliseconds(250)
        };
        _progressTimer.Tick += (s, e) =>
        {
            if (_isPlaying && !_disposed)
            {
                PositionChanged?.Invoke(this, _player.Position);
            }
        };

        ApplyVolume();
    }

    public void Initialize()
    {
        if (_disposed) return;

        LoadPlaylist();

        if (_playlist.Count > 0)
        {
            // Restore last active track if saved in config
            string savedTitle = _configService.CurrentConfig.CurrentMusicTrack;
            if (!string.IsNullOrWhiteSpace(savedTitle))
            {
                int idx = _playlist.FindIndex(t => string.Equals(t.Title, savedTitle, StringComparison.OrdinalIgnoreCase));
                if (idx >= 0) _currentTrackIndex = idx;
            }

            PrepareTrack(_currentTrackIndex);

            bool isTestMode = Program.StartupArgs.Any(a => a.StartsWith("--selftest", StringComparison.OrdinalIgnoreCase)
                                                        || a.StartsWith("--fake-update", StringComparison.OrdinalIgnoreCase));
            bool isUpdateRestart = Program.StartupArgs.Any(a => a.Equals("--update-restart", StringComparison.OrdinalIgnoreCase));

            if (_configService.CurrentConfig.MusicOnStartup && !_isMuted && _volumePercent > 0 && !isTestMode && !isUpdateRestart)
            {
                Play();
            }
        }
    }

    private void LoadPlaylist()
    {
        _playlist.Clear();

        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string musicDir = Path.Combine(appData, ".aura", "Music");

        try
        {
            if (!Directory.Exists(musicDir))
            {
                Directory.CreateDirectory(musicDir);
            }

            // If empty, seed default track and cover from launcher assets
            var existingFiles = Directory.GetFiles(musicDir);
            if (existingFiles.Length == 0)
            {
                SeedDefaultTrack(musicDir);
            }

            var audioFiles = Directory.GetFiles(musicDir)
                .Where(f => SupportedExtensions.Contains(Path.GetExtension(f).ToLowerInvariant()))
                .OrderBy(f => Path.GetFileName(f), StringComparer.OrdinalIgnoreCase)
                .ToList();

            foreach (var file in audioFiles)
            {
                var track = ParseTrack(file, musicDir);
                _playlist.Add(track);
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[AUDIO-SERVICE: ERROR] Failed to load playlist: {ex.Message}");
        }

        if (_playlist.Count == 0)
        {
            // Check bundled fallback if folder is still empty
            string fallbackMp3 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Audio", "anthem.mp3");
            if (File.Exists(fallbackMp3))
            {
                string? fallbackCover = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Audio", "artist.jpg");
                _playlist.Add(new AudioTrack("Aura", "zxcursed", fallbackMp3, File.Exists(fallbackCover) ? fallbackCover : null));
            }
        }

        TrackChanged?.Invoke(this, CurrentTrack);
    }

    private void SeedDefaultTrack(string targetDir)
    {
        try
        {
            string sourceMp3 = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Audio", "anthem.mp3");
            string sourceCover = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "Audio", "artist.jpg");

            string destMp3 = Path.Combine(targetDir, "zxcursed - Aura.mp3");
            string destCover = Path.Combine(targetDir, "zxcursed - Aura.jpg");

            if (File.Exists(sourceMp3) && !File.Exists(destMp3))
            {
                File.Copy(sourceMp3, destMp3, overwrite: true);
            }
            if (File.Exists(sourceCover) && !File.Exists(destCover))
            {
                File.Copy(sourceCover, destCover, overwrite: true);
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[AUDIO-SERVICE] SeedDefaultTrack failed: {ex.Message}");
        }
    }

    private AudioTrack ParseTrack(string filePath, string dir)
    {
        string rawName = Path.GetFileNameWithoutExtension(filePath);
        string artist = "Aura";
        string title = rawName;

        if (rawName.Contains(" - "))
        {
            var parts = rawName.Split(new[] { " - " }, 2, StringSplitOptions.TrimEntries);
            artist = parts[0];
            title = parts[1];
        }
        else if (rawName.Contains(" · "))
        {
            var parts = rawName.Split(new[] { " · " }, 2, StringSplitOptions.TrimEntries);
            artist = parts[0];
            title = parts[1];
        }

        // Cover lookup
        string? coverPath = null;
        string[] candidates =
        [
            Path.Combine(dir, $"{rawName}.jpg"),
            Path.Combine(dir, $"{rawName}.png"),
            Path.Combine(dir, $"{rawName}.jpeg"),
            Path.Combine(dir, "cover.jpg"),
            Path.Combine(dir, "cover.png"),
            Path.Combine(dir, "folder.jpg")
        ];

        foreach (var c in candidates)
        {
            if (File.Exists(c))
            {
                coverPath = c;
                break;
            }
        }

        return new AudioTrack(title, artist, filePath, coverPath);
    }

    private void PrepareTrack(int index)
    {
        if (index < 0 || index >= _playlist.Count) return;

        _currentTrackIndex = index;
        var track = _playlist[index];

        try
        {
            _player.Open(new Uri(track.Path, UriKind.Absolute));
            _duration = TimeSpan.Zero;
            PositionChanged?.Invoke(this, TimeSpan.Zero);
            TrackChanged?.Invoke(this, track);
            SaveConfig();
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[AUDIO-SERVICE: ERROR] PrepareTrack '{track.Path}' failed: {ex.Message}");
        }
    }

    public void Play()
    {
        if (_disposed || _playlist.Count == 0) return;

        try
        {
            _player.Play();
            _isPlaying = true;
            _progressTimer.Start();
            PlayStateChanged?.Invoke(this, true);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[AUDIO-SERVICE: ERROR] Play failed: {ex.Message}");
        }
    }

    public void Pause()
    {
        if (_disposed) return;

        try
        {
            _player.Pause();
            _isPlaying = false;
            _progressTimer.Stop();
            PlayStateChanged?.Invoke(this, false);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[AUDIO-SERVICE: ERROR] Pause failed: {ex.Message}");
        }
    }

    public void TogglePlayPause()
    {
        if (_isPlaying) Pause();
        else Play();
    }

    public void Next()
    {
        if (_disposed || _playlist.Count == 0) return;

        _currentTrackIndex = (_currentTrackIndex + 1) % _playlist.Count;
        PrepareTrack(_currentTrackIndex);
        Play();
    }

    public void Previous()
    {
        if (_disposed || _playlist.Count == 0) return;

        if (_player.Position.TotalSeconds > 3.0)
        {
            Seek(TimeSpan.Zero);
            Play();
            return;
        }

        _currentTrackIndex = (_currentTrackIndex - 1 + _playlist.Count) % _playlist.Count;
        PrepareTrack(_currentTrackIndex);
        Play();
    }

    public void Seek(TimeSpan position)
    {
        if (_disposed) return;
        _player.Position = position;
        PositionChanged?.Invoke(this, position);
    }

    public void Stop()
    {
        if (_disposed) return;

        try
        {
            _progressTimer.Stop();
            _player.Stop();
            _isPlaying = false;
            PlayStateChanged?.Invoke(this, false);
        }
        catch { }
    }

    private void ApplyVolume()
    {
        if (_disposed) return;
        _player.Volume = _isMuted ? 0.0 : (_volumePercent / 100.0);
    }

    private void SaveConfig()
    {
        try
        {
            _ = _configService.UpdateConfigAsync(cfg =>
            {
                cfg.AnthemVolume = _volumePercent;
                cfg.AnthemMuted = _isMuted;
                if (CurrentTrack != null)
                {
                    cfg.CurrentMusicTrack = CurrentTrack.Title;
                }
            });
        }
        catch { }
    }

    private void OnPlayerMediaOpened(object? sender, EventArgs e)
    {
        _duration = _player.NaturalDuration.HasTimeSpan ? _player.NaturalDuration.TimeSpan : TimeSpan.Zero;
        MediaOpened?.Invoke(this, EventArgs.Empty);
    }

    private void OnPlayerMediaEnded(object? sender, EventArgs e)
    {
        MediaEnded?.Invoke(this, EventArgs.Empty);
        Next();
    }

    private void OnPlayerMediaFailed(object? sender, ExceptionEventArgs e)
    {
        FabricGameLaunchService.LogLauncherEvent($"[AUDIO-SERVICE: ERROR] Media failed: {e.ErrorException?.Message}");
        _isPlaying = false;
        _progressTimer.Stop();
        PlayStateChanged?.Invoke(this, false);
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        try
        {
            _progressTimer.Stop();
            _player.Stop();
            _player.Close();
        }
        catch { }
    }
}

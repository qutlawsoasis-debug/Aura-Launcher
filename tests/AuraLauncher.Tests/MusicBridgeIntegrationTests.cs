using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using Xunit;

namespace AuraLauncher.Tests;

/// <summary>
/// Сквозной интеграционный тест моста музыки (Лаунчер MusicBridgeService -> Игра aura-glass BridgeMusicSource).
/// 
/// Примечание: Для сквозной проверки используется заглушка IEmbedMusicPlayer (StubEmbedMusicPlayer),
/// которая эмулирует воспроизведение и тайминги без реального WebView2 и аудио-устройства.
/// Реальный звук в рамках интеграционного теста не проверялся.
/// </summary>
public class MusicBridgeIntegrationTests
{
    [Fact]
    [Trait("Category", "Integration")]
    public async Task MusicBridge_LauncherToGameClient_EndToEnd_VerifiesControlsAndTracking()
    {
        // 1. Создаем изолированную временную папку инстанса игры
        var tempInstanceDir = Path.Combine(Path.GetTempPath(), "AuraTest", "bridge-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempInstanceDir);

        var stubPlayer = new StubEmbedMusicPlayer();
        var track1 = new MusicBridgeTrack("track_rick1", "Rick Astley", "Never Gonna Give You Up", 213000, "https://example.com/cover1.jpg");
        var track2 = new MusicBridgeTrack("track_rick2", "Rick Astley", "Together Forever", 205000, "https://example.com/cover2.jpg");
        stubPlayer.AddTrack(track1);
        stubPlayer.AddTrack(track2);
        stubPlayer.SetCurrentIndex(0, playNow: true);

        using var dummyAudio = new DummyAudioService();
        using var bridge = new MusicBridgeService(dummyAudio, embedPlayer: stubPlayer, port: 0);

        try
        {
            bridge.Start();
            Assert.True(bridge.IsRunning, "MusicBridgeService must be running");

            // Записываем конфиг в тестовую папку config/aura-music-bridge.json
            bridge.WriteConfigFile(tempInstanceDir);
            var configPath = Path.Combine(tempInstanceDir, "config", "aura-music-bridge.json");
            Assert.True(File.Exists(configPath), "aura-music-bridge.json must exist");

            // 2. Компилируем Java-клиент (BridgeMusicSource, MusicClock, MusicBridgeE2ERunner)
            var rootDir = FindRepoRoot();
            var javaCommonDir = Path.Combine(rootDir, "aura-glass-common");
            var outClassDir = Path.Combine(tempInstanceDir, "java-classes");
            Directory.CreateDirectory(outClassDir);

            var searchDirs = new[]
            {
                Path.Combine(Path.GetTempPath(), "AuraTest"),
                Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".gradle", "caches")
            };
            var gsonJar = FindJar(searchDirs, "gson-*.jar");

            var javaFiles = Directory.GetFiles(Path.Combine(javaCommonDir, "src", "main", "java", "net", "auramc", "glass", "client", "music"), "*.java", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(javaCommonDir, "src", "test", "java", "net", "auramc", "glass", "client", "music"), "*.java", SearchOption.AllDirectories))
                .ToList();

            var cpList = new List<string> { outClassDir };
            if (!string.IsNullOrEmpty(gsonJar)) cpList.Add(gsonJar);
            string classpath = string.Join(Path.PathSeparator.ToString(), cpList);

            // Компиляция через javac
            var javacArgs = $"-cp \"{classpath}\" -d \"{outClassDir}\" " + string.Join(" ", javaFiles.Select(f => $"\"{f}\""));
            var javacPsi = new ProcessStartInfo
            {
                FileName = "javac",
                Arguments = javacArgs,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using (var javacProc = Process.Start(javacPsi))
            {
                Assert.NotNull(javacProc);
                await javacProc.WaitForExitAsync();
                if (javacProc.ExitCode != 0)
                {
                    var err = await javacProc.StandardError.ReadToEndAsync();
                    var outMsg = await javacProc.StandardOutput.ReadToEndAsync();
                    Assert.Fail($"javac compilation failed with code {javacProc.ExitCode}:\n{err}\n{outMsg}");
                }
            }

            // 3. Запуск Java E2E теста (прогон сценария 60 секунд)
            int testSeconds = 60;
            var javaPsi = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = $"-cp \"{classpath}\" net.auramc.glass.client.music.MusicBridgeE2ERunner \"{configPath}\" {testSeconds}",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var outputLines = new System.Collections.Concurrent.ConcurrentBag<string>();
            var tcsReadyToClose = new TaskCompletionSource<bool>();

            using var javaProc = new Process { StartInfo = javaPsi };
            javaProc.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    outputLines.Add(e.Data);
                    Trace.WriteLine("[JAVA-E2E] " + e.Data);
                    if (e.Data.Contains("SIGNAL:READY_FOR_BRIDGE_CLOSE"))
                    {
                        tcsReadyToClose.TrySetResult(true);
                    }
                }
            };
            javaProc.ErrorDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    outputLines.Add("[ERR] " + e.Data);
                    Trace.WriteLine("[JAVA-E2E:ERR] " + e.Data);
                }
            };

            javaProc.Start();
            javaProc.BeginOutputReadLine();
            javaProc.BeginErrorReadLine();

            // Ждем сигнала готовности к закрытию моста (после всех шагов play/pause/seek/next/60s tracking)
            var readyTask = await Task.WhenAny(tcsReadyToClose.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            if (readyTask != tcsReadyToClose.Task)
            {
                javaProc.Kill(true);
                var fullLog = string.Join(Environment.NewLine, outputLines);
                Assert.Fail($"Timed out waiting for SIGNAL:READY_FOR_BRIDGE_CLOSE. Process output:\n{fullLog}");
            }

            // Имитируем закрытие моста: останавливаем сервер и удаляем конфиг
            bridge.Stop();
            Assert.False(File.Exists(configPath), "Stop() must delete the bridge config file");

            // Ожидаем завершения Java-процесса
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await javaProc.WaitForExitAsync(cts.Token);

            var combinedOutput = string.Join(Environment.NewLine, outputLines);
            if (javaProc.ExitCode != 0)
            {
                Assert.Fail($"Java E2E runner failed with exit code {javaProc.ExitCode}. Log:\n{combinedOutput}");
            }

            Assert.Contains("=== ALL E2E MUSIC BRIDGE TESTS PASSED ===", combinedOutput);
        }
        finally
        {
            try
            {
                bridge.Dispose();
                if (Directory.Exists(tempInstanceDir))
                {
                    Directory.Delete(tempInstanceDir, true);
                }
            }
            catch { }
        }
    }

    private static string FindRepoRoot()
    {
        var current = AppDomain.CurrentDomain.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (Directory.Exists(Path.Combine(current, "aura-glass-common")))
            {
                return current;
            }
            current = Path.GetDirectoryName(current);
        }
        return @"C:\Users\magne\Documents\AuraDev\.agent-state\agy\w4";
    }

    private static string? FindJar(string[] searchDirs, string filter)
    {
        foreach (var searchDir in searchDirs)
        {
            if (!Directory.Exists(searchDir)) continue;
            try
            {
                var files = Directory.GetFiles(searchDir, filter, SearchOption.AllDirectories);
                var match = files.FirstOrDefault(f => !f.EndsWith("-sources.jar") && !f.EndsWith("-javadoc.jar"));
                if (match != null) return match;
            }
            catch { }
        }
        return null;
    }

    #region Stubs

    private class StubEmbedMusicPlayer : IEmbedMusicPlayer
    {
        private readonly object _lock = new();
        private readonly Stopwatch _stopwatch = new();
        private readonly List<MusicBridgeTrack> _queue = new();
        private int _currentIndex = -1;
        private string _state = "stopped";
        private long _basePositionMs = 0;
        private long _lastUpdatedAtUnixMs = 0;
        private double _volume = 1.0;

        public bool IsActive => CurrentTrack != null;
        public bool HasTransport => true;
        public bool IsReady => true;

        public string State
        {
            get { lock (_lock) return _state; }
            private set
            {
                lock (_lock) _state = value;
                StateChanged?.Invoke(value);
            }
        }

        public MusicBridgeTrack? CurrentTrack
        {
            get
            {
                lock (_lock)
                {
                    return (_currentIndex >= 0 && _currentIndex < _queue.Count) ? _queue[_currentIndex] : null;
                }
            }
        }

        public double CurrentTimeSeconds
        {
            get
            {
                lock (_lock)
                {
                    long pos = GetExactPositionMsLocked(out _);
                    return pos / 1000.0;
                }
            }
        }

        public long LastUpdatedAtUnixMs
        {
            get { lock (_lock) return _lastUpdatedAtUnixMs; }
        }

        public double Volume
        {
            get { lock (_lock) return _volume; }
            set { lock (_lock) _volume = Math.Clamp(value, 0.0, 1.0); }
        }

        public IReadOnlyList<MusicBridgeTrack> Queue
        {
            get { lock (_lock) return _queue.ToList(); }
        }

        public int CurrentIndex
        {
            get { lock (_lock) return _currentIndex; }
        }

        public event Action<string, object?>? CommandDispatched;
        public event Action<MusicBridgeTrack?>? TrackChanged;
        public event Action<string>? StateChanged;

        public void AddTrack(MusicBridgeTrack track)
        {
            lock (_lock)
            {
                _queue.Add(track);
                if (_currentIndex == -1)
                {
                    _currentIndex = 0;
                }
            }
        }

        public void SetCurrentIndex(int idx, bool playNow = false)
        {
            lock (_lock)
            {
                if (idx >= 0 && idx < _queue.Count)
                {
                    _currentIndex = idx;
                    _basePositionMs = 0;
                    _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (playNow)
                    {
                        _state = "playing";
                        _stopwatch.Restart();
                    }
                    else
                    {
                        _state = "stopped";
                        _stopwatch.Reset();
                    }
                }
            }
        }

        public Task<bool> PlayAsync()
        {
            lock (_lock)
            {
                if (_state != "playing")
                {
                    _state = "playing";
                    _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    _stopwatch.Restart();
                    StateChanged?.Invoke(_state);
                }
            }
            return Task.FromResult(true);
        }

        public Task<bool> PauseAsync()
        {
            lock (_lock)
            {
                if (_state == "playing")
                {
                    _basePositionMs += _stopwatch.ElapsedMilliseconds;
                    _stopwatch.Reset();
                    _state = "paused";
                    _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    StateChanged?.Invoke(_state);
                }
            }
            return Task.FromResult(true);
        }

        public Task<bool> NextAsync()
        {
            lock (_lock)
            {
                if (_currentIndex + 1 < _queue.Count)
                {
                    _currentIndex++;
                    _basePositionMs = 0;
                    _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (_state == "playing") _stopwatch.Restart();
                    TrackChanged?.Invoke(CurrentTrack);
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }

        public Task<bool> PreviousAsync()
        {
            lock (_lock)
            {
                if (_currentIndex > 0)
                {
                    _currentIndex--;
                    _basePositionMs = 0;
                    _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (_state == "playing") _stopwatch.Restart();
                    TrackChanged?.Invoke(CurrentTrack);
                    return Task.FromResult(true);
                }
            }
            return Task.FromResult(false);
        }

        public Task<bool> SeekAsync(long positionMs)
        {
            lock (_lock)
            {
                _basePositionMs = Math.Max(0, positionMs);
                _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                if (_state == "playing")
                {
                    _stopwatch.Restart();
                }
                else
                {
                    _stopwatch.Reset();
                }
            }
            return Task.FromResult(true);
        }

        public Task<bool> SetVolumeAsync(double volume)
        {
            lock (_lock)
            {
                _volume = Math.Clamp(volume, 0.0, 1.0);
            }
            return Task.FromResult(true);
        }

        public Task<bool> LoadVideoAsync(string videoId, string? title = null, string? artist = null, string? coverUrl = null, long durationMs = 0)
        {
            var track = new MusicBridgeTrack(videoId, artist ?? "Artist", title ?? videoId, durationMs, coverUrl);
            return QueueTrackAsync(track, true);
        }

        public Task<bool> QueueTrackAsync(MusicBridgeTrack track, bool playNow = true)
        {
            lock (_lock)
            {
                _queue.Add(track);
                if (playNow || _currentIndex == -1)
                {
                    _currentIndex = _queue.Count - 1;
                    _basePositionMs = 0;
                    _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    if (playNow)
                    {
                        _state = "playing";
                        _stopwatch.Restart();
                    }
                    TrackChanged?.Invoke(track);
                    StateChanged?.Invoke(_state);
                }
            }
            return Task.FromResult(true);
        }

        public long GetExactPositionMs(out long updatedAtUnixMs)
        {
            lock (_lock)
            {
                return GetExactPositionMsLocked(out updatedAtUnixMs);
            }
        }

        private long GetExactPositionMsLocked(out long updatedAtUnixMs)
        {
            if (_state == "playing" && _stopwatch.IsRunning)
            {
                long elapsed = _stopwatch.ElapsedMilliseconds;
                long pos = _basePositionMs + elapsed;
                long dur = CurrentTrack?.DurationMs ?? 0;
                if (dur > 0 && pos > dur) pos = dur;
                updatedAtUnixMs = _lastUpdatedAtUnixMs + elapsed;
                return Math.Max(0, pos);
            }
            else
            {
                updatedAtUnixMs = _lastUpdatedAtUnixMs > 0 ? _lastUpdatedAtUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return Math.Max(0, _basePositionMs);
            }
        }

        public void HandleEmbedMessage(string rawJson) { }
        public void AttachWebView(object? coreWebView2) { }
        public void Dispose()
        {
            lock (_lock)
            {
                _stopwatch.Stop();
            }
        }
    }

    private class DummyAudioService : IAudioService
    {
        public IReadOnlyList<AudioTrack> Playlist => Array.Empty<AudioTrack>();
        public AudioTrack? CurrentTrack => null;
        public int CurrentTrackIndex { get; set; } = -1;
        public bool HasTracks => false;
        public TimeSpan Position { get; set; } = TimeSpan.Zero;
        public TimeSpan Duration => TimeSpan.Zero;
        public bool IsPlaying => false;
        public int VolumePercent { get; set; } = 100;
        public bool IsMuted { get; set; } = false;

        public event EventHandler? MediaOpened;
        public event EventHandler? MediaEnded;
        public event EventHandler<bool>? PlayStateChanged;
        public event EventHandler<AudioTrack?>? TrackChanged;
        public event EventHandler<TimeSpan>? PositionChanged;

        public void Initialize() { }
        public void Play() { }
        public void Pause() { }
        public void TogglePlayPause() { }
        public void Next() { }
        public void Previous() { }
        public void Seek(TimeSpan position) { }
        public void Stop() { }
        public void Enqueue(AudioTrack track) { }
        public bool PlayTrack(string trackIdOrTitle) => false;
        public void Dispose() { }
    }

    #endregion
}

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
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
        var tempInstanceDir = Path.Combine(Path.GetTempPath(), "AuraTest", "bridge-e2e-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempInstanceDir);

        // Изолированный логгер моста в тестовой папке - исключает запись в %APPDATA%
        var bridgeLogPath = Path.Combine(tempInstanceDir, "music-bridge.log");
        Action<string> testLogger = msg =>
        {
            Trace.WriteLine(msg);
            try { File.AppendAllText(bridgeLogPath, $"[{DateTime.UtcNow:O}] {msg}\n"); } catch { }
        };

        var stubPlayer = new StubEmbedMusicPlayer();
        var track1 = new MusicBridgeTrack("track_rick1", "Rick Astley", "Never Gonna Give You Up", 213000, "https://example.com/cover1.jpg");
        var track2 = new MusicBridgeTrack("track_rick2", "Rick Astley", "Together Forever", 205000, "https://example.com/cover2.jpg");
        stubPlayer.AddTrack(track1);
        stubPlayer.AddTrack(track2);
        stubPlayer.SetCurrentIndex(0, playNow: true);

        using var dummyAudio = new DummyAudioService();
        using var bridge = new MusicBridgeService(dummyAudio, embedPlayer: stubPlayer, port: 0, logger: testLogger);

        Process? javacProc = null;
        Process? javaProc = null;

        try
        {
            bridge.Start();
            Assert.True(bridge.IsRunning, "MusicBridgeService must be running");
            Assert.True(File.Exists(bridgeLogPath), "Bridge must write events to the isolated logger file");

            bridge.WriteConfigFile(tempInstanceDir);
            var configPath = Path.Combine(tempInstanceDir, "config", "aura-music-bridge.json");
            Assert.True(File.Exists(configPath), "aura-music-bridge.json must exist");

            // Компиляция Java-клиента
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
            Assert.False(string.IsNullOrEmpty(gsonJar), "Gson JAR must be found in Gradle cache or temp directories");

            var javaFiles = Directory.GetFiles(Path.Combine(javaCommonDir, "src", "main", "java", "net", "auramc", "glass", "client", "music"), "*.java", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(javaCommonDir, "src", "test", "java", "net", "auramc", "glass", "client", "music"), "*.java", SearchOption.AllDirectories))
                .ToList();

            var cpList = new List<string> { outClassDir, gsonJar };
            string classpath = string.Join(Path.PathSeparator.ToString(), cpList);

            var javacPsi = new ProcessStartInfo
            {
                FileName = "javac",
                Arguments = $"-cp \"{classpath}\" -d \"{outClassDir}\" " + string.Join(" ", javaFiles.Select(f => $"\"{f}\"")),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            try
            {
                javacProc = Process.Start(javacPsi);
                Assert.NotNull(javacProc);
                var javacOutTask = javacProc.StandardOutput.ReadToEndAsync();
                var javacErrTask = javacProc.StandardError.ReadToEndAsync();

                using var javacCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await javacProc.WaitForExitAsync(javacCts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { javacProc.Kill(entireProcessTree: true); } catch { }
                    Assert.Fail("javac compilation timed out after 30 seconds");
                }

                await Task.WhenAny(Task.WhenAll(javacOutTask, javacErrTask), Task.Delay(2000));
                var outMsg = javacOutTask.IsCompleted ? await javacOutTask : string.Empty;
                var err = javacErrTask.IsCompleted ? await javacErrTask : string.Empty;

                if (javacProc.ExitCode != 0)
                {
                    Assert.Fail($"javac compilation failed with code {javacProc.ExitCode}:\n{err}\n{outMsg}");
                }
            }
            finally
            {
                try
                {
                    if (javacProc != null && !javacProc.HasExited)
                    {
                        javacProc.Kill(entireProcessTree: true);
                        javacProc.WaitForExit(1000);
                    }
                }
                catch { }
                javacProc?.Dispose();
                javacProc = null;
            }

            // Запуск Java E2E теста (60 секунд трекинга)
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

            var outputLines = new ConcurrentBag<string>();
            var trackingSamples = new ConcurrentBag<(long timeMs, long clockPos, long serverNow)>();
            var tcsReadyToClose = new TaskCompletionSource<bool>();

            javaProc = new Process { StartInfo = javaPsi };
            javaProc.OutputDataReceived += (s, e) =>
            {
                if (e.Data != null)
                {
                    outputLines.Add(e.Data);
                    Trace.WriteLine("[JAVA-E2E] " + e.Data);

                    var m = Regex.Match(e.Data, @"TRACKING_SAMPLE:\s+time=(\d+)\s+clock=(\d+)\s+serverNow=(\d+)");
                    if (m.Success)
                    {
                        trackingSamples.Add((long.Parse(m.Groups[1].Value), long.Parse(m.Groups[2].Value), long.Parse(m.Groups[3].Value)));
                    }

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

            var readyTask = await Task.WhenAny(tcsReadyToClose.Task, Task.Delay(TimeSpan.FromSeconds(120)));
            if (readyTask != tcsReadyToClose.Task)
            {
                try { javaProc.Kill(entireProcessTree: true); } catch { }
                var fullLog = string.Join(Environment.NewLine, outputLines);
                Assert.Fail($"Timed out waiting for SIGNAL:READY_FOR_BRIDGE_CLOSE. Process output:\n{fullLog}");
            }

            // Сравнение позиции MusicClock с независимым состоянием C# stub-плеера
            Assert.NotEmpty(trackingSamples);
            foreach (var sample in trackingSamples)
            {
                long stubPos = stubPlayer.GetExactPositionAtTimestamp(sample.timeMs);
                long driftFromStub = Math.Abs(sample.clockPos - stubPos);
                Assert.True(driftFromStub <= 150, $"MusicClock drift from C# stub player exceeded 150 ms: drift={driftFromStub}ms (clock={sample.clockPos}, stub={stubPos})");
            }

            // Закрытие моста
            bridge.Stop();
            Assert.False(File.Exists(configPath), "Stop() must delete the bridge config file");

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
                if (javaProc != null && !javaProc.HasExited)
                {
                    javaProc.Kill(entireProcessTree: true);
                    javaProc.WaitForExit(3000);
                }
            }
            catch { }
            javaProc?.Dispose();

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

    [Fact]
    [Trait("Category", "Integration")]
    public async Task MusicBridge_IgnoredSeek_FailsConvergenceVerification()
    {
        var tempInstanceDir = Path.Combine(Path.GetTempPath(), "AuraTest", "bridge-failseek-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempInstanceDir);

        var bridgeLogPath = Path.Combine(tempInstanceDir, "music-bridge.log");
        Action<string> testLogger = msg => { try { File.AppendAllText(bridgeLogPath, msg + "\n"); } catch { } };

        // Имитируем плеер, который игнорирует команду перемотки
        var stubPlayer = new StubEmbedMusicPlayer { IgnoreSeek = true };
        var track1 = new MusicBridgeTrack("track_rick1", "Rick Astley", "Never Gonna Give You Up", 213000, "https://example.com/cover1.jpg");
        stubPlayer.AddTrack(track1);
        stubPlayer.SetCurrentIndex(0, playNow: true);

        using var dummyAudio = new DummyAudioService();
        using var bridge = new MusicBridgeService(dummyAudio, embedPlayer: stubPlayer, port: 0, logger: testLogger);

        Process? javacProc = null;
        Process? javaProc = null;
        try
        {
            bridge.Start();
            bridge.WriteConfigFile(tempInstanceDir);
            var configPath = Path.Combine(tempInstanceDir, "config", "aura-music-bridge.json");

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
            Assert.False(string.IsNullOrEmpty(gsonJar));

            var javaFiles = Directory.GetFiles(Path.Combine(javaCommonDir, "src", "main", "java", "net", "auramc", "glass", "client", "music"), "*.java", SearchOption.AllDirectories)
                .Concat(Directory.GetFiles(Path.Combine(javaCommonDir, "src", "test", "java", "net", "auramc", "glass", "client", "music"), "*.java", SearchOption.AllDirectories))
                .ToList();

            var cpList = new List<string> { outClassDir, gsonJar };
            string classpath = string.Join(Path.PathSeparator.ToString(), cpList);

            // Компиляция
            var javacPsi = new ProcessStartInfo
            {
                FileName = "javac",
                Arguments = $"-cp \"{classpath}\" -d \"{outClassDir}\" " + string.Join(" ", javaFiles.Select(f => $"\"{f}\"")),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };
            try
            {
                javacProc = Process.Start(javacPsi);
                Assert.NotNull(javacProc);
                var javacOutTask = javacProc.StandardOutput.ReadToEndAsync();
                var javacErrTask = javacProc.StandardError.ReadToEndAsync();

                using var javacCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                try
                {
                    await javacProc.WaitForExitAsync(javacCts.Token);
                }
                catch (OperationCanceledException)
                {
                    try { javacProc.Kill(entireProcessTree: true); } catch { }
                    Assert.Fail("javac compilation timed out after 30 seconds");
                }

                await Task.WhenAny(Task.WhenAll(javacOutTask, javacErrTask), Task.Delay(2000));
                var outMsg = javacOutTask.IsCompleted ? await javacOutTask : string.Empty;
                var err = javacErrTask.IsCompleted ? await javacErrTask : string.Empty;
                if (javacProc.ExitCode != 0)
                {
                    Assert.Fail($"javac failed with code {javacProc.ExitCode}:\n{err}\n{outMsg}");
                }
            }
            finally
            {
                try
                {
                    if (javacProc != null && !javacProc.HasExited)
                    {
                        javacProc.Kill(entireProcessTree: true);
                        javacProc.WaitForExit(1000);
                    }
                }
                catch { }
                javacProc?.Dispose();
                javacProc = null;
            }

            // Запуск с флагом --seek-only
            var javaPsi = new ProcessStartInfo
            {
                FileName = "java",
                Arguments = $"-cp \"{classpath}\" net.auramc.glass.client.music.MusicBridgeE2ERunner \"{configPath}\" 1 --seek-only",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            var outputLines = new ConcurrentBag<string>();
            javaProc = new Process { StartInfo = javaPsi };
            javaProc.OutputDataReceived += (s, e) => { if (e.Data != null) outputLines.Add(e.Data); };
            javaProc.ErrorDataReceived += (s, e) => { if (e.Data != null) outputLines.Add("[ERR] " + e.Data); };

            javaProc.Start();
            javaProc.BeginOutputReadLine();
            javaProc.BeginErrorReadLine();

            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            await javaProc.WaitForExitAsync(cts.Token);

            var combinedOutput = string.Join(Environment.NewLine, outputLines);

            // Тест ОБЯЗАН завершиться с ошибкой, если плеер проигнорировал команду seek
            Assert.NotEqual(0, javaProc.ExitCode);
            Assert.Contains("Server position failed to converge to seek target", combinedOutput);
        }
        finally
        {
            try
            {
                if (javaProc != null && !javaProc.HasExited)
                {
                    javaProc.Kill(entireProcessTree: true);
                    javaProc.WaitForExit(3000);
                }
            }
            catch { }
            javaProc?.Dispose();

            try
            {
                bridge.Dispose();
                if (Directory.Exists(tempInstanceDir)) Directory.Delete(tempInstanceDir, true);
            }
            catch { }
        }
    }

    private static string FindRepoRoot()
    {
        var dirsToSearch = new[]
        {
            AppDomain.CurrentDomain.BaseDirectory,
            Directory.GetCurrentDirectory()
        };

        foreach (var startDir in dirsToSearch)
        {
            var dir = startDir;
            while (!string.IsNullOrEmpty(dir))
            {
                if (Directory.Exists(Path.Combine(dir, "aura-glass-common")) &&
                    File.Exists(Path.Combine(dir, "AuraLauncher.csproj")))
                {
                    return dir;
                }
                dir = Path.GetDirectoryName(dir);
            }
        }

        throw new DirectoryNotFoundException(
            $"Could not find repository root containing 'aura-glass-common' and 'AuraLauncher.csproj' by searching up from '{AppDomain.CurrentDomain.BaseDirectory}' or '{Directory.GetCurrentDirectory()}'.");
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

        public bool IgnoreSeek { get; set; } = false;

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

        private class PlaybackSegment
        {
            public long StartUnixMs { get; set; }
            public long? EndUnixMs { get; set; }
            public long BasePositionMs { get; set; }
            public bool IsPlaying { get; set; }
        }

        private readonly List<PlaybackSegment> _segments = new();

        private void RecordSegmentLocked(long basePositionMs, bool isPlaying)
        {
            long now = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_segments.Count > 0 && _segments[^1].EndUnixMs == null)
            {
                _segments[^1].EndUnixMs = now;
            }
            _segments.Add(new PlaybackSegment
            {
                StartUnixMs = now,
                BasePositionMs = basePositionMs,
                IsPlaying = isPlaying
            });
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
                    RecordSegmentLocked(_basePositionMs, playNow);
                    TrackChanged?.Invoke(CurrentTrack);
                    if (playNow)
                    {
                        State = "playing";
                        _stopwatch.Restart();
                    }
                }
            }
        }

        public long GetExactPositionMs(out long updatedAtUnixMs)
        {
            lock (_lock)
            {
                return GetExactPositionMsLocked(out updatedAtUnixMs);
            }
        }

        public long GetExactPositionAtTimestamp(long targetUnixMs)
        {
            lock (_lock)
            {
                if (_segments.Count == 0) return _basePositionMs;

                var segment = _segments.FirstOrDefault(s => targetUnixMs >= s.StartUnixMs && (s.EndUnixMs == null || targetUnixMs <= s.EndUnixMs.Value));
                if (segment == null)
                {
                    if (targetUnixMs >= _segments[^1].StartUnixMs)
                    {
                        segment = _segments[^1];
                    }
                    else
                    {
                        segment = _segments.OrderBy(s => Math.Abs(s.StartUnixMs - targetUnixMs)).First();
                    }
                }

                if (!segment.IsPlaying)
                {
                    return segment.BasePositionMs;
                }

                long elapsed = Math.Max(0, targetUnixMs - segment.StartUnixMs);
                long pos = segment.BasePositionMs + elapsed;
                if (CurrentTrack != null && CurrentTrack.DurationMs > 0 && pos > CurrentTrack.DurationMs)
                {
                    pos = CurrentTrack.DurationMs;
                }
                return pos;
            }
        }

        private long GetExactPositionMsLocked(out long updatedAtUnixMs)
        {
            if (_state == "playing" && _stopwatch.IsRunning)
            {
                long elapsed = _stopwatch.ElapsedMilliseconds;
                long pos = _basePositionMs + elapsed;
                if (CurrentTrack != null && CurrentTrack.DurationMs > 0 && pos > CurrentTrack.DurationMs)
                {
                    pos = CurrentTrack.DurationMs;
                }
                updatedAtUnixMs = _lastUpdatedAtUnixMs + elapsed;
                return Math.Max(0, pos);
            }
            else
            {
                updatedAtUnixMs = _lastUpdatedAtUnixMs > 0 ? _lastUpdatedAtUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return Math.Max(0, _basePositionMs);
            }
        }

        public Task<bool> PlayAsync()
        {
            lock (_lock)
            {
                if (CurrentTrack == null) return Task.FromResult(false);
                if (_state != "playing")
                {
                    State = "playing";
                    _stopwatch.Restart();
                    _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    RecordSegmentLocked(_basePositionMs, true);
                }
                CommandDispatched?.Invoke("play", null);
                return Task.FromResult(true);
            }
        }

        public Task<bool> PauseAsync()
        {
            lock (_lock)
            {
                if (CurrentTrack == null) return Task.FromResult(false);
                if (_state == "playing")
                {
                    _basePositionMs += _stopwatch.ElapsedMilliseconds;
                    _stopwatch.Reset();
                    _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                    State = "paused";
                    RecordSegmentLocked(_basePositionMs, false);
                }
                CommandDispatched?.Invoke("pause", null);
                return Task.FromResult(true);
            }
        }

        public Task<bool> SeekAsync(long positionMs)
        {
            lock (_lock)
            {
                CommandDispatched?.Invoke("seek", positionMs);
                if (IgnoreSeek)
                {
                    // Имитируем баг игнорирования команды перемотки
                    return Task.FromResult(true);
                }

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
                RecordSegmentLocked(_basePositionMs, _state == "playing");
                return Task.FromResult(true);
            }
        }

        public Task<bool> SetVolumeAsync(double volume)
        {
            lock (_lock)
            {
                _volume = Math.Clamp(volume, 0.0, 1.0);
                CommandDispatched?.Invoke("volume", _volume);
                return Task.FromResult(true);
            }
        }

        public Task<bool> NextAsync()
        {
            lock (_lock)
            {
                if (_currentIndex + 1 < _queue.Count)
                {
                    SetCurrentIndex(_currentIndex + 1, playNow: _state == "playing");
                    CommandDispatched?.Invoke("next", null);
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
        }

        public Task<bool> PreviousAsync()
        {
            lock (_lock)
            {
                if (_currentIndex - 1 >= 0)
                {
                    SetCurrentIndex(_currentIndex - 1, playNow: _state == "playing");
                    CommandDispatched?.Invoke("prev", null);
                    return Task.FromResult(true);
                }
                return Task.FromResult(false);
            }
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
                        State = "playing";
                        _stopwatch.Restart();
                    }
                    TrackChanged?.Invoke(track);
                    StateChanged?.Invoke(_state);
                }
            }
            return Task.FromResult(true);
        }

        public void AttachWebView(object? coreWebView2) { }
        public void HandleEmbedMessage(string rawJson) { }
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

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class EmbedMusicPlayer : IEmbedMusicPlayer
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private const string DefaultChannel = "aura-music";
    private const string DefaultEmbedOrigin = "https://lobby-api.vercel.app";

    private sealed class PendingPlayerState
    {
        public MusicBridgeTrack? Track { get; set; }
        public string DesiredState { get; set; } = "playing";
        public double StartSeconds { get; set; } = 0.0;
        public double? Volume { get; set; }
    }

    private readonly object _lock = new();
    private readonly Stopwatch _stopwatch = new();
    private dynamic? _coreWebView2;
    private Func<string, Task<string?>>? _customScriptExecutor;

    private string _state = "stopped";
    private MusicBridgeTrack? _currentTrack;
    private PendingPlayerState? _pendingState;
    private double _currentTimeSeconds = 0.0;
    private long _lastUpdatedAtUnixMs = 0;
    private double _volume = 1.0;
    private long _durationMs = 0;
    private bool _isReady = false;
    private bool _disposed = false;

    private readonly List<MusicBridgeTrack> _queue = new();
    private int _currentIndex = -1;

    public bool IsActive
    {
        get
        {
            lock (_lock) return _currentTrack != null || _isReady;
        }
    }

    public bool HasTransport
    {
        get
        {
            lock (_lock) return _coreWebView2 != null || _customScriptExecutor != null;
        }
    }

    public bool IsReady
    {
        get
        {
            lock (_lock) return _isReady;
        }
    }

    public string State
    {
        get
        {
            lock (_lock) return _state;
        }
        private set
        {
            string oldState;
            lock (_lock)
            {
                oldState = _state;
                _state = value;
            }
            if (!string.Equals(oldState, value, StringComparison.OrdinalIgnoreCase))
            {
                StateChanged?.Invoke(value);
            }
        }
    }

    public MusicBridgeTrack? CurrentTrack
    {
        get
        {
            lock (_lock) return _currentTrack;
        }
        private set
        {
            lock (_lock) _currentTrack = value;
            TrackChanged?.Invoke(value);
        }
    }

    public double CurrentTimeSeconds
    {
        get
        {
            lock (_lock) return _currentTimeSeconds;
        }
    }

    public long LastUpdatedAtUnixMs
    {
        get
        {
            lock (_lock) return _lastUpdatedAtUnixMs;
        }
    }

    public double Volume
    {
        get
        {
            lock (_lock) return _volume;
        }
        set
        {
            lock (_lock) _volume = Math.Clamp(value, 0.0, 1.0);
            _ = SetVolumeAsync(_volume);
        }
    }

    public IReadOnlyList<MusicBridgeTrack> Queue
    {
        get
        {
            lock (_lock) return _queue.ToList();
        }
    }

    public int CurrentIndex
    {
        get
        {
            lock (_lock) return _currentIndex;
        }
    }

    public event Action<string, object?>? CommandDispatched;
    public event Action<MusicBridgeTrack?>? TrackChanged;
    public event Action<string>? StateChanged;

    public EmbedMusicPlayer(Func<string, Task<string?>>? customScriptExecutor = null)
    {
        _customScriptExecutor = customScriptExecutor;
        _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public void AttachWebView(object? coreWebView2)
    {
        lock (_lock)
        {
            _coreWebView2 = coreWebView2;
            _isReady = false;
        }
        FabricGameLaunchService.LogLauncherEvent("[EMBED-PLAYER] Attached WebView2 to embed music player");

        if (_coreWebView2 != null)
        {
            _ = Task.Run(async () =>
            {
                string initScript = $$"""
                (function() {
                    const IFRAME_ID = 'aura-yt-embed-frame';
                    const CHANNEL = '{{DefaultChannel}}';
                    const EXPECTED_ORIGIN = '{{DefaultEmbedOrigin}}';
                    if (!window.__auraMusicHostAdapterInstalled) {
                        window.__auraMusicHostAdapterInstalled = true;
                        window.addEventListener('message', function(event) {
                            try {
                                var frame = document.getElementById(IFRAME_ID);
                                if (!frame || !frame.contentWindow) return;
                                var frameOrigin = EXPECTED_ORIGIN;
                                try { frameOrigin = new URL(frame.src).origin; } catch (e) {}

                                if (event.source !== frame.contentWindow) return;
                                if (event.origin !== frameOrigin) return;

                                var d = event.data;
                                if (!d || d.source !== 'aura-yt-embed' || d.channel !== CHANNEL) return;

                                var payload = typeof d === 'string' ? JSON.parse(d) : Object.assign({}, d);
                                payload.origin = event.origin;

                                if (window.chrome && window.chrome.webview) {
                                    window.chrome.webview.postMessage(JSON.stringify(payload));
                                }
                            } catch (e) {}
                        });
                    }
                })();
                """;
                await ExecuteScriptOnUiAsync(initScript);
            });
        }
    }

    public void HandleEmbedMessage(string rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return;

        try
        {
            using var doc = JsonDocument.Parse(rawJson);
            var root = doc.RootElement;

            if (!root.TryGetProperty("source", out var src) || src.GetString() != "aura-yt-embed")
            {
                return;
            }

            if (root.TryGetProperty("channel", out var chanProp))
            {
                string? chan = chanProp.GetString();
                if (!string.IsNullOrEmpty(chan) && !string.Equals(chan, DefaultChannel, StringComparison.OrdinalIgnoreCase))
                {
                    FabricGameLaunchService.LogLauncherEvent($"[EMBED-PLAYER: WARN] Rejected message with invalid channel '{chan}'");
                    return;
                }
            }

            if (root.TryGetProperty("origin", out var originProp))
            {
                string? origin = originProp.GetString();
                if (!string.IsNullOrEmpty(origin) && !IsValidEmbedOrigin(origin))
                {
                    FabricGameLaunchService.LogLauncherEvent($"[EMBED-PLAYER: WARN] Rejected message from untrusted origin '{origin}'");
                    return;
                }
            }

            string evt = root.TryGetProperty("event", out var evProp) ? evProp.GetString() ?? "" : "";
            string videoId = root.TryGetProperty("videoId", out var vidProp) ? vidProp.GetString() ?? "" : "";

            string? newStateToNotify = null;
            PendingPlayerState? stateToApplyOnReady = null;

            lock (_lock)
            {
                switch (evt)
                {
                    case "ready":
                        if (!_isReady)
                        {
                            _isReady = true;
                            if (_pendingState != null)
                            {
                                stateToApplyOnReady = _pendingState;
                                _pendingState = null;
                            }
                        }
                        break;

                    case "timeUpdate":
                        if (root.TryGetProperty("currentTime", out var ctProp) && ctProp.TryGetDouble(out double ct))
                        {
                            _currentTimeSeconds = ct;
                        }
                        if (root.TryGetProperty("duration", out var durProp) && durProp.TryGetDouble(out double dur))
                        {
                            _durationMs = (long)Math.Round(dur * 1000.0);
                        }

                        _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        if (_state == "playing")
                        {
                            _stopwatch.Restart();
                        }
                        else
                        {
                            _stopwatch.Reset();
                        }

                        if (_currentTrack != null && _durationMs > 0 && _currentTrack.DurationMs != _durationMs)
                        {
                            _currentTrack = _currentTrack with { DurationMs = _durationMs };
                        }
                        break;

                    case "stateChange":
                        int stateCode = root.TryGetProperty("state", out var scProp) && scProp.TryGetInt32(out int sc) ? sc : -1;
                        if (stateCode == 1) // YT.PlayerState.PLAYING
                        {
                            CommitElapsedTimeLocked();
                            _state = "playing";
                            _stopwatch.Restart();
                            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            newStateToNotify = _state;
                        }
                        else if (stateCode == 2) // YT.PlayerState.PAUSED
                        {
                            CommitElapsedTimeLocked();
                            _state = "paused";
                            _stopwatch.Reset();
                            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            newStateToNotify = _state;
                        }
                        else if (stateCode == 0) // YT.PlayerState.ENDED
                        {
                            _state = "stopped";
                            _stopwatch.Reset();
                            _currentTimeSeconds = 0;
                            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                            newStateToNotify = _state;

                            // Auto-advance queue if next track exists
                            _ = Task.Run(async () =>
                            {
                                try { await NextAsync(); } catch { }
                            });
                        }
                        break;

                    case "error":
                    case "unavailable":
                        _state = "stopped";
                        _stopwatch.Reset();
                        newStateToNotify = _state;
                        FabricGameLaunchService.LogLauncherEvent($"[EMBED-PLAYER: WARN] Embed error for video '{videoId}'");
                        break;
                }
            }

            if (stateToApplyOnReady != null && stateToApplyOnReady.Track != null)
            {
                var track = stateToApplyOnReady.Track;
                double startSec = Math.Max(0.0, stateToApplyOnReady.StartSeconds);

                if (stateToApplyOnReady.DesiredState == "paused")
                {
                    var payload = new { videoId = track.Id, startSeconds = startSec };
                    _ = DispatchCommandAsync("cue", payload);
                }
                else if (stateToApplyOnReady.DesiredState == "playing")
                {
                    var payload = new { videoId = track.Id, startSeconds = startSec };
                    _ = DispatchCommandAsync("load", payload);
                }

                if (stateToApplyOnReady.Volume.HasValue)
                {
                    var volPayload = new { value = Math.Round(stateToApplyOnReady.Volume.Value * 100.0, 1) };
                    _ = DispatchCommandAsync("volume", volPayload);
                }
            }

            if (newStateToNotify != null)
            {
                StateChanged?.Invoke(newStateToNotify);
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[EMBED-PLAYER: ERROR] Failed to parse embed message: {ex.Message}");
        }
    }

    private static bool IsValidEmbedOrigin(string origin)
    {
        if (string.IsNullOrWhiteSpace(origin)) return false;
        try
        {
            var uri = new Uri(origin);
            string host = uri.Host.ToLowerInvariant();
            if (host == "lobby-api.vercel.app" || host == "localhost" || host == "127.0.0.1" || host == "[::1]")
            {
                return true;
            }
            if (host.EndsWith(".auramc.net") || host.EndsWith(".vercel.app"))
            {
                return true;
            }
        }
        catch { }
        return false;
    }

    public long GetExactPositionMs(out long updatedAtUnixMs)
    {
        lock (_lock)
        {
            long baseMs = (long)Math.Round(_currentTimeSeconds * 1000.0);
            if (_state == "playing" && _stopwatch.IsRunning)
            {
                long elapsed = _stopwatch.ElapsedMilliseconds;
                long pos = baseMs + elapsed;
                if (_durationMs > 0 && pos > _durationMs) pos = _durationMs;
                updatedAtUnixMs = _lastUpdatedAtUnixMs + elapsed;
                return Math.Max(0, pos);
            }
            else
            {
                updatedAtUnixMs = _lastUpdatedAtUnixMs > 0 ? _lastUpdatedAtUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return Math.Max(0, baseMs);
            }
        }
    }

    private void CommitElapsedTimeLocked()
    {
        if (_state == "playing" && _stopwatch.IsRunning)
        {
            long elapsed = _stopwatch.ElapsedMilliseconds;
            _currentTimeSeconds += elapsed / 1000.0;
            if (_durationMs > 0 && (_currentTimeSeconds * 1000.0) > _durationMs)
            {
                _currentTimeSeconds = _durationMs / 1000.0;
            }
            _lastUpdatedAtUnixMs += elapsed;
            _stopwatch.Reset();
        }
    }

    public async Task<bool> LoadVideoAsync(string videoId, string? title = null, string? artist = null, string? coverUrl = null, long durationMs = 0)
    {
        if (string.IsNullOrWhiteSpace(videoId)) return false;
        if (!HasTransport)
        {
            FabricGameLaunchService.LogLauncherEvent("[EMBED-PLAYER: WARN] LoadVideoAsync rejected: No transport attached");
            return false;
        }

        string effTitle = !string.IsNullOrWhiteSpace(title) ? title : videoId;
        string effArtist = !string.IsNullOrWhiteSpace(artist) ? artist : "Aura";
        var track = new MusicBridgeTrack(videoId, effArtist, effTitle, durationMs, coverUrl);

        return await QueueTrackAsync(track, playNow: true);
    }

    public async Task<bool> QueueTrackAsync(MusicBridgeTrack track, bool playNow = true)
    {
        if (!HasTransport)
        {
            FabricGameLaunchService.LogLauncherEvent("[EMBED-PLAYER: WARN] QueueTrackAsync rejected: No transport attached");
            return false;
        }

        int targetIndex;
        lock (_lock)
        {
            _queue.Add(track);
            if (playNow || _currentIndex == -1)
            {
                _currentIndex = _queue.Count - 1;
                targetIndex = _currentIndex;
            }
            else
            {
                return true; // Added to queue, current track continues
            }
        }

        return await PlayTrackAtIndexAsync(targetIndex);
    }

    private async Task<bool> PlayTrackAtIndexAsync(int index)
    {
        MusicBridgeTrack track;
        lock (_lock)
        {
            if (index < 0 || index >= _queue.Count) return false;
            track = _queue[index];
            _currentTrack = track;
            _currentTimeSeconds = 0.0;
            _durationMs = track.DurationMs;
            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _stopwatch.Reset();

            if (!_isReady)
            {
                _pendingState = new PendingPlayerState
                {
                    Track = track,
                    DesiredState = "playing",
                    StartSeconds = 0.0,
                    Volume = null
                };
            }
            else
            {
                _pendingState = null;
            }
        }

        TrackChanged?.Invoke(track);

        var payload = new { videoId = track.Id, startSeconds = 0 };
        bool dispatched = await DispatchCommandAsync("load", payload);
        if (!dispatched)
        {
            lock (_lock)
            {
                _state = "stopped";
                _pendingState = null;
            }
            return false;
        }
        return true;
    }

    public async Task<bool> PlayAsync()
    {
        if (!HasTransport) return false;

        lock (_lock)
        {
            if (_currentTrack == null) return false;
            CommitElapsedTimeLocked();
            _state = "playing";
            _stopwatch.Restart();
            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (!_isReady && _pendingState != null)
            {
                _pendingState.DesiredState = "playing";
            }
        }

        StateChanged?.Invoke("playing");
        return await DispatchCommandAsync("play", null);
    }

    public async Task<bool> PauseAsync()
    {
        if (!HasTransport) return false;

        lock (_lock)
        {
            CommitElapsedTimeLocked();
            _state = "paused";
            _stopwatch.Reset();
            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();

            if (!_isReady && _pendingState != null)
            {
                _pendingState.DesiredState = "paused";
            }
        }

        StateChanged?.Invoke("paused");
        return await DispatchCommandAsync("pause", null);
    }

    public async Task<bool> NextAsync()
    {
        if (!HasTransport) return false;

        int nextIndex;
        lock (_lock)
        {
            if (_currentIndex + 1 >= _queue.Count)
            {
                return false; // Reached end of queue
            }
            _currentIndex++;
            nextIndex = _currentIndex;
        }

        return await PlayTrackAtIndexAsync(nextIndex);
    }

    public async Task<bool> PreviousAsync()
    {
        if (!HasTransport) return false;

        int prevIndex;
        bool doSeek = false;
        lock (_lock)
        {
            if (_currentIndex < 0 || _queue.Count == 0) return false;

            if (CurrentTimeSeconds > 3.0 || _currentIndex == 0)
            {
                doSeek = true;
                prevIndex = _currentIndex;
            }
            else
            {
                _currentIndex--;
                prevIndex = _currentIndex;
            }
        }

        if (doSeek)
        {
            return await SeekAsync(0);
        }

        return await PlayTrackAtIndexAsync(prevIndex);
    }

    public async Task<bool> SeekAsync(long positionMs)
    {
        if (!HasTransport) return false;

        double seconds = Math.Max(0, positionMs / 1000.0);
        lock (_lock)
        {
            _currentTimeSeconds = seconds;
            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_state == "playing")
            {
                _stopwatch.Restart();
            }
            else
            {
                _stopwatch.Reset();
            }

            if (!_isReady && _pendingState != null)
            {
                _pendingState.StartSeconds = seconds;
            }
        }

        return await DispatchCommandAsync("seek", new { seconds });
    }

    public async Task<bool> SetVolumeAsync(double volume)
    {
        if (!HasTransport) return false;

        double clamped = Math.Clamp(volume, 0.0, 1.0);
        lock (_lock)
        {
            _volume = clamped;
            if (!_isReady && _pendingState != null)
            {
                _pendingState.Volume = clamped;
            }
        }

        return await DispatchCommandAsync("volume", new { value = clamped * 100.0 });
    }

    private string BuildCommandJson(string op, object? payload)
    {
        var dict = new Dictionary<string, object?>
        {
            ["source"] = "aura-yt-host",
            ["channel"] = DefaultChannel,
            ["op"] = op
        };

        if (payload != null)
        {
            var props = payload.GetType().GetProperties();
            foreach (var prop in props)
            {
                dict[prop.Name] = prop.GetValue(payload);
            }
        }

        return JsonSerializer.Serialize(dict, _jsonOptions);
    }

    private static bool ParseJsBooleanResult(string? res)
    {
        if (string.IsNullOrWhiteSpace(res)) return false;
        string trimmed = res.Trim();
        if (string.Equals(trimmed, "true", StringComparison.OrdinalIgnoreCase)) return true;
        if (string.Equals(trimmed, "false", StringComparison.OrdinalIgnoreCase)) return false;
        if (string.Equals(trimmed, "null", StringComparison.OrdinalIgnoreCase)) return false;

        try
        {
            using var doc = JsonDocument.Parse(trimmed);
            if (doc.RootElement.ValueKind == JsonValueKind.True) return true;
            if (doc.RootElement.ValueKind == JsonValueKind.False) return false;
            if (doc.RootElement.ValueKind == JsonValueKind.Null) return false;
            if (doc.RootElement.ValueKind == JsonValueKind.String)
            {
                return bool.TryParse(doc.RootElement.GetString(), out bool b) && b;
            }
        }
        catch
        {
            return false;
        }
        return false;
    }

    private async Task<bool> DispatchCommandAsync(string op, object? payload)
    {
        CommandDispatched?.Invoke(op, payload);

        if (!HasTransport)
        {
            return false;
        }

        try
        {
            string cmdJson = BuildCommandJson(op, payload);
            const string targetOrigin = DefaultEmbedOrigin;
            string script = $"(function(){{try{{var f=document.getElementById('aura-yt-embed-frame')||document.querySelector('iframe[src*=\"embed/player.html\"]');if(!f){{f=document.createElement('iframe');f.id='aura-yt-embed-frame';f.style.display='none';f.allow='autoplay; encrypted-media';var p=window.location.origin||'http://127.0.0.1';f.src='{targetOrigin}/embed/player.html?channel={DefaultChannel}&parentOrigin='+encodeURIComponent(p);document.body.appendChild(f);}}if(f&&f.contentWindow){{var o='{targetOrigin}';try{{o=new URL(f.src).origin;}}catch(e){{}}f.contentWindow.postMessage({cmdJson},o);return true;}}return false;}}catch(e){{return false;}}}})();";

            string? res = await ExecuteScriptOnUiAsync(script);
            return ParseJsBooleanResult(res);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[EMBED-PLAYER: ERROR] DispatchCommandAsync failed: {ex.Message}");
            return false;
        }
    }

    private static object? GetUiDispatcher()
    {
        try
        {
            var appType = Type.GetType("System.Windows.Application, PresentationFramework");
            if (appType != null)
            {
                var currentProp = appType.GetProperty("Current", BindingFlags.Public | BindingFlags.Static);
                var appInstance = currentProp?.GetValue(null);
                if (appInstance != null)
                {
                    var dispProp = appType.GetProperty("Dispatcher", BindingFlags.Public | BindingFlags.Instance);
                    return dispProp?.GetValue(appInstance);
                }
            }
        }
        catch { }
        return null;
    }

    private async Task<string?> ExecuteScriptOnUiAsync(string script)
    {
        if (_customScriptExecutor != null)
        {
            try
            {
                return await _customScriptExecutor(script);
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[EMBED-PLAYER: ERROR] Custom script executor failed: {ex.Message}");
                return null;
            }
        }

        if (_coreWebView2 == null) return null;

        bool isRealWebView = _coreWebView2?.GetType().Name == "CoreWebView2";
        if (isRealWebView)
        {
            dynamic? dispatcher = GetUiDispatcher();
            if (dispatcher != null)
            {
                bool checkAccess = true;
                try { checkAccess = (bool)dispatcher.CheckAccess(); } catch { }

                if (!checkAccess)
                {
                    try
                    {
                        var op = dispatcher.InvokeAsync(new Func<Task<string?>>(async () =>
                        {
                            try
                            {
                                return (string?)await _coreWebView2?.ExecuteScriptAsync(script);
                            }
                            catch
                            {
                                return null;
                            }
                        }));
                        return await op.Task.Unwrap();
                    }
                    catch (Exception ex)
                    {
                        FabricGameLaunchService.LogLauncherEvent($"[EMBED-PLAYER: ERROR] ExecuteScript failed on UI thread: {ex.Message}");
                        return null;
                    }
                }
            }
        }

        try
        {
            var method = _coreWebView2?.GetType().GetMethod("ExecuteScriptAsync", new[] { typeof(string) });
            if (method != null)
            {
                var taskObj = method.Invoke(_coreWebView2, new object[] { script }) as Task;
                if (taskObj != null)
                {
                    await taskObj;
                    var resultProp = taskObj.GetType().GetProperty("Result");
                    return resultProp?.GetValue(taskObj)?.ToString() ?? "null";
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[EMBED-PLAYER: ERROR] ExecuteScript failed: {ex.Message}");
            return null;
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopwatch.Stop();
        _coreWebView2 = null;
        _customScriptExecutor = null;
        _pendingState = null;
        _queue.Clear();
    }
}

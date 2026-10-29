using System;
using System.Diagnostics;
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

    private readonly object _lock = new();
    private readonly Stopwatch _stopwatch = new();
    private dynamic? _coreWebView2;

    private string _state = "stopped";
    private MusicBridgeTrack? _currentTrack;
    private double _currentTimeSeconds = 0.0;
    private long _lastUpdatedAtUnixMs = 0;
    private double _volume = 1.0;
    private long _durationMs = 0;
    private bool _isReady = false;
    private bool _disposed = false;

    public bool IsActive
    {
        get
        {
            lock (_lock) return _currentTrack != null || _isReady;
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

    public event Action<string, object?>? CommandDispatched;
    public event Action<MusicBridgeTrack?>? TrackChanged;
    public event Action<string>? StateChanged;

    public EmbedMusicPlayer()
    {
        _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
    }

    public void AttachWebView(object? coreWebView2)
    {
        lock (_lock)
        {
            _coreWebView2 = coreWebView2;
        }
        FabricGameLaunchService.LogLauncherEvent("[EMBED-PLAYER] Attached WebView2 to embed music player");
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

            string evt = root.TryGetProperty("event", out var evProp) ? evProp.GetString() ?? "" : "";
            string videoId = root.TryGetProperty("videoId", out var vidProp) ? vidProp.GetString() ?? "" : "";

            lock (_lock)
            {
                switch (evt)
                {
                    case "ready":
                        _isReady = true;
                        break;

                    case "timeUpdate":
                        if (root.TryGetProperty("currentTime", out var ctProp) && ctProp.TryGetDouble(out double ct))
                        {
                            _currentTimeSeconds = ct;
                        }
                        if (root.TryGetProperty("duration", out var durProp) && durProp.TryGetDouble(out double dur))
                        {
                            _durationMs = (long)(dur * 1000.0);
                        }

                        _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        if (_state == "playing")
                        {
                            _stopwatch.Restart();
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
                            _state = "playing";
                            _stopwatch.Restart();
                            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        }
                        else if (stateCode == 2) // YT.PlayerState.PAUSED
                        {
                            _state = "paused";
                            _stopwatch.Stop();
                            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        }
                        else if (stateCode == 0) // YT.PlayerState.ENDED
                        {
                            _state = "stopped";
                            _stopwatch.Reset();
                            _currentTimeSeconds = 0;
                            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                        }
                        break;

                    case "error":
                    case "unavailable":
                        _state = "stopped";
                        _stopwatch.Reset();
                        FabricGameLaunchService.LogLauncherEvent($"[EMBED-PLAYER: WARN] Embed error for video '{videoId}'");
                        break;
                }
            }

            StateChanged?.Invoke(_state);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[EMBED-PLAYER: ERROR] Failed to parse embed message: {ex.Message}");
        }
    }

    public long GetExactPositionMs(out long updatedAtUnixMs)
    {
        lock (_lock)
        {
            if (_state == "playing")
            {
                long elapsed = _stopwatch.ElapsedMilliseconds;
                long pos = (long)(_currentTimeSeconds * 1000.0) + elapsed;
                if (_durationMs > 0 && pos > _durationMs) pos = _durationMs;
                updatedAtUnixMs = _lastUpdatedAtUnixMs + elapsed;
                return Math.Max(0, pos);
            }
            else
            {
                updatedAtUnixMs = _lastUpdatedAtUnixMs > 0 ? _lastUpdatedAtUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
                return Math.Max(0, (long)(_currentTimeSeconds * 1000.0));
            }
        }
    }

    public async Task<bool> LoadVideoAsync(string videoId, string? title = null, string? artist = null, string? coverUrl = null, long durationMs = 0)
    {
        if (string.IsNullOrWhiteSpace(videoId)) return false;

        lock (_lock)
        {
            _currentTimeSeconds = 0.0;
            _durationMs = durationMs;
            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            _stopwatch.Reset();
            _state = "playing";
            _stopwatch.Start();

            string effectiveTitle = !string.IsNullOrWhiteSpace(title) ? title : videoId;
            string effectiveArtist = !string.IsNullOrWhiteSpace(artist) ? artist : "Aura";
            CurrentTrack = new MusicBridgeTrack(videoId, effectiveArtist, effectiveTitle, durationMs, coverUrl);
        }

        var payload = new { videoId, startSeconds = 0 };
        return await DispatchCommandAsync("load", payload);
    }

    public async Task<bool> PlayAsync()
    {
        lock (_lock)
        {
            if (_currentTrack == null) return false;
            _state = "playing";
            _stopwatch.Restart();
            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        return await DispatchCommandAsync("play", new { });
    }

    public async Task<bool> PauseAsync()
    {
        lock (_lock)
        {
            _state = "paused";
            _stopwatch.Stop();
            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        }
        return await DispatchCommandAsync("pause", new { });
    }

    public Task<bool> NextAsync()
    {
        // Future playlist queue advancement in embed player
        return Task.FromResult(true);
    }

    public Task<bool> PreviousAsync()
    {
        return SeekAsync(0);
    }

    public async Task<bool> SeekAsync(long positionMs)
    {
        double seconds = Math.Max(0, positionMs / 1000.0);
        lock (_lock)
        {
            _currentTimeSeconds = seconds;
            _lastUpdatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            if (_state == "playing")
            {
                _stopwatch.Restart();
            }
        }
        return await DispatchCommandAsync("seek", new { seconds });
    }

    public async Task<bool> SetVolumeAsync(double volume)
    {
        double clamped = Math.Clamp(volume, 0.0, 1.0);
        lock (_lock)
        {
            _volume = clamped;
        }
        return await DispatchCommandAsync("volume", new { value = clamped * 100.0 });
    }

    private async Task<bool> DispatchCommandAsync(string op, object payload)
    {
        CommandDispatched?.Invoke(op, payload);

        if (_coreWebView2 != null)
        {
            try
            {
                var msg = new
                {
                    source = "aura-yt-host",
                    channel = "aura-music",
                    op,
                    data = payload
                };
                string json = JsonSerializer.Serialize(msg, _jsonOptions);
                string script = $"window.postMessage({json}, '*');";
                await _coreWebView2.ExecuteScriptAsync(script);
                return true;
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[EMBED-PLAYER: ERROR] Script dispatch error: {ex.Message}");
                return false;
            }
        }

        return true;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _stopwatch.Stop();
        _coreWebView2 = null;
    }
}

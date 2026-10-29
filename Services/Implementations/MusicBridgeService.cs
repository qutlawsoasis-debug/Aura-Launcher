using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public static class MusicBridgeExtrapolation
{
    public static long ExtrapolatePosition(long positionMs, long updatedAtUnixMs, string state, long currentUnixMs)
    {
        if (!string.Equals(state, "playing", StringComparison.OrdinalIgnoreCase))
        {
            return Math.Max(0, positionMs);
        }

        long delta = currentUnixMs - updatedAtUnixMs;
        if (delta <= 0) return Math.Max(0, positionMs);

        return Math.Max(0, positionMs + delta);
    }
}

public class MusicBridgeService : IMusicBridgeService
{
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = false
    };

    private readonly IAudioService _audioService;
    private readonly ILobbyApiClient? _lobbyApiClient;
    private readonly IEmbedMusicPlayer _embedPlayer;
    private readonly HttpClient _httpClient;
    private readonly object _lock = new();
    private readonly HashSet<string> _activeInstanceDirs = new(StringComparer.OrdinalIgnoreCase);

    private HttpListener? _httpListener;
    private CancellationTokenSource? _listenerCts;
    private Task? _listenerTask;
    private bool _disposed;
    private int _port;
    private string _token;

    public int Port
    {
        get
        {
            lock (_lock) return _port;
        }
    }

    public string Token
    {
        get
        {
            lock (_lock) return _token;
        }
    }

    public bool IsRunning
    {
        get
        {
            lock (_lock) return _httpListener != null && _httpListener.IsListening;
        }
    }

    public IEmbedMusicPlayer EmbedPlayer => _embedPlayer;

    public MusicBridgeService(
        IAudioService audioService,
        ILobbyApiClient? lobbyApiClient = null,
        IEmbedMusicPlayer? embedPlayer = null,
        HttpClient? httpClient = null,
        int port = 0,
        string? token = null)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _lobbyApiClient = lobbyApiClient;
        _embedPlayer = embedPlayer ?? new EmbedMusicPlayer();
        _httpClient = httpClient ?? new HttpClient();

        _port = port;
        _token = token ?? GenerateSecureToken();
    }

    private static string GenerateSecureToken()
    {
        byte[] bytes = new byte[32];
        RandomNumberGenerator.Fill(bytes);
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static int FindFreeTcpPort()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        int freePort = ((IPEndPoint)listener.LocalEndpoint).Port;
        listener.Stop();
        return freePort;
    }

    public void Start()
    {
        lock (_lock)
        {
            if (_disposed) throw new ObjectDisposedException(nameof(MusicBridgeService));
            if (_httpListener != null && _httpListener.IsListening) return;

            if (string.IsNullOrWhiteSpace(_token))
            {
                _token = GenerateSecureToken();
            }

            int attempts = 0;
            while (attempts < 10)
            {
                attempts++;
                if (_port <= 0)
                {
                    _port = FindFreeTcpPort();
                }

                try
                {
                    var listener = new HttpListener();
                    // Loopback 127.0.0.1 ONLY (never bind to external 0.0.0.0 or any foreign interface)
                    listener.Prefixes.Add($"http://127.0.0.1:{_port}/");
                    listener.Start();

                    _httpListener = listener;
                    _listenerCts = new CancellationTokenSource();
                    _listenerTask = Task.Run(() => ListenLoopAsync(_listenerCts.Token));

                    FabricGameLaunchService.LogLauncherEvent($"[MUSIC-BRIDGE] Server started on 127.0.0.1:{_port}");
                    return;
                }
                catch (Exception ex)
                {
                    _httpListener?.Close();
                    _httpListener = null;
                    FabricGameLaunchService.LogLauncherEvent($"[MUSIC-BRIDGE: WARN] Port {_port} busy, retrying: {ex.Message}");
                    _port = 0; // Choose new port on next attempt
                }
            }

            throw new InvalidOperationException("Failed to bind MusicBridge HTTP listener to 127.0.0.1 after 10 attempts.");
        }
    }

    public void Stop()
    {
        lock (_lock)
        {
            if (_httpListener == null && _activeInstanceDirs.Count == 0) return;

            try
            {
                _listenerCts?.Cancel();
                _httpListener?.Stop();
                _httpListener?.Close();
            }
            catch { }
            finally
            {
                _httpListener = null;
                _listenerCts?.Dispose();
                _listenerCts = null;
                _listenerTask = null;
            }

            foreach (var instanceDir in _activeInstanceDirs.ToList())
            {
                try
                {
                    string filePath = Path.Combine(instanceDir, "config", "aura-music-bridge.json");
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                catch { }
            }
            _activeInstanceDirs.Clear();

            FabricGameLaunchService.LogLauncherEvent("[MUSIC-BRIDGE] Server stopped");
        }
    }

    public void WriteConfigFile(string instanceDir)
    {
        if (string.IsNullOrWhiteSpace(instanceDir)) return;

        try
        {
            if (!IsRunning)
            {
                Start();
            }

            string configDir = Path.Combine(instanceDir, "config");
            Directory.CreateDirectory(configDir);

            string filePath = Path.Combine(configDir, "aura-music-bridge.json");
            var config = new MusicBridgeConfig(Port, Token);
            string json = JsonSerializer.Serialize(config, new JsonSerializerOptions { WriteIndented = true });

            File.WriteAllText(filePath, json, new UTF8Encoding(false));

            lock (_lock)
            {
                _activeInstanceDirs.Add(instanceDir);
            }

            FabricGameLaunchService.LogLauncherEvent($"[MUSIC-BRIDGE] Written bridge config to {filePath}");
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[MUSIC-BRIDGE: ERROR] Failed to write bridge config: {ex.Message}");
        }
    }

    public void DeleteConfigFile(string instanceDir)
    {
        if (string.IsNullOrWhiteSpace(instanceDir)) return;

        try
        {
            string filePath = Path.Combine(instanceDir, "config", "aura-music-bridge.json");
            if (File.Exists(filePath))
            {
                File.Delete(filePath);
                FabricGameLaunchService.LogLauncherEvent($"[MUSIC-BRIDGE] Deleted bridge config {filePath}");
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[MUSIC-BRIDGE: ERROR] Failed to delete bridge config: {ex.Message}");
        }
        finally
        {
            lock (_lock)
            {
                _activeInstanceDirs.Remove(instanceDir);
            }
        }
    }

    public MusicBridgeNowResponse GetNowPlaying()
    {
        // 1. Primary source: Embed player
        if (_embedPlayer.CurrentTrack != null)
        {
            long positionMs = _embedPlayer.GetExactPositionMs(out long updatedAtUnixMs);
            return new MusicBridgeNowResponse(
                State: _embedPlayer.State,
                Track: _embedPlayer.CurrentTrack,
                PositionMs: positionMs,
                UpdatedAtUnixMs: updatedAtUnixMs,
                Volume: _embedPlayer.Volume
            );
        }

        // 2. Secondary source: Native AudioService
        TimeSpan position;
        TimeSpan duration = _audioService.Duration;
        bool isPlaying = _audioService.IsPlaying;
        int volumePercent = _audioService.VolumePercent;
        AudioTrack? currentTrack = _audioService.CurrentTrack;

        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            try
            {
                position = Application.Current.Dispatcher.Invoke(() => _audioService.Position);
            }
            catch
            {
                position = _audioService.Position;
            }
        }
        else
        {
            position = _audioService.Position;
        }

        long nativeUpdatedAt = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long nativePositionMs = Math.Max(0, (long)position.TotalMilliseconds);
        double volume = Math.Clamp(volumePercent / 100.0, 0.0, 1.0);

        string state;
        if (isPlaying)
        {
            state = "playing";
        }
        else if (currentTrack != null)
        {
            state = "paused";
        }
        else
        {
            state = "stopped";
        }

        MusicBridgeTrack? track = null;
        if (currentTrack != null)
        {
            string trackId = !string.IsNullOrWhiteSpace(currentTrack.Path)
                ? currentTrack.Path
                : $"{currentTrack.Artist} - {currentTrack.Title}";

            track = new MusicBridgeTrack(
                Id: trackId,
                Artist: currentTrack.Artist,
                Title: currentTrack.Title,
                DurationMs: Math.Max(0, (long)duration.TotalMilliseconds),
                CoverUrl: currentTrack.CoverPath
            );
        }

        return new MusicBridgeNowResponse(
            State: state,
            Track: track,
            PositionMs: nativePositionMs,
            UpdatedAtUnixMs: nativeUpdatedAt,
            Volume: volume
        );
    }

    private async Task ListenLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                if (_httpListener == null || !_httpListener.IsListening) break;
                context = await _httpListener.GetContextAsync();
            }
            catch (HttpListenerException) when (ct.IsCancellationRequested || !IsRunning)
            {
                break;
            }
            catch (ObjectDisposedException)
            {
                break;
            }
            catch (Exception ex)
            {
                if (ct.IsCancellationRequested || !IsRunning) break;
                FabricGameLaunchService.LogLauncherEvent($"[MUSIC-BRIDGE: ERROR] Accept loop error: {ex.Message}");
                continue;
            }

            _ = Task.Run(() => HandleRequestSafeAsync(context));
        }
    }

    private async Task HandleRequestSafeAsync(HttpListenerContext context)
    {
        try
        {
            await ProcessRequestAsync(context);
        }
        catch (Exception ex)
        {
            try
            {
                await SendJsonAsync(context.Response, 500, new { error = "internal_error", message = ex.Message });
            }
            catch { }
        }
    }

    public async Task ProcessRequestAsync(HttpListenerContext context)
    {
        var request = context.Request;
        var response = context.Response;

        // Reject browser requests containing Origin header (bridge is strictly for in-game HTTP client)
        if (!string.IsNullOrWhiteSpace(request.Headers["Origin"]))
        {
            await SendJsonAsync(response, 403, new { error = "forbidden", message = "Origin header not allowed" });
            return;
        }

        // Authorization validation: Bearer <token>
        string? authHeader = request.Headers["Authorization"];
        if (string.IsNullOrWhiteSpace(authHeader) || !authHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
        {
            await SendJsonAsync(response, 401, new { error = "unauthorized", message = "Missing or malformed Authorization header" });
            return;
        }

        string providedToken = authHeader.Substring("Bearer ".Length).Trim();
        byte[] expectedBytes = Encoding.UTF8.GetBytes(Token);
        byte[] providedBytes = Encoding.UTF8.GetBytes(providedToken);

        if (!CryptographicOperations.FixedTimeEquals(expectedBytes, providedBytes))
        {
            await SendJsonAsync(response, 401, new { error = "unauthorized", message = "Invalid token" });
            return;
        }

        string path = request.Url?.AbsolutePath?.TrimEnd('/') ?? "";
        if (string.IsNullOrEmpty(path)) path = "/";

        switch (request.HttpMethod.ToUpperInvariant(), path.ToLowerInvariant())
        {
            case ("GET", "/now"):
                await HandleGetNowAsync(response);
                break;

            case ("POST", "/play"):
                await HandlePostPlayAsync(response);
                break;

            case ("POST", "/pause"):
                await HandlePostPauseAsync(response);
                break;

            case ("POST", "/next"):
                await HandlePostNextAsync(response);
                break;

            case ("POST", "/prev"):
                await HandlePostPrevAsync(response);
                break;

            case ("POST", "/seek"):
                await HandlePostSeekAsync(request, response);
                break;

            case ("POST", "/volume"):
                await HandlePostVolumeAsync(request, response);
                break;

            case ("POST", "/queue"):
                await HandlePostQueueAsync(request, response);
                break;

            case ("GET", "/search"):
                await HandleGetSearchAsync(request, response);
                break;

            default:
                await SendJsonAsync(response, 404, new { error = "not_found", message = $"Route '{path}' not found" });
                break;
        }
    }

    private Task HandleGetNowAsync(HttpListenerResponse response)
    {
        var now = GetNowPlaying();
        return SendJsonAsync(response, 200, now);
    }

    private async Task HandlePostPlayAsync(HttpListenerResponse response)
    {
        if (_embedPlayer.HasTransport && _embedPlayer.CurrentTrack != null)
        {
            bool ok = await _embedPlayer.PlayAsync();
            if (!ok)
            {
                await SendJsonAsync(response, 409, new { ok = false, error = "player_failed", message = "Embed player failed to play" });
                return;
            }
        }
        else
        {
            RunOnUiOrDirect(() => _audioService.Play());
        }
        await SendJsonAsync(response, 200, new { ok = true, state = "playing" });
    }

    private async Task HandlePostPauseAsync(HttpListenerResponse response)
    {
        if (_embedPlayer.HasTransport && _embedPlayer.CurrentTrack != null)
        {
            bool ok = await _embedPlayer.PauseAsync();
            if (!ok)
            {
                await SendJsonAsync(response, 409, new { ok = false, error = "player_failed", message = "Embed player failed to pause" });
                return;
            }
        }
        else
        {
            RunOnUiOrDirect(() => _audioService.Pause());
        }
        await SendJsonAsync(response, 200, new { ok = true, state = "paused" });
    }

    private async Task HandlePostNextAsync(HttpListenerResponse response)
    {
        if (_embedPlayer.HasTransport && _embedPlayer.CurrentTrack != null)
        {
            bool ok = await _embedPlayer.NextAsync();
            if (!ok)
            {
                await SendJsonAsync(response, 409, new { ok = false, error = "queue_end", message = "No next track in queue" });
                return;
            }
        }
        else
        {
            RunOnUiOrDirect(() => _audioService.Next());
        }
        await SendJsonAsync(response, 200, new { ok = true });
    }

    private async Task HandlePostPrevAsync(HttpListenerResponse response)
    {
        if (_embedPlayer.HasTransport && _embedPlayer.CurrentTrack != null)
        {
            bool ok = await _embedPlayer.PreviousAsync();
            if (!ok)
            {
                await SendJsonAsync(response, 409, new { ok = false, error = "queue_start", message = "No previous track in queue" });
                return;
            }
        }
        else
        {
            RunOnUiOrDirect(() => _audioService.Previous());
        }
        await SendJsonAsync(response, 200, new { ok = true });
    }

    private async Task HandlePostSeekAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        string body = await ReadRequestBodyAsync(request);
        long? ms = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var req = JsonSerializer.Deserialize<MusicBridgeSeekRequest>(body, _jsonOptions);
                if (req?.Ms.HasValue == true) ms = req.Ms.Value;
                else if (req?.PositionMs.HasValue == true) ms = req.PositionMs.Value;
                else if (req?.Seconds.HasValue == true) ms = (long)(req.Seconds.Value * 1000.0);
            }
            catch { }
        }

        long targetMs = Math.Max(0, ms ?? 0);
        if (_embedPlayer.HasTransport && _embedPlayer.CurrentTrack != null)
        {
            bool ok = await _embedPlayer.SeekAsync(targetMs);
            if (!ok)
            {
                await SendJsonAsync(response, 409, new { ok = false, error = "player_failed", message = "Embed player seek failed" });
                return;
            }
        }
        else
        {
            RunOnUiOrDirect(() => _audioService.Seek(TimeSpan.FromMilliseconds(targetMs)));
        }
        await SendJsonAsync(response, 200, new { ok = true, positionMs = targetMs });
    }

    private async Task HandlePostVolumeAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        string body = await ReadRequestBodyAsync(request);
        double volume = 1.0;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var req = JsonSerializer.Deserialize<MusicBridgeVolumeRequest>(body, _jsonOptions);
                if (req?.Volume.HasValue == true) volume = req.Volume.Value;
                else if (req?.Value.HasValue == true) volume = req.Value.Value;
            }
            catch { }
        }

        volume = Math.Clamp(volume, 0.0, 1.0);
        if (_embedPlayer.HasTransport && _embedPlayer.CurrentTrack != null)
        {
            bool ok = await _embedPlayer.SetVolumeAsync(volume);
            if (!ok)
            {
                await SendJsonAsync(response, 409, new { ok = false, error = "player_failed", message = "Embed player set volume failed" });
                return;
            }
        }
        else
        {
            int percent = (int)Math.Round(volume * 100.0);
            RunOnUiOrDirect(() =>
            {
                _audioService.VolumePercent = percent;
                if (_audioService.IsMuted && percent > 0)
                {
                    _audioService.IsMuted = false;
                }
            });
        }

        await SendJsonAsync(response, 200, new { ok = true, volume });
    }

    private async Task HandlePostQueueAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        string body = await ReadRequestBodyAsync(request);
        string? trackId = null;
        string? title = null;
        string? artist = null;
        string? coverUrl = null;
        long? durationMs = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var req = JsonSerializer.Deserialize<MusicBridgeQueueRequest>(body, _jsonOptions);
                trackId = req?.TrackId ?? req?.Id;
                title = req?.Title;
                artist = req?.Artist;
                coverUrl = req?.CoverUrl;
                durationMs = req?.DurationMs;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(trackId))
        {
            await SendJsonAsync(response, 400, new { ok = false, error = "missing_track_id", message = "trackId is required" });
            return;
        }

        string? resolvedVideoId = null;

        // 1. Is trackId already a valid 11-char YouTube ID?
        if (Regex.IsMatch(trackId, @"^[a-zA-Z0-9_-]{11}$"))
        {
            resolvedVideoId = trackId;
        }
        // 2. Or is it a YouTube URL?
        else if (TryExtractYouTubeVideoId(trackId, out string? extractedId))
        {
            resolvedVideoId = extractedId;
        }
        // 3. Or resolve via catalog lobby-api (/api/music/resolve)
        else if (_lobbyApiClient != null)
        {
            string artistToResolve = !string.IsNullOrWhiteSpace(artist) ? artist : "";
            string titleToResolve = !string.IsNullOrWhiteSpace(title) ? title : trackId;
            var res = await _lobbyApiClient.ResolveMusicAsync(artistToResolve, titleToResolve);
            if (res != null && !string.IsNullOrWhiteSpace(res.VideoId) && Regex.IsMatch(res.VideoId, @"^[a-zA-Z0-9_-]{11}$"))
            {
                resolvedVideoId = res.VideoId;
            }
        }

        // 4. If not a YouTube/catalog ID, check if it's an existing local file
        if (resolvedVideoId == null && File.Exists(trackId))
        {
            string effTitle = !string.IsNullOrWhiteSpace(title) ? title : Path.GetFileNameWithoutExtension(trackId);
            string effArtist = !string.IsNullOrWhiteSpace(artist) ? artist : "Aura";
            var localTrack = new AudioTrack(effTitle, effArtist, trackId, coverUrl);
            RunOnUiOrDirect(() =>
            {
                _audioService.Enqueue(localTrack);
                if (!_audioService.IsPlaying)
                {
                    _audioService.PlayTrack(effTitle);
                }
            });
            await SendJsonAsync(response, 200, new { ok = true, trackId });
            return;
        }

        // 5. If resolution failed: do not report success! Return 422 Unprocessable Entity
        if (resolvedVideoId == null)
        {
            await SendJsonAsync(response, 422, new
            {
                ok = false,
                error = "resolve_failed",
                message = $"Track '{trackId}' could not be resolved to a playable stream"
            });
            return;
        }

        // 6. Queue and load into embed player
        string finalTitle = !string.IsNullOrWhiteSpace(title) ? title : resolvedVideoId;
        string finalArtist = !string.IsNullOrWhiteSpace(artist) ? artist : "Aura";
        long finalDuration = durationMs ?? 0;

        bool loaded = await _embedPlayer.LoadVideoAsync(resolvedVideoId, finalTitle, finalArtist, coverUrl, finalDuration);
        if (!loaded)
        {
            await SendJsonAsync(response, 503, new { ok = false, error = "player_failed", message = "Embed player failed to load track or transport unavailable" });
            return;
        }

        await SendJsonAsync(response, 200, new { ok = true, trackId = resolvedVideoId });
    }

    private static bool TryExtractYouTubeVideoId(string urlOrId, out string? videoId)
    {
        videoId = null;
        if (string.IsNullOrWhiteSpace(urlOrId)) return false;

        if (urlOrId.StartsWith("yt:", StringComparison.OrdinalIgnoreCase))
        {
            string sub = urlOrId.Substring(3).Trim();
            if (Regex.IsMatch(sub, @"^[a-zA-Z0-9_-]{11}$"))
            {
                videoId = sub;
                return true;
            }
        }

        var match = Regex.Match(urlOrId, @"(?:youtu\.be\/|youtube\.com\/(?:embed\/|v\/|watch\?v=|watch\?.+&v=))([\w-]{11})");
        if (match.Success)
        {
            videoId = match.Groups[1].Value;
            return true;
        }

        return false;
    }

    private async Task HandleGetSearchAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        string query = request.QueryString["q"]?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            await SendJsonAsync(response, 400, new { error = "missing_query", message = "Query parameter 'q' is required" });
            return;
        }

        var normalizedTracks = new List<MusicBridgeTrack>();

        if (_lobbyApiClient != null)
        {
            try
            {
                string rawJson = await _lobbyApiClient.SearchMusicRawAsync(query);
                if (!string.IsNullOrWhiteSpace(rawJson) && rawJson.Trim() != "[]")
                {
                    using var doc = JsonDocument.Parse(rawJson);
                    var root = doc.RootElement;

                    if (root.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var elem in root.EnumerateArray())
                        {
                            var t = ExtractTrack(elem);
                            if (t != null) normalizedTracks.Add(t);
                        }
                    }
                    else if (root.ValueKind == JsonValueKind.Object)
                    {
                        JsonElement arrayElem = default;
                        if (root.TryGetProperty("items", out var items) && items.ValueKind == JsonValueKind.Array)
                            arrayElem = items;
                        else if (root.TryGetProperty("tracks", out var tracks) && tracks.ValueKind == JsonValueKind.Array)
                            arrayElem = tracks;
                        else if (root.TryGetProperty("results", out var results) && results.ValueKind == JsonValueKind.Array)
                            arrayElem = results;

                        if (arrayElem.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var elem in arrayElem.EnumerateArray())
                            {
                                var t = ExtractTrack(elem);
                                if (t != null) normalizedTracks.Add(t);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[MUSIC-BRIDGE: WARN] Search catalog parse failed: {ex.Message}");
            }
        }

        // Fallback to local playlist if catalog returned no results
        if (normalizedTracks.Count == 0)
        {
            try
            {
                var localMatches = _audioService.Playlist
                    .Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Select(t => new MusicBridgeTrack(
                        Id: t.Path,
                        Artist: t.Artist,
                        Title: t.Title,
                        DurationMs: Math.Max(0, (long)_audioService.Duration.TotalMilliseconds),
                        CoverUrl: t.CoverPath
                    ));
                normalizedTracks.AddRange(localMatches);
            }
            catch { }
        }

        // Return normalized array adhering to MusicJsonParser contract (id, artist, title, durationMs, coverUrl)
        await SendJsonAsync(response, 200, normalizedTracks);
    }

    private static MusicBridgeTrack? ExtractTrack(JsonElement elem)
    {
        if (elem.ValueKind != JsonValueKind.Object) return null;

        string id = elem.TryGetProperty("id", out var pId) ? pId.GetString() ?? "" : "";
        string title = elem.TryGetProperty("title", out var pT) ? pT.GetString() ?? "" : "";
        string artist = elem.TryGetProperty("artist", out var pA) ? pA.GetString() ?? "" : "";

        string? coverUrl = null;
        if (elem.TryGetProperty("coverUrl", out var pC) && pC.ValueKind == JsonValueKind.String)
            coverUrl = pC.GetString();
        else if (elem.TryGetProperty("artwork", out var pArt) && pArt.ValueKind == JsonValueKind.String)
            coverUrl = pArt.GetString();

        long durationMs = 0;
        if (elem.TryGetProperty("durationMs", out var pDm) && pDm.TryGetInt64(out long dm))
            durationMs = dm;
        else if (elem.TryGetProperty("duration", out var pD) && pD.TryGetInt64(out long d))
            durationMs = d;
        else if (elem.TryGetProperty("durationSec", out var pDs) && pDs.TryGetDouble(out double ds))
            durationMs = (long)(ds * 1000.0);

        if (string.IsNullOrWhiteSpace(id) && string.IsNullOrWhiteSpace(title)) return null;

        return new MusicBridgeTrack(id, artist, title, durationMs, coverUrl);
    }

    private static void RunOnUiOrDirect(Action action)
    {
        if (Application.Current?.Dispatcher != null && !Application.Current.Dispatcher.CheckAccess())
        {
            Application.Current.Dispatcher.Invoke(action);
        }
        else
        {
            action();
        }
    }

    private static async Task<string> ReadRequestBodyAsync(HttpListenerRequest request)
    {
        if (!request.HasEntityBody) return string.Empty;
        using var reader = new StreamReader(request.InputStream, request.ContentEncoding ?? Encoding.UTF8);
        return await reader.ReadToEndAsync();
    }

    private static async Task SendJsonAsync(HttpListenerResponse response, int statusCode, object data)
    {
        response.StatusCode = statusCode;
        response.ContentType = "application/json; charset=utf-8";
        string json = JsonSerializer.Serialize(data, _jsonOptions);
        byte[] buffer = Encoding.UTF8.GetBytes(json);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.OutputStream.Close();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        Stop();
        _embedPlayer.Dispose();

        // Clean up any remaining written config files
        lock (_lock)
        {
            foreach (var instanceDir in _activeInstanceDirs.ToList())
            {
                try
                {
                    string filePath = Path.Combine(instanceDir, "config", "aura-music-bridge.json");
                    if (File.Exists(filePath))
                    {
                        File.Delete(filePath);
                    }
                }
                catch { }
            }
            _activeInstanceDirs.Clear();
        }

        _httpClient.Dispose();
    }
}

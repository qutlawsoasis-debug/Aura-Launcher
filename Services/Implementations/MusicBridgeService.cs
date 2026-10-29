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

    public MusicBridgeService(
        IAudioService audioService,
        ILobbyApiClient? lobbyApiClient = null,
        HttpClient? httpClient = null,
        int port = 0,
        string? token = null)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _lobbyApiClient = lobbyApiClient;
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
            if (_httpListener == null) return;

            try
            {
                _listenerCts?.Cancel();
                _httpListener.Stop();
                _httpListener.Close();
            }
            catch { }
            finally
            {
                _httpListener = null;
                _listenerCts?.Dispose();
                _listenerCts = null;
                _listenerTask = null;
            }

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

        long updatedAtUnixMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
        long positionMs = Math.Max(0, (long)position.TotalMilliseconds);
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
            PositionMs: positionMs,
            UpdatedAtUnixMs: updatedAtUnixMs,
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

        // CORS headers for local loopback in-game web views or browser controls
        response.Headers["Access-Control-Allow-Origin"] = "*";
        response.Headers["Access-Control-Allow-Methods"] = "GET, POST, OPTIONS";
        response.Headers["Access-Control-Allow-Headers"] = "Content-Type, Authorization";

        if (string.Equals(request.HttpMethod, "OPTIONS", StringComparison.OrdinalIgnoreCase))
        {
            response.StatusCode = 204;
            response.Close();
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

    private Task HandlePostPlayAsync(HttpListenerResponse response)
    {
        RunOnUiOrDirect(() => _audioService.Play());
        return SendJsonAsync(response, 200, new { ok = true, state = "playing" });
    }

    private Task HandlePostPauseAsync(HttpListenerResponse response)
    {
        RunOnUiOrDirect(() => _audioService.Pause());
        return SendJsonAsync(response, 200, new { ok = true, state = "paused" });
    }

    private Task HandlePostNextAsync(HttpListenerResponse response)
    {
        RunOnUiOrDirect(() => _audioService.Next());
        return SendJsonAsync(response, 200, new { ok = true });
    }

    private Task HandlePostPrevAsync(HttpListenerResponse response)
    {
        RunOnUiOrDirect(() => _audioService.Previous());
        return SendJsonAsync(response, 200, new { ok = true });
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
        RunOnUiOrDirect(() => _audioService.Seek(TimeSpan.FromMilliseconds(targetMs)));
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
        int percent = (int)Math.Round(volume * 100.0);

        RunOnUiOrDirect(() =>
        {
            _audioService.VolumePercent = percent;
            if (_audioService.IsMuted && percent > 0)
            {
                _audioService.IsMuted = false;
            }
        });

        await SendJsonAsync(response, 200, new { ok = true, volume });
    }

    private async Task HandlePostQueueAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        string body = await ReadRequestBodyAsync(request);
        string? trackId = null;
        string? title = null;
        string? artist = null;
        string? coverUrl = null;

        if (!string.IsNullOrWhiteSpace(body))
        {
            try
            {
                var req = JsonSerializer.Deserialize<MusicBridgeQueueRequest>(body, _jsonOptions);
                trackId = req?.TrackId ?? req?.Id;
                title = req?.Title;
                artist = req?.Artist;
                coverUrl = req?.CoverUrl;
            }
            catch { }
        }

        if (string.IsNullOrWhiteSpace(trackId))
        {
            await SendJsonAsync(response, 400, new { error = "missing_track_id", message = "trackId is required" });
            return;
        }

        RunOnUiOrDirect(() =>
        {
            // 1. Try finding in existing playlist
            bool played = _audioService.PlayTrack(trackId);
            if (!played)
            {
                // 2. Add as new track
                string effectiveTitle = !string.IsNullOrWhiteSpace(title)
                    ? title
                    : (File.Exists(trackId) ? Path.GetFileNameWithoutExtension(trackId) : trackId);
                string effectiveArtist = !string.IsNullOrWhiteSpace(artist) ? artist : "Aura";

                var newTrack = new AudioTrack(effectiveTitle, effectiveArtist, trackId, coverUrl);
                _audioService.Enqueue(newTrack);

                if (!_audioService.IsPlaying)
                {
                    _audioService.PlayTrack(effectiveTitle);
                }
            }
        });

        await SendJsonAsync(response, 200, new { ok = true, trackId });
    }

    private async Task HandleGetSearchAsync(HttpListenerRequest request, HttpListenerResponse response)
    {
        string query = request.QueryString["q"]?.Trim() ?? "";
        if (string.IsNullOrWhiteSpace(query))
        {
            await SendJsonAsync(response, 400, new { error = "missing_query", message = "Query parameter 'q' is required" });
            return;
        }

        string rawResults = "[]";
        if (_lobbyApiClient != null)
        {
            try
            {
                rawResults = await _lobbyApiClient.SearchMusicRawAsync(query);
            }
            catch
            {
                rawResults = "[]";
            }
        }

        // If remote search returned empty, fall back to matching local tracks
        if (string.IsNullOrWhiteSpace(rawResults) || rawResults.Trim() == "[]")
        {
            try
            {
                var localMatches = _audioService.Playlist
                    .Where(t => t.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                                t.Artist.Contains(query, StringComparison.OrdinalIgnoreCase))
                    .Select(t => new
                    {
                        id = t.Path,
                        kind = "track",
                        title = t.Title,
                        artist = t.Artist,
                        artwork = t.CoverPath ?? "",
                        durationSec = (int)_audioService.Duration.TotalSeconds
                    })
                    .ToList();

                if (localMatches.Count > 0)
                {
                    rawResults = JsonSerializer.Serialize(localMatches, _jsonOptions);
                }
            }
            catch { }
        }

        response.StatusCode = 200;
        response.ContentType = "application/json; charset=utf-8";
        byte[] buffer = Encoding.UTF8.GetBytes(rawResults);
        response.ContentLength64 = buffer.Length;
        await response.OutputStream.WriteAsync(buffer, 0, buffer.Length);
        response.OutputStream.Close();
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

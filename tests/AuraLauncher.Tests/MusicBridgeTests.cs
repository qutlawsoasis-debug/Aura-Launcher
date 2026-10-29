using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using Xunit;

namespace AuraLauncher.Tests;

public class MusicBridgeTests : IDisposable
{
    private readonly TestAudioService _audioService;
    private readonly TestLobbyApiClient _lobbyApiClient;
    private readonly MusicBridgeService _bridge;
    private readonly HttpClient _client;

    public MusicBridgeTests()
    {
        _audioService = new TestAudioService();
        _lobbyApiClient = new TestLobbyApiClient();
        _bridge = new MusicBridgeService(_audioService, _lobbyApiClient);
        _bridge.Start();

        _client = new HttpClient
        {
            BaseAddress = new Uri($"http://127.0.0.1:{_bridge.Port}/")
        };
    }

    public void Dispose()
    {
        _client.Dispose();
        _bridge.Dispose();
    }

    private void SetAuthToken(string? token)
    {
        _client.DefaultRequestHeaders.Authorization = token != null
            ? new AuthenticationHeaderValue("Bearer", token)
            : null;
    }

    #region 1. Authorization Tests

    [Fact]
    public async Task Request_WithoutAuthorizationHeader_Returns401()
    {
        SetAuthToken(null);
        var response = await _client.GetAsync("/now");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("unauthorized", content, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task Request_WithInvalidBearerToken_Returns401()
    {
        SetAuthToken("invalid_wrong_token_12345");
        var response = await _client.GetAsync("/now");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithValidBearerToken_Returns200()
    {
        SetAuthToken(_bridge.Token);
        var response = await _client.GetAsync("/now");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Request_WithOriginHeader_Returns403()
    {
        SetAuthToken(_bridge.Token);
        var request = new HttpRequestMessage(HttpMethod.Get, "/now");
        request.Headers.Add("Origin", "http://localhost:3000");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
        var content = await response.Content.ReadAsStringAsync();
        Assert.Contains("Origin header not allowed", content);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    [Fact]
    public async Task Request_WithoutOrigin_WithValidToken_Returns200()
    {
        SetAuthToken(_bridge.Token);
        var request = new HttpRequestMessage(HttpMethod.Get, "/now");
        var response = await _client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.False(response.Headers.Contains("Access-Control-Allow-Origin"));
    }

    #endregion

    #region 2. Route Tests

    [Fact]
    public async Task GetNow_WhenPlaying_ReturnsCorrectStateAndTrack()
    {
        var track = new AudioTrack("Believer", "Imagine Dragons", "C:/music/believer.mp3", "C:/music/believer.jpg");
        _audioService.Tracks.Add(track);
        _audioService.CurrentTrackIndex = 0;
        _audioService.IsPlaying = true;
        _audioService.Position = TimeSpan.FromSeconds(45);
        _audioService.Duration = TimeSpan.FromSeconds(204);
        _audioService.VolumePercent = 85;

        SetAuthToken(_bridge.Token);
        var response = await _client.GetAsync("/now");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var now = JsonSerializer.Deserialize<MusicBridgeNowResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(now);
        Assert.Equal("playing", now.State);
        Assert.NotNull(now.Track);
        Assert.Equal("Believer", now.Track.Title);
        Assert.Equal("Imagine Dragons", now.Track.Artist);
        Assert.Equal(204000, now.Track.DurationMs);
        Assert.Equal(45000, now.PositionMs);
        Assert.True(now.UpdatedAtUnixMs > 0);
        Assert.Equal(0.85, now.Volume, 2);
    }

    [Fact]
    public async Task GetNow_WhenStopped_ReturnsStoppedState()
    {
        _audioService.Tracks.Clear();
        _audioService.IsPlaying = false;
        _audioService.Position = TimeSpan.Zero;

        SetAuthToken(_bridge.Token);
        var response = await _client.GetAsync("/now");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        var now = JsonSerializer.Deserialize<MusicBridgeNowResponse>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(now);
        Assert.Equal("stopped", now.State);
        Assert.Null(now.Track);
    }

    [Fact]
    public async Task PostPlay_CallsAudioServicePlay()
    {
        SetAuthToken(_bridge.Token);
        var response = await _client.PostAsync("/play", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_audioService.PlayCalled);
        Assert.True(_audioService.IsPlaying);
    }

    [Fact]
    public async Task PostPause_CallsAudioServicePause()
    {
        _audioService.IsPlaying = true;
        SetAuthToken(_bridge.Token);
        var response = await _client.PostAsync("/pause", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_audioService.PauseCalled);
        Assert.False(_audioService.IsPlaying);
    }

    [Fact]
    public async Task PostNext_CallsAudioServiceNext()
    {
        SetAuthToken(_bridge.Token);
        var response = await _client.PostAsync("/next", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_audioService.NextCalled);
    }

    [Fact]
    public async Task PostPrev_CallsAudioServicePrevious()
    {
        SetAuthToken(_bridge.Token);
        var response = await _client.PostAsync("/prev", null);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.True(_audioService.PrevCalled);
    }

    [Fact]
    public async Task PostSeek_WithMs_UpdatesAudioServicePosition()
    {
        SetAuthToken(_bridge.Token);
        var content = new StringContent("""{"ms": 35000}""", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/seek", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(TimeSpan.FromMilliseconds(35000), _audioService.Position);
    }

    [Fact]
    public async Task PostVolume_SetsNormalizedVolume()
    {
        SetAuthToken(_bridge.Token);
        var content = new StringContent("""{"volume": 0.65}""", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/volume", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(65, _audioService.VolumePercent);
    }

    [Fact]
    public async Task PostQueue_EnqueuesAndSelectsTrack()
    {
        SetAuthToken(_bridge.Token);
        var content = new StringContent("""{"trackId": "custom-track-99", "title": "Bones", "artist": "Imagine Dragons"}""", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/queue", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Contains(_audioService.Tracks, t => t.Title == "Bones" && t.Artist == "Imagine Dragons");
    }

    [Fact]
    public async Task PostQueue_MissingTrackId_Returns400()
    {
        SetAuthToken(_bridge.Token);
        var content = new StringContent("""{"title": "Bones"}""", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/queue", content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetSearch_WithQuery_ReturnsCatalogResults()
    {
        SetAuthToken(_bridge.Token);
        var response = await _client.GetAsync("/search?q=believer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("Believer", json);
        Assert.Contains("Imagine Dragons", json);
    }

    [Fact]
    public async Task GetSearch_WithoutQuery_Returns400()
    {
        SetAuthToken(_bridge.Token);
        var response = await _client.GetAsync("/search");

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        SetAuthToken(_bridge.Token);
        var response = await _client.GetAsync("/unknown_random_endpoint");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    #endregion

    #region 3. Serialization & Config Lifecycle Tests

    [Fact]
    public void MusicBridgeConfig_Serialization_ContainsPortAndToken()
    {
        var cfg = new MusicBridgeConfig(49152, "a1b2c3d4e5f60123456789abcdef0123456789abcdef0123456789abcdef0123");
        string json = JsonSerializer.Serialize(cfg);

        Assert.Contains("\"port\":49152", json);
        Assert.Contains("\"token\":", json);

        var deserialized = JsonSerializer.Deserialize<MusicBridgeConfig>(json);
        Assert.NotNull(deserialized);
        Assert.Equal(49152, deserialized.Port);
        Assert.Equal("a1b2c3d4e5f60123456789abcdef0123456789abcdef0123456789abcdef0123", deserialized.Token);
    }

    [Fact]
    public void WriteConfigFile_And_DeleteConfigFile_ManagesConfigFileCorrectly()
    {
        string tempDir = Path.Combine(Path.GetTempPath(), "aura_test_instance_" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);

            _bridge.WriteConfigFile(tempDir);
            string configPath = Path.Combine(tempDir, "config", "aura-music-bridge.json");

            Assert.True(File.Exists(configPath), "Config file must exist after WriteConfigFile");

            string json = File.ReadAllText(configPath);
            var parsed = JsonSerializer.Deserialize<MusicBridgeConfig>(json, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            Assert.NotNull(parsed);
            Assert.Equal(_bridge.Port, parsed.Port);
            Assert.Equal(_bridge.Token, parsed.Token);

            _bridge.DeleteConfigFile(tempDir);
            Assert.False(File.Exists(configPath), "Config file must be deleted after DeleteConfigFile");
        }
        finally
        {
            if (Directory.Exists(tempDir))
            {
                try { Directory.Delete(tempDir, true); } catch { }
            }
        }
    }

    #endregion

    #region 4. Position Extrapolation Tests

    [Fact]
    public void ExtrapolatePosition_WhenPlaying_AddsElapsedTime()
    {
        long initialPosMs = 10000;
        long updatedAt = 1730000000000;
        long currentNow = updatedAt + 250; // 250 ms later

        long extrapolated = MusicBridgeExtrapolation.ExtrapolatePosition(initialPosMs, updatedAt, "playing", currentNow);

        Assert.Equal(10250, extrapolated);
    }

    [Fact]
    public void ExtrapolatePosition_WhenPaused_DoesNotAdvance()
    {
        long initialPosMs = 10000;
        long updatedAt = 1730000000000;
        long currentNow = updatedAt + 250;

        long extrapolated = MusicBridgeExtrapolation.ExtrapolatePosition(initialPosMs, updatedAt, "paused", currentNow);

        Assert.Equal(10000, extrapolated);
    }

    [Fact]
    public void ExtrapolatePosition_WhenStopped_DoesNotAdvance()
    {
        long initialPosMs = 0;
        long updatedAt = 1730000000000;
        long currentNow = updatedAt + 250;

        long extrapolated = MusicBridgeExtrapolation.ExtrapolatePosition(initialPosMs, updatedAt, "stopped", currentNow);

        Assert.Equal(0, extrapolated);
    }

    [Fact]
    public void ExtrapolatePosition_Accuracy_Within30MsThreshold()
    {
        // Simulate playing track where player advances at real-time rate with small network/timer jitter (e.g. 5-15 ms)
        long initialPosMs = 20000;
        long updatedAt = 1730000000000;

        for (int step = 10; step <= 1000; step += 50)
        {
            long now = updatedAt + step;
            // Real player advanced by step ms +/- small simulated jitter of 5ms
            long simulatedRealPlayerPos = initialPosMs + step;

            long extrapolated = MusicBridgeExtrapolation.ExtrapolatePosition(initialPosMs, updatedAt, "playing", now);

            long diff = Math.Abs(extrapolated - simulatedRealPlayerPos);
            Assert.True(diff <= 30, $"Extrapolation error {diff} ms exceeds 30 ms threshold at step {step} ms");
        }
    }

    [Fact]
    public void ExtrapolatePosition_ClockSkew_DoesNotReturnNegative()
    {
        long initialPosMs = 5000;
        long updatedAt = 1730000000500;
        long skewedNow = 1730000000400; // clock went backwards 100ms

        long extrapolated = MusicBridgeExtrapolation.ExtrapolatePosition(initialPosMs, updatedAt, "playing", skewedNow);

        Assert.Equal(5000, extrapolated);
    }

    #endregion

    #region Helper Test Classes

    private class TestAudioService : IAudioService
    {
        public List<AudioTrack> Tracks { get; } = new();
        public int CurrentTrackIndex { get; set; } = 0;
        public IReadOnlyList<AudioTrack> Playlist => Tracks;
        public AudioTrack? CurrentTrack => (CurrentTrackIndex >= 0 && CurrentTrackIndex < Tracks.Count) ? Tracks[CurrentTrackIndex] : null;
        public bool HasTracks => Tracks.Count > 0;
        public TimeSpan Position { get; set; } = TimeSpan.Zero;
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(200);
        public bool IsPlaying { get; set; } = false;
        public int VolumePercent { get; set; } = 100;
        public bool IsMuted { get; set; } = false;

        public event EventHandler? MediaOpened;
        public event EventHandler? MediaEnded;
        public event EventHandler<bool>? PlayStateChanged;
        public event EventHandler<AudioTrack?>? TrackChanged;
        public event EventHandler<TimeSpan>? PositionChanged;

        public bool PlayCalled { get; private set; }
        public bool PauseCalled { get; private set; }
        public bool NextCalled { get; private set; }
        public bool PrevCalled { get; private set; }
        public bool StopCalled { get; private set; }

        public void Initialize() { }
        public void Play() { IsPlaying = true; PlayCalled = true; PlayStateChanged?.Invoke(this, true); }
        public void Pause() { IsPlaying = false; PauseCalled = true; PlayStateChanged?.Invoke(this, false); }
        public void TogglePlayPause() { if (IsPlaying) Pause(); else Play(); }
        public void Next() { NextCalled = true; if (Tracks.Count > 0) CurrentTrackIndex = (CurrentTrackIndex + 1) % Tracks.Count; TrackChanged?.Invoke(this, CurrentTrack); }
        public void Previous() { PrevCalled = true; if (Tracks.Count > 0) CurrentTrackIndex = (CurrentTrackIndex - 1 + Tracks.Count) % Tracks.Count; TrackChanged?.Invoke(this, CurrentTrack); }
        public void Seek(TimeSpan position) { Position = position; PositionChanged?.Invoke(this, position); }
        public void Stop() { IsPlaying = false; StopCalled = true; PlayStateChanged?.Invoke(this, false); }
        public void Enqueue(AudioTrack track) { Tracks.Add(track); TrackChanged?.Invoke(this, CurrentTrack); }
        public bool PlayTrack(string trackIdOrTitle)
        {
            int idx = Tracks.FindIndex(t => string.Equals(t.Title, trackIdOrTitle, StringComparison.OrdinalIgnoreCase) ||
                                            string.Equals(t.Path, trackIdOrTitle, StringComparison.OrdinalIgnoreCase));
            if (idx >= 0)
            {
                CurrentTrackIndex = idx;
                Play();
                return true;
            }
            return false;
        }
        public void Dispose() { }
    }

    private class TestLobbyApiClient : ILobbyApiClient
    {
        public string MockSearchResponse { get; set; } = """[{"id":"yt_123","kind":"track","title":"Believer","artist":"Imagine Dragons","artwork":"https://example.com/art.jpg","durationSec":204}]""";

        public Task<string> SearchMusicRawAsync(string query, CancellationToken cancellationToken = default)
        {
            return Task.FromResult(MockSearchResponse);
        }

        public Task<LobbyCreateResponse?> CreateLobbyAsync(string hostName, IReadOnlyList<ModManifestEntry>? manifest = null, CancellationToken cancellationToken = default) => Task.FromResult<LobbyCreateResponse?>(null);
        public Task<LobbyJoinResponse?> JoinLobbyAsync(string code, string playerName, IReadOnlyList<ModManifestEntry>? manifest = null, CancellationToken cancellationToken = default) => Task.FromResult<LobbyJoinResponse?>(null);
        public Task<bool> UpdateManifestAsync(string code, string playerName, IReadOnlyList<ModManifestEntry> manifest, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<LobbyStatusResponse?> GetStatusAsync(string code, string? playerName = null, CancellationToken cancellationToken = default) => Task.FromResult<LobbyStatusResponse?>(null);
        public Task<(System.Net.HttpStatusCode? StatusCode, LobbyStatusResponse? Response, string RawBody)> GetStatusDetailedAsync(string code, string? playerName = null, CancellationToken cancellationToken = default) => Task.FromResult((default(System.Net.HttpStatusCode?), default(LobbyStatusResponse?), ""));
        public Task<bool> OpenLobbyAsync(string code, string hostToken, string tunnelAddress, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> HeartbeatAsync(string code, string hostToken, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> CloseLobbyAsync(string code, string hostToken, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> LeaveLobbyAsync(string code, string playerName, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<bool> KickPlayerAsync(string code, string hostToken, string playerName, CancellationToken cancellationToken = default) => Task.FromResult(true);
        public Task<TunnelConfigResponse?> GetTunnelConfigAsync(CancellationToken cancellationToken = default) => Task.FromResult<TunnelConfigResponse?>(null);
    }

    #endregion
}

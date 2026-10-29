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
    private readonly FakeCoreWebView _fakeCore;
    private readonly EmbedMusicPlayer _embedPlayer;
    private readonly MusicBridgeService _bridge;
    private readonly HttpClient _client;

    public MusicBridgeTests()
    {
        _audioService = new TestAudioService();
        _lobbyApiClient = new TestLobbyApiClient();
        _fakeCore = new FakeCoreWebView();
        _embedPlayer = new EmbedMusicPlayer();
        _embedPlayer.AttachWebView(_fakeCore);
        _bridge = new MusicBridgeService(_audioService, _lobbyApiClient, _embedPlayer);
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
        _embedPlayer.Dispose();
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
    public async Task PostQueue_LocalFile_EnqueuesInAudioService()
    {
        string tempFile = Path.Combine(Path.GetTempPath(), $"test_track_{Guid.NewGuid():N}.mp3");
        try
        {
            await File.WriteAllTextAsync(tempFile, "dummy audio content");
            SetAuthToken(_bridge.Token);
            var content = new StringContent(JsonSerializer.Serialize(new { trackId = tempFile, title = "Bones", artist = "Imagine Dragons" }), Encoding.UTF8, "application/json");
            var response = await _client.PostAsync("/queue", content);

            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            Assert.Contains(_audioService.Tracks, t => t.Title == "Bones" && t.Artist == "Imagine Dragons");
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public async Task PostQueue_ValidYouTubeId_DispatchesLoadCommandToEmbedPlayer()
    {
        string? dispatchedOp = null;
        object? dispatchedPayload = null;
        _embedPlayer.CommandDispatched += (op, payload) =>
        {
            dispatchedOp = op;
            dispatchedPayload = payload;
        };

        SetAuthToken(_bridge.Token);
        var content = new StringContent("""{"trackId": "dQw4w9WgXcQ", "title": "Never Gonna Give You Up", "artist": "Rick Astley"}""", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/queue", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("load", dispatchedOp);
        Assert.NotNull(_embedPlayer.CurrentTrack);
        Assert.Equal("dQw4w9WgXcQ", _embedPlayer.CurrentTrack.Id);
        Assert.Equal("Never Gonna Give You Up", _embedPlayer.CurrentTrack.Title);
        Assert.Equal("Rick Astley", _embedPlayer.CurrentTrack.Artist);
    }

    [Fact]
    public async Task PostQueue_YouTubeUrl_ExtractsIdAndLoadsVideo()
    {
        string? dispatchedOp = null;
        _embedPlayer.CommandDispatched += (op, payload) => dispatchedOp = op;

        SetAuthToken(_bridge.Token);
        var content = new StringContent("""{"trackId": "https://www.youtube.com/watch?v=dQw4w9WgXcQ", "title": "Never Gonna Give You Up", "artist": "Rick Astley"}""", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/queue", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("load", dispatchedOp);
        Assert.Equal("dQw4w9WgXcQ", _embedPlayer.CurrentTrack?.Id);
    }

    [Fact]
    public async Task PostQueue_CatalogTrackId_ResolvesViaLobbyApiAndLoads()
    {
        string? dispatchedOp = null;
        _embedPlayer.CommandDispatched += (op, payload) => dispatchedOp = op;

        SetAuthToken(_bridge.Token);
        var content = new StringContent("""{"trackId": "cat-track-123", "title": "ResolveMe", "artist": "ResolveMeArtist"}""", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/queue", content);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("load", dispatchedOp);
        Assert.Equal("dQw4w9WgXcQ", _embedPlayer.CurrentTrack?.Id);
    }

    [Fact]
    public async Task PostQueue_UnresolvableTrack_Returns422UnprocessableEntity()
    {
        SetAuthToken(_bridge.Token);
        var content = new StringContent("""{"trackId": "unknown_unresolvable_track_id_xyz", "title": "Unknown Song", "artist": "Unknown Artist"}""", Encoding.UTF8, "application/json");
        var response = await _client.PostAsync("/queue", content);

        Assert.Equal(HttpStatusCode.UnprocessableEntity, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();
        Assert.Contains("resolve_failed", json);
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

    [Fact]
    public async Task EmbedPlayer_TimeUpdateContract_AdvancingPositionPrecisionWithin30Ms()
    {
        using var player = new EmbedMusicPlayer();
        player.AttachWebView(new FakeCoreWebView());
        await player.LoadVideoAsync("dQw4w9WgXcQ", "Test Track", "Test Artist", null, 180000);

        // 1. Simulate iframe postMessage: ready
        player.HandleEmbedMessage("""{"source":"aura-yt-embed","channel":"aura-music","event":"ready"}""");

        // 2. Simulate iframe postMessage: stateChange playing (state 1)
        player.HandleEmbedMessage("""{"source":"aura-yt-embed","channel":"aura-music","event":"stateChange","state":1,"videoId":"dQw4w9WgXcQ"}""");

        // 3. Simulate iframe postMessage: timeUpdate at 10.0 seconds
        player.HandleEmbedMessage("""{"source":"aura-yt-embed","channel":"aura-music","event":"timeUpdate","currentTime":10.0,"duration":180.0,"videoId":"dQw4w9WgXcQ"}""");

        // Check immediate position: should be ~10000 ms
        long posInitial = player.GetExactPositionMs(out long updatedAt);
        Assert.InRange(posInitial, 10000, 10030);

        // Advance wall-clock by 60ms
        await Task.Delay(60);

        long posAdv = player.GetExactPositionMs(out long updatedAtAdv);
        long elapsedPos = posAdv - posInitial;
        long elapsedWall = updatedAtAdv - updatedAt;

        // Position measurement error against advancing time must be <= 30ms
        long error = Math.Abs(elapsedPos - elapsedWall);
        Assert.True(error <= 30, $"Extrapolation error {error} ms exceeded 30ms limit (elapsedPos={elapsedPos}, elapsedWall={elapsedWall})");

        // 4. Now pause (state 2)
        player.HandleEmbedMessage("""{"source":"aura-yt-embed","channel":"aura-music","event":"stateChange","state":2,"videoId":"dQw4w9WgXcQ"}""");
        long posPaused = player.GetExactPositionMs(out _);

        await Task.Delay(50);
        long posPausedAfter = player.GetExactPositionMs(out _);

        Assert.Equal(posPaused, posPausedAfter);
    }

    [Fact]
    public async Task EmbedPlayer_MultipleSteps_MeasurementErrorWithin30Ms()
    {
        using var player = new EmbedMusicPlayer();
        player.AttachWebView(new FakeCoreWebView());
        await player.LoadVideoAsync("dQw4w9WgXcQ", "Track", "Artist", null, 300000);
        player.HandleEmbedMessage("""{"source":"aura-yt-embed","channel":"aura-music","event":"stateChange","state":1,"videoId":"dQw4w9WgXcQ"}""");
        player.HandleEmbedMessage("""{"source":"aura-yt-embed","channel":"aura-music","event":"timeUpdate","currentTime":15.0,"duration":300.0,"videoId":"dQw4w9WgXcQ"}""");

        var sw = System.Diagnostics.Stopwatch.StartNew();
        long startPos = player.GetExactPositionMs(out _);

        for (int i = 0; i < 4; i++)
        {
            await Task.Delay(25);
            long currentPos = player.GetExactPositionMs(out _);
            long expectedAdvancement = sw.ElapsedMilliseconds;
            long actualAdvancement = currentPos - startPos;
            long diff = Math.Abs(actualAdvancement - expectedAdvancement);

            Assert.True(diff <= 30, $"At step {i}, diff {diff}ms exceeded 30ms (actual={actualAdvancement}, expected={expectedAdvancement})");
        }
    }

    [Fact]
    public async Task EmbedPlayer_MatchesQaProbeCriteria_NoRollbackAndNoFalseSuccess()
    {
        // 1. Without WebView/transport, LoadVideoAsync must return false!
        using var p = new EmbedMusicPlayer();
        bool loadWithoutTransport = await p.LoadVideoAsync("abcdefghijk");
        Assert.False(loadWithoutTransport);
        Assert.Equal("stopped", p.State);

        // 2. Time update and pause: NO ROLLBACK!
        p.HandleEmbedMessage("""{"source":"aura-yt-embed","event":"stateChange","state":1}""");
        p.HandleEmbedMessage("""{"source":"aura-yt-embed","event":"timeUpdate","currentTime":10.0}""");
        await Task.Delay(100);
        long before = p.GetExactPositionMs(out _);
        await p.PauseAsync();
        long after = p.GetExactPositionMs(out _);
        long rollbackMs = before - after;
        Assert.True(rollbackMs <= 0, $"Pause caused rollback of {rollbackMs} ms (before={before}, after={after})");

        // 3. Attach transport, load video, inspect dispatched script
        var core = new FakeCoreWebView();
        p.AttachWebView(core);
        bool loadWithTransport = await p.LoadVideoAsync("abcdefghijk");
        Assert.True(loadWithTransport);
        Assert.Contains("contentWindow.postMessage", core.LastScript);
        Assert.Contains("\"op\":\"load\"", core.LastScript);
        Assert.Contains("\"videoId\":\"abcdefghijk\"", core.LastScript);
        Assert.DoesNotContain("\"data\":{", core.LastScript); // Must be flat!
    }

    [Fact]
    public async Task EmbedPlayer_ProtocolReceiver_ValidatesContractAndAdvances()
    {
        // Model player.html message receiver
        string? receivedOp = null;
        string? receivedVideoId = null;
        double? receivedSeconds = null;
        double? receivedValue = null;
        bool receiverError = false;

        using var player = new EmbedMusicPlayer(script =>
        {
            // Extract postMessage argument from script
            var match = System.Text.RegularExpressions.Regex.Match(script, @"postMessage\((\{.*\}),\s*'\*'\)");
            if (match.Success)
            {
                using var doc = JsonDocument.Parse(match.Groups[1].Value);
                var root = doc.RootElement;
                if (root.GetProperty("source").GetString() != "aura-yt-host" ||
                    root.GetProperty("channel").GetString() != "aura-music")
                {
                    receiverError = true;
                }
                receivedOp = root.GetProperty("op").GetString();
                if (root.TryGetProperty("videoId", out var vid)) receivedVideoId = vid.GetString();
                if (root.TryGetProperty("seconds", out var sec)) receivedSeconds = sec.GetDouble();
                if (root.TryGetProperty("value", out var val)) receivedValue = val.GetDouble();
            }
            return Task.FromResult<string?>("true");
        });

        // 1. Load command dispatch
        bool loadOk = await player.LoadVideoAsync("abcdefghijk", "Title", "Artist");
        Assert.True(loadOk);
        Assert.False(receiverError);
        Assert.Equal("load", receivedOp);
        Assert.Equal("abcdefghijk", receivedVideoId);

        // 2. Seek command dispatch
        bool seekOk = await player.SeekAsync(45000);
        Assert.True(seekOk);
        Assert.Equal("seek", receivedOp);
        Assert.Equal(45.0, receivedSeconds);

        // 3. Volume command dispatch
        bool volOk = await player.SetVolumeAsync(0.75);
        Assert.True(volOk);
        Assert.Equal("volume", receivedOp);
        Assert.Equal(75.0, receivedValue);
    }

    [Fact]
    public async Task EmbedPlayer_QueueNavigation_MultipleTracksAndEndHandling()
    {
        var core = new FakeCoreWebView();
        using var p = new EmbedMusicPlayer();
        p.AttachWebView(core);

        var track1 = new MusicBridgeTrack("id_track_001", "Artist 1", "Song 1", 180000, null);
        var track2 = new MusicBridgeTrack("id_track_002", "Artist 2", "Song 2", 200000, null);
        var track3 = new MusicBridgeTrack("id_track_003", "Artist 3", "Song 3", 220000, null);

        // Queue 3 tracks
        Assert.True(await p.QueueTrackAsync(track1, playNow: true));
        Assert.True(await p.QueueTrackAsync(track2, playNow: false));
        Assert.True(await p.QueueTrackAsync(track3, playNow: false));

        Assert.Equal(3, p.Queue.Count);
        Assert.Equal(0, p.CurrentIndex);
        Assert.Equal("id_track_001", p.CurrentTrack?.Id);

        // Next -> track 2
        Assert.True(await p.NextAsync());
        Assert.Equal(1, p.CurrentIndex);
        Assert.Equal("id_track_002", p.CurrentTrack?.Id);

        // Next -> track 3
        Assert.True(await p.NextAsync());
        Assert.Equal(2, p.CurrentIndex);
        Assert.Equal("id_track_003", p.CurrentTrack?.Id);

        // Next at end of queue -> returns false!
        Assert.False(await p.NextAsync());
        Assert.Equal(2, p.CurrentIndex);

        // Previous -> track 2
        Assert.True(await p.PreviousAsync());
        Assert.Equal(1, p.CurrentIndex);
        Assert.Equal("id_track_002", p.CurrentTrack?.Id);

        // Previous -> track 1
        Assert.True(await p.PreviousAsync());
        Assert.Equal(0, p.CurrentIndex);
        Assert.Equal("id_track_001", p.CurrentTrack?.Id);
    }

    [Fact]
    public async Task EmbedPlayer_PauseResumeRepeatedPlay_PreservesTimeContinuity()
    {
        var core = new FakeCoreWebView();
        using var p = new EmbedMusicPlayer();
        p.AttachWebView(core);
        await p.LoadVideoAsync("abcdefghijk", "Title", "Artist", null, 300000);

        p.HandleEmbedMessage("""{"source":"aura-yt-embed","event":"stateChange","state":1}""");
        p.HandleEmbedMessage("""{"source":"aura-yt-embed","event":"timeUpdate","currentTime":20.0}""");

        await Task.Delay(50);
        long posBeforePause = p.GetExactPositionMs(out _);
        await p.PauseAsync();
        long posAfterPause = p.GetExactPositionMs(out _);
        Assert.True(Math.Abs(posBeforePause - posAfterPause) <= 5);

        // Resume without new timeUpdate
        await p.PlayAsync();
        await Task.Delay(50);
        long posAfterResume = p.GetExactPositionMs(out _);
        Assert.True(posAfterResume > posAfterPause, "Position must advance after resume");

        // Repeated play without new timeUpdate
        long posBeforeRepeatedPlay = p.GetExactPositionMs(out _);
        await p.PlayAsync();
        long posAfterRepeatedPlay = p.GetExactPositionMs(out _);
        Assert.True(posAfterRepeatedPlay >= posBeforeRepeatedPlay, "Repeated play must not lose progress");
    }

    [Fact]
    public async Task PostQueue_WithoutTransport_Returns503ServiceUnavailable()
    {
        using var isolatedPlayer = new EmbedMusicPlayer(); // no transport
        using var isolatedBridge = new MusicBridgeService(_audioService, _lobbyApiClient, isolatedPlayer);
        isolatedBridge.Start();

        using var client = new HttpClient { BaseAddress = new Uri($"http://127.0.0.1:{isolatedBridge.Port}/") };
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", isolatedBridge.Token);

        var content = new StringContent("""{"trackId": "dQw4w9WgXcQ", "title": "Never Gonna Give You Up", "artist": "Rick Astley"}""", Encoding.UTF8, "application/json");
        var response = await client.PostAsync("/queue", content);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    [Fact]
    public async Task GetSearch_NormalizesRawLobbyApi_MatchesJavaClientContract()
    {
        // Set raw lobby-api response with items, artwork, durationSec
        _lobbyApiClient.MockSearchResponse = """
        {
            "items": [
                {
                    "id": "yt_dQw4w9WgXcQ",
                    "kind": "track",
                    "title": "Believer",
                    "artist": "Imagine Dragons",
                    "artwork": "https://img.youtube.com/vi/dQw4w9WgXcQ/hqdefault.jpg",
                    "durationSec": 204.5
                }
            ]
        }
        """;

        SetAuthToken(_bridge.Token);
        var response = await _client.GetAsync("/search?q=believer");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadAsStringAsync();

        // Must be JSON array, conforming to Java MusicJsonParser.parseTrackList contract
        using var doc = JsonDocument.Parse(json);
        Assert.Equal(JsonValueKind.Array, doc.RootElement.ValueKind);

        var firstTrack = doc.RootElement[0];
        Assert.Equal("yt_dQw4w9WgXcQ", firstTrack.GetProperty("id").GetString());
        Assert.Equal("Believer", firstTrack.GetProperty("title").GetString());
        Assert.Equal("Imagine Dragons", firstTrack.GetProperty("artist").GetString());
        Assert.Equal(204500, firstTrack.GetProperty("durationMs").GetInt64());
        Assert.Equal("https://img.youtube.com/vi/dQw4w9WgXcQ/hqdefault.jpg", firstTrack.GetProperty("coverUrl").GetString());

        // Cross-Language Contract validation:
        // Simulate net.auramc.glass.client.music.MusicJsonParser.parseTrack logic
        string parsedId = firstTrack.TryGetProperty("id", out var idProp) ? idProp.GetString() ?? "" : "";
        string parsedArtist = firstTrack.TryGetProperty("artist", out var artProp) ? artProp.GetString() ?? "" : "";
        string parsedTitle = firstTrack.TryGetProperty("title", out var titProp) ? titProp.GetString() ?? "" : "";
        long parsedDurationMs = firstTrack.TryGetProperty("durationMs", out var durProp) ? durProp.GetInt64() : 0;
        string parsedCoverUrl = firstTrack.TryGetProperty("coverUrl", out var covProp) ? covProp.GetString() ?? "" : "";

        Assert.Equal("yt_dQw4w9WgXcQ", parsedId);
        Assert.Equal("Imagine Dragons", parsedArtist);
        Assert.Equal("Believer", parsedTitle);
        Assert.Equal(204500, parsedDurationMs);
        Assert.Equal("https://img.youtube.com/vi/dQw4w9WgXcQ/hqdefault.jpg", parsedCoverUrl);
    }

    [Fact]
    public async Task Search_Then_Queue_WiredToEmbedPlayer_LoadsVideo()
    {
        _lobbyApiClient.MockSearchResponse = """
        [
            {
                "id": "dQw4w9WgXcQ",
                "title": "Believer",
                "artist": "Imagine Dragons",
                "artwork": "https://example.com/art.jpg",
                "durationSec": 204
            }
        ]
        """;

        SetAuthToken(_bridge.Token);

        // 1. Search
        var searchResp = await _client.GetAsync("/search?q=believer");
        Assert.Equal(HttpStatusCode.OK, searchResp.StatusCode);
        var searchJson = await searchResp.Content.ReadAsStringAsync();
        var tracks = JsonSerializer.Deserialize<List<MusicBridgeTrack>>(searchJson, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(tracks);
        Assert.NotEmpty(tracks);

        var track = tracks[0];
        Assert.Equal("dQw4w9WgXcQ", track.Id);

        // 2. Queue
        var queueContent = new StringContent(JsonSerializer.Serialize(new { trackId = track.Id, title = track.Title, artist = track.Artist }), Encoding.UTF8, "application/json");
        var queueResp = await _client.PostAsync("/queue", queueContent);
        Assert.Equal(HttpStatusCode.OK, queueResp.StatusCode);

        // Simulate iframe player confirming playback start
        _embedPlayer.HandleEmbedMessage("""{"source":"aura-yt-embed","channel":"aura-music","event":"stateChange","state":1,"videoId":"dQw4w9WgXcQ"}""");

        // 3. Verify embed player is now playing
        Assert.NotNull(_embedPlayer.CurrentTrack);
        Assert.Equal("dQw4w9WgXcQ", _embedPlayer.CurrentTrack.Id);
        Assert.Equal("playing", _embedPlayer.State);

        // 4. Verify /now returns embed player info
        var nowResp = await _client.GetAsync("/now");
        Assert.Equal(HttpStatusCode.OK, nowResp.StatusCode);
        var now = JsonSerializer.Deserialize<MusicBridgeNowResponse>(await nowResp.Content.ReadAsStringAsync(), new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        Assert.NotNull(now);
        Assert.Equal("playing", now.State);
        Assert.Equal("dQw4w9WgXcQ", now.Track?.Id);
    }

    #endregion

    #region Helper Test Classes

    public class FakeCoreWebView
    {
        public string LastScript { get; set; } = "";
        public Task<string> ExecuteScriptAsync(string script)
        {
            LastScript = script;
            return Task.FromResult("true");
        }
    }

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

        public Task<MusicResolveResult?> ResolveMusicAsync(string artist, string title, CancellationToken cancellationToken = default)
        {
            if (title.Contains("ResolveMe", StringComparison.OrdinalIgnoreCase) ||
                artist.Contains("ResolveMe", StringComparison.OrdinalIgnoreCase) ||
                title.Contains("Believer", StringComparison.OrdinalIgnoreCase))
            {
                return Task.FromResult<MusicResolveResult?>(new MusicResolveResult("dQw4w9WgXcQ", null));
            }
            return Task.FromResult<MusicResolveResult?>(null);
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

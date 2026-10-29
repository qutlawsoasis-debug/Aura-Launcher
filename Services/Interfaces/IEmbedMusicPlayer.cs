using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface IEmbedMusicPlayer : IDisposable
{
    bool IsActive { get; }
    bool HasTransport { get; }
    bool IsReady { get; }
    string State { get; }
    MusicBridgeTrack? CurrentTrack { get; }
    double CurrentTimeSeconds { get; }
    long LastUpdatedAtUnixMs { get; }
    double Volume { get; set; }
    IReadOnlyList<MusicBridgeTrack> Queue { get; }
    int CurrentIndex { get; }

    Task<bool> PlayAsync();
    Task<bool> PauseAsync();
    Task<bool> NextAsync();
    Task<bool> PreviousAsync();
    Task<bool> SeekAsync(long positionMs);
    Task<bool> SetVolumeAsync(double volume);
    Task<bool> LoadVideoAsync(string videoId, string? title = null, string? artist = null, string? coverUrl = null, long durationMs = 0);
    Task<bool> QueueTrackAsync(MusicBridgeTrack track, bool playNow = true);

    long GetExactPositionMs(out long updatedAtUnixMs);

    void HandleEmbedMessage(string rawJson);
    void AttachWebView(object? coreWebView2);

    event Action<string, object?>? CommandDispatched;
    event Action<MusicBridgeTrack?>? TrackChanged;
    event Action<string>? StateChanged;
}

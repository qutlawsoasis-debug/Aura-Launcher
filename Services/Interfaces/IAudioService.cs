using System;
using System.Collections.Generic;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface IAudioService : IDisposable
{
    IReadOnlyList<AudioTrack> Playlist { get; }
    AudioTrack? CurrentTrack { get; }
    int CurrentTrackIndex { get; }
    bool HasTracks { get; }

    TimeSpan Position { get; set; }
    TimeSpan Duration { get; }
    bool IsPlaying { get; }
    int VolumePercent { get; set; }
    bool IsMuted { get; set; }

    event EventHandler? MediaOpened;
    event EventHandler? MediaEnded;
    event EventHandler<bool>? PlayStateChanged;
    event EventHandler<AudioTrack?>? TrackChanged;
    event EventHandler<TimeSpan>? PositionChanged;

    void Initialize();
    void Play();
    void Pause();
    void TogglePlayPause();
    void Next();
    void Previous();
    void Seek(TimeSpan position);
    void Stop();
    void Enqueue(AudioTrack track) { }
    bool PlayTrack(string trackIdOrTitle) => false;
}

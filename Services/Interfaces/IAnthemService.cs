using System;

namespace AuraLauncher.Services.Interfaces;

public interface IAnthemService : IDisposable
{
    int VolumePercent { get; set; }
    bool IsMuted { get; set; }
    bool IsPlaying { get; }
    bool IsAvailable { get; }
    double PlaybackProgress { get; }
    string CurrentTrackTitle { get; }
    TimeSpan CurrentPosition { get; }
    TimeSpan TotalDuration { get; }

    event EventHandler<string>? TrackChanged;
    event EventHandler<bool>? PlayStateChanged;
    event EventHandler<double>? ProgressChanged;

    void Initialize();
    void PlayAnthem(bool force = false);
    void TogglePlayPause();
    void NextTrack();
    void PreviousTrack();
    void ToggleMute();
    void SetVolume(int volumePercent);
    void Seek(double progress);
    void Stop();
}

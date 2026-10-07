using System;

namespace AuraLauncher.Services.Interfaces;

public interface IAnthemService : IDisposable
{
    int VolumePercent { get; set; }
    bool IsMuted { get; set; }
    bool IsPlaying { get; }
    string CurrentTrackTitle { get; }

    event EventHandler<string>? TrackChanged;

    void Initialize();
    void PlayAnthem(bool force = false);
    void TogglePlayPause();
    void NextTrack();
    void PreviousTrack();
    void ToggleMute();
    void SetVolume(int volumePercent);
    void Stop();
}

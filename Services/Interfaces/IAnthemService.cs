using System;

namespace AuraLauncher.Services.Interfaces;

public interface IAnthemService : IDisposable
{
    int VolumePercent { get; set; }
    bool IsMuted { get; set; }
    bool IsPlaying { get; }

    void Initialize();
    void PlayAnthem(bool force = false);
    void ToggleMute();
    void SetVolume(int volumePercent);
    void Stop();
}

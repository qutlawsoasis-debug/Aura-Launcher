using System;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class AnthemService : IAnthemService
{
    private readonly IAudioService _audioService;

    public AnthemService(IAudioService audioService)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));
        _audioService.TrackChanged += (s, track) => TrackChanged?.Invoke(this, CurrentTrackTitle);
        _audioService.PlayStateChanged += (s, playing) => PlayStateChanged?.Invoke(this, playing);
        _audioService.PositionChanged += (s, pos) =>
        {
            double progress = TotalDuration.TotalSeconds > 0 ? pos.TotalSeconds / TotalDuration.TotalSeconds : 0.0;
            ProgressChanged?.Invoke(this, progress);
        };
    }

    public int VolumePercent
    {
        get => _audioService.VolumePercent;
        set => _audioService.VolumePercent = value;
    }

    public bool IsMuted
    {
        get => _audioService.IsMuted;
        set => _audioService.IsMuted = value;
    }

    public bool IsPlaying => _audioService.IsPlaying;
    public bool IsAvailable => _audioService.HasTracks;

    public double PlaybackProgress
    {
        get
        {
            double total = TotalDuration.TotalSeconds;
            return total > 0 ? CurrentPosition.TotalSeconds / total : 0.0;
        }
    }

    public string CurrentTrackTitle
    {
        get
        {
            var t = _audioService.CurrentTrack;
            if (t == null) return "";
            return $"{t.Artist} · {t.Title}";
        }
    }

    public TimeSpan CurrentPosition => _audioService.Position;
    public TimeSpan TotalDuration => _audioService.Duration;

    public event EventHandler<string>? TrackChanged;
    public event EventHandler<bool>? PlayStateChanged;
    public event EventHandler<double>? ProgressChanged;

    public void Initialize() => _audioService.Initialize();
    public void PlayAnthem(bool force = false) => _audioService.Play();
    public void TogglePlayPause() => _audioService.TogglePlayPause();
    public void NextTrack() => _audioService.Next();
    public void PreviousTrack() => _audioService.Previous();
    public void ToggleMute() => _audioService.IsMuted = !_audioService.IsMuted;
    public void SetVolume(int volumePercent) => _audioService.VolumePercent = volumePercent;
    public void Seek(double progress)
    {
        var total = _audioService.Duration.TotalSeconds;
        _audioService.Seek(TimeSpan.FromSeconds(progress * total));
    }
    public void Stop() => _audioService.Stop();
    public void Dispose() => _audioService.Dispose();
}

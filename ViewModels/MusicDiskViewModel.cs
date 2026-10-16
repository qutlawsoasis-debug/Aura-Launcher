using System;
using System.IO;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;

namespace AuraLauncher.ViewModels;

public sealed partial class MusicDiskViewModel : ObservableObject
{
    private readonly IAudioService _audioService;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(PlayPauseCommand))]
    [NotifyCanExecuteChangedFor(nameof(PreviousCommand))]
    [NotifyCanExecuteChangedFor(nameof(NextCommand))]
    private bool _hasTracks;

    [ObservableProperty] private string _title = "Нет треков";
    [ObservableProperty] private string _artist = "—";
    [ObservableProperty] private ImageSource? _cover;
    [ObservableProperty] private TimeSpan _position = TimeSpan.Zero;
    [ObservableProperty] private TimeSpan _duration = TimeSpan.Zero;
    [ObservableProperty] private bool _isPlaying;

    public MusicDiskViewModel(IAudioService audioService)
    {
        _audioService = audioService ?? throw new ArgumentNullException(nameof(audioService));

        _audioService.TrackChanged += (s, track) => UpdateFromTrack(track);
        _audioService.PlayStateChanged += (s, playing) => IsPlaying = playing;
        _audioService.PositionChanged += (s, pos) => Position = pos;
        _audioService.MediaOpened += (s, e) => Duration = _audioService.Duration;

        UpdateFromTrack(_audioService.CurrentTrack);
        HasTracks = _audioService.HasTracks;
        IsPlaying = _audioService.IsPlaying;
    }

    private void UpdateFromTrack(AudioTrack? track)
    {
        HasTracks = _audioService.HasTracks;
        if (track == null)
        {
            Title = "Нет треков";
            Artist = "—";
            Cover = null;
            Position = TimeSpan.Zero;
            Duration = TimeSpan.Zero;
            return;
        }

        Title = track.Title;
        Artist = track.Artist;
        Position = _audioService.Position;
        Duration = _audioService.Duration;

        if (!string.IsNullOrWhiteSpace(track.CoverPath) && File.Exists(track.CoverPath))
        {
            try
            {
                var bi = new BitmapImage();
                bi.BeginInit();
                bi.CacheOption = BitmapCacheOption.OnLoad;
                bi.UriSource = new Uri(track.CoverPath, UriKind.Absolute);
                bi.EndInit();
                bi.Freeze();
                Cover = bi;
            }
            catch
            {
                Cover = null;
            }
        }
        else
        {
            Cover = null;
        }
    }

    [RelayCommand(CanExecute = nameof(HasTracks))]
    private void PlayPause()
    {
        _audioService.TogglePlayPause();
    }

    [RelayCommand(CanExecute = nameof(HasTracks))]
    private void Previous()
    {
        _audioService.Previous();
    }

    [RelayCommand(CanExecute = nameof(HasTracks))]
    private void Next()
    {
        _audioService.Next();
    }
}

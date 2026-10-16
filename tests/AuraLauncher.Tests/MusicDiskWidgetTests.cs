using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.ViewModels;
using Xunit;

namespace AuraLauncher.Tests;

public class MusicDiskWidgetTests
{
    private class FakeAudioService : IAudioService
    {
        private readonly List<AudioTrack> _tracks = new();
        private int _index = 0;

        public FakeAudioService(IEnumerable<AudioTrack>? initialTracks = null)
        {
            if (initialTracks != null)
            {
                _tracks.AddRange(initialTracks);
            }
        }

        public IReadOnlyList<AudioTrack> Playlist => _tracks;
        public AudioTrack? CurrentTrack => (_index >= 0 && _index < _tracks.Count) ? _tracks[_index] : null;
        public int CurrentTrackIndex => _index;
        public bool HasTracks => _tracks.Count > 0;

        public TimeSpan Position { get; set; } = TimeSpan.Zero;
        public TimeSpan Duration { get; set; } = TimeSpan.FromSeconds(120);
        public bool IsPlaying { get; private set; } = false;
        public int VolumePercent { get; set; } = 100;
        public bool IsMuted { get; set; } = false;

        public event EventHandler? MediaOpened;
        public event EventHandler? MediaEnded;
        public event EventHandler<bool>? PlayStateChanged;
        public event EventHandler<AudioTrack?>? TrackChanged;
        public event EventHandler<TimeSpan>? PositionChanged;

        public void Initialize() { }

        public void Play()
        {
            if (!HasTracks) return;
            IsPlaying = true;
            PlayStateChanged?.Invoke(this, true);
        }

        public void Pause()
        {
            IsPlaying = false;
            PlayStateChanged?.Invoke(this, false);
        }

        public void TogglePlayPause()
        {
            if (IsPlaying) Pause();
            else Play();
        }

        public void Next()
        {
            if (!HasTracks) return;
            _index = (_index + 1) % _tracks.Count;
            Position = TimeSpan.Zero;
            TrackChanged?.Invoke(this, CurrentTrack);
        }

        public void Previous()
        {
            if (!HasTracks) return;
            if (Position > TimeSpan.FromSeconds(3))
            {
                Position = TimeSpan.Zero;
                PositionChanged?.Invoke(this, TimeSpan.Zero);
                return;
            }

            _index = (_index - 1 + _tracks.Count) % _tracks.Count;
            Position = TimeSpan.Zero;
            TrackChanged?.Invoke(this, CurrentTrack);
        }

        public void Seek(TimeSpan position)
        {
            Position = position;
            PositionChanged?.Invoke(this, position);
        }

        public void Stop() => Pause();
        public void Dispose() => Stop();
    }

    [Fact]
    public void EmptyPlaylist_DisplaysNoTracks_AndDisablesCommands()
    {
        var audioService = new FakeAudioService();
        var vm = new MusicDiskViewModel(audioService);

        Assert.False(vm.HasTracks);
        Assert.Equal("Нет треков", vm.Title);
        Assert.Equal("—", vm.Artist);
        Assert.Null(vm.Cover);
        Assert.False(vm.PlayPauseCommand.CanExecute(null));
        Assert.False(vm.NextCommand.CanExecute(null));
        Assert.False(vm.PreviousCommand.CanExecute(null));
    }

    [Fact]
    public void PopulatedPlaylist_BindsCurrentTrack_AndEnablesCommands()
    {
        var tracks = new[]
        {
            new AudioTrack("Aura", "zxcursed", "path1.mp3", null),
            new AudioTrack("Track Two", "Artist Two", "path2.mp3", null)
        };
        var audioService = new FakeAudioService(tracks);
        var vm = new MusicDiskViewModel(audioService);

        Assert.True(vm.HasTracks);
        Assert.Equal("Aura", vm.Title);
        Assert.Equal("zxcursed", vm.Artist);
        Assert.True(vm.PlayPauseCommand.CanExecute(null));

        // Play
        vm.PlayPauseCommand.Execute(null);
        Assert.True(vm.IsPlaying);

        // Next track
        vm.NextCommand.Execute(null);
        Assert.Equal("Track Two", vm.Title);
        Assert.Equal("Artist Two", vm.Artist);

        // Next wraps around
        vm.NextCommand.Execute(null);
        Assert.Equal("Aura", vm.Title);
    }

    [Fact]
    public void PreviousCommand_RewindsIfOverThreeSeconds()
    {
        var tracks = new[]
        {
            new AudioTrack("Track 1", "Artist 1", "p1.mp3", null),
            new AudioTrack("Track 2", "Artist 2", "p2.mp3", null)
        };
        var audioService = new FakeAudioService(tracks);
        var vm = new MusicDiskViewModel(audioService);

        // Advance to track 2
        vm.NextCommand.Execute(null);
        Assert.Equal("Track 2", vm.Title);

        // Position > 3 seconds
        audioService.Position = TimeSpan.FromSeconds(10);
        vm.PreviousCommand.Execute(null);

        // Still on Track 2, but rewound to 0
        Assert.Equal("Track 2", vm.Title);
        Assert.Equal(TimeSpan.Zero, audioService.Position);

        // Now Position is 0, Previous should go to Track 1
        vm.PreviousCommand.Execute(null);
        Assert.Equal("Track 1", vm.Title);
    }
}

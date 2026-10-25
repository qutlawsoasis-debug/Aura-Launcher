using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;
using AuraLauncher.ViewModels;
using Xunit;

namespace AuraLauncher.Tests;

public class AnthemServiceTests
{
    private class MockConfigService : IConfigService
    {
        public LauncherConfig CurrentConfig { get; set; } = new();
        public event EventHandler<LauncherConfig>? ConfigChanged;

        public Task<LauncherConfig> LoadConfigAsync(CancellationToken cancellationToken = default)
            => Task.FromResult(CurrentConfig);

        public Task SaveConfigAsync(LauncherConfig config, CancellationToken cancellationToken = default)
        {
            CurrentConfig = config;
            ConfigChanged?.Invoke(this, config);
            return Task.CompletedTask;
        }

        public Task UpdateConfigAsync(Action<LauncherConfig> updateAction, CancellationToken cancellationToken = default)
        {
            updateAction(CurrentConfig);
            ConfigChanged?.Invoke(this, CurrentConfig);
            return Task.CompletedTask;
        }
    }

    private class MockAnthemService : IAnthemService
    {
        public int VolumePercent { get; set; } = 100;
        public bool IsMuted { get; set; } = false;
        public bool IsPlaying { get; set; } = false;
        public bool IsAvailable { get; set; } = true;
        public bool Initialized { get; set; } = false;
        public bool Stopped { get; set; } = false;
        public string CurrentTrackTitle { get; set; } = "Demo Artist · Demo Track";
        public double PlaybackProgress { get; set; } = 0.0;
        public TimeSpan CurrentPosition { get; set; } = TimeSpan.Zero;
        public TimeSpan TotalDuration { get; set; } = TimeSpan.FromSeconds(76);
        public event EventHandler<string>? TrackChanged;
        public event EventHandler<bool>? PlayStateChanged;
        public event EventHandler<double>? ProgressChanged;

        public void Seek(double progress) { PlaybackProgress = progress; ProgressChanged?.Invoke(this, progress); }

        public void Initialize() => Initialized = true;
        public void PlayAnthem(bool force = false) { IsPlaying = true; PlayStateChanged?.Invoke(this, true); }
        public void TogglePlayPause() { IsPlaying = !IsPlaying; PlayStateChanged?.Invoke(this, IsPlaying); }
        public void NextTrack() => TrackChanged?.Invoke(this, CurrentTrackTitle);
        public void PreviousTrack() => TrackChanged?.Invoke(this, CurrentTrackTitle);
        public void ToggleMute() => IsMuted = !IsMuted;
        public void SetVolume(int volumePercent) => VolumePercent = Math.Clamp(volumePercent, 0, 100);
        public void Stop() { IsPlaying = false; Stopped = true; PlayStateChanged?.Invoke(this, false); }
        public void Dispose() => Stop();
    }

    private class MockSkinService : ISkinService
    {
        public SkinValidationResult ValidateSkinFile(string? filePath) => new(true);
        public System.Windows.Media.ImageSource LoadSkinImage(string? skinPath) => null!;
        public System.Windows.Media.ImageSource ExtractHeadAvatar(string? skinPath) => null!;
        public System.Windows.Media.ImageSource ExtractFrontSkinPreview(string? skinPath) => null!;
        public System.Windows.Media.ImageSource ExtractBackSkinPreview(string? skinPath) => null!;
        public Task SyncSkinToGameAsync(string? skinPath, string nickname, string gameDir, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetToDefaultSteveAsync(string nickname, string gameDir, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SkinUploadResult> UploadSkinToLobbyApiAsync(string? skinPath, string nickname, string model, string? ownerToken, string? baseUrl = null, CancellationToken cancellationToken = default) => Task.FromResult(new SkinUploadResult(true));
        public Task<System.Windows.Media.ImageSource> GetAvatarForPlayerAsync(string nickname, CancellationToken cancellationToken = default) => Task.FromResult<System.Windows.Media.ImageSource>(null!);
        public Task<(System.Windows.Media.ImageSource SkinTexture, bool IsSlim)> GetSkinTextureForPlayerAsync(string nickname, CancellationToken cancellationToken = default) => Task.FromResult<(System.Windows.Media.ImageSource, bool)>((null!, false));
        public void EnsureCustomSkinLoaderConfig(string gameDir) { }
        public void ClearCustomSkinLoaderCache(string gameDir) { }
    }

    [Fact]
    public void AnthemAssets_Removed_From_Packaging()
    {
        string? projectDir = FindProjectDirectory();
        Assert.NotNull(projectDir);

        // Демо-гимн полностью исключён из WPF/Content упаковки и веб-ассетов
        string csprojPath = Path.Combine(projectDir, "AuraLauncher.csproj");
        Assert.True(File.Exists(csprojPath), $"Project file must exist at {csprojPath}");
        string csproj = File.ReadAllText(csprojPath);
        Assert.DoesNotContain("anthem.mp3", csproj, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("artist.jpg", csproj, StringComparison.OrdinalIgnoreCase);

        Assert.False(File.Exists(Path.Combine(projectDir, "Resources", "Audio", "anthem.mp3")),
            "Демо-гимн не должен находиться в Resources/Audio");
        Assert.False(File.Exists(Path.Combine(projectDir, "Resources", "Audio", "artist.jpg")),
            "Обложка демо-гимна не должна находиться в Resources/Audio");

        string webAnthem = Path.Combine(projectDir, "docs", "redesign", "v3", "public", "assets", "audio", "anthem.mp3");
        Assert.False(File.Exists(webAnthem), "Демо-гимн не должен находиться в веб-ассетах");
    }

    [Fact]
    public void LauncherConfig_MusicStartupSettings_AreRemoved()
    {
        // Автозапуск музыки удалён полностью: настроек, способных его включить, в конфиге быть не должно
        Assert.Null(typeof(LauncherConfig).GetProperty("MusicOnStartup"));
        Assert.Null(typeof(LauncherConfig).GetProperty("AnthemCustomPath"));
    }

    private class MockLauncherUpdateService : ILauncherUpdateService
    {
        public bool IsInstalled => false;
        public string CurrentVersion => "1.2.11";
        public Task<LauncherUpdateResult> CheckAndApplyAsync(IProgress<DownloadProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new LauncherUpdateResult(LauncherUpdateStatus.UpToDate, "OK"));
        public Task<string?> CheckForUpdatesAsync(CancellationToken ct = default) => Task.FromResult<string?>(null);
        public Task<LauncherUpdateResult> DownloadAndApplyAsync(IProgress<DownloadProgressReport>? progress = null, CancellationToken ct = default)
            => Task.FromResult(new LauncherUpdateResult(LauncherUpdateStatus.UpToDate, "OK"));
    }

    private class MockPackUpdateService : IPackUpdateService
    {
        public Task<PackUpdateResult> CheckAndApplyAsync(IProgress<DownloadProgressReport>? progress = null, CancellationToken ct = default, bool forceFullCheck = false)
            => Task.FromResult(new PackUpdateResult(PackUpdateStatus.UpToDate, "OK"));
    }

    [Fact]
    public void MainViewModel_AnthemProperties_InteractWithService()
    {
        var mockConfig = new MockConfigService();
        var mockAnthem = new MockAnthemService { VolumePercent = 80, IsMuted = false };
        var launchService = new TestLaunchService();
        var skinService = new MockSkinService();
        var updateService = new MockLauncherUpdateService();
        var packService = new MockPackUpdateService();

        var overviewVM = new OverviewViewModel(mockConfig, launchService);
        var settingsVM = new SettingsViewModel(mockConfig, skinService);
        var wardrobeVM = new WardrobeViewModel(skinService, mockConfig);

        var mainVM = new MainViewModel(
            mockConfig,
            updateService,
            packService,
            launchService,
            skinService,
            overviewVM,
            settingsVM,
            wardrobeVM,
            lobbyViewModel: null,
            serverListSyncService: null,
            anthemService: mockAnthem);

        Assert.Equal(80, mainVM.AnthemVolume);
        Assert.False(mainVM.IsAnthemMuted);
        Assert.Equal("Demo Artist · Demo Track", mainVM.CurrentTrackTitle);
        Assert.Equal("Demo Artist", mainVM.CurrentTrackArtist);
        Assert.Equal("Demo Track", mainVM.CurrentTrackSong);

        // Toggle mute
        mainVM.ToggleMuteCommand.Execute(null);
        Assert.True(mainVM.IsAnthemMuted);
        Assert.True(mockAnthem.IsMuted);

        // Change volume
        mainVM.AnthemVolume = 45;
        Assert.Equal(45, mockAnthem.VolumePercent);
    }

    private static string? FindProjectDirectory()
    {
        string current = AppContext.BaseDirectory;
        while (!string.IsNullOrEmpty(current))
        {
            if (File.Exists(Path.Combine(current, "AuraLauncher.csproj")))
            {
                return current;
            }
            var parent = Directory.GetParent(current);
            if (parent == null) break;
            current = parent.FullName;
        }
        return null;
    }
}

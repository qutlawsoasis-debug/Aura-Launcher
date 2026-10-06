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
        public bool Initialized { get; set; } = false;
        public bool Stopped { get; set; } = false;

        public void Initialize() => Initialized = true;
        public void PlayAnthem(bool force = false) => IsPlaying = true;
        public void ToggleMute() => IsMuted = !IsMuted;
        public void SetVolume(int volumePercent) => VolumePercent = Math.Clamp(volumePercent, 0, 100);
        public void Stop() { IsPlaying = false; Stopped = true; }
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
        public void EnsureCustomSkinLoaderConfig(string gameDir) { }
    }

    [Fact]
    public void AnthemFile_Exists_And_SizeUnder5MB()
    {
        string? projectDir = FindProjectDirectory();
        Assert.NotNull(projectDir);

        string anthemPath = Path.Combine(projectDir, "Resources", "Audio", "anthem.mp3");
        Assert.True(File.Exists(anthemPath), $"Anthem file must exist at {anthemPath}");

        var fileInfo = new FileInfo(anthemPath);
        Assert.True(fileInfo.Length > 0, "Anthem file must not be empty");
        Assert.True(fileInfo.Length <= 5 * 1024 * 1024, $"Anthem file must be under 5MB (was {fileInfo.Length} bytes)");
    }

    [Fact]
    public void ThirdPartyNotices_Contains_AnthemLicenseAndSource()
    {
        string? projectDir = FindProjectDirectory();
        Assert.NotNull(projectDir);

        string noticesPath = Path.Combine(projectDir, "THIRD-PARTY-NOTICES.txt");
        Assert.True(File.Exists(noticesPath));

        string content = File.ReadAllText(noticesPath);
        Assert.Contains("Ukrainian National Anthem", content);
        Assert.Contains("Public Domain", content);
        Assert.Contains("commons.wikimedia.org", content);
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
        public Task<PackUpdateResult> CheckAndApplyAsync(IProgress<DownloadProgressReport>? progress = null, CancellationToken ct = default)
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

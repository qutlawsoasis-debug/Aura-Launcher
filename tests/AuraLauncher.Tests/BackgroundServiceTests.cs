using System;
using System.IO.Packaging;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.ViewModels;
using Xunit;

namespace AuraLauncher.Tests;

public class BackgroundServiceTests
{
    public BackgroundServiceTests()
    {
        if (!UriParser.IsKnownScheme("pack"))
        {
            _ = PackUriHelper.UriSchemePack;
        }
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
    public void BackgroundService_Initialize_PopulatesAtLeast15Backgrounds()
    {
        var service = new BackgroundService();
        service.Initialize();

        Assert.True(service.TotalCount >= 15);
        Assert.True(service.AutoRotationEnabled);
    }

    [Fact]
    public void BackgroundService_AutoRotationToggle_UpdatesState()
    {
        var service = new BackgroundService();
        service.Initialize();

        service.AutoRotationEnabled = false;
        Assert.False(service.AutoRotationEnabled);

        service.AutoRotationEnabled = true;
        Assert.True(service.AutoRotationEnabled);
    }

    [Fact]
    public void SettingsViewModel_BackgroundCommands_WorkProperly()
    {
        var mockConfig = new TestConfigService("C:\\AuraTest");
        var mockSkin = new MockSkinService();
        var bgService = new BackgroundService();
        bgService.Initialize();

        var vm = new SettingsViewModel(mockConfig, mockSkin, null, null, bgService);

        Assert.True(vm.AutoRotateBackgrounds);

        vm.AutoRotateBackgrounds = false;
        Assert.False(bgService.AutoRotationEnabled);

        vm.NextBackgroundCommand.Execute(null);
        // Should execute without exception
    }
}

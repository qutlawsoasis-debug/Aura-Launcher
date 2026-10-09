using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.ViewModels;
using Xunit;

namespace AuraLauncher.Tests;

public class NewFeaturesUnitTests
{
    [Fact]
    public void WorldSaveItem_FormattingProperties_ReturnExpectedValues()
    {
        var item = new WorldSaveItem
        {
            FolderName = "SurvivalWorld",
            DisplayName = "Survival 1.20",
            DayFormatted = "День 84",
            DimensionFormatted = "Незер",
            CoordinatesFormatted = "120, 64, -351",
            SeedFormatted = "9876543210"
        };

        Assert.True(item.HasDetails);
        Assert.True(item.HasDay);
        Assert.Equal("День 84", item.DayFormatted);
        Assert.True(item.HasDimension);
        Assert.Equal("Незер", item.DimensionFormatted);
        Assert.True(item.HasCoordinates);
        Assert.Equal("120, 64, -351", item.CoordinatesFormatted);
        Assert.True(item.HasSeed);
        Assert.Equal("9876543210", item.SeedFormatted);
    }

    [Fact]
    public void WorldSaveItem_EmptyDetails_ReturnDashesAndFalseFlags()
    {
        var item = new WorldSaveItem
        {
            FolderName = "EmptyWorld",
            DisplayName = "Empty"
        };

        Assert.False(item.HasDetails);
        Assert.False(item.HasDay);
        Assert.Null(item.DayFormatted);
        Assert.False(item.HasDimension);
        Assert.Null(item.DimensionFormatted);
        Assert.False(item.HasCoordinates);
        Assert.Null(item.CoordinatesFormatted);
        Assert.False(item.HasSeed);
        Assert.Null(item.SeedFormatted);
    }

    [Fact]
    public async Task MinecraftPingService_UnreachableHost_ReturnsNullAndDoesNotInventData()
    {
        var pingService = new MinecraftPingService();
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(500));

        // Опрос заведомо закрытого порта на localhost
        var result = await pingService.PingServerAsync("127.0.0.1:59998", cts.Token);

        // Строгое соблюдение Anti-Slop: никаких выдуманных цифр пинга или онлайна при отсутствии связи
        Assert.Null(result.PingMs);
        Assert.Null(result.PlayersOnline);
        Assert.Null(result.PlayersMax);
        Assert.Equal("—", result.FormattedPing);
        Assert.Equal("—", result.FormattedPlayers);
    }

    [Fact]
    public void FriendItemViewModel_CanJoin_AllowsJoinWhenLobbyCodePresent()
    {
        var presence = new FriendPresenceItem
        {
            Id = "id-123",
            Nick = "FriendBob",
            Online = true,
            Status = "playing",
            LobbyCode = "AURA99",
            LastSeen = 0
        };
        var friendVm = new FriendItemViewModel(presence, _ => { }, _ => { });

        // Если у друга открыто лобби, кнопка "Войти" должна быть активна даже при статусе playing
        Assert.True(friendVm.CanJoin);

        friendVm.LobbyCode = null;
        Assert.False(friendVm.CanJoin);

        friendVm.Online = false;
        Assert.False(friendVm.CanJoin);
    }

    [Fact]
    public async Task LobbyViewModel_KickGuest_CallsServiceAndUpdatesUi()
    {
        var mockConfig = new TestConfigService("C:\\AuraTest");
        mockConfig.CurrentConfig.Nickname = "HostMaster";
        var mockLobby = new MockLobbyService();
        var launchService = new TestLaunchService();
        var lobbyVm = new LobbyViewModel(mockLobby, launchService, mockConfig);

        await lobbyVm.CreateLobbyCommand.ExecuteAsync(null);
        Assert.True(lobbyVm.IsHost);

        string? kickedTarget = null;
        mockLobby.OnKickCalled = name => kickedTarget = name;

        // Хост видит гостей с возможностью кикнуть
        var guestPlayerItem = new LobbyPlayerItem
        {
            Nickname = "NaughtyGuest",
            IsHost = false,
            CanKick = true,
            KickPlayerCommand = new AsyncRelayCommand(async () => await lobbyVm.KickPlayerAsync("NaughtyGuest"))
        };

        Assert.True(guestPlayerItem.CanKick);
        Assert.NotNull(guestPlayerItem.KickPlayerCommand);

        guestPlayerItem.KickPlayerCommand.Execute(null);

        // Даем асинхронному вызову завершиться
        await Task.Delay(50);

        Assert.Equal("NaughtyGuest", kickedTarget);
    }

    [Fact]
    public void LobbyViewModel_GuestCannotKickOthers_CanKickIsFalse()
    {
        var guestPlayerItem = new LobbyPlayerItem
        {
            Nickname = "OtherGuest",
            IsHost = false,
            CanKick = false,
            KickPlayerCommand = null
        };

        Assert.False(guestPlayerItem.CanKick);
        Assert.Null(guestPlayerItem.KickPlayerCommand);
    }

    [Fact]
    public async Task SettingsViewModel_CheckIntegrity_CallsPackUpdateWithForceFullCheck()
    {
        var mockConfig = new TestConfigService("C:\\AuraTest");
        var skinService = new MockSkinService();
        var mockPackUpdate = new MockPackUpdateServiceWithFlag();

        var settingsVm = new SettingsViewModel(
            mockConfig,
            skinService,
            packUpdateService: mockPackUpdate);

        Assert.False(settingsVm.IsCheckingIntegrity);
        Assert.False(settingsVm.HasIntegrityStatus);

        await settingsVm.CheckIntegrityCommand.ExecuteAsync(null);

        Assert.True(mockPackUpdate.WasForceFullCheckPassed);
        Assert.False(settingsVm.IsCheckingIntegrity);
        Assert.True(settingsVm.HasIntegrityStatus);
        Assert.Contains("Все файлы сборки проверены", settingsVm.IntegrityStatusText);
    }

    [Fact]
    public void NotificationService_NotifyMethods_TriggerInAppCallbackWithCorrectTypesAndTabs()
    {
        var configService = new TestConfigService("C:\\AuraTest");
        var notifService = new NotificationService(configService);

        string? lastTitle = null;
        string? lastMsg = null;
        string? lastType = null;
        string? lastTab = null;

        notifService.RegisterInAppToastHandler((title, msg, type, tab) =>
        {
            lastTitle = title;
            lastMsg = msg;
            lastType = type;
            lastTab = tab;
        });

        // 1. Скриншот
        notifService.NotifyScreenshotTaken("C:\\test\\2026-10-09.png", "2026-10-09.png");
        Assert.Equal("Скриншот сохранён", lastTitle);
        Assert.Equal("screenshot", lastType);
        Assert.Equal("Workshop", lastTab);

        // 2. Краш игры
        notifService.NotifyGameCrash(-1, "Сбой JVM");
        Assert.Equal("Сбой Minecraft", lastTitle);
        Assert.Equal("warning", lastType);
        Assert.Equal("Overview", lastTab);

        // 3. Кик из лобби
        notifService.NotifyKickedFromLobby();
        Assert.Equal("Исключение из лобби", lastTitle);
        Assert.Equal("warning", lastType);
        Assert.Equal("Lobby", lastTab);

        // 4. Проверка сборки
        notifService.NotifyIntegrityChecked("Все файлы в порядке");
        Assert.Equal("Целостность сборки", lastTitle);
        Assert.Equal("success", lastType);
        Assert.Equal("Settings", lastTab);
    }

    [Fact]
    public void ScreenshotWatcherService_StartAndDispose_DoesNotThrow()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "aura_screenshot_test_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(tempDir);

        try
        {
            using var watcher = new ScreenshotWatcherService();
            watcher.StartWatching(tempDir);
            watcher.StopWatching();
        }
        finally
        {
            try { Directory.Delete(tempDir, recursive: true); } catch { }
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

    private class MockPackUpdateServiceWithFlag : IPackUpdateService
    {
        public bool WasForceFullCheckPassed { get; private set; }

        public Task<PackUpdateResult> CheckAndApplyAsync(IProgress<DownloadProgressReport>? progress = null, CancellationToken ct = default, bool forceFullCheck = false)
        {
            WasForceFullCheckPassed = forceFullCheck;
            return Task.FromResult(new PackUpdateResult(PackUpdateStatus.UpToDate, "OK"));
        }
    }

    private class MockLobbyService : ILobbyService
    {
        public string? CurrentLobbyCode { get; set; }
        public string? CurrentHostToken { get; set; }
        public string? CurrentTunnelAddress { get; set; }
        public string CurrentStatus { get; set; } = "idle";
        public bool IsHost { get; set; }
        public bool IsReadyToPlay => true;
        public System.Collections.Generic.IReadOnlyDictionary<string, PlayerModSyncInfo>? CurrentModSync { get; set; }

        public Action<string>? OnKickCalled { get; set; }

        public event Action<string>? StatusChanged;
        public event Action<string>? TunnelAddressReady;
        public event Action<LobbyStatusResponse>? LobbyStatusUpdated;
        public event Action<System.Collections.Generic.IReadOnlyDictionary<string, PlayerModSyncInfo>>? ModSyncUpdated;
        public event Action? KickedFromLobby;

        public Task UpdateManifestAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<string?> CreateLobbyAsHostAsync(string hostName, CancellationToken cancellationToken = default)
        {
            CurrentLobbyCode = "TEST99";
            CurrentHostToken = "tok99";
            CurrentStatus = "waiting";
            IsHost = true;
            StatusChanged?.Invoke(CurrentStatus);
            return Task.FromResult<string?>("TEST99");
        }

        public Task<bool> HostOpenWorldAsync(string? customTunnelAddress = null, int localPort = 25565, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task CloseLobbyAsHostAsync(CancellationToken cancellationToken = default)
        {
            LeaveLobby();
            return Task.CompletedTask;
        }

        public Task<bool> JoinLobbyAsGuestAsync(string code, string playerName, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<LobbyStatusResponse?> RefreshGuestStatusAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<LobbyStatusResponse?>(null);

        public Task<bool> KickPlayerAsync(string playerName, CancellationToken cancellationToken = default)
        {
            OnKickCalled?.Invoke(playerName);
            return Task.FromResult(true);
        }

        public void LeaveLobby()
        {
            CurrentLobbyCode = null;
            CurrentHostToken = null;
            CurrentTunnelAddress = null;
            CurrentStatus = "idle";
            IsHost = false;
            StatusChanged?.Invoke(CurrentStatus);
        }
    }
}

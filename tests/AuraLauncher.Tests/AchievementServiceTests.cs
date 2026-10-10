using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using Xunit;

namespace AuraLauncher.Tests;

public class AchievementServiceTests
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

        public void TriggerConfigChanged()
        {
            ConfigChanged?.Invoke(this, CurrentConfig);
        }
    }

    private static (AchievementService Service, string TempPath) CreateTestService(MockConfigService configService)
    {
        string tempPath = Path.Combine(Path.GetTempPath(), $"aura_ach_{Guid.NewGuid():N}.json");
        var service = new AchievementService(configService, tempPath);
        return (service, tempPath);
    }

    [Fact]
    public void HundredHours_WithElevenHoursPlaytime_TracksProgressCorrectly()
    {
        var configService = new MockConfigService();
        configService.CurrentConfig.TotalPlayTimeSeconds = (11 * 3600) + (43 * 60); // 11h 43m
        configService.CurrentConfig.TotalGameLaunches = 14;

        var (service, tempPath) = CreateTestService(configService);
        try
        {
            service.Initialize();

            var progress = service.Progress;
            Assert.True(progress.ContainsKey("hundred_hours"));

            var hundredHoursProgress = progress["hundred_hours"];
            Assert.False(hundredHoursProgress.Unlocked);
            Assert.Equal(11.72, Math.Round(hundredHoursProgress.CurrentValue, 2));
            Assert.Equal(11, (int)hundredHoursProgress.CurrentValue);

            Assert.True(progress.ContainsKey("ten_launches"));
            Assert.True(progress["ten_launches"].Unlocked);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void HundredHours_ReachingTarget_UnlocksAchievement()
    {
        var configService = new MockConfigService();
        configService.CurrentConfig.TotalPlayTimeSeconds = 100 * 3600;

        var (service, tempPath) = CreateTestService(configService);
        try
        {
            service.Initialize();

            var progress = service.Progress;
            Assert.True(progress.ContainsKey("hundred_hours"));
            Assert.True(progress["hundred_hours"].Unlocked);
            Assert.True(service.IsUnlocked("hundred_hours"));
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void IncrementalReports_AdvanceProgressWithoutUnlockingUntilTarget()
    {
        var configService = new MockConfigService();
        var (service, tempPath) = CreateTestService(configService);
        try
        {
            service.Initialize();

            service.Report("friends_count", 3);
            var progress = service.Progress;

            Assert.True(progress.ContainsKey("first_friend"));
            Assert.True(progress["first_friend"].Unlocked);

            Assert.True(progress.ContainsKey("five_friends"));
            Assert.False(progress["five_friends"].Unlocked);
            Assert.Equal(3, progress["five_friends"].CurrentValue);

            service.Report("friends_count", 5);
            progress = service.Progress;
            Assert.True(progress["five_friends"].Unlocked);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }

    [Fact]
    public void ConfigChanged_AutomaticallySyncsPlaytimeProgress()
    {
        var configService = new MockConfigService();
        var (service, tempPath) = CreateTestService(configService);
        try
        {
            service.Initialize();

            configService.CurrentConfig.TotalPlayTimeSeconds = 5 * 3600;
            configService.TriggerConfigChanged();

            var progress = service.Progress;
            Assert.True(progress.ContainsKey("hundred_hours"));
            Assert.Equal(5.0, progress["hundred_hours"].CurrentValue);
            Assert.False(progress["hundred_hours"].Unlocked);
        }
        finally
        {
            if (File.Exists(tempPath)) File.Delete(tempPath);
        }
    }
}

using System;
using System.Collections.Generic;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface IAchievementService
{
    IReadOnlyList<AchievementDefinition> Definitions { get; }
    IReadOnlyDictionary<string, AchievementProgress> Progress { get; }

    int UnlockedCount { get; }
    int TotalCount { get; }
    (string Id, string Title, DateTime UnlockedAtUtc)? LatestUnlocked { get; }

    event EventHandler<AchievementDefinition>? AchievementUnlocked;

    void Initialize();
    void Report(string eventId, double value = 1.0);
    bool IsUnlocked(string id);

#if DEBUG
    void DebugUnlock(string id);
#endif
}

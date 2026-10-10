using System;
using System.Text.Json.Serialization;

namespace AuraLauncher.Models;

public class AchievementDefinition
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("title")]
    public string Title { get; set; } = string.Empty;

    [JsonPropertyName("description")]
    public string Description { get; set; } = string.Empty;

    [JsonPropertyName("target")]
    public int Target { get; set; } = 1;
}

public class AchievementProgress
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = string.Empty;

    [JsonPropertyName("currentValue")]
    public double CurrentValue { get; set; }

    [JsonPropertyName("unlocked")]
    public bool Unlocked { get; set; }

    [JsonPropertyName("unlockedAtUtc")]
    public DateTime? UnlockedAtUtc { get; set; }
}

public class AchievementsData
{
    [JsonPropertyName("progress")]
    public System.Collections.Generic.Dictionary<string, AchievementProgress> Progress { get; set; } = new();
}

public class AchievementDisplayItem
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public int Target { get; set; } = 1;
    public double CurrentValue { get; set; }
    public bool IsUnlocked { get; set; }
    public DateTime? UnlockedAtUtc { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public double ProgressRatio { get; set; }
    public string IconKey { get; set; } = "IconCrown";
}

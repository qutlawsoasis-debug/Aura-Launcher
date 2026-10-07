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

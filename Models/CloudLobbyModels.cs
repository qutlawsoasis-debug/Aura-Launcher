using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AuraLauncher.Models;

public record CloudWorldItem(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("owner")] string Owner,
    [property: JsonPropertyName("seed")] string? Seed,
    [property: JsonPropertyName("allowed_players")] List<string>? AllowedPlayers,
    [property: JsonPropertyName("created_at")] double CreatedAt,
    [property: JsonPropertyName("last_played")] double LastPlayed,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("port")] int? Port,
    [property: JsonPropertyName("online_players")] List<string>? OnlinePlayers,
    [property: JsonPropertyName("max_players")] int MaxPlayers = 8,
    [property: JsonPropertyName("is_owner")] bool IsOwner = false,
    [property: JsonPropertyName("stageText")] string? StageText = null,
    [property: JsonPropertyName("progress")] int Progress = 0
)
{
    [JsonIgnore]
    public bool IsRunning => string.Equals(Status, "running", System.StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsStarting => string.Equals(Status, "starting", System.StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsStopping => string.Equals(Status, "stopping", System.StringComparison.OrdinalIgnoreCase);

    [JsonIgnore]
    public bool IsActive => IsRunning || IsStarting || IsStopping;

    [JsonIgnore]
    public int OnlineCount => OnlinePlayers?.Count ?? 0;

    [JsonIgnore]
    public string FormattedPlayers => $"{OnlineCount}/{MaxPlayers}";

    [JsonIgnore]
    public string DisplayAddress => Port.HasValue ? $"89.168.82.200:{Port.Value}" : string.Empty;
}

public record CloudWorldsResponse(
    [property: JsonPropertyName("max_active")] int MaxActive,
    [property: JsonPropertyName("active_count")] int ActiveCount,
    [property: JsonPropertyName("worlds")] List<CloudWorldItem> Worlds
);

public record CloudLobbyCreateResponse(
    [property: JsonPropertyName("ip")] string? Ip,
    [property: JsonPropertyName("port")] int? Port,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("worldId")] string? WorldId,
    [property: JsonPropertyName("name")] string? Name,
    [property: JsonPropertyName("owner")] string? Owner,
    [property: JsonPropertyName("error")] string? Error
);

public record CloudWorldShareResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("allowed_players")] List<string>? AllowedPlayers,
    [property: JsonPropertyName("error")] string? Error
);

public record CloudWorldDeleteResponse(
    [property: JsonPropertyName("success")] bool Success,
    [property: JsonPropertyName("message")] string? Message,
    [property: JsonPropertyName("error")] string? Error = null
);


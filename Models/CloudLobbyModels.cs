using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace AuraLauncher.Models;

public record CloudLobbyItem(
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("owner")] string Owner,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("created_at")] double CreatedAt,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("online_players")] List<string>? OnlinePlayers = null
)
{
    [JsonIgnore]
    public string DisplayAddress => $"89.168.82.200:{Port}";

    [JsonIgnore]
    public int PlayerCount => OnlinePlayers?.Count ?? 0;

    [JsonIgnore]
    public string FormattedPlayers => $"{PlayerCount} игрок{(PlayerCount == 1 ? "" : (PlayerCount >= 2 && PlayerCount <= 4 ? "а" : "ов"))}";
}

public record CloudLobbiesListResponse(
    [property: JsonPropertyName("max_active")] int MaxActive,
    [property: JsonPropertyName("active_count")] int ActiveCount,
    [property: JsonPropertyName("lobbies")] List<CloudLobbyItem> Lobbies
);

public record CloudLobbyCreateResponse(
    [property: JsonPropertyName("ip")] string? Ip,
    [property: JsonPropertyName("port")] int? Port,
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("owner")] string? Owner,
    [property: JsonPropertyName("lobbyName")] string? LobbyName,
    [property: JsonPropertyName("error")] string? Error
);

public record CloudLobbyStopResponse(
    [property: JsonPropertyName("status")] string Status,
    [property: JsonPropertyName("port")] int? Port,
    [property: JsonPropertyName("error")] string? Error
);

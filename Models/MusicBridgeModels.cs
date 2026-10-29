using System;
using System.Text.Json.Serialization;

namespace AuraLauncher.Models;

public record MusicBridgeConfig(
    [property: JsonPropertyName("port")] int Port,
    [property: JsonPropertyName("token")] string Token
);

public record MusicBridgeTrack(
    [property: JsonPropertyName("id")] string Id,
    [property: JsonPropertyName("artist")] string Artist,
    [property: JsonPropertyName("title")] string Title,
    [property: JsonPropertyName("durationMs")] long DurationMs,
    [property: JsonPropertyName("coverUrl")] string? CoverUrl
);

public record MusicBridgeNowResponse(
    [property: JsonPropertyName("state")] string State, // "playing" | "paused" | "stopped"
    [property: JsonPropertyName("track")] MusicBridgeTrack? Track,
    [property: JsonPropertyName("positionMs")] long PositionMs,
    [property: JsonPropertyName("updatedAtUnixMs")] long UpdatedAtUnixMs,
    [property: JsonPropertyName("volume")] double Volume
);

public record MusicBridgeSeekRequest(
    [property: JsonPropertyName("ms")] long? Ms,
    [property: JsonPropertyName("positionMs")] long? PositionMs,
    [property: JsonPropertyName("seconds")] double? Seconds
);

public record MusicBridgeVolumeRequest(
    [property: JsonPropertyName("volume")] double? Volume,
    [property: JsonPropertyName("value")] double? Value
);

public record MusicBridgeQueueRequest(
    [property: JsonPropertyName("trackId")] string? TrackId,
    [property: JsonPropertyName("id")] string? Id,
    [property: JsonPropertyName("title")] string? Title,
    [property: JsonPropertyName("artist")] string? Artist,
    [property: JsonPropertyName("coverUrl")] string? CoverUrl,
    [property: JsonPropertyName("durationMs")] long? DurationMs
);

public record MusicResolveResult(
    [property: JsonPropertyName("videoId")] string? VideoId,
    [property: JsonPropertyName("error")] string? Error
);

using System.Text.Json.Serialization;

namespace AuraLauncher.Core.WebBridge;

public class BridgeMessage
{
    [JsonPropertyName("id")]
    public string? Id { get; set; }

    [JsonPropertyName("type")]
    public string Type { get; set; } = "command"; // "command", "event", "response"

    [JsonPropertyName("name")]
    public string Name { get; set; } = string.Empty;

    [JsonPropertyName("payload")]
    public object? Payload { get; set; }

    [JsonPropertyName("error")]
    public string? Error { get; set; }
}

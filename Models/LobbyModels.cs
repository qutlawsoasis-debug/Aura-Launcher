namespace AuraLauncher.Models;

public record LobbyCreateResponse(string Code, string HostToken, string Status, long CreatedAt);
public record LobbyJoinResponse(bool Success, string Code, string Status, string? TunnelAddress, int PlayerCount);
public record LobbyStatusResponse(string Code, string Status, string? TunnelAddress, int PlayerCount, long LastHeartbeat, string[]? Players = null, string? HostName = null);
public record TunnelConfigResponse(string? Secret, string? PublicAddress, int? PublicPort);

public class LobbyPlayerItem
{
    public string Nickname { get; set; } = "";
    public bool IsHost { get; set; }
    public System.Windows.Media.ImageSource? Avatar { get; set; }
}

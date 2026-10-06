namespace AuraLauncher.Models;

public record LobbyCreateResponse(string Code, string HostToken, string Status, long CreatedAt);
public record LobbyJoinResponse(bool Success, string Code, string Status, string? TunnelAddress, int PlayerCount);
public record LobbyStatusResponse(string Code, string Status, string? TunnelAddress, int PlayerCount, long LastHeartbeat);
public record TunnelConfigResponse(string? Secret, string? PublicAddress, int? PublicPort);

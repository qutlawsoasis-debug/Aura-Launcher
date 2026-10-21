using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public enum LobbySessionRole
{
    None,
    Host,
    Guest
}

public enum LobbySessionState
{
    Idle,
    Creating,
    WaitingForWorld,
    TunnelStarting,
    Active,
    Joining,
    ReadyToPlay,
    ConnectingToGame,
    Error
}

public record LobbyPlayerItemDto(
    string Nickname,
    bool IsHost,
    bool IsReady = true,
    string? Status = null
);

public record LobbyStatusDto(
    string Code,
    LobbySessionRole Role,
    LobbySessionState State,
    string? HostName,
    string? TunnelAddress,
    int? PingMs,
    IReadOnlyList<LobbyPlayerItemDto> Players,
    string? StatusHint = null
);

public interface ILobbySessionService : IDisposable
{
    LobbySessionRole Role { get; }
    LobbySessionState State { get; }
    string? CurrentLobbyCode { get; }
    string? CurrentHostToken { get; }
    string? CurrentTunnelAddress { get; }
    int? CurrentTunnelPingMs { get; }
    string? HostNick { get; }
    string? LastError { get; }
    string? StatusHint { get; }
    bool HasPersonalTunnel { get; }
    IReadOnlyList<LobbyPlayerItemDto> Players { get; }
    IReadOnlyDictionary<string, PlayerModSyncInfo>? ModSync { get; }

    event EventHandler<LobbySessionState>? StateChanged;
    event EventHandler<LobbyStatusDto>? LobbyUpdated;
    event EventHandler<string>? TunnelAddressReady;
    event EventHandler<int>? TunnelPingUpdated;
    event EventHandler<string>? PlayerKicked;
    event EventHandler<string>? ErrorOccurred;

    Task<string?> CreateLobbyAsync(string hostName, CancellationToken cancellationToken = default);
    Task<string?> ClaimPersonalTunnelAsync(Action<string>? onBrowserUrl = null, CancellationToken cancellationToken = default);
    Task<bool> JoinByCodeAsync(string code, string guestNick, CancellationToken cancellationToken = default);
    Task OpenWorldAsync(int? localPort = null, CancellationToken cancellationToken = default);
    Task ConnectToGameAsync(CancellationToken cancellationToken = default);
    Task<bool> KickPlayerAsync(string playerNick, CancellationToken cancellationToken = default);
    Task LeaveLobbyAsync(CancellationToken cancellationToken = default);
}

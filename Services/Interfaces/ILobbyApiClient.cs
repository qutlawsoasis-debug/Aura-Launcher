using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface ILobbyApiClient
{
    Task<LobbyCreateResponse?> CreateLobbyAsync(string hostName, IReadOnlyList<ModManifestEntry>? manifest = null, CancellationToken cancellationToken = default);
    Task<LobbyJoinResponse?> JoinLobbyAsync(string code, string playerName, IReadOnlyList<ModManifestEntry>? manifest = null, CancellationToken cancellationToken = default);
    Task<bool> UpdateManifestAsync(string code, string playerName, IReadOnlyList<ModManifestEntry> manifest, CancellationToken cancellationToken = default);
    Task<LobbyStatusResponse?> GetStatusAsync(string code, string? playerName = null, CancellationToken cancellationToken = default);
    Task<(System.Net.HttpStatusCode? StatusCode, LobbyStatusResponse? Response, string RawBody)> GetStatusDetailedAsync(string code, string? playerName = null, CancellationToken cancellationToken = default);
    Task<bool> OpenLobbyAsync(string code, string hostToken, string tunnelAddress, CancellationToken cancellationToken = default);
    Task<bool> HeartbeatAsync(string code, string hostToken, CancellationToken cancellationToken = default);
    Task<bool> CloseLobbyAsync(string code, string hostToken, CancellationToken cancellationToken = default);
    Task<bool> LeaveLobbyAsync(string code, string playerName, CancellationToken cancellationToken = default);
    Task<bool> KickPlayerAsync(string code, string hostToken, string playerName, CancellationToken cancellationToken = default);
    Task<TunnelConfigResponse?> GetTunnelConfigAsync(CancellationToken cancellationToken = default);
}

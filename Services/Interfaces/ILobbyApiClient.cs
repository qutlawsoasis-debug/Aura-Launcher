using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface ILobbyApiClient
{
    Task<LobbyCreateResponse?> CreateLobbyAsync(string hostName, CancellationToken cancellationToken = default);
    Task<LobbyJoinResponse?> JoinLobbyAsync(string code, string playerName, CancellationToken cancellationToken = default);
    Task<LobbyStatusResponse?> GetStatusAsync(string code, CancellationToken cancellationToken = default);
    Task<(System.Net.HttpStatusCode? StatusCode, LobbyStatusResponse? Response, string RawBody)> GetStatusDetailedAsync(string code, CancellationToken cancellationToken = default);
    Task<bool> OpenLobbyAsync(string code, string hostToken, string tunnelAddress, CancellationToken cancellationToken = default);
    Task<bool> HeartbeatAsync(string code, string hostToken, CancellationToken cancellationToken = default);
    Task<bool> CloseLobbyAsync(string code, string hostToken, CancellationToken cancellationToken = default);
    Task<bool> LeaveLobbyAsync(string code, string playerName, CancellationToken cancellationToken = default);
    Task<TunnelConfigResponse?> GetTunnelConfigAsync(CancellationToken cancellationToken = default);
}

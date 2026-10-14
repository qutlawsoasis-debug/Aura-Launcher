using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface ICloudLobbyService
{
    string ServerHost { get; }
    int MainServerPort { get; }
    string MainServerAddress { get; }

    Task<ServerPingResult> PingMainServerAsync(CancellationToken cancellationToken = default);
    Task<CloudLobbiesListResponse?> GetCloudLobbiesAsync(CancellationToken cancellationToken = default);
    Task<CloudLobbyCreateResponse?> CreateCloudLobbyAsync(string owner, string lobbyName, int maxPlayers = 8, CancellationToken cancellationToken = default);
    Task<bool> StopCloudLobbyAsync(int port, CancellationToken cancellationToken = default);
    Task<ServerPingResult> PingLobbyAsync(int port, CancellationToken cancellationToken = default);
}

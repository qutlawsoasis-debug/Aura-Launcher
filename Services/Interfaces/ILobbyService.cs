using System;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface ILobbyService
{
    string? CurrentLobbyCode { get; }
    string? CurrentHostToken { get; }
    string? CurrentTunnelAddress { get; }
    string CurrentStatus { get; } // "idle", "waiting", "open", "closed"
    bool IsHost { get; }
    bool IsReadyToPlay { get; }

    IReadOnlyDictionary<string, PlayerModSyncInfo>? CurrentModSync { get; }

    event Action<string>? StatusChanged;
    event Action<string>? TunnelAddressReady;
    event Action<LobbyStatusResponse>? LobbyStatusUpdated;
    event Action<IReadOnlyDictionary<string, PlayerModSyncInfo>>? ModSyncUpdated;

    Task<string?> CreateLobbyAsHostAsync(string hostName, CancellationToken cancellationToken = default);
    Task<bool> HostOpenWorldAsync(string? customTunnelAddress = null, int localPort = 25565, CancellationToken cancellationToken = default);
    Task CloseLobbyAsHostAsync(CancellationToken cancellationToken = default);

    Task<bool> JoinLobbyAsGuestAsync(string code, string playerName, CancellationToken cancellationToken = default);
    Task<LobbyStatusResponse?> RefreshGuestStatusAsync(CancellationToken cancellationToken = default);
    Task UpdateManifestAsync(CancellationToken cancellationToken = default);
    void LeaveLobby();
}

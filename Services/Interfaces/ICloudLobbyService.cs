using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface ICloudLobbyService
{
    string ServerHost { get; }

    Task<CloudWorldsResponse?> GetWorldsAsync(string playerName, CancellationToken cancellationToken = default);
    Task<CloudLobbyCreateResponse?> CreateOrStartWorldAsync(string owner, string worldName, string? seed = null, string? worldId = null, CancellationToken cancellationToken = default);
    Task<bool> StopWorldAsync(string? worldId = null, int? port = null, CancellationToken cancellationToken = default);
    Task<CloudWorldShareResponse?> ShareWorldAsync(string owner, string worldId, string targetPlayer, string action = "add", CancellationToken cancellationToken = default);
    Task<CloudWorldDeleteResponse?> DeleteWorldAsync(string owner, string worldId, CancellationToken cancellationToken = default);
    Task<CloudWorldItem?> UploadWorldAsync(string owner, string worldName, string zipFilePath, CancellationToken cancellationToken = default);
}

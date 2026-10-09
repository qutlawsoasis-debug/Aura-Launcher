using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

/// <summary>
/// Сервис синхронизации управляемых серверов сборки в servers.dat Minecraft.
/// </summary>
public interface IServerListSyncService
{
    Task SyncServersAsync(
        string gameDir,
        IReadOnlyList<ManifestServerEntry>? manifestServers,
        string? customStateFilePath = null,
        CancellationToken cancellationToken = default);

    Task UpsertActiveLobbyServerAsync(
        string gameDir,
        string tunnelAddress,
        string? hostName = null,
        string? lobbyCode = null,
        CancellationToken cancellationToken = default);

    Task RemoveActiveLobbyServerAsync(
        string gameDir,
        CancellationToken cancellationToken = default);
}

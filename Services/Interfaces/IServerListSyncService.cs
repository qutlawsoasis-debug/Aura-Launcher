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
}

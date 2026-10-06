using System;
using System.Threading;
using System.Threading.Tasks;

namespace AuraLauncher.Services.Interfaces;

public enum TunnelStatus
{
    Inactive,
    Starting,
    Active,
    Failed
}

public record TunnelInfo(
    string? PublicAddress,
    int? PublicPort,
    TunnelStatus Status,
    string? ErrorMessage = null
);

/// <summary>
/// Абстракция провайдера туннеля (playit, fake и др.).
/// </summary>
public interface ITunnelProvider : IDisposable
{
    TunnelInfo CurrentInfo { get; }
    bool IsProcessRunning { get; }
    event Action<TunnelInfo>? StatusChanged;

    Task<TunnelInfo> StartAsync(int localPort, CancellationToken ct = default);
    Task StopAsync(CancellationToken ct = default);
}

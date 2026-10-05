using System;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Тестовая реализация туннеля для изоляции логики и тестов.
/// </summary>
public class FakeTunnelProvider : ITunnelProvider
{
    private TunnelInfo _currentInfo = new(null, null, TunnelStatus.Inactive);

    public TunnelInfo CurrentInfo => _currentInfo;

    public event Action<TunnelInfo>? StatusChanged;

    public string DefaultHost { get; set; } = 
        string.Equals(Environment.GetEnvironmentVariable("AURA_FAKE_TUNNEL"), "1", StringComparison.OrdinalIgnoreCase) ||
        string.Equals(Environment.GetEnvironmentVariable("UseFakeTunnel"), "true", StringComparison.OrdinalIgnoreCase)
            ? "127.0.0.1"
            : "aura-lobby.craft.ply.gg";
    public int DefaultPublicPort { get; set; } = 25565;
    public bool ShouldFail { get; set; }
    public string FailureMessage { get; set; } = "Connection timed out";

    public Task<TunnelInfo> StartAsync(int localPort, CancellationToken ct = default)
    {
        if (ShouldFail)
        {
            _currentInfo = new TunnelInfo(null, null, TunnelStatus.Failed, FailureMessage);
            StatusChanged?.Invoke(_currentInfo);
            return Task.FromResult(_currentInfo);
        }

        _currentInfo = new TunnelInfo(DefaultHost, DefaultPublicPort, TunnelStatus.Active);
        StatusChanged?.Invoke(_currentInfo);
        return Task.FromResult(_currentInfo);
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _currentInfo = new TunnelInfo(null, null, TunnelStatus.Inactive);
        StatusChanged?.Invoke(_currentInfo);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _currentInfo = new TunnelInfo(null, null, TunnelStatus.Inactive);
    }
}

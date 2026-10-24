using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AuraLauncher.Services.Interfaces;

public interface IPrewarmGameService
{
    bool IsPrewarmedReady { get; }
    Process? PrewarmedProcess { get; }
    Task StartPrewarmAsync(CancellationToken cancellationToken = default);
    Task RestartPrewarmAsync(CancellationToken cancellationToken = default);
    Task<Process?> ActivateAsync(string? quickPlayMultiplayer = null, string? quickPlaySingleplayer = null);
    void StopPrewarm();
}

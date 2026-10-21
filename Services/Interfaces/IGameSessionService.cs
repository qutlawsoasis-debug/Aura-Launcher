using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace AuraLauncher.Services.Interfaces;

public enum GameSessionState
{
    Idle,
    Preparing,
    Launching,
    Running,
    Failed
}

public class LaunchOptions
{
    public string? QuickPlayMultiplayer { get; set; }
    public string? QuickPlaySingleplayer { get; set; }
}

public class LaunchProgress
{
    public string Stage { get; set; } = string.Empty;
    public double Percentage { get; set; }
    public string DetailText { get; set; } = string.Empty;
    public string FormattedSpeed { get; set; } = string.Empty;
    public string FormattedRemainingTime { get; set; } = string.Empty;
}

public class GameExitedEventArgs : EventArgs
{
    public int ExitCode { get; }
    public string? LogPath { get; }

    public GameExitedEventArgs(int exitCode, string? logPath = null)
    {
        ExitCode = exitCode;
        LogPath = logPath;
    }
}

public interface IGameSessionService
{
    GameSessionState State { get; }
    bool IsGameRunning { get; }
    Process? CurrentProcess { get; }

    event EventHandler<GameSessionState>? StateChanged;
    event EventHandler<string>? LogReceived;
    event EventHandler<GameExitedEventArgs>? GameExited;
    event EventHandler<Process>? GameStarted;

    Task<Process> LaunchAsync(
        LaunchOptions? options = null,
        IProgress<LaunchProgress>? progress = null,
        CancellationToken cancellationToken = default);

    Task<bool> StopAsync(int timeoutMs = 5000);
}

using System;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public enum LauncherUpdateStatus
{
    NotInstalled,
    UpToDate,
    UpdatedRestarting,
    Skipped,
    Failed
}

public class LauncherUpdateResult
{
    public LauncherUpdateStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public string? NewVersion { get; set; }

    public LauncherUpdateResult() { }

    public LauncherUpdateResult(LauncherUpdateStatus status, string message, string? newVersion = null)
    {
        Status = status;
        Message = message;
        NewVersion = newVersion;
    }
}

public interface ILauncherUpdateService
{
    bool IsInstalled { get; }
    string CurrentVersion { get; }
    Task<LauncherUpdateResult> CheckAndApplyAsync(IProgress<DownloadProgressReport>? progress = null, CancellationToken ct = default);
}

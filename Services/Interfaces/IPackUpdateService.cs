using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public enum PackUpdateStatus
{
    Updated,
    UpToDate,
    OfflineContinue,
    Failed
}

public class PackUpdateResult
{
    public PackUpdateStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public int FilesChanged { get; set; }
    public TimeSpan? Duration { get; set; }
    public IReadOnlyList<ManifestServerEntry>? Servers { get; set; }

    public PackUpdateResult() { }

    public PackUpdateResult(PackUpdateStatus status, string message, int filesChanged = 0, TimeSpan? duration = null)
    {
        Status = status;
        Message = message;
        FilesChanged = filesChanged;
        Duration = duration;
    }
}

public interface IPackUpdateService
{
    Task<PackUpdateResult> CheckAndApplyAsync(IProgress<DownloadProgressReport>? progress = null, CancellationToken ct = default, bool forceFullCheck = false);
}

using System;

namespace AuraLauncher.Models;

/// <summary>
/// POCO-модель отчета о прогрессе скачивания/обновления файлов.
/// </summary>
public class DownloadProgressReport
{
    public double Percentage { get; set; }
    public double SpeedMBs { get; set; }
    public TimeSpan? RemainingTime { get; set; }
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string CurrentFileName { get; set; } = string.Empty;

    public string FormattedSpeed => $"{SpeedMBs:F2} MB/s";
    public string FormattedRemainingTime => RemainingTime.HasValue 
        ? (RemainingTime.Value.TotalMinutes >= 1 
            ? $"{(int)RemainingTime.Value.TotalMinutes} мин {RemainingTime.Value.Seconds} сек" 
            : $"{RemainingTime.Value.Seconds} сек")
        : "--";
}

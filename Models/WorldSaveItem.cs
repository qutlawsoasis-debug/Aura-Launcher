using System;
using System.Collections.Generic;
using System.Windows.Media;
using AuraLauncher.Core;

namespace AuraLauncher.Models;

public class BackupTickItem
{
    public bool HasBackup { get; set; }
    public double Height => HasBackup ? 18.0 : 8.0;
}

public class WorldBackupItem : ObservableObject
{
    private bool _isConfirmingRestore;
    private bool _isConfirmingDelete;

    public string FileName { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public string SizeFormatted { get; set; } = string.Empty;

    public bool IsConfirmingRestore
    {
        get => _isConfirmingRestore;
        set => SetProperty(ref _isConfirmingRestore, value);
    }

    public bool IsConfirmingDelete
    {
        get => _isConfirmingDelete;
        set => SetProperty(ref _isConfirmingDelete, value);
    }
}

public class WorldSaveItem : ObservableObject
{
    private bool _isBackingUp;
    private string _backupStatusText = string.Empty;

    public string FolderName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FolderPath { get; set; } = string.Empty;
    public DateTime LastPlayed { get; set; } = DateTime.MinValue;
    public string SizeFormatted { get; set; } = string.Empty;
    public ImageSource? IconSource { get; set; }
    public string GameMode { get; set; } = "Выживание";

    public string PlaytimeFormatted { get; set; } = string.Empty;
    public bool HasPlaytime => !string.IsNullOrWhiteSpace(PlaytimeFormatted);

    public int BackupsCount { get; set; }
    public bool HasBackups => BackupsCount > 0;
    public string LastBackupText { get; set; } = "бэкапов нет";

    public List<BackupTickItem> BackupTicks { get; set; } = new();
    public List<WorldBackupItem> Backups { get; set; } = new();

    public string LastPlayedFormatted
    {
        get
        {
            if (LastPlayed == DateTime.MinValue) return string.Empty;
            if (LastPlayed.Date == DateTime.Today)
                return $"Играли сегодня в {LastPlayed:HH:mm}";
            if (LastPlayed.Date == DateTime.Today.AddDays(-1))
                return $"Играли вчера в {LastPlayed:HH:mm}";
            return $"Играли {LastPlayed:d MMM в HH:mm}";
        }
    }

    public bool IsBackingUp
    {
        get => _isBackingUp;
        set => SetProperty(ref _isBackingUp, value);
    }

    public string BackupStatusText
    {
        get => _backupStatusText;
        set => SetProperty(ref _backupStatusText, value);
    }
}

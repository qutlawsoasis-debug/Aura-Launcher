using System;
using System.Windows.Media;
using AuraLauncher.Core;

namespace AuraLauncher.Models;

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

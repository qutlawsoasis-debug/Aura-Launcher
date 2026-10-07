using AuraLauncher.Core;

namespace AuraLauncher.Models;

public class ModItem : ObservableObject
{
    private bool _isEnabled;

    public string FileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public string SizeFormatted { get; set; } = string.Empty;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}

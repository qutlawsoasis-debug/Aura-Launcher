using AuraLauncher.Core;

namespace AuraLauncher.Models;

public class ShaderPackItem : ObservableObject
{
    private bool _isActive;

    public string Name { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;

    public bool IsActive
    {
        get => _isActive;
        set => SetProperty(ref _isActive, value);
    }
}

namespace AuraLauncher.Models;

/// <summary>
/// POCO-модель установленного или доступного мода в сборке.
/// </summary>
public class ModInfo
{
    public string Name { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string FileName { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public bool IsEnabled { get; set; } = true;
}

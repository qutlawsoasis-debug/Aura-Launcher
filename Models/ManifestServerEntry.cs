namespace AuraLauncher.Models;

/// <summary>
/// Запись управляемого сервера из manifest.json сборки.
/// </summary>
public class ManifestServerEntry
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Address { get; set; } = string.Empty;
}

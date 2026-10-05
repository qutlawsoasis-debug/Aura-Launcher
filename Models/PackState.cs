using System;
using System.IO;
using System.Text.Json;

namespace AuraLauncher.Models;

/// <summary>
/// Состояние установленной сборки (pack-state.json).
/// </summary>
public class PackState
{
    public string PackVersion { get; set; } = string.Empty;
    public string GameDir { get; set; } = string.Empty;
    public string Minecraft { get; set; } = string.Empty;
    public string FabricLoader { get; set; } = string.Empty;
    public DateTime InstalledAtUtc { get; set; }
    public List<string> ManagedServers { get; set; } = new();

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    public static string GetDefaultStateFilePath()
    {
        var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        return Path.Combine(appData, "Aura", "pack-state.json");
    }

    /// <summary>
    /// Загружает состояние сборки и проверяет, что оно соответствует указанной папке игры.
    /// Если файл отсутствует, поврежден или папка игры не совпадает, возвращает null (сборка не установлена).
    /// </summary>
    public static PackState? LoadValidState(string currentGameDir, string? customStatePath = null)
    {
        if (string.IsNullOrWhiteSpace(currentGameDir))
            return null;

        var statePath = customStatePath ?? GetDefaultStateFilePath();
        if (!File.Exists(statePath))
            return null;

        try
        {
            var json = File.ReadAllText(statePath);
            var state = JsonSerializer.Deserialize<PackState>(json, JsonOptions);
            if (state == null)
                return null;

            // Проверка соответствия gameDir
            var normalizedCurrent = Path.GetFullPath(currentGameDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var normalizedState = Path.GetFullPath(state.GameDir).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

            if (!string.Equals(normalizedCurrent, normalizedState, StringComparison.OrdinalIgnoreCase))
                return null;

            return state;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Атомарно сохраняет состояние сборки через временный файл .tmp и последующий File.Move.
    /// </summary>
    public static void SaveState(PackState state, string? customStatePath = null)
    {
        ArgumentNullException.ThrowIfNull(state);

        var statePath = customStatePath ?? GetDefaultStateFilePath();
        var dir = Path.GetDirectoryName(statePath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
        {
            Directory.CreateDirectory(dir);
        }

        var json = JsonSerializer.Serialize(state, JsonOptions);
        var tmpPath = statePath + $".{Guid.NewGuid():N}.tmp";

        try
        {
            File.WriteAllText(tmpPath, json);
            File.Move(tmpPath, statePath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); } catch { }
            }
        }
    }
}

using System;

namespace AuraLauncher.Models;

/// <summary>
/// Определение поддерживаемых версий Minecraft и метаданных сборки Fabric.
/// </summary>
public static class GameVersionProfiles
{
    public const string Version120 = "1.20.1";
    public const string Version121 = "1.21.1";

    public const string DefaultFabric120 = "0.19.5";
    public const string DefaultFabric121 = "0.19.5";

    public static readonly string[] SupportedVersions = new[] { Version120, Version121 };

    /// <summary>
    /// Нормализация версии: если не 1.21.1, то дефолт 1.20.1.
    /// </summary>
    public static string NormalizeVersion(string? version)
    {
        if (string.Equals(version?.Trim(), Version121, StringComparison.OrdinalIgnoreCase))
        {
            return Version121;
        }
        return Version120;
    }

    /// <summary>
    /// Имя файла манифеста в репозитории Aura-Pack.
    /// 1.20.1 использует manifest.json для обратной совместимости, 1.21.1 — manifest-1.21.1.json.
    /// </summary>
    public static string GetManifestFileName(string? version)
    {
        return NormalizeVersion(version) == Version121 ? "manifest-1.21.1.json" : "manifest.json";
    }

    /// <summary>
    /// Дефолтная версия загрузчика Fabric для указанной версии Minecraft.
    /// </summary>
    public static string GetDefaultFabricLoader(string? version)
    {
        return NormalizeVersion(version) == Version121 ? DefaultFabric121 : DefaultFabric120;
    }

    /// <summary>
    /// Минимальная мажорная версия Java, требуемая для запуска (Java 17 для 1.20.1, Java 21 для 1.21.1).
    /// </summary>
    public static int GetMinJavaVersion(string? version)
    {
        return NormalizeVersion(version) == Version121 ? 21 : 17;
    }

    /// <summary>
    /// Имя компонента среды выполнения Java в манифесте Mojang.
    /// </summary>
    public static string GetJavaRuntimeComponent(string? version)
    {
        return NormalizeVersion(version) == Version121 ? "java-runtime-delta" : "java-runtime-gamma";
    }

    /// <summary>
    /// Пользовательское отображение версии в интерфейсе.
    /// </summary>
    public static string GetDisplayTitle(string? version)
    {
        return NormalizeVersion(version) == Version121 ? "Minecraft 1.21.1" : "Minecraft 1.20.1";
    }

    /// <summary>
    /// Префикс пути в репозитории Aura-Pack (для 1.20.1 пустая строка, для 1.21.1 "versions/1.21.1/").
    /// </summary>
    public static string GetRepoPrefix(string? version)
    {
        return NormalizeVersion(version) == Version121 ? "versions/1.21.1/" : "";
    }
}

using System;

namespace AuraLauncher.Models;

/// <summary>
/// Метаданные элемента в managed истории гардероба (ранее импортированные скины и плащи).
/// </summary>
public class WardrobeItem
{
    /// <summary>Уникальный managed идентификатор (например: skin_a1b2c3d4... или cape_e5f6g7h8...).</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Тип элемента: "skin" или "cape".</summary>
    public string Type { get; set; } = "skin";

    /// <summary>Безопасное имя исходного файла (без путей).</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>SHA-256 хеш содержимого файла для дедупликации.</summary>
    public string Hash { get; set; } = string.Empty;

    /// <summary>Модель скина: "default" (Steve 4px) или "slim" (Alex 3px). Для плаща не используется.</summary>
    public string Model { get; set; } = "default";

    /// <summary>Относительный путь к файлу внутри managed storage (.aura/skins или .aura/capes).</summary>
    public string RelativePath { get; set; } = string.Empty;

    /// <summary>Дата и время импорта (UTC).</summary>
    public DateTime CreatedUtc { get; set; } = DateTime.UtcNow;
}

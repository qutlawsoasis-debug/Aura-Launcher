namespace AuraLauncher.Models;

/// <summary>
/// Отчет о прогрессе автоустановки игрового окружения Minecraft (CmlLib.Core).
/// </summary>
public class InstallProgressReport
{
    /// <summary>
    /// Текущий этап ("Minecraft 1.20.1", "Java 17", "Fabric", "Библиотеки", "Ресурсы").
    /// </summary>
    public string Stage { get; set; } = string.Empty;

    /// <summary>
    /// Номер текущего этапа (1..5).
    /// </summary>
    public int StageIndex { get; set; } = 1;

    /// <summary>
    /// Общее число этапов (5).
    /// </summary>
    public int TotalStages { get; set; } = 5;

    /// <summary>
    /// Общий процент выполнения (0..100).
    /// </summary>
    public double Percentage { get; set; }

    /// <summary>
    /// Текущая скорость загрузки в формате "X.X МБ/с" или "X КБ/с".
    /// </summary>
    public string FormattedSpeed { get; set; } = string.Empty;

    /// <summary>
    /// Детализированный текст (имя файла или количество задач).
    /// </summary>
    public string DetailText { get; set; } = string.Empty;
}

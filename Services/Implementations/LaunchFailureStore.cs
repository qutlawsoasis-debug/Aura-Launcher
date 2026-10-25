using System;
using System.IO;
using System.Text.Json;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Персистентное хранение причины последнего сбоя запуска (last-launch-failure.json в каталоге logs инстанса).
/// Записывается только при сбое; успешный запуск файл не удаляет — причина сохраняется до следующего сбоя,
/// чтобы повторный запуск не стёр улику предыдущего.
/// </summary>
public static class LaunchFailureStore
{
    public const string FileName = "last-launch-failure.json";

    private static readonly JsonSerializerOptions JsonOptions = new() { WriteIndented = true };

    public static void Save(string? logsDir, object record)
    {
        if (string.IsNullOrWhiteSpace(logsDir)) return;

        try
        {
            Directory.CreateDirectory(logsDir);
            string json = JsonSerializer.Serialize(record, JsonOptions);
            File.WriteAllText(Path.Combine(logsDir, FileName), json);
        }
        catch
        {
            // Диагностика не должна ломать обработку завершения игры
        }
    }
}

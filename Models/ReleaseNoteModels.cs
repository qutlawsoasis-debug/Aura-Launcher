using System;
using System.Collections.Generic;

namespace AuraLauncher.Models;

public enum ReleaseNoteBadgeType
{
    New,       // Новое (янтарная)
    Improved,  // Улучшено (серая)
    Fixed      // Исправлено (зелёная)
}

public class ReleaseNoteItem
{
    public ReleaseNoteBadgeType BadgeType { get; set; } = ReleaseNoteBadgeType.New;
    
    public string BadgeText => BadgeType switch
    {
        ReleaseNoteBadgeType.New => "Новое",
        ReleaseNoteBadgeType.Improved => "Улучшено",
        ReleaseNoteBadgeType.Fixed => "Исправлено",
        _ => "Новое"
    };

    public string Text { get; set; } = string.Empty;

    public static ReleaseNoteItem Parse(string rawLine)
    {
        if (string.IsNullOrWhiteSpace(rawLine)) return new ReleaseNoteItem();
        string trimmed = rawLine.Trim();

        ReleaseNoteBadgeType badge = ReleaseNoteBadgeType.New;
        string text = trimmed;

        if (trimmed.StartsWith("[Новое]", StringComparison.OrdinalIgnoreCase))
        {
            badge = ReleaseNoteBadgeType.New;
            text = trimmed.Substring(7).Trim();
        }
        else if (trimmed.StartsWith("[Улучшено]", StringComparison.OrdinalIgnoreCase))
        {
            badge = ReleaseNoteBadgeType.Improved;
            text = trimmed.Substring(10).Trim();
        }
        else if (trimmed.StartsWith("[Исправлено]", StringComparison.OrdinalIgnoreCase))
        {
            badge = ReleaseNoteBadgeType.Fixed;
            text = trimmed.Substring(12).Trim();
        }
        else if (trimmed.StartsWith("Новое:", StringComparison.OrdinalIgnoreCase))
        {
            badge = ReleaseNoteBadgeType.New;
            text = trimmed.Substring(6).Trim();
        }
        else if (trimmed.StartsWith("Улучшено:", StringComparison.OrdinalIgnoreCase))
        {
            badge = ReleaseNoteBadgeType.Improved;
            text = trimmed.Substring(9).Trim();
        }
        else if (trimmed.StartsWith("Исправлено:", StringComparison.OrdinalIgnoreCase))
        {
            badge = ReleaseNoteBadgeType.Fixed;
            text = trimmed.Substring(11).Trim();
        }

        return new ReleaseNoteItem
        {
            BadgeType = badge,
            Text = text
        };
    }
}

public class ReleaseNoteVersion
{
    public string Version { get; set; } = string.Empty;           // e.g. "beta 1.0.66"
    public string InternalVersion { get; set; } = string.Empty;   // e.g. "1.2.74"
    public string DateText { get; set; } = string.Empty;          // e.g. "10 октября 2026"
    public string ShortTitle { get; set; } = string.Empty;        // e.g. "Карусель и анимация" (короткий заголовок до 3 слов)
    public bool IsCurrent { get; set; }                           // true если "Вы здесь"
    public List<ReleaseNoteItem> Items { get; set; } = new();
}

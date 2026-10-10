using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class ReleaseNotesService : IReleaseNotesService
{
    private readonly List<ReleaseNoteVersion> _versions = new();

    public ReleaseNotesService()
    {
        LoadVersions();
    }

    public IReadOnlyList<ReleaseNoteVersion> GetAllVersions() => _versions;

    public ReleaseNoteVersion GetCurrentVersion()
    {
        return _versions.FirstOrDefault(v => v.IsCurrent) ?? _versions.FirstOrDefault() ?? new ReleaseNoteVersion();
    }

    private void LoadVersions()
    {
        var (currentInternal, currentUserFacing) = LauncherUpdateService.ResolveVersions();

        // 1. Предзагруженные исторические заметки версий (игроцкий стиль)
        var list = new List<ReleaseNoteVersion>
        {
            new ReleaseNoteVersion
            {
                Version = currentUserFacing,
                InternalVersion = currentInternal,
                DateText = "10 октября 2026",
                ShortTitle = "Список изменений",
                IsCurrent = true,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Двухоконный Список изменений с фильтрацией категорий и выбором версий"),
                    ReleaseNoteItem.Parse("[Улучшено] Кнопка быстрого копирования текста обновлений для друзей"),
                    ReleaseNoteItem.Parse("[Исправлено] Тексты заметок переписаны в понятном игроцком стиле")
                }
            },
            new ReleaseNoteVersion
            {
                Version = "beta 1.0.66",
                InternalVersion = "1.2.74",
                DateText = "10 октября 2026",
                ShortTitle = "Обновление карусели",
                IsCurrent = false,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Стеклянная карусель на главной с анимацией историй и скриншотами"),
                    ReleaseNoteItem.Parse("[Улучшено] Прогресс-бары карусели с эффектом свечения и паузой при наведении"),
                    ReleaseNoteItem.Parse("[Исправлено] Переходы между слайдами карусели с каскадным появлением элементов")
                }
            },
            new ReleaseNoteVersion
            {
                Version = "beta 1.0.65",
                InternalVersion = "1.2.73",
                DateText = "9 октября 2026",
                ShortTitle = "Управление памятью",
                IsCurrent = false,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Плавный одометр переключения выделенной ОЗУ в Настройках"),
                    ReleaseNoteItem.Parse("[Улучшено] Прозрачная верхняя зона перетаскивания окна лаунчера"),
                    ReleaseNoteItem.Parse("[Исправлено] Обновлённое окно загрузки с анимированным логотипом и повтором")
                }
            },
            new ReleaseNoteVersion
            {
                Version = "beta 1.0.64",
                InternalVersion = "1.2.72",
                DateText = "8 октября 2026",
                ShortTitle = "Сетевой статус",
                IsCurrent = false,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Подсказки этапов в лобби и системные уведомления Windows"),
                    ReleaseNoteItem.Parse("[Улучшено] Мгновенная синхронизация открытого локального мира"),
                    ReleaseNoteItem.Parse("[Исправлено] Подключение к лобби по коду приглашения без сбоев")
                }
            },
            new ReleaseNoteVersion
            {
                Version = "beta 1.0.63",
                InternalVersion = "1.2.68",
                DateText = "6 октября 2026",
                ShortTitle = "Мастерская и моды",
                IsCurrent = false,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Статистика мира в Мастерской: сид, координаты и текущий игровой день"),
                    ReleaseNoteItem.Parse("[Улучшено] Автоматическое копирование сделанного скриншота в буфер обмена"),
                    ReleaseNoteItem.Parse("[Исправлено] Очистка кнопок Мастерской и матовые стеклянные поля Настроек")
                }
            },
            new ReleaseNoteVersion
            {
                Version = "beta 1.0.62",
                InternalVersion = "1.2.64",
                DateText = "4 октября 2026",
                ShortTitle = "Интерфейс и стили",
                IsCurrent = false,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Векторная анимация отрисовки чекбоксов и обновлённые тост-уведомления"),
                    ReleaseNoteItem.Parse("[Улучшено] Перекомпоновка элементов управления и десктопные переключатели"),
                    ReleaseNoteItem.Parse("[Исправлено] Отображение системных уведомлений поверх открытой игры")
                }
            }
        };

        // Если локальная текущая версия из version.json отличается, обновим список
        try
        {
            var candidates = new[]
            {
                Path.Combine(AppContext.BaseDirectory, "version.json"),
                Path.Combine(Environment.CurrentDirectory, "version.json")
            };

            foreach (var path in candidates)
            {
                if (File.Exists(path))
                {
                    var json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    if (doc.RootElement.TryGetProperty("changelog", out var clProp) && clProp.ValueKind == JsonValueKind.Array)
                    {
                        var rawLines = new List<string>();
                        foreach (var item in clProp.EnumerateArray())
                        {
                            var l = item.GetString();
                            if (!string.IsNullOrWhiteSpace(l)) rawLines.Add(l);
                        }

                        if (rawLines.Count > 0 && list.Count > 0 && list[0].Version == currentUserFacing)
                        {
                            // Обновим пункты текущей версии распарсенными строками
                            list[0].Items = rawLines.Select(ReleaseNoteItem.Parse).ToList();
                        }
                    }
                    break;
                }
            }
        }
        catch { }

        _versions.Clear();
        _versions.AddRange(list);
    }
}

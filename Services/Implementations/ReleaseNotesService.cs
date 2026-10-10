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
                ShortTitle = "Обновление оформления",
                IsCurrent = true,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Полноэкранный просмотр заметок обновлений и руководство по оформлению релизов"),
                    ReleaseNoteItem.Parse("[Улучшено] Приглушённые матовые оттенки и отсутствие просвечивания в окнах лаунчера"),
                    ReleaseNoteItem.Parse("[Исправлено] Мгновенный отклик кнопок в заголовочной зоне и модальных окнах")
                }
            },
            new ReleaseNoteVersion
            {
                Version = "beta 1.0.67",
                InternalVersion = "1.2.75",
                DateText = "10 октября 2026",
                ShortTitle = "Удобный список изменений",
                IsCurrent = false,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Окно заметок с разделами по категориям и кнопкой скопировать для друзей"),
                    ReleaseNoteItem.Parse("[Улучшено] Матовый фон и акцентные цвета интерфейса стали более приглушёнными"),
                    ReleaseNoteItem.Parse("[Исправлено] Кнопки в окне изменений теперь мгновенно откликаются на клики")
                }
            },
            new ReleaseNoteVersion
            {
                Version = "beta 1.0.66",
                InternalVersion = "1.2.74",
                DateText = "10 октября 2026",
                ShortTitle = "Стеклянная карусель новостей",
                IsCurrent = false,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Интерактивная карусель на главной с историями и скриншотами"),
                    ReleaseNoteItem.Parse("[Улучшено] Индикаторы прогресса плавно замирают при наведении курсора"),
                    ReleaseNoteItem.Parse("[Исправлено] Анимация смены слайдов работает без рывков и провисаний")
                }
            },
            new ReleaseNoteVersion
            {
                Version = "beta 1.0.65",
                InternalVersion = "1.2.73",
                DateText = "9 октября 2026",
                ShortTitle = "Настройка памяти и загрузка",
                IsCurrent = false,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Наглядный ползунок выбора оперативной памяти в Настройках"),
                    ReleaseNoteItem.Parse("[Улучшено] Прозрачная верхняя область окна для удобного перетаскивания"),
                    ReleaseNoteItem.Parse("[Исправлено] Экран запуска лаунчера с анимацией и кнопкой повтора")
                }
            },
            new ReleaseNoteVersion
            {
                Version = "beta 1.0.64",
                InternalVersion = "1.2.72",
                DateText = "8 октября 2026",
                ShortTitle = "Уведомления и совместная игра",
                IsCurrent = false,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Системные уведомления Windows о подключении друзей и запуске сервера"),
                    ReleaseNoteItem.Parse("[Улучшено] Автоматическое обнаружение открытого мира для друзей в сети"),
                    ReleaseNoteItem.Parse("[Исправлено] Подключение к совместному лобби по коду приглашения")
                }
            },
            new ReleaseNoteVersion
            {
                Version = "beta 1.0.63",
                InternalVersion = "1.2.68",
                DateText = "6 октября 2026",
                ShortTitle = "Статистика миров и скриншоты",
                IsCurrent = false,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Карточки миров показывают текущий игровой день, сид и координаты"),
                    ReleaseNoteItem.Parse("[Улучшено] Сделанный в игре скриншот сразу копируется в буфер обмена"),
                    ReleaseNoteItem.Parse("[Исправлено] Отображение информации о мире при переключении подразделов")
                }
            },
            new ReleaseNoteVersion
            {
                Version = "beta 1.0.62",
                InternalVersion = "1.2.64",
                DateText = "4 октября 2026",
                ShortTitle = "Обновление интерфейса",
                IsCurrent = false,
                Items = new List<ReleaseNoteItem>
                {
                    ReleaseNoteItem.Parse("[Новое] Плавная анимация переключателей и обновлённые тосты в игре"),
                    ReleaseNoteItem.Parse("[Улучшено] Обновлённый вид кнопок управления и переключателей разделов"),
                    ReleaseNoteItem.Parse("[Исправлено] Уведомления больше не перекрывают элементы управления")
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

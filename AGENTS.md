# Aura Launcher — Правила проекта для ИИ-агента (AGENTS.md)

Полный контекст проекта, версий и путей находится в [docs/HANDOFF.md](docs/HANDOFF.md).
Эталон дизайна и вёрстки — в [DESIGN.md](DESIGN.md).

---

## 1. Архитектура и пути

- **Лаунчер (`Aura-Launcher`)**: `C:\Users\magne\Documents\GitHub\Aura-Launcher`
  - Стек: C# 12 / .NET 8.0 (`net8.0-windows10.0.19041.0`), WPF, MVVM, DI, `CmlLib.Core` (Minecraft 1.20.1 + Fabric `0.19.5`), `Velopack` (`vpk`).
- **Бэкенд (`lobby-api`)**: `C:\Users\magne\Documents\GitHub\Aura-Launcher\lobby-api`
  - Стек: TypeScript, Vercel Serverless (`https://lobby-api.vercel.app`), Upstash Redis (`KV_REST_API_URL`, `KV_REST_API_TOKEN`), `PLAYIT_SECRET`.
- **Сборка модов (`Aura-Pack`)**: `C:\Users\magne\Documents\GitHub\Aura-Pack`
  - Репозиторий: `qutlawsoasis-debug/Aura-Pack` (ветка `main`, синхронизация по `manifest.json` с SHA-256).
- **Данные и логи на ПК**:
  - Конфиг лаунчера: `%APPDATA%\.aura\config.json` (единый источник правды через `IConfigService`, атомарная запись `.tmp` -> `File.Move`, сохранение неизвестных JSON-полей, бэкап `.bak` при повреждении).
  - Папка игры по умолчанию: `%APPDATA%\.aura` (моды: `%APPDATA%\.aura\mods`, логи игры: `%APPDATA%\.aura\logs\latest.log` и `launcher-game.log`, туннель: `%APPDATA%\.aura\logs\tunnel.log`).
  - Лог самого лаунчера: `%APPDATA%\Aura\launcher.log`.

---

## 2. Дисциплина выполнения и фильтр Anti-Slop

1. **Никакой самодеятельности**: делай строго то, что просят в задаче. Не добавляй новые фичи, экраны, кнопки, надписи и анимации по своей инициативе.
2. **Никаких выдуманных данных**: в интерфейсе запрещены фейковый онлайн, пинг, счётчики и заглушки. Показывай только реальные данные или `"—"`.
3. **Фильтр Anti-Slop (обязателен)**:
   - Перед работой с UI, текстами или кодом соблюдай правила из `.agents/skills/antislop/SKILL.md`:
     - UI: `.agents/skills/antislop-ui/SKILL.md` (в связке с `DESIGN.md` и `.agents/skills/aura-ui-design/SKILL.md`).
     - Код: `.agents/skills/antislop-code/SKILL.md` — никаких баннеров из символов (`// === SECTION ===`), комментариев, пересказывающих имя метода/переменной, и эмодзи в коде или логах.
     - Тексты: `.agents/skills/antislop-copywriting/SKILL.md` — никакой рекламной воды, капса и пустых слоганов.
4. **Планирование сложных задач**: для многошаговых задач (>5 шагов) используй навык `.agents/skills/planning-with-files/SKILL.md` (`task_plan.md`, `findings.md`, `progress.md` на диске).
5. **Релизы**: при выпуске обновлений лаунчера или модпака следуй навыку `.agents/skills/aura-release/SKILL.md`.

---

## 3. Правила UI и WPF / XAML (`DESIGN.md`)

1. **Перед любой UI-задачей** прочитай [DESIGN.md](DESIGN.md) и навык [.agents/skills/aura-ui-design/SKILL.md](.agents/skills/aura-ui-design/SKILL.md).
2. **Золотое правило**: перед созданием нового элемента найди максимально похожий в существующих View (`OverviewView`, `LobbyView`, `FriendsView`, `WardrobeView`, `WorkshopView`, `SettingsView`) и скопируй его стиль и отступы.
3. **Токены и ресурсы**:
   - Цвета и кисти — только из `Themes/Colors.xaml` (`BgBase`, `Panel`, `PanelRaised`, `TextPrimary`, `Accent`, `Border`, `BorderSubtle` и др.). Никаких `#RRGGBB` в `Views/` и `Controls/`.
   - Помни, что `Color` и `SolidColorBrush` — разные типы ресурсов (`Background`/`Foreground`/`BorderBrush` принимают только `Brush`).
   - Иконки — только векторные `Path` из `Themes/Icons.xaml`. Никаких эмодзи (`U+1F300–U+1FAFF`, `U+2600–U+27BF`).
   - Шрифты — только `HeadingFont` (**Oswald**) и `MainFont` (**Onest**). Шрифт `Inter` и пиксельные шрифты строго запрещены.
   - Скругление углов — строго `CornerRadius="3"`.
   - Никаких `DropShadowEffect`/`BlurEffect` с цветным/amber свечением и никаких прыжков кнопок (`TranslateTransform`) при наведении/клике.
   - Никаких жёстких `Width`/`Height` у `TextBlock`, `Button` и текстовых панелей.
   - Никаких локальных `<Style TargetType="Button|TextBox|ListBox">` внутри `Views/` — все стили контролов хранятся в `Styles/UiTheme.xaml`.
4. **Автопроверка дизайна**: после любой правки XAML/UI обязательно запускай `.\scripts\check-design.ps1` (должно быть 0 нарушений).

---

## 4. Стандарты кода C# / .NET 8 и Безопасность

1. **Производительность и потоки WPF**:
   - Все `Freezable`-объекты в C# (кисти, `BitmapImage`, 3D-материалы и геометрии) обязательно замораживать (`.Freeze()`).
   - Не блокировать UI-поток (никаких `.Result` / `.Wait()`); использовать `async`/`await` и `CancellationToken`.
2. **Безопасность и пути**:
   - Никогда не хардкодить локальные пути (`D:\`, `C:\Users\magne` и т.п.) в коде или тестах.
   - При скачивании файлов сборки отвергать пути с `..`, абсолютные пути и ссылки не с `raw.githubusercontent.com` по HTTPS; проверять SHA-256 и писать через `.part` с атомарной заменой.
   - Секреты (например, токен playit) хранить через DPAPI; не коммитить `.env` и ключи в репозиторий.

---

## 5. Честность верификации

1. Не пиши «готово», «проверено» или «работает», если ты не запустил проверку (`dotnet build`, `dotnet test`, `.\scripts\check-design.ps1`).
2. В итоговом отчёте отдельным пунктом честно перечисляй, что **НЕ было проверено** фактическим запуском.
3. **Критерий запуска Minecraft**: запуск игры считается проверенным **только** если процесс игры жив и в `%APPDATA%\.aura\logs\latest.log` появились строки `Reloading ResourceManager` и `Created: ...atlas` (без `Mixin apply failed` / `Mod resolution failed`). Сама по себе строка `Backend library: LWJGL` не доказывает успешный запуск.

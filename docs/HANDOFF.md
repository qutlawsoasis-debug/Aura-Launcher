# Aura Project Handoff (Контекст для нового чата)

## 1. Репозитории и пути на ПК
- **Лаунчер (`Aura-Launcher`)**: `C:\Users\magne\Documents\GitHub\Aura-Launcher`
  - GitHub: `https://github.com/qutlawsoasis-debug/Aura-Launcher`
  - Стек: C# / .NET 8.0 (`net8.0-windows10.0.19041.0`), WPF, MVVM, CmlLib.Core (запуск Minecraft 1.20.1 + Fabric `0.19.5`), Velopack (`vpk` для автообновлений и инсталлятора).
  - Текущая версия релиза: **`1.2.46` (`beta 1.0.38`)** — опубликована в GitHub Releases (`v1.2.46`).
- **Бэкенд лобби, друзей, скинов и отчётов (`lobby-api`)**: `C:\Users\magne\Documents\GitHub\Aura-Launcher\lobby-api`
  - Хостинг: Vercel (`https://lobby-api.vercel.app`), проект `qutlawsoasis-debugs-projects/lobby-api`.
  - Хранилище: Upstash Redis (`KV_REST_API_URL`, `KV_REST_API_TOKEN`) + `PLAYIT_SECRET` для playit.gg туннеля.
- **Сборка модов (`Aura-Pack`)**: `C:\Users\magne\Documents\GitHub\Aura-Pack`
  - GitHub: `https://github.com/qutlawsoasis-debug/Aura-Pack`
  - Текущий `packVersion` в `manifest.json`: **`20261008.0316`** (219 файлов, Minecraft 1.20.1, Fabric 0.19.5).

---

## 2. Где хранятся данные и логи на ПК пользователя
- **Конфиг лаунчера**: `%APPDATA%\.aura\config.json` (перенесён из `%APPDATA%\Aura\config.json`, старый файл мигрирует автоматически при первом запуске).
  - При повреждении JSON создаётся копия `config.json.bak`.
  - `SaveConfigAsync` атомарный (через `.tmp` -> `File.Move`), сохраняет неизвестные JSON-поля.
- **Директория игры по умолчанию**: `%APPDATA%\.aura`
  - Моды: `%APPDATA%\.aura\mods`
  - Логи игры: `%APPDATA%\.aura\logs\latest.log`, `%APPDATA%\.aura\logs\launcher-game.log`
  - Лог туннеля: `%APPDATA%\.aura\logs\tunnel.log`
- **Лог самого лаунчера**: `%APPDATA%\Aura\launcher.log`
- **Установленный через Velopack лаунчер**: `%LOCALAPPDATA%\AuraLauncher`

---

## 3. Дизайн-система и правила вёрстки
Подробные правила описаны в `DESIGN.md`:
- Все цвета только через ресурсы из `Themes/Colors.xaml` (`BgBase`, `Panel`, `PanelRaised`, `TextPrimary`, `Accent`, `Border`, `BorderSubtle` и т.д.).
- Шрифты: `HeadingFont` (**Oswald**) для заголовков, `MainFont` (**Onest**) для текста.
- Скругления: `CornerRadius="3"`. Без эмодзи (только векторные иконки из `Themes/Icons.xaml`), без свечения (`DropShadowEffect` с цветным glow запрещён).
- Никаких жёстких `Width`/`Height` у `TextBlock`, `Button` и текстовых панелей.
- Проверка соблюдения дизайн-системы: `.\scripts\check-design.ps1`.

---

## 4. Как выпускать релизы

### Релиз лаунчера (`Aura-Launcher`)
1. Обновить версию в `AuraLauncher.csproj` (`<Version>1.2.XX</Version>`) и `version.json` (`internalVersion`: `1.2.XX`, `userFacingVersion`: `beta 1.0.YY`, где `YY = XX - 8`).
2. Закоммитить и запушить в `main`.
3. Собрать пакет через Velopack:
   ```powershell
   .\scripts\build-release.ps1
   ```
   Артефакты появятся в `C:\AuraRelease\1.2.XX\`.
4. Опубликовать релиз на GitHub через `gh`:
   ```powershell
   gh release create v1.2.XX `
     "C:\AuraRelease\1.2.XX\AuraLauncher-win-Setup.exe" `
     "C:\AuraRelease\1.2.XX\AuraLauncher-win-Portable.zip" `
     "C:\AuraRelease\1.2.XX\AuraLauncher-1.2.XX-full.nupkg" `
     "C:\AuraRelease\1.2.XX\AuraLauncher-1.2.XX-delta.nupkg" `
     "C:\AuraRelease\1.2.XX\RELEASES" `
     "C:\AuraRelease\1.2.XX\releases.win.json" `
     "C:\AuraRelease\1.2.XX\assets.win.json" `
     --title "beta 1.0.YY (v1.2.XX)" --notes "beta 1.0.YY"
   ```

### Обновление сборки модов (`Aura-Pack`)
В папке `C:\Users\magne\Documents\GitHub\Aura-Pack`:
```powershell
.\tools\publish.ps1 -Yes
```
Скрипт сам пересчитывает `manifest.json` (SHA-256, размеры, `packVersion`), коммитит и пушит в `main`. Лаунчер у всех игроков подтягивает изменения автоматически при нажатии «Играть» или «Создать лобби».

---

## 5. Последние выполненные изменения
1. **`StartupWindow` (`Views/StartupWindow.xaml`)**: новое стартовое окно со сценой 300×188, проверкой/скачиванием обновлений Velopack и защитой от гонки закрытия `Application` (`App.xaml.cs`).
2. **Главный экран (`Views/OverviewView.xaml`)**: правая колонка `StoriesRail` в виде компактного блока со слайдами («Скриншоты», «Достижения», «Что нового»), горизонтальной картинкой 16:9 внутри слайда, 3 полосками прогресса по 5 сек, автопаузой и навигацией кликом (левые 30% / правые 70% / удержание).
3. **Мастерская (`Views/WorkshopView.xaml`)**: редизайн вкладок и карточек («Миры», «Моды», «Шейдеры», «Скриншоты»).
4. **Сохранение настроек (`ConfigService.cs`, `SettingsViewModel.cs`, `WardrobeViewModel.cs`, `MainWindow.xaml.cs`)**: конфиг перенесён в `%APPDATA%\.aura\config.json`, мгновенный дебаунс сохранения (300 мс), сохранение при закрытии окна и перед рестартом обновления.
5. **Исправления в `Aura-Pack`**:
   - Удалён конфликтующий мод `NE-1.20.1-1.9.0.jar` (*NoExpensive*), вызывавший Mixin-краш с `no_xp_anvils`.
   - В `alexsmobs-2.2.2-fabric+1.20.1.jar` добавлены недостающие теги предметов (`c:nuggets/iron`, `c:ingots/iron`, `c:ingots/netherite`, `c:rods/wooden`, `c:strings`, `c:glass_blocks`), чтобы работали все крафты *Alex's Mobs* (включая «Ракету эндериофага»).
6. **Среда разработки и правила агента (`AGENTS.md` + `.agents/skills/`)**:
   - Настроены и актуализированы корневые `AGENTS.md` в `Aura-Launcher` и `Aura-Pack` (исключены из `manifest.json` в `make-manifest.ps1`).
   - В `.agents/skills/` установлены: `antislop` (`antislop`, `antislop-ui`, `antislop-code`, `antislop-copywriting`), `planning-with-files`, синхронизированный с `DESIGN.md` навык `aura-ui-design` и регламент релизов `aura-release`.
   - Прогон `.\scripts\check-design.ps1` на текущий момент выявляет 47 старых находок во `Views/` (36 HEX-цветов в `StartupWindow.xaml`, `WorkshopView.xaml`, `SettingsView.xaml`, `LobbyView.xaml`, 1 `TemplateBinding FontFamily` и 10 локальных `<Button.Style>` в `FriendsView.xaml` и `LobbyView.xaml`).


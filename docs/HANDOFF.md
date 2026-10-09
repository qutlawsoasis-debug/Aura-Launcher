# Aura Project Handoff (Контекст для нового чата)

## 1. Репозитории и пути на ПК
- **Лаунчер (`Aura-Launcher`)**: `C:\Users\magne\Documents\GitHub\Aura-Launcher`
  - GitHub: `https://github.com/qutlawsoasis-debug/Aura-Launcher`
  - Стек: C# / .NET 8.0 (`net8.0-windows10.0.19041.0`), WPF, MVVM, CmlLib.Core (запуск Minecraft 1.20.1 + Fabric `0.19.5`), Velopack (`vpk` для автообновлений и инсталлятора).
  - Текущая версия релиза: **`1.2.61` (`beta 1.0.53`)** — опубликована в GitHub Releases (`v1.2.61`).
- **Бэкенд лобби, друзей, скинов и отчётов (`lobby-api`)**: `C:\Users\magne\Documents\GitHub\Aura-Launcher\lobby-api`
  - Хостинг: Vercel (`https://lobby-api.vercel.app`), проект `qutlawsoasis-debugs-projects/lobby-api`.
  - Хранилище: Upstash Redis (`KV_REST_API_URL`, `KV_REST_API_TOKEN`) + `PLAYIT_SECRET` для playit.gg туннеля.
- **Сборка модов (`Aura-Pack`)**: `C:\Users\magne\Documents\GitHub\Aura-Pack`
  - GitHub: `https://github.com/qutlawsoasis-debug/Aura-Pack`
  - Текущий `packVersion` в `manifest.json`: **`20261009.1806`** (Minecraft 1.20.1, Fabric 0.19.5).

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
- Проверка соблюдения дизайн-системы: `.\scripts\check-design.ps1` (в текущей кодовой базе `0 findings`).

---

## 4. Как выпускать релизы

### Релиз лаунчера (`Aura-Launcher`)
1. Обновить версию в `AuraLauncher.csproj` (`<Version>1.2.XX</Version>`) и `version.json` (`internalVersion`: `1.2.XX`, `userFacingVersion`: `beta 1.0.YY`, где `YY = XX - 8`).
2. Закоммитить и запушить в `main`.
3. Собрать пакет через Velopack:
   ```powershell
   .\scripts\build-release.ps1
   ```
   Артефакты появятся в `C:\AuraRelease\1.2.XX\`. В `C:\AuraRelease` хранить не более **5 последних версий**.
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

## 5. Последние выполненные изменения (до `v1.2.61` / `beta 1.0.53`)
1. **Синхронизация скинов и модели рук (Alex/Steve)**:
   - В `CustomSkinLoader.json` первым приоритетом добавлен `ElyBy` API (`https://lobby-api.vercel.app/api/csl/`), реализован эндпоинт `lobby-api/api/csl/[...slug].ts`, возвращающий `SKIN` и `metadata: { model: "slim" }` для Alex.
2. **Интеграция миров, серверов, профилей графики и анализатора крашей (`v1.2.60`)**:
   - `ServerDatService`: автоматическое добавление/обновление активного сервера лобби в `servers.dat`.
   - Экспорт и импорт миров (`.zip`) в Мастерской и авто-бэкап мира перед открытием лобби.
   - `CrashAnalyzerService`: человекочитаемый разбор причин вылета Minecraft (`OutOfMemoryError`, конфликты модов, драйвер видеокарты и т.д.).
   - Профили графики в Настройках (`Слабый ПК`, `Баланс`, `Ультра`), применяющие пресеты в `options.txt` и `config/iris.properties`.
   - `Aura-Pack`: добавлен `TaxFreeLevels-1.4.10-fabric-1.20.2.jar` (бесплатные чары на столе зачарований в дополнение к бесплатной наковальне `no_xp_anvils`).
3. **Редизайн Настроек и сброс скроллбаров (`v1.2.61` / `beta 1.0.53`)**:
   - В `Styles/UiTheme.xaml` и `Views/SettingsView.xaml` переработаны чипы выбора ОЗУ (`RamPillRadio` в 4 колонки), ползунок `SettingsRamSlider` с янтарным заполнением, сегментированные переключатели профиля графики и режима запуска (`SettingsSegmentRadio`), поле пути к папке игры и кнопка `Изменить` (`SettingsActionBtn`).
   - В `Behaviors/SmoothScroll.cs` и `MainWindow.xaml.cs` добавлен автоматический сброс скроллбаров (`ResetAllScrollViewers`) в `0` при переключении вкладок сайдбара.

---

## 6. Выполненные фичи и фиксы пакета v1.2.62

Все 8 согласованных фич и фиксов полностью реализованы и протестированы:

1. **Баг №1 + Авто-возврат в лобби**:
   - `lobby-api`: добавлены `playerHeartbeats` гостей, авто-очистка гостей без пульса >25 сек.
   - Клиент: периодический пульс гостя в `GetStatusAsync(code, playerName)`, сохранение `LastActiveLobbyCode` в конфигурации, при повторном запуске лаунчер проверяет код и автоматически возвращает в активное лобби с показом уведомления.
2. **Баг №2: Доступность кнопки «Войти» у друзей**:
   - `lobby-api`: `store.ts` сохраняет и возвращает `lobbyCode` даже при статусе `"playing"`.
   - Клиент: немедленный `SyncNowAsync` при создании/входе/выходе из лобби, `CanJoin` активен при наличии `LobbyCode`.
3. **Прямой вход в мир («В мир»)**:
   - В Мастерской добавлена акцентная кнопка «В мир» с иконкой `IconPlay`, передающая флаг `--quickPlaySingleplayer <FolderName>` в процесс Minecraft для мгновенного входа в одиночный мир.
4. **Кик участника из лобби хостом**:
   - Бэкенд: эндпоинт `POST /api/kick` и 403 Forbidden в `/api/join` для исключенных игроков.
   - Клиент: в карточке игроков у хоста кнопка «Кикнуть» с векторной иконкой `IconKick`. При исключении гость получает всплывающее уведомление и возвращается на главный экран лобби.
5. **Живой пинг (мс) и статус «Кто уже на сервере»**:
   - `MinecraftPingService`: измерение реального TCP-пинга + опрос количества игроков через Minecraft SLP Handshake.
   - В карточке лобби отображаются бейджи с пингом (`IconWifi`) и онлайном (`IconWorld`). При недоступности сервера честно показывается `"—"` (строго без фейковых данных).
6. **День мира, координаты X/Y/Z, измерение и сид в Мастерской**:
   - Чтение из `level.dat` через `fNbt` номера игрового дня, измерения (Верхний мир / Незер / Энд), координат позиции игрока и сида генерации.
   - Отображение чипов характеристик в избранном мире и строках списка миров.
7. **Авто-копирование скриншота (F2) в буфер Windows**:
   - `ScreenshotWatcherService`: отслеживание появления новых снимков в `<gameDir>\screenshots`, безопасное чтение и помещение в системный буфер обмена (`Clipboard.SetDataObject` как картинка + файл) для вставки в Discord/Telegram, всплывающий тост в лаунчере.
8. **Кнопка «Проверить целостность сборки» в Настройках**:
   - `PackUpdateService`: поддержка `forceFullCheck = true` с принудительной проверкой SHA-256 всех модов и конфигов манифеста, восстановление повреждённых/удалённых файлов в 1 клик.
   - Пункт в Настройках 1.4 с кнопкой и прогресс-статусом.
9. **Редизайн тост-уведомлений и новые In-App / Windows Toast оповещения**:
   - **UI/UX тостов**: объединены в единый вертикальный стек (`StackPanel` в верхнем правом углу) без наложения друг на друга. Карточка тоста выполнена с левой цветной акцентной полосой (`EmberBrush` для действий/скриншотов, `OkBrush` для успеха, `BadBrush` для ошибок/кика), векторной иконкой 28x28 (`IconInfo`, `IconCamera`, `IconCheck`, `IconWarning`, `IconWorld`, `IconUser`), кнопкой быстрого закрытия `IconClose` и кликабельным телом с переходом в целевую вкладку (Мастерская/Лобби/Настройки).
   - **Плавная анимация**: выезд по горизонтали `TranslateTransform.X` (от 20 до 0 за 220 мс) и фейд `Opacity` (от 0 до 1) с `CubicEase`.
   - **Windows Notifications**: системные уведомления Windows Action Center поверх открытого окна Minecraft (при F2-скриншоте, краше игры с кодом ошибки и исключением из лобби), гарантирующие видимость событий во время игры.
   - **Новые сценарии уведомлений**: скриншот скопирован в буфер (с кликом в Мастерскую), аварийный краш Minecraft (с кодом и причиной), исключение из лобби (кик), результат проверки целостности сборки и появление новых обновлений.

# Конвейер Aura: создание → сборка → проверка → правка / релиз

Доска ведётся здесь. Каждая задача — одна строка. Стадию меняет тот, кто её закончил.

## Стадии
1. **Создание** — чат-разработчик по ТЗ `agy-task-*.md`. Итог: код + отчёт `agy-*-report.md` с «ГОТОВО». Стадия → «сборка».
2. **Сборка** — тот же разработчик: `aura-ui\gradlew build` (и для 1.21.1, когда будет), `dotnet build`/`dotnet test` для лаунчера, `npx tsc --noEmit` для lobby-api. Красное → назад в «создание». Зелёное → «проверка».
3. **Проверка** — Claude: дифф, логи, кадры из игры (runClient с автосъёмкой). Не прошло → «правка» с конкретным списком в отчёте. Прошло → коммит + строка в `release-queue.md` → «к релизу».
4. **Правка** — тот же разработчик по списку Claude → снова «сборка».
5. **Релиз** — релиз-агент по `release-queue.md` и навыку `aura-release`. Не во время игры пользователя (lock).

## Правила для всех
- Игру (runClient) запускает только тот, кто держит `aura-ui\run\agent-run.lock`; `claude-for-user` = пользователь играет, никто не запускает. После теста — закрыть игру полностью (java KnotClient + jcef_helper).
- Один файл — один исполнитель за раз (смотри колонку «файлы»).
- Обе версии: 1.20.1 и 1.21.1 (см. задачу «порт на 1.21.1»).
- Без git checkout/restore/stash/reset/clean, без push (пушит только релиз-агент).

## Доска
| Задача | Исполнитель (чат) | Файлы | Стадия | Заметки |
|--------|-------------------|-------|--------|---------|
| ESC плавно + матовое размытие + разрешение экрана | 7f592cf9 | McefHtmlScreen, HtmlScreen, HtmlBridge, Blur, GameMenuScreenMixin, index.html | принято Claude |  кадр с меню 20:45; ждёт общего релиза HTML-интерфейса (MCEF в сборку) |
| Чат в стиле Aura | c7560004 | ChatHudMixin, ChatScreenMixin | проверка | код готов, gradlew build зелёный; ждёт снятия lock для кадров в игре |
| Слова в воздухе: Onest + эффекты | c7560004 | FloatingLyrics, LyricsDemo, WorldRendererMixin | проверка | QA готов: docs\redesign\qa\lyrics\qa.md (3 кадра: 107 мс entry, hold 39 FPS, exit blur 10px) |
| Экранные HUD-элементы в стиль Aura (tooltip, bossbar, sidebar, title, tab) | c7560004 | TooltipMixin, BossBarHudMixin, ScoreboardSidebarMixin, TitleMixin, PlayerListHudMixin, AuraHudKit | проверка | код готов, gradlew build зелёный (10с); ждёт снятия lock для кадров |
| Окна инвентаря в стиле Aura (стекло, слоты, анимация) | c7560004 | HandledScreenMixin, AuraHudKit | проверка | исправлен @Inject на INVOKE drawBackground в render, gradlew build зелёный, игра стартует без краша |
| Экран загрузки «After Effects» + пропуск Experimental | 925366ce | AuraSplash, SplashOverlayMixin, IntegratedServerLoaderMixin | проверка | правки QA: логотип <= 38% ширины, подзаголовок под логотипом, прогресс 64px от низа, строка «Загрузка ресурсов · 42%» |
| Aura Music: предзагрузка, YouTube, E2E | 925366ce | player/**, player.html | проверка | E2E принят; в игре проверить открытие без чёрного экрана |
| Порт всего aura-ui на 1.21.1 (общий код + mc1201/mc1211) | 7f592cf9 / c7560004 | aura-ui-1.21\** | проверка | 7f592cf9: экраны/web; c7560004: HUD, lyrics, inventory, mixins перенесены, gradlew build зелёный (7с) |
| Служебные экраны: загрузка мира, подключение, отключено, смерть | 925366ce | mixin\LevelLoading/Connect/Disconnected/DeathScreenMixin, screen\AuraWorldScreens | проверка | краш JVM устранён (fontOnest >= 0 защита от повторной загрузки NanoVG шрифта, boolean safe fallback на ваниль), gradlew build зелёный |
| Функции HTML-меню: клавиши, миры (создать/копия/удалить), серверы | 925366ce | HtmlBridge, index.html, title.html | проверка | код готов, gradlew build зелёный; ждёт снятия lock для проверки в игре |
| Пресеты графики (лаунчер) | релиз-агент 8418a0db | — | выпущено | v1.2.148 опубликован; очередь пуста, ожидание новых задач |

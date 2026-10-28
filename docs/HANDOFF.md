# Aura — передача текущего состояния

Срез: 28.10.2026, Europe/Berlin. Источники: реальные Git HEAD/remote, version.json, манифесты Aura-Pack и menu-only dev-проверки. Локальная версия файла не означает опубликованный релиз.

## Репозитории и копии

| Копия | Состояние |
|---|---|
| GitHub Aura-Launcher main | `e4681f1`, последний опубликованный релиз `v1.2.161` / beta1.0.153, опубликован26.10.2026. |
| Новый интеграционный клон | `C:/Users/magne/AppData/Local/Temp/AuraDev/glass-github-integration-20261028/repo`, ветка `codex/aura-glass`, основан на e4681f1. [PR #10](https://github.com/qutlawsoasis-debug/Aura-Launcher/pull/10) открыт, не слит. Источник сохранён в удалённой ветке; OS-temp-клон не считать единственной резервной копией. |
| AuraDev | `C:/Users/magne/Documents/AuraDev`, HEAD `d761b14`,49 локальных коммитов включая snapshot/отчёты, remote отсутствует. Рабочая копия чистая. version.json:1.2.154 / beta1.0.146. В этой задаче не изменялась. |
| Старый Aura-Launcher | `C:/Users/magne/Documents/GitHub/Aura-Launcher`, HEAD `c9dbff9` от25.10; main отстаёт от GitHub на22 коммита. Рабочая копия содержит многочисленные чужие изменения/untracked-файлы, version.json1.2.152 / beta1.0.144. Только чтение; не reset/clean/stash и не переносить её целиком. |
| GitHub Aura-Pack main | `0cb530f`, оба манифеста имеют packVersion `20261027.1908`; коммит содержимого `c7ef6e0`. Ранний native glass/motion поставляется внутри aura-defaults. Отдельного aura-glass в манифестах нет. |
| Старый checkout Aura-Pack | `C:/Users/magne/Documents/GitHub/Aura-Pack`, локальный HEAD `a9b5762`, локальные манифесты20261025.1406. Этот checkout отстал; локальный manifest не равен текущему GitHub. В задаче не изменялся. |

GitHub: [Aura-Launcher](https://github.com/qutlawsoasis-debug/Aura-Launcher), [Aura-Pack](https://github.com/qutlawsoasis-debug/Aura-Pack). Лаунчер: C#/.NET8 WPF, CmlLib.Core, Velopack; backend lobby-api: TypeScript/Vercel/Redis, исходники рядом в монорепозитории. Никаких секретов в этот документ не переносить.

## Сохранение glass и границы PR

Не выполнялся merge независимой snapshot-истории AuraDev. Три glass-модуля перенесены из git archive d761b14 в чистый актуальный main:

- `d86c6e3`: aura-glass, aura-glass-1.21, aura-glass-common; из defaults обеих версий удалён только дублирующий splash-рендер и регистрация. Правила Realms/предупреждений main сохранены.
- `9a131f6`: сохранены aura-hud-spec.md и clients-research.md из незакоммиченных документов старого клона; уточнена типографика и добавлена сверка этапов1–8.
- `99b16d8`: реальный запуск выявил неверную init-инъекцию MultiplayerWarningScreenMixin в defaults1.21; устаревший mixin удалён, обход предупреждения остаётся в TitleScreenMixin. Строгая проверка остальных defaults-миксинов сохранена.
- `59642ad`: Off с плотной заливкой без capture/blur; проверены обе версии и общий код.

Не переносились старый лаунчер/backend, version.json, установщики и pack-бинарники, runtime/QA-автоматика/снимки, несвязанные локальные quiet-chat/Xaero/backup-warning правки defaults. Глобальный override ванильного font provider из AuraDev исключён: он не нужен модульному glass и содержит отсутствующую в snapshot TTF-ссылку. Отдельный клиентский glass и defaults не зависят друг от друга.

Текущая ветка не опубликована в Aura-Pack. Старые pack-бинарники defaults могут содержать прежний UI: не рекомендовать игрокам вручную подмешивать новый glass без согласованного обновления обоих JAR. Любые merge/релиз/изменение manifest требуют отдельного поручения и принятия изменений.

## Модули, сборка и режимы

- aura-glass: Minecraft1.20.1, Java17+; aura-glass-1.21: Minecraft1.21.1, Java21+.
- aura-glass-common: общие sourceSets, не отдельный распространяемый JAR. GlassRenderer/BlurService, SDF/GLSL, MSDF, spring-анимации, JSON-пресеты, F6/F7, F8-галерея и UI-миксины.
- aura-defaults / aura-defaults-1.21: игровые правила и ограничения создания/хостинга миров, предупреждения/onboarding/Realms; серверная часть не переносилась в glass. В интеграции сохраняются текущие правила main, а не все незакоммиченные изменения старых копий.

Из каждого соответствующего модуля: `./gradlew build`, без clean. Все четыре сборки прошли последовательно на JDK21. Общие Java-проверки запускаются отдельно от Gradle module:test: прошли7 тестовых программ, включая Off. Не выдавать Gradle test NO-SOURCE за прохождение common-тестов.

Конфиг игры: `config/aura-glass/styles.json`, hot-reload. HIGH/FULL/full — полные эффекты; LOW/low — tint/простая кромка без capture/blur; OFF/off — плотная заливка без capture/blur и оптических эффектов; MEDIUM остаётся совместимым режимом blur. Off использует существующую плоскую shader-ветку, GLSL не изменялся. offFill задаёт RGB28/30/36 и alpha0.94 по умолчанию; primary/selected цвета сохраняются плотными. Off не означает выгрузку всего UI-кеша или нулевое GPU-время.

selectedPreset по умолчанию SELECTED_TINT; альтернативы SELECTED_GLASS/SELECTED_DARK, акцент отдельно theme.accent. Пресеты2e сохранены, задача2f по стабилизации оттенков/галерее не включена в этот перенос. F6 — тестовая панель, F7 — диагностика, F8 — галерея. Экран настроек эффектов в самой игре ещё не реализован: переключение через JSON.

## ТЗ, типографика и статус HUD

Источник требований: [aura-hud-spec](redesign/reports/aura-hud-spec.md); исследование: [clients-research](redesign/reports/clients-research.md); подробная [сверка1–8](redesign/reports/aura-hud-status-2026-10-28.md).

Игра: MSDF Onest400/500/600/700, Manrope только для отсутствующих греческих глифов; если глифа нет в обоих — vanilla fallback одного символа. Лицензии: aura-glass-common/src/main/resources/META-INF/licenses/aura-glass/. Лаунчер отдельно: WPF HeadingFont/MainFont Manrope (Styles/UiTheme.xaml), WebUIv3 Onest (tokens.css/core.css). Это описание текущих реализаций, не замена шрифтов лаунчера.

Этап1 частично подтверждён фундаментом;2 реализован, но полный GUI-scale/профиль/визуальная приёмка не закрыты. Этапы3(HUD) и4(редактор) не начаты: нет HudRegistry/HudConfig/HudEditorScreen и игровых элементов. WorldGlassHook — только точка захвата для галереи, а AuraSnapSolver не является редактором. Этапы5–8 частичны: pause/menu/options/worlds/servers оформлены через vanilla widgets; tooltip, chat HUD/command suggestions и полная ручная приёмка ещё не закрыты. Следующий код HUD — только после нового ТЗ архитектора.

## Фактическая проверка интеграции

Обе версии запускались отдельно в скрытых QA-копиях только до TitleScreen. Проверены совместно glass+defaults, завершение resource reload, строки Created: ...atlas и100 тиков настоящего меню. Окно visible=false/focused=false, звук/lockCursor заблокированы QA-only миксинами; профили данных отдельные. В мир не входили, снимки и ввод ОС не делали. В успешных итоговых логах нет Mixin apply...failed, InvalidInjectionException или ошибок aura-glass. По23 target-класса каждой версии и61 метод-селектор сверены через javap; мировые targets дополнительно проверены по дескрипторам без входа в мир.

После Off обе версии снова прошли HIGH→OFF→LOW→HIGH на меню. В Off: capture=false, blur-path0, fallback0, flat6, один glass draw call; в LOW capture=false, возврат HIGH восстановил capture. Отчёты: [menu smoke](redesign/qa/glass-integration/menu-smoke.md), [Off](redesign/qa/glass-integration/off.md).

Среда: GTX1660SUPER, OpenGL3.2 NVIDIA617.42,1280×720, GUI2, MSAA0, Iris отсутствует. Это smoke, не полный бенчмарк. CPU/аллокации и будущая стоимость HUD не измерены. Реальные сторонние моды/шейдерпаки, миры, остальные GPU, полный GUI scale1–4 и пользовательская визуальная приёмка не проверены.

## Изоляция и дальнейшая работа

Установленные профили APPDATA/.aura, APPDATA/Aura и LOCALAPPDATA/AuraLauncher, миры и установленный лаунчер не запускать/не изменять. AURA_OFFSCREEN сам по себе не доказывает скрытость окна. Разрешённый dev-smoke — только доказанно скрытая отдельная QA-среда до меню, без мира/снимков/захвата ввода/звука. Логи/кеши/распакованные JAR/одноразовые инструменты — в OS temp, не в публикуемых исходниках.

Ветку codex/aura-glass можно продолжать после review; прямой push/merge main запрещён. Staged-файлы перечислять явно; перед коммитом проверять содержимое, перед PR/его обновлением — весь диапазон base..head. Не отключать Git hooks и branch protection. Отдельная публикация Aura-Pack сейчас не разрешена. Условия релизов и секреты backend не менять в рамках HUD.

Старый HANDOFF с v1.2.99 и packVersion20261013.0148 — исторический материал, не текущий статус. Старые клоны сохранены без перезаписи. После согласованных шагов1–5 работа останавливается у архитектора за ТЗ на этап3.

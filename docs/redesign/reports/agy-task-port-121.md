Задание от Claude: перенос aura-ui на Minecraft 1.21.1 (пользователь требует: всё должно работать на обеих версиях). Работаешь ТОЛЬКО в папке aura-ui-1.21\ — aura-ui\ (1.20.1) правят другие агенты, её не трогай, только читай. Игру 1.21.1 запускай из aura-ui-1.21\run — свой lock aura-ui-1.21\run\agent-run.lock, и также не запускай, если в aura-ui\run\agent-run.lock написано claude-for-user (пользователь играет на этом ПК). Отчёт в файл. Доска — PIPELINE.md, строка «Порт на 1.21.1».

TARGET: aura-ui-1.21\** (там старая копия мода; gradle.properties уже 1.21.1 / yarn 1.21.1+build.3)

ACTION:
1. Синхронизируй исходники из aura-ui\src (текущее состояние: web/HtmlBridge, WebAssets, LocalWebServer, screen/McefHtmlScreen+HtmlScreen, player/**, lyrics/**, hud/**, mixin/**, render/AuraRenderer+Blur, AuraSplash) в aura-ui-1.21\src и адаптируй под 1.21.1 (yarn): Identifier.of(...) вместо new Identifier, изменения DrawContext/RenderSystem/BufferBuilder (Tessellator.begin → BufferBuilder, end/draw через BufferRenderer.drawWithGlobalProgram), PostEffectProcessor API, Screen.renderBackground сигнатура, mouseScrolled(double,double,double,double), KeyBinding/options API, IntegratedServerLoader изменения, ChatHud. Каждое отличие версии — в отдельном классе-адаптере, чтобы потом вынести общий код.
2. MCEF для 1.21.1: найди официальный релиз mcef-fabric для 1.21.1 (Modrinth/GitHub CinemaMod), положи в aura-ui-1.21\libs, подключи как в 1.20.1 (modCompileOnly + modLocalRuntime), fabric.mod.json suggests mcef. Источник и версию — в отчёт.
3. syncWeb так же, как в aura-ui\build.gradle (страницы общие из docs\redesign\ingame).
4. Запиши в отчёт таблицу: класс → что пришлось менять для 1.21.1. Это основа будущего общего модуля.

DONT: git checkout/restore/stash/reset/clean; не трогать aura-ui\ и docs\redesign\ingame\*.html; не коммитить; не пушить.

VERIFY: aura-ui-1.21\gradlew build зелёный; runClient 1.21.1 (когда можно): ESC HTML открывается, настройки применяются (BRIDGE op=set в логе), M — плеер, плашка HUD, слова в воздухе (демо). Кадры docs\redesign\ingame\shots\port121-*.png.

REPORT в docs\redesign\reports\agy-port-121-report.md: что перенесено, таблица отличий, источник MCEF 1.21.1, хвост сборки, что не работает — честно. Последней строкой: ГОТОВО

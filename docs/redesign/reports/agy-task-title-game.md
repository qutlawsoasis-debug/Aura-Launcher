Задание от Claude. Параллельно: чат «плеер» пишет aura-ui/.../player/** и KeyboardMixin, чат «HUD» пишет HudOverlay и InGameHudMixin — их файлы НЕ трогай. Отчёт в файл, не в чат.

TARGET (только эти файлы):
- aura-ui\src\main\java\com\auramc\auraui\web\HtmlBridge.java (новые op)
- aura-ui\src\main\java\com\auramc\auraui\web\WebAssets.java (URL для title.html и player.html)
- aura-ui\src\main\java\com\auramc\auraui\screen\HtmlScreen.java, McefHtmlScreen.java (режим «главный экран»)
- aura-ui\src\main\java\com\auramc\auraui\mixin\TitleScreenMixin.java
- aura-ui\build.gradle (syncWeb: добавить title.html, player.html, hud.html)

ISSUE: Главный экран в игре — ванильный/NanoVG. Нужен наш title.html (уже умеет протокол, см. docs\redesign\reports\agy-task-title-js.md) с настоящими мирами и серверами. Коммит c711397 — рабочий мост (важно: роутер регистрируется один раз на MCEF.getClient().getHandle() ДО создания браузера, не ломай это).

ACTION:
1. HtmlBridge — новые op строго по протоколу из agy-task-title-js.md: worlds (LevelStorage: имя папки, displayName, gameMode, lastPlayed, размер папки в МБ, icon.png → data:image/png;base64 или null), servers (ServerList.loadFile), ping (MultiplayerServerListPinger в фоне, ответ через callback, таймаут 3 с), play_world (как SelectWorldScreen: client.createIntegratedServerLoader().start(screen, id)), connect (ConnectScreen.connect), add_server (ServerList.add + saveFile, rp → ResourcePackPolicy), action create_world (CreateWorldScreen.create), options (OptionsScreen), quit (scheduleStop). Плюс op lyric: {"op":"lyric","text":"…"} → com.auramc.auraui.lyrics.FloatingLyrics.show(text) (класс уже есть).
2. HtmlScreen.title() → McefHtmlScreen в режиме title: URL title.html?screen=worlds&ingame=1, без размытия мира (мира нет), Esc ничего не делает, shouldPause false.
3. TitleScreenMixin: при HtmlScreen.isEnabled() открывать HtmlScreen.title(), иначе как сейчас.
4. syncWeb: включить title.html, player.html, hud.html.

DONT: git checkout/restore/stash/reset/clean; не трогать FloatingLyrics, index.html/title.html/player.html/hud.html, MinecraftClientMixin, NanoVG-экраны; не коммитить; не пушить.

VERIFY: aura-ui\gradlew build; aura-ui\gradlew runClient -PauraHtml=true БЕЗ quickPlay (временно запусти с -PnoQuick или убери аргумент только у себя в командной строке — build.gradle quickPlay не удалять): главный экран — наш хаб, в нём TestWorld с настоящей датой; клик «Играть» загружает мир. F2-кадр хаба → docs\redesign\ingame\shots\game-title.png. Строки [aura-ui] BRIDGE op=worlds/servers/play_world в latest.log.

REPORT в docs\redesign\reports\agy-title-game-report.md: diff --stat, хвост build, цитаты BRIDGE, путь кадра, что не сделал — честно. Последней строкой: ГОТОВО

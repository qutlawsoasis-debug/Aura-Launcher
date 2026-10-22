Задание от Claude. Параллельно: чат «главный экран» правит HtmlBridge/WebAssets/McefHtmlScreen/TitleScreenMixin/build.gradle (он же добавит op lyric → FloatingLyrics.show и player.html в syncWeb), чат «HUD» пишет HudOverlay/InGameHudMixin — их файлы НЕ трогай. Отчёт в файл, не в чат.

TARGET (только эти файлы):
- новые aura-ui\src\main\java\com\auramc\auraui\player\PlayerService.java, PlayerScreen.java
- новый aura-ui\src\main\java\com\auramc\auraui\mixin\KeyboardMixin.java
- aura-ui\src\main\resources\aura-ui.mixins.json — ТОЛЬКО добавить строку "KeyboardMixin" (перечитай файл прямо перед правкой: его может менять другой агент)
- aura-ui\src\main\java\com\auramc\auraui\lyrics\FloatingLyrics.java — только вынести демо-обвязку (телепорт, смена времени, авто-скриншоты) в отдельный класс lyrics\LyricsDemo.java

ISSUE: Плеер (docs\redesign\ingame\player.html) должен жить в игре: музыка играет в фоне, пока ходишь по миру; клавиша открывает его; строки песни летят в мир (FloatingLyrics).

ACTION:
1. PlayerService (синглтон): один постоянный MCEF-браузер (прозрачный) с URL WebAssets-распаковки player.html (?ingame=1). Создаётся лениво при первом открытии, не закрывается при закрытии экрана (музыка продолжает играть), закрывается при выходе из игры. Размер = кадр 1:1. Пока экран закрыт — браузер не рисуется на экран (текстуру не трогаем), но звук идёт.
2. PlayerScreen: показывает текстуру браузера на весь экран поверх размытого мира (как McefHtmlScreen — посмотри его render и проброс ввода, скопируй подход), Esc — закрыть экран (музыка продолжает), пауза игры НЕ включается.
3. KeyboardMixin: клавиша M (GLFW_KEY_M) в игре без открытого экрана → открыть PlayerScreen. Только если HtmlScreen.isEnabled().
4. Тексты: player.html уже шлёт {"op":"lyric",...} — обработку в HtmlBridge добавит другой агент; ты проверь, что строки летят в мир, когда его часть появится (если не появилась к моменту проверки — временно проверь через FloatingLyrics.show из PlayerService и честно напиши).
5. LyricsDemo: вынести из FloatingLyrics весь код демо (телепорт, время, авто-F2, FPS-лог) — в боевом классе остаются только show/render.

DONT: git checkout/restore/stash/reset/clean; не трогать HtmlBridge, WebAssets, McefHtmlScreen, HtmlScreen, TitleScreenMixin, build.gradle, html-файлы; не коммитить; не пушить.

VERIFY: aura-ui\gradlew build; runClient -PauraHtml=true: M → плеер; найти и включить трек; Esc → в мире музыка играет; строки текста появляются в мире. F2-кадры: docs\redesign\ingame\shots\game-player.png (экран плеера), game-player-lyrics.png (мир со словами). FPS F3 в мире с играющей музыкой и без.

REPORT в docs\redesign\reports\agy-player-game-report.md: diff --stat, хвост build, пути кадров, FPS, что не сделал — честно. Последней строкой: ГОТОВО

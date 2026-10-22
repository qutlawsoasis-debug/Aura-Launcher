Задание от Claude. Это эксперимент с замером: HUD на HTML поверх игры. Параллельно: чат «главный экран» правит HtmlBridge/WebAssets/McefHtmlScreen/build.gradle, чат «плеер» пишет player/** и KeyboardMixin — их файлы НЕ трогай. Отчёт в файл, не в чат.

TARGET (только эти файлы):
- новый aura-ui\src\main\java\com\auramc\auraui\hud\HudOverlay.java
- новый aura-ui\src\main\java\com\auramc\auraui\mixin\InGameHudMixin.java
- aura-ui\src\main\resources\aura-ui.mixins.json — ТОЛЬКО добавить строку "InGameHudMixin" (перечитай файл прямо перед правкой)
- docs\redesign\ingame\hud.html — только добавить режим ?ingame=1 (без фона мира, без моков) и функцию window.auraHud.update(data)

ISSUE: Хотим понять, можно ли рисовать HUD (docs\redesign\ingame\hud.html) через Chromium поверх игры без потери FPS. Нужна рабочая версия и честный замер.

ACTION:
1. hud.html ?ingame=1: прозрачный фон, без мира и без симуляции; данные только из window.auraHud.update({fps, x,y,z, facing, biome, netherX, netherZ, time, session, keys:{w,a,s,d,lmb,rmb}, cpsL, cpsR, hp, maxHp, food, xp, level, slot, target:{name,hp,maxHp,dist}|null}).
2. HudOverlay: один постоянный прозрачный MCEF-браузер с hud.html?ingame=1 (URL: распаковка WebAssets → <run>/aura-ui-web/hud.html; если файла ещё нет — путь docs через свойство -Daura.hud.url для теста). Раз в 100 мс собирать данные из client.player/world/options и вызывать browser.executeJavaScript("auraHud.update(" + json + ")"). Нажатия WASD/ЛКМ/ПКМ — из client.options.*Key.isPressed(), CPS — счётчик кликов за 1 с.
3. InGameHudMixin: в InGameHud.render (TAIL) рисовать текстуру браузера на весь экран (как McefHtmlScreen.render; премультиплицированная альфа ONE / ONE_MINUS_SRC_ALPHA). Только если HtmlScreen.isEnabled() и Boolean.getBoolean("aura.hud.html"). Ванильный HUD при этом не отключать (это следующий шаг).
4. Замер: F3 FPS 3 раза в одном месте: без HTML-HUD, с HTML-HUD. Если просадка > 10% — попробуй ограничить частоту кадров браузера (CefBrowserSettings/windowless_frame_rate = 30, если MCEF позволяет) и замерь снова.

DONT: git checkout/restore/stash/reset/clean; не трогать чужие файлы из шапки; не коммитить; не пушить.

VERIFY: aura-ui\gradlew build; runClient -PauraHtml=true с vmArg -Daura.hud.html=true (передай через командную строку gradle, build.gradle не правь: используй JAVA_TOOL_OPTIONS или -Dorg.gradle.jvmargs нельзя — если не выходит, временно читай флаг из файла run\config\aura-hud-html.txt). F2-кадр с HUD в мире днём → docs\redesign\ingame\shots\game-hud.png. FPS-таблица.

REPORT в docs\redesign\reports\agy-hud-game-report.md: diff --stat, хвост build, путь кадра, таблица FPS (без / с / с ограничением), вывод одной фразой: годится ли HTML-HUD. Последней строкой: ГОТОВО

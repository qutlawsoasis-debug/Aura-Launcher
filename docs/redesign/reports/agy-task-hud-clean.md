Задание от Claude (по живой проверке пользователя). Это дополнение к твоей текущей задаче HTML-HUD. Параллельно другие чаты правят McefHtmlScreen/HtmlBridge/index.html и player/**/player.html. Их файлы НЕ трогай. runClient — только через aura-ui\run\agent-run.lock (если там claude-for-user — НЕ запускать). Отчёт в файл.

TARGET: hud\HudOverlay.java, mixin\InGameHudMixin.java, docs\redesign\ingame\hud.html

Пожелание пользователя дословно по смыслу: на экране игры убрать ВСЁ — хотбар, сердца, голод, опыт, броню, пузыри, клавиши WASD, координаты, цель, тосты — оставить только одну плашку вверху по центру: «Aura · FPS · время» (ту, что сейчас слева сверху), поставить её на место центральной плашки с часами/таймером сессии (её убрать). Прицел оставить (без него нельзя играть) — тонкий, в стиле Aura.

ACTION:
1. InGameHudMixin: при включённом HTML-HUD отменить отрисовку ванильных частей (renderHotbar, renderStatusBars, renderExperienceBar, renderMountHealth/JumpBar, renderHeldItemTooltip, statusEffectOverlay) — через @Inject HEAD cancel соответствующих методов InGameHud 1.20.1 (yarn). Чат, табло игроков (Tab), заголовки/субтитры, F3 и экран смерти — оставить.
2. hud.html ?ingame=1: показывать только верхнюю центральную плашку (пилюля с точкой-акцентом, «Aura», FPS с мини-графиком, время) + прицел по центру; всё остальное скрыть. Появление при входе в мир — fade 300 мс.
3. Если FPS-замер из твоей текущей задачи показал просадку > 10% — для одной плашки браузер не нужен: тогда нарисуй плашку нативно (DrawContext/NanoVG есть в проекте — AuraRenderer) и честно напиши в отчёте, какой путь выбрал и почему.

DONT: git checkout/restore/stash/reset/clean; не трогать чужие файлы; не коммитить; не пушить.

VERIFY: gradlew build; runClient с HTML-HUD: F2 → docs\redesign\ingame\shots\game-hud-clean.png (видна только плашка сверху по центру и прицел). FPS F3 с HUD и без.

REPORT: допиши в agy-hud-game-report.md раздел «Чистый HUD» (что скрыто, какой путь, FPS, кадр). Последней строкой файла: ГОТОВО

Задание от Claude. Параллельно чат «главный экран» правит HtmlScreen/McefHtmlScreen/HtmlBridge/index.html/mixins.json, чат «HUD» — hud/** и InGameHudMixin. Их файлы НЕ трогай. runClient — только через lock-файл aura-ui\run\agent-run.lock. Отчёт в файл, не в чат.

TARGET (только эти файлы):
- aura-ui\src\main\java\com\auramc\auraui\lyrics\FloatingLyrics.java
- aura-ui\src\main\java\com\auramc\auraui\lyrics\LyricsDemo.java
- aura-ui\src\main\java\com\auramc\auraui\player\PlayerService.java (только если нужно)

ISSUE: На кадре game-player-lyrics.png слова наезжают друг на друга («that» на «tired») и обрезаются блоками; кадры сняты под водой ночью.

ACTION:
1. Раскладка без наложений: позиции слов выбирать в экранных координатах (проекция центра слова), хранить прямоугольники уже показанных слов (с отступом 12px), до 12 попыток подобрать свободное место, иначе — строка «столбиком» ниже предыдущего слова. Слова одной строки появляются группой в одной области экрана (не по всему экрану), следующая строка — в другой области.
2. Читаемость: если между камерой и словом есть блок ближе 2 блоков — слово рисуется без depth-test (видно сквозь листву вплотную), иначе с depth-test.
3. Кириллица: проверить строку «Я так хочу летать» — ванильный шрифт её рисует; размер не меньше латиницы.
4. LyricsDemo: перед кадрами — день (6000) и поверхность суши (как раньше), без воды.

DONT: git checkout/restore/stash/reset/clean; не трогать чужие файлы; не коммитить; не пушить.

VERIFY: gradlew build; runClient с демо (run\config\aura-lyrics-demo.txt): 2 кадра → docs\redesign\ingame\shots\lyrics3d-v4-a.png, lyrics3d-v4-b.png (на одном — русская строка). Удалить aura-lyrics-demo.txt в конце.

REPORT в docs\redesign\reports\agy-lyrics-polish-report.md: что изменено, кадры, что не сделал — честно. Последней строкой: ГОТОВО

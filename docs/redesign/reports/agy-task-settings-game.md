Задание от Claude. Параллельно чат «HUD» пишет HudOverlay/InGameHudMixin, чат «плеер» — player/** и FloatingLyrics. Их файлы НЕ трогай. Не запускай runClient без lock-файла aura-ui\run\agent-run.lock (правило прежнее). Отчёт в файл, не в чат.

TARGET (только эти файлы):
- aura-ui\src\main\java\com\auramc\auraui\screen\HtmlScreen.java, McefHtmlScreen.java (режим «настройки»)
- aura-ui\src\main\java\com\auramc\auraui\web\HtmlBridge.java (action options → наши настройки)
- новый aura-ui\src\main\java\com\auramc\auraui\mixin\OptionsScreenMixin.java + строка в aura-ui.mixins.json (перечитай файл перед правкой)
- docs\redesign\ingame\index.html — только режим ?view=settings&standalone=1

ISSUE: Кнопка «Настройки» на нашем главном экране и любые ванильные «Настройки» открывают старое окно Minecraft. Должны открываться наши HTML-настройки (index.html, вид settings).

ACTION:
1. McefHtmlScreen: режим settings — URL index.html?view=settings&ingame=1&standalone=1; если открыт из главного меню (мира нет) — без размытия, фон тёмный (страница сама рисует фон, см. п.3); Esc на верхнем уровне настроек → вернуться на предыдущий экран (parent), а не в игру.
2. HtmlBridge: action options → client.setScreen(HtmlScreen.settings(текущий экран)); плитка «Настройки» в ESC уже открывает вид settings внутри страницы — это оставить.
3. index.html standalone=1: при Esc на верхнем уровне настроек — cefQuery action "back" (новый, в Java → setScreen(parent)); фон без мира: тёмный градиент + лёгкий шум, чтобы стекло читалось.
4. OptionsScreenMixin: OptionsScreen.init HEAD → если HtmlScreen.isEnabled(): открыть HtmlScreen.settings(parent) и отменить. Внимание: «Управление», «Видео» и т.п. подэкраны ванильных настроек не трогать.

DONT: git checkout/restore/stash/reset/clean; не трогать чужие файлы; не коммитить; не пушить.

VERIFY: gradlew build; runClient -PauraHtml=true: из главного экрана шестерёнка → наши настройки → Esc → обратно в хаб; в мире Esc → пауза → «Настройки» → Esc → пауза. F2: docs\redesign\ingame\shots\game-settings-title.png. Строки BRIDGE action=options/back в latest.log.

REPORT в docs\redesign\reports\agy-settings-game-report.md: diff --stat, хвост build, цитаты логов, кадр, что не сделал — честно. Последней строкой: ГОТОВО

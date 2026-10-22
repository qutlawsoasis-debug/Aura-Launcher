Задание от Claude (архитектор). Параллельно другой агент правит docs\redesign\ingame\index.html — его НЕ трогай. Отчёт пиши в файл, не в чат.

TARGET (только эти файлы):
- aura-ui\src\main\java\com\auramc\auraui\screen\McefHtmlScreen.java (обработка cefQuery)
- новый aura-ui\src\main\java\com\auramc\auraui\web\HtmlBridge.java (протокол ниже)
- новый aura-ui\src\main\java\com\auramc\auraui\web\WebAssets.java (распаковка страницы)
- aura-ui\build.gradle (задача syncWeb)
- aura-ui\src\main\resources\assets\aura-ui\web\** (генерируется syncWeb, руками не править)

ISSUE: Прототип HTML-паузы (коммит 49f179e, флаг -Daura.html=true) грузит страницу с localhost:5180 и умеет только resume/main_menu. Нужно: страница внутри мода + настоящие настройки игры через мост.

ACTION:
1. build.gradle: задача syncWeb (Copy) перед processResources: docs/redesign/ingame/index.html → assets/aura-ui/web/index.html; docs/redesign/v3/public/assets/fonts/Onest.woff2 → web/fonts/; нужные картинки bg_1,2,5,6,7,9,11,12,13,14,15.jpg и Coock0ld.png → web/images/. Пути в index.html правит ДРУГОЙ агент на ./fonts/… и ./images/…; lucide он тоже кладёт локально (web/lucide.min.js копировать из docs/redesign/ingame/lucide.min.js).
2. WebAssets: при первом открытии распаковать assets/aura-ui/web/** в <runDir>/aura-ui-web/ (перезаписывать, если версия мода изменилась) и отдавать URL file:///…/index.html?view=esc&ingame=1. Свойство -Daura.html.url по-прежнему переопределяет URL.
3. HtmlBridge — запрос из JS это строка JSON, ответ — строка JSON через callback.success:
   - {"op":"get"} → {"values":{id:value,...},"keys":[{"name":"Вперёд","key":"W"},...],"player":"<ник>","world":"<имя мира или сервера>","fps":N}
   - {"op":"set","id":"<id>","value":<v>} → применить к client.options, options.write(), ответ {"ok":true} или {"ok":false,"error":"..."}
   - {"op":"action","name":"resume|main_menu|open_lan|advancements|stats|screenshots_folder"} → resume/main_menu как сейчас; open_lan → OpenToLanScreen; advancements → AdvancementsScreen; stats → StatsScreen; screenshots_folder → Util.getOperatingSystem().open(папка screenshots). invite → {"ok":false,"error":"not_ready"}.
   Таблица id → опция (значения — как в макете):
   render→viewDistance (2..32); sim→simulationDistance (5..32); quality→graphicsMode fast|fancy|fabulous; fps→maxFps ("0" = 260); vsync→enableVsync; fov→fov (30..110); bright→gamma (0..100 ⇄ 0.0..1.0); particles→particles min|dec|all (MINIMAL|DECREASED|ALL); clouds→cloudRenderMode off|fast|fancy; master/music/blocks/mobs/players→громкость категорий MASTER/MUSIC/BLOCKS/HOSTILE/PLAYERS (0..100 ⇄ 0.0..1.0); sens→mouseSensitivity (1..200% ⇄ 0.0..1.0, 100% = 0.5); toggleSprint→sprintToggled; raw→rawMouseInput; chatOpacity→chatOpacity (0..100 ⇄ 0.0..1.0); language→ru_ru|uk_ua|en_us (ru|uk|en) + client.reloadResources().
   Неизвестный id → {"ok":false,"error":"unknown_id"}. Всё, что трогает игру, — через client.execute.
4. Логи: каждая операция одной строкой [aura-ui] BRIDGE op=… id=… value=… ok=…

DONT: git checkout/restore/stash/reset/clean; не трогать docs\redesign\ingame\index.html, AuraPauseScreen, NanoVG-код, лаунчер; не коммитить; не пушить.

VERIFY: aura-ui\gradlew build; aura-ui\gradlew runClient -PauraHtml=true (страница должна открыться уже без сервера 5180 — останови его или проверь, что URL file:///). В паузе сменить прорисовку и громкость → в run\options.txt изменились renderDistance и soundCategory_master. Строки BRIDGE в run\logs\latest.log.

REPORT в docs\redesign\reports\agy-bridge-java-report.md: diff --stat, хвост build, цитаты BRIDGE из latest.log, renderDistance/soundCategory_master из options.txt до и после, что не сделал — честно. Последней строкой: ГОТОВО

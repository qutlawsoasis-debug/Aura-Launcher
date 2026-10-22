Задание от Claude (архитектор). Работаешь параллельно с другими агентами — трогай ТОЛЬКО свои файлы. Отчёт пиши в файл, не в чат.

TARGET:
- Новый файл: C:\Users\magne\Documents\GitHub\Aura-Launcher\docs\redesign\ingame\title.html
- Кадры: C:\Users\magne\Documents\GitHub\Aura-Launcher\docs\redesign\ingame\shots\title-*.png
- Эталон стиля (только читать): docs\redesign\ingame\index.html — токены :root, .glass, .btn-play, .pcard, шрифт Onest, иконки lucide@0.460.0, картинки ../v3/public/assets/images/bg_*.jpg
- Референс (только смотреть): C:\Users\magne\Downloads\референс для клодика\video_2026-10-22_12-43-33.mp4 (meadow, главный экран как у PS5)

ISSUE: Нужен HTML-макет главного экрана игры (то, что видно после запуска Minecraft) в стиле PS5-хаба, в цветах Aura. Не копия лаунчера: без капсулы «Острова».

ACTION:
1. title.html, экраны (переключение без перезагрузки, плавно):
   a) заставка-приветствие: «Добро пожаловать, Coock0ld» + аватар (../v3/public/assets/images/Coock0ld.png, лицо скина, image-rendering: pixelated), через 2 с уходит в хаб;
   b) хаб как у PS5: сверху вкладки «Одиночная игра / Сетевая игра», справа часы и аватар; ряд плиток (миры или серверы) — выбранная плитка крупнее и с акцентной рамкой, стрелки ←/→ и мышь двигают выбор; фон — большой арт выбранного мира (bg_*.jpg), плавно меняется (crossfade); снизу-слева название, описание, «Играть» (.btn-play) и «…»;
   c) окно «Добавить сервер» поверх размытого фона (имя, адрес, ресурспак: Спрашивать/Да/Нет, кнопка «Добавить»), закрытие по Esc;
   d) экран загрузки мира: логотип/название мира, прогресс-полоса акцентом, подпись этапа.
2. Данные моковые: 4 мира (Новый мир, Креатив, Остров, Хардкор) и 3 сервера (Aura · Новый мир, Hypixel, 2b2t).
3. Анимации только transform/opacity; один живой backdrop-filter на экран максимум.
4. Кадры headless Chrome 1920×1080: title-welcome.png, title-hub-worlds.png, title-hub-servers.png, title-add-server.png, title-loading.png (сделай query-параметр ?screen=... для каждого). Команда: "C:\Program Files\Google\Chrome\Application\chrome.exe" --headless=new --window-size=1920,1080 --virtual-time-budget=4000 --screenshot=<путь> "http://localhost:5180/ingame/title.html?screen=..." (сервер на 5180 из docs\redesign; если не отвечает — python -m http.server 5180 --directory docs/redesign).

DONT: git checkout/restore/stash/reset/clean; не трогать index.html, hud.html и любые другие файлы кроме title.html и shots/title-*.png; не коммитить; не пушить; не подключать новые библиотеки кроме lucide.

VERIFY: в консоли 0 ошибок; все 5 кадров реальные и разные; стрелки и Esc работают.

REPORT в C:\Users\magne\Documents\GitHub\Aura-Launcher\docs\redesign\reports\agy-title-report.md: экраны, пути кадров, ошибки консоли (или «0»), что не сделал — честно. Последней строкой: ГОТОВО

Задание от Claude (архитектор). Работаешь параллельно с другими агентами — трогай ТОЛЬКО свои файлы. Отчёт пиши в файл, не в чат.

TARGET:
- Новый файл: C:\Users\magne\Documents\GitHub\Aura-Launcher\docs\redesign\ingame\hud.html
- Кадры: C:\Users\magne\Documents\GitHub\Aura-Launcher\docs\redesign\ingame\shots\hud-*.png
- Эталон стиля (только читать): docs\redesign\ingame\index.html — токены :root, .pill, .hud-card, шрифт Onest, иконки lucide@0.460.0, фон ../v3/public/assets/images/bg_13.jpg
- Референс (только смотреть): C:\Users\magne\Downloads\референс для клодика\video_2026-10-22_12-43-30.mp4 (Mykvador, HUD слева и сверху)

ISSUE: Нужен HTML-макет игрового HUD в стиле Aura (тёмное стекло, акцент var(--accent) #64D2FF), как у Mykvador, но аккуратнее.

ACTION:
1. hud.html: мир на весь экран (bg_13.jpg), поверх — HUD-виджеты:
   - сверху слева: пилюля «Aura · FPS · время» с мини-графиком FPS;
   - слева: карточка координат / направления / биома / координат Ада (иконки lucide);
   - сверху по центру: часы и таймер сессии;
   - слева снизу: клавиши W A S D + ЛКМ/ПКМ с CPS, нажатая клавиша подсвечивается акцентом (имитировать нажатия от клавиатуры);
   - справа: карточка цели (голова, ник, полоска здоровья) — появляется/исчезает с анимацией;
   - справа сверху: уведомления-тосты (друг зашёл, достижение) — выезжают и уходят;
   - снизу по центру: хотбар 9 слотов + полоски здоровья/голода/опыта своим стилем.
2. Все цифры «живые» (FPS, CPS, время меняются раз в секунду через JS). Анимации только transform/opacity.
3. Переключатель режима редактирования (клавиша H): виджеты можно перетаскивать мышью, позиция сохраняется в localStorage.
4. Без backdrop-filter у виджетов (только полупрозрачная заливка + рамка 1px + блик сверху), как .hud-card в index.html.
5. Кадры headless Chrome 1920×1080: hud-default.png, hud-edit.png, hud-target.png. Команда: "C:\Program Files\Google\Chrome\Application\chrome.exe" --headless=new --window-size=1920,1080 --virtual-time-budget=4000 --screenshot=<путь> "http://localhost:5180/ingame/hud.html?..." (сервер уже поднят на порту 5180 из docs\redesign; если не отвечает — python -m http.server 5180 --directory docs/redesign).

DONT: git checkout/restore/stash/reset/clean; не трогать index.html и любые другие файлы кроме hud.html и shots/hud-*.png; не коммитить; не пушить; не подключать новые библиотеки кроме lucide.

VERIFY: открыть страницу, в консоли 0 ошибок; кадры реальные (не нарисованные скриптом поверх), на них видны все виджеты.

REPORT в C:\Users\magne\Documents\GitHub\Aura-Launcher\docs\redesign\reports\agy-hud-report.md: список виджетов, пути кадров, ошибки консоли (или «0»), что не сделал — честно. Последней строкой: ГОТОВО

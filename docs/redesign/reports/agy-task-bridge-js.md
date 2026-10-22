Задание от Claude (архитектор). Параллельно другой агент пишет Java-мост в aura-ui — его файлы НЕ трогай. Отчёт пиши в файл, не в чат.

TARGET (только эти файлы):
- docs\redesign\ingame\index.html
- новый docs\redesign\ingame\lucide.min.js (lucide@0.460.0, dist/umd/lucide.min.js с unpkg.com)

ISSUE: Макет ESC/настроек внутри игры (режим ?ingame=1) показывает выдуманные значения. Нужно брать и менять настоящие через мост window.cefQuery, а в браузере (без ?ingame) работать как сейчас.

ACTION:
1. Пути: шрифт → ./fonts/Onest.woff2, картинки → ./images/<имя>.jpg|png, lucide → <script src="./lucide.min.js">. Чтобы в браузере макет по-прежнему открывался с http://localhost:5180/ingame/, положи рядом: docs\redesign\ingame\fonts\ и images\ НЕ создавай — вместо этого: если location.protocol начинается с http → использовать старые пути ../v3/public/assets/…, иначе ./… (одна функция asset(path)). Для @font-face сделать то же через JS (FontFace API) или два src.
2. Протокол (строки JSON через window.cefQuery({request, onSuccess(resp), onFailure(code,msg)})):
   - при открытии (INGAME): {"op":"get"} → {values, keys, player, world, fps}. Подставить values в S по id (render, sim, quality, fps, vsync, fov, bright, particles, clouds, master, music, blocks, mobs, players, sens, toggleSprint, raw, chatOpacity, language); keys → список клавиш вместо выдуманного; player → ник в шапке ESC и в футере; world → название мира.
   - изменение настройки (слайдер — по отпусканию мыши, чипы/переключатели — сразу): {"op":"set","id":…,"value":…}. При ok:false — откатить значение и показать тост с ошибкой (маленькая пилюля снизу, 2 с).
   - плитки ESC: Открыть для сети → action open_lan, Достижения → advancements, Статистика → stats, Скриншоты → screenshots_folder, Пригласить → invite (при not_ready тост «Скоро»).
   - То, чего нет в игре (тема, HUD, масштаб, эффекты, анимации, пресеты, шейдеры, конфиги, DH, голосовой чат, время/фильтр чата) — оставить локальными, но тему и эффекты сохранять в localStorage.
3. Без ?ingame всё работает как сейчас (моки, без ошибок в консоли).

DONT: git checkout/restore/stash/reset/clean; не трогать aura-ui\**, другие страницы (hud.html, title.html); не менять внешний вид, кроме тоста; не коммитить; не пушить.

VERIFY: http://localhost:5180/ingame/index.html?view=settings — в консоли 0 ошибок, всё как раньше. Проверить протокол без игры: в консоли браузера подменить window.cefQuery заглушкой, которая логирует request и отвечает фиксированным JSON — убедиться, что get подставляет значения, set уходит при изменении.

REPORT в docs\redesign\reports\agy-bridge-js-report.md: что изменено (по пунктам), лог запросов из заглушки (get, 3 разных set, 2 action), ошибки консоли (или «0»), что не сделал — честно. Последней строкой: ГОТОВО

Задание от Claude (архитектор). Параллельно другие агенты правят aura-ui, index.html, title.html — их НЕ трогай. Отчёт пиши в файл, не в чат.

TARGET (только эти файлы):
- новый docs\redesign\ingame\player.html
- кадры docs\redesign\ingame\shots\player-*.png
- Стиль (только читать): docs\redesign\ingame\index.html (токены :root, .glass, .pcard, .chip, .slider, asset(), sendQuery, локальный ./lucide.min.js)
- Референсы (только смотреть): C:\Users\magne\Downloads\референс для клодика\ssstik.io_1792676001631 - Trim.mp4 (мини-плеер в углу и слова песни, парящие в мире); стиль — Apple Music (полноэкранный «Сейчас играет» с размытой обложкой на фоне и крупным синхронным текстом).

ISSUE: Нужен музыкальный плеер внутри игры: поиск песни сразу по нескольким площадкам, воспроизведение, синхронный текст в стиле Apple Music. Этот файл потом откроется в игре через Chromium (MCEF), звук играет сам Chromium.

ACTION:
1. player.html — экраны (переход плавный):
   a) Поиск: большое поле, чипы источников (Все · Audius · Jamendo · iTunes · Deezer), результаты одной лентой: обложка, название, артист, длительность, бейдж источника и «полный трек / превью 30 с». Дебаунс 300 мс.
   b) Сейчас играет (Apple Music): фон — размытая обложка (CSS filter на <img>, НЕ backdrop-filter) + медленное «дыхание» цвета; слева большая обложка с тенью, название/артист, прогресс, кнопки ⏮ ⏯ ⏭, громкость; справа синхронный текст: текущая строка крупная белая, прошлые/будущие тусклее и мельче, плавная прокрутка к текущей (transform), клик по строке — перемотка. Если синхронного текста нет — обычный текст, если нет совсем — «Текст не найден».
   c) Очередь: список следующих треков, перетаскивание порядка.
   d) Мини-плеер (?mini=1): плашка для HUD 360×72 — обложка, название, артист, тонкий прогресс, ⏯. Прозрачный фон страницы.
2. Источники (реальные, без ключей, через fetch/JSONP; если CORS не пускает — JSONP или пометка в отчёте):
   - Audius: https://api.audius.co → выбрать хост, /v1/tracks/search?query=…&app_name=aura ; поток /v1/tracks/<id>/stream?app_name=aura (полные треки).
   - Jamendo: нужен client_id — если нет, пропустить источник и написать в отчёте.
   - iTunes: https://itunes.apple.com/search?term=…&entity=song&limit=20 (callback= для JSONP), previewUrl — 30 с.
   - Deezer: https://api.deezer.com/search?q=…&output=jsonp , preview — 30 с.
   - Тексты: LRCLIB https://lrclib.net/api/get?artist_name=…&track_name=…&duration=… (syncedLyrics в формате [mm:ss.xx]); при неудаче /api/search?q=…
   Объединение: дубликаты (одинаковые артист+название) склеивать, предпочитая полный трек.
3. Воспроизведение через <audio>; события для игры: при смене строки текста вызывать window.cefQuery({request: JSON.stringify({op:"lyric", text:"<строка>", t:<сек>}), …}) если cefQuery есть (иначе console.debug). Это для будущих «слов в воздухе».
4. Анимации только transform/opacity; 0 ошибок в консоли; клавиши: / — поиск, пробел — пауза, ←/→ — ±5 с, Esc — назад.

DONT: git checkout/restore/stash/reset/clean; не трогать другие файлы; не подключать библиотеки кроме lucide; не использовать YouTube/Яндекс/VK/SoundCloud (только перечисленные API); не коммитить; не пушить.

VERIFY: http://localhost:5180/ingame/player.html — поиск «Imagine Dragons Believer» и «lofi» даёт результаты минимум из 2 источников; трек играет; у Believer показывается синхронный текст и подсвечивается текущая строка. Кадры headless Chrome 1920×1080 (сервер 5180 из docs\redesign): player-search.png, player-now.png, player-queue.png, player-mini.png (для кадров можно параметр ?demo=1 с заранее сохранённым JSON-ответом, но поиск в браузере должен работать вживую).

REPORT в docs\redesign\reports\agy-player-report.md: какие источники работают (с примером ответа), какие нет и почему, пути кадров, ошибки консоли (или «0»), что не сделал — честно. Последней строкой: ГОТОВО

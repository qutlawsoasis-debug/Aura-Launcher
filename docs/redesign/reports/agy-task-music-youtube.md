Задание от Claude. Пользователь хочет полные треки «как в музыкальных ботах», но законно. Делаем ТОЛЬКО через официальные средства YouTube: YouTube Data API v3 (поиск) + официальный IFrame Player API (воспроизведение). Никакого извлечения аудиопотоков, никаких сторонних «загрузчиков», никаких обходов. Параллельно другие чаты правят HtmlBridge/McefHtmlScreen/index.html и hud/**. runClient — только через aura-ui\run\agent-run.lock. Отчёт в файл.

TARGET:
- новый aura-ui\src\main\java\com\auramc\auraui\web\LocalWebServer.java
- aura-ui\src\main\java\com\auramc\auraui\player\PlayerService.java (URL плеера)
- docs\redesign\ingame\player.html

ISSUE: Официальный плеер YouTube не работает со страниц file:// (нужен http-origin). Поиск — через Data API с ключом пользователя.

ACTION:
1. LocalWebServer: com.sun.net.httpserver.HttpServer на 127.0.0.1, случайный свободный порт, отдаёт файлы из <run>/aura-ui-web (только чтение, только GET, защита от ../, корректные Content-Type). Стартует лениво из PlayerService, останавливается при выходе из игры. PlayerService открывает http://127.0.0.1:<port>/player.html?ingame=1.
2. player.html — источник youtube в модуле StreamResolver:
   - Ключ API: поле «Ключ YouTube API» в настройках плеера (иконка шестерёнки в шапке), вводит пользователь сам; хранить через cefQuery store_set/store_get key "youtube_key" (op уже есть в HtmlBridge) и localStorage. Ключ НЕ логировать и не показывать целиком (маска ••••1234). Если ключа нет — понятная плашка «Добавьте ключ YouTube API, чтобы слушать полные треки» и ссылка-инструкция (текстом, без перехода).
   - Поиск: search.list part=snippet, type=video, videoCategoryId=10, videoEmbeddable=true, maxResults=20; Альбомы: type=playlist (q + " album"); Исполнители: type=channel. Длительности — videos.list part=contentDetails (ISO 8601 → м:сс). Очистка названий: убрать «(Official Video)», «[Official Audio]», «Lyrics» и т.п.; артист — channelTitle без « - Topic»/«VEVO».
   - Альбом/плейлист → playlistItems.list; исполнитель → его каналы: uploads-плейлист (channels.list contentDetails.relatedPlaylists.uploads).
   - Воспроизведение: официальный IFrame Player API (https://www.youtube.com/iframe_api), плеер ВИДИМЫЙ — в «Сейчас играет» на месте обложки показывать видео 16:9 со скруглением (так требуют правила YouTube — не прятать плеер). Управление (play/pause/seek/volume/next) через API плеера, прогресс — getCurrentTime/getDuration; ended → следующий трек. Ошибки 101/150 (встраивание запрещено) → пометить трек и перейти к следующему.
   - Тексты LRCLIB как раньше (по очищенным артист+название+длительность).
   - Остальные источники (iTunes/Deezer превью, Audius) — убрать из поиска, если ключ YouTube есть; без ключа — оставить как запасной режим.
   - Квота: кэшировать результаты поиска на 24 ч в localStorage (поиск стоит 100 единиц из 10 000/день).
3. op lyric при смене строки — как раньше.

DONT: git checkout/restore/stash/reset/clean; никакого извлечения потоков (yt-dlp, ytdl, разбор signature и т.п.) — только IFrame API; не добавлять второй CefMessageRouter; не трогать чужие файлы; не коммитить; не пушить.

VERIFY: gradlew build. В браузере (http://localhost:5180/ingame/player.html) без ключа — плашка про ключ, запасной режим работает, 0 ошибок консоли. С ключом проверить не сможешь (ключа нет) — сделай заглушку ответов Data API (?mockyt=1 с сохранённым JSON) и проверь весь UI: поиск, альбом, исполнитель, воспроизведение через IFrame с реальным videoId (IFrame без ключа работает) — трек играет больше 30 с. В игре (через lock): M → ?mockyt=1 → трек играет (IFrame внутри MCEF с http-origin 127.0.0.1). Кадры docs\redesign\ingame\shots\music-yt-now.png, music-yt-search.png.

REPORT в docs\redesign\reports\agy-music-youtube-report.md: что сделано, работает ли IFrame в MCEF (цитата лога/кадр), квоты, что не сделал — честно. Последней строкой: ГОТОВО

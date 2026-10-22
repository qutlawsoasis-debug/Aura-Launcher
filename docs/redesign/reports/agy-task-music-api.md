Задание от Claude. Бэкенд для плеера: поиск музыки на нашем сервере, чтобы игрокам не нужны были никакие ключи. Отчёт в файл, не в чат.

TARGET (только это):
- новые lobby-api\api\music\resolve.ts (главный), lobby-api\api\music\search.ts, lobby-api\api\music\list.ts
- lobby-api\api\_utils.ts — только если нужен общий хелпер (не ломать существующее)
- lobby-api\DEPLOY.md — дописать переменную окружения

ISSUE: Плеер в игре ищет треки через официальный YouTube Data API v3. Ключ должен жить только на сервере (переменная окружения Vercel YOUTUBE_API_KEY), игроки обращаются к нам.

ACTION:
0. СХЕМА (важно): поиск/альбомы/исполнители игрок делает через iTunes/Deezer прямо в плеере (бесплатно, без квоты). Сервер нужен ТОЛЬКО чтобы найти видео YouTube для конкретного трека в момент «Играть» и запомнить навсегда.
   Главный маршрут: GET /api/music/resolve?artist=…&title=…&durationSec=… → {"videoId":"…"} или 404. Порядок: Redis music:resolve:<artist|title lower> (без TTL — навсегда) → если нет: search.list type=video videoEmbeddable=true videoCategoryId=10 q="<artist> <title> audio" maxResults=5 + videos.list contentDetails → выбрать видео с длительностью ±7 с от durationSec, предпочитая каналы «<artist> - Topic» и официальные; сохранить в Redis навсегда. Лимит 30/мин с IP.
   Маршруты search/list ниже — делать во вторую очередь (как запасные).
1. GET /api/music/search?q=…&kind=tracks|albums|artists
   - tracks: search.list part=snippet type=video videoCategoryId=10 videoEmbeddable=true maxResults=20 + videos.list part=contentDetails (длительность); albums: type=playlist (q + " album"); artists: type=channel.
   - Нормализовать: {id, kind, title (без «(Official Video)», «[Official Audio]», «Lyrics», «Lyric Video», «HD»), artist (channelTitle без « - Topic», «VEVO»), artwork (thumbnails.high/maxres), durationSec?}.
   - Кэш в Upstash Redis (как в остальных маршрутах): ключ music:search:<kind>:<q нормализованный lower>, TTL 7 дней. Ответ с Cache-Control: s-maxage=86400.
   - Лимит: не больше 30 запросов в минуту с одного IP (Redis INCR + EXPIRE), иначе 429.
2. GET /api/music/list?id=…&kind=playlist|artist — треки плейлиста (playlistItems.list до 50) или загрузки исполнителя (channels.list contentDetails.relatedPlaylists.uploads → playlistItems), с длительностями; тот же кэш (TTL 1 день) и лимит.
3. Нет YOUTUBE_API_KEY → 503 {"error":"music_not_configured"}. Ошибки YouTube (quotaExceeded) → 503 {"error":"quota"}. Ключ никогда не возвращать и не логировать.
4. DEPLOY.md: как добавить YOUTUBE_API_KEY в Vercel (Settings → Environment Variables) и как создать ключ в Google Cloud (YouTube Data API v3, ограничение ключа по API).

DONT: git checkout/restore/stash/reset/clean; не деплоить (vercel deploy НЕ запускать); не трогать другие маршруты и C#-лаунчер; не коммитить; не пушить.

VERIFY: в lobby-api: npx tsc --noEmit без ошибок; локальная проверка обработчиков вызовом функции с фейковым fetch (замокать ответы YouTube из сохранённого JSON) — search tracks/albums/artists и list возвращают нормализованный формат; без ключа — 503.

REPORT в docs\redesign\reports\agy-music-api-report.md: формат ответов (пример JSON), вывод tsc, что не сделал — честно. Последней строкой: ГОТОВО

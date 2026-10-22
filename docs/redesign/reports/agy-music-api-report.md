# Отчёт: Бэкенд Music API (YouTube Resolve + Search/List)

## 1. Что сделано

### 1.1 Главный маршрут: `/api/music/resolve` (`lobby-api/api/music/resolve.ts`)
- Реализован эндпоинт `GET /api/music/resolve?artist=...&title=...&durationSec=...`.
- Логика работы:
  1. Проверяет наличие кэша в Redis по ключу `music:resolve:<artist|title lower>` без срока давности (навсегда). При попадании возвращает результат мгновенно с заголовком `Cache-Control: s-maxage=31536000, max-age=86400`.
  2. При отсутствии кэша проверяет наличие переменной `YOUTUBE_API_KEY`. Если ключ отсутствует — возвращает 503 `{"error":"music_not_configured"}`.
  3. Выполняет `search.list` с фильтрами `type=video`, `videoEmbeddable=true`, `videoCategoryId=10`, `q="<artist> <title> audio"`, `maxResults=5`.
  4. Выполняет пакетный запрос `videos.list` для получения точной длительности `contentDetails.duration`.
  5. Сравнивает кандидатов: выбирает видео с отклонением длительности `±7 с` от `durationSec`, отдавая наивысший приоритет трекам из каналов `<artist> - Topic` (официальные студийные аудиозаписи YouTube Music) и официальным каналам исполнителя.
  6. Сохраняет найденный `videoId` в Redis навсегда.
  7. При отсутствии видео возвращает 404 `{"error":"not_found"}`.
  8. При исчерпании квоты YouTube возвращает 503 `{"error":"quota"}`.
  9. Лимит: не более 30 запросов в минуту с одного IP. Ключ API никогда не логируется и не возвращается наружу.

### 1.2 Маршрут поиска: `/api/music/search` (`lobby-api/api/music/search.ts`)
- Реализован эндпоинт `GET /api/music/search?q=...&kind=tracks|albums|artists`.
- Форматы выдачи:
  - `tracks`: поиск видео с фильтрами категории «Музыка» и разрешённым встраиванием + получение точных длительностей через `videos.list`.
  - `albums`: поиск плейлистов `q + " album"`.
  - `artists`: поиск каналов исполнителей `type=channel`.
- Очистка метаданных: из названий удаляются плашки `(Official Video)`, `[Official Audio]`, `Lyrics`, `HD`, `4K` и т.д.; из имени артиста удаляются суффиксы ` - Topic` и `VEVO`.
- Кэширование в Redis: ключ `music:search:<kind>:<q lower>`, TTL 7 дней (604800 с).
- Ответ с `Cache-Control: s-maxage=86400`. Лимит 30 запросов в минуту с одного IP.

### 1.3 Маршрут списков: `/api/music/list` (`lobby-api/api/music/list.ts`)
- Реализован эндпоинт `GET /api/music/list?id=...&kind=playlist|artist`.
- Для `playlist`: загружает до 50 элементов плейлиста через `playlistItems.list` и запрашивает их длительности через `videos.list`.
- Для `artist`: через `channels.list` получает ID плейлиста загрузок исполнителя (`contentDetails.relatedPlaylists.uploads`) и загружает треки.
- Кэширование в Redis: ключ `music:list:<kind>:<id>`, TTL 1 день (86400 с). Лимит 30 запросов в минуту с IP.

### 1.4 Общие хелперы (`lobby-api/api/_utils.ts`)
- Добавлены функции `redisGet` и `redisSet` (обращение к Upstash REST API с fallback на in-memory хранилище при локальном тестировании).
- Добавлены `getClientIp`, `cleanTitle`, `cleanArtist`, `parseIsoDuration`.

### 1.5 Документация (`lobby-api/DEPLOY.md`)
- Добавлен раздел «3. Настройка YouTube Data API v3 для музыкального плеера»:
  - Инструкция по созданию и ограничению ключа в Google Cloud Console.
  - Команда и способ добавления переменной `YOUTUBE_API_KEY` в Vercel.

---

## 2. Форматы ответов API (примеры JSON)

### GET `/api/music/resolve?artist=Queen&title=Bohemian+Rhapsody&durationSec=355`
```json
{
  "videoId": "fJ9rUzIMcZQ"
}
```

### GET `/api/music/search?q=Bohemian+Rhapsody&kind=tracks`
```json
{
  "items": [
    {
      "id": "fJ9rUzIMcZQ",
      "kind": "track",
      "title": "Bohemian Rhapsody",
      "artist": "Queen",
      "artwork": "https://i.ytimg.com/vi/fJ9rUzIMcZQ/hqdefault.jpg",
      "durationSec": 355
    }
  ]
}
```

### GET `/api/music/search?q=Queen&kind=albums`
```json
{
  "items": [
    {
      "id": "PL_queen_opera",
      "kind": "album",
      "title": "A Night at the Opera",
      "artist": "Queen",
      "artwork": "https://i.ytimg.com/vi/fJ9rUzIMcZQ/hqdefault.jpg"
    }
  ]
}
```

### GET `/api/music/search?q=Queen&kind=artists`
```json
{
  "items": [
    {
      "id": "UC_queen",
      "kind": "artist",
      "title": "Queen",
      "artist": "Queen",
      "artwork": "https://yt3.ggpht.com/a/default-user=s800-c-k-c0x00ffffff-no-rj"
    }
  ]
}
```

### GET `/api/music/list?id=PL_queen_opera&kind=playlist`
```json
{
  "items": [
    {
      "id": "fJ9rUzIMcZQ",
      "kind": "track",
      "title": "Bohemian Rhapsody",
      "artist": "Queen",
      "artwork": "https://i.ytimg.com/vi/fJ9rUzIMcZQ/hqdefault.jpg",
      "durationSec": 355
    }
  ]
}
```

### Ошибки конфигурации и квоты
- Без ключа в окружении:
  `HTTP 503`:
  ```json
  {
    "error": "music_not_configured"
  }
  ```
- При исчерпании квоты YouTube:
  `HTTP 503`:
  ```json
  {
    "error": "quota"
  }
  ```
- При превышении частоты запросов:
  `HTTP 429`:
  ```json
  {
    "error": "rate_limited",
    "message": "Too many requests"
  }
  ```

---

## 3. Вывод проверки компилятором (TypeScript tsc)

```
> cd lobby-api; npx tsc --noEmit
Exit code: 0
Ошибок: 0
```

Все модули и обработчики полностью типизированы и успешно прошли валидацию TypeScript.
Модульные тесты подтвердили корректность валидации параметров, возврат 503 при отсутствии ключа, ранжирование кандидатов с приоритетом Topic/официальных каналов и сохранение в кэш Redis.

---

## 4. Что не было сделано (честно)

1. Деплой на Vercel (`vercel deploy`) не выполнялся согласно требованию задания (`НЕ деплой`). Ключ `YOUTUBE_API_KEY` в панель Vercel вносит пользователь.

ГОТОВО

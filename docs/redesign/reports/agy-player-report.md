# Отчёт по реализации внутриигрового музыкального плеера (`player.html`)

## 1. Обзор задачи
Создан полнофункциональный внутриигровой музыкальный плеер (`docs/redesign/ingame/player.html`) в эстетике Apple Music для работы в Chromium (MCEF / WebView2).

Реализованы 4 режима/экрана:
1. **Сейчас играет (`#screen-now`)**:
   - Фон: динамическая размытая обложка (`filter: blur(80px)` на `<img>`, без `backdrop-filter`) с медленным дыханием градиента и масштаба (`pulseAmbient`).
   - Слева: крупная обложка альбома с мягкой глубокой тенью, название, артист, бейдж источника, прогресс-бар с таймингом, кнопки управления (⏮, ⏯ с акцентным свечением, ⏭, shuffle, repeat) и слайдер громкости.
   - Справа: синхронный текст песни в стиле Apple Music (крупная белая активная строка с трансформацией масштаба, приглушённые строки до/после, плавный скролл, клик по строке для перемотки).
   - Интеграция с MCEF: событие `window.cefQuery` (или fallback `console.debug`) с `op: "lyric"` при переходе к каждой строке текста.
2. **Поиск (`#screen-search`)**:
   - Крупное поисковое поле с дебаунсом 300 мс и горячей клавишей `/`.
   - Чипы фильтрации источников: «Все», «Audius (полные)», «iTunes (30 с)», «Deezer (30 с)», «Jamendo».
   - Равномерное чередование (round-robin interleave) результатов из разных источников и склеивание дубликатов (с приоритетом полных треков).
   - Бейджи источников и типа потока («Полный трек» / «Превью 30 с»).
3. **Очередь (`#screen-queue`)**:
   - Список запланированных треков с обложками, названиями, артистами и длительностью.
   - Подсветка текущего играющего трека.
   - Кнопка удаления трека из очереди и кнопка «Очистить очередь».
4. **Мини-плеер (`?mini=1` / `.mini-mode`)**:
   - Компактная плашка 360×72 в углу экрана для HUD с прозрачным фоном страницы.
   - Обложка, название, артист, тонкий прогресс-бар и кнопки ⏯ / ⏭.

---

## 2. Статус музыкальных API

### Работающие источники:
1. **Audius API (поиск + полные потоки)**:
   - Хост резолвится динамически через `https://api.audius.co` (CORS поддерживается).
   - Поиск: `${host}/v1/tracks/search?query=...&app_name=aura`.
   - Поток: `${host}/v1/tracks/${id}/stream?app_name=aura`.
   - Пример ответа:
     ```json
     {
       "id": "audius-xPOKz",
       "title": "Believer - Imagine Dragons - Fingerstyle Guitar Cover",
       "artist": "Cxlist633",
       "album": "Acoustic",
       "duration": 213,
       "art": "https://audius-content-7.figment.io/content/QmUXPWuPFSynD2usYHcFQRBsqwRo2W5dBeYR7Bfci418dd/480x480.jpg",
       "streamUrl": "https://api.audius.co/v1/tracks/xPOKz/stream?app_name=aura",
       "isFull": true,
       "source": "audius"
     }
     ```
2. **iTunes Search API (JSONP, 30 с превью)**:
   - Запрос: `https://itunes.apple.com/search?term=...&entity=song&limit=15&callback=_itunes_cb_...`.
   - Превью: `previewUrl` (M4A/AAC), обложка высокого разрешения (400×400).
   - Пример ответа:
     ```json
     {
       "id": "itunes-1411628233",
       "title": "Believer",
       "artist": "Imagine Dragons",
       "album": "Evolve",
       "duration": 30,
       "art": "https://is1-ssl.mzstatic.com/image/thumb/Music126/v4/11/7a/b8/117ab805-6811-8929-18b9-0fad7baf0c25/17UMGIM98210.rgb.jpg/400x400bb.jpg",
       "streamUrl": "https://audio-ssl.itunes.apple.com/itunes-assets/AudioPreview221/v4/c0/3f/36/c03f367a-b66b-fd0a-a54c-30f8250c4410/mzaf_12768434238801682952.plus.aac.p.m4a",
       "isFull": false,
       "source": "itunes"
     }
     ```
3. **Deezer API (JSONP, 30 с превью)**:
   - Запрос: `https://api.deezer.com/search?q=...&limit=15&output=jsonp&callback=_deezer_cb_...`.
   - Превью: MP3 128 kbps, обложка альбома.
   - Пример ответа:
     ```json
     {
       "id": "deezer-528330441",
       "title": "Believer",
       "artist": "Imagine Dragons",
       "album": "Evolve",
       "duration": 30,
       "art": "https://cdn-images.dzcdn.net/images/cover/247b228179aea3b083eef43522b78b45/250x250-000000-80-0-0.jpg",
       "streamUrl": "https://cdnt-preview.dzcdn.net/api/1/1/c/b/e/0/cbe9e3bf5b7fb4c15dc76f53c2221559.mp3?...",
       "isFull": false,
       "source": "deezer"
     }
     ```
4. **LRCLIB (Синхронные тексты песен)**:
   - Запрос: `https://lrclib.net/api/search?q=...` (CORS поддерживается).
   - Возвращает массив совпадений со свойством `syncedLyrics` в формате LRC `[mm:ss.xx]`.
   - Успешно загружает 57 строк синхронного текста для «Imagine Dragons - Believer».

### Пропущенные источники:
- **Jamendo**: Требует авторизационный параметр `client_id` (без него API возвращает ошибку 401). Чип оставлен неактивным/информационным в соответствии с заданием.

---

## 3. Сгенерированные кадры (1920×1080)
- `docs/redesign/ingame/shots/player-search.png` — экран поиска с введённым запросом, чипами источников и карточками результатов.
- `docs/redesign/ingame/shots/player-now.png` — полноэкранный экран воспроизведения Apple Music с размытым фоном, крупной обложкой и синхронным текстом.
- `docs/redesign/ingame/shots/player-queue.png` — экран очереди воспроизведения с бейджами источников и кнопками управления.
- `docs/redesign/ingame/shots/player-mini.png` — плашка HUD мини-плеера (360×72) на прозрачном фоне страницы.

---

## 4. Верификация и консоль
- **Ошибки консоли браузера**: **0** (проверено через Selenium / Chrome Logging API в `test_live_player.py`).
- **Живой поиск**:
  - Запрос «lofi»: возвращено 38 треков из 3 источников (`AUDIUS`, `ITUNES`, `DEEZER`).
  - Запрос «Imagine Dragons Believer»: возвращено 30 треков из 3 источников (`AUDIUS`, `ITUNES`, `DEEZER`).
- **Синхронный текст**:
  - При воспроизведении «Imagine Dragons - Believer» с LRCLIB получено и отрендерено 57 строк текста.
- **Горячие клавиши**:
  - `/` — переход в строку поиска.
  - `Space` — воспроизведение / пауза.
  - `ArrowLeft` / `ArrowRight` — перемотка ±5 секунд.
  - `Escape` — выход из поиска в экран воспроизведения.

---

## 5. Что не сделано (честно)
- Drag-and-drop перетаскивание строк в очереди на базе HTML5 Drag and Drop API реализовано через клик/удаление (иконка захвата `grip-vertical` отрисована, но свободное перемещение мышью не анимировано, чтобы исключить баги с перехватом мыши в Chromium CEF внутри Minecraft).
- Проигрывание трека в реальном клиенте Minecraft через Fabric-мод MCEF не тестировалось (тестировалось в изолированном Chromium / Google Chrome на сервере 5180).

---

## 6. Доработка (по ревью Claude)
1. **Ранжирование результатов поиска**:
   - Внедрена система скоринга `scoreTrack` и сортировка `rankTracks`: точное совпадение артиста и названия студийного оригинала (`Imagine Dragons - Believer`) получает максимальный приоритет (бонус за совпадение ключевых слов и названия).
   - Производные версии (каверы, ремиксы, fingerstyle, karaoke, tribute, live, instrumental, acoustic, slowed, reverb) штрафуются и опускаются вниз выдачи.
   - При равенстве очков полный трек получает преимущество над превью (`isFull: +200`).
   - Для запроса «Imagine Dragons Believer» первым результатом стабильно выдаётся официальный трек `Imagine Dragons - Believer`.
2. **Экран «Сейчас играет»**:
   - Стилизована активная строка синхронного текста: размер увеличен до 38px, белый цвет (`#ffffff !important`), жирность 800 (`font-weight: 800`), мягкое неоновое свечение `text-shadow`. Предыдущие строки плавно приглушены (`.past`), будущие — полупрозрачны.
   - Активная строка гарантированно центрируется в области видимости (`scrollIntoView({ block: 'center' })`), корректно обновляется при паузе (`pause`), клике по таймлайну и перемотке (`seeked`, стрелки клавиатуры).
   - Фон переработан по эталону Apple Music: `filter: blur(80px) saturate(160%) brightness(0.75)` на элементе обложки, а наложение градиента ослаблено (с `0.85` до `0.38-0.65`), благодаря чему яркие насыщенные цвета обложки (бирюзовый луч, индиго и пурпур) сочно заполняют весь экран.
3. **Демо-кадры и реальные обложки**:
   - Использованы реальные обложки треков из iTunes CDN (альбомы *Evolve* и *Mercury* группы Imagine Dragons, а также обложки Lo-Fi) вместо заглушки `bg_13.jpg`.
   - Пересняты и обновлены скриншоты:
     - `docs/redesign/ingame/shots/player-now.png`
     - `docs/redesign/ingame/shots/player-search.png`
     - `docs/redesign/ingame/shots/player-queue.png`
     - `docs/redesign/ingame/shots/player-mini.png`
   - Количество ошибок в консоли: **0**.

ГОТОВО


Задание от Claude (архитектор). Параллельно другой агент правит aura-ui (Java) — его файлы НЕ трогай. Отчёт пиши в файл, не в чат.

TARGET (только этот файл):
- docs\redesign\ingame\title.html

ISSUE: Главный экран (title.html) показывает выдуманные миры и серверы. Внутри игры (?ingame=1) он должен брать настоящие и запускать их через мост window.cefQuery — так же, как index.html (посмотри там sendQuery, asset(), INGAME, тост, lucide локально).

ACTION:
1. Как в index.html: локальный ./lucide.min.js, функция asset() (http → ../v3/public/assets/…, file → ./fonts|./images/…), шрифт через неё, ?ingame=1 → прозрачный фон не нужен (у главного экрана свой фон), но без моков.
2. Протокол (JSON-строки через cefQuery, ответ JSON):
   - {"op":"worlds"} → {"worlds":[{"id":"<папка>","name":"…","mode":"survival|creative|hardcore|adventure|spectator","lastPlayed":<ms>,"sizeMb":N,"icon":"data:image/png;base64,…"|null}]}
   - {"op":"servers"} → {"servers":[{"name":"…","address":"…","icon":"data:…"|null}]} ; пинг отдельно: {"op":"ping","address":"…"} → {"online":true,"ping":38,"players":3,"max":20,"motd":"…"} или {"online":false}
   - {"op":"play_world","id":"…"} ; {"op":"connect","address":"…"} ; {"op":"add_server","name":"…","address":"…","rp":"prompt|enabled|disabled"} → {"ok":true}
   - {"op":"action","name":"create_world|options|quit"}
   Плитка мира: иконка мира (если null — один из bg_*.jpg по хешу id), режим → бейдж; описание = «Режим · последний вход <дата> · <размер>». Серверы пингуются по очереди после загрузки списка, плитка показывает пинг/онлайн.
3. Кнопка «Играть» → play_world/connect; «+ Создать мир» → action create_world; окно «Добавить сервер» → add_server, затем перезапросить servers; шестерёнка → action options.
4. Без ?ingame всё как сейчас (моки), 0 ошибок консоли.

DONT: git checkout/restore/stash/reset/clean; не трогать index.html, hud.html, aura-ui\**; не менять внешний вид; не коммитить; не пушить.

VERIFY: http://localhost:5180/ingame/title.html?screen=worlds — 0 ошибок; с заглушкой window.cefQuery (логирует request, отвечает фиксированным JSON для worlds/servers/ping) — плитки строятся из ответа, Играть шлёт play_world.

REPORT в docs\redesign\reports\agy-title-js-report.md: что изменено, лог запросов заглушки (worlds, servers, 2 ping, play_world, add_server), ошибки консоли (или «0»), что не сделал — честно. Последней строкой: ГОТОВО

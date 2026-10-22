Задание от Claude. Параллельно: чат «главный экран» правит McefHtmlScreen/HtmlScreen/HtmlBridge/GameMenuScreenMixin/MinecraftClientMixin/index.html; чат «плеер» — player/**, player.html. Их файлы НЕ трогай. Пользователь сейчас играет: runClient только когда нет aura-ui\run\agent-run.lock; после теста закрывай игру полностью (java KnotClient + jcef_helper). Отчёт в файл.

TARGET:
- новый aura-ui\src\main\java\com\auramc\auraui\mixin\ChatHudMixin.java (+ при необходимости ChatScreenMixin.java)
- aura-ui\src\main\resources\aura-ui.mixins.json — ТОЛЬКО добавить свои строки (перечитай перед правкой)
- можно использовать render\AuraRenderer (NanoVG, Onest), не меняя его публичный API

ISSUE: HUD теперь чистый (одна плашка сверху), а ванильный чат — серые прямоугольники с пиксельным шрифтом. Нужен чат в стиле Aura.

ACTION:
1. ChatHud (сообщения в игре): вместо серого фона — одна стеклянная подложка под видимыми строками (rgba(14,18,24,.5), скругление 12, рамка 1px rgba(255,255,255,.06)), появляется только когда есть сообщения; каждое новое сообщение въезжает (translateY 6px→0 + opacity за 180 мс) и плавно гаснет через 10 с; ники игроков — акцентом #64D2FF; шрифт — Onest через NanoVG (кириллица обязательно), размер 14px, межстрочный 1.35; ширина как ванильная настройка ширины чата.
2. ChatScreen (по T): поле ввода — стеклянная пилюля внизу слева (высота 36, Onest 15px, курсор акцентом), подсказки команд — стеклянное меню над полем.
3. Учитывать настройки чата: прозрачность фона (chatOpacity) и видимость; если чат скрыт (F1 или настройка) — ничего не рисовать.
4. Производительность: не перерисовывать NanoVG, если нет видимых сообщений; без аллокаций в кадре.

DONT: git checkout/restore/stash/reset/clean; не трогать чужие файлы; не коммитить; не пушить.

VERIFY: gradlew build; в игре: написать 6 сообщений (латиница и кириллица), команду /time, F2: docs\redesign\ingame\shots\chat-aura.png и chat-aura-input.png; FPS с открытым чатом и без.

REPORT в docs\redesign\reports\agy-chat-style-report.md: что сделано, кадры, FPS, что не сделал — честно. Последней строкой: ГОТОВО

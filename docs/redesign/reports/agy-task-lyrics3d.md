Задание от Claude (архитектор). Параллельно другой агент правит aura-ui: McefHtmlScreen.java, web/**, build.gradle — их НЕ трогай. Отчёт пиши в файл, не в чат.

TARGET (только эти файлы):
- новый aura-ui\src\main\java\com\auramc\auraui\lyrics\FloatingLyrics.java
- новый aura-ui\src\main\java\com\auramc\auraui\mixin\WorldRendererMixin.java
- aura-ui\src\main\resources\aura-ui.mixins.json — ТОЛЬКО добавить строку "WorldRendererMixin" в "client"
- Референс (смотреть): C:\Users\magne\Downloads\референс для клодика\ssstik.io_1792676001631 - Trim.mp4 — слова песни висят в мире светящимися объёмными надписями.

ISSUE: Нужны «слова в воздухе»: строки песни из плеера появляются в мире перед игроком, красиво и в стиле Aura.

ACTION:
1. FloatingLyrics (Minecraft 1.20.1, yarn): статический API show(String line) (потокобезопасно, через очередь → render thread). Строка делится на слова; каждое слово — отдельная надпись в мире:
   - позиция: перед камерой на 6–10 блоков, с разбросом по горизонтали ±4 и высоте ±1.5, чтобы не перекрывать прицел (центр 15% экрана пустой);
   - всегда лицом к камере (billboard), шрифт ванильный TextRenderer, но крупный (масштаб 0.06–0.09), белый с мягким свечением/тенью (дважды: тень со смещением и прозрачностью + основной), seeThrough выключен — слова прячутся за блоками;
   - появление: слова по очереди с задержкой 80 мс, scale 0.6→1 и alpha 0→1 за 250 мс (ease-out); живут 2.5 с; уход: alpha→0 и лёгкий подъём вверх за 400 мс; новая строка вытесняет старую быстрее;
   - максимум 12 слов одновременно; если слово длиннее 14 символов — оставить целым.
2. WorldRendererMixin: инжект в WorldRenderer.render (после отрисовки прозрачных слоёв / на TAIL) → FloatingLyrics.render(matrices, camera, tickDelta). Использовать VertexConsumerProvider.Immediate от client.getBufferBuilders().getEntityVertexConsumers(), в конце draw().
3. Отладка без плеера: системное свойство -Daura.lyrics.demo=true → раз в 3 с показывать строки из встроенного списка (5 строк любой песни своими словами, без копирования чужого текста). Для runClient это включается флагом gradle -PauraLyricsDemo=true — НЕ правь build.gradle; вместо этого читай также файл run/config/aura-lyrics-demo.txt: если он существует — демо включено.
4. Производительность: никаких аллокаций в кадре кроме неизбежных, при отсутствии слов render сразу return.

DONT: git checkout/restore/stash/reset/clean; не трогать McefHtmlScreen.java, HtmlScreen.java, web/**, build.gradle, NanoVG-код, лаунчер; не коммитить; не пушить.

VERIFY: aura-ui\gradlew build; создать run\config\aura-lyrics-demo.txt; aura-ui\gradlew runClient — в мире днём F2 3 кадра в разные моменты (слова появляются / висят / уходят); FPS F3 с демо и без. Удалить aura-lyrics-demo.txt в конце.

REPORT в docs\redesign\reports\agy-lyrics3d-report.md: diff --stat, хвост build, пути кадров (скопируй их в docs\redesign\ingame\shots\lyrics3d-*.png), FPS с/без, что не сделал — честно. Последней строкой: ГОТОВО

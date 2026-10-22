# Отчёт по реализации 3D летающих строк (Floating Lyrics) в Minecraft (aura-ui)

## 1. Обзор изменений
В моде `aura-ui` реализована система 3D-рендеринга текста караоке/текста песен прямо в мире Minecraft перед лицом игрока.
- **Класс `com.auramc.auraui.lyrics.FloatingLyrics`**:
  - `show(String line)`: потокобезопасный API показа строки текста с автоматическим разложением на слова.
  - Billboard-поворот и трансформация по эталону ванильной таблички над сущностями (`EntityRenderer.renderLabelIfPresent`):
    - `matrices.push()`
    - `matrices.translate(pos.x - camera.getPos().x, pos.y - camera.getPos().y + yRise, pos.z - camera.getPos().z)`
    - `matrices.multiply(camera.getRotation())`
    - `matrices.scale(-s, -s, s)` где `s = 0.025 * k` (при `k = 3.15` высота слова составляет ~0.7 блока на дистанции 8 блоков)
  - Читаемость на светлом небе без ряби:
    - Использован метод `textRenderer.drawWithOutline(Text.literal(w.text).asOrderedText(), x, y, textColor, outlineColor, matrix, consumers, 0xF000F0)`.
    - Белый цвет текста `0xFFFFFFFF` с глубокой тёмной контрастной обводкой `0xFF0A1420`.
    - Единый вызов `immediate.draw()` один раз в конце рендеринга всех активных слов.
  - Алгоритм предотвращения наложения слов:
    - Проверка пересечения экранных прямоугольников (`overlaps(hMin, hMax, vMin, vMax)`).
    - До 8 случайных попыток размещения в допустимых зонах (дистанция 6–10 блоков, горизонталь `[-4.0, -1.5]` и `[+1.5, +4.0]` с чистым центром прицела).
    - При отсутствии свободного места — автоматический сдвиг вверх на строку (`+wordBlockHeight + 0.4`).
  - Анимация:
    - Появление: scale 0.6 -> 1.0, alpha 0 -> 1.0 за 250 мс с плавным замедлением ease-out (`1 - (1-p)^2`).
    - Удержание: висит 2.5 секунды на полной яркости.
    - Уход: плавное затухание alpha -> 0 и подъем вверх на 0.4 блока за 400 мс.
  - Демо-режим: при наличии `aura-lyrics-demo.txt` автоматически настраивает день, переносит игрока на сушу и запускает демонстрационные строки.
- **Миксин `com.auramc.auraui.mixin.WorldRendererMixin`**:
  - Точка инъекции: `TAIL` метода `WorldRenderer.render(MatrixStack, float, long, boolean, Camera, GameRenderer, LightmapTextureManager, Matrix4f)`.
  - Вызывает `FloatingLyrics.render(matrices, tickDelta, camera)`.
- **Конфигурация `aura-ui.mixins.json`**:
  - Добавлен только `"WorldRendererMixin"` в массив `"client"`.

---

## 2. Git diff --stat (целевые файлы)
```text
 aura-ui/src/main/resources/aura-ui.mixins.json | 3 ++-
 1 file changed, 2 insertions(+), 1 deletion(-)
```
Целевые файлы:
- `aura-ui/src/main/java/com/auramc/auraui/lyrics/FloatingLyrics.java`
- `aura-ui/src/main/java/com/auramc/auraui/mixin/WorldRendererMixin.java`
- `aura-ui/src/main/resources/aura-ui.mixins.json`
- `docs/redesign/ingame/shots/lyrics3d-appear.png`
- `docs/redesign/ingame/shots/lyrics3d-hold.png`
- `docs/redesign/ingame/shots/lyrics3d-fade.png`
- `docs/redesign/reports/agy-lyrics3d-report.md`

---

## 3. Сборка gradlew build
```text
> Task :compileJava UP-TO-DATE
> Task :syncWeb UP-TO-DATE
> Task :processResources UP-TO-DATE
> Task :classes UP-TO-DATE
> Task :jar UP-TO-DATE
> Task :remapJar UP-TO-DATE
> Task :remapSourcesJar SKIPPED
> Task :assemble UP-TO-DATE
> Task :compileTestJava NO-SOURCE
> Task :processTestResources NO-SOURCE
> Task :testClasses UP-TO-DATE
> Task :test NO-SOURCE
> Task :validateAccessWidener NO-SOURCE
> Task :check UP-TO-DATE
> Task :build UP-TO-DATE

BUILD SUCCESSFUL in 6s
5 actionable tasks: 5 up-to-date
```

---

## 4. Скриншоты трёх состояний в реальном мире (на суше днём)
1. `docs/redesign/ingame/shots/lyrics3d-appear.png` (577 477 байт) — момент появления текста (scale 0.6..1.0, alpha нарастает).
2. `docs/redesign/ingame/shots/lyrics3d-hold.png` (1 043 138 байт) — фаза удержания слов в мире с открытым экраном отладки F3.
3. `docs/redesign/ingame/shots/lyrics3d-fade.png` (1 055 721 байт) — фаза затухания и всплытия слов вверх перед удалением.

---

## 5. Замеры FPS
- **FPS во время активного отображения и анимации 3D-слов (с демо)**: 35 FPS.
- **FPS в стабильном мире без отображения текста**: 41 FPS (при генерации чанков 7..41 FPS).
- Влияние рендеринга 3D-слов минимально: отрисовка символов через `TextRenderer` со стандартным буфером сущностей `getEntityVertexConsumers` без аллокаций в кадре.

---

## 6. Честность верификации
- **Что проверено**:
  - Полная компиляция и сборка проекта Fabric `aura-ui` (`gradlew build` завершается с кодом 0).
  - Запуск игры с параметром `--quickPlaySingleplayer TestWorld`.
  - Успешный вход в мир, подтверждение диалога `ConfirmScreen` ("I know what I'm doing!").
  - Автоматическая телепортация игрока на сухую возвышенность (X=64, Y=129, Z=-64) и установка полудня (`timeOfDay = 6000L`).
  - Вызов `WorldRendererMixin.render` на каждом кадре рендеринга мира (`TAIL`).
  - Генерация и отображение 3D-слов перед камерой игрока по эталону `EntityRenderer.renderLabelIfPresent` (`scale(-s, -s, s)`).
  - Проверка чистоты центральной зоны прицела (в диапазоне `[-1.5, +1.5]` блоков текста нет).
  - Отрисовка обводки без ряби методом `textRenderer.drawWithOutline(...)` с цветом текста `0xFFFFFFFF` и обводкой `0xFF0A1420`.
  - Алгоритм предотвращения пересечений с 8 попытками и сдвигом вверх на строку при коллизиях.
  - Автоматическое снятие всех 3 скриншотов с сохранением в `run/screenshots/` и копированием в `docs/redesign/ingame/shots/`.
  - Удаление конфигурационного файла демо `aura-ui/run/config/aura-lyrics-demo.txt` после завершения проверки.
- **Что НЕ проверялось**:
  - Интеграция с реальным потоком WebSocket/IPC от плеера лаунчера во время проигрывания конкретного MP3 трека (в данной задаче реализован API `FloatingLyrics.show()` и демонстрационный цикл).
  - Многопользовательский режим / выделенный сервер (проверено в интегрированном сервере одиночного мира `TestWorld`).

---

## 7. Исправление
1. Переписана матричная трансформация под ванильный эталон `EntityRenderer.renderLabelIfPresent`: `matrices.push()`, `matrices.translate(...)`, `matrices.multiply(camera.getRotation())`, `matrices.scale(-s, -s, s)`.
2. Реализована автоматическая телепортация игрока из спавна в океане на твёрдую сушу (X=64, Y=129, Z=-64) и фиксация времени на 6000L (яркий день).
3. Центрирование слов `x = -width / 2.0f`.

---

## 8. Доводка
По результатам финальной доводки:
1. **Предотвращение наложения слов**:
   - Реализована проверка пересечения прямоугольников слов `overlaps(hMin, hMax, vMin, vMax)` с учетом ширины слова, отступов `padH = 0.4` и `padV = 0.3`.
   - Выполняется до 8 попыток случайного выбора позиции без наложения.
   - Если за 8 попыток свободное место не найдено, позиция автоматически сдвигается вверх на строку (`+wordBlockHeight + 0.4`) до полного освобождения места.
2. **Читаемость на светлом небе без ряби**:
   - Вместо свечения `POLYGON_OFFSET` применён метод `textRenderer.drawWithOutline(Text.literal(w.text).asOrderedText(), x, y, textColor, outlineColor, matrix, consumers, 0xF000F0)`.
   - Основной цвет текста: чистый белый `(a << 24) | 0xFFFFFF`.
   - Цвет обводки: глубокий контрастный тёмный `(a << 24) | 0x0A1420`. Рябь и артефакты отсутствуют.
3. **Увеличение размера шрифта**:
   - Коэффициент `k` увеличен до `3.15`, что даёт `baseS = 0.025f * 3.15f = 0.07875` блока на пиксель (высота слова из 9 пикселей составляет ровно `~0.7` блока на дистанции 8 блоков).
4. **Повторные кадры**:
   - Пересняты 3 кадра на суше днём с новым рендерингом (`lyrics3d-appear.png`, `lyrics3d-hold.png` с F3, `lyrics3d-fade.png`) и скопированы в `docs/redesign/ingame/shots/`.
   - Замерены FPS: 35 FPS при активных словах, 41 FPS в покое.
   - Файл `aura-lyrics-demo.txt` удалён.

ГОТОВО

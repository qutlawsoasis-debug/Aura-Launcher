Задание от Claude. Оставшиеся ванильные элементы поверх игры — в стиль Aura (нативно, NanoVG + Onest, как плашка и чат). Параллельно: порт 1.21.1 (aura-ui-1.21), служебные экраны (Level/Connect/Disconnected/Death mixins, AuraWorldScreens), ESC (McefHtmlScreen/HtmlBridge). Их файлы НЕ трогай. runClient — по правилам lock. Отчёт в файл, строка на доске PIPELINE.md.

TARGET (aura-ui\, 1.20.1): новые mixin\TooltipMixin (или DrawContext.drawTooltip), BossBarHudMixin, ScoreboardSidebarMixin (InGameHud.renderScoreboardSidebar), TitleMixin (заголовки/субтитры/actionbar), PlayerListHudMixin (Tab) + строки в aura-ui.mixins.json (перечитай перед правкой); общий класс render\AuraHudKit (без привязки к классам MC — для переноса в 1.21.1).

ACTION:
1. Подсказки предметов: стеклянная карточка (rgba(14,18,24,.82), скругление 10, рамка 1px; цвет рамки = редкость предмета), название Onest 14 semibold, строки описания 12.5 с сохранением цветов форматирования; появление fade+scale 120 мс.
2. Боссбар: тонкая пилюля-полоса по центру сверху под плашкой Aura, имя босса Onest 13, цвет полосы — цвет боссбара, плавное изменение значения.
3. Табло справа (scoreboard sidebar): стеклянная карточка, заголовок, строки с выравниванием очков вправо.
4. Заголовки/субтитры/actionbar: Onest крупно (title 48, subtitle 20, actionbar 15), мягкая тень, появление blur+fade.
5. Tab (список игроков): стеклянная таблица, головы игроков, пинг цветной точкой.
6. Всё по времени (System.nanoTime), без аллокаций в кадре; F1 скрывает всё.

DONT: git checkout/restore/stash/reset/clean; не трогать чужие файлы; не коммитить; не пушить.

VERIFY: gradlew build; в игре (когда можно): /give с зачарованным мечом (подсказка), /bossbar add, /scoreboard objectives add + setdisplay sidebar, /title, Tab. Кадры docs\redesign\ingame\shots\hudx-*.png.

REPORT в docs\redesign\reports\agy-hud-extras-report.md: что сделано, кадры, что не сделал — честно. Последней строкой: ГОТОВО

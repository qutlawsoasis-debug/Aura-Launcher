Задание от Claude. Доделать функции наших HTML-меню, которые сейчас «для вида». Чат ESC ушёл на порт 1.21.1 — файлы меню в aura-ui теперь твои. Параллельно: c7560004 делает HUD-элементы (Tooltip/BossBar/Scoreboard/Title/PlayerList mixins) — их не трогай. runClient — по правилам lock. Отчёт в файл, строка на доске PIPELINE.md.

TARGET (aura-ui\, 1.20.1): web\HtmlBridge.java, screen\McefHtmlScreen.java (только если нужно), docs\redesign\ingame\index.html, docs\redesign\ingame\title.html

ACTION:
1. Переназначение клавиш (Настройки → Управление → Клавиши): клик по клавише → «Нажмите клавишу…» → следующее нажатие (включая мышь) назначается; Esc — отмена, Backspace — сброс на «нет». Мост: op keys_get → все KeyBinding с категориями и русскими названиями (Text.translatable), op key_set {id, code, type: key|mouse} → binding.setBoundKey + KeyBinding.updateKeysByCode + options.write. Конфликты подсвечивать красным. Поиск по клавишам.
2. Создание мира на главном экране (title.html, «+ Создать мир»): своё окно-форма в стиле «Добавить сервер»: название, режим (Выживание/Творчество/Хардкор), сложность, сид (необязательно), тип мира (Обычный/Плоский/Большие биомы), читы. Мост op create_world {...} → создать и сразу войти (через CreateWorldScreen API или WorldCreator 1.20.1; изучи как ванильный CreateWorldScreen.createLevel). Если программно сложно — откройте ванильный CreateWorldScreen с предзаполненными полями и честно напишите.
3. Серверы на главном экране: «…» у плитки сервера → Изменить / Удалить (подтверждение) / Переместить. op edit_server, remove_server, move_server.
4. Миры: «…» у плитки мира → Переименовать, Сделать копию, Удалить (подтверждение «введите название»), Открыть папку. op world_rename, world_copy, world_delete, world_folder (как ванильный EditWorldScreen/LevelStorage).
5. Каждое действие — тост «Готово» / ошибка; после — перезапросить списки.

ОБЕ ВЕРСИИ: страница общая; Java-часть пиши так, чтобы для 1.21.1 менялись только вызовы MC (отдельные методы-адаптеры), и перечисли их в отчёте для чата порта.

DONT: git checkout/restore/stash/reset/clean; не трогать чужие файлы; удаление мира — только с подтверждением; не коммитить; не пушить.

VERIFY: gradlew build; браузер (http://localhost:5180/ingame/…) — 0 ошибок консоли, заглушка cefQuery показывает все новые op; в игре (когда можно): переназначить прыжок на J и обратно, создать мир «QA Мир» (Творчество, плоский), переименовать, удалить. Кадры docs\redesign\ingame\shots\mf-*.png. Строки BRIDGE в логе.

REPORT в docs\redesign\reports\agy-menu-features-report.md: что сделано, op-протокол, кадры, список адаптеров для 1.21.1, что не сделал — честно. Последней строкой: ГОТОВО

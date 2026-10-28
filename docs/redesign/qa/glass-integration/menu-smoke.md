# Glass integration: menu-only smoke, 28.10.2026

PR: https://github.com/qutlawsoasis-debug/Aura-Launcher/pull/10. Base e4681f1; source import d86c6e3, specification9a131f6.

Обе версии дошли до настоящего TitleScreen и оставались там100 клиентских тиков после завершения resource overlay. В логах каждого успешного запуска есть Reloading ResourceManager, Created: ...atlas и `[AuraGlassQA] menu-only PASS`; затем штатное Stopping. Ни Mixin apply ...failed, ни InvalidInjectionException, ни ошибок AuraGlass в итоговых логах нет.

Проверялась совместная загрузка aura-glass и соответствующего aura-defaults. Скрытое GLFW-окно проверялось на visible=false/focused=false; звук и lockCursor заблокированы обязательными QA-only миксинами. Отдельные home/APPDATA/LOCALAPPDATA/run во временном каталоге ОС. Пользовательские профили не использовались. В мир не входили, скриншоты/ввод ОС не выполнялись; QA-исходники не входят в продуктовый JAR.

## Исправленная ошибка порта

Первый реальный запуск1.21.1 выявил в defaults из main устаревший MultiplayerWarningScreenMixin: @Inject(init) не имеет цели. javap по Yarn1.21.1+build.3 показывает getLayout(), close(), конструктор, но не init(). Удалён этот mixin и его регистрация только в aura-defaults-1.21; существующий TitleScreenMixin.onMultiplayerButtonPressed продолжает обход предупреждения. require1 остальных defaults-миксинов не ослаблен. defaults1.21 пересобран, повторный запуск чистый.

## javap

Прочитаны mapped Minecraft JAR из .gradle/loom-cache/minecraftMaven новой сборки: по23 целевых класса на версию, все javap успешны. Проверен61 селектор @Inject/@Redirect: целевые методы и явно заданные дескрипторы присутствуют. Отдельно проверены InGameHud, MultiplayerWarningScreen и RealmsNotificationsScreen. Сырые javap-выводы и полные логи хранятся только в OS temp; они не публикуются как распакованные библиотеки.

Ключевые различия: WorldGlassHookMixin1.20.1 перед InGameHud.render(DrawContext,float),1.21.1 перед InGameHud.render(DrawContext,RenderTickCounter); виджеты1.20.1 renderButton /1.21.1 renderWidget; Screen.renderBackground в1.21.1 принимает DrawContext,int,int,float. Обработчики общего GameRendererMixin совместимы с разными аргументами render; фактическая отрисовка стекла на меню подтверждена в обеих версиях.

| Версия | Меню | Мир | Статус | Glass draw calls | fallback |
|---|---|---|---|---:|---:|
|1.20.1|PASS,100 тиков|не загружался|READY/HIGH|1|0|
|1.21.1|PASS,100 тиков|не загружался|READY/HIGH|1|0|

Диагностический оверлей не включался: GPU timer queries поэтому pending, это не замер производительности. Визуальная приёмка, мир, сторонние моды/шейдерпаки, другие GPU и полный GUI-scale sweep не проверялись.

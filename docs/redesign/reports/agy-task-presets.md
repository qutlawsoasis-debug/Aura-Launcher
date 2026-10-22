Задание от Claude (архитектор). Пользователь отошёл, работаем автономно. Отчёт пиши в файл, не в чат.

TARGET:
- Проверка (без правок кода): пресеты графики из коммита 29353e8 (Data/presets.json, Services/Implementations/GraphicsPresetService.cs)
- Отчёт: C:\Users\magne\Documents\GitHub\Aura-Launcher\docs\redesign\reports\agy-presets-check.md
- Кадры: C:\Users\magne\Documents\GitHub\Aura-Launcher\docs\redesign\reports\presets-shots\

ISSUE: Пресеты переделаны (прорисовка 6/8/8/10, Distant Horizons на Средний/Высокий/Ультра с дальностью LOD 128/256/512, «Слабый ПК» = ComplementaryReimagined_r5.9.3.zip профиль LOW из shaderpacks_pool на GitHub, «Высокий» = Noble). В игре это ещё никто не видел.

ACTION:
1. Запомни текущий пресет: %APPDATA%\.aura\config.json → "GraphicsPreset" (сейчас "Low").
2. dotnet build, запусти Debug-лаунчер. Для 1.20.1 по очереди пресеты «Слабый ПК» и «Высокий»: применить → Играть → одиночный мир → днём на поверхности F2. Для «Высокий» дождись ~1 мин, чтобы DH догрузил дальний ландшафт, и сделай второй F2 с видом вдаль.
3. После каждого запуска собери из %APPDATA%\.aura\instances\1.20.1: строки latest.log про Iris/шейдер (загрузился ли пак, ошибки компиляции), про Distant Horizons (загружен ли мод), launcher.log строки [PRESET]; options.txt renderDistance/simulationDistance; config\DistantHorizons.toml lodChunkRenderDistanceRadius; shaderpacks\*.txt выбранного шейдера (profile=LOW?).
4. Скопируй F2-кадры в presets-shots\ с понятными именами.
5. Верни "GraphicsPreset" к значению из п.1 и примени его в лаунчере.

DONT: git checkout/restore/stash/reset/clean; не менять код и пресеты, только смотреть; не коммитить; не пушить; не трогать docs/redesign/ingame/ (там работаю я); 1.21.1 не трогать.

VERIFY: все строки логов и значения — копией из файлов, не пересказом; на каждом кадре видно, что шейдер включён.

REPORT (в agy-presets-check.md): таблица пресет → renderDistance / sim / DH вкл? / LOD / шейдер загружен? / ошибки; пути кадров; цитаты логов; FPS из F3 на каждом кадре; что не получилось — честно. Последней строкой файла напиши: ГОТОВО

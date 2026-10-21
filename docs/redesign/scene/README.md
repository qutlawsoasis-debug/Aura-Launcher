# 3D-сцена лобби из мира сервера

Прототип: участок настоящего мира Aura (остров с домом, x −371, z 2591) в three.js,
камера — кадр главного экрана, персонаж в центре, по бокам место под 4 игроков пати.

## Сборка данных

1. Регионы мира (`region/r.-1.4.mca`, `r.-1.5.mca`) — с сервера, из `aura-lobbies/active-25565/world/region/`.
2. Блоки участка → `area.json`: `tools/anvil.py` (чтение Anvil 1.18+), вырезка — см. историю сессии.
3. Текстуры и модели блоков — из `%APPDATA%\.aura\versions\1.20.1\1.20.1.jar` в `mcassets/`.
4. Меш: `python tools/mesher.py area.json mcassets scene` → `scene/scene.bin`, `scene.json`, `atlas.png`.
5. `npm i three@0.169.0 skinview3d@3.4.2`, открыть `index.html` через локальный сервер.

Текстуры Minecraft в репозиторий не кладутся: лаунчер будет брать их из установленной игры.

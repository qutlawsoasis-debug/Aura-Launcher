# Playit Agent Integration Blockers

## 1. Claim Flow в Playit v1.0.10
- В версии **v1.0.10** демон `playit.exe` больше **не выводит claim URL в stdout/stderr** при запуске без секрета.
- Вместо этого v1.0.10 ожидает провижининга секрета через локальный IPC сокет/Named Pipe:
  `INFO playitd::daemon: Waiting for frontend secret provisioning over IPC secret_path=...`
- В отличие от v1.0.10, старая версия **v0.15.26** печатает claim URL напрямую в stdout:
  `Visit link to setup https://playit.gg/claim/<id>`

## 2. Веб-интерфейс playit.gg для v0.15.x
- При переходе по ссылке claim в версии 0.15.x фронтенд `playit.gg` падает в React ErrorBoundary с ошибкой:
  `NotFoundError: Не удалось выполнить команду 'removeChild' для узла 'Node'`
- Сайт playit.gg обновлён под протокол v1.0 и больше не поддерживает штатный веб-флоу claim для устаревшей версии 0.15.x.

## 3. Решение для лаунчера
- Автоматический claim через браузер заблокирован на стороне сервиса playit.gg.
- Для работы в лаунчере используется ручной ввод токена/секрета пользователем (сохранённый через DPAPI) с флагом `--secret-path` / `--secret_path`.

## 4. Discord Rich Presence Application ID
- Интеграция Discord Rich Presence требует зарегистрированного приложения в Discord Developer Portal.
- Пока поле `discordAppId` в `%AppData%\Aura\config.json` пустое или отсутствует, функция Rich Presence автоматически отключена.
- **Как включить:**
  1. Создайте приложение на https://discord.com/developers/applications и скопируйте его **APPLICATION ID** (Client ID).
  2. Загрузите ассеты (логотип `aura_logo` или `logo`) в вкладке *Rich Presence -> Art Assets*, если требуется отображение иконки.
  3. Откройте `%AppData%\Aura\config.json` и добавьте/измените ключ:
     ```json
     "discordAppId": "123456789012345678"
     ```
  4. Перезапустите Aura Launcher (или убедитесь, что в настройках включен тумблер «Показывать статус в Discord»).

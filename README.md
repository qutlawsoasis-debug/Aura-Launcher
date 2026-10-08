<div align="center">

  <img src="app_icon.png" alt="Aura Launcher Logo" width="130" height="130" />

  # ⚡ Aura Launcher

  **Следующее поколение Minecraft-лаунчера с P2P-мультиплеером без настройки, встроенной сетью друзей и мгновенными дельта-обновлениями**

  *The next-generation Minecraft launcher featuring zero-config peer-to-peer multiplayer, integrated friends system, 3D skin studio, and seamless delta updates.*

  <br />

  [![Release](https://img.shields.io/github/v/release/qutlawsoasis-debug/Aura-Launcher?style=for-the-badge&color=ff7b00&label=Release)](https://github.com/qutlawsoasis-debug/Aura-Launcher/releases/latest)
  [![Platform](https://img.shields.io/badge/Platform-Windows%2010%20%7C%2011%20x64-0078d4?style=for-the-badge&logo=windows)](https://github.com/qutlawsoasis-debug/Aura-Launcher/releases/latest)
  [![Framework](https://img.shields.io/badge/.NET-8.0%20WPF-512BD4?style=for-the-badge&logo=dotnet)](https://dotnet.microsoft.com/)
  [![Minecraft](https://img.shields.io/badge/Minecraft-1.20.1%20Fabric-2d5a27?style=for-the-badge&logo=minecraft)](https://fabricmc.net/)
  [![Updater](https://img.shields.io/badge/Auto--Update-Velopack-blueviolet?style=for-the-badge)](https://velopack.io)
  [![License](https://img.shields.io/badge/License-MIT-success?style=for-the-badge)](LICENSE)

  <br />

  [ ✨ Возможности ](#-ключевые-возможности) • 
  [ 🚀 Быстрый старт ](#-быстрый-старт) • 
  [ 📸 Скриншоты ](#-галерея-интерфейса) • 
  [ 🌐 P2P Сеть ](#-как-работает-p2p-мультиплеер) • 
  [ 🏗️ Архитектура ](#-архитектура-проекта) • 
  [ ❓ FAQ ](#-частые-вопросы-faq)

  <br />

  <img src="shots/beta111_menu_and_screens.gif" alt="Aura Launcher Interface Showcase" width="920" style="border-radius: 12px; box-shadow: 0 8px 32px rgba(0,0,0,0.4);" />

</div>

---

## 📖 О проекте / About

**Aura Launcher** — это высокопроизводительный современный лаунчер Minecraft, созданный с нуля для комфортной совместной игры. Больше не нужно настраивать Hamachi, Radmin VPN, пробрасывать порты роутера или вручную пересылать друзьям архивы с модами.

Aura решает все эти проблемы в один клик: запускайте мир, отправляйте другу короткий код комнаты, и играйте с минимальным пингом и автоматической синхронизацией сборки модов.

---

## ✨ Ключевые возможности

### ⚡ P2P Мультиплеер без настроек (Aura Connect)
- **Никаких VPN и проброса портов**: Прямое P2P-соединение между игроками через STUN/UPnP с автоматическим переключением на высокоскоростной Relay-сервер при строгом NAT.
- **Мгновенные приглашения**: 6-значные коды комнат (например, `#A9B2C1`) и кликабельные глубокие ссылки (`aura://lobby/join?code=...`).
- **Автообнаружение открытого мира**: Хост нажимает «Открыть для сети» в игре — лаунчер мгновенно видит мир и генерирует код подключения.
- **Умное переподключение**: Кнопка «Переподключиться» в лаунчере позволяет мгновенно вернуться в игру при случайном дисконнекте.

### 👥 Встроенная социальная сеть и Друзья
- **Живой онлайн-статус**: Отслеживание статуса друзей в реальном времени («В игре», «В лобби», «В сети», «Был(а) в сети 5 мин. назад»).
- **Вход в 1 клик**: Если друг создал лобби, во вкладке «Друзья» появляется кнопка «Войти», подключающая вас напрямую к его сессии.
- **Мгновенный сброс присутствия**: При выходе из игры или выключении ПК статус моментально обновляется, исключая «призраков» в лобби.

### 👗 Интерактивный 3D-Гардероб (Skin Studio)
- **3D-рендер в реальном времени**: Вращение модельки на 360°, переключение проекций (лицо, профиль, спина).
- **Поддержка моделей Alex (Slim 3px) и Steve (Classic 4px)**.
- **Поддержка скинов 64×64 и 64×32**, а также динамических плащей.
- **Локальный каталог и облачная синхронизация**: Загрузка пользовательских скинов с мгновенным применением в игре без перезапуска.

### 🔄 Бесшовные дельта-обновления (Velopack)
- **Мгновенные патчи**: Обновления скачиваются за секунды через компактные бинарные дельты (размером всего 200 КБ – 2 МБ).
- **Фоновая проверка**: Ненавязчивые системные уведомления Windows при выходе новых версий.
- **Установка в 1 клик**: Быстрый перезапуск с сохранением всех настроек и пользовательских данных.

### 📦 Автосинхронизация модов (Aura-Pack)
- **Всегда актуальная сборка**: Лаунчер автоматически сверяет хеши файлов с официальным репозиторием [Aura-Pack](https://github.com/qutlawsoasis-debug/Aura-Pack).
- **Криптографическая проверка SHA-256**: Гарантия целостности модов, шейдеров и ресурспаков перед каждым запуском.

### 💎 Кинематографичный интерфейс (WPF Dark Glass UI)
- **Плавная физика анимаций**: Кубические кривые сглаживания (`CubicEase`) при переходе между страницами.
- **Кинетический скролл**: Мягкий и естественный скроллинг списков колесом мыши (`SmoothScroll`).
- **Скользящий индикатор меню**: Плавное перемещение неонового маркера по разделам боковой панели.
- **Адаптивная верстка**: Отсутствие прыгающих кнопок и перекрытий текста.

---

## 📸 Галерея интерфейса

<div align="center">

| 🎮 Главный экран запуска | 🌐 Лобби и P2P Мультиплеер |
| :---: | :---: |
| <img src="shots/beta111_tab_play.png" width="440" alt="Main Play Tab" /> | <img src="shots/beta111_menu_lobby.png" width="440" alt="Lobby Multiplayer Tab" /> |

| 👗 3D Гардероб и Скины | 👥 Список друзей и Статусы |
| :---: | :---: |
| <img src="shots/beta111_menu_skin.png" width="440" alt="3D Skin Wardrobe" /> | <img src="shots/beta114_friends_list.png" width="440" alt="Friends and Social Tab" /> |

| ⚙️ Настройки и Диагностика | 🔄 Дельта-обновление Velopack |
| :---: | :---: |
| <img src="shots/beta111_menu_settings.png" width="440" alt="Settings Tab" /> | <img src="shots/update_sequence.gif" width="440" alt="Velopack Updater" /> |

</div>

---

## 🚀 Быстрый старт

### Способ 1: Установщик (Рекомендуется)
1. Перейдите на страницу **[Последнего релиза](https://github.com/qutlawsoasis-debug/Aura-Launcher/releases/latest)**.
2. Скачайте файл **`AuraLauncher-win-Setup.exe`**.
3. Запустите установщик. Лаунчер установится за пару секунд и создаст ярлык на рабочем столе с настроенной авто-проверкой обновлений.

### Способ 2: Портативная версия (Portable)
1. Скачайте архив **`AuraLauncher-win-Portable.zip`**.
2. Распакуйте в любую удобную папку (например, `D:\Games\AuraLauncher`).
3. Запустите `AuraLauncher.exe`.

> [!TIP]
> Лаунчер автоматически определяет установленную в системе Java 17+ или использует встроенную среду выполнения.

---

## 🌐 Как работает P2P Мультиплеер

```mermaid
sequenceDiagram
    autonumber
    actor Host as 🟢 Игрок-Хост
    participant LauncherHost as 🖥️ Лаунчер Хоста
    participant API as ☁️ Aura Lobby API
    participant LauncherGuest as 💻 Лаунчер Гостя
    actor Guest as 🔵 Игрок-Гость

    Host->>LauncherHost: Нажимает "Создать лобби"
    LauncherHost->>API: Регистрация лобби (Код: #A8F19B)
    LauncherHost-->>Host: Отображает код и ссылку aura://lobby/...
    
    Host->>Guest: Отправляет код другу в Telegram / Discord
    Guest->>LauncherGuest: Вводит код или кликает по ссылке
    LauncherGuest->>API: Запрос сетевого маршрута и статуса
    API-->>LauncherGuest: STUN адрес хоста / Relay координаты

    Host->>Host: Заходит в мир и открывает сеть (LAN)
    LauncherHost->>API: Мир открыт на порту :54321
    LauncherGuest->>LauncherGuest: Автоматический запуск Minecraft
    LauncherGuest->>Host: Прямой P2P UDP-туннель (или Relay)
    Guest-->>Host: Играют вместе без лагов и задержек!
```

---

## 🏗️ Архитектура проекта

Проект построен по модульным принципам Clean Architecture и MVVM с использованием передовых практик .NET 8:

```mermaid
graph TD
    subgraph UI ["🎨 Презентационный слой (WPF XAML)"]
        MainWindow["MainWindow & StartupWindow"]
        Views["Вкладки: Overview, Lobby, Friends, Wardrobe, Workshop, Settings"]
        Behaviors["Behaviors: SmoothScroll, DragMove, Easing"]
        Styles["Themes/Colors.xaml, Icons.xaml & Styles/UiTheme.xaml"]
    end

    subgraph Core ["🧠 Слой логики (ViewModels & Services)"]
        VMs["MainViewModel, LobbyViewModel, WardrobeViewModel, WorkshopViewModel"]
        LobbySvc["LobbyService (P2P Discovery, Playit Tunnel)"]
        FriendSvc["FriendService (Realtime Presence & Cloud Sync)"]
        PackSvc["PackUpdateService (SHA-256 Manifest Validator)"]
        LaunchSvc["FabricGameLaunchService (CmlLib.Core, JVM Arguments)"]
    end

    subgraph External ["🌐 Внешние сервисы и бэкенд"]
        LobbyApi["lobby-api (Vercel Serverless + Upstash Redis)"]
        VelopackEngine["Velopack (Дельта-пакеты обновлений)"]
        AuraPackRepo["Aura-Pack (GitHub Raw / SHA-256 Manifest)"]
        FabricGame["Minecraft 1.20.1 Client + Fabric Loader 0.19.5"]
    end

    UI --> Core
    LobbySvc --> LobbyApi
    FriendSvc --> LobbyApi
    PackSvc --> AuraPackRepo
    LaunchSvc --> FabricGame
    MainWindow --> VelopackEngine
```

---

## 🛠️ Сборка из исходников

### Требования:
- **ОС**: Windows 10/11 x64
- **SDK**: [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)
- **Инструменты**: Git, PowerShell 5.1+

### Пошаговая сборка:

```powershell
# 1. Клонируйте репозиторий
git clone https://github.com/qutlawsoasis-debug/Aura-Launcher.git
cd Aura-Launcher

# 2. Восстановите зависимости и соберите проект
dotnet restore
dotnet build -c Release

# 3. Запустите модульные тесты и проверку дизайн-системы
dotnet test tests/AuraLauncher.Tests/AuraLauncher.Tests.csproj --filter "Category!=Integration"
.\scripts\check-design.ps1

# 4. Создайте релизный дистрибутив и установщик Velopack
.\scripts\build-release.ps1
```

---

## ❓ Частые вопросы (FAQ)

<details>
<summary><b>Нужна ли лицензия Minecraft для игры?</b></summary>
Aura Launcher поддерживает как авторизованные профили, так и удобный локальный режим с персональным никнеймом и выбором 3D-скина.
</details>

<details>
<summary><b>Почему P2P лучше обычных виртуальных сетей (Hamachi/Radmin)?</b></summary>
Aura Connect устанавливает соединение напрямую без установки виртуальных сетевых драйверов. Это минимизирует сетевой пинг, убирает паразитный оверхед и исключает ограничения по количеству участников в комнате.
</details>

<details>
<summary><b>Как работают дельта-обновления?</b></summary>
Благодаря алгоритмам упаковщика Velopack скачиваются исключительно байтовые различия (diff) между предыдущей и новой версией. Даже крупное обновление весит всего несколько сотен килобайт и применяется почти мгновенно.
</details>

<details>
<summary><b>Где хранятся настройки, моды и логи?</b></summary>
Конфигурация лаунчера, моды и логи игры хранятся в папке <code>%APPDATA%\.aura</code> (конфиг: <code>%APPDATA%\.aura\config.json</code>, логи игры: <code>%APPDATA%\.aura\logs\latest.log</code>, лог лаунчера: <code>%APPDATA%\Aura\launcher.log</code>). Вы также можете в 1 клик сгенерировать полный диагностический отчёт в разделе настроек.
</details>

---

## 🤝 Участие в разработке (Contributing)

Мы рады любым предложениям по улучшению, сообщениям об ошибках и пулл-реквестам!
- Ознакомьтесь с [Руководством разработчика (CONTRIBUTING.md)](CONTRIBUTING.md).
- Создавайте [Issue](https://github.com/qutlawsoasis-debug/Aura-Launcher/issues) для багов и предложений.

---

## 📄 Лицензия

Проект распространяется под открытой лицензией [MIT License](LICENSE).  
Minecraft является зарегистрированным товарным знаком Mojang Synergies AB. Aura Launcher не связан с Mojang AB или Microsoft.

<div align="center">
  <sub>Создано с ❤️ сообществом Aura. Приятной игры!</sub>
</div>

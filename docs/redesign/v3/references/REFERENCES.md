# Aura Launcher v3 Redesign — Исследование и Каталог Референсов (REFERENCES.md)

Документ фиксирует банк референсов для визуального языка **Aura Launcher v3** («Apple Liquid Glass + Gaming Precision»).
Каждый референс деконструирован по правилам:
- **Что берём в v3**: конкретные радиусы скруглений, specular-блики, толщину кромки, блюр, пружинную динамику (spring-motion), типографику, плотность информации.
- **Что категорически отвергаем**: RGB-радуги, перегруженность элементами, глухие непрозрачные рамки, фальшивые декорации, шумные градиенты.

---

## Категория 1: Apple visionOS & macOS Sequoia (`references/visionos-apple/`)

### 01. `01-visionos-home-screen.png`
- **Источник**: [Wikimedia Commons - Apple Vision Pro visionOS Home Screen](https://upload.wikimedia.org/wikipedia/commons/1/11/Apple_Vision_Pro_home_screen_-_visionOS_27.png)
- **Что берём в v3**:
  - Парящие круглые и капсульные острова с многослойным блюром (`backdrop-filter: blur(28px) saturate(140%)`).
  - Тонкий верхний блик по периметру (`inset 0 1px 0 rgba(255, 255, 255, 0.28)`), имитирующий падение света сверху.
  - Ощущение «воздуха» между островами и глубокая глубина по Z-оси.
- **Что отвергаем**:
  - Полную круглую форму для основных окон — в десктопном лаунчере окна и карточки должны иметь чёткую прямоугольную сетку со скруглением `14px–20px`.

### 02. `02-visionos-materials-system.png`
- **Источник**: [Apple HIG - Foundations Materials](https://developer.apple.com/tutorials/images/com.apple.HIG/foundations-materials-thumbnail@2x.png)
- **Что берём в v3**:
  - Материаловедение стекла: разделение на базовую поверхность (`--glass-panel: rgba(8, 16, 24, 0.52)`), приподнятую (`--glass-panel-elevated`) и вложенные плашки (`--glass-raised: rgba(255, 255, 255, 0.06)`).
  - Светопропускание: фон игры/мира остаётся видимым и атмосферным, но контрастность текста защищена сатурацией 140%.
- **Что отвергаем**:
  - Чересчур светлые подложки. Aura Launcher — это строго тёмная тема (`#050B0F`), никакого светлого молочного стекла.

### 03. `03-visionos-spatial-design.png`
- **Источник**: [Apple HIG - Designing for visionOS](https://developer.apple.com/tutorials/images/com.apple.HIG/platforms-designing-for-visionos-thumbnail@2x.png)
- **Что берём в v3**:
  - Иерархию плоскостей: орнаменты (floating pills) парят над основным окном, выдвинуты на 8–16px ближе к пользователю.
  - Мягкие, физически корректные тени (`0 24px 60px rgba(0, 0, 0, 0.55)`).
- **Что отвергаем**:
  - 3D-стереоскопический рендеринг и перспективные искажения, мешающие читать данные на плоском мониторе.

### 04. `04-visionos-windows-depth.png`
- **Источник**: [Apple HIG - Components Windows](https://developer.apple.com/tutorials/images/com.apple.HIG/components-windows-thumbnail@2x.png)
- **Что берём в v3**:
  - Конструкцию окна: верхний бар управления (window controls), монолитная рамка `1px solid rgba(255, 255, 255, 0.14)`.
  - Внутренний паддинг `24px–32px`, создающий премиальное ощущение свободного пространства.
- **Что отвергаем**:
  - Оконные рамки из толстого белого пластика.

### 05. `05-visionos-tab-bars-floating.png`
- **Источник**: [Apple HIG - Tab Bars](https://developer.apple.com/tutorials/images/com.apple.HIG/components-tab-bars-thumbnail@2x.png)
- **Что берём в v3**:
  - Плавающую навигационную капсулу (floating pill dock) по центру вверху (`border-radius: 32px`, `backdrop-filter: blur(20px)`).
  - Подсветку активной вкладки через `rgba(255, 255, 255, 0.15)` с мягким бликом `inset 0 1px 0 rgba(255, 255, 255, 0.3)`.
  - Маленькую янтарную точку-индикатор под активным разделом.
- **Что отвергаем**:
  - Вертикальные боковые сайдбары на всю высоту, съедающие полезную ширину контента.

### 06. `06-visionos-segmented-controls.png`
- **Источник**: [Apple HIG - Segmented Controls](https://developer.apple.com/tutorials/images/com.apple.HIG/components-segmented-controls-thumbnail@2x.png)
- **Что берём в v3**:
  - Переключатели профилей графики и режимов лаунчера (Ультра / Стандарт / Оптимизация).
  - Скользящую плашку активного сегмента (`spring-duration: 250ms`, `cubic-bezier(0.2, 0.85, 0.25, 1)`).
- **Что отвергаем**:
  - Резкие дискретные скачки без анимации скольжения.

### 07. `07-visionos-sliders.png`
- **Источник**: [Apple HIG - Sliders](https://developer.apple.com/tutorials/images/com.apple.HIG/components-sliders-thumbnail@2x.png)
- **Что берём в v3**:
  - Тонкий трек ползунка RAM (6px) с полупрозрачным фоном `rgba(255, 255, 255, 0.14)` и янтарной полосой заполнения.
  - Круглый ползунок (thumb 22px) с тактильным откликом при drag (`scale(1.1)`).
- **Что отвергаем**:
  - Грубые квадратные Windows 98 / Java Swing слайдеры.

### 08. `08-visionos-toggles.png`
- **Источник**: [Apple HIG - Toggles](https://developer.apple.com/tutorials/images/com.apple.HIG/components-toggles-thumbnail@2x.png)
- **Что берём в v3**:
  - Фирменные тумблеры iOS/visionOS: 48x26px, круглый кноб 20px, плавный переход фона в янтарный при включении.
  - Specular highlight на кнобе для ощущения полированного стекла.
- **Что отвергаем**:
  - Стандартные HTML-чекбоксы с галочками.

### 09. `09-visionos-buttons.png`
- **Источник**: [Apple HIG - Buttons](https://developer.apple.com/tutorials/images/com.apple.HIG/components-buttons-thumbnail@2x.png)
- **Что берём в v3**:
  - Двухуровневую систему кнопок: Primary (янтарная кнопка «Играть» с глубоким glow) и Secondary (стеклянные кнопки с `rgba(255, 255, 255, 0.08)`).
  - Интерактивный scale `scale(0.97)` на pointer-down для немедленного физического отклика.
- **Что отвергаем**:
  - Неоправданные анимации подпрыгивания или смещения `translateY(-4px)`.

### 10. `10-visionos-audio-widget.png`
- **Источник**: [Apple HIG - Playing Audio Pattern](https://developer.apple.com/tutorials/images/com.apple.HIG/patterns-playing-audio-thumbnail@2x.png)
- **Что берём в v3**:
  - Компактный плавающий виджет плеера (floating music pill, radius 48px): обложка трека 40x40px, анимированный эквалайзер из 3 столбиков, кнопки play/pause.
  - Интеграция саундтрека Aura Launcher прямо в шапку интерфейса без перегрузки основного экрана.
- **Что отвергаем**:
  - Громоздкие панели плеера на пол-экрана.

### 11. `11-visionos-motion-springs.png`
- **Источник**: [Apple HIG - Motion Principles](https://developer.apple.com/tutorials/images/com.apple.HIG/foundations-motion-thumbnail@2x.png)
- **Что берём в v3**:
  - Критически демпфированные пружины (`damping: 1.0`, `duration: 400ms`, `cubic-bezier(0.2, 0.85, 0.25, 1)`).
  - Пошаговое появление островов (stagger delays: 60ms, 120ms, 180ms).
- **Что отвергаем**:
  - Линейные скучные переходы `linear` или медленный `ease-in-out` на 1 секунду.

### 12. `12-visionos-typography.png`
- **Источник**: [Apple HIG - Typography Tracking](https://developer.apple.com/tutorials/images/com.apple.HIG/foundations-typography-thumbnail@2x.png)
- **Что берём в v3**:
  - Оптический кернинг: отрицательный трекинг для крупных заголовков (`letter-spacing: -0.025em`) и нейтральный/слегка разреженный для мелких бейджей (`+0.02em`).
  - Строгую иерархию начертаний Manrope (Medium для чтения, SemiBold/Bold для заголовков).
- **Что отвергаем**:
  - Пиксельные Minecraft-шрифты в основном интерфейсе лаунчера (оставляем только как опцию для никнейма).

### 13. `13-visionos-color-vibrancy.png`
- **Источник**: [Apple HIG - Color Vibrancy](https://developer.apple.com/tutorials/images/com.apple.HIG/foundations-color-thumbnail@2x.png)
- **Что берём в v3**:
  - Прозрачные текстовые слои с разным уровнем яркости: Primary (`#FFF`), Secondary (`rgba(245, 248, 255, 0.72)`), Tertiary (`rgba(245, 248, 255, 0.45)`).
- **Что отвергаем**:
  - Серые тусклые монохромные тексты без контраста.

### 14. `14-visionos-layout-hierarchy.png`
- **Источник**: [Apple HIG - Layout](https://developer.apple.com/tutorials/images/com.apple.HIG/foundations-layout-thumbnail@2x.png)
- **Что берём в v3**:
  - Модульную островную сетку: фон виден между независимыми карточками, окно не забито сплошной глухой заливкой.
- **Что отвергаем**:
  - Беспорядочное хаотичное наслоение плашек друг на друга.

---

## Категория 2: Minecraft Game Clients (`references/game-clients/`)

### 01. `01-vape-v4-main-gui.png`
- **Источник**: [Vape.gg Documentation GUI](https://docs.vape.gg/img/vape-gui.png)
- **Что берём в v3**:
  - Экстремальную плотность и эргономику: чёткое левое меню категорий, плиточный список параметров, статус активности.
  - Тёмно-угольную цветовую гамму с янтарным/оранжевым акцентом (идеальное совпадение с фирменным стилем Aura).
- **Что отвергаем**:
  - Чит-специфичные элементы (Reach, AimAssist). В лаунчере эта структура используется для Мастерской модов и Настроек.

### 02. `02-vape-v4-settings-menu.png`
- **Источник**: [Vape.gg Documentation Settings](https://docs.vape.gg/img/gui/open-general-settings.png)
- **Что берём в v3**:
  - Вложенные параметры карточки: раскрытие подробных опций по клику прямо внутри карточки без перехода на отдельный экран.
  - Минималистичные радиокнопки и слайдеры.
- **Что отвергаем**:
  - Слишком мелкий 10px нечитаемый текст.

### 03. `03-vape-v4-hud-overlays.png`
- **Источник**: [Vape.gg Documentation Overlays](https://docs.vape.gg/img/overlays.png)
- **Что берём в v3**:
  - Компактные информационные виджеты (HUD-бейдж пинга, туннеля P2P, онлайн-статуса друзей).
- **Что отвергаем**:
  - Загромождение игрового поля.

### 04. `04-nursultan-clickgui-overview.jpg`
- **Источник**: [Nursultan Client Review Showcase](https://i.ytimg.com/vi/8sN43jc80nk/hqdefault.jpg)
- **Что берём в v3**:
  - Стеклянные карточки модулей со скруглением `12px–14px`.
  - Мягкую подсветку активного состояния модуля (акцентный янтарный контур).
- **Что отвергаем**:
  - Фиолетово-розовый едкий неон и аниме-баннеры в шапке.

### 05. `05-nursultan-clickgui-modules.jpg`
- **Источник**: [Nursultan Client Modules UI](https://i.ytimg.com/vi/dG_seOS37uM/hqdefault.jpg)
- **Что берём в v3**:
  - Двухколоночный лэйаут модулей в экране Мастерской (Workshop): левая колонка — системные моды оптимизации (Sodium, Lithium, Iris), правая — визуальные моды и шейдеры.
- **Что отвергаем**:
  - Сплошные перегруженные списки без смысловой группировки.

### 06. `06-nursultan-hud-elements.jpg`
- **Источник**: [Nursultan Client HUD Showcase](https://i.ytimg.com/vi/izu20i111YQ/hqdefault.jpg)
- **Что берём в v3**:
  - Анимированные бейджи статуса с зелёной и янтарной точкой пульсации (Ping, P2P Host status).
- **Что отвергаем**:
  - Агрессивный RGB-перелив (rainbow effect).

### 07. `07-celestial-recode-showcase.jpg`
- **Источник**: [Celestial Recode Review](https://i.ytimg.com/vi/ajbGcnK1k9w/hqdefault.jpg)
- **Что берём в v3**:
  - Плавную акриловую прозрачность и качественный блюр подложки.
  - Интерактивные чипы (фильтры категорий: Все / Оптимизация / Визуал / Геймплей).
- **Что отвергаем**:
  - Избыточное свечение вокруг текста (glow blur > 10px), затрудняющее чтение.

### 08. `08-celestial-clickgui-cards.jpg`
- **Источник**: [Celestial ClickGUI Layout](https://i.ytimg.com/vi/x4N5s0fWTJI/hqdefault.jpg)
- **Что берём в v3**:
  - Строгие карточки с легким specular-кантом `inset 0 1px 0 rgba(255, 255, 255, 0.18)`.
  - Компактное размещение тумблера справа от названия функции.
- **Что отвергаем**:
  - Черные глухие дыры вместо карточек.

### 09. `09-celestial-sliders-switchers.jpg`
- **Источник**: [Celestial Controls Showcase](https://i.ytimg.com/vi/76viXY31zes/hqdefault.jpg)
- **Что берём в v3**:
  - Цифровые значения слайдера, отображаемые прямо над ползунком при наведении («4096 MB», «8192 MB»).
- **Что отвергаем**:
  - Отсутствие единиц измерения или некликабельные мелкие бегунки.

### 10. `10-plintus-visuals-hud.jpg`
- **Источник**: [Plintus Visuals Minecraft Showcase](https://i.ytimg.com/vi/pHXNWXtHscU/hqdefault.jpg)
- **Что берём в v3**:
  - Лаконичные полупрозрачные плашки параметров в стиле минималистичного HUD.
  - Высокую контрастность и отличную читаемость на фоне динамичного мира.
- **Что отвергаем**:
  - Привязку элементов к краям экрана без отступов.

### 11. `11-plintus-visuals-pvp-card.jpg`
- **Источник**: [Plintus Visuals PvP Card UI](https://i.ytimg.com/vi/tB8uJNTnJ44/hqdefault.jpg)
- **Что берём в v3**:
  - Карточки статистики игрока: аватар, пинг, статус сервера (онлайн/оффлайн) в компактной форме.
- **Что отвергаем**:
  - Вырвиглазные текстурные рамки из пикселей.

### 12. `12-plintus-visuals-config.jpg`
- **Источник**: [Plintus Visuals Settings Layout](https://i.ytimg.com/vi/oFgHw93GEuE/hqdefault.jpg)
- **Что берём в v3**:
  - Удобную сетку быстрых пресетов настроек (в один клик: «Низкие», «Баланс», «Ультра»).
- **Что отвергаем**:
  - Неинтуитивные сокращения названий.

---

## Категория 3: Modern Minecraft Launchers (`references/launchers/`)

### 01. `01-prism-instance-screenshots.png`
- **Источник**: [Prism Launcher Screenshots Guide](https://prismlauncher.org/img/screenshots/InstanceScreenshotsDark.png)
- **Что берём в v3**:
  - Архитектуру галереи скриншотов: предпросмотр кадров из папки `screenshots/` с датой и кнопкой «Открыть в проводнике».
  - Тёмную палитру фона.
- **Что отвергаем**:
  - Устаревший классический интерфейс в стиле Qt/Win32 с мелкими серыми системными тулбарами.

### 02. `02-prism-copy-instance.png`
- **Источник**: [Prism Launcher Copy Instance UI](https://prismlauncher.org/img/screenshots/CopyInstanceDark.png)
- **Что берём в v3**:
  - Чёткую структуру модального диалога: заголовок, поле ввода с placeholder, вспомогательные чекбоксы и кнопки внизу справа.
- **Что отвергаем**:
  - Глухой непрозрачный серый фон диалогового окна без блюра.

### 03. `03-prism-new-instance-technic.png`
- **Источник**: [Prism Launcher Technic Import UI](https://prismlauncher.org/img/screenshots/NewInstanceTechnicDark.png)
- **Что берём в v3**:
  - Список скроллинга версий и сборок с поисковой строкой наверху.
- **Что отвергаем**:
  - Сплошные таблицы со скучными границами столбцов.

### 04. `04-prism-settings-console.png`
- **Источник**: [Prism Launcher Console UI](https://prismlauncher.org/img/screenshots/SettingsLauncherConsoleDark.png)
- **Что берём в v3**:
  - Окно диагностических логов лаунчера: моноширинный шрифт, цветовая дифференциация уровней лога (INFO — белый, WARN — янтарный, ERROR — красный).
- **Что отвергаем**:
  - Нечитаемые монотонные простыни логов без цветовой разметки.

### 05. `05-prism-new-instance-modrinth.png`
- **Источник**: [Prism Launcher Modrinth Search UI](https://prismlauncher.org/img/screenshots/NewInstanceModrinthDark.png)
- **Что берём в v3**:
  - Карточки модпаков с иконкой, кратким описанием, количеством загрузок и версией игры.
- **Что отвергаем**:
  - Отсутствие визуальных акцентов на кнопке установки.

### 06. `06-prism-new-instance-vanilla.png`
- **Источник**: [Prism Launcher Vanilla Version Select](https://prismlauncher.org/img/screenshots/NewInstanceVanillaDark.png)
- **Что берём в v3**:
  - Лаконичный селектор версии Minecraft (в Aura Launcher v3: 1.20.1 Fabric 0.19.5).
- **Что отвергаем**:
  - Избыточные списки древних бета-версий, не относящихся к сборке Aura-Pack.

### 07. `07-modrinth-homepage.png`
- **Источник**: [Wikimedia Commons - Modrinth Homepage](https://upload.wikimedia.org/wikipedia/commons/thumb/c/c1/Modrinth%27s_home_page.png/960px-Modrinth%27s_home_page.png)
- **Что берём в v3**:
  - Премиальную тёмную эстетику Modrinth App: скруглённые карточки, аккуратные бейджи тегов (Fabric, 1.20.1, Performance).
  - Контрастный акцентный цвет для главных целевых действий.
- **Что отвергаем**:
  - Веб-хедер на всю ширину с кучей ссылок, не нужных в лаунчере.

### 08. `08-modrinth-search-opensource.png`
- **Источник**: [Wikimedia Commons - Modrinth Search Interface](https://upload.wikimedia.org/wikipedia/commons/4/4e/Modrinth_search_open_source_%28cropped%29.png)
- **Что берём в v3**:
  - Поисковую строку с мгновенной фильтрацией модов в экране Workshop.
  - Бейджи статусов (Активен / Отключен / Доступно обновление).
- **Что отвергаем**:
  - Излишне сложную фильтрацию по лицензиям.

### 09. `09-lunar-store-coins.png`
- **Источник**: [Lunar Client Coins & Store UI](https://a.storyblok.com/f/150231/1300x500/334fdcdc71/coins-purchase.png)
- **Что берём в v3**:
  - Оформление карточек в экране «Гардероб» (Wardrobe): предпросмотр скина, плаща, выбор slim/classic модели.
  - Мягкую подсветку выбранного элемента.
- **Что отвергаем**:
  - Донатные баннеры и микротранзакции. В Aura Launcher все функции и скины бесплатны.

### 10. `10-lunar-download-grid.png`
- **Источник**: [Lunar Client UI Grid Showcase](https://cmsassets.lunarclientcdn.com/cdn-cgi/image/quality=85,format=auto/assets/edcac7cc5f-lunar-client-download-grid.png)
- **Что берём в v3**:
  - Сбалансированную модульную сетку, гармонию между иллюстрацией/аватаром и текстовым описанием.
- **Что отвергаем**:
  - Огромные рекламные блоки.

---

## Категория 4: Modern Glass UI Concepts (`references/glass-ui/`)

### 01. `01-glasskit-intro-showcase.jpg`
- **Источник**: [GlassKit UI Component Library](https://raw.githubusercontent.com/JUNGHERZ/GlassKit/main/intro_screenshot.jpg)
- **Что берём в v3**:
  - Реализацию многослойного стекла на CSS: идеальное сочетание `backdrop-filter: blur(24px)`, границы `1px solid rgba(255, 255, 255, 0.12)` и верхнего блика.
  - Карточки статистики и графиков.
- **Что отвергаем**:
  - Размытие текста на слишком прозрачных подложках (в v3 текст всегда 100% контрастный).

### 02. `02-swiftui-glass-materials.png`
- **Источник**: [Glassmorphism SwiftUI Library](https://raw.githubusercontent.com/TheAppWizard/GlassmorphismSwiftUI/main/glassop.png)
- **Что берём в v3**:
  - Нативные радиусы скругления (`16px–24px`), органично смотрящиеся в связке со стеклянными текстурами.
  - Объемный эффект линзы на кнопках.
- **Что отвергаем**:
  - Белые молочные пятна без сатурации.

### 03. `03-vscode-glass-windows.png`
- **Источник**: [Glass Theme Doyle for VS Code](https://raw.githubusercontent.com/KrautGTI/glass-theme-doyle/master/windows.png)
- **Что берём в v3**:
  - Интеграцию тёмного стекла в реальное десктопное приложение: читаемость кода и логов на полупрозрачном фоне.
- **Что отвергаем**:
  - Полную прозрачность рабочего поля (в v3 поле логов имеет плотный фон `rgba(8, 16, 24, 0.85)`).

### 04. `04-vscode-glass-settings.png`
- **Источник**: [Glass Theme Settings UI](https://raw.githubusercontent.com/KrautGTI/glass-theme-doyle/master/windowsSettings.png)
- **Что берём в v3**:
  - Стеклянные инпуты настроек: поля ввода памяти, селекторы папки игры.
- **Что отвергаем**:
  - Плоские белые границы без блика.

### 05. `05-glass-switch-dashboard.png`
- **Источник**: [Switch Dashboard Real-time Glass UI](https://raw.githubusercontent.com/byte4geek/switch-dashboard/main/images/dashboard.png)
- **Что берём в v3**:
  - Виджеты сетевого статуса: график пинга, статус P2P туннеля (Playit / UPnP), скорость синхронизации.
  - Тонкие разделители `rgba(255, 255, 255, 0.08)`.
- **Что отвергаем**:
  - Перегруз графиками, отвлекающими от игры.

### 06. `06-dark-glass-refraction.jpg`
- **Источник**: [Unsplash Dark Glass Refraction](https://images.unsplash.com/photo-1618005182384-a83a8bd57fbe?w=1200&q=80)
- **Что берём в v3**:
  - Эстетику преломления света и микроградиента: глубокая тёмная основа с тонкими световыми кромками.
- **Что отвергаем**:
  - Слишком яркие цветные пятна.

### 07. `07-dark-frosted-geometry.jpg`
- **Источник**: [Unsplash Frosted Dark Texture](https://images.unsplash.com/photo-1634017839464-5c339ebe3cb4?w=1200&q=80)
- **Что берём в v3**:
  - Текстуру микроматового стекла (`backdrop-filter: blur(28px)`), сглаживающую динамичный фон заднего плана.
- **Что отвергаем**:
  - Зернистый шум (noise texture), снижающий производительность рендеринга WebView2 / WPF.

### 08. `08-liquid-glass-flow.jpg`
- **Источник**: [Unsplash Liquid Glass Flow](https://images.unsplash.com/photo-1618005198919-d3d4b5a92ead?w=1200&q=80)
- **Что берём в v3**:
  - Плавность и физичность световых пятен (янтарный sunbeam-градиент в правом верхнем углу интерфейса).
- **Что отвергаем**:
  - Хаотичные деформированные формы (интерфейс Aura строго геометричен и выровнен).

### 09. `09-crystal-iridescent-dark.jpg`
- **Источник**: [Unsplash Dark Crystal Edge](https://images.unsplash.com/photo-1550684848-fac1c5b4e853?w=1200&q=80)
- **Что берём в v3**:
  - Чёткость граней карточек (1px crisp edge): стекло не выглядит «мыльным», границы контрастны.
- **Что отвергаем**:
  - Радужные переливы на гранях.

---

## 5. Сводные правила редизайна v3 на основе референсов

| Параметр | Значение в Aura Launcher v3 | Обоснование референсом |
| :--- | :--- | :--- |
| **Базовый фон** | `#050B0F` + обои мира + виньетка | Modrinth + Apple visionOS dark canvas |
| **Стеклянный остров** | `rgba(8, 16, 24, 0.52)` + blur 28px + sat 140% | visionOS Materials System |
| **Кромка и блик** | `1px solid rgba(255, 255, 255, 0.14)` + `inset 0 1px 0 rgba(255, 255, 255, 0.28)` | Apple WWDC Fluid Interfaces specular |
| **Скругления окон** | `20px` (окно/панель), `14px` (карточка), `32px` (dock pill) | Apple Human Interface Guidelines |
| **Акцентный цвет** | Янтарный `#F2A63C` (glow: `0 0 24px rgba(242, 166, 60, 0.45)`) | Vape v4 + фирменный стиль Aura |
| **Типографика** | `Manrope` (tracking: `-0.025em` заголовки, `0` body) | Apple Typography & Modrinth App |
| **Пружинная физика** | `cubic-bezier(0.2, 0.85, 0.25, 1)`, duration 400ms | Apple WWDC Designing Fluid Interfaces |
| **Плотность модулей** | Двухколоночная сетка карточек с тумблерами и инлайн-опциями | Vape v4 + Celestial Recode |

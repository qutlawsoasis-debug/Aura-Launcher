using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using Microsoft.Win32;

namespace AuraLauncher
{
    public class LauncherConfig
    {
        public string Nickname { get; set; } = "Player";
        public int RamMb { get; set; } = 6144;
        public string SkinPath { get; set; } = "";
        public string GitHubRepo { get; set; } = "qutlawsoasis-debug/Aura";
        public string CurrentVersion { get; set; } = "0.0.0";
    }

    public partial class MainWindow : Window
    {
        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int attrValue, int attrSize);

        private const int DWMWA_USE_IMMERSIVE_DARK_MODE = 20;
        private const int DWMWA_WINDOW_CORNER_PREFERENCE = 33;
        private const int DWMWA_SYSTEMBACKDROP_TYPE = 38;

        private LauncherConfig _config = new();
        private readonly string _configPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "aura_config.json");
        private readonly HttpClient _http = new();
        private readonly DispatcherTimer _quoteTimer = new();
        private int _quoteIndex = 0;

        private readonly string[] _csQuotes = new[]
        {
            "Снаряжаем деревенских жителей мечами...",
            "Калибруем шейдерные лучи...",
            "Выращиваем горные хребты Terralith...",
            "Смазываем дверные петли...",
            "Скашиваем лишнюю траву...",
            "Упаковываем спальники в рюкзаки...",
            "Сжимаем квантовые сетевые туннели...",
            "Полируем линзы оптического зума...",
            "Сверяем координаты путеводных камней...",
            "Проверяем целостность кубического мира..."
        };

        public MainWindow()
        {
            InitializeComponent();
            _http.DefaultRequestHeaders.Add("User-Agent", "AuraLauncher");
            _quoteTimer.Interval = TimeSpan.FromSeconds(3.5);
            _quoteTimer.Tick += (s, e) => NextQuote();

            Loaded += MainWindow_Loaded;
        }

        private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyWindows11MicaOrAcrylic();
            LoadConfig();
            LoadModsList();
            await CheckForUpdatesAsync();
        }

        private void Window_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            if (e.ChangedButton == MouseButton.Left)
            {
                DragMove();
            }
        }

        private void ApplyWindows11MicaOrAcrylic()
        {
            try
            {
                var helper = new WindowInteropHelper(this);
                int darkMode = 1;
                DwmSetWindowAttribute(helper.Handle, DWMWA_USE_IMMERSIVE_DARK_MODE, ref darkMode, sizeof(int));

                int cornerPref = 2; // Round
                DwmSetWindowAttribute(helper.Handle, DWMWA_WINDOW_CORNER_PREFERENCE, ref cornerPref, sizeof(int));

                int backdropType = 3; // 3 = Acrylic, 2 = Mica
                DwmSetWindowAttribute(helper.Handle, DWMWA_SYSTEMBACKDROP_TYPE, ref backdropType, sizeof(int));
            }
            catch { }
        }

        private void LoadConfig()
        {
            if (File.Exists(_configPath))
            {
                try
                {
                    var json = File.ReadAllText(_configPath);
                    _config = JsonSerializer.Deserialize<LauncherConfig>(json) ?? new LauncherConfig();
                }
                catch { }
            }

            var nick = string.IsNullOrWhiteSpace(_config.Nickname) ? "Player" : _config.Nickname;
            TxtSettingsNick.Text = nick;
            TxtSidebarNick.Text = nick;
            TxtSkinNickPreview.Text = nick;

            TxtGitHubRepo.Text = string.IsNullOrWhiteSpace(_config.GitHubRepo) ? "qutlawsoasis-debug/Aura" : _config.GitHubRepo;
            TxtGameDir.Text = FindMinecraftDir();

            switch (_config.RamMb)
            {
                case 4096: RbRam4.IsChecked = true; break;
                case 8192: RbRam8.IsChecked = true; break;
                case 12288: RbRam12.IsChecked = true; break;
                default: RbRam6.IsChecked = true; break;
            }

            UpdateSkinModelPreview(_config.SkinPath);
        }

        private void SaveConfig()
        {
            try
            {
                _config.Nickname = TxtSettingsNick.Text.Trim();
                _config.GitHubRepo = TxtGitHubRepo.Text.Trim();
                var json = JsonSerializer.Serialize(_config, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(_configPath, json);
            }
            catch { }
        }

        private void NextQuote()
        {
            _quoteIndex = (_quoteIndex + 1) % _csQuotes.Length;
            TxtWittyQuote.Text = _csQuotes[_quoteIndex];
        }

        private void BtnMinimize_Click(object sender, RoutedEventArgs e)
        {
            WindowState = WindowState.Minimized;
        }

        private void BtnClose_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();
            Close();
        }

        private void Nav_Click(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag is string tag)
            {
                TabPlay.Visibility = tag == "TabPlay" ? Visibility.Visible : Visibility.Collapsed;
                TabSkin.Visibility = tag == "TabSkin" ? Visibility.Visible : Visibility.Collapsed;
                TabMods.Visibility = tag == "TabMods" ? Visibility.Visible : Visibility.Collapsed;
                TabSettings.Visibility = tag == "TabSettings" ? Visibility.Visible : Visibility.Collapsed;
            }
        }

        private void TxtNickname_TextChanged(object sender, TextChangedEventArgs e)
        {
            var nick = TxtSettingsNick.Text.Trim();
            if (string.IsNullOrEmpty(nick)) nick = "Player";
            _config.Nickname = nick;
            TxtSidebarNick.Text = nick;
            TxtSkinNickPreview.Text = nick;
        }

        private void RamRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is RadioButton rb && rb.Tag != null)
            {
                if (int.TryParse(rb.Tag.ToString(), out int ram))
                {
                    _config.RamMb = ram;
                }
            }
        }

        private void BtnOpenGameDir_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                var dir = TxtGameDir.Text;
                if (!Directory.Exists(dir)) Directory.CreateDirectory(dir);
                Process.Start(new ProcessStartInfo
                {
                    FileName = dir,
                    UseShellExecute = true
                });
            }
            catch { }
        }

        private async void BtnSaveSettings_Click(object sender, RoutedEventArgs e)
        {
            SaveConfig();
            TxtSettingsStatus.Visibility = Visibility.Visible;
            await Task.Delay(2000);
            TxtSettingsStatus.Visibility = Visibility.Collapsed;
        }

        // ==========================================
        // SKIN SYSTEM & FULL-BODY RENDERING
        // ==========================================
        private void UpdateSkinModelPreview(string? skinPath)
        {
            var modelImg = CreateFullBodyModel(skinPath);
            ImgBodyModel.Source = modelImg;
            ImgWardrobeModel.Source = modelImg;
        }

        private void BtnChangeSkin_Click(object sender, RoutedEventArgs e)
        {
            var ofd = new OpenFileDialog
            {
                Filter = "Minecraft Skin (*.png)|*.png",
                Title = "Выберите файл скина (.png)"
            };

            if (ofd.ShowDialog() == true)
            {
                try
                {
                    _config.SkinPath = ofd.FileName;
                    UpdateSkinModelPreview(_config.SkinPath);
                    SyncSkinToGame(_config.SkinPath);
                    SaveConfig();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при загрузке скина: {ex.Message}", "AURA", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void BtnResetSteve_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                _config.SkinPath = "";
                UpdateSkinModelPreview(null);

                // Copy default steve.png to game skin directory
                var stevePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steve.png");
                if (File.Exists(stevePath))
                {
                    SyncSkinToGame(stevePath);
                }

                SaveConfig();
            }
            catch { }
        }

        private void SyncSkinToGame(string skinFilePath)
        {
            try
            {
                var mcDir = FindMinecraftDir();
                var nick = string.IsNullOrWhiteSpace(_config.Nickname) ? "Player" : _config.Nickname;
                var localSkinDir = Path.Combine(mcDir, "CustomSkinLoader", "LocalSkin", "skins");
                Directory.CreateDirectory(localSkinDir);

                var targetPath = Path.Combine(localSkinDir, $"{nick}.png");
                File.Copy(skinFilePath, targetPath, true);
            }
            catch { }
        }

        private ImageSource CreateFullBodyModel(string? skinPath)
        {
            BitmapSource? sourceBmp = null;
            if (!string.IsNullOrEmpty(skinPath) && File.Exists(skinPath))
            {
                try
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(skinPath);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    sourceBmp = bmp;
                }
                catch { }
            }

            if (sourceBmp == null)
            {
                string stevePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steve.png");
                if (File.Exists(stevePath))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(stevePath);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        sourceBmp = bmp;
                    }
                    catch { }
                }
            }

            if (sourceBmp == null)
            {
                return new WriteableBitmap(160, 320, 96, 96, PixelFormats.Bgra32, null);
            }

            var conv = new FormatConvertedBitmap(sourceBmp, PixelFormats.Bgra32, null, 0);
            int skinW = conv.PixelWidth;
            int skinH = conv.PixelHeight;

            uint[] skinPixels = new uint[skinW * skinH];
            conv.CopyPixels(skinPixels, skinW * 4, 0);

            uint[] canvas = new uint[16 * 32];

            void Blit(int sx, int sy, int w, int h, int dx, int dy, bool flipX = false)
            {
                if (sx + w > skinW || sy + h > skinH) return;

                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int srcX = flipX ? (sx + w - 1 - x) : (sx + x);
                        int srcY = sy + y;
                        uint px = skinPixels[srcY * skinW + srcX];
                        byte a = (byte)((px >> 24) & 0xFF);
                        if (a == 0) continue;

                        int dstX = dx + x;
                        int dstY = dy + y;
                        if (dstX < 0 || dstX >= 16 || dstY < 0 || dstY >= 32) continue;

                        int dstIdx = dstY * 16 + dstX;
                        if (a == 255)
                        {
                            canvas[dstIdx] = px;
                        }
                        else
                        {
                            uint dstPx = canvas[dstIdx];
                            byte da = (byte)((dstPx >> 24) & 0xFF);
                            byte dr = (byte)((dstPx >> 16) & 0xFF);
                            byte dg = (byte)((dstPx >> 8) & 0xFF);
                            byte db = (byte)(dstPx & 0xFF);

                            byte sr = (byte)((px >> 16) & 0xFF);
                            byte sg = (byte)((px >> 8) & 0xFF);
                            byte sb = (byte)(px & 0xFF);

                            int inv = 255 - a;
                            int oa = a + (da * inv) / 255;
                            int or = (sr * a + dr * inv) / 255;
                            int og = (sg * a + dg * inv) / 255;
                            int ob = (sb * a + db * inv) / 255;

                            canvas[dstIdx] = (uint)((oa << 24) | (or << 16) | (og << 8) | ob);
                        }
                    }
                }
            }

            // Head (8,8,8,8) + Hat (40,8,8,8) at (4,0)
            Blit(8, 8, 8, 8, 4, 0);
            Blit(40, 8, 8, 8, 4, 0);

            // Torso (20,20,8,12) + Jacket (20,36,8,12) at (4,8)
            Blit(20, 20, 8, 12, 4, 8);
            Blit(20, 36, 8, 12, 4, 8);

            // Right Arm (44,20,4,12) + Sleeve (44,36,4,12) at (0,8)
            Blit(44, 20, 4, 12, 0, 8);
            Blit(44, 36, 4, 12, 0, 8);

            // Left Arm
            if (skinH >= 64)
            {
                Blit(36, 52, 4, 12, 12, 8);
                Blit(52, 52, 4, 12, 12, 8);
            }
            else
            {
                Blit(44, 20, 4, 12, 12, 8, flipX: true);
            }

            // Right Leg
            Blit(4, 20, 4, 12, 4, 20);
            Blit(4, 36, 4, 12, 4, 20);

            // Left Leg
            if (skinH >= 64)
            {
                Blit(20, 52, 4, 12, 8, 20);
                Blit(4, 52, 4, 12, 8, 20);
            }
            else
            {
                Blit(4, 20, 4, 12, 8, 20, flipX: true);
            }

            // Upscale 10x to 160x320 for razor-sharp rendering on all screens
            uint[] scaled = new uint[160 * 320];
            for (int y = 0; y < 320; y++)
            {
                int sy = y / 10;
                int rowStart = y * 160;
                int srcRowStart = sy * 16;
                for (int x = 0; x < 160; x++)
                {
                    scaled[rowStart + x] = canvas[srcRowStart + (x / 10)];
                }
            }

            var wb = new WriteableBitmap(160, 320, 96, 96, PixelFormats.Bgra32, null);
            wb.WritePixels(new Int32Rect(0, 0, 160, 320), scaled, 160 * 4, 0);
            wb.Freeze();
            return wb;
        }

        // ==========================================
        // MODS CATALOG
        // ==========================================
        private void LoadModsList()
        {
            PanelModsList.Children.Clear();
            var mcDir = FindMinecraftDir();
            var modsDir = Path.Combine(mcDir, "mods");

            var modDetails = new Dictionary<string, (string Title, string Category, string Description)>(StringComparer.OrdinalIgnoreCase)
            {
                ["sodium-fabric"] = ("Sodium", "Оптимизация", "Революционный графический движок, увеличивающий FPS в разы."),
                ["iris"] = ("Iris Shaders", "Графика", "Современный движок шейдеров с поддержкой шейдеров OptiFine."),
                ["lithium-fabric"] = ("Lithium", "Оптимизация", "Комплексная оптимизация игровой физики, мобов и чанков."),
                ["ferritecore"] = ("FerriteCore", "Память", "Снижает потребление оперативной памяти Minecraft до 50%."),
                ["modernfix"] = ("ModernFix", "Оптимизация", "Устраняет утечки памяти и ускоряет время запуска игры."),
                ["indium"] = ("Indium", "Графика", "Мост совместимости между Sodium и модами с кастомным рендером."),
                ["immediatelyfast"] = ("ImmediatelyFast", "Оптимизация", "Оптимизация отрисовки интерфейса, текста и частиц."),
                ["entityculling"] = ("Entity Culling", "Оптимизация", "Скрывает невидимых за стенами мобов для экономии FPS."),
                ["krypton"] = ("Krypton", "Сеть", "Оптимизация сетевого стека для плавного мультиплеера."),
                ["terralith"] = ("Terralith", "Мир", "Глобальная генерация 100+ новых ванильных биомов и пещер."),
                ["tectonic"] = ("Tectonic", "Мир", "Массивные горные гряды, подземные реки и рельеф."),
                ["ctov"] = ("CTOV (Villages)", "Деревни", "Капитальный редизайн деревень под каждый биом мира."),
                ["guardvillagers"] = ("Guard Villagers", "Геймплей", "Жители-стражники с мечами и луками, защищающие деревни."),
                ["travelersbackpack"] = ("Traveler's Backpack", "Снаряжение", "Удобные рюкзаки со спальниками и баками для жидкостей."),
                ["waystones"] = ("Waystones", "Путешествия", "Путеводные камни для телепортации между поселениями."),
                ["farmersdelight"] = ("Farmer's Delight", "Кулинария", "Расширенная кулинария, готовка в котлах, сковороды и блюда."),
                ["treechop"] = ("TreeChop", "Геймплей", "Реалистичная рубка деревьев с динамической анимацией ствола."),
                ["carryon"] = ("Carry On", "Геймплей", "Возможность переносить сундуки, животных и мелкие блоки в руках."),
                ["artifacts"] = ("Artifacts", "Снаряжение", "Редкие ценные реликвии и аксессуары в сундуках подземелий."),
                ["comforts"] = ("Comforts", "Геймплей", "Спальные мешки и гамаки для отдыха без смены точки спавна."),
                ["appleskin"] = ("AppleSkin", "Интерфейс", "Показ насыщения и восстанавливаемого здоровья еды."),
                ["jade"] = ("Jade (WAILA)", "Интерфейс", "Информативная плашка с описанием блока или моба под прицелом."),
                ["zoomify"] = ("Zoomify", "Управление", "Плавный зум с кинематографическим приближением на C."),
                ["modmenu"] = ("Mod Menu", "Интерфейс", "Главное меню модов и настроек конфигурации на ~ (тильду)."),
                ["controlling"] = ("Controlling", "Интерфейс", "Поиск и удобное устранение конфликтов клавиш управления."),
                ["customskinloader"] = ("CustomSkinLoader", "Скины", "Отображение HD и кастомных скинов без лицензии."),
                ["sound-physics-remastered"] = ("Sound Physics", "Звук", "Реалистичная акустика, эхо в пещерах и реверберация."),
                ["dungeons-and-taverns"] = ("Dungeons and Taverns", "Структуры", "Атмосферные таверны и интересные данжи для исследования."),
                ["clumps"] = ("Clumps", "Оптимизация", "Объединение сфер опыта в один сгусток для устранения лагов."),
                ["animated-doors"] = ("Animated Doors", "Анимация", "Плавная механика открытия дверей."),
                ["short-grass"] = ("Short Grass", "Графика", "Аккуратная низкая трава для чистого обзора биомов."),
                ["doubledoors"] = ("Double Doors", "Геймплей", "Одновременное открытие двойных дверей одним кликом."),
                ["fallingleaves"] = ("Falling Leaves", "Атмосфера", "Опадающие с деревьев разноцветные листья."),
                ["visuality"] = ("Visuality", "Атмосфера", "Красивые частицы искр, капель и ударов."),
                ["eating-animation"] = ("Eating Animation", "Анимация", "Анимация откусывания еды в руке."),
                ["notenoughanimations"] = ("Not Enough Animations", "Анимация", "Плавные движения персонажа от третьего лица.")
            };

            var jars = Directory.Exists(modsDir)
                ? Directory.GetFiles(modsDir, "*.jar")
                : Array.Empty<string>();

            var processed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var jar in jars)
            {
                var fn = Path.GetFileName(jar);
                string matchedKey = "";
                foreach (var kvp in modDetails)
                {
                    if (fn.Contains(kvp.Key, StringComparison.OrdinalIgnoreCase))
                    {
                        matchedKey = kvp.Key;
                        break;
                    }
                }

                if (matchedKey != "" && !processed.Contains(matchedKey))
                {
                    processed.Add(matchedKey);
                    var info = modDetails[matchedKey];
                    PanelModsList.Children.Add(CreateModItem(info.Title, info.Category, info.Description));
                }
                else if (matchedKey == "")
                {
                    var cleanTitle = Path.GetFileNameWithoutExtension(fn);
                    PanelModsList.Children.Add(CreateModItem(cleanTitle, "Мод", fn));
                }
            }

            if (PanelModsList.Children.Count == 0)
            {
                foreach (var kvp in modDetails)
                {
                    PanelModsList.Children.Add(CreateModItem(kvp.Value.Title, kvp.Value.Category, kvp.Value.Description));
                }
            }
        }

        private Border CreateModItem(string title, string category, string description)
        {
            var brd = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x14, 0x18, 0x22)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x20, 0x27, 0x38)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(14, 10, 14, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sp = new StackPanel();
            var tbTitle = new TextBlock
            {
                Text = title,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.Bold
            };
            var tbDesc = new TextBlock
            {
                Text = description,
                Foreground = new SolidColorBrush(Color.FromRgb(0x94, 0xA3, 0xB8)),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(tbTitle);
            sp.Children.Add(tbDesc);

            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(40, 0x10, 0xB9, 0x81)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(80, 0x10, 0xB9, 0x81)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            var tbCat = new TextBlock
            {
                Text = category,
                Foreground = new SolidColorBrush(Color.FromRgb(0x34, 0xD3, 0x99)),
                FontSize = 11,
                FontWeight = FontWeights.SemiBold
            };
            badge.Child = tbCat;

            Grid.SetColumn(sp, 0);
            Grid.SetColumn(badge, 1);
            grid.Children.Add(sp);
            grid.Children.Add(badge);

            brd.Child = grid;
            return brd;
        }

        // ==========================================
        // GITHUB UPDATE SYSTEM
        // ==========================================
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                TxtTechnicalStatus.Text = "Проверка обновлений...";
                var url = $"https://api.github.com/repos/{_config.GitHubRepo}/releases/latest";
                var response = await _http.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var tagName = doc.RootElement.GetProperty("tag_name").GetString() ?? "";

                    if (tagName != _config.CurrentVersion)
                    {
                        TxtTechnicalStatus.Text = $"Доступно обновление ({tagName})";
                        return;
                    }
                }

                TxtTechnicalStatus.Text = "Сборка актуальна";
            }
            catch
            {
                TxtTechnicalStatus.Text = "Локальный режим";
            }
        }

        // ==========================================
        // PLAY & LAUNCH
        // ==========================================
        private async void BtnPlay_Click(object sender, RoutedEventArgs e)
        {
            BtnPlay.IsEnabled = false;
            SaveConfig();

            if (!string.IsNullOrEmpty(_config.SkinPath) && File.Exists(_config.SkinPath))
            {
                SyncSkinToGame(_config.SkinPath);
            }
            else
            {
                var stevePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steve.png");
                if (File.Exists(stevePath))
                {
                    SyncSkinToGame(stevePath);
                }
            }

            try
            {
                bool updateNeeded = await CheckIfUpdateNeededAsync();
                if (updateNeeded)
                {
                    await PerformUpdateDownloadAsync();
                }

                TxtTechnicalStatus.Text = "Запуск игры...";
                TxtWittyQuote.Text = "Увидимся в кубическом мире!";

                LaunchMinecraft();
                await Task.Delay(1800);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска: {ex.Message}", "AURA", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnPlay.IsEnabled = true;
                BrdProgress.Visibility = Visibility.Collapsed;
                _quoteTimer.Stop();
            }
        }

        private async Task<bool> CheckIfUpdateNeededAsync()
        {
            try
            {
                var mcDir = FindMinecraftDir();
                var modsDir = Path.Combine(mcDir, "mods");
                if (!Directory.Exists(modsDir) || Directory.GetFiles(modsDir).Length == 0)
                {
                    return true;
                }

                var url = $"https://api.github.com/repos/{_config.GitHubRepo}/releases/latest";
                var response = await _http.GetAsync(url);
                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
                    return tag != _config.CurrentVersion;
                }
            }
            catch { }

            return false;
        }

        private async Task PerformUpdateDownloadAsync()
        {
            _quoteTimer.Start();
            BrdProgress.Visibility = Visibility.Visible;
            ProgressBarDownload.Value = 0;

            try
            {
                var url = $"https://api.github.com/repos/{_config.GitHubRepo}/releases/latest";
                var response = await _http.GetAsync(url);
                if (!response.IsSuccessStatusCode) return;

                var json = await response.Content.ReadAsStringAsync();
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                var tagName = root.GetProperty("tag_name").GetString() ?? "v1.0.0";

                string downloadUrl = "";
                if (root.TryGetProperty("assets", out var assets) && assets.GetArrayLength() > 0)
                {
                    foreach (var a in assets.EnumerateArray())
                    {
                        var name = a.GetProperty("name").GetString() ?? "";
                        if (name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                        {
                            downloadUrl = a.GetProperty("browser_download_url").GetString() ?? "";
                            break;
                        }
                    }
                }

                if (string.IsNullOrEmpty(downloadUrl))
                {
                    downloadUrl = root.GetProperty("zipball_url").GetString() ?? "";
                }

                if (!string.IsNullOrEmpty(downloadUrl))
                {
                    var tempZip = Path.Combine(Path.GetTempPath(), "aura_update.zip");
                    using (var sResponse = await _http.GetAsync(downloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        sResponse.EnsureSuccessStatusCode();
                        var totalBytes = sResponse.Content.Headers.ContentLength ?? 46 * 1024 * 1024;
                        using var contentStream = await sResponse.Content.ReadAsStreamAsync();
                        using var fileStream = new FileStream(tempZip, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

                        var buffer = new byte[16384];
                        long totalRead = 0;
                        int bytesRead;
                        var sw = Stopwatch.StartNew();

                        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                        {
                            await fileStream.WriteAsync(buffer, 0, bytesRead);
                            totalRead += bytesRead;

                            double pct = (double)totalRead / totalBytes * 100.0;
                            double speedMBs = (totalRead / 1024.0 / 1024.0) / Math.Max(0.1, sw.Elapsed.TotalSeconds);

                            ProgressBarDownload.Value = Math.Min(100, pct);
                            TxtTechnicalStatus.Text = $"{totalRead / 1024 / 1024} МБ / {totalBytes / 1024 / 1024} МБ ({speedMBs:F1} МБ/с) • {pct:F0}%";
                        }
                    }

                    TxtTechnicalStatus.Text = "Распаковка сборки...";
                    var mcDir = FindMinecraftDir();

                    using (var archive = ZipFile.OpenRead(tempZip))
                    {
                        foreach (var entry in archive.Entries)
                        {
                            if (string.IsNullOrEmpty(entry.Name)) continue;

                            string targetSub = "";
                            if (entry.FullName.Contains("mods/", StringComparison.OrdinalIgnoreCase)) targetSub = "mods";
                            else if (entry.FullName.Contains("shaderpacks/", StringComparison.OrdinalIgnoreCase)) targetSub = "shaderpacks";
                            else if (entry.FullName.Contains("resourcepacks/", StringComparison.OrdinalIgnoreCase)) targetSub = "resourcepacks";
                            else if (entry.FullName.Contains("config/", StringComparison.OrdinalIgnoreCase)) targetSub = "config";

                            if (!string.IsNullOrEmpty(targetSub))
                            {
                                var destDir = Path.Combine(mcDir, targetSub);
                                Directory.CreateDirectory(destDir);
                                var destFile = Path.Combine(destDir, entry.Name);
                                entry.ExtractToFile(destFile, true);
                            }
                        }
                    }

                    File.Delete(tempZip);
                    _config.CurrentVersion = tagName;
                    SaveConfig();
                }
            }
            finally
            {
                _quoteTimer.Stop();
                BrdProgress.Visibility = Visibility.Collapsed;
            }
        }

        // ==========================================
        // ROBUST FABRIC LAUNCHER (ZERO CMD LIMIT)
        // ==========================================
        private void LaunchMinecraft()
        {
            var mcDir = FindMinecraftDir();
            var javaw = FindJavaRuntime(mcDir);
            var nick = string.IsNullOrWhiteSpace(_config.Nickname) ? "Player" : _config.Nickname;

            var versionsDir = Path.Combine(mcDir, "versions");
            string verDir = Path.Combine(versionsDir, "fabric-loader-0.19.5-1.20.1");
            string verJsonPath = Path.Combine(verDir, "fabric-loader-0.19.5-1.20.1.json");

            if (!File.Exists(verJsonPath))
            {
                verDir = Path.Combine(versionsDir, "Fabric 1.20.1");
                verJsonPath = Path.Combine(verDir, "Fabric 1.20.1.json");
            }

            var cpList = new List<string>();
            var libsDir = Path.Combine(mcDir, "libraries");

            // Parse EXACT libraries from JSON (avoids dumping all 2000 jars into command line)
            if (File.Exists(verJsonPath))
            {
                try
                {
                    var jsonDoc = JsonDocument.Parse(File.ReadAllText(verJsonPath));
                    if (jsonDoc.RootElement.TryGetProperty("libraries", out var libsArr))
                    {
                        foreach (var lib in libsArr.EnumerateArray())
                        {
                            if (lib.TryGetProperty("name", out var nameProp))
                            {
                                var name = nameProp.GetString() ?? "";
                                var parts = name.Split(':');
                                if (parts.Length >= 3)
                                {
                                    var group = parts[0].Replace('.', Path.DirectorySeparatorChar);
                                    var artifact = parts[1];
                                    var ver = parts[2];
                                    var jarRel = Path.Combine(group, artifact, ver, $"{artifact}-{ver}.jar");
                                    var jarFull = Path.Combine(libsDir, jarRel);

                                    if (File.Exists(jarFull))
                                    {
                                        cpList.Add(jarFull);
                                    }
                                }
                            }
                        }
                    }
                }
                catch { }
            }

            // Fallback: If JSON couldn't be parsed, use common Fabric essentials
            if (cpList.Count == 0 && Directory.Exists(libsDir))
            {
                foreach (var jar in Directory.GetFiles(libsDir, "*.jar", SearchOption.TopDirectoryOnly))
                {
                    cpList.Add(jar);
                }
            }

            // Client JAR
            var base1201 = Path.Combine(versionsDir, "1.20.1", "1.20.1.jar");
            if (File.Exists(base1201))
            {
                cpList.Add(base1201);
            }
            else
            {
                var verJar = Path.Combine(verDir, $"{Path.GetFileName(verDir)}.jar");
                if (File.Exists(verJar)) cpList.Add(verJar);
            }

            var nativesDir = Path.Combine(verDir, "natives");
            if (!Directory.Exists(nativesDir)) nativesDir = Path.Combine(mcDir, "bin", "natives");

            var classPath = string.Join(";", cpList);
            var uuid = Guid.NewGuid().ToString("N");

            // Write arguments to an argument file (no BOM) to completely prevent Windows cmd length limits
            var argsFile = Path.Combine(Path.GetTempPath(), "aura_minecraft_args.txt");
            var sb = new StringBuilder();
            sb.AppendLine($"-Xmx{_config.RamMb}M");
            sb.AppendLine("-XX:+UnlockExperimentalVMOptions");
            sb.AppendLine("-XX:+UseG1GC");
            sb.AppendLine($"-Djava.library.path={nativesDir}");
            sb.AppendLine("-cp");
            sb.AppendLine(classPath);
            sb.AppendLine("net.fabricmc.loader.impl.launch.knot.KnotClient");
            sb.AppendLine("--version");
            sb.AppendLine("1.20.1");
            sb.AppendLine("--gameDir");
            sb.AppendLine(mcDir);
            sb.AppendLine("--assetsDir");
            sb.AppendLine(Path.Combine(mcDir, "assets"));
            sb.AppendLine("--assetIndex");
            sb.AppendLine("5");
            sb.AppendLine("--username");
            sb.AppendLine(nick);
            sb.AppendLine("--uuid");
            sb.AppendLine(uuid);
            sb.AppendLine("--accessToken");
            sb.AppendLine("0");

            File.WriteAllText(argsFile, sb.ToString(), new UTF8Encoding(false));

            var psi = new ProcessStartInfo
            {
                FileName = javaw,
                Arguments = $"\"@{argsFile}\"",
                WorkingDirectory = mcDir,
                UseShellExecute = false
            };

            Process.Start(psi);
        }

        private string FindMinecraftDir()
        {
            if (Directory.Exists(@"D:\.minecraft")) return @"D:\.minecraft";
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appData, ".minecraft");
        }

        private string FindJavaRuntime(string mcDir)
        {
            var gamma = Path.Combine(mcDir, "runtime", "java-runtime-gamma", "windows", "java-runtime-gamma", "bin", "javaw.exe");
            if (File.Exists(gamma)) return gamma;

            var delta = Path.Combine(mcDir, "runtime", "java-runtime-delta", "windows", "java-runtime-delta", "bin", "javaw.exe");
            if (File.Exists(delta)) return delta;

            var sysJava = @"C:\Program Files\Java\jre-1.8\bin\javaw.exe";
            if (File.Exists(sysJava)) return sysJava;

            return "javaw.exe";
        }
    }
}
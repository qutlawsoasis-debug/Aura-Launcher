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

    public class ModItemInfo
    {
        public string Title { get; set; } = "";
        public string Category { get; set; } = "";
        public string Description { get; set; } = "";
        public Color CategoryColor { get; set; } = Color.FromRgb(0x10, 0xB9, 0x81);
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
        private readonly List<ModItemInfo> _allMods = new();

        private readonly string[] _csQuotes = new[]
        {
            "Заряжаем пули...",
            "Снаряжаем деревенских стражников мечами...",
            "Калибруем шейдерные лучи...",
            "Выращиваем горные хребты Terralith...",
            "Смазываем петли анимированных дверей...",
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
            TxtDockNick.Text = nick;
            TxtWardrobeNick.Text = nick;

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

        // ==========================================
        // FLOATING SHEET MODALS (APPLE HIG)
        // ==========================================
        private void BtnOpenSkinSheet_Click(object sender, RoutedEventArgs e)
        {
            OpenSheet("Гардероб", SheetSkin);
        }

        private void BtnOpenModsSheet_Click(object sender, RoutedEventArgs e)
        {
            OpenSheet("Модификации (45)", SheetMods);
        }

        private void BtnOpenSettingsSheet_Click(object sender, RoutedEventArgs e)
        {
            OpenSheet("Настройки", SheetSettings);
        }

        private void OpenSheet(string title, FrameworkElement activeContent)
        {
            TxtSheetTitle.Text = title;
            SheetSkin.Visibility = Visibility.Collapsed;
            SheetMods.Visibility = Visibility.Collapsed;
            SheetSettings.Visibility = Visibility.Collapsed;
            activeContent.Visibility = Visibility.Visible;
            OverlayScrim.Visibility = Visibility.Visible;
        }

        private void BtnCloseSheet_Click(object sender, RoutedEventArgs e)
        {
            OverlayScrim.Visibility = Visibility.Collapsed;
        }

        private void OverlayScrim_Click(object sender, MouseButtonEventArgs e)
        {
            OverlayScrim.Visibility = Visibility.Collapsed;
        }

        private void SheetContainer_Click(object sender, MouseButtonEventArgs e)
        {
            e.Handled = true; // prevent scrim from closing when clicking inside
        }

        private void TxtNickname_TextChanged(object sender, TextChangedEventArgs e)
        {
            var nick = TxtSettingsNick.Text.Trim();
            if (string.IsNullOrEmpty(nick)) nick = "Player";
            _config.Nickname = nick;
            TxtDockNick.Text = nick;
            TxtWardrobeNick.Text = nick;
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
        // DRAG & DROP SKIN SUPPORT
        // ==========================================
        private void Window_DragOver(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && files[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    e.Effects = DragDropEffects.Copy;
                    e.Handled = true;
                    return;
                }
            }
            e.Effects = DragDropEffects.None;
            e.Handled = true;
        }

        private void Window_Drop(object sender, DragEventArgs e)
        {
            if (e.Data.GetDataPresent(DataFormats.FileDrop))
            {
                var files = (string[])e.Data.GetData(DataFormats.FileDrop);
                if (files != null && files.Length > 0 && files[0].EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                {
                    try
                    {
                        _config.SkinPath = files[0];
                        UpdateSkinModelPreview(_config.SkinPath);
                        SyncSkinToGame(_config.SkinPath);
                        SaveConfig();

                        OpenSheet("Гардероб", SheetSkin);
                    }
                    catch { }
                }
            }
        }

        // ==========================================
        // SKIN SYSTEM & FULL-BODY RENDERING
        // ==========================================
        private void UpdateSkinModelPreview(string? skinPath)
        {
            var (modelImg, miniFace, desc) = CreateModelAndMiniFace(skinPath);
            ImgBodyModel.Source = modelImg;
            ImgBodyReflection.Source = modelImg;
            ImgWardrobeModel.Source = modelImg;
            ImgDockFace.Source = miniFace;
            TxtWardrobeFormat.Text = desc;
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

                var mcDir = FindMinecraftDir();
                var nick = string.IsNullOrWhiteSpace(_config.Nickname) ? "Player" : _config.Nickname;
                var localSkinDir = Path.Combine(mcDir, "CustomSkinLoader", "LocalSkin", "skins");
                Directory.CreateDirectory(localSkinDir);
                var targetPath = Path.Combine(localSkinDir, $"{nick}.png");

                var stevePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steve.png");
                if (File.Exists(stevePath))
                {
                    File.Copy(stevePath, targetPath, true);
                }
                else
                {
                    try
                    {
                        var uri = new Uri("pack://application:,,,/steve.png");
                        var streamInfo = Application.GetResourceStream(uri);
                        if (streamInfo != null)
                        {
                            using var fs = File.Create(targetPath);
                            streamInfo.Stream.CopyTo(fs);
                        }
                    }
                    catch { }
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

        private (ImageSource FullModel, ImageSource MiniFace, string Description) CreateModelAndMiniFace(string? skinPath)
        {
            BitmapSource? sourceBmp = null;
            string desc = "Классический Стив (64×64)";

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
                    desc = $"{Path.GetFileName(skinPath)} ({bmp.PixelWidth}×{bmp.PixelHeight})";
                }
                catch { }
            }

            if (sourceBmp == null)
            {
                try
                {
                    var uri = new Uri("pack://application:,,,/steve.png");
                    var streamInfo = Application.GetResourceStream(uri);
                    if (streamInfo != null)
                    {
                        using var s = streamInfo.Stream;
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.StreamSource = s;
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        sourceBmp = bmp;
                    }
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
                var blank = new WriteableBitmap(160, 320, 96, 96, PixelFormats.Bgra32, null);
                var blankMini = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgra32, null);
                return (blank, blankMini, desc);
            }

            var conv = new FormatConvertedBitmap(sourceBmp, PixelFormats.Bgra32, null, 0);
            int skinW = conv.PixelWidth;
            int skinH = conv.PixelHeight;

            uint[] skinPixels = new uint[skinW * skinH];
            conv.CopyPixels(skinPixels, skinW * 4, 0);

            // 1. FULL BODY MODEL (16x32 -> 160x320)
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

            // Upscale 10x to 160x320
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

            var wbFull = new WriteableBitmap(160, 320, 96, 96, PixelFormats.Bgra32, null);
            wbFull.WritePixels(new Int32Rect(0, 0, 160, 320), scaled, 160 * 4, 0);
            wbFull.Freeze();

            // 2. MINI HEAD FACE (8x8 -> 32x32)
            uint[] face8x8 = new uint[8 * 8];
            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int baseSrc = (8 + y) * skinW + (8 + x);
                    uint px = skinPixels[baseSrc];

                    if (skinW >= 48)
                    {
                        int hatSrc = (8 + y) * skinW + (40 + x);
                        uint hatPx = skinPixels[hatSrc];
                        byte hatA = (byte)((hatPx >> 24) & 0xFF);
                        if (hatA == 255)
                        {
                            px = hatPx;
                        }
                        else if (hatA > 0)
                        {
                            byte da = (byte)((px >> 24) & 0xFF);
                            byte dr = (byte)((px >> 16) & 0xFF);
                            byte dg = (byte)((px >> 8) & 0xFF);
                            byte db = (byte)(px & 0xFF);

                            byte sr = (byte)((hatPx >> 16) & 0xFF);
                            byte sg = (byte)((hatPx >> 8) & 0xFF);
                            byte sb = (byte)(hatPx & 0xFF);

                            int inv = 255 - hatA;
                            int oa = hatA + (da * inv) / 255;
                            int or = (sr * hatA + dr * inv) / 255;
                            int og = (sg * hatA + dg * inv) / 255;
                            int ob = (sb * hatA + db * inv) / 255;
                            px = (uint)((oa << 24) | (or << 16) | (og << 8) | ob);
                        }
                    }

                    face8x8[y * 8 + x] = px;
                }
            }

            uint[] scaled32 = new uint[32 * 32];
            for (int y = 0; y < 32; y++)
            {
                int sy = y / 4;
                for (int x = 0; x < 32; x++)
                {
                    scaled32[y * 32 + x] = face8x8[sy * 8 + (x / 4)];
                }
            }

            var wbMini = new WriteableBitmap(32, 32, 96, 96, PixelFormats.Bgra32, null);
            wbMini.WritePixels(new Int32Rect(0, 0, 32, 32), scaled32, 32 * 4, 0);
            wbMini.Freeze();

            return (wbFull, wbMini, desc);
        }

        // ==========================================
        // MODS CATALOG & REAL-TIME SEARCH
        // ==========================================
        private void LoadModsList()
        {
            _allMods.Clear();
            var mcDir = FindMinecraftDir();
            var modsDir = Path.Combine(mcDir, "mods");

            var modDetails = new Dictionary<string, (string Title, string Category, string Description, Color TagColor)>(StringComparer.OrdinalIgnoreCase)
            {
                ["sodium-fabric"] = ("Sodium", "Оптимизация", "Революционный графический движок, увеличивающий FPS в разы.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["iris"] = ("Iris Shaders", "Графика", "Современный движок шейдеров с поддержкой шейдеров OptiFine.", Color.FromRgb(0x06, 0xB6, 0xD4)),
                ["lithium-fabric"] = ("Lithium", "Оптимизация", "Комплексная оптимизация игровой физики, мобов и чанков.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["ferritecore"] = ("FerriteCore", "Память", "Снижает потребление оперативной памяти Minecraft до 50%.", Color.FromRgb(0x3B, 0x82, 0xF6)),
                ["modernfix"] = ("ModernFix", "Оптимизация", "Устраняет утечки памяти и ускоряет время запуска игры.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["indium"] = ("Indium", "Графика", "Мост совместимости между Sodium и модами с кастомным рендером.", Color.FromRgb(0x06, 0xB6, 0xD4)),
                ["immediatelyfast"] = ("ImmediatelyFast", "Оптимизация", "Оптимизация отрисовки интерфейса, текста и частиц.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["entityculling"] = ("Entity Culling", "Оптимизация", "Скрывает невидимых за стенами мобов для экономии FPS.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["krypton"] = ("Krypton", "Сеть", "Оптимизация сетевого стека для плавного мультиплеера.", Color.FromRgb(0x63, 0x66, 0xF1)),
                ["terralith"] = ("Terralith", "Мир", "Глобальная генерация 100+ новых ванильных биомов и пещер.", Color.FromRgb(0x8B, 0x5C, 0xF6)),
                ["tectonic"] = ("Tectonic", "Мир", "Массивные горные гряды, подземные реки и рельеф.", Color.FromRgb(0x8B, 0x5C, 0xF6)),
                ["ctov"] = ("CTOV (Villages)", "Деревни", "Капитальный редизайн деревень под каждый биом мира.", Color.FromRgb(0xF5, 0x9E, 0x0B)),
                ["guardvillagers"] = ("Guard Villagers", "Геймплей", "Жители-стражники с мечами и луками, защищающие деревни.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["travelersbackpack"] = ("Traveler's Backpack", "Снаряжение", "Удобные рюкзаки со спальниками и баками для жидкостей.", Color.FromRgb(0xEC, 0x48, 0x99)),
                ["waystones"] = ("Waystones", "Путешествия", "Путеводные камни для телепортации между поселениями.", Color.FromRgb(0x3B, 0x82, 0xF6)),
                ["farmersdelight"] = ("Farmer's Delight", "Кулинария", "Расширенная кулинария, готовка в котлах, сковороды и блюда.", Color.FromRgb(0xF5, 0x9E, 0x0B)),
                ["treechop"] = ("TreeChop", "Геймплей", "Реалистичная рубка деревьев с динамической анимацией ствола.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["carryon"] = ("Carry On", "Геймплей", "Возможность переносить сундуки, животных и мелкие блоки в руках.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["artifacts"] = ("Artifacts", "Снаряжение", "Редкие ценные реликвии и аксессуары в сундуках подземелий.", Color.FromRgb(0xEC, 0x48, 0x99)),
                ["comforts"] = ("Comforts", "Геймплей", "Спальные мешки и гамаки для отдыха без смены точки спавна.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["appleskin"] = ("AppleSkin", "Интерфейс", "Показ насыщения и восстанавливаемого здоровья еды.", Color.FromRgb(0x06, 0xB6, 0xD4)),
                ["jade"] = ("Jade (WAILA)", "Интерфейс", "Информативная плашка с описанием блока или моба под прицелом.", Color.FromRgb(0x06, 0xB6, 0xD4)),
                ["zoomify"] = ("Zoomify", "Управление", "Плавный зум с кинематографическим приближением на C.", Color.FromRgb(0x63, 0x66, 0xF1)),
                ["modmenu"] = ("Mod Menu", "Интерфейс", "Главное меню модов и настроек конфигурации на ~ (тильду).", Color.FromRgb(0x06, 0xB6, 0xD4)),
                ["controlling"] = ("Controlling", "Интерфейс", "Поиск и удобное устранение конфликтов клавиш управления.", Color.FromRgb(0x06, 0xB6, 0xD4)),
                ["customskinloader"] = ("CustomSkinLoader", "Скины", "Отображение HD и кастомных скинов без лицензии.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["sound-physics-remastered"] = ("Sound Physics", "Звук", "Реалистичная акустика, эхо в пещерах и реверберация.", Color.FromRgb(0x8B, 0x5C, 0xF6)),
                ["dungeons-and-taverns"] = ("Dungeons & Taverns", "Структуры", "Атмосферные таверны и интересные данжи для исследования.", Color.FromRgb(0xF5, 0x9E, 0x0B)),
                ["clumps"] = ("Clumps", "Оптимизация", "Объединение сфер опыта в один сгусток для устранения лагов.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["animated-doors"] = ("Animated Doors", "Анимация", "Плавная механика открытия дверей.", Color.FromRgb(0xF4, 0x3F, 0x5E)),
                ["short-grass"] = ("Short Grass", "Графика", "Аккуратная низкая трава для чистого обзора биомов.", Color.FromRgb(0x06, 0xB6, 0xD4)),
                ["doubledoors"] = ("Double Doors", "Геймплей", "Одновременное открытие двойных дверей одним кликом.", Color.FromRgb(0x10, 0xB9, 0x81)),
                ["fallingleaves"] = ("Falling Leaves", "Атмосфера", "Опадающие с деревьев разноцветные листья.", Color.FromRgb(0xF5, 0x9E, 0x0B)),
                ["visuality"] = ("Visuality", "Атмосфера", "Красивые частицы искр, капель и ударов.", Color.FromRgb(0x06, 0xB6, 0xD4)),
                ["eating-animation"] = ("Eating Animation", "Анимация", "Анимация откусывания еды в руке.", Color.FromRgb(0xF4, 0x3F, 0x5E)),
                ["notenoughanimations"] = ("Not Enough Animations", "Анимация", "Плавные движения персонажа от третьего лица.", Color.FromRgb(0xF4, 0x3F, 0x5E))
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
                    _allMods.Add(new ModItemInfo
                    {
                        Title = info.Title,
                        Category = info.Category,
                        Description = info.Description,
                        CategoryColor = info.TagColor
                    });
                }
                else if (matchedKey == "")
                {
                    var cleanTitle = Path.GetFileNameWithoutExtension(fn);
                    _allMods.Add(new ModItemInfo
                    {
                        Title = cleanTitle,
                        Category = "Мод",
                        Description = fn,
                        CategoryColor = Color.FromRgb(0x64, 0x74, 0x8B)
                    });
                }
            }

            if (_allMods.Count == 0)
            {
                foreach (var kvp in modDetails)
                {
                    _allMods.Add(new ModItemInfo
                    {
                        Title = kvp.Value.Title,
                        Category = kvp.Value.Category,
                        Description = kvp.Value.Description,
                        CategoryColor = kvp.Value.TagColor
                    });
                }
            }

            RenderFilteredMods("");
        }

        private void TxtSearchMods_TextChanged(object sender, TextChangedEventArgs e)
        {
            RenderFilteredMods(TxtSearchMods.Text.Trim());
        }

        private void RenderFilteredMods(string query)
        {
            PanelModsList.Children.Clear();
            var filtered = string.IsNullOrEmpty(query)
                ? _allMods
                : _allMods.FindAll(m =>
                    m.Title.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    m.Category.Contains(query, StringComparison.OrdinalIgnoreCase) ||
                    m.Description.Contains(query, StringComparison.OrdinalIgnoreCase));

            TxtModsCount.Text = $"{filtered.Count} модов";
            TxtNavModsLabel.Text = $"Модификации ({_allMods.Count})";

            foreach (var mod in filtered)
            {
                PanelModsList.Children.Add(CreateModCard(mod));
            }
        }

        private Border CreateModCard(ModItemInfo mod)
        {
            var brd = new Border
            {
                Background = new SolidColorBrush(Color.FromRgb(0x0E, 0x12, 0x1D)),
                BorderBrush = new SolidColorBrush(Color.FromRgb(0x1C, 0x24, 0x36)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(9),
                Margin = new Thickness(0, 0, 0, 8),
                Padding = new Thickness(14, 10, 14, 10)
            };

            var grid = new Grid();
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            var sp = new StackPanel();
            var tbTitle = new TextBlock
            {
                Text = mod.Title,
                Foreground = Brushes.White,
                FontSize = 13,
                FontWeight = FontWeights.SemiBold
            };
            var tbDesc = new TextBlock
            {
                Text = mod.Description,
                Foreground = new SolidColorBrush(Color.FromRgb(0x8E, 0x9C, 0xAE)),
                FontSize = 11,
                Margin = new Thickness(0, 2, 0, 0),
                TextWrapping = TextWrapping.Wrap
            };
            sp.Children.Add(tbTitle);
            sp.Children.Add(tbDesc);

            var badge = new Border
            {
                Background = new SolidColorBrush(Color.FromArgb(28, mod.CategoryColor.R, mod.CategoryColor.G, mod.CategoryColor.B)),
                BorderBrush = new SolidColorBrush(Color.FromArgb(70, mod.CategoryColor.R, mod.CategoryColor.G, mod.CategoryColor.B)),
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(6),
                Padding = new Thickness(8, 3, 8, 3),
                VerticalAlignment = VerticalAlignment.Center
            };
            var tbCat = new TextBlock
            {
                Text = mod.Category,
                Foreground = new SolidColorBrush(mod.CategoryColor),
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

                TxtTechnicalStatus.Text = "Все файлы актуальны";
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

            if (cpList.Count == 0 && Directory.Exists(libsDir))
            {
                foreach (var jar in Directory.GetFiles(libsDir, "*.jar", SearchOption.TopDirectoryOnly))
                {
                    cpList.Add(jar);
                }
            }

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
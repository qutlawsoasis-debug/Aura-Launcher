using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using System.Windows;
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
            "Полируем линзы зума...",
            "Сверяем путеводные камни...",
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

        private void MainWindow_Loaded(object sender, RoutedEventArgs e)
        {
            ApplyWindows11MicaOrAcrylic();
            LoadConfig();
            CheckForUpdatesAsync();
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

            TxtNickname.Text = string.IsNullOrWhiteSpace(_config.Nickname) ? "Player" : _config.Nickname;

            switch (_config.RamMb)
            {
                case 4096: RbRam4.IsChecked = true; break;
                case 8192: RbRam8.IsChecked = true; break;
                case 12288: RbRam12.IsChecked = true; break;
                default: RbRam6.IsChecked = true; break;
            }

            if (!string.IsNullOrEmpty(_config.SkinPath) && File.Exists(_config.SkinPath))
            {
                RenderSkinFace(_config.SkinPath);
            }
            else
            {
                RenderDefaultFace();
            }
        }

        private void SaveConfig()
        {
            try
            {
                _config.Nickname = TxtNickname.Text.Trim();
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

        private void TxtNickname_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            _config.Nickname = TxtNickname.Text.Trim();
        }

        private void RamRadio_Checked(object sender, RoutedEventArgs e)
        {
            if (sender is System.Windows.Controls.RadioButton rb && rb.Tag != null)
            {
                if (int.TryParse(rb.Tag.ToString(), out int ram))
                {
                    _config.RamMb = ram;
                }
            }
        }

        // ==========================================
        // SKIN SYSTEM
        // ==========================================
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
                    RenderSkinFace(_config.SkinPath);
                    SyncSkinToGame(_config.SkinPath);
                    SaveConfig();
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"Ошибка при загрузке скина: {ex.Message}", "AURA", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
        }

        private void RenderSkinFace(string skinFilePath)
        {
            try
            {
                var bitmap = new BitmapImage();
                bitmap.BeginInit();
                bitmap.UriSource = new Uri(skinFilePath);
                bitmap.CacheOption = BitmapCacheOption.OnLoad;
                bitmap.EndInit();

                // Crop Head (8, 8, 8, 8) and Hat (40, 8, 8, 8)
                var headRect = new Int32Rect(8, 8, 8, 8);
                var hatRect = new Int32Rect(40, 8, 8, 8);

                var headCropped = new CroppedBitmap(bitmap, headRect);
                CroppedBitmap? hatCropped = null;

                if (bitmap.PixelWidth >= 48)
                {
                    try { hatCropped = new CroppedBitmap(bitmap, hatRect); } catch { }
                }

                // Render into visual
                var dv = new DrawingVisual();
                using (var dc = dv.RenderOpen())
                {
                    dc.DrawImage(headCropped, new Rect(0, 0, 80, 80));
                    if (hatCropped != null)
                    {
                        dc.DrawImage(hatCropped, new Rect(0, 0, 80, 80));
                    }
                }

                var rtb = new RenderTargetBitmap(80, 80, 96, 96, PixelFormats.Pbgra32);
                rtb.Render(dv);
                ImgSkinFace.Source = rtb;
            }
            catch
            {
                RenderDefaultFace();
            }
        }

        private void RenderDefaultFace()
        {
            // Simple minimalist avatar placeholder
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                dc.DrawRectangle(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#1E293B")), null, new Rect(0, 0, 80, 80));
                dc.DrawEllipse(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")), null, new Point(40, 34), 16, 16);
                dc.DrawEllipse(new SolidColorBrush((Color)ColorConverter.ConvertFromString("#10B981")), null, new Point(40, 75), 26, 20);
            }
            var rtb = new RenderTargetBitmap(80, 80, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            ImgSkinFace.Source = rtb;
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

        // ==========================================
        // GITHUB UPDATE SYSTEM
        // ==========================================
        private async Task CheckForUpdatesAsync()
        {
            try
            {
                TxtStatusBadge.Text = "Проверка обновлений...";
                TxtTechnicalStatus.Text = "Связь с репозиторием GitHub...";

                // Try fetching latest release
                var url = $"https://api.github.com/repos/{_config.GitHubRepo}/releases/latest";
                var response = await _http.GetAsync(url);

                if (response.IsSuccessStatusCode)
                {
                    var json = await response.Content.ReadAsStringAsync();
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;
                    var tagName = root.GetProperty("tag_name").GetString() ?? "";

                    if (tagName != _config.CurrentVersion)
                    {
                        TxtStatusBadge.Text = $"Доступно обновление ({tagName})";
                        TxtTechnicalStatus.Text = "Нажмите «ИГРАТЬ» для авто-обновления сборки";
                        return;
                    }
                }

                TxtStatusBadge.Text = "Готов к игре";
                TxtTechnicalStatus.Text = "Все моды и шейдеры актуальны";
            }
            catch
            {
                TxtStatusBadge.Text = "Локальный режим";
                TxtTechnicalStatus.Text = "Готов к запуску сборки";
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

            try
            {
                // Check if update download is required
                bool updateNeeded = await CheckIfUpdateNeededAsync();
                if (updateNeeded)
                {
                    await PerformUpdateDownloadAsync();
                }

                TxtTechnicalStatus.Text = "Запуск игры...";
                TxtWittyQuote.Text = "Увидимся в кубическом мире!";

                LaunchMinecraft();
                await Task.Delay(1500);
                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка запуска: {ex.Message}", "AURA", MessageBoxButton.OK, MessageBoxImage.Error);
                BtnPlay.IsEnabled = true;
                ProgressBarDownload.Visibility = Visibility.Collapsed;
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
            ProgressBarDownload.Visibility = Visibility.Visible;
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

                // Find zip asset
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
                        var totalBytes = sResponse.Content.Headers.ContentLength ?? 45 * 1024 * 1024;
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
                            TxtTechnicalStatus.Text = $"Загрузка: {totalRead / 1024 / 1024} МБ / {totalBytes / 1024 / 1024} МБ ({speedMBs:F1} МБ/с) • {pct:F0}%";
                        }
                    }

                    // Unpack
                    TxtTechnicalStatus.Text = "Распаковка и установка компонентов...";
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
                ProgressBarDownload.Visibility = Visibility.Collapsed;
            }
        }

        private void LaunchMinecraft()
        {
            var mcDir = FindMinecraftDir();
            var javaw = FindJavaRuntime(mcDir);
            var nick = string.IsNullOrWhiteSpace(_config.Nickname) ? "Player" : _config.Nickname;

            // Search for fabric loader json
            var versionsDir = Path.Combine(mcDir, "versions");
            string verDir = Path.Combine(versionsDir, "fabric-loader-0.19.5-1.20.1");
            if (!Directory.Exists(verDir))
            {
                verDir = Path.Combine(versionsDir, "Fabric 1.20.1");
            }

            // Build classpath
            var cpList = new List<string>();
            var libsDir = Path.Combine(mcDir, "libraries");

            if (Directory.Exists(libsDir))
            {
                foreach (var jar in Directory.GetFiles(libsDir, "*.jar", SearchOption.AllDirectories))
                {
                    cpList.Add(jar);
                }
            }

            var verJar = Path.Combine(verDir, $"{Path.GetFileName(verDir)}.jar");
            if (!File.Exists(verJar))
            {
                var base1201 = Path.Combine(versionsDir, "1.20.1", "1.20.1.jar");
                if (File.Exists(base1201)) verJar = base1201;
            }

            if (File.Exists(verJar)) cpList.Add(verJar);

            var nativesDir = Path.Combine(verDir, "natives");
            if (!Directory.Exists(nativesDir)) nativesDir = Path.Combine(mcDir, "bin", "natives");

            var classPath = string.Join(";", cpList);
            var uuid = Guid.NewGuid().ToString().Replace("-", "");

            var psi = new ProcessStartInfo
            {
                FileName = javaw,
                Arguments = $"-Xmx{_config.RamMb}M -XX:+UnlockExperimentalVMOptions -XX:+UseG1GC -Djava.library.path=\"{nativesDir}\" -cp \"{classPath}\" net.fabricmc.loader.impl.launch.knot.KnotClient --username {nick} --version 1.20.1 --gameDir \"{mcDir}\" --assetsDir \"{Path.Combine(mcDir, "assets")}\" --assetIndex 5 --uuid {uuid} --accessToken 0",
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
using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Реализация сервиса управления скинами и подготовки 2D-превью.
/// </summary>
public class SkinService : ISkinService
{
    private readonly IConfigService? _configService;
    private readonly string? _customProfileDir;

    public SkinService() : this(null, null) { }

    public SkinService(IConfigService? configService) : this(configService, null) { }

    public SkinService(IConfigService? configService, string? customProfileDir)
    {
        _configService = configService;
        _customProfileDir = customProfileDir;
    }

    public string GetProfileDataDir()
    {
        if (!string.IsNullOrWhiteSpace(_customProfileDir))
            return Path.GetFullPath(_customProfileDir);

        if (_configService != null)
        {
            var dir = _configService.ProfileDataDir;
            if (!string.IsNullOrWhiteSpace(dir))
                return Path.GetFullPath(dir);
        }

        string? envProfile = Environment.GetEnvironmentVariable("AURA_PROFILE_DIR");
        if (!string.IsNullOrWhiteSpace(envProfile))
        {
            var appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.GetFullPath(Path.IsPathRooted(envProfile)
                ? envProfile
                : Path.Combine(appData, ".aura", "profiles", envProfile));
        }

        return Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura"));
    }
    public SkinValidationResult ValidateSkinBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return new SkinValidationResult(false, "Массив байтов скина пуст.");
        }

        if (bytes.Length > 2 * 1024 * 1024)
        {
            return new SkinValidationResult(false, "Размер файла скина превышает допустимый лимит (2 МБ).");
        }

        try
        {
            using var ms = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0)
            {
                return new SkinValidationResult(false, "Файл не содержит графических кадров или поврежден.");
            }

            var frame = decoder.Frames[0];
            int width = frame.PixelWidth;
            int height = frame.PixelHeight;

            if (width == 64 && height == 64)
            {
                return new SkinValidationResult(true, null, 64, 64);
            }

            if (width == 64 && height == 32)
            {
                return new SkinValidationResult(true, null, 64, 32);
            }

            return new SkinValidationResult(false, $"Неподдерживаемый размер скина ({width}×{height}). Требуется 64×64 или 64×32.", width, height);
        }
        catch (Exception ex)
        {
            return new SkinValidationResult(false, $"Файл поврежден или не является допустимым PNG: {ex.Message}");
        }
    }

    public bool TryResolveWardrobeItemPath(WardrobeItem? item, out string resolvedFullPath)
    {
        resolvedFullPath = string.Empty;
        if (item == null) return false;

        string subFolder = string.Equals(item.Type, "cape", StringComparison.OrdinalIgnoreCase) ? "capes" : "skins";
        string profileDir = GetProfileDataDir();
        string managedDir = Path.GetFullPath(Path.Combine(profileDir, subFolder));

        // 1. Полноразмерный SHA-256 хеш (64 hex-символа)
        if (!string.IsNullOrWhiteSpace(item.Hash) && item.Hash.Length == 64 && Regex.IsMatch(item.Hash, "^[0-9a-fA-F]{64}$"))
        {
            string candidate = Path.GetFullPath(Path.Combine(managedDir, $"{item.Hash.ToLowerInvariant()}.png"));
            if (candidate.StartsWith(managedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
            {
                resolvedFullPath = candidate;
                return true;
            }
        }

        // 2. По Id формата type_sha256 (64 hex-символа)
        if (!string.IsNullOrWhiteSpace(item.Id))
        {
            var parts = item.Id.Split('_', 2);
            if (parts.Length == 2 && parts[1].Length == 64 && Regex.IsMatch(parts[1], "^[0-9a-fA-F]{64}$"))
            {
                string candidate = Path.GetFullPath(Path.Combine(managedDir, $"{parts[1].ToLowerInvariant()}.png"));
                if (candidate.StartsWith(managedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                {
                    resolvedFullPath = candidate;
                    return true;
                }
            }
        }

        // 3. Fallback: только безопасное имя файла из RelativePath (строго внутри managedDir, без .. и без рутовых путей)
        if (!string.IsNullOrWhiteSpace(item.RelativePath))
        {
            string fileName = Path.GetFileName(item.RelativePath);
            if (!string.IsNullOrWhiteSpace(fileName) && fileName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
            {
                string candidate = Path.GetFullPath(Path.Combine(managedDir, fileName));
                if (candidate.StartsWith(managedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase) && File.Exists(candidate))
                {
                    resolvedFullPath = candidate;
                    return true;
                }
            }
        }

        return false;
    }

    public SkinValidationResult ValidateSkinFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new SkinValidationResult(false, "Путь к файлу не указан.");
        }

        if (!File.Exists(filePath))
        {
            return new SkinValidationResult(false, "Выбранный файл скина не существует.");
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                return new SkinValidationResult(false, "Файл поврежден (0 байт).");
            }

            int width = 0;
            int height = 0;

            using (var stream = File.OpenRead(filePath))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count == 0)
                {
                    return new SkinValidationResult(false, "Файл не содержит графических кадров или поврежден.");
                }

                var frame = decoder.Frames[0];
                width = frame.PixelWidth;
                height = frame.PixelHeight;
            }

            if (width == 64 && height == 64)
            {
                return new SkinValidationResult(true, null, 64, 64);
            }

            if (width == 64 && height == 32)
            {
                return new SkinValidationResult(true, null, 64, 32);
            }

            return new SkinValidationResult(false, $"Неподдерживаемый размер скина ({width}×{height}). Требуется 64×64 или 64×32.", width, height);
        }
        catch (Exception ex)
        {
            return new SkinValidationResult(false, $"Файл поврежден или не является допустимым PNG: {ex.Message}");
        }
    }

    public ImageSource LoadSkinImage(string? skinPath)
    {
        BitmapSource? sourceBmp = null;

        if (!string.IsNullOrWhiteSpace(skinPath) && File.Exists(skinPath))
        {
            try
            {
                var validation = ValidateSkinFile(skinPath);
                if (validation.IsValid)
                {
                    var bmp = new BitmapImage();
                    bmp.BeginInit();
                    bmp.UriSource = new Uri(skinPath, UriKind.Absolute);
                    bmp.CacheOption = BitmapCacheOption.OnLoad;
                    bmp.EndInit();
                    if (bmp.CanFreeze) bmp.Freeze();
                    sourceBmp = bmp;
                }
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка загрузки скина: {ex.Message}");
            }
        }

        if (sourceBmp == null)
        {
            sourceBmp = LoadDefaultSteveBitmap();
        }

        // Приведение скина к стандарту 64x64 (если загружен классический 64x32)
        if (sourceBmp.PixelWidth == 64 && sourceBmp.PixelHeight == 32)
        {
            sourceBmp = Convert64x32To64x64(sourceBmp);
        }

        if (sourceBmp.CanFreeze && !sourceBmp.IsFrozen)
        {
            sourceBmp.Freeze();
        }

        return sourceBmp;
    }

    /// <summary>
    /// Извлечение аватара головы игрока: базовая голова (8,8,8,8) + слой шляпы/шлема (40,8,8,8).
    /// </summary>
    public ImageSource ExtractHeadAvatar(string? skinPath)
    {
        try
        {
            var skinBmp = LoadSkinImage(skinPath) as BitmapSource;
            if (skinBmp == null || skinBmp.PixelWidth < 16 || skinBmp.PixelHeight < 16)
            {
                return LoadDefaultSteveHead();
            }

            return ExtractHeadAvatarDirect(skinBmp);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка извлечения аватара головы: {ex.Message}");
            return LoadDefaultSteveHead();
        }
    }

    /// <summary>
    /// Полное плоское 2D-превью (вид спереди: голова, торс, руки, ноги + оверлей).
    /// </summary>
    public ImageSource ExtractFrontSkinPreview(string? skinPath)
    {
        try
        {
            var skinBmp = LoadSkinImage(skinPath) as BitmapSource;
            if (skinBmp == null) skinBmp = LoadDefaultSteveBitmap();

            bool isSlim = DetectIsSlim(skinBmp);
            int armW = isSlim ? 3 : 4;
            double rightArmX = isSlim ? 1 : 0;
            double leftArmX = 12;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 1. Голова (Head): Базовый (8,8, 8,8) -> (4,0), Оверлей (40,8, 8,8) -> (4,0)
                DrawSubRect(dc, skinBmp, new Int32Rect(8, 8, 8, 8), new Rect(4, 0, 8, 8));
                DrawSubRect(dc, skinBmp, new Int32Rect(40, 8, 8, 8), new Rect(4, 0, 8, 8));

                // 2. Торс (Body): Базовый (20,20, 8,12) -> (4,8), Оверлей (20,36, 8,12) -> (4,8)
                DrawSubRect(dc, skinBmp, new Int32Rect(20, 20, 8, 12), new Rect(4, 8, 8, 12));
                DrawSubRect(dc, skinBmp, new Int32Rect(20, 36, 8, 12), new Rect(4, 8, 8, 12));

                // 3. Правая рука (Right Arm): Базовый (44,20, armW,12) -> (rightArmX,8), Оверлей (44,36, armW,12) -> (rightArmX,8)
                DrawSubRect(dc, skinBmp, new Int32Rect(44, 20, armW, 12), new Rect(rightArmX, 8, armW, 12));
                DrawSubRect(dc, skinBmp, new Int32Rect(44, 36, armW, 12), new Rect(rightArmX, 8, armW, 12));

                // 4. Левая рука (Left Arm): Базовый (36,52, armW,12) -> (12,8), Оверлей (52,52, armW,12) -> (12,8)
                DrawSubRect(dc, skinBmp, new Int32Rect(36, 52, armW, 12), new Rect(leftArmX, 8, armW, 12));
                DrawSubRect(dc, skinBmp, new Int32Rect(52, 52, armW, 12), new Rect(leftArmX, 8, armW, 12));

                // 5. Правая нога (Right Leg): Базовый (4,20, 4,12) -> (4,20), Оверлей (4,36, 4,12) -> (4,20)
                DrawSubRect(dc, skinBmp, new Int32Rect(4, 20, 4, 12), new Rect(4, 20, 4, 12));
                DrawSubRect(dc, skinBmp, new Int32Rect(4, 36, 4, 12), new Rect(4, 20, 4, 12));

                // 6. Левая нога (Left Leg): Базовый (20,52, 4,12) -> (8,20), Оверлей (4,52, 4,12) -> (8,20)
                DrawSubRect(dc, skinBmp, new Int32Rect(20, 52, 4, 12), new Rect(8, 20, 4, 12));
                DrawSubRect(dc, skinBmp, new Int32Rect(4, 52, 4, 12), new Rect(8, 20, 4, 12));
            }

            var rtb = new RenderTargetBitmap(16, 32, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            if (rtb.CanFreeze) rtb.Freeze();
            return rtb;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка генерации превью (вид спереди): {ex.Message}");
            var fallback = new WriteableBitmap(16, 32, 96, 96, PixelFormats.Bgra32, null);
            if (fallback.CanFreeze) fallback.Freeze();
            return fallback;
        }
    }

    /// <summary>
    /// Полное плоское 2D-превью (вид сзади: голова, торс, руки, ноги + оверлей).
    /// </summary>
    public ImageSource ExtractBackSkinPreview(string? skinPath)
    {
        try
        {
            var skinBmp = LoadSkinImage(skinPath) as BitmapSource;
            if (skinBmp == null) skinBmp = LoadDefaultSteveBitmap();

            bool isSlim = DetectIsSlim(skinBmp);
            int armW = isSlim ? 3 : 4;
            double leftArmX = isSlim ? 1 : 0;
            double rightArmX = 12;

            int leftArmBackX = isSlim ? 43 : 44;
            int leftArmSleeveX = isSlim ? 59 : 60;
            int rightArmBackX = isSlim ? 51 : 52;
            int rightArmSleeveX = isSlim ? 51 : 52;

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 1. Голова сзади: Базовый (24,8, 8,8) -> (4,0), Оверлей (56,8, 8,8) -> (4,0)
                DrawSubRect(dc, skinBmp, new Int32Rect(24, 8, 8, 8), new Rect(4, 0, 8, 8));
                DrawSubRect(dc, skinBmp, new Int32Rect(56, 8, 8, 8), new Rect(4, 0, 8, 8));

                // 2. Спина (Body Back): Базовый (32,20, 8,12) -> (4,8), Оверлей (32,36, 8,12) -> (4,8)
                DrawSubRect(dc, skinBmp, new Int32Rect(32, 20, 8, 12), new Rect(4, 8, 8, 12));
                DrawSubRect(dc, skinBmp, new Int32Rect(32, 36, 8, 12), new Rect(4, 8, 8, 12));

                // 3. Левая рука сзади (слева от зрителя): Базовый (leftArmBackX,52, armW,12) -> (leftArmX,8), Оверлей (leftArmSleeveX,52, armW,12) -> (leftArmX,8)
                DrawSubRect(dc, skinBmp, new Int32Rect(leftArmBackX, 52, armW, 12), new Rect(leftArmX, 8, armW, 12));
                DrawSubRect(dc, skinBmp, new Int32Rect(leftArmSleeveX, 52, armW, 12), new Rect(leftArmX, 8, armW, 12));

                // 4. Правая рука сзади (справа от зрителя): Базовый (rightArmBackX,20, armW,12) -> (rightArmX,8), Оверлей (rightArmSleeveX,36, armW,12) -> (rightArmX,8)
                DrawSubRect(dc, skinBmp, new Int32Rect(rightArmBackX, 20, armW, 12), new Rect(rightArmX, 8, armW, 12));
                DrawSubRect(dc, skinBmp, new Int32Rect(rightArmSleeveX, 36, armW, 12), new Rect(rightArmX, 8, armW, 12));

                // 5. Левая нога сзади (слева от зрителя): Базовый (28,52, 4,12) -> (4,20), Оверлей (12,52, 4,12) -> (4,20)
                DrawSubRect(dc, skinBmp, new Int32Rect(28, 52, 4, 12), new Rect(4, 20, 4, 12));
                DrawSubRect(dc, skinBmp, new Int32Rect(12, 52, 4, 12), new Rect(4, 20, 4, 12));

                // 6. Правая нога сзади (справа от зрителя): Базовый (12,20, 4,12) -> (8,20), Оверлей (12,36, 4,12) -> (8,20)
                DrawSubRect(dc, skinBmp, new Int32Rect(12, 20, 4, 12), new Rect(8, 20, 4, 12));
                DrawSubRect(dc, skinBmp, new Int32Rect(12, 36, 4, 12), new Rect(8, 20, 4, 12));
            }

            var rtb = new RenderTargetBitmap(16, 32, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            if (rtb.CanFreeze) rtb.Freeze();
            return rtb;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка генерации превью (вид сзади): {ex.Message}");
            var fallback = new WriteableBitmap(16, 32, 96, 96, PixelFormats.Bgra32, null);
            if (fallback.CanFreeze) fallback.Freeze();
            return fallback;
        }
    }

    private static bool DetectIsSlim(BitmapSource skinBmp)
    {
        if (skinBmp.PixelWidth != 64 || skinBmp.PixelHeight != 64)
            return false;

        try
        {
            // В скинах Alex правая рука имеет ширину 14 пикселей (x=40..53).
            // Столбец 55 (и 54) в строках y=20..31 полностью прозрачен (Alpha == 0).
            // В скинах Стива столбец 55 содержит заднюю сторону руки и непрозрачен.
            var formatConverted = new FormatConvertedBitmap(skinBmp, PixelFormats.Bgra32, null, 0);
            int stride = 64 * 4;
            byte[] pixels = new byte[64 * 64 * 4];
            formatConverted.CopyPixels(pixels, stride, 0);

            for (int y = 20; y < 32; y++)
            {
                byte alpha = pixels[(y * 64 + 55) * 4 + 3];
                if (alpha > 0) return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка детекции slim-модели: {ex.Message}");
            return false;
        }
    }

    private static void DrawSubRect(DrawingContext dc, BitmapSource source, Int32Rect crop, Rect dest)
    {
        if (crop.X >= 0 && crop.Y >= 0 &&
            crop.X + crop.Width <= source.PixelWidth &&
            crop.Y + crop.Height <= source.PixelHeight)
        {
            try
            {
                var cropped = new CroppedBitmap(source, crop);
                dc.DrawImage(cropped, dest);
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка отрисовки подпрямоугольника: {ex.Message}");
            }
        }
    }

    public static IEnumerable<string> ResolveTargetGameDirectories(string gameDir)
    {
        var targets = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrWhiteSpace(gameDir))
            return targets;

        var fullGameDir = Path.GetFullPath(gameDir);
        targets.Add(fullGameDir);

        // Если указан корневой GameDir, проверяем папку instances
        var instancesDir = Path.Combine(fullGameDir, "instances");
        if (Directory.Exists(instancesDir))
        {
            var inst1201 = Path.Combine(instancesDir, "1.20.1");
            var inst1211 = Path.Combine(instancesDir, "1.21.1");
            if (Directory.Exists(inst1201)) targets.Add(inst1201);
            if (Directory.Exists(inst1211)) targets.Add(inst1211);
        }

        // Если указан подкаталог инстанса (например .../instances/1.20.1)
        var parent = Directory.GetParent(fullGameDir);
        if (parent != null && string.Equals(parent.Name, "instances", StringComparison.OrdinalIgnoreCase))
        {
            var grandParent = parent.Parent;
            if (grandParent != null)
            {
                targets.Add(grandParent.FullName);
                var sibling1201 = Path.Combine(parent.FullName, "1.20.1");
                var sibling1211 = Path.Combine(parent.FullName, "1.21.1");
                if (Directory.Exists(sibling1201)) targets.Add(sibling1201);
                if (Directory.Exists(sibling1211)) targets.Add(sibling1211);
            }
        }

        return targets;
    }

    public async Task SyncSkinToGameAsync(string? skinPath, string nickname, string gameDir, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameDir))
            return;

        var val = NicknameValidator.Validate(nickname);
        if (!val.IsValid)
        {
            throw new ArgumentException($"Недопустимый никнейм игрока для синхронизации скина: '{nickname}' ({val.Error})", nameof(nickname));
        }

        string safeNick = nickname.Trim();
        var targetDirs = ResolveTargetGameDirectories(gameDir);

        foreach (var dir in targetDirs)
        {
            var localSkinDir = Path.GetFullPath(Path.Combine(dir, "CustomSkinLoader", "LocalSkin", "skins"));
            Directory.CreateDirectory(localSkinDir);
            var targetFile = Path.GetFullPath(Path.Combine(localSkinDir, $"{safeNick}.png"));

            if (!targetFile.StartsWith(localSkinDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Небезопасный целевой путь для скина: {targetFile}");
            }

            if (!string.IsNullOrWhiteSpace(skinPath) && File.Exists(skinPath))
            {
                await Task.Run(() => File.Copy(skinPath, targetFile, overwrite: true), cancellationToken);
            }
            else
            {
                await ResetToDefaultSteveForDirAsync(dir, safeNick, cancellationToken);
            }

            ClearCustomSkinLoaderCache(dir);
        }

        InvalidateMemoryCache(safeNick);
    }

    public async Task ResetToDefaultSteveAsync(string nickname, string gameDir, CancellationToken cancellationToken = default)
    {
        await SyncSkinToGameAsync(null, nickname, gameDir, cancellationToken);
    }

    private static async Task ResetToDefaultSteveForDirAsync(string gameDir, string safeNick, CancellationToken cancellationToken)
    {
        var localSkinDir = Path.GetFullPath(Path.Combine(gameDir, "CustomSkinLoader", "LocalSkin", "skins"));
        Directory.CreateDirectory(localSkinDir);
        var targetFile = Path.GetFullPath(Path.Combine(localSkinDir, $"{safeNick}.png"));

        if (!targetFile.StartsWith(localSkinDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        bool written = false;
        try
        {
            var uri = new Uri("pack://application:,,,/AuraLauncher;component/steve.png", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo != null)
            {
                await using var fs = File.Create(targetFile);
                await streamInfo.Stream.CopyToAsync(fs, cancellationToken);
                written = true;
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка извлечения ресурса steve.png: {ex.Message}");
        }

        if (!written)
        {
            var steveLocal = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steve.png");
            if (File.Exists(steveLocal))
            {
                await Task.Run(() => File.Copy(steveLocal, targetFile, overwrite: true), cancellationToken);
            }
        }
    }

    private static BitmapSource? _cachedSteveBitmap;
    private static ImageSource? _cachedDefaultSteveHead;

    public static BitmapSource LoadDefaultSteveBitmap()
    {
        if (_cachedSteveBitmap != null) return _cachedSteveBitmap;

        try
        {
            var uri = new Uri("pack://application:,,,/AuraLauncher;component/steve.png", UriKind.Absolute);
            var streamInfo = Application.GetResourceStream(uri);
            if (streamInfo != null)
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.StreamSource = streamInfo.Stream;
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                if (bmp.CanFreeze) bmp.Freeze();
                _cachedSteveBitmap = bmp;
                return bmp;
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка загрузки pack uri steve.png: {ex.Message}");
        }

        try
        {
            string localSteve = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steve.png");
            if (File.Exists(localSteve))
            {
                var bmp = new BitmapImage();
                bmp.BeginInit();
                bmp.UriSource = new Uri(localSteve, UriKind.Absolute);
                bmp.CacheOption = BitmapCacheOption.OnLoad;
                bmp.EndInit();
                if (bmp.CanFreeze) bmp.Freeze();
                _cachedSteveBitmap = bmp;
                return bmp;
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка загрузки локального steve.png: {ex.Message}");
        }

        // Fallback: 64x64 пустой битмап
        var fallback = new WriteableBitmap(64, 64, 96, 96, PixelFormats.Bgra32, null);
        if (fallback.CanFreeze) fallback.Freeze();
        _cachedSteveBitmap = fallback;
        return fallback;
    }

    public static ImageSource LoadDefaultSteveHead()
    {
        if (_cachedDefaultSteveHead != null) return _cachedDefaultSteveHead;
        var bmp = LoadDefaultSteveBitmap();
        var head = ExtractHeadAvatarDirect(bmp);
        _cachedDefaultSteveHead = head;
        return head;
    }

    public static bool IsDefaultSteveHead(ImageSource? img) =>
        img == null || ReferenceEquals(img, _cachedDefaultSteveHead) || ReferenceEquals(img, _cachedSteveBitmap);

    public static BitmapSource ExtractHeadAvatarDirect(BitmapSource skinBmp)
    {
        try
        {
            BitmapSource converted = skinBmp;
            if (converted.Format != PixelFormats.Bgra32)
            {
                converted = new FormatConvertedBitmap(skinBmp, PixelFormats.Bgra32, null, 0);
            }
            if (converted.PixelWidth == 64 && converted.PixelHeight == 32)
            {
                converted = Convert64x32To64x64(converted);
            }
            if (converted.PixelWidth != 64 || converted.PixelHeight != 64)
            {
                return LoadDefaultSteveBitmap();
            }

            int stride = 64 * 4;
            byte[] skinPixels = new byte[64 * stride];
            converted.CopyPixels(skinPixels, stride, 0);

            byte[] headPixels = new byte[8 * 8 * 4];

            for (int y = 0; y < 8; y++)
            {
                for (int x = 0; x < 8; x++)
                {
                    int baseIdx = ((8 + y) * 64 + (8 + x)) * 4;
                    int hatIdx = ((8 + y) * 64 + (40 + x)) * 4;
                    int outIdx = (y * 8 + x) * 4;

                    byte baseB = skinPixels[baseIdx + 0];
                    byte baseG = skinPixels[baseIdx + 1];
                    byte baseR = skinPixels[baseIdx + 2];
                    byte baseA = skinPixels[baseIdx + 3];

                    byte hatB = skinPixels[hatIdx + 0];
                    byte hatG = skinPixels[hatIdx + 1];
                    byte hatR = skinPixels[hatIdx + 2];
                    byte hatA = skinPixels[hatIdx + 3];

                    if (hatA == 0)
                    {
                        headPixels[outIdx + 0] = baseB;
                        headPixels[outIdx + 1] = baseG;
                        headPixels[outIdx + 2] = baseR;
                        headPixels[outIdx + 3] = baseA;
                    }
                    else if (hatA == 255)
                    {
                        headPixels[outIdx + 0] = hatB;
                        headPixels[outIdx + 1] = hatG;
                        headPixels[outIdx + 2] = hatR;
                        headPixels[outIdx + 3] = 255;
                    }
                    else
                    {
                        float hatAlpha = hatA / 255f;
                        float baseAlpha = (baseA / 255f) * (1f - hatAlpha);
                        float totalAlpha = hatAlpha + baseAlpha;

                        if (totalAlpha > 0.001f)
                        {
                            headPixels[outIdx + 0] = (byte)((hatB * hatAlpha + baseB * baseAlpha) / totalAlpha);
                            headPixels[outIdx + 1] = (byte)((hatG * hatAlpha + baseG * baseAlpha) / totalAlpha);
                            headPixels[outIdx + 2] = (byte)((hatR * hatAlpha + baseR * baseAlpha) / totalAlpha);
                            headPixels[outIdx + 3] = (byte)(totalAlpha * 255);
                        }
                    }
                }
            }

            var result = BitmapSource.Create(8, 8, 96, 96, PixelFormats.Bgra32, null, headPixels, 8 * 4);
            if (result.CanFreeze) result.Freeze();
            return result;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка создания битмапа головы: {ex.Message}");
            return LoadDefaultSteveBitmap();
        }
    }

    private static BitmapSource Convert64x32To64x64(BitmapSource source32)
    {
        try
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // 1. Верхняя половина 64x32 (голова, туловище, правая рука, правая нога)
                dc.DrawImage(source32, new Rect(0, 0, 64, 32));

                // 2. Дублирование ноги в левую ногу:
                // Правая нога в 64x32: (0, 16, 16, 16) -> Левая нога в 64x64: (16, 48, 16, 16)
                if (source32.PixelWidth >= 16 && source32.PixelHeight >= 32)
                {
                    var legCrop = new CroppedBitmap(source32, new Int32Rect(0, 16, 16, 16));
                    dc.DrawImage(legCrop, new Rect(16, 48, 16, 16));
                }

                // 3. Дублирование руки в левую руку:
                // Правая рука в 64x32: (40, 16, 16, 16) -> Левая рука в 64x64: (32, 48, 16, 16)
                if (source32.PixelWidth >= 56 && source32.PixelHeight >= 32)
                {
                    var armCrop = new CroppedBitmap(source32, new Int32Rect(40, 16, 16, 16));
                    dc.DrawImage(armCrop, new Rect(32, 48, 16, 16));
                }
            }

            var rtb = new RenderTargetBitmap(64, 64, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            if (rtb.CanFreeze) rtb.Freeze();
            return rtb;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка конвертации скина 64x32 в 64x64: {ex.Message}");
            return source32;
        }
    }

    private static readonly System.Net.Http.HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    public async Task<SkinUploadResult> UploadSkinToLobbyApiAsync(
        string? skinPath,
        string nickname,
        string model,
        string? ownerToken,
        string? baseUrl = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return new SkinUploadResult(false, null, "Никнейм не указан.");
        }

        byte[] skinBytes;
        if (!string.IsNullOrWhiteSpace(skinPath) && File.Exists(skinPath))
        {
            try
            {
                skinBytes = await File.ReadAllBytesAsync(skinPath, cancellationToken);
            }
            catch (Exception ex)
            {
                return new SkinUploadResult(false, null, $"Не удалось прочитать файл скина: {ex.Message}");
            }
        }
        else
        {
            // Берем дефолтного Стива
            try
            {
                var uri = new Uri("pack://application:,,,/AuraLauncher;component/steve.png", UriKind.Absolute);
                var streamInfo = Application.GetResourceStream(uri);
                if (streamInfo != null)
                {
                    using var ms = new MemoryStream();
                    await streamInfo.Stream.CopyToAsync(ms, cancellationToken);
                    skinBytes = ms.ToArray();
                }
                else
                {
                    var steveLocal = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steve.png");
                    if (File.Exists(steveLocal))
                    {
                        skinBytes = await File.ReadAllBytesAsync(steveLocal, cancellationToken);
                    }
                    else
                    {
                        return new SkinUploadResult(false, null, "Файл скина не найден.");
                    }
                }
            }
            catch (Exception ex)
            {
                return new SkinUploadResult(false, null, $"Не удалось загрузить стандартный скин: {ex.Message}");
            }
        }

        var base64 = Convert.ToBase64String(skinBytes);
        var apiBase = !string.IsNullOrWhiteSpace(baseUrl) ? baseUrl.TrimEnd('/') : "https://lobby-api.vercel.app";
        var endpoint = $"{apiBase}/api/skin";

        var payload = new
        {
            nickname = nickname.Trim(),
            skinBase64 = base64,
            model = model == "slim" ? "slim" : "default",
            ownerToken = string.IsNullOrWhiteSpace(ownerToken) ? null : ownerToken.Trim()
        };

        var json = System.Text.Json.JsonSerializer.Serialize(payload);
        using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");

        try
        {
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, endpoint)
            {
                Content = content
            };

            AddUserAuthHeaders(req);

            using var response = await _httpClient.SendAsync(req, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

            // Если сессия UserId в Redis истекла (401), повторяем запрос без X-User-Id/X-User-Token (по ownerToken)
            if (response.StatusCode == System.Net.HttpStatusCode.Unauthorized && req.Headers.Contains("X-User-Id"))
            {
                using var retryContent = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
                using var retryReq = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, endpoint)
                {
                    Content = retryContent
                };
                using var retryResp = await _httpClient.SendAsync(retryReq, cancellationToken);
                responseText = await retryResp.Content.ReadAsStringAsync(cancellationToken);
                if (retryResp.IsSuccessStatusCode)
                {
                    string? retryOwnerToken = ownerToken;
                    try
                    {
                        using var doc = System.Text.Json.JsonDocument.Parse(responseText);
                        if (doc.RootElement.TryGetProperty("ownerToken", out var tokenElem))
                        {
                            retryOwnerToken = tokenElem.GetString();
                        }
                    }
                    catch (Exception ex)
                    {
                        FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка парсинга ownerToken при повторной попытке: {ex.Message}");
                    }
                    InvalidateMemoryCache(nickname);
                    return new SkinUploadResult(true, retryOwnerToken, null);
                }
            }

            if (response.StatusCode == System.Net.HttpStatusCode.Conflict || response.StatusCode == System.Net.HttpStatusCode.Forbidden)
            {
                return new SkinUploadResult(false, null, "Этот ник уже занят другим игроком, выбери другой");
            }

            if (!response.IsSuccessStatusCode)
            {
                string err = "Ошибка загрузки скина на сервер";
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(responseText);
                    if (doc.RootElement.TryGetProperty("error", out var errElem))
                    {
                        err = errElem.GetString() ?? err;
                    }
                }
                catch (Exception ex)
                {
                    FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка парсинга сообщения об ошибке: {ex.Message}");
                }
                return new SkinUploadResult(false, null, err);
            }

            string? newOwnerToken = ownerToken;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(responseText);
                if (doc.RootElement.TryGetProperty("ownerToken", out var tokenElem))
                {
                    newOwnerToken = tokenElem.GetString();
                }
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка парсинга нового ownerToken: {ex.Message}");
            }
            InvalidateMemoryCache(nickname);
            return new SkinUploadResult(true, newOwnerToken, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return new SkinUploadResult(false, null, $"Сетевая ошибка при загрузке скина: {ex.Message}");
        }
    }

    /// <summary>
    /// Заголовки X-User-Id / X-User-Token из %APPDATA%\.aura\config.json (токен расшифровывается DPAPI):
    /// без них lobby-api не даёт менять скин и плащ зарегистрированного ника.
    /// </summary>
    public static void AddUserAuthHeaders(System.Net.Http.HttpRequestMessage req)
    {
        try
        {
            string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            string cfgPath = Path.Combine(appData, ".aura", "config.json");
            if (!File.Exists(cfgPath))
            {
                cfgPath = Path.Combine(appData, "Aura", "config.json");
            }
            if (!File.Exists(cfgPath)) return;

            var cfgJson = File.ReadAllText(cfgPath);
            using var doc = System.Text.Json.JsonDocument.Parse(cfgJson);
            if (doc.RootElement.TryGetProperty("UserId", out var uidElem) && uidElem.GetString() is { } uid && !string.IsNullOrWhiteSpace(uid))
            {
                req.Headers.TryAddWithoutValidation("X-User-Id", uid);
            }
            if (doc.RootElement.TryGetProperty("UserTokenEncrypted", out var tokElem) && tokElem.GetString() is { } tokEnc && !string.IsNullOrWhiteSpace(tokEnc))
            {
                try
                {
                    var cipherBytes = Convert.FromBase64String(tokEnc);
                    var plainBytes = System.Security.Cryptography.ProtectedData.Unprotect(cipherBytes, null, System.Security.Cryptography.DataProtectionScope.CurrentUser);
                    var plainToken = System.Text.Encoding.UTF8.GetString(plainBytes);
                    req.Headers.TryAddWithoutValidation("X-User-Token", plainToken);
                }
                catch
                {
                    req.Headers.TryAddWithoutValidation("X-User-Token", tokEnc);
                }
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка извлечения учетных данных из config.json: {ex.Message}");
        }
    }

    public void EnsureCustomSkinLoaderConfig(string gameDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(gameDir)) return;
            var cslDir = Path.Combine(gameDir, "CustomSkinLoader");
            Directory.CreateDirectory(cslDir);
            var cslJsonPath = Path.Combine(cslDir, "CustomSkinLoader.json");

            var auraSource = new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = "AuraLobby",
                ["type"] = "CustomSkinAPI",
                ["root"] = "https://lobby-api.vercel.app/csl/"
            };

            var localSkinSource = new System.Text.Json.Nodes.JsonObject
            {
                ["name"] = "LocalSkin",
                ["type"] = "Legacy",
                ["checkPNG"] = false,
                ["skin"] = "LocalSkin/skins/{USERNAME}.png",
                ["model"] = "auto",
                ["cape"] = "LocalSkin/capes/{USERNAME}.png",
                ["elytra"] = "LocalSkin/elytras/{USERNAME}.png"
            };

            var rootObj = new System.Text.Json.Nodes.JsonObject
            {
                ["version"] = "14.27",
                ["buildNumber"] = 94,
                ["loadlist"] = new System.Text.Json.Nodes.JsonArray(auraSource, localSkinSource),
                ["enableDynamicSkull"] = true,
                ["enableTransparentSkin"] = true,
                ["forceLoadAllTextures"] = false,
                ["enableCape"] = true,
                ["threadPoolSize"] = 4,
                ["enableLogStdOut"] = false,
                ["cacheExpiry"] = 0,
                ["forceUpdateSkull"] = true,
                ["enableLocalProfileCache"] = false,
                ["enableCacheAutoClean"] = true,
                ["forceDisableCache"] = true
            };

            var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(cslJsonPath, rootObj.ToJsonString(options));

            ClearCustomSkinLoaderCache(gameDir);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[CSL-CONFIG: ERROR] {ex.Message}");
        }
    }

    public void ClearCustomSkinLoaderCache(string gameDir)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(gameDir)) return;
            var fullGameDir = Path.GetFullPath(gameDir);
            var cachesDir = Path.GetFullPath(Path.Combine(fullGameDir, "CustomSkinLoader", "caches"));
            if (!cachesDir.StartsWith(fullGameDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                return;
            if (Directory.Exists(cachesDir))
            {
                var files = Directory.GetFiles(cachesDir, "*", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    try { File.Delete(f); }
                    catch (Exception ex)
                    {
                        FabricGameLaunchService.LogLauncherEvent($"[CSL-CACHE: WARN] Ошибка удаления файла кэша {f}: {ex.Message}");
                    }
                }
                FabricGameLaunchService.LogLauncherEvent($"[CSL-CACHE] Очищен кэш CustomSkinLoader: {cachesDir}");
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[CSL-CACHE: ERROR] Ошибка очистки кэша: {ex.Message}");
        }
    }

    private static readonly TimeSpan SkinCheckTtl = TimeSpan.FromSeconds(15);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (ImageSource Avatar, DateTime FetchedAt)> _avatarCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, (ImageSource SkinTexture, bool IsSlim, string Hash, DateTime FetchedAt)> _skinTextureCache = new(StringComparer.OrdinalIgnoreCase);
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, BitmapSource> _textureByHashCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ImageSource> GetAvatarForPlayerAsync(string nickname, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return LoadDefaultSteveHead();
        }

        if (_avatarCache.TryGetValue(nickname, out var cached) && (DateTime.UtcNow - cached.FetchedAt) < SkinCheckTtl)
        {
            return cached.Avatar;
        }

        var (skinTexture, _) = await GetSkinTextureForPlayerAsync(nickname, cancellationToken);
        if (skinTexture is BitmapSource bmp && !ReferenceEquals(bmp, LoadDefaultSteveBitmap()))
        {
            if (_avatarCache.TryGetValue(nickname, out var updated))
            {
                return updated.Avatar;
            }
            var avatar = ExtractHeadAvatarDirect(bmp);
            _avatarCache[nickname] = (avatar, DateTime.UtcNow);
            return avatar;
        }

        return LoadDefaultSteveHead();
    }

    public async Task<(ImageSource SkinTexture, bool IsSlim)> GetSkinTextureForPlayerAsync(string nickname, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return (LoadDefaultSteveBitmap(), false);
        }

        if (_skinTextureCache.TryGetValue(nickname, out var cachedSkin) && (DateTime.UtcNow - cachedSkin.FetchedAt) < SkinCheckTtl)
        {
            return (cachedSkin.SkinTexture, cachedSkin.IsSlim);
        }

        try
        {
            var url = $"https://lobby-api.vercel.app/csl/{Uri.EscapeDataString(nickname)}.json";
            using var req = new HttpRequestMessage(HttpMethod.Get, url);
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                var json = await resp.Content.ReadAsStringAsync(cancellationToken);
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("skins", out var skinsProp))
                {
                    string? textureHashOrUrl = null;
                    bool isSlimFromApi = false;
                    if (skinsProp.TryGetProperty("slim", out var slimProp) && slimProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        textureHashOrUrl = slimProp.GetString();
                        isSlimFromApi = true;
                    }
                    else if (skinsProp.TryGetProperty("default", out var defProp) && defProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        textureHashOrUrl = defProp.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(textureHashOrUrl))
                    {
                        if (cachedSkin.SkinTexture != null &&
                            string.Equals(cachedSkin.Hash, textureHashOrUrl, StringComparison.OrdinalIgnoreCase) &&
                            cachedSkin.IsSlim == isSlimFromApi)
                        {
                            _skinTextureCache[nickname] = (cachedSkin.SkinTexture, cachedSkin.IsSlim, cachedSkin.Hash, DateTime.UtcNow);
                            if (_avatarCache.TryGetValue(nickname, out var prevAv))
                            {
                                _avatarCache[nickname] = (prevAv.Avatar, DateTime.UtcNow);
                            }
                            return (cachedSkin.SkinTexture, cachedSkin.IsSlim);
                        }

                        if (!_textureByHashCache.TryGetValue(textureHashOrUrl, out var converted))
                        {
                            string resolvedUrl = textureHashOrUrl;
                            if (!resolvedUrl.StartsWith("http://", StringComparison.OrdinalIgnoreCase) &&
                                !resolvedUrl.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                            {
                                resolvedUrl = resolvedUrl.StartsWith('/')
                                    ? $"https://lobby-api.vercel.app{resolvedUrl}"
                                    : $"https://lobby-api.vercel.app/csl/textures/{resolvedUrl}";
                            }

                            byte[] pngBytes = await _httpClient.GetByteArrayAsync(resolvedUrl, cancellationToken);
                            using var ms = new MemoryStream(pngBytes);
                            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                            var frame = decoder.Frames[0];
                            converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                            if (converted.PixelWidth == 64 && converted.PixelHeight == 32)
                            {
                                converted = Convert64x32To64x64(converted);
                            }
                            if (converted.CanFreeze && !converted.IsFrozen) converted.Freeze();
                            _textureByHashCache[textureHashOrUrl] = converted;
                        }

                        bool isSlim = isSlimFromApi || DetectIsSlim(converted);
                        var avatar = ExtractHeadAvatarDirect(converted);
                        _skinTextureCache[nickname] = (converted, isSlim, textureHashOrUrl, DateTime.UtcNow);
                        _avatarCache[nickname] = (avatar, DateTime.UtcNow);
                        return (converted, isSlim);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SKIN: ERROR] Ошибка получения текстуры скина: {ex.Message}");
        }

        if (cachedSkin.SkinTexture != null)
        {
            return (cachedSkin.SkinTexture, cachedSkin.IsSlim);
        }

        return (LoadDefaultSteveBitmap(), false);
    }

    public SkinValidationResult ValidateCapeBytes(byte[]? bytes)
    {
        if (bytes == null || bytes.Length == 0)
        {
            return new SkinValidationResult(false, "Массив байтов плаща пуст.");
        }

        if (bytes.Length > 2 * 1024 * 1024)
        {
            return new SkinValidationResult(false, "Файл плаща превышает допустимый размер (2 МБ).");
        }

        if (bytes.Length < 24)
        {
            return new SkinValidationResult(false, "Файл не является допустимым изображением PNG (слишком мал).");
        }

        // 1. Проверка сигнатуры PNG до дорогостоящего декодирования
        if (bytes[0] != 0x89 || bytes[1] != 0x50 || bytes[2] != 0x4E || bytes[3] != 0x47 ||
            bytes[4] != 0x0D || bytes[5] != 0x0A || bytes[6] != 0x1A || bytes[7] != 0x0A)
        {
            return new SkinValidationResult(false, "Файл не является допустимым изображением PNG.");
        }

        // 2. Чтение IHDR и валидация лимитов (размеры, пиксели, соотношение) ДО создания BitmapDecoder
        int width = (bytes[16] << 24) | (bytes[17] << 16) | (bytes[18] << 8) | bytes[19];
        int height = (bytes[20] << 24) | (bytes[21] << 16) | (bytes[22] << 8) | bytes[23];

        if (width <= 0 || height <= 0)
        {
            return new SkinValidationResult(false, "Поврежденные размеры изображения в заголовке PNG.");
        }

        if (width > 1024 || height > 512 || (long)width * height > 1024 * 512)
        {
            return new SkinValidationResult(false, $"Неподдерживаемый размер плаща ({width}×{height}). Превышен максимальный лимит 1024×512.", width, height);
        }

        // Допустимые размеры плащей в Minecraft и CustomSkinLoader:
        // - Стандартные 2:1 кратные 64x32 до 1024x512: 64x32 (1x), 128x64 (2x), 256x128 (4x), 512x256 (8x), 1024x512 (16x)
        // - Классический обрезанный: 22x17
        // - Квадратный плащ: 64x64
        bool isStandard2To1 = (width == height * 2) && (width == 64 || width == 128 || width == 256 || width == 512 || width == 1024);
        bool isClassic = (width == 22 && height == 17);
        bool isSquare = (width == 64 && height == 64);

        if (!isStandard2To1 && !isClassic && !isSquare)
        {
            return new SkinValidationResult(false, $"Неподдерживаемый размер плаща ({width}×{height}). Требуются размеры формата 2:1 (64×32, 128×64, 256×128, 512×256, 1024×512) либо 22×17, 64×64.", width, height);
        }

        try
        {
            using var ms = new MemoryStream(bytes);
            var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
            if (decoder.Frames.Count == 0)
            {
                return new SkinValidationResult(false, "Файл не содержит графических кадров или поврежден.");
            }

            var frame = decoder.Frames[0];
            if (frame.PixelWidth != width || frame.PixelHeight != height)
            {
                return new SkinValidationResult(false, "Несоответствие фактических размеров кадра заголовку PNG.", frame.PixelWidth, frame.PixelHeight);
            }

            return new SkinValidationResult(true, null, width, height);
        }
        catch (Exception ex)
        {
            return new SkinValidationResult(false, $"Файл плаща поврежден или не является допустимым PNG: {ex.Message}");
        }
    }

    public SkinValidationResult ValidateCapeFile(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
        {
            return new SkinValidationResult(false, "Путь к файлу плаща не указан.");
        }

        if (!File.Exists(filePath))
        {
            return new SkinValidationResult(false, "Выбранный файл плаща не существует.");
        }

        try
        {
            var fileInfo = new FileInfo(filePath);
            if (fileInfo.Length == 0)
            {
                return new SkinValidationResult(false, "Файл плаща поврежден (0 байт).");
            }

            if (fileInfo.Length > 2 * 1024 * 1024)
            {
                return new SkinValidationResult(false, "Файл плаща превышает допустимый размер (2 МБ).");
            }

            if (fileInfo.Length < 24)
            {
                return new SkinValidationResult(false, "Файл не является допустимым изображением PNG (слишком мал).");
            }

            // Читаем заголовок PNG (24 байта) ДО полной загрузки и декодирования
            byte[] header = new byte[24];
            using (var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read))
            {
                int read = fs.Read(header, 0, 24);
                if (read < 24)
                {
                    return new SkinValidationResult(false, "Файл поврежден (не удалось прочитать заголовок PNG).");
                }
            }

            if (header[0] != 0x89 || header[1] != 0x50 || header[2] != 0x4E || header[3] != 0x47 ||
                header[4] != 0x0D || header[5] != 0x0A || header[6] != 0x1A || header[7] != 0x0A)
            {
                return new SkinValidationResult(false, "Файл не является допустимым изображением PNG.");
            }

            int width = (header[16] << 24) | (header[17] << 16) | (header[18] << 8) | header[19];
            int height = (header[20] << 24) | (header[21] << 16) | (header[22] << 8) | header[23];

            if (width <= 0 || height <= 0)
            {
                return new SkinValidationResult(false, "Поврежденные размеры изображения в заголовке PNG.");
            }

            if (width > 1024 || height > 512 || (long)width * height > 1024 * 512)
            {
                return new SkinValidationResult(false, $"Неподдерживаемый размер плаща ({width}×{height}). Превышен максимальный лимит 1024×512.", width, height);
            }

            bool isStandard2To1 = (width == height * 2) && (width == 64 || width == 128 || width == 256 || width == 512 || width == 1024);
            bool isClassic = (width == 22 && height == 17);
            bool isSquare = (width == 64 && height == 64);

            if (!isStandard2To1 && !isClassic && !isSquare)
            {
                return new SkinValidationResult(false, $"Неподдерживаемый размер плаща ({width}×{height}). Требуются размеры формата 2:1 (64×32, 128×64, 256×128, 512×256, 1024×512) либо 22×17, 64×64.", width, height);
            }

            using (var stream = File.OpenRead(filePath))
            {
                var decoder = BitmapDecoder.Create(stream, BitmapCreateOptions.DelayCreation, BitmapCacheOption.None);
                if (decoder.Frames.Count == 0)
                {
                    return new SkinValidationResult(false, "Файл не содержит графических кадров или поврежден.");
                }

                var frame = decoder.Frames[0];
                if (frame.PixelWidth != width || frame.PixelHeight != height)
                {
                    return new SkinValidationResult(false, "Несоответствие фактических размеров кадра заголовку PNG.", frame.PixelWidth, frame.PixelHeight);
                }
            }

            return new SkinValidationResult(true, null, width, height);
        }
        catch (Exception ex)
        {
            return new SkinValidationResult(false, $"Файл плаща поврежден или не является допустимым PNG: {ex.Message}");
        }
    }

    public ImageSource? LoadCapeImage(string? capePath)
    {
        if (string.IsNullOrWhiteSpace(capePath))
            return null;

        // Встроенные плащи Aura
        if (string.Equals(capePath, "cyan", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(capePath, "silver", StringComparison.OrdinalIgnoreCase))
        {
            string capeName = capePath.ToLowerInvariant();
            string[] possiblePaths =
            {
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "assets", "images", $"cape_{capeName}.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "redesign", "v3", "dist", "assets", "images", $"cape_{capeName}.png"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "docs", "redesign", "v3", "public", "assets", "images", $"cape_{capeName}.png")
            };

            foreach (var p in possiblePaths)
            {
                if (File.Exists(p))
                {
                    try
                    {
                        var bmp = new BitmapImage();
                        bmp.BeginInit();
                        bmp.UriSource = new Uri(p, UriKind.Absolute);
                        bmp.CacheOption = BitmapCacheOption.OnLoad;
                        bmp.EndInit();
                        if (bmp.CanFreeze) bmp.Freeze();
                        return bmp;
                    }
                    catch { }
                }
            }

            return null;
        }

        if (!File.Exists(capePath))
            return null;

        try
        {
            var bmp = new BitmapImage();
            bmp.BeginInit();
            bmp.UriSource = new Uri(capePath, UriKind.Absolute);
            bmp.CacheOption = BitmapCacheOption.OnLoad;
            bmp.EndInit();
            if (bmp.CanFreeze) bmp.Freeze();
            return bmp;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[CAPE: ERROR] Ошибка загрузки текстуры плаща: {ex.Message}");
            return null;
        }
    }

    public async Task SyncCapeToGameAsync(string? capePath, string nickname, string gameDir, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameDir))
            return;

        var val = NicknameValidator.Validate(nickname);
        if (!val.IsValid)
        {
            throw new ArgumentException($"Недопустимый никнейм игрока для синхронизации плаща: '{nickname}' ({val.Error})", nameof(nickname));
        }

        string safeNick = nickname.Trim();
        var targetDirs = ResolveTargetGameDirectories(gameDir);

        foreach (var dir in targetDirs)
        {
            var localCapeDir = Path.GetFullPath(Path.Combine(dir, "CustomSkinLoader", "LocalSkin", "capes"));
            Directory.CreateDirectory(localCapeDir);
            var targetFile = Path.GetFullPath(Path.Combine(localCapeDir, $"{safeNick}.png"));

            if (!targetFile.StartsWith(localCapeDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Небезопасный целевой путь для плаща: {targetFile}");
            }

            if (!string.IsNullOrWhiteSpace(capePath) && File.Exists(capePath))
            {
                await Task.Run(() => File.Copy(capePath, targetFile, overwrite: true), cancellationToken);
                FabricGameLaunchService.LogLauncherEvent($"[CAPE] Синхронизирован кастомный плащ в {targetFile}");
            }
            else
            {
                if (File.Exists(targetFile))
                {
                    await Task.Run(() => File.Delete(targetFile), cancellationToken);
                    FabricGameLaunchService.LogLauncherEvent($"[CAPE] Локальный плащ удален: {targetFile}");
                }
            }

            ClearCustomSkinLoaderCache(dir);
        }
    }

    public async Task<(string ManagedPath, string Hash)> SaveToManagedStorageAsync(byte[] fileBytes, string type, string originalFileName, CancellationToken cancellationToken = default)
    {
        if (fileBytes == null || fileBytes.Length == 0)
            throw new ArgumentException("Массив байтов пуст.", nameof(fileBytes));

        // 1. Валидация формата и декомпрессия графических кадров
        if (string.Equals(type, "cape", StringComparison.OrdinalIgnoreCase))
        {
            var val = ValidateCapeBytes(fileBytes);
            if (!val.IsValid)
            {
                throw new InvalidOperationException(val.ErrorMessage ?? "Недопустимый файл плаща.");
            }
        }
        else
        {
            var val = ValidateSkinBytes(fileBytes);
            if (!val.IsValid)
            {
                throw new InvalidOperationException(val.ErrorMessage ?? "Недопустимый файл скина.");
            }
        }

        // 2. Вычисление SHA-256 хеша
        string hashHex;
        using (var sha = System.Security.Cryptography.SHA256.Create())
        {
            byte[] hashBytes = sha.ComputeHash(fileBytes);
            hashHex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        }

        // 3. Разрешение изолированного каталога хранилища (через IConfigService / AURA_PROFILE_DIR)
        string profileDir = GetProfileDataDir();
        string subFolder = string.Equals(type, "cape", StringComparison.OrdinalIgnoreCase) ? "capes" : "skins";
        string managedDir = Path.GetFullPath(Path.Combine(profileDir, subFolder));
        Directory.CreateDirectory(managedDir);

        string targetPath = Path.GetFullPath(Path.Combine(managedDir, $"{hashHex}.png"));
        if (!targetPath.StartsWith(managedDir + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"Небезопасный целевой путь managed storage: {targetPath}");
        }

        // 4. Дедупликация с проверкой целостности существующего файла на диске
        if (File.Exists(targetPath))
        {
            try
            {
                var fi = new FileInfo(targetPath);
                if (fi.Length == fileBytes.Length)
                {
                    byte[] existingBytes = await File.ReadAllBytesAsync(targetPath, cancellationToken);
                    using var sha = System.Security.Cryptography.SHA256.Create();
                    string existingHash = Convert.ToHexString(sha.ComputeHash(existingBytes)).ToLowerInvariant();
                    if (string.Equals(existingHash, hashHex, StringComparison.OrdinalIgnoreCase))
                    {
                        return (targetPath, hashHex);
                    }
                }
            }
            catch
            {
                // Если чтение существующего файла сбоит, перезаписываем
            }
        }

        // 5. Запись через временный .part файл в ТОМ ЖЕ каталоге (гарантирует атомарность перемещения без межтомных сбоев)
        string partPath = Path.Combine(managedDir, $"{hashHex}.{Guid.NewGuid():N}.part");
        try
        {
            await File.WriteAllBytesAsync(partPath, fileBytes, cancellationToken);
            File.Move(partPath, targetPath, overwrite: true);
            return (targetPath, hashHex);
        }
        finally
        {
            try
            {
                if (File.Exists(partPath))
                    File.Delete(partPath);
            }
            catch { }
        }
    }

    public async Task<(string ManagedPath, string Hash)> SaveToManagedStorageAsync(string sourceFilePath, string type, string? originalFileName = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourceFilePath) || !File.Exists(sourceFilePath))
            throw new FileNotFoundException("Исходный файл не найден.", sourceFilePath);

        byte[] bytes = await File.ReadAllBytesAsync(sourceFilePath, cancellationToken);
        string safeName = !string.IsNullOrWhiteSpace(originalFileName)
            ? Path.GetFileName(originalFileName)
            : Path.GetFileName(sourceFilePath);

        return await SaveToManagedStorageAsync(bytes, type, safeName, cancellationToken);
    }

    public void InvalidateMemoryCache(string? nickname = null)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            _skinTextureCache.Clear();
            _avatarCache.Clear();
        }
        else
        {
            _skinTextureCache.TryRemove(nickname, out _);
            _avatarCache.TryRemove(nickname, out _);
        }
    }
}

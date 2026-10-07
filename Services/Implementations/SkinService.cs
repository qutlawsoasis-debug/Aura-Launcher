using System;
using System.IO;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Реализация сервиса управления скинами и подготовки 2D-превью.
/// </summary>
public class SkinService : ISkinService
{
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
            catch { }
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
                return LoadDefaultSteveBitmap();
            }

            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                // Отрисовка базовой головы (8,8,8,8)
                DrawSubRect(dc, skinBmp, new Int32Rect(8, 8, 8, 8), new Rect(0, 0, 8, 8));

                // Наложение слоя шляпы поверх базовой головы (40,8,8,8)
                DrawSubRect(dc, skinBmp, new Int32Rect(40, 8, 8, 8), new Rect(0, 0, 8, 8));
            }

            var rtb = new RenderTargetBitmap(8, 8, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            if (rtb.CanFreeze) rtb.Freeze();
            return rtb;
        }
        catch
        {
            return LoadDefaultSteveBitmap();
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
        catch
        {
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
        catch
        {
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
        catch
        {
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
            catch { }
        }
    }

    public async Task SyncSkinToGameAsync(string? skinPath, string nickname, string gameDir, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || string.IsNullOrWhiteSpace(nickname))
            return;

        var localSkinDir = Path.Combine(gameDir, "CustomSkinLoader", "LocalSkin", "skins");
        Directory.CreateDirectory(localSkinDir);
        var targetFile = Path.Combine(localSkinDir, $"{nickname.Trim()}.png");

        if (!string.IsNullOrWhiteSpace(skinPath) && File.Exists(skinPath))
        {
            await Task.Run(() => File.Copy(skinPath, targetFile, overwrite: true), cancellationToken);
        }
        else
        {
            await ResetToDefaultSteveAsync(nickname, gameDir, cancellationToken);
        }

        ClearCustomSkinLoaderCache(gameDir);
    }

    public async Task ResetToDefaultSteveAsync(string nickname, string gameDir, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(gameDir) || string.IsNullOrWhiteSpace(nickname))
            return;

        var localSkinDir = Path.Combine(gameDir, "CustomSkinLoader", "LocalSkin", "skins");
        Directory.CreateDirectory(localSkinDir);
        var targetFile = Path.Combine(localSkinDir, $"{nickname.Trim()}.png");

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
        catch { }

        if (!written)
        {
            var steveLocal = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "steve.png");
            if (File.Exists(steveLocal))
            {
                await Task.Run(() => File.Copy(steveLocal, targetFile, overwrite: true), cancellationToken);
            }
        }
    }

    public static BitmapSource LoadDefaultSteveBitmap()
    {
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
                return bmp;
            }
        }
        catch { }

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
                return bmp;
            }
        }
        catch { }

        // Fallback: 64x64 пустой битмап
        var fallback = new WriteableBitmap(64, 64, 96, 96, PixelFormats.Bgra32, null);
        if (fallback.CanFreeze) fallback.Freeze();
        return fallback;
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
        catch
        {
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

            // Read auth tokens from %APPDATA%\.aura\config.json via DPAPI if available
            try
            {
                string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
                string cfgPath = Path.Combine(appData, ".aura", "config.json");
                if (!File.Exists(cfgPath))
                {
                    cfgPath = Path.Combine(appData, "Aura", "config.json");
                }
                if (File.Exists(cfgPath))
                {
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
            }
            catch { }

            using var response = await _httpClient.SendAsync(req, cancellationToken);
            var responseText = await response.Content.ReadAsStringAsync(cancellationToken);

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
                catch { }
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
            catch { }

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

            System.Text.Json.Nodes.JsonNode? rootNode = null;
            if (File.Exists(cslJsonPath))
            {
                try
                {
                    var text = File.ReadAllText(cslJsonPath);
                    rootNode = System.Text.Json.Nodes.JsonNode.Parse(text);
                }
                catch { }
            }

            if (rootNode is not System.Text.Json.Nodes.JsonObject rootObj)
            {
                rootObj = new System.Text.Json.Nodes.JsonObject
                {
                    ["version"] = "15.0.1",
                    ["buildNumber"] = 40,
                    ["loadlist"] = new System.Text.Json.Nodes.JsonArray(),
                    ["enableTransparentSkin"] = true,
                    ["forceLoadAllTextures"] = true,
                    ["enableCape"] = true,
                    ["threadPoolSize"] = 8,
                    ["enableLogStdOut"] = false,
                    ["cacheExpiry"] = 30,
                    ["forceUpdateSkull"] = false,
                    ["enableLocalProfileCache"] = false,
                    ["enableCacheAutoClean"] = false,
                    ["forceDisableCache"] = false
                };
            }

            var loadlistNode = rootObj["loadlist"] as System.Text.Json.Nodes.JsonArray;
            if (loadlistNode == null)
            {
                loadlistNode = new System.Text.Json.Nodes.JsonArray();
                rootObj["loadlist"] = loadlistNode;
            }

            // Проверяем, есть ли уже AuraLobby в списке
            for (int i = loadlistNode.Count - 1; i >= 0; i--)
            {
                var item = loadlistNode[i] as System.Text.Json.Nodes.JsonObject;
                if (item != null && item["name"]?.GetValue<string>() == "AuraLobby")
                {
                    loadlistNode.RemoveAt(i);
                }
            }

            // Вставляем первым элементом
            loadlistNode.Insert(0, auraSource);

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
            var cachesDir = Path.Combine(gameDir, "CustomSkinLoader", "caches");
            if (Directory.Exists(cachesDir))
            {
                var files = Directory.GetFiles(cachesDir, "*", SearchOption.AllDirectories);
                foreach (var f in files)
                {
                    try { File.Delete(f); } catch { }
                }
                FabricGameLaunchService.LogLauncherEvent($"[CSL-CACHE] Очищен кэш CustomSkinLoader: {cachesDir}");
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[CSL-CACHE: ERROR] Ошибка очистки кэша: {ex.Message}");
        }
    }

    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, ImageSource> _avatarCache = new(StringComparer.OrdinalIgnoreCase);

    public async Task<ImageSource> GetAvatarForPlayerAsync(string nickname, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(nickname))
        {
            return LoadDefaultSteveBitmap();
        }

        if (_avatarCache.TryGetValue(nickname, out var cached))
        {
            return cached;
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
                    string? textureUrl = null;
                    if (skinsProp.TryGetProperty("default", out var defProp) && defProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        textureUrl = defProp.GetString();
                    }
                    else if (skinsProp.TryGetProperty("slim", out var slimProp) && slimProp.ValueKind == System.Text.Json.JsonValueKind.String)
                    {
                        textureUrl = slimProp.GetString();
                    }

                    if (!string.IsNullOrWhiteSpace(textureUrl))
                    {
                        byte[] pngBytes = await _httpClient.GetByteArrayAsync(textureUrl, cancellationToken);
                        using var ms = new MemoryStream(pngBytes);
                        var decoder = BitmapDecoder.Create(ms, BitmapCreateOptions.None, BitmapCacheOption.OnLoad);
                        var frame = decoder.Frames[0];
                        var converted = new FormatConvertedBitmap(frame, PixelFormats.Bgra32, null, 0);
                        if (converted.CanFreeze) converted.Freeze();

                        var avatar = ExtractHeadAvatarFromBitmap(converted);
                        _avatarCache[nickname] = avatar;
                        return avatar;
                    }
                }
            }
        }
        catch
        {
        }

        var fallback = LoadDefaultSteveBitmap();
        _avatarCache[nickname] = fallback;
        return fallback;
    }

    private ImageSource ExtractHeadAvatarFromBitmap(BitmapSource skinBmp)
    {
        try
        {
            var dv = new DrawingVisual();
            using (var dc = dv.RenderOpen())
            {
                DrawSubRect(dc, skinBmp, new Int32Rect(8, 8, 8, 8), new Rect(0, 0, 8, 8));
                DrawSubRect(dc, skinBmp, new Int32Rect(40, 8, 8, 8), new Rect(0, 0, 8, 8));
            }

            var rtb = new RenderTargetBitmap(8, 8, 96, 96, PixelFormats.Pbgra32);
            rtb.Render(dv);
            if (rtb.CanFreeze) rtb.Freeze();
            return rtb;
        }
        catch
        {
            return LoadDefaultSteveBitmap();
        }
    }
}

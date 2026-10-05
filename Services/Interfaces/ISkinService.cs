using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;

namespace AuraLauncher.Services.Interfaces;

public record SkinValidationResult(bool IsValid, string? ErrorMessage = null, int Width = 0, int Height = 0);
public record SkinUploadResult(bool Success, string? OwnerToken = null, string? ErrorMessage = null);

/// <summary>
/// Сервис управления скинами игрока и подготовки 2D-превью (вид спереди и сзади).
/// </summary>
public interface ISkinService
{
    /// <summary>
    /// Валидация файла скина (проверка существования, целостности и размеров 64x64 или 64x32).
    /// </summary>
    SkinValidationResult ValidateSkinFile(string? filePath);

    /// <summary>
    /// Загрузка скина как ImageSource (64x64 с NearestNeighbor скейлингом).
    /// </summary>
    ImageSource LoadSkinImage(string? skinPath);

    /// <summary>
    /// Извлечение аватара головы игрока (базовый слой 8,8,8,8 + слой шляпы 40,8,8,8).
    /// </summary>
    ImageSource ExtractHeadAvatar(string? skinPath);

    /// <summary>
    /// Извлечение полного плоского 2D-превью скина (вид спереди: голова, торс, руки, ноги + оверлей).
    /// </summary>
    ImageSource ExtractFrontSkinPreview(string? skinPath);

    /// <summary>
    /// Извлечение полного плоского 2D-превью скина (вид сзади: голова, торс, руки, ноги + оверлей).
    /// </summary>
    ImageSource ExtractBackSkinPreview(string? skinPath);

    /// <summary>
    /// Синхронизация файла скина в каталог CustomSkinLoader игры Minecraft (<gameDir>/CustomSkinLoader/LocalSkin/skins/<nick>.png).
    /// </summary>
    Task SyncSkinToGameAsync(string? skinPath, string nickname, string gameDir, CancellationToken cancellationToken = default);

    /// <summary>
    /// Сброс скина на дефолтного Стива.
    /// </summary>
    Task ResetToDefaultSteveAsync(string nickname, string gameDir, CancellationToken cancellationToken = default);

    /// <summary>
    /// Загрузка скина в lobby-api для отображения у всех игроков.
    /// </summary>
    Task<SkinUploadResult> UploadSkinToLobbyApiAsync(string? skinPath, string nickname, string model, string? ownerToken, string? baseUrl = null, CancellationToken cancellationToken = default);

    /// <summary>
    /// Мерж конфига CustomSkinLoader.json: добавление AuraLobby первым источником в loadlist.
    /// </summary>
    void EnsureCustomSkinLoaderConfig(string gameDir);
}

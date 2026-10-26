using System;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public record SkinValidationResult(bool IsValid, string? ErrorMessage = null, int Width = 0, int Height = 0);
public record SkinUploadResult(bool Success, string? OwnerToken = null, string? ErrorMessage = null);

/// <summary>
/// Сервис управления скинами игрока и подготовки 2D-превью (вид спереди и сзади).
/// </summary>
public interface ISkinService
{
    /// <summary>
    /// Валидация байтов скина (проверка PNG и размеров 64x64 или 64x32).
    /// </summary>
    SkinValidationResult ValidateSkinBytes(byte[]? bytes) => new SkinValidationResult(false, "Not implemented");

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
    /// Получение аватара головы игрока по никнейму (из lobby-api /csl/{nick}.json или дефолтный Стив).
    /// </summary>
    Task<ImageSource> GetAvatarForPlayerAsync(string nickname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Получение полной текстуры скина (64x64) и модели рук (isSlim) для игрока по никнейму (из lobby-api /csl/{nick}.json или дефолтный Стив).
    /// </summary>
    Task<(ImageSource SkinTexture, bool IsSlim)> GetSkinTextureForPlayerAsync(string nickname, CancellationToken cancellationToken = default);

    /// <summary>
    /// Мерж конфига CustomSkinLoader.json: добавление AuraLobby первым источником в loadlist.
    /// </summary>
    void EnsureCustomSkinLoaderConfig(string gameDir);

    /// <summary>
    /// Очистка папки кэша CustomSkinLoader (<gameDir>/CustomSkinLoader/caches).
    /// </summary>
    void ClearCustomSkinLoaderCache(string gameDir);

    /// <summary>
    /// Валидация файла плаща (проверка существования, целостности и допустимых размеров 2:1 от 64x32 до 1024x512, 22x17 или 64x64).
    /// </summary>
    SkinValidationResult ValidateCapeFile(string? filePath) => new SkinValidationResult(false, "Not implemented");

    /// <summary>
    /// Валидация байтов плаща (проверка PNG и допустимых размеров).
    /// </summary>
    SkinValidationResult ValidateCapeBytes(byte[]? bytes) => new SkinValidationResult(false, "Not implemented");

    /// <summary>
    /// Возвращает канонический путь к каталогу профиля данных лаунчера.
    /// </summary>
    string GetProfileDataDir() => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura");

    /// <summary>
    /// Безопасное разрешение пути к элементу истории гардероба с проверкой канонической изоляции.
    /// </summary>
    bool TryResolveWardrobeItemPath(WardrobeItem? item, out string resolvedFullPath)
    {
        resolvedFullPath = string.Empty;
        return false;
    }

    /// <summary>
    /// Загрузка плаща как ImageSource.
    /// </summary>
    ImageSource? LoadCapeImage(string? capePath) => null;

    /// <summary>
    /// Синхронизация файла плаща в каталог CustomSkinLoader игры Minecraft (<gameDir>/CustomSkinLoader/LocalSkin/capes/<nick>.png).
    /// Если capePath пустой/отсутствует, локальный плащ удаляется.
    /// </summary>
    Task SyncCapeToGameAsync(string? capePath, string nickname, string gameDir, CancellationToken cancellationToken = default) => Task.CompletedTask;

    /// <summary>
    /// Сохранение файла в managed storage (.aura/skins или .aura/capes) с атомарной записью, валидацией и SHA-256 дедупликацией.
    /// </summary>
    Task<(string ManagedPath, string Hash)> SaveToManagedStorageAsync(byte[] fileBytes, string type, string originalFileName, CancellationToken cancellationToken = default)
        => Task.FromResult((string.Empty, string.Empty));

    /// <summary>
    /// Сохранение существующего файла в managed storage (.aura/skins или .aura/capes) с атомарным копированием и SHA-256 дедупликацией.
    /// </summary>
    Task<(string ManagedPath, string Hash)> SaveToManagedStorageAsync(string sourceFilePath, string type, string? originalFileName = null, CancellationToken cancellationToken = default)
        => Task.FromResult((string.Empty, string.Empty));

    /// <summary>
    /// Сброс оперативного кэша скинов/аватарок в памяти лаунчера.
    /// </summary>
    void InvalidateMemoryCache(string? nickname = null) { }
}

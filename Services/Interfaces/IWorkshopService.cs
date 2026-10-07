using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface IWorkshopService
{
    Task<List<WorldSaveItem>> GetWorldSavesAsync(string gameDir);
    Task<string> CreateWorldBackupAsync(WorldSaveItem world);
    Task RestoreWorldBackupAsync(WorldSaveItem world, WorldBackupItem backup);
    Task DeleteWorldBackupAsync(WorldBackupItem backup);
    Task<List<ModItem>> GetModsAsync(string gameDir);
    bool ToggleMod(ModItem mod, bool enable);
    Task<List<ShaderPackItem>> GetShaderPacksAsync(string gameDir);
    void SetActiveShaderPack(string gameDir, string shaderFileName);
    Task<List<ScreenshotItem>> GetScreenshotsAsync(string gameDir);
    bool DeleteScreenshot(ScreenshotItem item);
}

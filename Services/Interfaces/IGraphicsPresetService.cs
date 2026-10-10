using System.Collections.Generic;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public class PresetApplyResult
{
    public bool Success { get; set; }
    public string PresetKey { get; set; } = string.Empty;
    public string? Warning { get; set; }
    public int FinalRamMb { get; set; }
    public string ChosenShader { get; set; } = string.Empty;
    public List<string> JvmArgs { get; set; } = new();
    public Dictionary<string, string> WrittenOptions { get; set; } = new();
}

public interface IGraphicsPresetService
{
    IReadOnlyDictionary<string, GraphicsPresetModel> GetPresets();
    GraphicsPresetModel? GetPreset(string presetKey);
    PresetApplyResult ApplyPreset(string gameDir, string presetKey);
}

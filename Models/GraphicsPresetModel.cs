using System;
using System.Collections.Generic;

namespace AuraLauncher.Models;

public class GraphicsPresetModel
{
    public string Name { get; set; } = string.Empty;
    public string Hardware { get; set; } = string.Empty;
    public int RenderDistance { get; set; } = 12;
    public int SimulationDistance { get; set; } = 8;
    public string Graphics { get; set; } = "fancy";
    public string Particles { get; set; } = "decreased";
    public string Clouds { get; set; } = "on";
    public double EntityDistanceScaling { get; set; } = 1.0;
    public int BiomeBlendRadius { get; set; } = 3;
    public int MipmapLevels { get; set; } = 2;
    public int Ao { get; set; } = 2;
    public int RamMb { get; set; } = 6144;
    public List<string> JvmArgs { get; set; } = new();
    public string ShaderPack { get; set; } = "MakeUp-UltraFast-9.5f.zip";
    public bool EnableShaders { get; set; } = true;
    public Dictionary<string, string> ShaderOptions { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public class PresetsContainer
{
    public Dictionary<string, GraphicsPresetModel> Presets { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

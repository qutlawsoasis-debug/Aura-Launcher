using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class GraphicsPresetService : IGraphicsPresetService
{
    private readonly IConfigService _configService;
    private readonly IWorkshopService _workshopService;
    private readonly Dictionary<string, GraphicsPresetModel> _presets = new(StringComparer.OrdinalIgnoreCase);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Auto, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    public GraphicsPresetService(IConfigService configService, IWorkshopService workshopService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _workshopService = workshopService ?? throw new ArgumentNullException(nameof(workshopService));
        LoadPresets();
    }

    private void LoadPresets()
    {
        string? loadedJson = null;

        var searchPaths = new[]
        {
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Data", "presets.json"),
            Path.Combine(Directory.GetCurrentDirectory(), "Data", "presets.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "Data", "presets.json"),
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "..", "..", "..", "..", "Data", "presets.json"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura", "presets.json")
        };

        foreach (var path in searchPaths)
        {
            if (File.Exists(path))
            {
                try
                {
                    loadedJson = File.ReadAllText(path, Encoding.UTF8);
                    break;
                }
                catch { }
            }
        }

        if (!string.IsNullOrWhiteSpace(loadedJson))
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
                var container = JsonSerializer.Deserialize<PresetsContainer>(loadedJson, options);
                if (container?.Presets != null && container.Presets.Count > 0)
                {
                    foreach (var kvp in container.Presets)
                    {
                        _presets[kvp.Key] = kvp.Value;
                    }
                    return;
                }
            }
            catch (Exception ex)
            {
                FabricGameLaunchService.LogLauncherEvent($"[PRESETS: ERROR] Ошибка парсинга presets.json: {ex.Message}");
            }
        }

        PopulateDefaultPresets();
    }

    private void PopulateDefaultPresets()
    {
        _presets["Low"] = new GraphicsPresetModel
        {
            Name = "Слабый ПК",
            Hardware = "RX 560, GTX 1660 и подобные",
            RenderDistance = 12,
            SimulationDistance = 6,
            Graphics = "fast",
            Particles = "decreased",
            Clouds = "off",
            EntityDistanceScaling = 0.75,
            BiomeBlendRadius = 1,
            MipmapLevels = 2,
            Ao = 1,
            RamMb = 6144,
            JvmArgs = new List<string>
            {
                "-XX:+UnlockExperimentalVMOptions",
                "-XX:+UseG1GC",
                "-XX:G1NewSizePercent=20",
                "-XX:G1ReservePercent=20",
                "-XX:MaxGCPauseMillis=50",
                "-XX:G1HeapRegionSize=16M"
            },
            ShaderPack = "MakeUp-UltraFast-9.5f.zip",
            EnableShaders = true,
            ShaderOptions = new Dictionary<string, string>()
        };

        _presets["Medium"] = new GraphicsPresetModel
        {
            Name = "Средний",
            Hardware = "RTX 20",
            RenderDistance = 16,
            SimulationDistance = 8,
            Graphics = "fancy",
            Particles = "decreased",
            Clouds = "on",
            EntityDistanceScaling = 1.0,
            BiomeBlendRadius = 3,
            MipmapLevels = 3,
            Ao = 2,
            RamMb = 8192,
            JvmArgs = new List<string>
            {
                "-XX:+UnlockExperimentalVMOptions",
                "-XX:+UseG1GC",
                "-XX:G1NewSizePercent=20",
                "-XX:G1ReservePercent=20",
                "-XX:MaxGCPauseMillis=50",
                "-XX:G1HeapRegionSize=16M"
            },
            ShaderPack = "BSL_v10.1.8.zip",
            EnableShaders = true,
            ShaderOptions = new Dictionary<string, string>()
        };

        _presets["High"] = new GraphicsPresetModel
        {
            Name = "Высокий",
            Hardware = "RTX 30",
            RenderDistance = 24,
            SimulationDistance = 10,
            Graphics = "fancy",
            Particles = "all",
            Clouds = "on",
            EntityDistanceScaling = 1.25,
            BiomeBlendRadius = 5,
            MipmapLevels = 4,
            Ao = 2,
            RamMb = 8192,
            JvmArgs = new List<string>
            {
                "-XX:+UnlockExperimentalVMOptions",
                "-XX:+UseG1GC",
                "-XX:G1NewSizePercent=20",
                "-XX:G1ReservePercent=20",
                "-XX:MaxGCPauseMillis=50",
                "-XX:G1HeapRegionSize=16M"
            },
            ShaderPack = "photon_v1.3b.zip",
            EnableShaders = true,
            ShaderOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["profile"] = "high",
                ["INFO"] = "2",
                ["shadowMapResolution"] = "2048",
                ["SHADOW_PCF"] = "true",
                ["SHADOW_COLOR"] = "true",
                ["SHADOW_VPS"] = "true",
                ["ENTITY_SHADOWS"] = "true",
                ["BLOCK_ENTITY_SHADOWS"] = "false",
                ["ENVIRONMENT_REFLECTIONS"] = "true",
                ["GTAO"] = "true",
                ["VL"] = "true",
                ["AIR_FOG_COLORED_LIGHT_SHAFTS"] = "false",
                ["WATER_CAUSTICS"] = "false",
                ["WATER_PARALLAX"] = "true",
                ["COLORED_LIGHTS"] = "false",
                ["SHADOW_SSRT"] = "true"
            }
        };

        _presets["Ultra"] = new GraphicsPresetModel
        {
            Name = "Ультра",
            Hardware = "RTX 40 и 50",
            RenderDistance = 32,
            SimulationDistance = 12,
            Graphics = "fancy",
            Particles = "all",
            Clouds = "on",
            EntityDistanceScaling = 1.5,
            BiomeBlendRadius = 7,
            MipmapLevels = 4,
            Ao = 2,
            RamMb = 10240,
            JvmArgs = new List<string>
            {
                "-XX:+UnlockExperimentalVMOptions",
                "-XX:+UseG1GC",
                "-XX:G1NewSizePercent=20",
                "-XX:G1ReservePercent=20",
                "-XX:MaxGCPauseMillis=50",
                "-XX:G1HeapRegionSize=32M"
            },
            ShaderPack = "ComplementaryReimagined_r5.9.3.zip",
            EnableShaders = true,
            ShaderOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["profile"] = "ULTRA"
            }
        };
    }

    public IReadOnlyDictionary<string, GraphicsPresetModel> GetPresets() => _presets;

    public GraphicsPresetModel? GetPreset(string presetKey)
    {
        if (string.IsNullOrWhiteSpace(presetKey)) return null;
        return _presets.TryGetValue(presetKey.Trim(), out var p) ? p : null;
    }

    public static int GetPhysicalRamMb()
    {
        try
        {
            var mem = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
            if (GlobalMemoryStatusEx(ref mem) && mem.ullTotalPhys > 0)
            {
                return (int)(mem.ullTotalPhys / (1024 * 1024));
            }
        }
        catch { }
        return 16384;
    }

    public PresetApplyResult ApplyPreset(string gameDir, string presetKey)
    {
        var result = new PresetApplyResult
        {
            PresetKey = presetKey,
            Success = false
        };

        var preset = GetPreset(presetKey);
        if (preset == null)
        {
            result.Warning = $"Неизвестный пресет графики: '{presetKey}'";
            return result;
        }

        try
        {
            // 1. Ограничение RAM до 60% физической памяти
            int physRamMb = GetPhysicalRamMb();
            int maxAllowedRam = physRamMb > 0 ? (int)(physRamMb * 0.60) : int.MaxValue;
            int finalRam = preset.RamMb;
            if (physRamMb > 0 && finalRam > maxAllowedRam)
            {
                finalRam = Math.Max(2048, maxAllowedRam);
            }
            result.FinalRamMb = finalRam;
            result.JvmArgs = new List<string>(preset.JvmArgs);

            // 2. Определение и проверка шейдерпака
            string chosenShader = "OFF";
            if (preset.EnableShaders)
            {
                string targetShaderPath = Path.Combine(gameDir, "shaderpacks", preset.ShaderPack);
                if (File.Exists(targetShaderPath))
                {
                    chosenShader = preset.ShaderPack;
                }
                else
                {
                    result.Warning = $"Шейдерпак '{preset.ShaderPack}' не найден на диске. Откат на MakeUp-UltraFast-9.5f.zip";
                    FabricGameLaunchService.LogLauncherEvent($"[PRESET: WARN] {result.Warning}");
                    string fallbackPath = Path.Combine(gameDir, "shaderpacks", "MakeUp-UltraFast-9.5f.zip");
                    chosenShader = File.Exists(fallbackPath) ? "MakeUp-UltraFast-9.5f.zip" : "OFF";
                }
            }
            result.ChosenShader = chosenShader;

            // Применение шейдера через WorkshopService
            _workshopService.SetActiveShaderPack(gameDir, chosenShader);

            // 2.1 Запись специфичных настроек шейдера (shaderOptions) в файл настроек Iris
            if (preset.ShaderOptions != null && preset.ShaderOptions.Count > 0 && !string.Equals(chosenShader, "OFF", StringComparison.OrdinalIgnoreCase))
            {
                try
                {
                    var shaderTxtPath = Path.Combine(gameDir, "shaderpacks", $"{chosenShader}.txt");
                    var shaderDir = Path.GetDirectoryName(shaderTxtPath);
                    if (!string.IsNullOrEmpty(shaderDir)) Directory.CreateDirectory(shaderDir);

                    var shaderLines = File.Exists(shaderTxtPath)
                        ? File.ReadAllLines(shaderTxtPath, Encoding.UTF8).ToList()
                        : new List<string>();

                    var updatedKeys = new HashSet<string>(StringComparer.Ordinal);
                    for (int i = 0; i < shaderLines.Count; i++)
                    {
                        var line = shaderLines[i].TrimStart();
                        if (line.StartsWith("#") || line.StartsWith("//")) continue;
                        int eqIdx = shaderLines[i].IndexOf('=');
                        if (eqIdx <= 0) continue;
                        string key = shaderLines[i][..eqIdx].Trim();
                        if (preset.ShaderOptions.TryGetValue(key, out var newVal))
                        {
                            shaderLines[i] = $"{key}={newVal}";
                            updatedKeys.Add(key);
                        }
                    }

                    foreach (var kvp in preset.ShaderOptions)
                    {
                        if (!updatedKeys.Contains(kvp.Key))
                        {
                            shaderLines.Add($"{kvp.Key}={kvp.Value}");
                        }
                    }

                    var tmpShaderTxt = shaderTxtPath + ".tmp";
                    File.WriteAllLines(tmpShaderTxt, shaderLines, new UTF8Encoding(false));
                    File.Move(tmpShaderTxt, shaderTxtPath, overwrite: true);
                }
                catch (Exception ex)
                {
                    FabricGameLaunchService.LogLauncherEvent($"[PRESET: WARN] Не удалось записать shaderOptions для '{chosenShader}': {ex.Message}");
                }
            }

            // 3. Запись настроек в options.txt (ТОЛЬКО ключи пресета)
            Directory.CreateDirectory(gameDir);
            var optionsPath = Path.Combine(gameDir, "options.txt");
            var lines = File.Exists(optionsPath)
                ? File.ReadAllLines(optionsPath, Encoding.UTF8).ToList()
                : new List<string> { "version:3465", "lang:ru_ru", "guiScale:2", "fullscreen:true" };

            string particlesVal = preset.Particles.ToLowerInvariant() switch
            {
                "all" or "0" => "0",
                "minimal" or "2" => "2",
                _ => "1"
            };

            string cloudsVal = preset.Clouds.ToLowerInvariant() switch
            {
                "off" or "false" => "\"false\"",
                "fast" => "\"fast\"",
                _ => "\"true\""
            };

            string graphicsVal = preset.Graphics.Equals("fast", StringComparison.OrdinalIgnoreCase) ? "0" : "1";

            var targetValues = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["renderDistance"] = preset.RenderDistance.ToString(CultureInfo.InvariantCulture),
                ["simulationDistance"] = preset.SimulationDistance.ToString(CultureInfo.InvariantCulture),
                ["graphicsMode"] = graphicsVal,
                ["particles"] = particlesVal,
                ["clouds"] = cloudsVal,
                ["entityDistanceScaling"] = preset.EntityDistanceScaling.ToString("0.##", CultureInfo.InvariantCulture),
                ["biomeBlendRadius"] = preset.BiomeBlendRadius.ToString(CultureInfo.InvariantCulture),
                ["mipmapLevels"] = preset.MipmapLevels.ToString(CultureInfo.InvariantCulture),
                ["ao"] = preset.Ao.ToString(CultureInfo.InvariantCulture)
            };
            result.WrittenOptions = targetValues;

            var appliedKeys = new HashSet<string>(StringComparer.Ordinal);
            for (int i = 0; i < lines.Count; i++)
            {
                int colonIdx = lines[i].IndexOf(':');
                if (colonIdx <= 0) continue;

                string key = lines[i][..colonIdx].Trim();
                if (targetValues.TryGetValue(key, out var newVal))
                {
                    lines[i] = $"{key}:{newVal}";
                    appliedKeys.Add(key);
                }
            }

            foreach (var kvp in targetValues)
            {
                if (!appliedKeys.Contains(kvp.Key))
                {
                    lines.Add($"{kvp.Key}:{kvp.Value}");
                }
            }

            var tmpOptionsPath = optionsPath + ".tmp";
            File.WriteAllLines(tmpOptionsPath, lines, new UTF8Encoding(false));
            File.Move(tmpOptionsPath, optionsPath, overwrite: true);

            // 4. Обновление конфигурации лаунчера (RAM, JvmArgs, GraphicsPreset)
            var config = _configService.CurrentConfig;
            config.RamMb = finalRam;
            config.JvmArgs = string.Join(" ", preset.JvmArgs);
            config.GraphicsPreset = presetKey;
            _configService.SaveConfigAsync(config).ConfigureAwait(false);

            // 5. Логирование в launcher-game.log и launcher.log
            string shaderOptsDesc = preset.ShaderOptions.Count > 0
                ? string.Join(", ", preset.ShaderOptions.Select(kv => $"{kv.Key}={kv.Value}"))
                : "нет";

            string logMessage =
                $"[PRESET] Применён профиль '{presetKey}':\n" +
                $"  options.txt: renderDistance={preset.RenderDistance}, simulationDistance={preset.SimulationDistance}, graphicsMode={graphicsVal}, particles={particlesVal}, clouds={cloudsVal}, entityDistanceScaling={preset.EntityDistanceScaling.ToString("0.##", CultureInfo.InvariantCulture)}, biomeBlendRadius={preset.BiomeBlendRadius}, mipmapLevels={preset.MipmapLevels}, ao={preset.Ao}\n" +
                $"  RAM: {finalRam} MB (исходно: {preset.RamMb} MB, 60% лимит: {(physRamMb > 0 ? $"{maxAllowedRam} MB" : "не определён")})\n" +
                $"  Шейдерпак: {chosenShader} (включены: {preset.EnableShaders}){(result.Warning != null ? $" [ПРЕДУПРЕЖДЕНИЕ: {result.Warning}]" : "")}\n" +
                $"  Опции шейдера: {shaderOptsDesc}\n" +
                $"  JVM-аргументы: {string.Join(" ", preset.JvmArgs)}";

            FabricGameLaunchService.LogGameLaunchEvent(gameDir, logMessage);

            result.Success = true;
        }
        catch (Exception ex)
        {
            result.Warning = $"Ошибка применения пресета '{presetKey}': {ex.Message}";
            FabricGameLaunchService.LogLauncherEvent($"[PRESET: ERROR] {result.Warning}");
        }

        return result;
    }
}

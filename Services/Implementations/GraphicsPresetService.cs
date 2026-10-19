using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Management;
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

                    // Миграция устаревшего/проблемного шейдера DERCODE на стабильный Complementary Unbound ULTRA
                    foreach (var p in _presets.Values)
                    {
                        if (p.ShaderPack.Contains("DERCODE", StringComparison.OrdinalIgnoreCase))
                        {
                            p.ShaderPack = "ComplementaryUnbound_r5.9.3.zip";
                            p.ShaderOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                            {
                                ["profile"] = "ULTRA",
                                ["SHADOW_QUALITY"] = "2",
                                ["shadowDistance"] = "224.0",
                                ["WATER_REFLECT_QUALITY"] = "2",
                                ["BLOCK_REFLECT_QUALITY"] = "3",
                                ["LIGHTSHAFT_QUALI_DEFINE"] = "2",
                                ["SSAO_QUALI_DEFINE"] = "2",
                                ["FXAA_DEFINE"] = "1",
                                ["DETAIL_QUALITY"] = "3",
                                ["CLOUD_QUALITY"] = "3",
                                ["ANISOTROPIC_FILTER"] = "8",
                                ["COLORED_LIGHTING"] = "256",
                                ["WORLD_SPACE_REFLECTIONS"] = "1",
                                ["ENTITY_SHADOW"] = "1",
                                ["RAIN_PUDDLES"] = "2"
                            };
                        }
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
            Hardware = "GTX 1050/1650, RX 550/560, Vega, Intel HD",
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
            ShaderOptions = new Dictionary<string, string>(),
            ResourcePacks = new List<string>
            {
                "Sodium_Russian_Translation.zip",
                "AnimatedDoors-RP-2.0.0.zip",
                "unobtrusive-grass-1.0.1.zip",
                "Drigo-3D-Lanterns-x-Punchy.zip",
                "Modded-Swords-x-Punchy-1.20.1-v1.3.2.zip"
            }
        };

        _presets["Medium"] = new GraphicsPresetModel
        {
            Name = "Средний",
            Hardware = "GTX 1660, RTX 2060, RX 6600",
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
            ShaderOptions = new Dictionary<string, string>(),
            ResourcePacks = new List<string>
            {
                "Sodium_Russian_Translation.zip",
                "AnimatedDoors-RP-2.0.0.zip",
                "unobtrusive-grass-1.0.1.zip",
                "Drigo-3D-Lanterns-x-Punchy.zip",
                "Modded-Swords-x-Punchy-1.20.1-v1.3.2.zip",
                "FreshAnimations_v1.10.4.zip",
                "Better-Leaves-9.6.zip"
            }
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
            ShaderPack = "ComplementaryReimagined_r5.9.3.zip",
            EnableShaders = true,
            ShaderOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["profile"] = "ULTRA",
                ["SHADOW_QUALITY"] = "2",
                ["shadowDistance"] = "224.0",
                ["WATER_REFLECT_QUALITY"] = "2",
                ["BLOCK_REFLECT_QUALITY"] = "3",
                ["LIGHTSHAFT_QUALI_DEFINE"] = "2",
                ["SSAO_QUALI_DEFINE"] = "2",
                ["FXAA_DEFINE"] = "1",
                ["DETAIL_QUALITY"] = "3",
                ["CLOUD_QUALITY"] = "3",
                ["ANISOTROPIC_FILTER"] = "8",
                ["COLORED_LIGHTING"] = "256",
                ["WORLD_SPACE_REFLECTIONS"] = "1",
                ["ENTITY_SHADOW"] = "1",
                ["RAIN_PUDDLES"] = "2"
            },
            ResourcePacks = new List<string>
            {
                "Sodium_Russian_Translation.zip",
                "AnimatedDoors-RP-2.0.0.zip",
                "unobtrusive-grass-1.0.1.zip",
                "Drigo-3D-Lanterns-x-Punchy.zip",
                "Modded-Swords-x-Punchy-1.20.1-v1.3.2.zip",
                "FreshAnimations_v1.10.4.zip",
                "Better-Leaves-9.6.zip"
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
            ShaderPack = "ComplementaryUnbound_r5.9.3.zip",
            EnableShaders = true,
            ShaderOptions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["profile"] = "ULTRA",
                ["SHADOW_QUALITY"] = "2",
                ["shadowDistance"] = "224.0",
                ["WATER_REFLECT_QUALITY"] = "2",
                ["BLOCK_REFLECT_QUALITY"] = "3",
                ["LIGHTSHAFT_QUALI_DEFINE"] = "2",
                ["SSAO_QUALI_DEFINE"] = "2",
                ["FXAA_DEFINE"] = "1",
                ["DETAIL_QUALITY"] = "3",
                ["CLOUD_QUALITY"] = "3",
                ["ANISOTROPIC_FILTER"] = "8",
                ["COLORED_LIGHTING"] = "256",
                ["WORLD_SPACE_REFLECTIONS"] = "1",
                ["ENTITY_SHADOW"] = "1",
                ["RAIN_PUDDLES"] = "2"
            },
            ResourcePacks = new List<string>
            {
                "Sodium_Russian_Translation.zip",
                "AnimatedDoors-RP-2.0.0.zip",
                "unobtrusive-grass-1.0.1.zip",
                "Drigo-3D-Lanterns-x-Punchy.zip",
                "Modded-Swords-x-Punchy-1.20.1-v1.3.2.zip",
                "FreshAnimations_v1.10.4.zip",
                "Better-Leaves-9.6.zip",
                "Patrix_1.20_models.zip",
                "Patrix_1.20_32x_bonus.zip",
                "Patrix_1.20_32x_basic.zip",
                "Patrix_1.20_32x_addon.zip"
            }
        };
    }

    public IReadOnlyDictionary<string, GraphicsPresetModel> GetPresets()
    {
        LoadPresets();
        return _presets;
    }

    public GraphicsPresetModel? GetPreset(string presetKey)
    {
        if (string.IsNullOrWhiteSpace(presetKey)) return null;
        LoadPresets();
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

    public enum HardwareTier
    {
        Low,
        Medium,
        High,
        Ultra
    }

    public static List<string> GetGpuNames()
    {
        var list = new List<string>();
        if (!RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            return list;
        }

        try
        {
            using var searcher = new ManagementObjectSearcher("SELECT Name, Description FROM Win32_VideoController");
            foreach (ManagementObject obj in searcher.Get())
            {
                var name = obj["Name"]?.ToString() ?? obj["Description"]?.ToString();
                if (!string.IsNullOrWhiteSpace(name))
                {
                    list.Add(name.Trim());
                }
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[GPU: ERROR] Ошибка при чтении Win32_VideoController: {ex.Message}");
        }

        return list;
    }

    public static HardwareTier ClassifyGpu(string gpuName)
    {
        if (string.IsNullOrWhiteSpace(gpuName)) return HardwareTier.Low;
        var name = gpuName.ToUpperInvariant();

        if (name.Contains("BASIC DISPLAY") || name.Contains("BASIC RENDER") || name.Contains("VIRTUAL") || name.Contains("REMOTE"))
        {
            return HardwareTier.Low;
        }

        // Ultra: RTX 40xx, RTX 50xx, RX 7900
        if (name.Contains("RTX 40") || name.Contains("RTX 50") || name.Contains("RX 7900"))
        {
            return HardwareTier.Ultra;
        }

        // High: RTX 30xx, RX 6700+, RX 6800+, RX 6900+, RX 7700+, RX 7800+
        if (name.Contains("RTX 30") ||
            name.Contains("RX 6700") || name.Contains("RX 6750") ||
            name.Contains("RX 6800") || name.Contains("RX 6850") ||
            name.Contains("RX 6900") || name.Contains("RX 6950") ||
            name.Contains("RX 7700") || name.Contains("RX 7800"))
        {
            return HardwareTier.High;
        }

        // Medium: GTX 1660, RTX 20xx, RX 6600, RX 7600, GTX 1070/1080, RX 580/590, etc.
        if (name.Contains("1660") ||
            name.Contains("RTX 20") ||
            name.Contains("RX 6600") || name.Contains("RX 6650") ||
            name.Contains("RX 7600") || name.Contains("RX 5600") ||
            name.Contains("RX 5700") || name.Contains("RX 580") ||
            name.Contains("RX 590") || name.Contains("GTX 1070") ||
            name.Contains("GTX 1080") || name.Contains("GTX 1060") ||
            name.Contains("ARC A7") || name.Contains("ARC A5"))
        {
            return HardwareTier.Medium;
        }

        // Low: слабая/интегрированная (Intel HD/UHD/Iris, Vega, RX 550/560, GT/GTX 1050/1650 и т.д.)
        if (name.Contains("INTEL") || name.Contains("HD GRAPHICS") || name.Contains("UHD GRAPHICS") || name.Contains("IRIS") ||
            name.Contains("VEGA") || name.Contains("RX 550") || name.Contains("RX 560") || name.Contains("RX 460") ||
            name.Contains("1050") || name.Contains("1650") || name.Contains("GT ") || name.Contains("GTX 7") ||
            name.Contains("GTX 8") || name.Contains("GTX 9") || name.Contains("MX") || name.Contains("RADEON(TM) GRAPHICS") ||
            name.Contains("R7") || name.Contains("R5") || name.Contains("R9") || name.Contains("QUADRO") || name.Contains("NVS"))
        {
            return HardwareTier.Low;
        }

        // Если не распознано: ни в коем случае не Ultra по умолчанию!
        return HardwareTier.Low;
    }

    public static HardwareTier DetermineHardwareTier(int ramMb, IEnumerable<string>? gpuNames, out string bestGpu)
    {
        bestGpu = "Не определена";
        var list = gpuNames?.Where(g => !string.IsNullOrWhiteSpace(g)).ToList() ?? new List<string>();

        var maxTier = HardwareTier.Low;
        if (list.Count > 0)
        {
            foreach (var gpu in list)
            {
                var tier = ClassifyGpu(gpu);
                if (tier > maxTier || bestGpu == "Не определена")
                {
                    maxTier = tier;
                    bestGpu = gpu;
                }
            }
        }

        // RAM < 8GB (с учетом резерва < 7680 MB) -> строго Low
        if (ramMb < 7680)
        {
            return HardwareTier.Low;
        }

        // Ultra требует >= 15360 MB (16GB) RAM
        if (maxTier == HardwareTier.Ultra && ramMb < 15360)
        {
            return HardwareTier.High;
        }

        // High требует >= 10240 MB RAM
        if (maxTier == HardwareTier.High && ramMb < 10240)
        {
            return HardwareTier.Medium;
        }

        return maxTier;
    }

    public string AutoDetectPreset()
    {
        int ramMb = GetPhysicalRamMb();
        var gpus = GetGpuNames();
        var bestTier = DetermineHardwareTier(ramMb, gpus, out string bestGpu);

        string preset = bestTier switch
        {
            HardwareTier.Ultra => "Ultra",
            HardwareTier.High => "High",
            HardwareTier.Medium => "Medium",
            _ => "Low"
        };

        FabricGameLaunchService.LogLauncherEvent($"[HARDWARE] Детекция железа: RAM={ramMb} MB, GPU='{bestGpu}' -> Рекомендованный пресет: {preset}");
        return preset;
    }

    public PresetApplyResult ApplyPreset(string gameDir, string presetKey, bool persistConfig = true)
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

            var rpDir = Path.Combine(gameDir, "resourcepacks");
            var activePacks = new List<string> { "\"vanilla\"", "\"fabric\"" };

            // Сохраняем встроенные модовые паки (например, moonlight:merged_pack, punchy:punchy, serilum_mod_translations)
            int existingRpIdx = lines.FindIndex(l => l.StartsWith("resourcePacks:", StringComparison.Ordinal));
            if (existingRpIdx >= 0)
            {
                var existingList = FabricGameLaunchService.ParseOptionsList(lines[existingRpIdx]);
                foreach (var p in existingList)
                {
                    if (!p.StartsWith("file/", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(p, "vanilla", StringComparison.OrdinalIgnoreCase) &&
                        !string.Equals(p, "fabric", StringComparison.OrdinalIgnoreCase))
                    {
                        var esc = p.Replace("\\", "\\\\").Replace("\"", "\\\"");
                        activePacks.Add($"\"{esc}\"");
                    }
                }
            }

            IEnumerable<string> candidatePacks = (preset.ResourcePacks != null && preset.ResourcePacks.Count > 0)
                ? preset.ResourcePacks
                : (presetKey.Equals("Ultra", StringComparison.OrdinalIgnoreCase)
                    ? new[]
                    {
                        "Sodium_Russian_Translation.zip",
                        "AnimatedDoors-RP-2.0.0.zip",
                        "unobtrusive-grass-1.0.1.zip",
                        "Drigo-3D-Lanterns-x-Punchy.zip",
                        "Modded-Swords-x-Punchy-1.20.1-v1.3.2.zip",
                        "FreshAnimations_v1.10.4.zip",
                        "Better-Leaves-9.6.zip",
                        "Patrix_1.20_models.zip",
                        "Patrix_1.20_32x_bonus.zip",
                        "Patrix_1.20_32x_basic.zip",
                        "Patrix_1.20_32x_addon.zip"
                    }
                    : new[]
                    {
                        "Sodium_Russian_Translation.zip",
                        "AnimatedDoors-RP-2.0.0.zip",
                        "unobtrusive-grass-1.0.1.zip",
                        "Drigo-3D-Lanterns-x-Punchy.zip",
                        "Modded-Swords-x-Punchy-1.20.1-v1.3.2.zip"
                    });

            foreach (var cp in candidatePacks)
            {
                if (File.Exists(Path.Combine(rpDir, cp)))
                {
                    activePacks.Add($"\"file/{cp}\"");
                }
            }

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
                ["ao"] = preset.Ao.ToString(CultureInfo.InvariantCulture),
                ["resourcePacks"] = $"[{string.Join(",", activePacks)}]"
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
            if (persistConfig)
            {
                var config = _configService.CurrentConfig;
                config.RamMb = finalRam;
                config.JvmArgs = string.Join(" ", preset.JvmArgs);
                config.GraphicsPreset = presetKey;
                _configService.SaveConfigAsync(config).ConfigureAwait(false);
            }

            // 5. Логирование в launcher-game.log и launcher.log
            string shaderOptsDesc = (preset.ShaderOptions?.Count > 0)
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

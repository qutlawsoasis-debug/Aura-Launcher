using System;
using System.IO;

namespace AuraLauncher.Models;

/// <summary>
/// Доменная модель конфигурации пользователя и параметров запуска.
/// </summary>
public class LauncherConfig
{
    public const string DefaultPackRepo = "qutlawsoasis-debug/Aura-Pack";

    public string Nickname { get; set; } = "Player";
    public int RamMb { get; set; } = 6144;
    public string SkinPath { get; set; } = "";
    public string SkinOriginalName { get; set; } = "";
    public string GameDir { get; set; } = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura");
    public string PackRepo { get; set; } = DefaultPackRepo;
    public DateTime? LastUpdateUtc { get; set; }
    public string? QuickPlayMultiplayer { get; set; }
    public string LobbyApiBaseUrl { get; set; } = "https://lobby-api.vercel.app";
    public string SkinOwnerToken { get; set; } = "";
    public string SkinModel { get; set; } = "default";
    public int AnthemVolume { get; set; } = 100;
    public bool AnthemMuted { get; set; } = false;
    public string? AnthemCustomPath { get; set; }
}

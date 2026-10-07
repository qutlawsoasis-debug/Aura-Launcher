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
    public bool MusicOnStartup { get; set; } = true;
    public bool HideLauncherWhilePlaying { get; set; } = true;
    public string? UserId { get; set; }
    public string? UserTokenEncrypted { get; set; }
    public string? FriendCode { get; set; }
    public bool AutoConnectOnInviteAccept { get; set; } = true;
    public bool WindowsNotificationsEnabled { get; set; } = true;
    public string? DiscordAppId { get; set; } = "1556968494673690674";
    public bool DiscordRpcEnabled { get; set; } = true;
    public long TotalPlayTimeSeconds { get; set; } = 0;
    public int TotalGameLaunches { get; set; } = 0;
    public DateTime? LastPlayedUtc { get; set; }
    public string CurrentMusicTrack { get; set; } = "Aura Cyberpunk Anthem";
    public string WindowDisplayState { get; set; } = "Maximized";
}

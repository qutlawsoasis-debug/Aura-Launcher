using System.Collections.Generic;
using System.Text.Json.Serialization;
using System.Windows.Input;

namespace AuraLauncher.Models;

public class ModManifestEntry
{
    [JsonPropertyName("id")]
    public string Id { get; set; } = "";

    [JsonPropertyName("name")]
    public string Name { get; set; } = "";

    [JsonPropertyName("version")]
    public string Version { get; set; } = "";

    [JsonPropertyName("enabled")]
    public bool Enabled { get; set; }
}

public class ModMismatchItem
{
    [JsonPropertyName("modId")]
    public string ModId { get; set; } = "";

    [JsonPropertyName("modName")]
    public string ModName { get; set; } = "";

    [JsonPropertyName("type")]
    public string Type { get; set; } = ""; // "disabled", "missing", "extra", "version"

    [JsonPropertyName("typeRu")]
    public string TypeRu { get; set; } = "";

    [JsonPropertyName("version")]
    public string? Version { get; set; }

    [JsonPropertyName("playerVersion")]
    public string? PlayerVersion { get; set; }

    [JsonIgnore]
    public string? GuestVersion { get => PlayerVersion; set => PlayerVersion = value; }

    [JsonPropertyName("hostVersion")]
    public string? HostVersion { get; set; }

    [JsonIgnore]
    public string DisplayDescription => Type switch
    {
        "disabled" => "выключен у вас",
        "missing" => "нет у вас",
        "extra" => "лишний у вас",
        "version" => !string.IsNullOrWhiteSpace(PlayerVersion) && !string.IsNullOrWhiteSpace(HostVersion)
            ? $"версия {PlayerVersion} у вас, {HostVersion} у хоста"
            : (!string.IsNullOrWhiteSpace(Version) ? $"версия {Version}" : TypeRu),
        _ => !string.IsNullOrWhiteSpace(TypeRu) ? TypeRu : Type
    };

    [JsonIgnore]
    public bool IsFixable => Type == "disabled" || Type == "extra";
}

public class PlayerModSyncInfo
{
    [JsonPropertyName("hash")]
    public string? Hash { get; set; }

    [JsonPropertyName("status")]
    public string Status { get; set; } = "unverified"; // "synced", "mismatch", "unverified"

    [JsonPropertyName("mismatches")]
    public List<ModMismatchItem> Mismatches { get; set; } = new();
}

public record LobbyCreateResponse(string Code, string HostToken, string Status, long CreatedAt);
public record LobbyJoinResponse(bool Success, string Code, string Status, string? TunnelAddress, int PlayerCount);
public record LobbyStatusResponse(
    string Code,
    string Status,
    string? TunnelAddress,
    int PlayerCount,
    long LastHeartbeat,
    string[]? Players = null,
    string? HostName = null,
    [property: JsonPropertyName("modSync")] Dictionary<string, PlayerModSyncInfo>? ModSync = null);
public record TunnelConfigResponse(string? Secret, string? PublicAddress, int? PublicPort);

public class LobbyPlayerItem : System.ComponentModel.INotifyPropertyChanged
{
    private string _nickname = "";
    public string Nickname
    {
        get => _nickname;
        set { if (_nickname != value) { _nickname = value; OnPropertyChanged(nameof(Nickname)); } }
    }

    private bool _isHost;
    public bool IsHost
    {
        get => _isHost;
        set { if (_isHost != value) { _isHost = value; OnPropertyChanged(nameof(IsHost)); } }
    }

    private System.Windows.Media.ImageSource? _avatar;
    public System.Windows.Media.ImageSource? Avatar
    {
        get => _avatar;
        set { if (_avatar != value) { _avatar = value; OnPropertyChanged(nameof(Avatar)); } }
    }

    private bool _isNewlyAdded;
    public bool IsNewlyAdded
    {
        get => _isNewlyAdded;
        set { if (_isNewlyAdded != value) { _isNewlyAdded = value; OnPropertyChanged(nameof(IsNewlyAdded)); } }
    }

    private string _modSyncStatus = "unverified"; // "synced", "mismatch", "unverified"
    public string ModSyncStatus
    {
        get => _modSyncStatus;
        set
        {
            if (_modSyncStatus != value)
            {
                _modSyncStatus = value;
                OnPropertyChanged(nameof(ModSyncStatus));
                OnPropertyChanged(nameof(ModSyncBadgeText));
                OnPropertyChanged(nameof(IsModSyncSynced));
                OnPropertyChanged(nameof(IsModSyncMismatch));
                OnPropertyChanged(nameof(IsModSyncUnverified));
            }
        }
    }

    private int _mismatchCount;
    public int MismatchCount
    {
        get => _mismatchCount;
        set
        {
            if (_mismatchCount != value)
            {
                _mismatchCount = value;
                OnPropertyChanged(nameof(MismatchCount));
                OnPropertyChanged(nameof(ModSyncBadgeText));
            }
        }
    }

    private string _modSyncTooltip = "";
    public string ModSyncTooltip
    {
        get => _modSyncTooltip;
        set
        {
            if (_modSyncTooltip != value)
            {
                _modSyncTooltip = value;
                OnPropertyChanged(nameof(ModSyncTooltip));
            }
        }
    }

    private List<ModMismatchItem> _mismatches = new();
    public List<ModMismatchItem> Mismatches
    {
        get => _mismatches;
        set
        {
            if (_mismatches != value)
            {
                _mismatches = value;
                OnPropertyChanged(nameof(Mismatches));
            }
        }
    }

    public string ModSyncBadgeText => ModSyncStatus switch
    {
        "synced" => "Моды совпадают",
        "mismatch" => $"Расходятся: {MismatchCount}",
        _ => "Не проверен"
    };

    public bool IsModSyncSynced => ModSyncStatus == "synced";
    public bool IsModSyncMismatch => ModSyncStatus == "mismatch";
    public bool IsModSyncUnverified => ModSyncStatus == "unverified";

    public ICommand? OpenMismatchDialogCommand { get; set; }

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}

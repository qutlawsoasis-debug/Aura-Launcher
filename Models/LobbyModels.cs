namespace AuraLauncher.Models;

public record LobbyCreateResponse(string Code, string HostToken, string Status, long CreatedAt);
public record LobbyJoinResponse(bool Success, string Code, string Status, string? TunnelAddress, int PlayerCount);
public record LobbyStatusResponse(string Code, string Status, string? TunnelAddress, int PlayerCount, long LastHeartbeat, string[]? Players = null, string? HostName = null);
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

    public event System.ComponentModel.PropertyChangedEventHandler? PropertyChanged;
    protected virtual void OnPropertyChanged(string propertyName)
    {
        PropertyChanged?.Invoke(this, new System.ComponentModel.PropertyChangedEventArgs(propertyName));
    }
}

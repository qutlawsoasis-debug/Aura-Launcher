using AuraLauncher.Core;

namespace AuraLauncher.Models;

public class ModItem : ObservableObject
{
    private string _fileName = string.Empty;
    private string _displayName = string.Empty;
    private string _fullPath = string.Empty;
    private string _sizeFormatted = string.Empty;
    private string _version = string.Empty;
    private bool _isEnabled;

    public string FileName
    {
        get => _fileName;
        set => SetProperty(ref _fileName, value);
    }

    public string DisplayName
    {
        get => _displayName;
        set => SetProperty(ref _displayName, value);
    }

    public string FullPath
    {
        get => _fullPath;
        set => SetProperty(ref _fullPath, value);
    }

    public string SizeFormatted
    {
        get => _sizeFormatted;
        set
        {
            if (SetProperty(ref _sizeFormatted, value))
            {
                OnPropertyChanged(nameof(VersionOrSize));
            }
        }
    }

    public string Version
    {
        get => _version;
        set
        {
            if (SetProperty(ref _version, value))
            {
                OnPropertyChanged(nameof(VersionOrSize));
            }
        }
    }

    public string VersionOrSize => !string.IsNullOrWhiteSpace(Version) ? Version : SizeFormatted;

    public bool IsEnabled
    {
        get => _isEnabled;
        set => SetProperty(ref _isEnabled, value);
    }
}

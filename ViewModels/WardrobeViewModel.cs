using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Media;
using Microsoft.Win32;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.ViewModels;

/// <summary>
/// ViewModel гардероба (выбор и 2D плоский предпросмотр скина персонажа: спереди и сзади).
/// Не хранит собственных копий: источником правды для ника и путей является IConfigService.
/// </summary>
public class WardrobeViewModel : ObservableObject
{
    private readonly ISkinService _skinService;
    private readonly IConfigService _configService;

    private ImageSource? _skinFrontPreview;
    private ImageSource? _skinBackPreview;
    private string _formatDescription = "Классический скин Стива (64×64)";
    private string _statusMessage = string.Empty;
    private bool _isStatusError;

    private static readonly SolidColorBrush SuccessBrush = new(Color.FromRgb(0x5F, 0xD3, 0x9A));
    private static readonly SolidColorBrush ErrorBrush = new(Color.FromRgb(0xFF, 0x8A, 0x7A));

    static WardrobeViewModel()
    {
        SuccessBrush.Freeze();
        ErrorBrush.Freeze();
    }

    public event Action? CustomSkinApplied;

    public string Nickname => _configService.CurrentConfig.Nickname;

    public string CopySkinToPersistentStorage(string sourcePath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(sourcePath) || !File.Exists(sourcePath))
                return sourcePath;

            var baseDir = _skinService?.GetProfileDataDir() ?? _configService?.ProfileDataDir ?? Environment.GetEnvironmentVariable("AURA_PROFILE_DIR") ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), ".aura");
            var skinsDir = Path.Combine(baseDir, "skins");
            Directory.CreateDirectory(skinsDir);

            var fileName = Path.GetFileName(sourcePath);
            if (string.IsNullOrWhiteSpace(fileName))
                fileName = "skin.png";

            var targetPath = Path.Combine(skinsDir, fileName);
            if (!string.Equals(Path.GetFullPath(sourcePath), Path.GetFullPath(targetPath), StringComparison.OrdinalIgnoreCase))
            {
                File.Copy(sourcePath, targetPath, overwrite: true);
            }
            return targetPath;
        }
        catch
        {
            return sourcePath;
        }
    }

    public string SkinPath
    {
        get => _configService.CurrentConfig.SkinPath;
        set
        {
            var resolved = !string.IsNullOrWhiteSpace(value) && File.Exists(value)
                ? CopySkinToPersistentStorage(value)
                : (value ?? string.Empty);

            if (_configService.CurrentConfig.SkinPath != resolved)
            {
                _configService.CurrentConfig.SkinPath = resolved;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SkinDisplayName));
                _ = _configService.SaveConfigAsync(_configService.CurrentConfig);
                UpdateSkinPreviews();
                if (!string.IsNullOrWhiteSpace(resolved) && File.Exists(resolved))
                {
                    CustomSkinApplied?.Invoke();
                }
            }
        }
    }

    public string SkinOriginalName
    {
        get => _configService.CurrentConfig.SkinOriginalName;
        set
        {
            if (_configService.CurrentConfig.SkinOriginalName != value)
            {
                _configService.CurrentConfig.SkinOriginalName = value ?? string.Empty;
                OnPropertyChanged();
                OnPropertyChanged(nameof(SkinDisplayName));
                _ = _configService.SaveConfigAsync(_configService.CurrentConfig);
            }
        }
    }

    public string SkinDisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(_configService.CurrentConfig.SkinOriginalName))
                return _configService.CurrentConfig.SkinOriginalName;

            return "Стандартный";
        }
    }

    public ImageSource? SkinFrontPreview
    {
        get => _skinFrontPreview;
        set => SetProperty(ref _skinFrontPreview, value);
    }

    public ImageSource? SkinBackPreview
    {
        get => _skinBackPreview;
        set => SetProperty(ref _skinBackPreview, value);
    }

    public string FormatDescription
    {
        get => _formatDescription;
        set => SetProperty(ref _formatDescription, value);
    }

    public string StatusMessage
    {
        get => _statusMessage;
        set
        {
            if (SetProperty(ref _statusMessage, value))
            {
                OnPropertyChanged(nameof(HasStatusMessage));
            }
        }
    }

    public bool IsStatusError
    {
        get => _isStatusError;
        set
        {
            if (SetProperty(ref _isStatusError, value))
            {
                OnPropertyChanged(nameof(StatusBrush));
            }
        }
    }

    public bool HasStatusMessage => !string.IsNullOrWhiteSpace(_statusMessage);

    public SolidColorBrush StatusBrush => _isStatusError ? ErrorBrush : SuccessBrush;

    private readonly IFriendService? _friendService;
    private string _nicknameInput = string.Empty;
    private string _nicknameErrorText = string.Empty;
    private bool _isProfileIdCopied;

    public string NicknameInput
    {
        get => _nicknameInput;
        set
        {
            if (SetProperty(ref _nicknameInput, value ?? string.Empty))
            {
                ValidateNicknameInput(value);
            }
        }
    }

    public string NicknameErrorText
    {
        get => _nicknameErrorText;
        private set
        {
            if (SetProperty(ref _nicknameErrorText, value))
            {
                OnPropertyChanged(nameof(HasNicknameError));
            }
        }
    }

    public bool HasNicknameError => !string.IsNullOrWhiteSpace(_nicknameErrorText);

    public string ProfileId => _friendService?.CurrentFriendCode ?? _configService.CurrentConfig.FriendCode ?? "--------";

    public bool IsProfileIdCopied
    {
        get => _isProfileIdCopied;
        private set
        {
            if (SetProperty(ref _isProfileIdCopied, value))
            {
                OnPropertyChanged(nameof(CopyProfileIdText));
            }
        }
    }

    public string CopyProfileIdText => IsProfileIdCopied ? "Скопировано!" : "Копировать";

    public RelayCommand CopyProfileIdCommand { get; }
    public RelayCommand SaveNicknameCommand { get; }

    public bool IsSlimModel
    {
        get => _configService.CurrentConfig.SkinModel == "slim";
        set
        {
            var newModel = value ? "slim" : "default";
            if (_configService.CurrentConfig.SkinModel != newModel)
            {
                _configService.CurrentConfig.SkinModel = newModel;
                OnPropertyChanged();
                _ = _configService.SaveConfigAsync(_configService.CurrentConfig);
                _ = UploadCurrentSkinAsync();
            }
        }
    }

    public RelayCommand SelectSkinCommand { get; }
    public RelayCommand ResetSkinCommand { get; }
    public RelayCommand SelectCapeCommand { get; }
    public RelayCommand SelectCyanCapeCommand { get; }
    public RelayCommand SelectSilverCapeCommand { get; }
    public RelayCommand RemoveCapeCommand { get; }
    public RelayCommand ApplyHistoryItemCommand { get; }
    public RelayCommand DeleteHistoryItemCommand { get; }

    public System.Collections.ObjectModel.ObservableCollection<WardrobeItem> HistoryItems { get; } = new();

    public string CapeId
    {
        get => _configService.CurrentConfig.CapeId;
        set
        {
            if (_configService.CurrentConfig.CapeId != value)
            {
                _configService.CurrentConfig.CapeId = value ?? "none";
                OnPropertyChanged();
                OnPropertyChanged(nameof(CapeDisplayName));
                OnPropertyChanged(nameof(IsCapeCyan));
                OnPropertyChanged(nameof(IsCapeSilver));
                OnPropertyChanged(nameof(IsCapeCustom));
                OnPropertyChanged(nameof(IsCapeNone));
                _ = _configService.SaveConfigAsync(_configService.CurrentConfig);
            }
        }
    }

    public string CapePath => _configService.CurrentConfig.CapePath;

    public string CapeDisplayName
    {
        get
        {
            var id = _configService.CurrentConfig.CapeId;
            if (id == "cyan") return "Плащ Aura";
            if (id == "silver") return "Серебряный плащ";
            if (id == "custom")
            {
                return !string.IsNullOrWhiteSpace(_configService.CurrentConfig.CapeOriginalName)
                    ? _configService.CurrentConfig.CapeOriginalName
                    : "Свой плащ";
            }
            return "Без плаща";
        }
    }

    public bool IsCapeCyan => CapeId == "cyan";
    public bool IsCapeSilver => CapeId == "silver";
    public bool IsCapeCustom => CapeId == "custom";
    public bool IsCapeNone => string.IsNullOrWhiteSpace(CapeId) || CapeId == "none";

    public WardrobeViewModel(ISkinService skinService, IConfigService configService, IFriendService? friendService = null)
    {
        _skinService = skinService ?? throw new ArgumentNullException(nameof(skinService));
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _friendService = friendService;

        _nicknameInput = _configService.CurrentConfig.Nickname ?? "Player";

        SelectSkinCommand = new RelayCommand(_ => SelectSkinFile());
        ResetSkinCommand = new RelayCommand(async _ => await ResetSkinToDefaultAsync());
        CopyProfileIdCommand = new RelayCommand(_ => CopyProfileId());
        SaveNicknameCommand = new RelayCommand(async _ => await ApplyNicknameChangeAsync());

        SelectCapeCommand = new RelayCommand(_ => SelectCapeFile());
        SelectCyanCapeCommand = new RelayCommand(async _ => await SelectBuiltInCapeAsync("cyan"));
        SelectSilverCapeCommand = new RelayCommand(async _ => await SelectBuiltInCapeAsync("silver"));
        RemoveCapeCommand = new RelayCommand(async _ => await RemoveCapeAsync());
        ApplyHistoryItemCommand = new RelayCommand(async item => { if (item is WardrobeItem wi) await ApplyHistoryItemAsync(wi); });
        DeleteHistoryItemCommand = new RelayCommand(async item => { if (item is WardrobeItem wi) await DeleteHistoryItemAsync(wi); });

        UpdateSkinPreviews();
        RefreshHistory();

        _configService.ConfigChanged += (s, cfg) =>
        {
            OnPropertyChanged(nameof(Nickname));
            if (_nicknameInput != cfg.Nickname && !HasNicknameError)
            {
                _nicknameInput = cfg.Nickname;
                OnPropertyChanged(nameof(NicknameInput));
            }
            OnPropertyChanged(nameof(ProfileId));
            OnPropertyChanged(nameof(SkinPath));
            OnPropertyChanged(nameof(IsSlimModel));
            OnPropertyChanged(nameof(CapeId));
            OnPropertyChanged(nameof(CapePath));
            OnPropertyChanged(nameof(CapeDisplayName));
            OnPropertyChanged(nameof(IsCapeCyan));
            OnPropertyChanged(nameof(IsCapeSilver));
            OnPropertyChanged(nameof(IsCapeCustom));
            OnPropertyChanged(nameof(IsCapeNone));
            UpdateSkinPreviews();
            RefreshHistory();
        };
    }

    private void ValidateNicknameInput(string? val)
    {
        var result = NicknameValidator.Validate(val);
        if (result.IsValid)
        {
            NicknameErrorText = string.Empty;
        }
        else
        {
            NicknameErrorText = "Ник: от 3 до 16 символов, латиница, цифры и _.";
        }
    }

    public async Task ApplyNicknameChangeAsync()
    {
        var clean = (_nicknameInput ?? string.Empty).Trim();
        var validation = NicknameValidator.Validate(clean);
        if (!validation.IsValid)
        {
            NicknameErrorText = "Ник: от 3 до 16 символов, латиница, цифры и _.";
            return;
        }

        var oldNick = _configService.CurrentConfig.Nickname;
        if (string.Equals(oldNick, clean, StringComparison.Ordinal))
        {
            NicknameErrorText = string.Empty;
            return;
        }

        if (_friendService != null)
        {
            var res = await _friendService.ChangeNicknameAsync(clean);
            if (!res.Success)
            {
                NicknameErrorText = res.ErrorMessage ?? "Ошибка смены ника";
                return;
            }
        }

        NicknameErrorText = string.Empty;
        _configService.CurrentConfig.Nickname = clean;
        await _configService.SaveConfigAsync(_configService.CurrentConfig);

        var cfg = _configService.CurrentConfig;
        if (!string.IsNullOrWhiteSpace(oldNick) && !string.Equals(oldNick, clean, StringComparison.OrdinalIgnoreCase))
        {
            _skinService.CleanupPlayerGameFiles(oldNick, cfg.GameDir);
        }

        await _skinService.SyncSkinToGameAsync(cfg.SkinPath, clean, cfg.GameDir);
        string? capeToSync = (string.Equals(cfg.CapeId, "custom", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(cfg.CapePath) && File.Exists(cfg.CapePath))
            ? cfg.CapePath
            : null;
        await _skinService.SyncCapeToGameAsync(capeToSync, clean, cfg.GameDir);
        _skinService.EnsureCustomSkinLoaderConfig(cfg.GameDir);
        _skinService.ClearCustomSkinLoaderCache(cfg.GameDir);

        OnPropertyChanged(nameof(Nickname));
    }

    private void CopyProfileId()
    {
        var code = ProfileId;
        if (string.IsNullOrWhiteSpace(code) || code.Contains('-')) return;

        try
        {
            System.Windows.Clipboard.SetText(code);
            IsProfileIdCopied = true;
            _ = Task.Delay(1600).ContinueWith(_ =>
            {
                System.Windows.Application.Current?.Dispatcher?.InvokeAsync(() => IsProfileIdCopied = false);
            });
        }
        catch { }
    }

    public async void SelectSkinFile()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Выберите скин Minecraft (.png)",
            Filter = "Скины Minecraft (*.png)|*.png|Все файлы (*.*)|*.*",
            Multiselect = false
        };

        if (ofd.ShowDialog() == true)
        {
            var validation = _skinService.ValidateSkinFile(ofd.FileName);
            if (!validation.IsValid)
            {
                IsStatusError = true;
                StatusMessage = validation.ErrorMessage ?? "Неподдерживаемый файл скина.";
                return;
            }

            IsStatusError = false;
            string originalName = Path.GetFileName(ofd.FileName);

            // Сохраняем в managed storage профиля с SHA-256 дедупликацией
            string managedPath;
            string hash;
            try
            {
                (managedPath, hash) = await _skinService.SaveToManagedStorageAsync(ofd.FileName, "skin", originalName);
            }
            catch (Exception ex)
            {
                IsStatusError = true;
                StatusMessage = $"Ошибка сохранения скина: {ex.Message}";
                return;
            }

            bool isSlim = SkinService.DetectIsSlim(managedPath);
            string detectedModel = isSlim ? "slim" : "default";

            SkinPath = managedPath;
            SkinOriginalName = originalName;
            FormatDescription = validation.Height == 32
                ? "Классический скин Minecraft (64×32)"
                : "Современный скин Minecraft (64×64)";

            // Обновляем историю гардероба
            var cfg = _configService.CurrentConfig;
            cfg.SkinModel = detectedModel;
            cfg.WardrobeHistory ??= new System.Collections.Generic.List<WardrobeItem>();
            string itemId = $"skin_{hash}";
            cfg.WardrobeHistory.RemoveAll(x => string.Equals(x.Hash, hash, StringComparison.OrdinalIgnoreCase) && x.Type == "skin");
            cfg.WardrobeHistory.Insert(0, new WardrobeItem
            {
                Id = itemId,
                Type = "skin",
                Name = originalName,
                Hash = hash,
                Model = detectedModel,
                RelativePath = Path.Combine("skins", $"{hash}.png").Replace('\\', '/'),
                CreatedUtc = DateTime.UtcNow
            });
            await _configService.SaveConfigAsync(cfg);
            OnPropertyChanged(nameof(IsSlimModel));
            RefreshHistory();

            // Синхронизируем с игрой через единый путь config.GameDir
            var mcDir = _configService.CurrentConfig.GameDir;
            _ = _skinService.SyncSkinToGameAsync(SkinPath, Nickname, mcDir);
            _skinService.EnsureCustomSkinLoaderConfig(mcDir);

            // Загружаем в lobby-api
            _ = UploadCurrentSkinAsync();
        }
    }

    public async void SelectCapeFile()
    {
        var ofd = new OpenFileDialog
        {
            Title = "Выберите плащ Minecraft (.png)",
            Filter = "Плащи Minecraft (*.png)|*.png|Все файлы (*.*)|*.*",
            Multiselect = false
        };

        if (ofd.ShowDialog() == true)
        {
            var validation = _skinService.ValidateCapeFile(ofd.FileName);
            if (!validation.IsValid)
            {
                IsStatusError = true;
                StatusMessage = validation.ErrorMessage ?? "Неподдерживаемый файл плаща.";
                return;
            }

            IsStatusError = false;
            string originalName = Path.GetFileName(ofd.FileName);

            string managedPath;
            string hash;
            try
            {
                (managedPath, hash) = await _skinService.SaveToManagedStorageAsync(ofd.FileName, "cape", originalName);
            }
            catch (Exception ex)
            {
                IsStatusError = true;
                StatusMessage = $"Ошибка сохранения плаща: {ex.Message}";
                return;
            }

            var cfg = _configService.CurrentConfig;
            cfg.CapeId = "custom";
            cfg.CapePath = managedPath;
            cfg.CapeOriginalName = originalName;

            cfg.WardrobeHistory ??= new System.Collections.Generic.List<WardrobeItem>();
            string itemId = $"cape_{hash}";
            cfg.WardrobeHistory.RemoveAll(x => string.Equals(x.Hash, hash, StringComparison.OrdinalIgnoreCase) && x.Type == "cape");
            cfg.WardrobeHistory.Insert(0, new WardrobeItem
            {
                Id = itemId,
                Type = "cape",
                Name = originalName,
                Hash = hash,
                RelativePath = Path.Combine("capes", $"{hash}.png").Replace('\\', '/'),
                CreatedUtc = DateTime.UtcNow
            });
            await _configService.SaveConfigAsync(cfg);

            OnPropertyChanged(nameof(CapeId));
            OnPropertyChanged(nameof(CapePath));
            OnPropertyChanged(nameof(CapeDisplayName));
            OnPropertyChanged(nameof(IsCapeCustom));
            OnPropertyChanged(nameof(IsCapeNone));
            OnPropertyChanged(nameof(IsCapeCyan));
            OnPropertyChanged(nameof(IsCapeSilver));
            RefreshHistory();

            await _skinService.SyncCapeToGameAsync(managedPath, Nickname, cfg.GameDir);
            _skinService.EnsureCustomSkinLoaderConfig(cfg.GameDir);
            _ = SetLobbyApiCapeAsync("none");

            IsStatusError = false;
            StatusMessage = "Плащ применён (виден вам в игре).";
            ScheduleStatusMessageClear();
        }
    }

    public async Task SelectBuiltInCapeAsync(string cape)
    {
        if (cape != "cyan" && cape != "silver") return;

        var cfg = _configService.CurrentConfig;
        cfg.CapeId = cape;
        cfg.CapePath = "";
        cfg.CapeOriginalName = "";
        await _configService.SaveConfigAsync(cfg);

        OnPropertyChanged(nameof(CapeId));
        OnPropertyChanged(nameof(CapePath));
        OnPropertyChanged(nameof(CapeDisplayName));
        OnPropertyChanged(nameof(IsCapeCyan));
        OnPropertyChanged(nameof(IsCapeSilver));
        OnPropertyChanged(nameof(IsCapeCustom));
        OnPropertyChanged(nameof(IsCapeNone));

        await _skinService.SyncCapeToGameAsync(null, Nickname, cfg.GameDir);
        _skinService.EnsureCustomSkinLoaderConfig(cfg.GameDir);
        await SetLobbyApiCapeAsync(cape);

        IsStatusError = false;
        StatusMessage = cape == "cyan" ? "Плащ Aura надет." : "Серебряный плащ надет.";
        ScheduleStatusMessageClear();
    }

    public async Task RemoveCapeAsync()
    {
        var cfg = _configService.CurrentConfig;
        cfg.CapeId = "none";
        cfg.CapePath = "";
        cfg.CapeOriginalName = "";
        await _configService.SaveConfigAsync(cfg);

        OnPropertyChanged(nameof(CapeId));
        OnPropertyChanged(nameof(CapePath));
        OnPropertyChanged(nameof(CapeDisplayName));
        OnPropertyChanged(nameof(IsCapeCyan));
        OnPropertyChanged(nameof(IsCapeSilver));
        OnPropertyChanged(nameof(IsCapeCustom));
        OnPropertyChanged(nameof(IsCapeNone));

        await _skinService.SyncCapeToGameAsync(null, Nickname, cfg.GameDir);
        _skinService.EnsureCustomSkinLoaderConfig(cfg.GameDir);
        await SetLobbyApiCapeAsync("none");

        IsStatusError = false;
        StatusMessage = "Плащ снят.";
        ScheduleStatusMessageClear();
    }

    public async Task ApplyHistoryItemAsync(WardrobeItem item)
    {
        if (item == null) return;

        if (_skinService == null || !_skinService.TryResolveWardrobeItemPath(item, out string fullPath))
        {
            IsStatusError = true;
            StatusMessage = "Файл не найден в хранилище профиля.";
            RefreshHistory();
            return;
        }

        if (item.Type == "cape")
        {
            var cfg = _configService.CurrentConfig;
            cfg.CapeId = "custom";
            cfg.CapePath = fullPath;
            cfg.CapeOriginalName = item.Name;
            await _configService.SaveConfigAsync(cfg);

            OnPropertyChanged(nameof(CapeId));
            OnPropertyChanged(nameof(CapePath));
            OnPropertyChanged(nameof(CapeDisplayName));
            OnPropertyChanged(nameof(IsCapeCustom));
            OnPropertyChanged(nameof(IsCapeNone));
            OnPropertyChanged(nameof(IsCapeCyan));
            OnPropertyChanged(nameof(IsCapeSilver));

            await _skinService.SyncCapeToGameAsync(fullPath, Nickname, cfg.GameDir);
            _skinService.EnsureCustomSkinLoaderConfig(cfg.GameDir);
            _ = SetLobbyApiCapeAsync("none");

            IsStatusError = false;
            StatusMessage = $"Плащ «{item.Name}» применён.";
            ScheduleStatusMessageClear();
        }
        else
        {
            SkinPath = fullPath;
            SkinOriginalName = item.Name;
            string model = item.Model;
            if (string.IsNullOrWhiteSpace(model))
            {
                model = SkinService.DetectIsSlim(fullPath) ? "slim" : "default";
            }
            IsSlimModel = model == "slim";
            var mcDir = _configService.CurrentConfig.GameDir;
            await _skinService.SyncSkinToGameAsync(SkinPath, Nickname, mcDir);
            _skinService.EnsureCustomSkinLoaderConfig(mcDir);
            _ = UploadCurrentSkinAsync();
        }
    }

    public async Task DeleteHistoryItemAsync(WardrobeItem item)
    {
        if (item == null) return;

        var cfg = _configService.CurrentConfig;
        cfg.WardrobeHistory?.RemoveAll(x => string.Equals(x.Id, item.Id, StringComparison.OrdinalIgnoreCase));

        string fullPath = "";
        bool hasFile = _skinService != null && _skinService.TryResolveWardrobeItemPath(item, out fullPath);

        if (item.Type == "cape")
        {
            if (cfg.CapeId == "custom" && (string.Equals(cfg.CapePath, fullPath, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(cfg.CapePath)))
            {
                await RemoveCapeAsync();
            }
        }
        else
        {
            if (string.Equals(cfg.SkinPath, fullPath, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(cfg.SkinPath))
            {
                await ResetSkinToDefaultAsync();
            }
        }

        await _configService.SaveConfigAsync(cfg);

        // Удаление файла с диска ТОЛЬКО если нет других записей с тем же хешем
        if (hasFile && !string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
        {
            bool referencedByOther = cfg.WardrobeHistory != null && cfg.WardrobeHistory.Any(x => string.Equals(x.Hash, item.Hash, StringComparison.OrdinalIgnoreCase));
            if (!referencedByOther)
            {
                try
                {
                    string profileDir = _skinService!.GetProfileDataDir();
                    string safeBase = Path.GetFullPath(Path.Combine(profileDir, item.Type == "cape" ? "capes" : "skins"));
                    if (fullPath.StartsWith(safeBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(fullPath);
                    }
                }
                catch { }
            }
        }

        RefreshHistory();
    }

    public void RefreshHistory()
    {
        HistoryItems.Clear();
        var raw = _configService.CurrentConfig.WardrobeHistory;
        if (raw == null) return;

        foreach (var item in raw)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id)) continue;

            if (_skinService != null && _skinService.TryResolveWardrobeItemPath(item, out _))
            {
                HistoryItems.Add(item);
            }
        }
    }

    private static readonly System.Net.Http.HttpClient CapeHttpClient = new() { Timeout = TimeSpan.FromSeconds(15) };

    private async Task SetLobbyApiCapeAsync(string cape)
    {
        try
        {
            var cfg = _configService.CurrentConfig;
            string nick = cfg.Nickname?.Trim() ?? string.Empty;
            if (!NicknameValidator.Validate(nick).IsValid) return;

            string baseUrl = string.IsNullOrWhiteSpace(cfg.LobbyApiBaseUrl) ? "https://lobby-api.vercel.app" : cfg.LobbyApiBaseUrl.TrimEnd('/');
            string json = System.Text.Json.JsonSerializer.Serialize(new { nickname = nick, ownerToken = cfg.SkinOwnerToken, cape });
            using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{baseUrl}/api/skin") { Content = content };
            SkinService.AddUserAuthHeaders(req);
            using var resp = await CapeHttpClient.SendAsync(req);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[WARDROBE: WARN] SetLobbyApiCapeAsync: {ex.Message}");
        }
    }

    public async Task UploadCurrentSkinAsync()
    {
        try
        {
            var cfg = _configService.CurrentConfig;
            var res = await _skinService.UploadSkinToLobbyApiAsync(
                cfg.SkinPath,
                cfg.Nickname,
                cfg.SkinModel,
                cfg.SkinOwnerToken,
                cfg.LobbyApiBaseUrl);

            if (res.Success)
            {
                if (!string.IsNullOrWhiteSpace(res.OwnerToken))
                {
                    cfg.SkinOwnerToken = res.OwnerToken;
                    await _configService.SaveConfigAsync(cfg);
                }
                _skinService.EnsureCustomSkinLoaderConfig(cfg.GameDir);
                _skinService.ClearCustomSkinLoaderCache(cfg.GameDir);
                IsStatusError = false;
                StatusMessage = "Скин успешно загружен и применён!";
                ScheduleStatusMessageClear();
            }
            else
            {
                IsStatusError = true;
                StatusMessage = res.ErrorMessage ?? "Ошибка загрузки скина.";
            }
        }
        catch (Exception ex)
        {
            IsStatusError = true;
            StatusMessage = $"Ошибка отправки скина: {ex.Message}";
        }
    }

    private System.Windows.Threading.DispatcherTimer? _statusMessageTimer;

    private void ScheduleStatusMessageClear()
    {
        _statusMessageTimer?.Stop();
        _statusMessageTimer = new System.Windows.Threading.DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(3)
        };
        _statusMessageTimer.Tick += (s, e) =>
        {
            _statusMessageTimer.Stop();
            if (!_isStatusError)
            {
                StatusMessage = string.Empty;
            }
        };
        _statusMessageTimer.Start();
    }

    public async Task ResetSkinToDefaultAsync()
    {
        IsStatusError = false;
        SkinPath = string.Empty;
        SkinOriginalName = string.Empty;

        var mcDir = _configService.CurrentConfig.GameDir;
        await _skinService.ResetToDefaultSteveAsync(Nickname, mcDir);

        FormatDescription = "Классический скин Стива (64×64)";
        StatusMessage = "Скин сброшен на классического Стива.";

        // Загружаем сброшенный дефолтный скин на сервер
        _ = UploadCurrentSkinAsync();
    }

    private void UpdateSkinPreviews()
    {
        try
        {
            if (!string.IsNullOrWhiteSpace(SkinPath) && File.Exists(SkinPath))
            {
                var validation = _skinService.ValidateSkinFile(SkinPath);
                if (validation.IsValid)
                {
                    FormatDescription = validation.Height == 32
                        ? "Классический скин Minecraft (64×32)"
                        : "Современный скин Minecraft (64×64)";
                }
                else
                {
                    IsStatusError = true;
                    StatusMessage = validation.ErrorMessage ?? "Неподдерживаемый размер скина";
                    FormatDescription = "Классический скин Стива (64×64)";
                }
            }
            else
            {
                FormatDescription = "Классический скин Стива (64×64)";
            }

            SkinFrontPreview = _skinService.ExtractFrontSkinPreview(SkinPath);
            SkinBackPreview = _skinService.ExtractBackSkinPreview(SkinPath);
        }
        catch (Exception ex)
        {
            IsStatusError = true;
            StatusMessage = $"Ошибка загрузки скина: {ex.Message}";
        }
    }
}

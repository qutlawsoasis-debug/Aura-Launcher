using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using AuraLauncher.Core;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.Services.Implementations;

namespace AuraLauncher.ViewModels;

public class FriendRequestItemViewModel : ObservableObject
{
    public string Id { get; }
    public string Nick { get; }
    public RelayCommand AcceptCommand { get; }
    public RelayCommand DeclineCommand { get; }

    public FriendRequestItemViewModel(string id, string nick, Action<string, bool> onRespond)
    {
        Id = id;
        Nick = nick;
        AcceptCommand = new RelayCommand(_ => onRespond(id, true));
        DeclineCommand = new RelayCommand(_ => onRespond(id, false));
    }
}

public class FriendItemViewModel : ObservableObject
{
    private readonly Action<FriendItemViewModel> _onInvite;
    private readonly Action<FriendItemViewModel> _onRemove;

    public string Id { get; }
    public string Nick { get; }

    private bool _online;
    public bool Online
    {
        get => _online;
        set
        {
            if (SetProperty(ref _online, value))
            {
                OnPropertyChanged(nameof(RowOpacity));
                OnPropertyChanged(nameof(CanInvite));
            }
        }
    }

    private string _status = "online";
    public string Status
    {
        get => _status;
        set
        {
            if (SetProperty(ref _status, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    private long _lastSeen;
    public long LastSeen
    {
        get => _lastSeen;
        set
        {
            if (SetProperty(ref _lastSeen, value))
            {
                OnPropertyChanged(nameof(StatusText));
            }
        }
    }

    public string StatusText
    {
        get
        {
            if (Online)
            {
                return Status switch
                {
                    "lobby" => "В лобби",
                    "playing" => "Играет",
                    _ => "В сети"
                };
            }
            return $"Был(а) в сети {FormatLastSeen(LastSeen)}";
        }
    }

    public double RowOpacity => Online ? 1.0 : 0.45;

    private ImageSource _avatar;
    public ImageSource Avatar
    {
        get => _avatar;
        set => SetProperty(ref _avatar, value);
    }

    private bool _isInMyLobby;
    public bool IsInMyLobby
    {
        get => _isInMyLobby;
        set
        {
            if (SetProperty(ref _isInMyLobby, value))
            {
                OnPropertyChanged(nameof(CanInvite));
            }
        }
    }

    private string? _inviteState; // null, "pending", "accepted", "declined", "expired"
    public string? InviteState
    {
        get => _inviteState;
        set
        {
            if (SetProperty(ref _inviteState, value))
            {
                OnPropertyChanged(nameof(InviteStateText));
                OnPropertyChanged(nameof(CanInvite));
                OnPropertyChanged(nameof(HasInviteState));
            }
        }
    }

    public bool HasInviteState => !string.IsNullOrEmpty(_inviteState);

    public string? InviteStateText => _inviteState switch
    {
        "pending" => "Приглашён…",
        "accepted" => "Принял",
        "declined" => "Отклонил",
        "expired" => "Не ответил",
        _ => null
    };

    public bool CanInvite => Online && !IsInMyLobby && string.IsNullOrEmpty(_inviteState);

    private bool _isConfirmingDelete;
    public bool IsConfirmingDelete
    {
        get => _isConfirmingDelete;
        set => SetProperty(ref _isConfirmingDelete, value);
    }

    public RelayCommand InviteCommand { get; }
    public RelayCommand StartDeleteCommand { get; }
    public RelayCommand ConfirmDeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }

    public FriendItemViewModel(FriendPresenceItem item, Action<FriendItemViewModel> onInvite, Action<FriendItemViewModel> onRemove)
    {
        Id = item.Id;
        Nick = item.Nick;
        Online = item.Online;
        Status = item.Status;
        LastSeen = item.LastSeen;
        _avatar = SkinService.LoadDefaultSteveBitmap();
        _onInvite = onInvite;
        _onRemove = onRemove;

        InviteCommand = new RelayCommand(_ => _onInvite(this));
        StartDeleteCommand = new RelayCommand(_ => IsConfirmingDelete = true);
        ConfirmDeleteCommand = new RelayCommand(_ =>
        {
            IsConfirmingDelete = false;
            _onRemove(this);
        });
        CancelDeleteCommand = new RelayCommand(_ => IsConfirmingDelete = false);
    }

    public void UpdateFromPresence(FriendPresenceItem item)
    {
        Online = item.Online;
        Status = item.Status;
        LastSeen = item.LastSeen;
        OnPropertyChanged(nameof(StatusText));
    }

    public static string FormatLastSeen(long lastSeenMs)
    {
        if (lastSeenMs <= 0) return "недавно";
        var dt = DateTimeOffset.FromUnixTimeMilliseconds(lastSeenMs).LocalDateTime;
        var diff = DateTime.Now - dt;
        if (diff.TotalMinutes < 1) return "только что";
        if (diff.TotalMinutes < 60) return $"{(int)diff.TotalMinutes} мин. назад";
        if (diff.TotalHours < 24) return $"{(int)diff.TotalHours} ч. назад";
        if (diff.TotalDays < 2) return "вчера";
        return dt.ToString("dd.MM.yyyy");
    }
}

public class FriendsViewModel : ObservableObject
{
    private readonly IFriendService _friendService;
    private readonly ILobbyService _lobbyService;
    private readonly ISkinService _skinService;
    private readonly LobbyViewModel _lobbyViewModel;

    public event Action? OpenLobbyRequested;

    public ObservableCollection<FriendItemViewModel> Friends { get; } = new();
    public ObservableCollection<FriendRequestItemViewModel> IncomingRequests { get; } = new();

    private string _addCodeInput = string.Empty;
    public string AddCodeInput
    {
        get => _addCodeInput;
        set
        {
            var clean = (value ?? string.Empty).Trim().ToUpperInvariant();
            if (clean.Length > 8) clean = clean.Substring(0, 8);
            if (SetProperty(ref _addCodeInput, clean))
            {
                AddFriendErrorMessage = string.Empty;
                OnPropertyChanged(nameof(CanAddFriend));
            }
        }
    }

    private string _addFriendErrorMessage = string.Empty;
    public string AddFriendErrorMessage
    {
        get => _addFriendErrorMessage;
        set
        {
            if (SetProperty(ref _addFriendErrorMessage, value))
            {
                OnPropertyChanged(nameof(HasAddFriendError));
            }
        }
    }

    public bool HasAddFriendError => !string.IsNullOrWhiteSpace(_addFriendErrorMessage);
    public bool CanAddFriend => AddCodeInput.Length == 8 && !IsBusy;

    private bool _isBusy;
    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            if (SetProperty(ref _isBusy, value))
            {
                OnPropertyChanged(nameof(CanAddFriend));
            }
        }
    }

    private bool _isCodeCopied;
    public bool IsCodeCopied
    {
        get => _isCodeCopied;
        private set
        {
            if (SetProperty(ref _isCodeCopied, value))
            {
                OnPropertyChanged(nameof(CopyButtonText));
            }
        }
    }

    public string CopyButtonText => IsCodeCopied ? "Скопировано!" : "Копировать";

    private bool _isLinkCopied;
    public bool IsLinkCopied
    {
        get => _isLinkCopied;
        private set
        {
            if (SetProperty(ref _isLinkCopied, value))
            {
                OnPropertyChanged(nameof(CopyLinkButtonText));
            }
        }
    }

    public string CopyLinkButtonText => IsLinkCopied ? "Скопировано!" : "Копировать ссылку";

    public string MyFriendCode => _friendService.CurrentFriendCode ?? "--------";

    // 8 символов кода для ячеек
    public char CodeChar0 => GetCodeChar(0);
    public char CodeChar1 => GetCodeChar(1);
    public char CodeChar2 => GetCodeChar(2);
    public char CodeChar3 => GetCodeChar(3);
    public char CodeChar4 => GetCodeChar(4);
    public char CodeChar5 => GetCodeChar(5);
    public char CodeChar6 => GetCodeChar(6);
    public char CodeChar7 => GetCodeChar(7);

    private char GetCodeChar(int index)
    {
        var code = MyFriendCode;
        if (string.IsNullOrEmpty(code) || index >= code.Length) return ' ';
        return code[index];
    }

    private void NotifyCodeCharsChanged()
    {
        OnPropertyChanged(nameof(MyFriendCode));
        OnPropertyChanged(nameof(CodeChar0));
        OnPropertyChanged(nameof(CodeChar1));
        OnPropertyChanged(nameof(CodeChar2));
        OnPropertyChanged(nameof(CodeChar3));
        OnPropertyChanged(nameof(CodeChar4));
        OnPropertyChanged(nameof(CodeChar5));
        OnPropertyChanged(nameof(CodeChar6));
        OnPropertyChanged(nameof(CodeChar7));
    }

    public bool HasIncomingRequests => IncomingRequests.Count > 0;
    public bool IsEmptyList => Friends.Count == 0 && !HasIncomingRequests;

    public bool IsLobbyActiveBannerVisible => _lobbyViewModel.IsInLobby && _lobbyService.IsHost;

    public RelayCommand CopyCodeCommand { get; }
    public RelayCommand CopyLinkCommand { get; }
    public RelayCommand AddFriendCommand { get; }
    public RelayCommand OpenLobbyCommand { get; }

    public FriendsViewModel(
        IFriendService friendService,
        ILobbyService lobbyService,
        ISkinService skinService,
        LobbyViewModel lobbyViewModel)
    {
        _friendService = friendService ?? throw new ArgumentNullException(nameof(friendService));
        _lobbyService = lobbyService ?? throw new ArgumentNullException(nameof(lobbyService));
        _skinService = skinService ?? throw new ArgumentNullException(nameof(skinService));
        _lobbyViewModel = lobbyViewModel ?? throw new ArgumentNullException(nameof(lobbyViewModel));

        CopyCodeCommand = new RelayCommand(_ => CopyCode());
        CopyLinkCommand = new RelayCommand(_ => CopyFriendLink());
        AddFriendCommand = new RelayCommand(async _ => await AddFriendAsync(), _ => CanAddFriend);
        OpenLobbyCommand = new RelayCommand(_ => OpenLobbyRequested?.Invoke());

        _friendService.SyncUpdated += OnSyncUpdated;
        _lobbyService.StatusChanged += _ => Dispatch(() => OnPropertyChanged(nameof(IsLobbyActiveBannerVisible)));
    }

    public void OnTabActivated()
    {
        _friendService.IsFriendsTabActive = true;
        NotifyCodeCharsChanged();
        _ = _friendService.SyncNowAsync();
    }

    public void OnTabDeactivated()
    {
        _friendService.IsFriendsTabActive = false;
    }

    private void Dispatch(Action action)
    {
        var dispatcher = Application.Current?.Dispatcher;
        if (dispatcher == null || dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            dispatcher.InvokeAsync(action);
        }
    }

    private void CopyCode()
    {
        var code = MyFriendCode;
        if (string.IsNullOrWhiteSpace(code) || code.Contains('-')) return;

        try
        {
            Clipboard.SetText(code);
            IsCodeCopied = true;
            _ = Task.Delay(1600).ContinueWith(_ => Dispatch(() => IsCodeCopied = false));
        }
        catch { }
    }

    private void CopyFriendLink()
    {
        var code = MyFriendCode;
        if (string.IsNullOrWhiteSpace(code) || code.Contains('-')) return;

        try
        {
            var url = $"https://lobby-api.vercel.app/f/{code}";
            Clipboard.SetText(url);
            IsLinkCopied = true;
            _ = Task.Delay(2000).ContinueWith(_ => Dispatch(() => IsLinkCopied = false));
        }
        catch { }
    }

    private async Task AddFriendAsync()
    {
        if (AddCodeInput.Length != 8 || IsBusy) return;

        IsBusy = true;
        AddFriendErrorMessage = string.Empty;

        try
        {
            var (success, errorMsg) = await _friendService.SendFriendRequestAsync(AddCodeInput);
            if (success)
            {
                AddCodeInput = string.Empty;
            }
            else
            {
                AddFriendErrorMessage = errorMsg ?? "Ошибка отправки заявки";
            }
        }
        catch (Exception ex)
        {
            AddFriendErrorMessage = ex.Message;
        }
        finally
        {
            IsBusy = false;
        }
    }

    private void OnSyncUpdated(SyncResponse sync)
    {
        Dispatch(() =>
        {
            NotifyCodeCharsChanged();

            // 1. Входящие заявки
            IncomingRequests.Clear();
            if (sync.IncomingRequests != null)
            {
                foreach (var req in sync.IncomingRequests)
                {
                    IncomingRequests.Add(new FriendRequestItemViewModel(req.Id, req.Nick, OnRespondFriendRequest));
                }
            }
            OnPropertyChanged(nameof(HasIncomingRequests));

            // 2. Список друзей
            var currentFriendsDict = Friends.ToDictionary(f => f.Id);
            var updatedList = new List<FriendItemViewModel>();

            if (sync.Friends != null)
            {
                foreach (var item in sync.Friends)
                {
                    if (currentFriendsDict.TryGetValue(item.Id, out var existing))
                    {
                        existing.UpdateFromPresence(item);
                        existing.IsInMyLobby = _lobbyViewModel.IsInLobby &&
                            string.Equals(item.Status, "lobby", StringComparison.OrdinalIgnoreCase);
                        updatedList.Add(existing);
                    }
                    else
                    {
                        var vm = new FriendItemViewModel(item, OnInviteFriend, OnRemoveFriend);
                        updatedList.Add(vm);
                        _ = LoadAvatarAsync(vm);
                    }
                }
            }

            // Обработка sentInvites
            if (sync.SentInvites != null)
            {
                foreach (var sent in sync.SentInvites)
                {
                    var friend = updatedList.FirstOrDefault(f => f.Id == sent.FriendId);
                    if (friend != null)
                    {
                        friend.InviteState = sent.State;
                    }
                }
            }

            // Сортировка: онлайн первыми, затем офлайн
            var sorted = updatedList
                .OrderByDescending(f => f.Online)
                .ThenBy(f => f.Nick)
                .ToList();

            Friends.Clear();
            foreach (var f in sorted)
            {
                Friends.Add(f);
            }

            OnPropertyChanged(nameof(IsEmptyList));
            OnPropertyChanged(nameof(IsLobbyActiveBannerVisible));
        });
    }

    private async Task LoadAvatarAsync(FriendItemViewModel vm)
    {
        try
        {
            var avatar = await _skinService.GetAvatarForPlayerAsync(vm.Nick);
            if (avatar != null)
            {
                Dispatch(() => vm.Avatar = avatar);
            }
        }
        catch { }
    }

    private void OnRespondFriendRequest(string fromId, bool accept)
    {
        _ = Task.Run(async () =>
        {
            await _friendService.RespondFriendRequestAsync(fromId, accept);
        });
    }

    private void OnRemoveFriend(FriendItemViewModel friend)
    {
        _ = Task.Run(async () =>
        {
            var success = await _friendService.RemoveFriendAsync(friend.Id);
            if (success)
            {
                Dispatch(() =>
                {
                    Friends.Remove(friend);
                    OnPropertyChanged(nameof(IsEmptyList));
                });
            }
        });
    }

    private void OnInviteFriend(FriendItemViewModel friend)
    {
        _ = Task.Run(async () =>
        {
            try
            {
                string? lobbyCode = _lobbyService.CurrentLobbyCode;
                string? hostToken = _lobbyService.CurrentHostToken;

                // Если лобби нет, создается автоматически (я хост)
                if (!_lobbyViewModel.IsInLobby || !_lobbyService.IsHost || string.IsNullOrWhiteSpace(lobbyCode) || string.IsNullOrWhiteSpace(hostToken))
                {
                    lobbyCode = await _lobbyViewModel.CreateLobbyAsync();
                    hostToken = _lobbyService.CurrentHostToken;
                }

                if (string.IsNullOrWhiteSpace(lobbyCode) || string.IsNullOrWhiteSpace(hostToken))
                {
                    Dispatch(() => friend.InviteState = null);
                    return;
                }

                Dispatch(() =>
                {
                    friend.InviteState = "pending";
                    OnPropertyChanged(nameof(IsLobbyActiveBannerVisible));
                });

                var (success, inviteId, error) = await _friendService.SendInviteAsync(friend.Id, lobbyCode, hostToken);
                if (!success)
                {
                    Dispatch(() => friend.InviteState = null);
                    return;
                }

                // Таймер 120 с на случай истечения
                _ = Task.Delay(TimeSpan.FromSeconds(120)).ContinueWith(_ =>
                {
                    Dispatch(() =>
                    {
                        if (friend.InviteState == "pending")
                        {
                            friend.InviteState = "expired";
                        }
                    });
                });
            }
            catch
            {
                Dispatch(() => friend.InviteState = null);
            }
        });
    }
}

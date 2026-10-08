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
                OnPropertyChanged(nameof(CanJoin));
                OnPropertyChanged(nameof(CanInvite));
            }
        }
    }

    private string? _lobbyCode;
    public string? LobbyCode
    {
        get => _lobbyCode;
        set
        {
            if (SetProperty(ref _lobbyCode, value))
            {
                OnPropertyChanged(nameof(CanJoin));
                OnPropertyChanged(nameof(CanInvite));
            }
        }
    }

    private string? _incomingInviteId;
    public string? IncomingInviteId
    {
        get => _incomingInviteId;
        set
        {
            if (SetProperty(ref _incomingInviteId, value))
            {
                OnPropertyChanged(nameof(CanJoin));
                OnPropertyChanged(nameof(CanInvite));
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

    private ImageSource _avatar = SkinService.LoadDefaultSteveHead();
    public ImageSource Avatar
    {
        get => _avatar;
        set => SetProperty(ref _avatar, value);
    }

    private bool _isCustomAvatarLoaded;
    public bool IsCustomAvatarLoaded
    {
        get => _isCustomAvatarLoaded;
        set => SetProperty(ref _isCustomAvatarLoaded, value);
    }

    private bool _isInMyLobby;
    public bool IsInMyLobby
    {
        get => _isInMyLobby;
        set
        {
            if (SetProperty(ref _isInMyLobby, value))
            {
                OnPropertyChanged(nameof(CanJoin));
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
                OnPropertyChanged(nameof(CanJoin));
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

    public bool CanInvite => Online && !IsInMyLobby && !CanJoin && string.IsNullOrEmpty(_inviteState);
    public bool CanJoin => Online && !IsInMyLobby && !HasInviteState && string.Equals(Status, "lobby", StringComparison.OrdinalIgnoreCase);

    private bool _isConfirmingDelete;
    public bool IsConfirmingDelete
    {
        get => _isConfirmingDelete;
        set => SetProperty(ref _isConfirmingDelete, value);
    }

    public RelayCommand InviteCommand { get; }
    public RelayCommand JoinCommand { get; }
    public RelayCommand StartDeleteCommand { get; }
    public RelayCommand ConfirmDeleteCommand { get; }
    public RelayCommand CancelDeleteCommand { get; }

    public FriendItemViewModel(FriendPresenceItem item, Action<FriendItemViewModel> onInvite, Action<FriendItemViewModel> onRemove, Action<FriendItemViewModel>? onJoin = null)
    {
        Id = item.Id;
        Nick = item.Nick;
        Online = item.Online;
        Status = item.Status;
        LobbyCode = item.LobbyCode;
        LastSeen = item.LastSeen;
        _avatar = SkinService.LoadDefaultSteveBitmap();
        _onInvite = onInvite;
        _onRemove = onRemove;

        InviteCommand = new RelayCommand(_ => _onInvite(this));
        JoinCommand = new RelayCommand(_ => onJoin?.Invoke(this));
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
        LobbyCode = item.LobbyCode;
        LastSeen = item.LastSeen;
        OnPropertyChanged(nameof(StatusText));
        OnPropertyChanged(nameof(CanJoin));
        OnPropertyChanged(nameof(CanInvite));
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
    private readonly SemaphoreSlim _inviteLock = new(1, 1);
    private readonly HashSet<string> _inFlightInviteFriendIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _dismissedSentInviteIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<string> _knownSentInviteIds = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, string> _latestSentInviteIdByFriend = new(StringComparer.OrdinalIgnoreCase);
    private string? _trackedLobbyCode;
    private bool _wasInHostLobby;

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
        _lobbyService.StatusChanged += _ => Dispatch(RefreshLobbyStateForFriends);
        _lobbyViewModel.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(LobbyViewModel.IsInLobby) ||
                e.PropertyName == nameof(LobbyViewModel.LobbyCode) ||
                e.PropertyName == nameof(LobbyViewModel.IsLobbyCreated) ||
                e.PropertyName == nameof(LobbyViewModel.IsHost))
            {
                Dispatch(RefreshLobbyStateForFriends);
            }
        };
        _lobbyViewModel.LobbyPlayers.CollectionChanged += (_, _) => Dispatch(RefreshLobbyStateForFriends);
    }

    private bool ComputeIsFriendInMyLobby(string friendNick, string? friendStatus, string? friendLobbyCode)
    {
        if (!_lobbyViewModel.IsInLobby) return false;

        if (_lobbyViewModel.LobbyPlayers.Any(p => string.Equals(p.Nickname, friendNick, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        var myLobbyCode = _lobbyViewModel.LobbyCode ?? _lobbyService.CurrentLobbyCode;
        if (!string.IsNullOrWhiteSpace(myLobbyCode) &&
            !string.IsNullOrWhiteSpace(friendLobbyCode) &&
            string.Equals(myLobbyCode, friendLobbyCode, StringComparison.OrdinalIgnoreCase) &&
            string.Equals(friendStatus, "lobby", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        return false;
    }

    private void DismissAllKnownSentInvites()
    {
        foreach (var id in _knownSentInviteIds)
        {
            _dismissedSentInviteIds.Add(id);
        }
        foreach (var id in _latestSentInviteIdByFriend.Values)
        {
            _dismissedSentInviteIds.Add(id);
        }
        _latestSentInviteIdByFriend.Clear();
    }

    private void RefreshLobbyStateForFriends()
    {
        bool isHostLobbyNow = _lobbyViewModel.IsInLobby && _lobbyService.IsHost;
        string? currentCode = _lobbyViewModel.LobbyCode ?? _lobbyService.CurrentLobbyCode;

        if (!isHostLobbyNow)
        {
            if (_wasInHostLobby || _knownSentInviteIds.Count > 0)
            {
                DismissAllKnownSentInvites();
            }
            _wasInHostLobby = false;
            _trackedLobbyCode = null;
        }
        else
        {
            if (_wasInHostLobby &&
                !string.IsNullOrWhiteSpace(_trackedLobbyCode) &&
                !string.IsNullOrWhiteSpace(currentCode) &&
                !string.Equals(_trackedLobbyCode, currentCode, StringComparison.OrdinalIgnoreCase))
            {
                DismissAllKnownSentInvites();
            }
            _wasInHostLobby = true;
            _trackedLobbyCode = currentCode;
        }

        foreach (var friend in Friends)
        {
            bool wasInMyLobby = friend.IsInMyLobby;
            bool nowInMyLobby = ComputeIsFriendInMyLobby(friend.Nick, friend.Status, friend.LobbyCode);
            friend.IsInMyLobby = nowInMyLobby;

            if (!isHostLobbyNow)
            {
                if (!_inFlightInviteFriendIds.Contains(friend.Id))
                {
                    friend.InviteState = null;
                }
            }
            else if (wasInMyLobby && !nowInMyLobby)
            {
                if (_latestSentInviteIdByFriend.TryGetValue(friend.Id, out var invId))
                {
                    _dismissedSentInviteIds.Add(invId);
                    _latestSentInviteIdByFriend.Remove(friend.Id);
                }
                if (!_inFlightInviteFriendIds.Contains(friend.Id))
                {
                    friend.InviteState = null;
                }
            }
        }

        OnPropertyChanged(nameof(IsLobbyActiveBannerVisible));
    }

    public void OnTabActivated()
    {
        _friendService.IsFriendsTabActive = true;
        NotifyCodeCharsChanged();
        RefreshLobbyStateForFriends();
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
            RefreshLobbyStateForFriends();

            bool isHostLobbyNow = _lobbyViewModel.IsInLobby && _lobbyService.IsHost;
            string? myLobbyCode = _lobbyViewModel.LobbyCode ?? _lobbyService.CurrentLobbyCode;

            var incomingList = sync.IncomingRequests ?? Array.Empty<FriendRequestItem>();
            bool requestsSame = IncomingRequests.Count == incomingList.Length &&
                                IncomingRequests.Select(r => r.Id).SequenceEqual(incomingList.Select(r => r.Id), StringComparer.OrdinalIgnoreCase);
            if (!requestsSame)
            {
                IncomingRequests.Clear();
                foreach (var req in incomingList)
                {
                    IncomingRequests.Add(new FriendRequestItemViewModel(req.Id, req.Nick, OnRespondFriendRequest));
                }
            }
            OnPropertyChanged(nameof(HasIncomingRequests));

            var incomingInviteBySender = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (sync.Invites != null)
            {
                foreach (var inv in sync.Invites)
                {
                    if (!string.IsNullOrWhiteSpace(inv.FromId) && !string.IsNullOrWhiteSpace(inv.InviteId))
                    {
                        incomingInviteBySender[inv.FromId] = inv.InviteId;
                    }
                }
            }

            if (sync.SentInvites != null)
            {
                foreach (var sent in sync.SentInvites)
                {
                    if (!string.IsNullOrWhiteSpace(sent.InviteId))
                    {
                        _knownSentInviteIds.Add(sent.InviteId);
                        if (!isHostLobbyNow)
                        {
                            _dismissedSentInviteIds.Add(sent.InviteId);
                        }
                        else if (!string.IsNullOrWhiteSpace(sent.LobbyCode) &&
                                 !string.IsNullOrWhiteSpace(myLobbyCode) &&
                                 !string.Equals(sent.LobbyCode, myLobbyCode, StringComparison.OrdinalIgnoreCase))
                        {
                            _dismissedSentInviteIds.Add(sent.InviteId);
                        }
                    }
                }
            }

            var currentFriendsDict = Friends.ToDictionary(f => f.Id, StringComparer.OrdinalIgnoreCase);
            var updatedList = new List<FriendItemViewModel>();

            if (sync.Friends != null)
            {
                foreach (var item in sync.Friends)
                {
                    FriendItemViewModel vm;
                    bool wasInMyLobby = false;
                    if (currentFriendsDict.TryGetValue(item.Id, out var existing))
                    {
                        wasInMyLobby = existing.IsInMyLobby;
                        existing.UpdateFromPresence(item);
                        vm = existing;
                        if (!existing.IsCustomAvatarLoaded)
                        {
                            _ = LoadAvatarAsync(existing);
                        }
                    }
                    else
                    {
                        vm = new FriendItemViewModel(item, OnInviteFriend, OnRemoveFriend, OnJoinFriend);
                        _ = LoadAvatarAsync(vm);
                    }

                    vm.IncomingInviteId = incomingInviteBySender.TryGetValue(item.Id, out var inInvId) ? inInvId : null;
                    bool nowInMyLobby = ComputeIsFriendInMyLobby(item.Nick, item.Status, item.LobbyCode);
                    vm.IsInMyLobby = nowInMyLobby;

                    if (wasInMyLobby && !nowInMyLobby && _latestSentInviteIdByFriend.TryGetValue(item.Id, out var oldInvId))
                    {
                        _dismissedSentInviteIds.Add(oldInvId);
                        _latestSentInviteIdByFriend.Remove(item.Id);
                    }

                    if (!_inFlightInviteFriendIds.Contains(item.Id))
                    {
                        vm.InviteState = null;
                    }

                    updatedList.Add(vm);
                }
            }

            if (isHostLobbyNow && sync.SentInvites != null)
            {
                foreach (var sent in sync.SentInvites)
                {
                    if (!string.IsNullOrWhiteSpace(sent.InviteId) && _dismissedSentInviteIds.Contains(sent.InviteId))
                    {
                        continue;
                    }

                    if (!string.IsNullOrWhiteSpace(sent.LobbyCode) &&
                        !string.IsNullOrWhiteSpace(myLobbyCode) &&
                        !string.Equals(sent.LobbyCode, myLobbyCode, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var friend = updatedList.FirstOrDefault(f => string.Equals(f.Id, sent.FriendId, StringComparison.OrdinalIgnoreCase));
                    if (friend == null) continue;

                    if (!string.IsNullOrWhiteSpace(sent.InviteId))
                    {
                        _latestSentInviteIdByFriend[friend.Id] = sent.InviteId;
                    }

                    if (string.Equals(sent.State, "accepted", StringComparison.OrdinalIgnoreCase) &&
                        !friend.IsInMyLobby &&
                        !string.Equals(friend.Status, "lobby", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.IsNullOrWhiteSpace(sent.InviteId))
                        {
                            _dismissedSentInviteIds.Add(sent.InviteId);
                        }
                        friend.InviteState = null;
                        continue;
                    }

                    friend.InviteState = sent.State;

                    if (string.Equals(sent.State, "declined", StringComparison.OrdinalIgnoreCase) ||
                        string.Equals(sent.State, "expired", StringComparison.OrdinalIgnoreCase))
                    {
                        var invIdToDismiss = sent.InviteId;
                        var targetFriend = friend;
                        _ = Task.Delay(TimeSpan.FromSeconds(4)).ContinueWith(_ =>
                        {
                            Dispatch(() =>
                            {
                                if (!string.IsNullOrWhiteSpace(invIdToDismiss))
                                {
                                    _dismissedSentInviteIds.Add(invIdToDismiss);
                                }
                                if (string.Equals(targetFriend.InviteState, "declined", StringComparison.OrdinalIgnoreCase) ||
                                    string.Equals(targetFriend.InviteState, "expired", StringComparison.OrdinalIgnoreCase))
                                {
                                    targetFriend.InviteState = null;
                                }
                            });
                        });
                    }
                }
            }

            var sorted = updatedList
                .OrderByDescending(f => f.Online)
                .ThenBy(f => f.Nick)
                .ToList();

            bool sameFriendsOrder = Friends.Count == sorted.Count &&
                                    Friends.Select(f => f.Id).SequenceEqual(sorted.Select(f => f.Id), StringComparer.OrdinalIgnoreCase);
            if (!sameFriendsOrder)
            {
                Friends.Clear();
                foreach (var f in sorted)
                {
                    Friends.Add(f);
                }
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
                Dispatch(() =>
                {
                    vm.Avatar = avatar;
                    if (!SkinService.IsDefaultSteveHead(avatar))
                    {
                        vm.IsCustomAvatarLoaded = true;
                    }
                });
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
        if (friend == null || !friend.Online || friend.IsInMyLobby) return;
        if (_inFlightInviteFriendIds.Contains(friend.Id) || !string.IsNullOrEmpty(friend.InviteState)) return;

        _inFlightInviteFriendIds.Add(friend.Id);
        friend.InviteState = "pending";

        _ = Task.Run(async () =>
        {
            await _inviteLock.WaitAsync();
            try
            {
                string? lobbyCode = _lobbyService.CurrentLobbyCode;
                string? hostToken = _lobbyService.CurrentHostToken;

                if (!_lobbyViewModel.IsInLobby || !_lobbyService.IsHost || string.IsNullOrWhiteSpace(lobbyCode) || string.IsNullOrWhiteSpace(hostToken))
                {
                    lobbyCode = await _lobbyViewModel.CreateLobbyAsync();
                    hostToken = _lobbyService.CurrentHostToken;
                }

                if (!_lobbyViewModel.IsInLobby || !_lobbyService.IsHost || string.IsNullOrWhiteSpace(lobbyCode) || string.IsNullOrWhiteSpace(hostToken))
                {
                    Dispatch(() =>
                    {
                        _inFlightInviteFriendIds.Remove(friend.Id);
                        friend.InviteState = null;
                    });
                    return;
                }

                Dispatch(() =>
                {
                    _wasInHostLobby = true;
                    _trackedLobbyCode = lobbyCode;
                    friend.InviteState = "pending";
                    OnPropertyChanged(nameof(IsLobbyActiveBannerVisible));
                });

                var (success, inviteId, _) = await _friendService.SendInviteAsync(friend.Id, lobbyCode, hostToken);
                if (!success)
                {
                    Dispatch(() =>
                    {
                        _inFlightInviteFriendIds.Remove(friend.Id);
                        friend.InviteState = null;
                    });
                    return;
                }

                Dispatch(() =>
                {
                    if (!string.IsNullOrWhiteSpace(inviteId))
                    {
                        _knownSentInviteIds.Add(inviteId);
                        _latestSentInviteIdByFriend[friend.Id] = inviteId;
                    }
                    _inFlightInviteFriendIds.Remove(friend.Id);
                    if (_lobbyViewModel.IsInLobby && _lobbyService.IsHost && string.Equals(_lobbyViewModel.LobbyCode ?? _lobbyService.CurrentLobbyCode, lobbyCode, StringComparison.OrdinalIgnoreCase))
                    {
                        friend.InviteState = "pending";
                    }
                    else
                    {
                        if (!string.IsNullOrWhiteSpace(inviteId))
                        {
                            _dismissedSentInviteIds.Add(inviteId);
                        }
                        friend.InviteState = null;
                    }
                });

                var expectedLobbyCode = lobbyCode;
                _ = Task.Delay(TimeSpan.FromSeconds(120)).ContinueWith(_ =>
                {
                    Dispatch(() =>
                    {
                        if (_lobbyViewModel.IsInLobby &&
                            _lobbyService.IsHost &&
                            string.Equals(_lobbyViewModel.LobbyCode ?? _lobbyService.CurrentLobbyCode, expectedLobbyCode, StringComparison.OrdinalIgnoreCase) &&
                            friend.InviteState == "pending")
                        {
                            friend.InviteState = "expired";
                        }
                    });
                });
            }
            catch
            {
                Dispatch(() =>
                {
                    _inFlightInviteFriendIds.Remove(friend.Id);
                    friend.InviteState = null;
                });
            }
            finally
            {
                _inviteLock.Release();
            }
        });
    }

    private void OnJoinFriend(FriendItemViewModel friend)
    {
        if (friend == null) return;

        try
        {
            OpenLobbyRequested?.Invoke();
        }
        catch { }

        _ = Task.Run(async () =>
        {
            try
            {
                string? codeToJoin = friend.LobbyCode;
                bool fromInvite = false;

                if (string.IsNullOrWhiteSpace(codeToJoin) && !string.IsNullOrWhiteSpace(friend.IncomingInviteId))
                {
                    var (ok, inviteLobbyCode, _) = await _friendService.RespondInviteAsync(friend.IncomingInviteId, accept: true);
                    if (ok && !string.IsNullOrWhiteSpace(inviteLobbyCode))
                    {
                        codeToJoin = inviteLobbyCode;
                        fromInvite = true;
                    }
                }

                if (!string.IsNullOrWhiteSpace(codeToJoin))
                {
                    await _lobbyViewModel.JoinByCodeAsync(codeToJoin, fromInvite);
                }
            }
            catch { }
        });
    }
}

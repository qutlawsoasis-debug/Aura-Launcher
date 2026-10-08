using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Media;
using System.Windows.Media.Media3D;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using AuraLauncher.ViewModels;
using Xunit;

namespace AuraLauncher.Tests;

public class FriendsAndLobbyScenarioTests
{
    private sealed class ScenarioLobbyService : ILobbyService
    {
        public string? CurrentLobbyCode { get; set; }
        public string? CurrentHostToken { get; set; }
        public string? CurrentTunnelAddress { get; set; }
        public string CurrentStatus { get; set; } = "idle";
        public bool IsHost { get; set; }
        public bool IsReadyToPlay => CurrentStatus.Equals("open", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(CurrentTunnelAddress);
        public IReadOnlyDictionary<string, PlayerModSyncInfo>? CurrentModSync { get; set; }

        public int CreateCallCount;
        public int JoinCallCount;
        public int LeaveCallCount;
        public int CloseCallCount;
        public int NextLobbyNumber = 1;
        public TimeSpan CreateDelay { get; set; } = TimeSpan.Zero;
        public TimeSpan JoinDelay { get; set; } = TimeSpan.Zero;

        public event Action<string>? StatusChanged;
        public event Action<string>? TunnelAddressReady;
        public event Action<LobbyStatusResponse>? LobbyStatusUpdated;
        public event Action<IReadOnlyDictionary<string, PlayerModSyncInfo>>? ModSyncUpdated;

        public Task UpdateManifestAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;

        public async Task<string?> CreateLobbyAsHostAsync(string hostName, CancellationToken cancellationToken = default)
        {
            int callNum = Interlocked.Increment(ref CreateCallCount);
            if (CreateDelay > TimeSpan.Zero)
            {
                await Task.Delay(CreateDelay, cancellationToken);
            }

            string code = $"HOST{callNum:00}";
            CurrentLobbyCode = code;
            CurrentHostToken = $"token-{callNum}";
            CurrentStatus = "waiting";
            IsHost = true;
            StatusChanged?.Invoke(CurrentStatus);
            return code;
        }

        public Task<bool> HostOpenWorldAsync(string? customTunnelAddress = null, int localPort = 25565, CancellationToken cancellationToken = default)
        {
            CurrentStatus = "open";
            CurrentTunnelAddress = customTunnelAddress ?? $"127.0.0.1:{localPort}";
            StatusChanged?.Invoke(CurrentStatus);
            TunnelAddressReady?.Invoke(CurrentTunnelAddress);
            return Task.FromResult(true);
        }

        public Task CloseLobbyAsHostAsync(CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref CloseCallCount);
            LeaveLobby();
            return Task.CompletedTask;
        }

        public async Task<bool> JoinLobbyAsGuestAsync(string code, string playerName, CancellationToken cancellationToken = default)
        {
            Interlocked.Increment(ref JoinCallCount);
            if (JoinDelay > TimeSpan.Zero)
            {
                await Task.Delay(JoinDelay, cancellationToken);
            }

            if (code == "FAIL00") return false;

            CurrentLobbyCode = code;
            CurrentHostToken = null;
            CurrentStatus = "waiting";
            IsHost = false;
            StatusChanged?.Invoke(CurrentStatus);
            return true;
        }

        public Task<LobbyStatusResponse?> RefreshGuestStatusAsync(CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrWhiteSpace(CurrentLobbyCode))
            {
                return Task.FromResult<LobbyStatusResponse?>(null);
            }

            var resp = new LobbyStatusResponse(CurrentLobbyCode, CurrentStatus, CurrentTunnelAddress, 2, 0);
            LobbyStatusUpdated?.Invoke(resp);
            return Task.FromResult<LobbyStatusResponse?>(resp);
        }

        public void LeaveLobby()
        {
            Interlocked.Increment(ref LeaveCallCount);
            CurrentLobbyCode = null;
            CurrentHostToken = null;
            CurrentTunnelAddress = null;
            CurrentStatus = "idle";
            IsHost = false;
            StatusChanged?.Invoke(CurrentStatus);
        }
    }

    private sealed class ScenarioFriendService : IFriendService
    {
        public bool IsRegistered => true;
        public string? CurrentUserId { get; set; } = "me-id";
        public string? CurrentFriendCode { get; set; } = "MYCODE12";
        public bool IsFriendsTabActive { get; set; }
        public bool IsInLobby { get; set; }
        public string? CurrentLobbyCode { get; set; }
        public string? CurrentHostToken { get; set; }
        public bool IsGameRunning { get; set; }

        public int SendInviteCallCount;
        public List<(string FriendId, string LobbyCode, string HostToken)> SentInvitesLog { get; } = new();
        public Dictionary<string, string> InviteAcceptLobbyCodeByInviteId { get; } = new(StringComparer.OrdinalIgnoreCase);

        public event Action<SyncResponse>? SyncUpdated;
        public event Action<IReadOnlyList<FriendPresenceItem>>? FriendsListUpdated;
        public event Action<IncomingInviteItem>? InviteReceived;
        public event Action<FriendRequestItem>? FriendRequestReceived;

        public void Start() { }
        public void Stop() { }
        public void Dispose() { }

        public Task EnsureRegisteredAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SyncResponse?> SyncNowAsync(CancellationToken cancellationToken = default) => Task.FromResult<SyncResponse?>(null);

        public void EmitSync(SyncResponse response)
        {
            FriendsListUpdated?.Invoke(response.Friends);
            SyncUpdated?.Invoke(response);
        }

        public void EmitIncomingInvite(IncomingInviteItem item)
        {
            InviteReceived?.Invoke(item);
        }

        public void EmitFriendRequest(FriendRequestItem item)
        {
            FriendRequestReceived?.Invoke(item);
        }

        public Task<(bool Success, string? ErrorMessage)> SendFriendRequestAsync(string friendCode, CancellationToken cancellationToken = default)
            => Task.FromResult((true, (string?)null));

        public Task<bool> RespondFriendRequestAsync(string fromId, bool accept, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> RemoveFriendAsync(string friendId, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<(bool Success, string? InviteId, string? ErrorMessage)> SendInviteAsync(string friendId, string lobbyCode, string hostToken, CancellationToken cancellationToken = default)
        {
            int num = Interlocked.Increment(ref SendInviteCallCount);
            lock (SentInvitesLog)
            {
                SentInvitesLog.Add((friendId, lobbyCode, hostToken));
            }
            return Task.FromResult((true, (string?)$"inv-{num}", (string?)null));
        }

        public Task<(bool Success, string? ErrorMessage)> ChangeNicknameAsync(string newNick, CancellationToken cancellationToken = default)
            => Task.FromResult((true, (string?)null));

        public Task<(bool Success, string? LobbyCode, string? ErrorMessage)> RespondInviteAsync(string inviteId, bool accept, CancellationToken cancellationToken = default)
        {
            if (accept && InviteAcceptLobbyCodeByInviteId.TryGetValue(inviteId, out var code))
            {
                return Task.FromResult((true, (string?)code, (string?)null));
            }
            return Task.FromResult((accept, (string?)null, (string?)null));
        }

        public Task ReportOfflineAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
    }

    private sealed class ScenarioSkinService : ISkinService
    {
        public SkinValidationResult ValidateSkinFile(string? filePath) => new(true);
        public ImageSource LoadSkinImage(string? skinPath) => SkinService.LoadDefaultSteveBitmap();
        public ImageSource ExtractHeadAvatar(string? skinPath) => SkinService.LoadDefaultSteveBitmap();
        public ImageSource ExtractFrontSkinPreview(string? skinPath) => SkinService.LoadDefaultSteveBitmap();
        public ImageSource ExtractBackSkinPreview(string? skinPath) => SkinService.LoadDefaultSteveBitmap();
        public Task SyncSkinToGameAsync(string? skinPath, string nickname, string gameDir, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ResetToDefaultSteveAsync(string nickname, string gameDir, CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task<SkinUploadResult> UploadSkinToLobbyApiAsync(string? skinPath, string nickname, string model, string? ownerToken, string? baseUrl = null, CancellationToken cancellationToken = default) => Task.FromResult(new SkinUploadResult(true, "token"));
        public Task<ImageSource> GetAvatarForPlayerAsync(string nickname, CancellationToken cancellationToken = default) => Task.FromResult<ImageSource>(SkinService.LoadDefaultSteveBitmap());
        public Task<(ImageSource SkinTexture, bool IsSlim)> GetSkinTextureForPlayerAsync(string nickname, CancellationToken cancellationToken = default) => Task.FromResult< (ImageSource SkinTexture, bool IsSlim) >((SkinService.LoadDefaultSteveBitmap(), false));
        public void EnsureCustomSkinLoaderConfig(string gameDir) { }
        public void ClearCustomSkinLoaderCache(string gameDir) { }
    }

    private static (LobbyViewModel LobbyVM, FriendsViewModel FriendsVM, ScenarioLobbyService LobbyService, ScenarioFriendService FriendService) CreateTestHarness()
    {
        var lobbyService = new ScenarioLobbyService();
        var friendService = new ScenarioFriendService();
        var skinService = new ScenarioSkinService();
        var configService = new TestConfigService(System.IO.Path.GetTempPath());
        configService.CurrentConfig.Nickname = "HostPlayer";
        var launchService = new TestLaunchService();

        var lobbyVm = new LobbyViewModel(lobbyService, launchService, configService, skinService: skinService);
        var friendsVm = new FriendsViewModel(friendService, lobbyService, skinService, lobbyVm);

        return (lobbyVm, friendsVm, lobbyService, friendService);
    }

    [Fact]
    public void SentInvites_StaleAccepted_IsIgnoredWhenNotInLobby()
    {
        var (lobbyVm, friendsVm, _, friendService) = CreateTestHarness();

        Assert.False(lobbyVm.IsInLobby);

        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "FriendOne", Online = true, Status = "online" }
            },
            SentInvites = new SentInviteItem[]
            {
                new() { InviteId = "stale-inv-1", FriendId = "f1", State = "accepted", LobbyCode = "OLD123" }
            }
        });

        Assert.Single(friendsVm.Friends);
        var friend = friendsVm.Friends[0];
        Assert.Null(friend.InviteState);
        Assert.Null(friend.InviteStateText);
        Assert.False(friend.HasInviteState);
        Assert.True(friend.CanInvite);
    }

    [Fact]
    public async Task SentInvites_StaleAccepted_IsIgnoredWhenUserIsGuestInLobby()
    {
        var (lobbyVm, friendsVm, _, friendService) = CreateTestHarness();

        await lobbyVm.JoinByCodeAsync("GUEST1");
        Assert.True(lobbyVm.IsInLobby);
        Assert.False(lobbyVm.IsHost);

        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "FriendOne", Online = true, Status = "online" }
            },
            SentInvites = new SentInviteItem[]
            {
                new() { InviteId = "stale-inv-2", FriendId = "f1", State = "accepted" }
            }
        });

        var friend = friendsVm.Friends[0];
        Assert.Null(friend.InviteState);
        Assert.False(friendsVm.IsLobbyActiveBannerVisible);
        Assert.True(friend.CanInvite);
    }

    [Fact]
    public async Task LeavingHostLobby_ImmediatelyClearsInviteState_AndDismissesStaleInviteAcrossNewLobbies()
    {
        var (lobbyVm, friendsVm, _, friendService) = CreateTestHarness();

        await lobbyVm.CreateLobbyCommand.ExecuteAsync(null);
        Assert.True(lobbyVm.IsInLobby);
        Assert.Equal("HOST01", lobbyVm.LobbyCode);

        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "FriendOne", Online = true, Status = "lobby", LobbyCode = "HOST01" }
            },
            SentInvites = new SentInviteItem[]
            {
                new() { InviteId = "inv-host01", FriendId = "f1", State = "accepted", LobbyCode = "HOST01" }
            }
        });

        var friend = friendsVm.Friends[0];
        Assert.Equal("accepted", friend.InviteState);
        Assert.Equal("Принял", friend.InviteStateText);
        Assert.True(friend.IsInMyLobby);

        // User leaves the lobby
        lobbyVm.LeaveLobbyCommand.Execute(null);

        Assert.False(lobbyVm.IsInLobby);
        Assert.Null(lobbyVm.LobbyCode);
        Assert.Null(friend.InviteState);
        Assert.False(friend.IsInMyLobby);
        Assert.False(friendsVm.IsLobbyActiveBannerVisible);

        // Server sync still returns the old accepted invite (even without LobbyCode)
        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "FriendOne", Online = true, Status = "online" }
            },
            SentInvites = new SentInviteItem[]
            {
                new() { InviteId = "inv-host01", FriendId = "f1", State = "accepted", LobbyCode = null }
            }
        });

        Assert.Null(friend.InviteState);
        Assert.True(friend.CanInvite);

        // User creates a brand new lobby HOST02, and old invite is still in Redis TTL
        await lobbyVm.CreateLobbyCommand.ExecuteAsync(null);
        Assert.Equal("HOST02", lobbyVm.LobbyCode);

        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "FriendOne", Online = true, Status = "online" }
            },
            SentInvites = new SentInviteItem[]
            {
                new() { InviteId = "inv-host01", FriendId = "f1", State = "accepted", LobbyCode = null }
            }
        });

        Assert.Null(friend.InviteState);
        Assert.True(friend.CanInvite);
    }

    [Fact]
    public async Task FriendAccepts_JoinsLobby_ThenLeavesLobby_ClearsAcceptedAndRestoresCanInvite()
    {
        var (lobbyVm, friendsVm, _, friendService) = CreateTestHarness();

        await lobbyVm.CreateLobbyCommand.ExecuteAsync(null);

        // Friend accepts and joins HOST01
        lobbyVm.LobbyPlayers.Add(new LobbyPlayerItem { Nickname = "FriendOne", IsHost = false });
        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "FriendOne", Online = true, Status = "lobby", LobbyCode = "HOST01" }
            },
            SentInvites = new SentInviteItem[]
            {
                new() { InviteId = "inv-100", FriendId = "f1", State = "accepted", LobbyCode = "HOST01" }
            }
        });

        var friend = friendsVm.Friends[0];
        Assert.True(friend.IsInMyLobby);
        Assert.Equal("accepted", friend.InviteState);
        Assert.False(friend.CanInvite);

        // Now FriendOne leaves the lobby
        var playerItem = lobbyVm.LobbyPlayers.First(p => p.Nickname == "FriendOne");
        lobbyVm.LobbyPlayers.Remove(playerItem);

        // Immediately upon leaving LobbyPlayers (when friend status becomes online), InviteState is cleared
        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "FriendOne", Online = true, Status = "online", LobbyCode = null }
            },
            SentInvites = new SentInviteItem[]
            {
                new() { InviteId = "inv-100", FriendId = "f1", State = "accepted", LobbyCode = "HOST01" }
            }
        });

        Assert.False(friend.IsInMyLobby);
        Assert.Null(friend.InviteState);
        Assert.True(friend.CanInvite);
    }

    [Fact]
    public async Task RapidClickInvite_OnSameAndMultipleFriends_CreatesOnlyOneLobby_AndSendsSingleInvitePerFriend()
    {
        var (lobbyVm, friendsVm, lobbyService, friendService) = CreateTestHarness();
        lobbyService.CreateDelay = TimeSpan.FromMilliseconds(60);

        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "Alpha", Online = true, Status = "online" },
                new() { Id = "f2", Nick = "Bravo", Online = true, Status = "online" },
                new() { Id = "f3", Nick = "Charlie", Online = true, Status = "online" }
            }
        });

        var f1 = friendsVm.Friends.First(f => f.Id == "f1");
        var f2 = friendsVm.Friends.First(f => f.Id == "f2");
        var f3 = friendsVm.Friends.First(f => f.Id == "f3");

        // Rapidly click Invite on f1 three times, and on f2 and f3 concurrently
        f1.InviteCommand.Execute(null);
        f1.InviteCommand.Execute(null);
        f1.InviteCommand.Execute(null);
        f2.InviteCommand.Execute(null);
        f3.InviteCommand.Execute(null);

        // Immediately after synchronous command execution, all three show "pending" ("Приглашён…")
        Assert.Equal("pending", f1.InviteState);
        Assert.Equal("pending", f2.InviteState);
        Assert.Equal("pending", f3.InviteState);
        Assert.False(f1.CanInvite);

        // Wait for background tasks to complete
        for (int i = 0; i < 40 && friendService.SendInviteCallCount < 3; i++)
        {
            await Task.Delay(20);
        }

        Assert.Equal(1, lobbyService.CreateCallCount);
        Assert.Equal(3, friendService.SendInviteCallCount);
        Assert.True(lobbyVm.IsInLobby);
        Assert.Equal("HOST01", lobbyVm.LobbyCode);
        Assert.All(friendService.SentInvitesLog, entry => Assert.Equal("HOST01", entry.LobbyCode));
    }

    [Fact]
    public async Task Guest_JoinLobby_SetsLobbyCode_DisplayCode_AndResetsHostFlags()
    {
        var (lobbyVm, _, _, _) = CreateTestHarness();

        lobbyVm.GuestCodeInput = "ab12cd";
        await lobbyVm.JoinLobbyCommand.ExecuteAsync(null);

        Assert.True(lobbyVm.IsInLobby);
        Assert.True(lobbyVm.IsGuestJoined);
        Assert.False(lobbyVm.IsLobbyCreated);
        Assert.False(lobbyVm.IsHost);
        Assert.Equal("AB12CD", lobbyVm.LobbyCode);
        Assert.Equal("AB12CD", lobbyVm.DisplayCode);
        Assert.Equal("A", lobbyVm.CodeChar0);
        Assert.Equal("B", lobbyVm.CodeChar1);
        Assert.Equal("1", lobbyVm.CodeChar2);
        Assert.Equal("2", lobbyVm.CodeChar3);
        Assert.Equal("C", lobbyVm.CodeChar4);
        Assert.Equal("D", lobbyVm.CodeChar5);
        Assert.True(lobbyVm.CopyCodeCommand.CanExecute(null));
        Assert.True(lobbyVm.LeaveLobbyCommand.CanExecute(null));
        Assert.False(lobbyVm.OpenWorldCommand.CanExecute(null));
        Assert.NotEmpty(lobbyVm.LobbyPlayers);
    }

    [Fact]
    public async Task SwitchFromHostLobby_ToGuestLobby_ViaFriendsJoinButton_ClosesHostAndJoinsFriendLobby()
    {
        var (lobbyVm, friendsVm, lobbyService, friendService) = CreateTestHarness();

        await lobbyVm.CreateLobbyCommand.ExecuteAsync(null);
        Assert.True(lobbyVm.IsInLobby);
        Assert.True(lobbyVm.IsHost);
        Assert.True(lobbyVm.IsLobbyCreated);
        Assert.Equal("HOST01", lobbyVm.LobbyCode);

        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "FriendHost", Online = true, Status = "lobby", LobbyCode = "FRND99" }
            }
        });

        var friend = friendsVm.Friends[0];
        Assert.True(friend.CanJoin);
        Assert.False(friend.CanInvite);

        bool openLobbyFired = false;
        friendsVm.OpenLobbyRequested += () => openLobbyFired = true;

        friend.JoinCommand.Execute(null);

        for (int i = 0; i < 40 && lobbyVm.LobbyCode != "FRND99"; i++)
        {
            await Task.Delay(20);
        }

        Assert.True(openLobbyFired);
        Assert.True(lobbyVm.IsInLobby);
        Assert.True(lobbyVm.IsGuestJoined);
        Assert.False(lobbyVm.IsLobbyCreated);
        Assert.False(lobbyVm.IsHost);
        Assert.Equal("FRND99", lobbyVm.LobbyCode);
        Assert.True(lobbyService.LeaveCallCount >= 1);

        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "FriendHost", Online = true, Status = "lobby", LobbyCode = "FRND99" }
            }
        });

        Assert.True(friend.IsInMyLobby);
        Assert.False(friend.CanJoin);
    }

    [Fact]
    public async Task JoinFriend_WithoutDirectLobbyCode_AcceptsIncomingInviteToJoin()
    {
        var (lobbyVm, friendsVm, _, friendService) = CreateTestHarness();
        friendService.InviteAcceptLobbyCodeByInviteId["inv-from-f1"] = "INV777";

        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "FriendHost", Online = true, Status = "lobby", LobbyCode = null }
            },
            Invites = new IncomingInviteItem[]
            {
                new() { InviteId = "inv-from-f1", FromId = "f1", FromNick = "FriendHost", Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }
            }
        });

        var friend = friendsVm.Friends[0];
        Assert.True(friend.CanJoin);

        friend.JoinCommand.Execute(null);

        for (int i = 0; i < 40 && lobbyVm.LobbyCode != "INV777"; i++)
        {
            await Task.Delay(20);
        }

        Assert.True(lobbyVm.IsInLobby);
        Assert.True(lobbyVm.IsGuestJoined);
        Assert.Equal("INV777", lobbyVm.LobbyCode);
    }

    private sealed class DelayedMockLobbyApiClient : ILobbyApiClient
    {
        public TimeSpan CloseDelay { get; set; } = TimeSpan.FromMilliseconds(80);
        public HttpStatusCode? DetailedStatusCode { get; set; } = HttpStatusCode.OK;
        public LobbyStatusResponse? DetailedStatusResponse { get; set; }

        public Task<LobbyCreateResponse?> CreateLobbyAsync(string hostName, IReadOnlyList<ModManifestEntry>? manifest = null, CancellationToken cancellationToken = default)
            => Task.FromResult<LobbyCreateResponse?>(new LobbyCreateResponse("FIRST1", "host-token-1", "waiting", 0));

        public Task<LobbyJoinResponse?> JoinLobbyAsync(string code, string playerName, IReadOnlyList<ModManifestEntry>? manifest = null, CancellationToken cancellationToken = default)
            => Task.FromResult<LobbyJoinResponse?>(new LobbyJoinResponse(true, code, "waiting", null, 2));

        public Task<bool> UpdateManifestAsync(string code, string playerName, IReadOnlyList<ModManifestEntry> manifest, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<LobbyStatusResponse?> GetStatusAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult(DetailedStatusResponse);

        public Task<(HttpStatusCode? StatusCode, LobbyStatusResponse? Response, string RawBody)> GetStatusDetailedAsync(string code, CancellationToken cancellationToken = default)
            => Task.FromResult((DetailedStatusCode, DetailedStatusResponse, string.Empty));

        public Task<bool> OpenLobbyAsync(string code, string hostToken, string tunnelAddress, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<bool> HeartbeatAsync(string code, string hostToken, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public async Task<bool> CloseLobbyAsync(string code, string hostToken, CancellationToken cancellationToken = default)
        {
            if (CloseDelay > TimeSpan.Zero)
            {
                await Task.Delay(CloseDelay, cancellationToken);
            }
            return true;
        }

        public Task<bool> LeaveLobbyAsync(string code, string playerName, CancellationToken cancellationToken = default)
            => Task.FromResult(true);

        public Task<TunnelConfigResponse?> GetTunnelConfigAsync(CancellationToken cancellationToken = default)
            => Task.FromResult<TunnelConfigResponse?>(new TunnelConfigResponse("secret", "127.0.0.1", 25565));
    }

    [Fact]
    public async Task LobbyService_RapidLeaveAndRejoin_DelayedCloseDoesNotWipeNewLobby()
    {
        var api = new DelayedMockLobbyApiClient { CloseDelay = TimeSpan.FromMilliseconds(100) };
        using var tunnel = new FakeTunnelProvider();
        using var service = new LobbyService(api, tunnel);

        var createdCode = await service.CreateLobbyAsHostAsync("HostPlayer");
        Assert.Equal("FIRST1", createdCode);
        Assert.True(service.IsHost);

        // Fire CloseLobbyAsHostAsync (which has a 100ms network delay) and immediately join another lobby
        var closeTask = service.CloseLobbyAsHostAsync();

        // State is reset synchronously before awaiting network close
        Assert.Null(service.CurrentLobbyCode);
        Assert.False(service.IsHost);

        bool joined = await service.JoinLobbyAsGuestAsync("SECOND", "HostPlayer");
        Assert.True(joined);
        Assert.Equal("SECOND", service.CurrentLobbyCode);
        Assert.Equal("waiting", service.CurrentStatus);

        // Wait for delayed CloseLobbyAsync HTTP call to finish
        await closeTask;

        // Delayed close must NOT overwrite the newly joined lobby!
        Assert.Equal("SECOND", service.CurrentLobbyCode);
        Assert.Equal("waiting", service.CurrentStatus);
        Assert.False(service.IsHost);
    }

    [Fact]
    public async Task LobbyService_GuestRefresh_WhenServerReturns404_TransitionsToClosed()
    {
        var api = new DelayedMockLobbyApiClient
        {
            DetailedStatusCode = HttpStatusCode.NotFound,
            DetailedStatusResponse = null
        };
        using var tunnel = new FakeTunnelProvider();
        using var service = new LobbyService(api, tunnel);

        await service.JoinLobbyAsGuestAsync("ROOM42", "GuestPlayer");
        Assert.Equal("waiting", service.CurrentStatus);

        string? observedStatus = null;
        service.StatusChanged += s => observedStatus = s;

        var result = await service.RefreshGuestStatusAsync();

        Assert.Null(result);
        Assert.Equal("closed", service.CurrentStatus);
        Assert.Equal("closed", observedStatus);
    }

    [Fact]
    public void FriendsCollection_DoesNotClearAndRebuild_WhenSyncOrderUnchanged()
    {
        var (_, friendsVm, _, friendService) = CreateTestHarness();

        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "Alpha", Online = true, Status = "online" },
                new() { Id = "f2", Nick = "Bravo", Online = false, Status = "offline" }
            }
        });

        Assert.Equal(2, friendsVm.Friends.Count);
        var originalF1 = friendsVm.Friends[0];

        int collectionChangeEvents = 0;
        friendsVm.Friends.CollectionChanged += (_, _) => collectionChangeEvents++;

        // Second sync with updated status for Alpha, but same order
        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "Alpha", Online = true, Status = "playing" },
                new() { Id = "f2", Nick = "Bravo", Online = false, Status = "offline" }
            }
        });

        Assert.Equal(0, collectionChangeEvents);
        Assert.Same(originalF1, friendsVm.Friends[0]);
        Assert.Equal("playing", friendsVm.Friends[0].Status);
        Assert.Equal("Играет", friendsVm.Friends[0].StatusText);
    }

    [Fact]
    public async Task RapidSwitch_CreateLeaveJoinCreateLeave_MaintainsConsistentState()
    {
        var (lobbyVm, friendsVm, _, friendService) = CreateTestHarness();

        friendService.EmitSync(new SyncResponse
        {
            Friends = new FriendPresenceItem[]
            {
                new() { Id = "f1", Nick = "Alpha", Online = true, Status = "online" }
            }
        });
        var friend = friendsVm.Friends[0];

        // 1. Create host lobby
        await lobbyVm.CreateLobbyAsync();
        Assert.True(lobbyVm.IsInLobby);
        Assert.True(lobbyVm.IsHost);
        Assert.True(lobbyVm.IsLobbyCreated);
        Assert.Equal("HOST01", lobbyVm.LobbyCode);
        Assert.True(friendsVm.IsLobbyActiveBannerVisible);

        // 2. Leave host lobby
        lobbyVm.LeaveLobbyCommand.Execute(null);
        Assert.False(lobbyVm.IsInLobby);
        Assert.False(lobbyVm.IsHost);
        Assert.False(lobbyVm.IsLobbyCreated);
        Assert.Null(lobbyVm.LobbyCode);
        Assert.False(friendsVm.IsLobbyActiveBannerVisible);

        // 3. Join guest lobby
        await lobbyVm.JoinByCodeAsync("GUEST9");
        Assert.True(lobbyVm.IsInLobby);
        Assert.False(lobbyVm.IsHost);
        Assert.False(lobbyVm.IsLobbyCreated);
        Assert.True(lobbyVm.IsGuestJoined);
        Assert.Equal("GUEST9", lobbyVm.LobbyCode);
        Assert.False(friendsVm.IsLobbyActiveBannerVisible);

        // 4. Directly create host lobby while in guest lobby (e.g. clicking Invite in Friends)
        await lobbyVm.CreateLobbyAsync();
        Assert.True(lobbyVm.IsInLobby);
        Assert.True(lobbyVm.IsHost);
        Assert.True(lobbyVm.IsLobbyCreated);
        Assert.False(lobbyVm.IsGuestJoined);
        Assert.Equal("HOST02", lobbyVm.LobbyCode);
        Assert.True(friendsVm.IsLobbyActiveBannerVisible);

        // 5. Leave again
        lobbyVm.LeaveLobbyCommand.Execute(null);
        Assert.False(lobbyVm.IsInLobby);
        Assert.Null(lobbyVm.LobbyCode);
        Assert.Null(friend.InviteState);
        Assert.True(friend.CanInvite);
    }
}

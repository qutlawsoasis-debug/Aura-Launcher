using System;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface IFriendService : IDisposable
{
    bool IsRegistered { get; }
    string? CurrentUserId { get; }
    string? CurrentFriendCode { get; }

    bool IsFriendsTabActive { get; set; }
    bool IsInLobby { get; set; }
    string? CurrentLobbyCode { get; set; }
    string? CurrentHostToken { get; set; }
    bool IsGameRunning { get; set; }

    event Action<SyncResponse>? SyncUpdated;
    event Action<IncomingInviteItem>? InviteReceived;
    event Action<FriendRequestItem>? FriendRequestReceived;

    void Start();
    void Stop();

    Task EnsureRegisteredAsync(CancellationToken cancellationToken = default);
    Task<SyncResponse?> SyncNowAsync(CancellationToken cancellationToken = default);
    Task<(bool Success, string? ErrorMessage)> SendFriendRequestAsync(string friendCode, CancellationToken cancellationToken = default);
    Task<bool> RespondFriendRequestAsync(string fromId, bool accept, CancellationToken cancellationToken = default);
    Task<bool> RemoveFriendAsync(string friendId, CancellationToken cancellationToken = default);
    Task<(bool Success, string? InviteId, string? ErrorMessage)> SendInviteAsync(string friendId, string lobbyCode, string hostToken, CancellationToken cancellationToken = default);
    Task<(bool Success, string? LobbyCode, string? ErrorMessage)> RespondInviteAsync(string inviteId, bool accept, CancellationToken cancellationToken = default);
}

using System;

namespace AuraLauncher.Services.Interfaces;

public interface INotificationService
{
    void Notify(string title, string message, string targetTab = "Overview");
    void NotifyLobbyInvite(string fromNick, string inviteId = "", string lobbyCode = "");
    void NotifyFriendRequest(string fromNick, string fromId = "", string friendCode = "");
    void NotifyPlayerJoinedLobby(string playerNick);
    void NotifyHostOpenedWorld();
    bool IsWindowVisibleAndFocused();
    void RegisterInAppToastHandler(Action<string, string> showToast);
}

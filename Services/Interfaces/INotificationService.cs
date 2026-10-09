using System;

namespace AuraLauncher.Services.Interfaces;

public interface INotificationService
{
    void Notify(string title, string message, string targetTab = "Overview", string type = "info");
    void NotifyLobbyInvite(string fromNick, string inviteId = "", string lobbyCode = "");
    void NotifyFriendRequest(string fromNick, string fromId = "", string friendCode = "");
    void NotifyPlayerJoinedLobby(string playerNick);
    void NotifyHostOpenedWorld();
    void NotifyScreenshotTaken(string filePath, string fileName);
    void NotifyGameCrash(int exitCode, string? reason = null);
    void NotifyKickedFromLobby();
    void NotifyUpdateAvailable(string updateTitle, string updateMessage);
    void NotifyIntegrityChecked(string statusText);
    bool IsWindowVisibleAndFocused();
    void RegisterInAppToastHandler(Action<string, string, string, string?> showToast);
    void RegisterInAppToastHandler(Action<string, string> showToast);
}

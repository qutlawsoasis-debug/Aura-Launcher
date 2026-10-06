using System;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class NotificationService : INotificationService
{
    private readonly IConfigService _configService;
    private Action<string, string>? _showInAppToastCallback;

    public NotificationService(IConfigService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public void RegisterInAppToastHandler(Action<string, string> showToast)
    {
        _showInAppToastCallback = showToast;
    }

    public bool IsWindowVisibleAndFocused()
    {
        try
        {
            if (Application.Current == null) return false;
            return Application.Current.Dispatcher.Invoke(() =>
            {
                var win = Application.Current.MainWindow;
                if (win == null) return false;
                return win.IsVisible && win.WindowState != WindowState.Minimized && win.IsActive;
            });
        }
        catch
        {
            return false;
        }
    }

    public void Notify(string title, string message, string targetTab = "Overview")
    {
        if (IsWindowVisibleAndFocused())
        {
            _showInAppToastCallback?.Invoke(title, message);
        }
        else
        {
            if (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true)
            {
                ShowWindowsToast(title, message, targetTab);
            }
        }
    }

    public void NotifyLobbyInvite(string fromNick, string inviteId = "", string lobbyCode = "")
    {
        if (IsWindowVisibleAndFocused())
        {
            // In-app invite toast is handled directly in MainViewModel
        }
        else
        {
            if (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true)
            {
                ShowWindowsToast("Приглашение в лобби", $"{fromNick} зовёт вас в игру", "Lobby");
            }
        }
    }

    public void NotifyFriendRequest(string fromNick, string fromId = "", string friendCode = "")
    {
        if (IsWindowVisibleAndFocused())
        {
            _showInAppToastCallback?.Invoke("Заявка в друзья", $"{fromNick} хочет добавить вас в друзья");
        }
        else
        {
            if (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true)
            {
                ShowWindowsToast("Заявка в друзья", $"{fromNick} хочет добавить вас в друзья", "Friends");
            }
        }
    }

    public void NotifyPlayerJoinedLobby(string playerNick)
    {
        if (IsWindowVisibleAndFocused())
        {
            _showInAppToastCallback?.Invoke("Игрок в лобби", $"{playerNick} зашёл в ваше лобби");
        }
        else
        {
            if (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true)
            {
                ShowWindowsToast("Игрок в лобби", $"{playerNick} зашёл в ваше лобби", "Lobby");
            }
        }
    }

    public void NotifyHostOpenedWorld()
    {
        if (IsWindowVisibleAndFocused())
        {
            _showInAppToastCallback?.Invoke("Мир открыт", "Хост открыл мир");
        }
        else
        {
            if (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true)
            {
                ShowWindowsToast("Мир открыт", "Хост открыл мир", "Lobby");
            }
        }
    }

    private void ShowWindowsToast(string title, string message, string targetTab)
    {
        try
        {
            new ToastContentBuilder()
                .AddText(title)
                .AddText(message)
                .AddArgument("action", "openTab")
                .AddArgument("tab", targetTab)
                .Show();
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[NOTIFICATION: WIN TOAST ERROR] {ex.Message}");
        }
    }
}

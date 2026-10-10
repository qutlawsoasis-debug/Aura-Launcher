using System;
using System.Windows;
using Microsoft.Toolkit.Uwp.Notifications;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class NotificationService : INotificationService
{
    private readonly IConfigService _configService;
    private Action<string, string, string, string?>? _showInAppToastCallback;

    public NotificationService(IConfigService configService)
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
    }

    public void RegisterInAppToastHandler(Action<string, string, string, string?> showToast)
    {
        _showInAppToastCallback = showToast;
    }

    public void RegisterInAppToastHandler(Action<string, string> showToast)
    {
        _showInAppToastCallback = (t, m, type, tab) => showToast(t, m);
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

    public void Notify(string title, string message, string targetTab = "Overview", string type = "info")
    {
        _showInAppToastCallback?.Invoke(title, message, type, targetTab);

        if (!IsWindowVisibleAndFocused() && (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true))
        {
            ShowWindowsToast(title, message, targetTab);
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
            _showInAppToastCallback?.Invoke("Заявка в друзья", $"{fromNick} хочет добавить вас в друзья", "friends", "Friends");
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
        _showInAppToastCallback?.Invoke("Игрок в лобби", $"{playerNick} зашёл в ваше лобби", "lobby", "Lobby");

        if (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true)
        {
            ShowWindowsToast("Игрок в лобби", $"{playerNick} зашёл в ваше лобби", "Lobby");
        }

        try
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch { }
    }

    public void NotifyHostOpenedWorld()
    {
        _showInAppToastCallback?.Invoke("Мир открыт!", "Хост открыл мир. Нажмите для подключения к игре", "lobby", "Lobby");

        if (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true)
        {
            ShowWindowsToast("Мир открыт!", "Хост открыл мир. Подключайтесь через лаунчер!", "Lobby");
        }

        try
        {
            System.Media.SystemSounds.Asterisk.Play();
        }
        catch { }
    }

    public void NotifyScreenshotTaken(string filePath, string fileName)
    {
        // Всегда показываем тост внутри лаунчера
        _showInAppToastCallback?.Invoke("Скриншот сохранён", $"{fileName} скопирован в буфер", "screenshot", "Workshop");

        // Если игра запущена или лаунчер не активен — шлем уведомление прямо поверх игры через Windows Toast
        if (!IsWindowVisibleAndFocused() && (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true))
        {
            ShowWindowsToast("Скриншот скопирован", $"{fileName} сохранён и помещён в буфер", "Workshop");
        }
    }

    public void NotifyGameCrash(int exitCode, string? reason = null)
    {
        string desc = !string.IsNullOrWhiteSpace(reason) ? reason : $"Игра завершилась с кодом {exitCode}";
        _showInAppToastCallback?.Invoke("Сбой Minecraft", desc, "warning", "Overview");

        if (!IsWindowVisibleAndFocused() && (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true))
        {
            ShowWindowsToast("Сбой Minecraft", desc, "Overview");
        }
    }

    public void NotifyKickedFromLobby()
    {
        _showInAppToastCallback?.Invoke("Исключение из лобби", "Вы были исключены хостом", "warning", "Lobby");

        if (!IsWindowVisibleAndFocused() && (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true))
        {
            ShowWindowsToast("Исключение из лобби", "Вы были исключены хостом", "Lobby");
        }
    }

    public void NotifyUpdateAvailable(string updateTitle, string updateMessage)
    {
        _showInAppToastCallback?.Invoke(updateTitle, updateMessage, "info", "Overview");

        if (!IsWindowVisibleAndFocused() && (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true))
        {
            ShowWindowsToast(updateTitle, updateMessage, "Overview");
        }
    }

    public void NotifyIntegrityChecked(string statusText)
    {
        _showInAppToastCallback?.Invoke("Целостность сборки", statusText, "success", "Settings");

        if (!IsWindowVisibleAndFocused() && (_configService.CurrentConfig?.WindowsNotificationsEnabled ?? true))
        {
            ShowWindowsToast("Целостность сборки", statusText, "Settings");
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

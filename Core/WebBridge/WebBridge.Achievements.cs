using System;
using System.Linq;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Core.WebBridge;

/// <summary>
/// Достижения: в новом окне мост сам сообщает сервису события (как MainViewModel в старом окне)
/// и пересылает в интерфейс разблокировки.
/// </summary>
public partial class WebBridge
{
    private string? _reportedLobbyCode;

    private void SubscribeAchievements()
    {
        if (_achievementService == null) return;
        _achievementService.AchievementUnlocked += OnAchievementUnlocked;
        _gameSessionService.GameStarted += OnGameStartedForAchievements;
    }

    private void UnsubscribeAchievements()
    {
        if (_achievementService == null) return;
        _achievementService.AchievementUnlocked -= OnAchievementUnlocked;
        _gameSessionService.GameStarted -= OnGameStartedForAchievements;
    }

    private void ReportAchievement(string eventId, double value = 1.0)
    {
        try { _achievementService?.Report(eventId, value); }
        catch (Exception ex) { _logger($"[WEBBRIDGE: WARN] achievement {eventId}: {ex.Message}"); }
    }

    private void OnGameStartedForAchievements(object? sender, System.Diagnostics.Process e) => ReportAchievement("game_launch");

    private void OnAchievementUnlocked(object? sender, AchievementDefinition def)
    {
        _logger($"[WEBBRIDGE] achievement unlocked: {def.Id}");
        SendEvent("achievement.unlocked", new { id = def.Id, title = def.Title, description = def.Description });
    }

    /// <summary>События лобби для достижений: «первое лобби» и «полный стол».</summary>
    private void ReportLobbyAchievements()
    {
        if (_lobbySession == null) return;
        if (_lobbySession.Role == LobbySessionRole.Host && !string.IsNullOrWhiteSpace(_lobbySession.CurrentLobbyCode)
            && _lobbySession.CurrentLobbyCode != _reportedLobbyCode)
        {
            _reportedLobbyCode = _lobbySession.CurrentLobbyCode;
            ReportAchievement("lobby_created");
        }
        if (_lobbySession.Role != LobbySessionRole.None)
        {
            ReportAchievement("lobby_players_count", _lobbySession.Players.Count);
        }
    }

    private void HandleAchievementsGet(BridgeMessage msg)
    {
        if (_achievementService == null)
        {
            SendError(msg.Id, msg.Name, "achievement_service_unavailable");
            return;
        }
        var progress = _achievementService.Progress;
        SendResponse(msg.Id, msg.Name, new
        {
            unlocked = _achievementService.UnlockedCount,
            total = _achievementService.TotalCount,
            items = _achievementService.Definitions.Select(d =>
            {
                progress.TryGetValue(d.Id, out var p);
                return new
                {
                    id = d.Id,
                    title = d.Title,
                    description = d.Description,
                    target = d.Target,
                    current = Math.Min(p?.CurrentValue ?? 0, d.Target),
                    unlocked = p?.Unlocked ?? false,
                    unlockedAt = p?.UnlockedAtUtc?.ToLocalTime().ToString("dd.MM.yyyy")
                };
            })
        });
    }
}

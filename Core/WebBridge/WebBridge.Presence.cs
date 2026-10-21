using System;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Core.WebBridge;

/// <summary>
/// Статус в Discord для нового окна (как MainViewModel): «В лаунчере», «В лобби», «В игре».
/// </summary>
public partial class WebBridge
{
    private void StartPresence()
    {
        if (_discordRpcService == null) return;
        try
        {
            _discordRpcService.Initialize();
            UpdatePresence();
        }
        catch (Exception ex) { _logger($"[WEBBRIDGE: WARN] discord rpc: {ex.Message}"); }
    }

    private void UpdatePresence()
    {
        if (_discordRpcService == null) return;
        try
        {
            if (_gameSessionService.IsGameRunning)
            {
                _discordRpcService.SetPlayingGame();
            }
            else if (_lobbySession != null && _lobbySession.Role != LobbySessionRole.None && _lobbySession.Players.Count > 0)
            {
                _discordRpcService.SetInLobby(_lobbySession.Players.Count, _lobbySession.HostNick, _lobbySession.CurrentLobbyCode);
            }
            else
            {
                _discordRpcService.SetInLauncher();
            }
        }
        catch (Exception ex) { _logger($"[WEBBRIDGE: WARN] discord rpc: {ex.Message}"); }
    }
}

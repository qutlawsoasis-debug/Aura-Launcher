using System;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Core.WebBridge;

/// <summary>
/// Команды и события раздела «Лобби» (поверх ILobbySessionService)
/// и приглашения друзей в лобби.
/// </summary>
public partial class WebBridge
{
    private const int LobbyMaxPlayers = 8;

    private void SubscribeLobby()
    {
        if (_lobbySession == null) return;
        _lobbySession.StateChanged += OnLobbyStateChanged;
        _lobbySession.LobbyUpdated += OnLobbyUpdated;
        _lobbySession.TunnelPingUpdated += OnLobbyPingUpdated;
        _lobbySession.PlayerKicked += OnLobbyKicked;
        _lobbySession.ErrorOccurred += OnLobbyError;
    }

    private void UnsubscribeLobby()
    {
        if (_lobbySession == null) return;
        _lobbySession.StateChanged -= OnLobbyStateChanged;
        _lobbySession.LobbyUpdated -= OnLobbyUpdated;
        _lobbySession.TunnelPingUpdated -= OnLobbyPingUpdated;
        _lobbySession.PlayerKicked -= OnLobbyKicked;
        _lobbySession.ErrorOccurred -= OnLobbyError;
    }

    private static string MapLobbyState(LobbySessionState state) => state switch
    {
        LobbySessionState.Idle => "idle",
        LobbySessionState.Creating => "creating",
        LobbySessionState.WaitingForWorld => "waitingForWorld",
        LobbySessionState.TunnelStarting => "tunnelStarting",
        LobbySessionState.Active => "active",
        LobbySessionState.Joining => "joining",
        LobbySessionState.ReadyToPlay => "readyToPlay",
        LobbySessionState.ConnectingToGame => "connecting",
        LobbySessionState.Error => "error",
        _ => "idle"
    };

    private object BuildLobbySnapshot()
    {
        var s = _lobbySession!;
        return new
        {
            role = s.Role switch
            {
                LobbySessionRole.Host => "host",
                LobbySessionRole.Guest => "guest",
                _ => "none"
            },
            state = MapLobbyState(s.State),
            code = s.CurrentLobbyCode,
            hostName = s.HostNick,
            tunnelAddress = s.CurrentTunnelAddress,
            pingMs = s.CurrentTunnelPingMs,
            statusHint = s.StatusHint,
            lastError = s.LastError,
            max = LobbyMaxPlayers,
            players = s.Players.Select(p => new
            {
                nick = p.Nickname,
                isHost = p.IsHost,
                isReady = p.IsReady,
                status = p.Status
            })
        };
    }

    private void SendLobbySnapshotEvent()
    {
        if (_lobbySession == null) return;
        SendEvent("lobby.updated", BuildLobbySnapshot());
    }

    private void OnLobbyStateChanged(object? sender, LobbySessionState state)
    {
        _logger($"[WEBBRIDGE] lobby state -> {MapLobbyState(state)}");
        SendLobbySnapshotEvent();
    }

    private void OnLobbyUpdated(object? sender, LobbyStatusDto dto) => SendLobbySnapshotEvent();

    private void OnLobbyPingUpdated(object? sender, int ms) => SendLobbySnapshotEvent();

    private void OnLobbyKicked(object? sender, string code)
    {
        _logger($"[WEBBRIDGE] lobby.kicked code={code}");
        SendEvent("lobby.kicked", new { code });
    }

    private void OnLobbyError(object? sender, string error)
    {
        _logger($"[WEBBRIDGE: WARN] lobby error: {error}");
        SendEvent("lobby.error", new { error });
    }

    private bool EnsureLobby(BridgeMessage msg)
    {
        if (_lobbySession != null) return true;
        SendError(msg.Id, msg.Name, "lobby_service_unavailable");
        return false;
    }

    private static string ReadString(BridgeMessage msg, string prop)
    {
        if (msg.Payload is JsonElement el && el.ValueKind == JsonValueKind.Object &&
            el.TryGetProperty(prop, out var v) && v.ValueKind == JsonValueKind.String)
        {
            return v.GetString()?.Trim() ?? string.Empty;
        }
        return string.Empty;
    }

    private bool IsInLobby => _lobbySession != null && _lobbySession.Role != LobbySessionRole.None;

    private void HandleLobbyGet(BridgeMessage msg)
    {
        if (!EnsureLobby(msg)) return;
        SendResponse(msg.Id, msg.Name, BuildLobbySnapshot());
    }

    private async Task HandleLobbyCreateAsync(BridgeMessage msg)
    {
        if (!EnsureLobby(msg)) return;
        if (IsInLobby)
        {
            SendError(msg.Id, msg.Name, "already_in_lobby");
            return;
        }

        string nick = _configService.CurrentConfig.Nickname?.Trim() ?? string.Empty;
        _logger($"[WEBBRIDGE] lobby.create host={nick}");
        string? code = await _lobbySession!.CreateLobbyAsync(nick);
        if (string.IsNullOrWhiteSpace(code))
        {
            SendError(msg.Id, msg.Name, _lobbySession.LastError ?? "create_failed");
            return;
        }

        _logger($"[WEBBRIDGE] lobby.create ok code={code}");
        SendResponse(msg.Id, msg.Name, BuildLobbySnapshot());
    }

    private void HandleLobbyOpenWorld(BridgeMessage msg)
    {
        if (!EnsureLobby(msg)) return;
        if (_lobbySession!.Role != LobbySessionRole.Host)
        {
            SendError(msg.Id, msg.Name, "not_host");
            return;
        }

        // Открытие мира ждёт запуска игры и «Открыть для сети», поэтому идёт в фоне;
        // прогресс приходит событиями lobby.updated.
        _logger("[WEBBRIDGE] lobby.openWorld");
        SendResponse(msg.Id, msg.Name, new { accepted = true });
        _ = Task.Run(async () =>
        {
            try { await _lobbySession.OpenWorldAsync(); }
            catch (Exception ex) { _logger($"[WEBBRIDGE: ERROR] lobby.openWorld: {ex.Message}"); }
        });
    }

    private async Task HandleLobbyJoinAsync(BridgeMessage msg)
    {
        if (!EnsureLobby(msg)) return;
        string code = ReadString(msg, "code").ToUpperInvariant();
        if (string.IsNullOrWhiteSpace(code))
        {
            SendError(msg.Id, msg.Name, "invalid_code");
            return;
        }
        if (IsInLobby)
        {
            SendError(msg.Id, msg.Name, "already_in_lobby");
            return;
        }

        string nick = _configService.CurrentConfig.Nickname?.Trim() ?? string.Empty;
        _logger($"[WEBBRIDGE] lobby.join code={code}");
        bool ok = await _lobbySession!.JoinByCodeAsync(code, nick);
        if (!ok)
        {
            string error = _lobbySession.LastError ?? "join_failed";
            _logger($"[WEBBRIDGE: WARN] lobby.join failed: {error}");
            SendError(msg.Id, msg.Name, error);
            return;
        }

        SendResponse(msg.Id, msg.Name, BuildLobbySnapshot());
    }

    private void HandleLobbyPlay(BridgeMessage msg)
    {
        if (!EnsureLobby(msg)) return;
        if (_lobbySession!.Role != LobbySessionRole.Guest)
        {
            SendError(msg.Id, msg.Name, "not_guest");
            return;
        }
        if (_gameSessionService.State != GameSessionState.Idle && _gameSessionService.State != GameSessionState.Failed)
        {
            SendError(msg.Id, msg.Name, "busy");
            return;
        }

        _logger("[WEBBRIDGE] lobby.play");
        SendResponse(msg.Id, msg.Name, new { accepted = true });
        _ = Task.Run(async () =>
        {
            try { await _lobbySession.ConnectToGameAsync(); }
            catch (Exception ex) { _logger($"[WEBBRIDGE: ERROR] lobby.play: {ex.Message}"); }
        });
    }

    private async Task HandleLobbyKickAsync(BridgeMessage msg)
    {
        if (!EnsureLobby(msg)) return;
        string nick = ReadString(msg, "nick");
        if (string.IsNullOrWhiteSpace(nick))
        {
            SendError(msg.Id, msg.Name, "invalid_nick");
            return;
        }
        if (_lobbySession!.Role != LobbySessionRole.Host)
        {
            SendError(msg.Id, msg.Name, "not_host");
            return;
        }

        _logger($"[WEBBRIDGE] lobby.kick {nick}");
        bool ok = await _lobbySession.KickPlayerAsync(nick);
        SendResponse(msg.Id, msg.Name, new { ok });
    }

    private async Task HandleLobbyLeaveAsync(BridgeMessage msg)
    {
        if (!EnsureLobby(msg)) return;
        _logger("[WEBBRIDGE] lobby.leave");
        await _lobbySession!.LeaveLobbyAsync();
        SendResponse(msg.Id, msg.Name, BuildLobbySnapshot());
    }

    private async Task HandleFriendsInviteAsync(BridgeMessage msg)
    {
        if (_friendService == null)
        {
            SendError(msg.Id, msg.Name, "friend_service_unavailable");
            return;
        }
        string friendId = ReadString(msg, "friendId");
        if (string.IsNullOrWhiteSpace(friendId))
        {
            SendError(msg.Id, msg.Name, "invalid_friend_id");
            return;
        }

        string? code = _lobbySession?.CurrentLobbyCode;
        if (_lobbySession?.Role != LobbySessionRole.Host || string.IsNullOrWhiteSpace(code))
        {
            SendError(msg.Id, msg.Name, "not_in_lobby");
            return;
        }

        _logger($"[WEBBRIDGE] friends.invite friend={friendId} code={code}");
        var (success, inviteId, error) = await _friendService.SendInviteAsync(friendId, code, _lobbySession.CurrentHostToken);
        SendResponse(msg.Id, msg.Name, new { ok = success, inviteId, error });
    }

    private async Task HandleFriendsRespondInviteAsync(BridgeMessage msg)
    {
        if (_friendService == null)
        {
            SendError(msg.Id, msg.Name, "friend_service_unavailable");
            return;
        }
        string inviteId = ReadString(msg, "inviteId");
        bool accept = msg.Payload is JsonElement el && el.ValueKind == JsonValueKind.Object &&
                      el.TryGetProperty("accept", out var a) && a.ValueKind == JsonValueKind.True;
        if (string.IsNullOrWhiteSpace(inviteId))
        {
            SendError(msg.Id, msg.Name, "invalid_invite_id");
            return;
        }

        _logger($"[WEBBRIDGE] friends.respondInvite {inviteId} accept={accept}");
        var (success, lobbyCode, error) = await _friendService.RespondInviteAsync(inviteId, accept);
        if (!success)
        {
            SendResponse(msg.Id, msg.Name, new { ok = false, error = error ?? "unknown_error" });
            return;
        }
        if (!accept || string.IsNullOrWhiteSpace(lobbyCode) || _lobbySession == null)
        {
            SendResponse(msg.Id, msg.Name, new { ok = true, joined = false });
            return;
        }
        if (IsInLobby)
        {
            SendResponse(msg.Id, msg.Name, new { ok = false, error = "already_in_lobby" });
            return;
        }

        string nick = _configService.CurrentConfig.Nickname?.Trim() ?? string.Empty;
        bool joined = await _lobbySession.JoinByCodeAsync(lobbyCode, nick);
        SendResponse(msg.Id, msg.Name, new { ok = joined, joined, code = lobbyCode, error = joined ? null : _lobbySession.LastError });
    }
}

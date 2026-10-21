using System;
using System.IO;
using System.Threading.Tasks;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Core.WebBridge;

/// <summary>
/// Смена ника игрока (повторяет WardrobeViewModel.ApplyNicknameChangeAsync).
/// </summary>
public partial class WebBridge
{
    private const string NicknameRuleText = "Ник: от 3 до 16 символов, латиница, цифры и _";

    private async Task HandleNicknameSetAsync(BridgeMessage msg)
    {
        string nick = ReadString(msg, "nickname");
        if (!NicknameValidator.Validate(nick).IsValid)
        {
            SendError(msg.Id, msg.Name, NicknameRuleText);
            return;
        }

        var cfg = _configService.CurrentConfig;
        if (string.Equals(cfg.Nickname, nick, StringComparison.Ordinal))
        {
            SendResponse(msg.Id, msg.Name, new { nickname = nick });
            return;
        }
        if (_gameSessionService.State != GameSessionState.Idle && _gameSessionService.State != GameSessionState.Failed)
        {
            SendError(msg.Id, msg.Name, "Нельзя сменить ник, пока игра запущена");
            return;
        }
        if (IsInLobby)
        {
            SendError(msg.Id, msg.Name, "Сначала выйдите из лобби");
            return;
        }

        _logger($"[WEBBRIDGE] nickname.set {cfg.Nickname} -> {nick}");
        if (_friendService != null)
        {
            var (success, error) = await _friendService.ChangeNicknameAsync(nick);
            if (!success)
            {
                _logger($"[WEBBRIDGE: WARN] nickname.set rejected by friends api: {error}");
                SendError(msg.Id, msg.Name, error ?? "Не удалось сменить ник");
                return;
            }
        }

        await _configService.UpdateConfigAsync(c => c.Nickname = nick);

        string? uploadError = null;
        if (_skinService != null)
        {
            try
            {
                await _skinService.SyncSkinToGameAsync(cfg.SkinPath, nick, cfg.GameDir);
                _skinService.ClearCustomSkinLoaderCache(cfg.GameDir);

                if (!string.IsNullOrWhiteSpace(cfg.SkinPath) && File.Exists(cfg.SkinPath))
                {
                    var res = await _skinService.UploadSkinToLobbyApiAsync(cfg.SkinPath, nick, cfg.SkinModel, cfg.SkinOwnerToken, cfg.LobbyApiBaseUrl);
                    if (res.Success && !string.IsNullOrWhiteSpace(res.OwnerToken))
                    {
                        await _configService.UpdateConfigAsync(c => c.SkinOwnerToken = res.OwnerToken!);
                    }
                    else if (!res.Success)
                    {
                        uploadError = res.ErrorMessage;
                    }
                }
            }
            catch (Exception ex)
            {
                uploadError = ex.Message;
                _logger($"[WEBBRIDGE: WARN] nickname.set skin resync: {ex.Message}");
            }
        }

        _logger($"[WEBBRIDGE] nickname.set ok {nick}");
        SendResponse(msg.Id, msg.Name, new { nickname = nick, uploadError });
    }
}

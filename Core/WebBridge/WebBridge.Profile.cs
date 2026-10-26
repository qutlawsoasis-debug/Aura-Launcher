using System;
using System.IO;
using System.Threading.Tasks;
using AuraLauncher.Services.Implementations;
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

    private static readonly System.Net.Http.HttpClient CapeHttp = new() { Timeout = TimeSpan.FromSeconds(15) };

    /// <summary>
    /// Плащ: сохраняется в lobby-api (CustomSkinLoader у всех игроков берёт его оттуда) и в конфиг.
    /// </summary>
    private async Task HandleSkinSetCapeAsync(BridgeMessage msg)
    {
        string cape = ReadString(msg, "cape");
        if (cape != "cyan" && cape != "silver" && cape != "none")
        {
            SendError(msg.Id, msg.Name, "invalid_value");
            return;
        }

        var cfg = _configService.CurrentConfig;
        string nick = cfg.Nickname?.Trim() ?? string.Empty;
        if (!NicknameValidator.Validate(nick).IsValid)
        {
            SendError(msg.Id, msg.Name, NicknameRuleText);
            return;
        }

        _logger($"[WEBBRIDGE] skin.setCape {cape}");
        try
        {
            string baseUrl = string.IsNullOrWhiteSpace(cfg.LobbyApiBaseUrl) ? "https://lobby-api.vercel.app" : cfg.LobbyApiBaseUrl.TrimEnd('/');
            string json = System.Text.Json.JsonSerializer.Serialize(new { nickname = nick, ownerToken = cfg.SkinOwnerToken, cape });
            using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{baseUrl}/api/skin") { Content = content };
            SkinService.AddUserAuthHeaders(req);
            using var resp = await CapeHttp.SendAsync(req);
            if (!resp.IsSuccessStatusCode)
            {
                string body = await resp.Content.ReadAsStringAsync();
                string error = (int)resp.StatusCode == 404 ? "Сначала загрузите свой скин" : $"Сервер скинов: {(int)resp.StatusCode}";
                try
                {
                    using var doc = System.Text.Json.JsonDocument.Parse(body);
                    if ((int)resp.StatusCode != 404 && doc.RootElement.TryGetProperty("error", out var e)) error = e.GetString() ?? error;
                }
                catch { }
                _logger($"[WEBBRIDGE: WARN] skin.setCape rejected: {(int)resp.StatusCode} {body}");
                SendError(msg.Id, msg.Name, error);
                return;
            }
        }
        catch (Exception ex)
        {
            _logger($"[WEBBRIDGE: WARN] skin.setCape: {ex.Message}");
            SendError(msg.Id, msg.Name, "Не удалось связаться с сервером скинов");
            return;
        }

        await _configService.UpdateConfigAsync(c =>
        {
            c.CapeId = cape;
            c.CapePath = "";
            c.CapeOriginalName = "";
        });

        if (_skinService != null)
        {
            await _skinService.SyncCapeToGameAsync(null, cfg.Nickname ?? "", cfg.GameDir);
            _skinService.ClearCustomSkinLoaderCache(cfg.GameDir);
        }

        SendResponse(msg.Id, msg.Name, new { cape });
    }
}

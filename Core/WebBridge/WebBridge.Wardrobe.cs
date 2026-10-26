using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;

namespace AuraLauncher.Core.WebBridge;

public partial class WebBridge
{
    private async Task HandleCapeUploadAsync(BridgeMessage msg)
    {
        if (_skinService == null)
        {
            _logger("[WEBBRIDGE: ERROR] cape.upload failed: ISkinService not available");
            SendError(msg.Id, msg.Name, "service_unavailable");
            return;
        }

        string fileName = string.Empty;
        string base64Data = string.Empty;

        if (msg.Payload is JsonElement el)
        {
            if (el.TryGetProperty("fileName", out var fnProp) && fnProp.ValueKind == JsonValueKind.String)
                fileName = fnProp.GetString() ?? string.Empty;

            if (el.TryGetProperty("base64Data", out var b64Prop) && b64Prop.ValueKind == JsonValueKind.String)
                base64Data = b64Prop.GetString() ?? string.Empty;
        }

        if (string.IsNullOrWhiteSpace(base64Data))
        {
            _logger("[WEBBRIDGE: WARN] cape.upload rejected: empty base64Data");
            SendError(msg.Id, msg.Name, "invalid_payload");
            return;
        }

        int commaIdx = base64Data.IndexOf(',');
        if (commaIdx >= 0)
        {
            base64Data = base64Data.Substring(commaIdx + 1);
        }

        byte[] rawBytes;
        try
        {
            rawBytes = Convert.FromBase64String(base64Data);
        }
        catch
        {
            _logger("[WEBBRIDGE: WARN] cape.upload rejected: invalid base64");
            SendError(msg.Id, msg.Name, "invalid_base64");
            return;
        }

        if (rawBytes.Length > 1024 * 1024)
        {
            _logger($"[WEBBRIDGE: WARN] cape.upload rejected: payload exceeds 1MB ({rawBytes.Length} bytes)");
            SendError(msg.Id, msg.Name, "file_too_large");
            return;
        }

        if (rawBytes.Length < 8 ||
            rawBytes[0] != 0x89 || rawBytes[1] != 0x50 || rawBytes[2] != 0x4E || rawBytes[3] != 0x47 ||
            rawBytes[4] != 0x0D || rawBytes[5] != 0x0A || rawBytes[6] != 0x1A || rawBytes[7] != 0x0A)
        {
            _logger("[WEBBRIDGE: WARN] cape.upload rejected: not a PNG file");
            SendError(msg.Id, msg.Name, "invalid_format");
            return;
        }

        var validation = _skinService.ValidateCapeBytes(rawBytes);
        if (!validation.IsValid)
        {
            string errMsg = validation.ErrorMessage ?? "Неподдерживаемый файл плаща.";
            _logger($"[WEBBRIDGE: WARN] cape.upload validation failed: {errMsg}");
            SendError(msg.Id, msg.Name, errMsg);
            return;
        }

        string safeFileName = Path.GetFileName(fileName);
        if (string.IsNullOrWhiteSpace(safeFileName))
            safeFileName = $"cape_{Guid.NewGuid():N}.png";

        // Сохранение в managed storage с SHA-256 дедупликацией
        var (managedPath, hash) = await _skinService.SaveToManagedStorageAsync(rawBytes, "cape", safeFileName);

        var cfg = _configService.CurrentConfig;
        cfg.CapeId = "custom";
        cfg.CapePath = managedPath;
        cfg.CapeOriginalName = safeFileName;

        // Обновляем историю гардероба
        cfg.WardrobeHistory ??= new List<WardrobeItem>();
        string itemId = $"cape_{hash}";
        cfg.WardrobeHistory.RemoveAll(x => string.Equals(x.Hash, hash, StringComparison.OrdinalIgnoreCase) && x.Type == "cape");

        var historyItem = new WardrobeItem
        {
            Id = itemId,
            Type = "cape",
            Name = safeFileName,
            Hash = hash,
            RelativePath = Path.Combine("capes", $"{hash}.png").Replace('\\', '/'),
            CreatedUtc = DateTime.UtcNow
        };
        cfg.WardrobeHistory.Insert(0, historyItem);

        await _configService.SaveConfigAsync(cfg);

        // Синхронизируем в локальную игру
        await _skinService.SyncCapeToGameAsync(managedPath, cfg.Nickname, cfg.GameDir);

        // В lobby-api отключаем серверные плащи (cyan/silver), так как сервер не принимает кастомный PNG
        _ = SetLobbyApiCapeAsync("none");

        _logger($"[WEBBRIDGE] cape.upload finished: ok=true, id={itemId}, file={safeFileName}");
        SendResponse(msg.Id, msg.Name, new
        {
            ok = true,
            id = itemId,
            fileName = safeFileName,
            cape = "custom",
            dataUrl = $"data:image/png;base64,{Convert.ToBase64String(rawBytes)}",
            note = "Кастомный плащ применён (виден вам в игре)"
        });
    }

    private void HandleWardrobeGetHistory(BridgeMessage msg)
    {
        var cfg = _configService.CurrentConfig;
        var rawList = cfg.WardrobeHistory ?? new List<WardrobeItem>();

        var skins = new List<object>();
        var capes = new List<object>();

        foreach (var item in rawList)
        {
            if (item == null || string.IsNullOrWhiteSpace(item.Id))
                continue;

            if (_skinService == null || !_skinService.TryResolveWardrobeItemPath(item, out string fullPath))
                continue;

            string? dataUrl = null;
            try
            {
                byte[] bytes = File.ReadAllBytes(fullPath);
                dataUrl = $"data:image/png;base64,{Convert.ToBase64String(bytes)}";
            }
            catch
            {
                continue;
            }

            if (item.Type == "cape")
            {
                bool isCurrent = cfg.CapeId == "custom" && string.Equals(cfg.CapePath, fullPath, StringComparison.OrdinalIgnoreCase);
                capes.Add(new
                {
                    id = item.Id,
                    type = "cape",
                    name = item.Name,
                    hash = item.Hash,
                    dataUrl = dataUrl,
                    isCurrent = isCurrent,
                    createdUtc = item.CreatedUtc
                });
            }
            else
            {
                bool isCurrent = string.Equals(cfg.SkinPath, fullPath, StringComparison.OrdinalIgnoreCase);
                skins.Add(new
                {
                    id = item.Id,
                    type = "skin",
                    name = item.Name,
                    hash = item.Hash,
                    model = item.Model ?? "default",
                    dataUrl = dataUrl,
                    isCurrent = isCurrent,
                    createdUtc = item.CreatedUtc
                });
            }
        }

        SendResponse(msg.Id, msg.Name, new
        {
            skins = skins,
            capes = capes
        });
    }

    private async Task HandleWardrobeApplyAsync(BridgeMessage msg)
    {
        string id = ReadString(msg, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            SendError(msg.Id, msg.Name, "invalid_id");
            return;
        }

        var cfg = _configService.CurrentConfig;
        var item = cfg.WardrobeHistory?.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            SendError(msg.Id, msg.Name, "Элемент не найден в истории");
            return;
        }

        if (_skinService == null || !_skinService.TryResolveWardrobeItemPath(item, out string fullPath))
        {
            SendError(msg.Id, msg.Name, "Файл не найден в хранилище профиля");
            return;
        }

        if (item.Type == "cape")
        {
            cfg.CapeId = "custom";
            cfg.CapePath = fullPath;
            cfg.CapeOriginalName = item.Name;
            await _configService.SaveConfigAsync(cfg);

            if (_skinService != null)
            {
                await _skinService.SyncCapeToGameAsync(fullPath, cfg.Nickname, cfg.GameDir);
            }

            _ = SetLobbyApiCapeAsync("none");

            _logger($"[WEBBRIDGE] wardrobe.apply cape {id} ({item.Name})");
            SendResponse(msg.Id, msg.Name, new { ok = true, type = "cape", id = item.Id, name = item.Name });
        }
        else
        {
            cfg.SkinPath = fullPath;
            cfg.SkinOriginalName = item.Name;
            cfg.SkinModel = item.Model ?? "default";
            await _configService.SaveConfigAsync(cfg);

            string? uploadError = null;
            if (_skinService != null)
            {
                await _skinService.SyncSkinToGameAsync(fullPath, cfg.Nickname, cfg.GameDir);
                try
                {
                    var res = await _skinService.UploadSkinToLobbyApiAsync(
                        fullPath,
                        cfg.Nickname,
                        cfg.SkinModel,
                        cfg.SkinOwnerToken,
                        cfg.LobbyApiBaseUrl);

                    if (res.Success && !string.IsNullOrWhiteSpace(res.OwnerToken))
                    {
                        cfg.SkinOwnerToken = res.OwnerToken;
                        await _configService.SaveConfigAsync(cfg);
                    }
                    else if (!res.Success)
                    {
                        uploadError = res.ErrorMessage;
                    }
                }
                catch (Exception ex)
                {
                    uploadError = ex.Message;
                }
            }

            _logger($"[WEBBRIDGE] wardrobe.apply skin {id} ({item.Name})");
            SendResponse(msg.Id, msg.Name, new
            {
                ok = true,
                type = "skin",
                id = item.Id,
                name = item.Name,
                model = cfg.SkinModel,
                uploadError = uploadError
            });
        }
    }

    private async Task HandleWardrobeDeleteAsync(BridgeMessage msg)
    {
        string id = ReadString(msg, "id");
        if (string.IsNullOrWhiteSpace(id))
        {
            SendError(msg.Id, msg.Name, "invalid_id");
            return;
        }

        var cfg = _configService.CurrentConfig;
        var item = cfg.WardrobeHistory?.FirstOrDefault(x => string.Equals(x.Id, id, StringComparison.OrdinalIgnoreCase));
        if (item == null)
        {
            SendResponse(msg.Id, msg.Name, new { ok = true, id });
            return;
        }

        cfg.WardrobeHistory!.Remove(item);

        string fullPath = "";
        bool hasFile = _skinService != null && _skinService.TryResolveWardrobeItemPath(item, out fullPath);

        // Если это был текущий выбранный элемент, сбрасываем выбор
        if (item.Type == "cape")
        {
            if (cfg.CapeId == "custom" && (string.Equals(cfg.CapePath, fullPath, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(cfg.CapePath)))
            {
                cfg.CapeId = "none";
                cfg.CapePath = "";
                cfg.CapeOriginalName = "";
                if (_skinService != null)
                {
                    await _skinService.SyncCapeToGameAsync(null, cfg.Nickname, cfg.GameDir);
                }
            }
        }
        else
        {
            if (string.Equals(cfg.SkinPath, fullPath, StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(cfg.SkinPath))
            {
                cfg.SkinPath = "";
                cfg.SkinOriginalName = "";
                if (_skinService != null)
                {
                    await _skinService.ResetToDefaultSteveAsync(cfg.Nickname, cfg.GameDir);
                }
            }
        }

        await _configService.SaveConfigAsync(cfg);

        // Удаление файла с диска ТОЛЬКО если нет других записей с тем же хешем
        if (hasFile && !string.IsNullOrWhiteSpace(fullPath) && File.Exists(fullPath))
        {
            bool referencedByOther = cfg.WardrobeHistory.Any(x => string.Equals(x.Hash, item.Hash, StringComparison.OrdinalIgnoreCase));
            if (!referencedByOther)
            {
                try
                {
                    string profileDir = _skinService!.GetProfileDataDir();
                    string safeBase = Path.GetFullPath(Path.Combine(profileDir, item.Type == "cape" ? "capes" : "skins"));
                    if (fullPath.StartsWith(safeBase + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
                    {
                        File.Delete(fullPath);
                        _logger($"[WEBBRIDGE] wardrobe.delete: удален файл {fullPath}");
                    }
                }
                catch (Exception ex)
                {
                    _logger($"[WEBBRIDGE: WARN] wardrobe.delete: сбой удаления файла {fullPath}: {ex.Message}");
                }
            }
        }

        _logger($"[WEBBRIDGE] wardrobe.delete {id}");
        SendResponse(msg.Id, msg.Name, new { ok = true, id });
    }

    private async Task SetLobbyApiCapeAsync(string cape)
    {
        try
        {
            var cfg = _configService.CurrentConfig;
            string nick = cfg.Nickname?.Trim() ?? string.Empty;
            if (!NicknameValidator.Validate(nick).IsValid) return;

            string baseUrl = string.IsNullOrWhiteSpace(cfg.LobbyApiBaseUrl) ? "https://lobby-api.vercel.app" : cfg.LobbyApiBaseUrl.TrimEnd('/');
            string json = JsonSerializer.Serialize(new { nickname = nick, ownerToken = cfg.SkinOwnerToken, cape });
            using var content = new System.Net.Http.StringContent(json, System.Text.Encoding.UTF8, "application/json");
            using var req = new System.Net.Http.HttpRequestMessage(System.Net.Http.HttpMethod.Post, $"{baseUrl}/api/skin") { Content = content };
            SkinService.AddUserAuthHeaders(req);
            using var resp = await CapeHttp.SendAsync(req);
        }
        catch (Exception ex)
        {
            _logger($"[WEBBRIDGE: WARN] SetLobbyApiCapeAsync({cape}): {ex.Message}");
        }
    }
}

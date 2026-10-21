using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Core.WebBridge;

/// <summary>
/// Облачные миры на сервере (повторяет облачную часть LobbyViewModel):
/// список, создание, запуск и подключение, остановка, удаление, доступ друзьям, загрузка своего мира.
/// </summary>
public partial class WebBridge
{
    private bool _isUploadingWorld;

    private bool EnsureCloud(BridgeMessage msg)
    {
        if (_cloudLobbyService != null) return true;
        SendError(msg.Id, msg.Name, "Облачные миры недоступны");
        return false;
    }

    private string? ValidNickOrError(BridgeMessage msg)
    {
        string nick = _configService.CurrentConfig.Nickname?.Trim() ?? string.Empty;
        if (NicknameValidator.Validate(nick).IsValid) return nick;
        SendError(msg.Id, msg.Name, "Укажите корректный ник в Гардеробе");
        return null;
    }

    private object MapCloudWorld(CloudWorldItem w) => new
    {
        id = w.Id,
        name = w.Name,
        owner = w.Owner,
        isOwner = w.IsOwner,
        seed = w.Seed,
        status = w.Status,
        stageText = w.StageText,
        progress = w.Progress,
        port = w.Port,
        online = w.OnlinePlayers ?? new(),
        max = w.MaxPlayers,
        allowed = w.AllowedPlayers ?? new(),
        lastPlayed = w.LastPlayed
    };

    private async Task<CloudWorldItem?> FindCloudWorldAsync(string nick, string id)
    {
        var resp = await _cloudLobbyService!.GetWorldsAsync(nick);
        return resp?.Worlds?.FirstOrDefault(w => string.Equals(w.Id, id, StringComparison.OrdinalIgnoreCase));
    }

    private async Task HandleCloudListAsync(BridgeMessage msg)
    {
        if (!EnsureCloud(msg)) return;
        string? nick = ValidNickOrError(msg);
        if (nick == null) return;

        var resp = await _cloudLobbyService!.GetWorldsAsync(nick);
        SendResponse(msg.Id, msg.Name, new
        {
            maxActive = resp?.MaxActive ?? 0,
            activeCount = resp?.ActiveCount ?? 0,
            worlds = (resp?.Worlds ?? new()).Select(MapCloudWorld)
        });
    }

    private async Task HandleCloudCreateAsync(BridgeMessage msg)
    {
        if (!EnsureCloud(msg)) return;
        string? nick = ValidNickOrError(msg);
        if (nick == null) return;

        string name = ReadString(msg, "name");
        string seed = ReadString(msg, "seed");
        if (string.IsNullOrWhiteSpace(name))
        {
            SendError(msg.Id, msg.Name, "Укажите название мира");
            return;
        }

        _logger($"[WEBBRIDGE] cloud.create '{name}'");
        var res = await _cloudLobbyService!.CreateOrStartWorldAsync(nick, name, string.IsNullOrWhiteSpace(seed) ? null : seed);
        if (res == null || !res.Port.HasValue)
        {
            SendError(msg.Id, msg.Name, res?.Error ?? "Не удалось создать мир");
            return;
        }
        SendResponse(msg.Id, msg.Name, new { worldId = res.WorldId, port = res.Port, status = res.Status });
    }

    private async Task HandleCloudStartAsync(BridgeMessage msg)
    {
        if (!EnsureCloud(msg)) return;
        string? nick = ValidNickOrError(msg);
        if (nick == null) return;

        var world = await FindCloudWorldAsync(nick, ReadString(msg, "id"));
        if (world == null)
        {
            SendError(msg.Id, msg.Name, "Мир не найден");
            return;
        }

        _logger($"[WEBBRIDGE] cloud.start {world.Id}");
        var res = await _cloudLobbyService!.CreateOrStartWorldAsync(world.Owner, world.Name, world.Seed, world.Id);
        if (res == null || !res.Port.HasValue)
        {
            SendError(msg.Id, msg.Name, res?.Error ?? "Ошибка запуска");
            return;
        }
        SendResponse(msg.Id, msg.Name, new { port = res.Port, status = res.Status });
    }

    private async Task HandleCloudConnectAsync(BridgeMessage msg)
    {
        if (!EnsureCloud(msg)) return;
        string? nick = ValidNickOrError(msg);
        if (nick == null) return;
        if (_gameSessionService.State != GameSessionState.Idle && _gameSessionService.State != GameSessionState.Failed)
        {
            SendError(msg.Id, msg.Name, "Игра уже запущена");
            return;
        }

        var world = await FindCloudWorldAsync(nick, ReadString(msg, "id"));
        if (world == null || !world.IsRunning || !world.Port.HasValue)
        {
            SendError(msg.Id, msg.Name, "Мир ещё не запущен");
            return;
        }

        string address = $"{_cloudLobbyService!.ServerHost}:{world.Port.Value}";
        _logger($"[WEBBRIDGE] cloud.connect {world.Id} -> {address}");
        SendResponse(msg.Id, msg.Name, new { accepted = true, address });
        _ = Task.Run(async () =>
        {
            try { await _gameSessionService.LaunchAsync(new LaunchOptions { QuickPlayMultiplayer = address }); }
            catch (Exception ex) { _logger($"[WEBBRIDGE: ERROR] cloud.connect: {ex.Message}"); }
        });
    }

    private async Task HandleCloudStopAsync(BridgeMessage msg)
    {
        if (!EnsureCloud(msg)) return;
        string? nick = ValidNickOrError(msg);
        if (nick == null) return;

        var world = await FindCloudWorldAsync(nick, ReadString(msg, "id"));
        if (world == null)
        {
            SendError(msg.Id, msg.Name, "Мир не найден");
            return;
        }
        _logger($"[WEBBRIDGE] cloud.stop {world.Id}");
        bool ok = await _cloudLobbyService!.StopWorldAsync(world.Id, world.Port);
        SendResponse(msg.Id, msg.Name, new { ok });
    }

    private async Task HandleCloudDeleteAsync(BridgeMessage msg)
    {
        if (!EnsureCloud(msg)) return;
        string? nick = ValidNickOrError(msg);
        if (nick == null) return;

        var world = await FindCloudWorldAsync(nick, ReadString(msg, "id"));
        if (world == null || !world.IsOwner)
        {
            SendError(msg.Id, msg.Name, "Удалить мир может только владелец");
            return;
        }
        _logger($"[WEBBRIDGE] cloud.delete {world.Id}");
        var res = await _cloudLobbyService!.DeleteWorldAsync(world.Owner, world.Id);
        if (res == null || !res.Success)
        {
            SendError(msg.Id, msg.Name, res?.Error ?? "Не удалось удалить мир");
            return;
        }
        SendResponse(msg.Id, msg.Name, new { ok = true });
    }

    private async Task HandleCloudShareAsync(BridgeMessage msg)
    {
        if (!EnsureCloud(msg)) return;
        string? nick = ValidNickOrError(msg);
        if (nick == null) return;

        string id = ReadString(msg, "id");
        string target = ReadString(msg, "nick");
        bool add = msg.Payload is JsonElement el && el.ValueKind == JsonValueKind.Object &&
                   el.TryGetProperty("add", out var a) && a.ValueKind == JsonValueKind.True;
        if (string.IsNullOrWhiteSpace(target))
        {
            SendError(msg.Id, msg.Name, "invalid_nick");
            return;
        }

        _logger($"[WEBBRIDGE] cloud.share {id} {(add ? "+" : "-")}{target}");
        var res = await _cloudLobbyService!.ShareWorldAsync(nick, id, target, add ? "add" : "remove");
        if (res == null || !res.Success)
        {
            SendError(msg.Id, msg.Name, res?.Error ?? "Не удалось изменить доступ");
            return;
        }
        SendResponse(msg.Id, msg.Name, new { ok = true, allowed = res.AllowedPlayers ?? new() });
    }

    private async Task HandleWorldsLocalAsync(BridgeMessage msg)
    {
        if (_workshopService == null)
        {
            SendError(msg.Id, msg.Name, "workshop_service_unavailable");
            return;
        }
        var saves = await _workshopService.GetWorldSavesAsync(GetInstanceDir());
        SendResponse(msg.Id, msg.Name, new
        {
            worlds = saves.Select(s => new
            {
                folder = s.FolderName,
                name = s.DisplayName,
                size = s.SizeFormatted,
                lastPlayed = s.LastPlayedFormatted,
                mode = s.GameMode,
                playtime = s.PlaytimeFormatted,
                day = s.DayFormatted,
                backups = s.Backups.Select(b => new { file = b.FileName, created = b.CreatedAt.ToString("dd.MM.yyyy HH:mm"), size = b.SizeFormatted })
            })
        });
    }

    /// <summary>Загрузка своего мира на сервер (как LobbyViewModel.UploadLocalWorldAsync, но из папки инстанса).</summary>
    private async Task HandleCloudUploadAsync(BridgeMessage msg)
    {
        if (!EnsureCloud(msg) || _workshopService == null) return;
        string? nick = ValidNickOrError(msg);
        if (nick == null) return;
        if (_isUploadingWorld)
        {
            SendError(msg.Id, msg.Name, "busy");
            return;
        }

        string folder = ReadString(msg, "folder");
        var saves = await _workshopService.GetWorldSavesAsync(GetInstanceDir());
        var save = saves.FirstOrDefault(s => string.Equals(s.FolderName, folder, StringComparison.OrdinalIgnoreCase));
        if (save == null || !Directory.Exists(save.FolderPath))
        {
            SendError(msg.Id, msg.Name, "Папка мира не найдена");
            return;
        }

        _isUploadingWorld = true;
        string tempZip = Path.Combine(Path.GetTempPath(), $"aura_upload_{Guid.NewGuid():N}.zip");
        try
        {
            _logger($"[WEBBRIDGE] cloud.upload '{save.DisplayName}'");
            SendEvent("cloud.uploadProgress", new { text = "Упаковываем мир…" });
            await Task.Run(() =>
            {
                using var zipStream = File.Create(tempZip);
                using var archive = new ZipArchive(zipStream, ZipArchiveMode.Create, true);
                bool hasUsercache = false;
                foreach (var file in new DirectoryInfo(save.FolderPath).GetFiles("*", SearchOption.AllDirectories))
                {
                    if (string.Equals(file.Name, "session.lock", StringComparison.OrdinalIgnoreCase)) continue;
                    if (string.Equals(file.Name, "usercache.json", StringComparison.OrdinalIgnoreCase)) hasUsercache = true;
                    string rel = Path.GetRelativePath(save.FolderPath, file.FullName).Replace('\\', '/');
                    archive.CreateEntryFromFile(file.FullName, rel, CompressionLevel.Fastest);
                }
                string rootCache = Path.Combine(GetInstanceDir(), "usercache.json");
                if (!hasUsercache && File.Exists(rootCache))
                {
                    archive.CreateEntryFromFile(rootCache, "usercache.json", CompressionLevel.Fastest);
                }
            });

            SendEvent("cloud.uploadProgress", new { text = "Загружаем на сервер…" });
            var uploaded = await _cloudLobbyService!.UploadWorldAsync(nick, save.DisplayName, tempZip);
            if (uploaded == null)
            {
                SendError(msg.Id, msg.Name, "Не удалось загрузить мир на сервер");
                return;
            }
            SendResponse(msg.Id, msg.Name, MapCloudWorld(uploaded));
        }
        catch (Exception ex)
        {
            _logger($"[WEBBRIDGE: ERROR] cloud.upload: {ex.Message}");
            SendError(msg.Id, msg.Name, $"Ошибка загрузки: {ex.Message}");
        }
        finally
        {
            _isUploadingWorld = false;
            try { if (File.Exists(tempZip)) File.Delete(tempZip); } catch { }
        }
    }
}

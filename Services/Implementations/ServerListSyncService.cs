using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;
using fNbt;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Сервис синхронизации управляемых серверов сборки в uncompressed big-endian NBT servers.dat.
/// Безопасен для чужих пользовательских серверов и неизвестных тегов NBT.
/// </summary>
public class ServerListSyncService : IServerListSyncService
{
    private static bool _sessionBackupDone;
    private readonly IGameLaunchService? _launchService;

    public ServerListSyncService(IGameLaunchService? launchService = null)
    {
        _launchService = launchService;
    }

    public static void ResetSessionBackupFlag()
    {
        _sessionBackupDone = false;
    }

    public static bool IsValidServerAddress(string? address)
    {
        if (string.IsNullOrWhiteSpace(address)) return false;
        address = address.Trim();
        if (address.Length > 255) return false;

        if (address.Any(c => char.IsWhiteSpace(c) || c == '"' || c == '\'' || char.IsControl(c)))
            return false;

        if (address.StartsWith('[') && address.Contains(']'))
        {
            int closeBracket = address.IndexOf(']');
            string hostPart = address.Substring(1, closeBracket - 1);
            string rest = address.Substring(closeBracket + 1);
            if (string.IsNullOrWhiteSpace(hostPart)) return false;
            if (string.IsNullOrEmpty(rest)) return true;
            if (!rest.StartsWith(':')) return false;
            string portPart = rest.Substring(1);
            return int.TryParse(portPart, out int port) && port >= 1 && port <= 65535;
        }

        var parts = address.Split(':');
        if (parts.Length == 1)
        {
            return !string.IsNullOrWhiteSpace(parts[0]);
        }
        else if (parts.Length == 2)
        {
            var host = parts[0];
            var portStr = parts[1];
            if (string.IsNullOrWhiteSpace(host)) return false;
            return int.TryParse(portStr, out int port) && port >= 1 && port <= 65535;
        }

        return false;
    }

    public Task SyncServersAsync(
        string gameDir,
        IReadOnlyList<ManifestServerEntry>? manifestServers,
        string? customStateFilePath = null,
        CancellationToken cancellationToken = default)
    {
        // 8. Любая ошибка синхронизации серверов не блокирует запуск игры
        try
        {
            SyncServersInternal(gameDir, manifestServers, customStateFilePath, cancellationToken);
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SERVER-SYNC] skipped: unexpected error: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private void SyncServersInternal(
        string gameDir,
        IReadOnlyList<ManifestServerEntry>? manifestServers,
        string? customStateFilePath,
        CancellationToken cancellationToken)
    {
        // 0. Отсутствие поля servers в манифесте -> ничего не менять
        if (manifestServers == null)
        {
            return;
        }

        // Синхронизация НИКОГДА не выполняется при запущенной игре
        if (_launchService != null && _launchService.IsGameRunning)
        {
            FabricGameLaunchService.LogLauncherEvent("[SERVER-SYNC] skipped: game is currently running");
            return;
        }

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(gameDir))
        {
            return;
        }

        var normalizedGameDir = Path.GetFullPath(gameDir);
        if (!Directory.Exists(normalizedGameDir))
        {
            Directory.CreateDirectory(normalizedGameDir);
        }

        var serversDatPath = Path.Combine(normalizedGameDir, "servers.dat");

        // Фильтрация и валидация записей манифеста (IP-адреса в лог не писать!)
        var validManifestServers = new List<ManifestServerEntry>();
        foreach (var entry in manifestServers)
        {
            if (entry == null || string.IsNullOrWhiteSpace(entry.Id))
            {
                FabricGameLaunchService.LogLauncherEvent("[SERVER-SYNC] skipped: missing server id");
                continue;
            }

            if (!IsValidServerAddress(entry.Address))
            {
                FabricGameLaunchService.LogLauncherEvent($"[SERVER-SYNC] skipped: invalid server address format for id '{entry.Id}'");
                continue;
            }

            validManifestServers.Add(entry);
        }

        // Загрузка состояния pack-state.json
        var packState = PackState.LoadValidState(normalizedGameDir, customStateFilePath) ?? new PackState
        {
            GameDir = normalizedGameDir
        };

        var previousManagedIds = new HashSet<string>(packState.ManagedServers ?? new List<string>(), StringComparer.OrdinalIgnoreCase);
        var currentManifestIds = new HashSet<string>(validManifestServers.Select(s => s.Id), StringComparer.OrdinalIgnoreCase);

        var nbtFile = new NbtFile();

        // 1. Файла нет: создать с нуля.
        // 2. Файл есть: прочитать, сохранив все чужие записи и неизвестные теги нетронутыми.
        if (File.Exists(serversDatPath))
        {
            try
            {
                nbtFile.LoadFromFile(serversDatPath, NbtCompression.None, null);
            }
            catch (Exception ex)
            {
                // 7. Если файл повреждён и не читается: НЕ затирать. Сохранить копию servers.dat.corrupt-<timestamp>
                var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss");
                var corruptPath = serversDatPath + $".corrupt-{timestamp}";
                try
                {
                    File.Copy(serversDatPath, corruptPath, overwrite: true);
                }
                catch { }

                FabricGameLaunchService.LogLauncherEvent($"[SERVER-SYNC] skipped: servers.dat corrupt, backup created ({ex.Message})");
                return;
            }
        }

        if (nbtFile.RootTag == null)
        {
            nbtFile.RootTag = new NbtCompound("");
        }

        var serversList = nbtFile.RootTag["servers"] as NbtList;
        if (serversList == null)
        {
            serversList = new NbtList("servers", NbtTagType.Compound);
            nbtFile.RootTag["servers"] = serversList;
        }

        int addedCount = 0;
        int updatedCount = 0;
        int unchangedCount = 0;
        int removedCount = 0;

        // 4. Для каждой записи манифеста:
        foreach (var server in validManifestServers)
        {
            // Поиск существующей записи:
            // 1) По прямому совпадению id в compound
            // 2) Если id нет, но id был в managedServers и совпадают name или ip
            NbtCompound? existing = null;
            foreach (var item in serversList)
            {
                if (item is NbtCompound comp)
                {
                    var idTag = comp["id"] as NbtString;
                    if (idTag != null && string.Equals(idTag.Value, server.Id, StringComparison.OrdinalIgnoreCase))
                    {
                        existing = comp;
                        break;
                    }
                }
            }

            if (existing == null && previousManagedIds.Contains(server.Id))
            {
                foreach (var item in serversList)
                {
                    if (item is NbtCompound comp)
                    {
                        var nameTag = comp["name"] as NbtString;
                        var ipTag = comp["ip"] as NbtString;
                        if ((nameTag != null && string.Equals(nameTag.Value, server.Name, StringComparison.Ordinal)) ||
                            (ipTag != null && string.Equals(ipTag.Value, server.Address, StringComparison.OrdinalIgnoreCase)))
                        {
                            existing = comp;
                            break;
                        }
                    }
                }
            }

            if (existing != null)
            {
                bool nameMatches = string.Equals(existing["name"]?.StringValue, server.Name, StringComparison.Ordinal);
                bool ipMatches = string.Equals(existing["ip"]?.StringValue, server.Address, StringComparison.OrdinalIgnoreCase);
                bool idMatches = existing["id"] != null && string.Equals(existing["id"]?.StringValue, server.Id, StringComparison.OrdinalIgnoreCase);

                if (nameMatches && ipMatches && idMatches)
                {
                    unchangedCount++;
                }
                else
                {
                    existing["name"] = new NbtString("name", server.Name);
                    existing["ip"] = new NbtString("ip", server.Address);
                    existing["id"] = new NbtString("id", server.Id);
                    updatedCount++;
                }
            }
            else
            {
                // Нет: вставить В НАЧАЛО списка
                var newCompound = new NbtCompound
                {
                    new NbtString("name", server.Name),
                    new NbtString("ip", server.Address),
                    new NbtString("id", server.Id)
                };
                serversList.Insert(0, newCompound);
                addedCount++;
            }
        }

        // 5. Если id был в managedServers, но исчез из манифеста: удалить запись
        var idsToRemove = previousManagedIds.Where(id => !currentManifestIds.Contains(id)).ToList();
        foreach (var removedId in idsToRemove)
        {
            var matching = serversList
                .OfType<NbtCompound>()
                .Where(c => string.Equals(c["id"]?.StringValue, removedId, StringComparison.OrdinalIgnoreCase))
                .ToList();

            foreach (var comp in matching)
            {
                serversList.Remove(comp);
                removedCount++;
            }
        }

        // Обновляем список managedServers в состоянии сборки
        packState.ManagedServers = currentManifestIds.ToList();
        PackState.SaveState(packState, customStateFilePath);

        // 6. Запись атомарная: во временный файл, затем замена
        // Идемпотентность: если ничего не изменилось и файл уже существует, не трогаем файл на диске
        bool hasChanges = addedCount > 0 || updatedCount > 0 || removedCount > 0 || !File.Exists(serversDatPath);

        if (hasChanges)
        {
            // Перед первой модификацией делается servers.dat.aura.bak (один раз за сессию)
            if (File.Exists(serversDatPath) && !_sessionBackupDone)
            {
                try
                {
                    File.Copy(serversDatPath, serversDatPath + ".aura.bak", overwrite: true);
                    _sessionBackupDone = true;
                }
                catch { }
            }

            var tmpPath = serversDatPath + $".{Guid.NewGuid():N}.tmp";
            try
            {
                nbtFile.SaveToFile(tmpPath, NbtCompression.None);
                File.Move(tmpPath, serversDatPath, overwrite: true);
            }
            finally
            {
                if (File.Exists(tmpPath))
                {
                    try { File.Delete(tmpPath); } catch { }
                }
            }
        }

        FabricGameLaunchService.LogLauncherEvent(
            $"[SERVER-SYNC] added: {addedCount}, updated: {updatedCount}, removed: {removedCount}, unchanged: {unchangedCount}");
    }

    public const string ActiveLobbyServerId = "aura-active-lobby";

    public Task UpsertActiveLobbyServerAsync(
        string gameDir,
        string tunnelAddress,
        string? hostName = null,
        string? lobbyCode = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(gameDir) || !IsValidServerAddress(tunnelAddress))
            {
                return Task.CompletedTask;
            }

            cancellationToken.ThrowIfCancellationRequested();

            var normalizedGameDir = Path.GetFullPath(gameDir);
            Directory.CreateDirectory(normalizedGameDir);
            var serversDatPath = Path.Combine(normalizedGameDir, "servers.dat");

            var nbtFile = new NbtFile();
            if (File.Exists(serversDatPath))
            {
                try
                {
                    nbtFile.LoadFromFile(serversDatPath, NbtCompression.None, null);
                }
                catch (Exception ex)
                {
                    FabricGameLaunchService.LogLauncherEvent($"[SERVER-SYNC] active lobby upsert skipped: servers.dat unreadable ({ex.Message})");
                    return Task.CompletedTask;
                }
            }

            if (nbtFile.RootTag == null)
            {
                nbtFile.RootTag = new NbtCompound("");
            }

            var serversList = nbtFile.RootTag["servers"] as NbtList;
            if (serversList == null)
            {
                serversList = new NbtList("servers", NbtTagType.Compound);
                nbtFile.RootTag["servers"] = serversList;
            }

            string cleanAddress = tunnelAddress.Trim();
            string displayName;
            if (!string.IsNullOrWhiteSpace(hostName) && !string.IsNullOrWhiteSpace(lobbyCode))
            {
                displayName = $"Лобби Aura: {hostName.Trim()} [{lobbyCode.Trim()}]";
            }
            else if (!string.IsNullOrWhiteSpace(hostName))
            {
                displayName = $"Лобби Aura: {hostName.Trim()}";
            }
            else if (!string.IsNullOrWhiteSpace(lobbyCode))
            {
                displayName = $"Лобби Aura [{lobbyCode.Trim()}]";
            }
            else
            {
                displayName = "Лобби Aura";
            }

            // Проверяем, не стоит ли уже на 0-м месте точно такая же запись
            if (serversList.Count > 0 && serversList[0] is NbtCompound firstComp)
            {
                bool sameId = string.Equals(firstComp["id"]?.StringValue, ActiveLobbyServerId, StringComparison.OrdinalIgnoreCase);
                bool sameName = string.Equals(firstComp["name"]?.StringValue, displayName, StringComparison.Ordinal);
                bool sameIp = string.Equals(firstComp["ip"]?.StringValue, cleanAddress, StringComparison.OrdinalIgnoreCase);
                if (sameId && sameName && sameIp && File.Exists(serversDatPath))
                {
                    return Task.CompletedTask;
                }
            }

            // Удаляем старые записи активного лобби
            for (int i = serversList.Count - 1; i >= 0; i--)
            {
                if (serversList[i] is NbtCompound comp)
                {
                    string? idVal = comp["id"]?.StringValue;
                    string? nameVal = comp["name"]?.StringValue;
                    if (string.Equals(idVal, ActiveLobbyServerId, StringComparison.OrdinalIgnoreCase) ||
                        (nameVal != null && nameVal.StartsWith("Лобби Aura", StringComparison.OrdinalIgnoreCase)))
                    {
                        serversList.RemoveAt(i);
                    }
                }
            }

            var lobbyCompound = new NbtCompound
            {
                new NbtString("name", displayName),
                new NbtString("ip", cleanAddress),
                new NbtString("id", ActiveLobbyServerId),
                new NbtByte("acceptTextures", 1),
                new NbtByte("hidden", 0)
            };

            serversList.Insert(0, lobbyCompound);
            SaveServersDatAtomically(serversDatPath, nbtFile);
            FabricGameLaunchService.LogLauncherEvent($"[SERVER-SYNC] Active lobby server updated at index 0: '{displayName}'");
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SERVER-SYNC] UpsertActiveLobbyServer error: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    public Task RemoveActiveLobbyServerAsync(
        string gameDir,
        CancellationToken cancellationToken = default)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(gameDir))
            {
                return Task.CompletedTask;
            }

            var normalizedGameDir = Path.GetFullPath(gameDir);
            var serversDatPath = Path.Combine(normalizedGameDir, "servers.dat");
            if (!File.Exists(serversDatPath))
            {
                return Task.CompletedTask;
            }

            var nbtFile = new NbtFile();
            try
            {
                nbtFile.LoadFromFile(serversDatPath, NbtCompression.None, null);
            }
            catch
            {
                return Task.CompletedTask;
            }

            var serversList = nbtFile.RootTag?["servers"] as NbtList;
            if (serversList == null || serversList.Count == 0)
            {
                return Task.CompletedTask;
            }

            int removed = 0;
            for (int i = serversList.Count - 1; i >= 0; i--)
            {
                if (serversList[i] is NbtCompound comp)
                {
                    string? idVal = comp["id"]?.StringValue;
                    string? nameVal = comp["name"]?.StringValue;
                    if (string.Equals(idVal, ActiveLobbyServerId, StringComparison.OrdinalIgnoreCase) ||
                        (nameVal != null && nameVal.StartsWith("Лобби Aura", StringComparison.OrdinalIgnoreCase)))
                    {
                        serversList.RemoveAt(i);
                        removed++;
                    }
                }
            }

            if (removed > 0)
            {
                SaveServersDatAtomically(serversDatPath, nbtFile);
                FabricGameLaunchService.LogLauncherEvent($"[SERVER-SYNC] Removed {removed} active lobby server entry(s) from servers.dat");
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[SERVER-SYNC] RemoveActiveLobbyServer error: {ex.Message}");
        }

        return Task.CompletedTask;
    }

    private static void SaveServersDatAtomically(string serversDatPath, NbtFile nbtFile)
    {
        if (File.Exists(serversDatPath) && !_sessionBackupDone)
        {
            try
            {
                File.Copy(serversDatPath, serversDatPath + ".aura.bak", overwrite: true);
                _sessionBackupDone = true;
            }
            catch { }
        }

        var tmpPath = serversDatPath + $".{Guid.NewGuid():N}.tmp";
        try
        {
            nbtFile.SaveToFile(tmpPath, NbtCompression.None);
            File.Move(tmpPath, serversDatPath, overwrite: true);
        }
        finally
        {
            if (File.Exists(tmpPath))
            {
                try { File.Delete(tmpPath); } catch { }
            }
        }
    }
}

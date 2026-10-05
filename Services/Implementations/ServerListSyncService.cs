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
}

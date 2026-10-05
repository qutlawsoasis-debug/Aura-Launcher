using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using fNbt;
using Xunit;

namespace AuraLauncher.Tests;

public class ServerListSyncServiceTests : IDisposable
{
    private readonly string _testDir;
    private readonly string _stateFilePath;

    public ServerListSyncServiceTests()
    {
        _testDir = Path.Combine(Path.GetTempPath(), "aura_tests_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_testDir);
        _stateFilePath = Path.Combine(_testDir, "pack-state.json");
        ServerListSyncService.ResetSessionBackupFlag();
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testDir))
            {
                Directory.Delete(_testDir, recursive: true);
            }
        }
        catch { }
    }

    [Fact]
    public async Task SyncServers_FileDoesNotExist_CreatesFromScratch()
    {
        var service = new ServerListSyncService();
        var manifestServers = new List<ManifestServerEntry>
        {
            new() { Id = "aura-main", Name = "Aura", Address = "192.168.1.10:25565" }
        };

        await service.SyncServersAsync(_testDir, manifestServers, _stateFilePath);

        var serversDat = Path.Combine(_testDir, "servers.dat");
        Assert.True(File.Exists(serversDat));

        var nbt = new NbtFile();
        nbt.LoadFromFile(serversDat, NbtCompression.None, null);
        var list = nbt.RootTag["servers"] as NbtList;
        Assert.NotNull(list);
        Assert.Single(list);

        var comp = list[0] as NbtCompound;
        Assert.NotNull(comp);
        Assert.Equal("Aura", comp["name"]?.StringValue);
        Assert.Equal("192.168.1.10:25565", comp["ip"]?.StringValue);
        Assert.Equal("aura-main", comp["id"]?.StringValue);

        var state = PackState.LoadValidState(_testDir, _stateFilePath);
        Assert.NotNull(state);
        Assert.Contains("aura-main", state.ManagedServers);
    }

    [Fact]
    public async Task SyncServers_ExistingUserServers_PreservesUserServersOrderAndUnknownTags()
    {
        var serversDat = Path.Combine(_testDir, "servers.dat");
        var nbt = new NbtFile();
        var root = new NbtCompound("");
        var list = new NbtList("servers", NbtTagType.Compound);

        var userComp1 = new NbtCompound
        {
            new NbtString("name", "Friend Realm"),
            new NbtString("ip", "friend.net:25565"),
            new NbtString("unknownCustomTag", "customValue123"),
            new NbtByte("acceptTextures", 1)
        };
        var userComp2 = new NbtCompound
        {
            new NbtString("name", "Hypixel"),
            new NbtString("ip", "mc.hypixel.net")
        };
        list.Add(userComp1);
        list.Add(userComp2);
        root.Add(list);
        nbt.RootTag = root;
        nbt.SaveToFile(serversDat, NbtCompression.None);

        var service = new ServerListSyncService();
        var manifestServers = new List<ManifestServerEntry>
        {
            new() { Id = "aura-main", Name = "Aura", Address = "play.aura.org" }
        };

        await service.SyncServersAsync(_testDir, manifestServers, _stateFilePath);

        var reloaded = new NbtFile();
        reloaded.LoadFromFile(serversDat, NbtCompression.None, null);
        var reloadedList = reloaded.RootTag["servers"] as NbtList;
        Assert.NotNull(reloadedList);
        Assert.Equal(3, reloadedList.Count);

        // Index 0: inserted at the beginning
        var auraComp = reloadedList[0] as NbtCompound;
        Assert.NotNull(auraComp);
        Assert.Equal("Aura", auraComp["name"]?.StringValue);
        Assert.Equal("play.aura.org", auraComp["ip"]?.StringValue);
        Assert.Equal("aura-main", auraComp["id"]?.StringValue);

        // Index 1: original user server 1 preserved intact with unknown tag
        var preserved1 = reloadedList[1] as NbtCompound;
        Assert.NotNull(preserved1);
        Assert.Equal("Friend Realm", preserved1["name"]?.StringValue);
        Assert.Equal("friend.net:25565", preserved1["ip"]?.StringValue);
        Assert.Equal("customValue123", preserved1["unknownCustomTag"]?.StringValue);
        Assert.Equal((byte)1, preserved1["acceptTextures"]?.ByteValue);

        // Index 2: original user server 2
        var preserved2 = reloadedList[2] as NbtCompound;
        Assert.NotNull(preserved2);
        Assert.Equal("Hypixel", preserved2["name"]?.StringValue);
    }

    [Fact]
    public async Task SyncServers_Idempotency_RepeatRunDoesNotModifyFileBytes()
    {
        var service = new ServerListSyncService();
        var manifestServers = new List<ManifestServerEntry>
        {
            new() { Id = "aura-main", Name = "Aura", Address = "play.aura.org" }
        };

        await service.SyncServersAsync(_testDir, manifestServers, _stateFilePath);

        var serversDat = Path.Combine(_testDir, "servers.dat");
        var firstBytes = File.ReadAllBytes(serversDat);

        // Повторный запуск
        await service.SyncServersAsync(_testDir, manifestServers, _stateFilePath);
        var secondBytes = File.ReadAllBytes(serversDat);

        Assert.Equal(firstBytes, secondBytes);
    }

    [Fact]
    public async Task SyncServers_AddressChanged_UpdatesAddress()
    {
        var service = new ServerListSyncService();
        var initial = new List<ManifestServerEntry>
        {
            new() { Id = "aura-main", Name = "Aura", Address = "old.aura.org:25565" }
        };
        await service.SyncServersAsync(_testDir, initial, _stateFilePath);

        var updated = new List<ManifestServerEntry>
        {
            new() { Id = "aura-main", Name = "Aura Official", Address = "new.aura.org:25565" }
        };
        await service.SyncServersAsync(_testDir, updated, _stateFilePath);

        var serversDat = Path.Combine(_testDir, "servers.dat");
        var nbt = new NbtFile();
        nbt.LoadFromFile(serversDat, NbtCompression.None, null);
        var list = nbt.RootTag["servers"] as NbtList;
        Assert.NotNull(list);
        Assert.Single(list);

        var comp = list[0] as NbtCompound;
        Assert.NotNull(comp);
        Assert.Equal("Aura Official", comp["name"]?.StringValue);
        Assert.Equal("new.aura.org:25565", comp["ip"]?.StringValue);
    }

    [Fact]
    public async Task SyncServers_IdRemovedFromManifest_DeletesEntryAndRemovesFromState()
    {
        var service = new ServerListSyncService();
        var initial = new List<ManifestServerEntry>
        {
            new() { Id = "aura-main", Name = "Aura", Address = "play.aura.org" },
            new() { Id = "aura-event", Name = "Aura Event", Address = "event.aura.org" }
        };
        await service.SyncServersAsync(_testDir, initial, _stateFilePath);

        var current = new List<ManifestServerEntry>
        {
            new() { Id = "aura-main", Name = "Aura", Address = "play.aura.org" }
        };
        await service.SyncServersAsync(_testDir, current, _stateFilePath);

        var serversDat = Path.Combine(_testDir, "servers.dat");
        var nbt = new NbtFile();
        nbt.LoadFromFile(serversDat, NbtCompression.None, null);
        var list = nbt.RootTag["servers"] as NbtList;
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal("aura-main", ((NbtCompound)list[0])["id"]?.StringValue);

        var state = PackState.LoadValidState(_testDir, _stateFilePath);
        Assert.NotNull(state);
        Assert.DoesNotContain("aura-event", state.ManagedServers);
        Assert.Contains("aura-main", state.ManagedServers);
    }

    [Fact]
    public async Task SyncServers_CorruptedFile_DoesNotOverwriteOriginalAndCreatesCorruptBackup()
    {
        var serversDat = Path.Combine(_testDir, "servers.dat");
        var corruptData = new byte[] { 0xDE, 0xAD, 0xBE, 0xEF, 0x01, 0x02, 0x03, 0x04 };
        File.WriteAllBytes(serversDat, corruptData);

        var service = new ServerListSyncService();
        var manifest = new List<ManifestServerEntry>
        {
            new() { Id = "aura-main", Name = "Aura", Address = "play.aura.org" }
        };

        // Не должно выбрасывать исключение
        await service.SyncServersAsync(_testDir, manifest, _stateFilePath);

        // Оригинальный файл не затерт
        Assert.Equal(corruptData, File.ReadAllBytes(serversDat));

        // Создана копия corrupt
        var corruptFiles = Directory.GetFiles(_testDir, "servers.dat.corrupt-*");
        Assert.Single(corruptFiles);
        Assert.Equal(corruptData, File.ReadAllBytes(corruptFiles[0]));
    }

    [Fact]
    public async Task SyncServers_InvalidAddressInManifest_SkipsInvalidAndProcessesValid()
    {
        var service = new ServerListSyncService();
        var manifest = new List<ManifestServerEntry>
        {
            new() { Id = "bad-space", Name = "Bad 1", Address = "invalid host with spaces:25565" },
            new() { Id = "bad-port", Name = "Bad 2", Address = "host:99999" },
            new() { Id = "good", Name = "Good Server", Address = "valid.server.com:25565" }
        };

        await service.SyncServersAsync(_testDir, manifest, _stateFilePath);

        var serversDat = Path.Combine(_testDir, "servers.dat");
        var nbt = new NbtFile();
        nbt.LoadFromFile(serversDat, NbtCompression.None, null);
        var list = nbt.RootTag["servers"] as NbtList;
        Assert.NotNull(list);
        Assert.Single(list);
        Assert.Equal("good", ((NbtCompound)list[0])["id"]?.StringValue);
    }

    [Fact]
    public async Task SyncServers_NullServersInManifest_DoesNothing()
    {
        var serversDat = Path.Combine(_testDir, "servers.dat");
        var nbt = new NbtFile();
        var root = new NbtCompound("");
        var list = new NbtList("servers", NbtTagType.Compound);
        list.Add(new NbtCompound { new NbtString("name", "UserServer"), new NbtString("ip", "1.2.3.4") });
        root.Add(list);
        nbt.RootTag = root;
        nbt.SaveToFile(serversDat, NbtCompression.None);

        var originalBytes = File.ReadAllBytes(serversDat);

        var service = new ServerListSyncService();
        await service.SyncServersAsync(_testDir, null, _stateFilePath);

        Assert.Equal(originalBytes, File.ReadAllBytes(serversDat));
    }

    [Theory]
    [InlineData("play.aura.org", true)]
    [InlineData("play.aura.org:25565", true)]
    [InlineData("192.168.1.1", true)]
    [InlineData("192.168.1.1:25565", true)]
    [InlineData("localhost", true)]
    [InlineData("localhost:1", true)]
    [InlineData("localhost:65535", true)]
    [InlineData("[::1]:25565", true)]
    [InlineData("[2001:db8::1]", true)]
    [InlineData(null, false)]
    [InlineData("", false)]
    [InlineData("   ", false)]
    [InlineData("host:0", false)]
    [InlineData("host:65536", false)]
    [InlineData("host:-1", false)]
    [InlineData("host:notaport", false)]
    [InlineData("host with spaces", false)]
    [InlineData("host:123:456", false)]
    public void IsValidServerAddress_ValidatesCorrectly(string? address, bool expected)
    {
        bool result = ServerListSyncService.IsValidServerAddress(address);
        Assert.Equal(expected, result);
    }
}

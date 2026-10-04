using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using Xunit;

namespace AuraLauncher.Tests;

public class TestConfigService : IConfigService
{
    public LauncherConfig CurrentConfig { get; set; }

    public event EventHandler<LauncherConfig>? ConfigChanged;

    public TestConfigService(string gameDir, string packRepo = PackUpdateService.DefaultPackRepo)
    {
        CurrentConfig = new LauncherConfig
        {
            GameDir = gameDir,
            PackRepo = packRepo,
            Nickname = "TestPlayer"
        };
    }

    public Task<LauncherConfig> LoadConfigAsync(CancellationToken cancellationToken = default) => Task.FromResult(CurrentConfig);

    public Task SaveConfigAsync(LauncherConfig config, CancellationToken cancellationToken = default)
    {
        CurrentConfig = config;
        ConfigChanged?.Invoke(this, config);
        return Task.CompletedTask;
    }

    public Task UpdateConfigAsync(Action<LauncherConfig> updateAction, CancellationToken cancellationToken = default)
    {
        updateAction(CurrentConfig);
        ConfigChanged?.Invoke(this, CurrentConfig);
        return Task.CompletedTask;
    }
}

public class MockHttpMessageHandler : HttpMessageHandler
{
    public Func<HttpRequestMessage, Task<HttpResponseMessage>>? Handler { get; set; }

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (Handler != null)
        {
            return Handler(request);
        }

        return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
    }
}

public class PackUpdateServiceTests : IDisposable
{
    private readonly string _testRoot;
    private readonly string _gameDir;
    private readonly string _stateFilePath;

    public PackUpdateServiceTests()
    {
        _testRoot = Path.Combine(Path.GetTempPath(), "AuraTests_" + Guid.NewGuid().ToString("N"));
        _gameDir = Path.Combine(_testRoot, "game");
        _stateFilePath = Path.Combine(_testRoot, "pack-state.json");
        Directory.CreateDirectory(_gameDir);
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_testRoot))
            {
                Directory.Delete(_testRoot, true);
            }
        }
        catch { }
    }

    private static string Sha256(byte[] data)
    {
        return Convert.ToHexString(SHA256.HashData(data)).ToLowerInvariant();
    }

    private string BuildManifestJson(
        string packVersion = "20261004.1800",
        string minecraft = "1.20.1",
        string fabricLoader = "0.19.5",
        IEnumerable<object>? files = null)
    {
        var obj = new
        {
            name = "Aura",
            packVersion,
            minecraft,
            fabricLoader,
            files = files ?? Array.Empty<object>()
        };
        return JsonSerializer.Serialize(obj);
    }

    [Fact]
    public async Task Sync_ModAdditionReplacementDeletion_WorksCorrectly()
    {
        // 1. Arrange
        var modABytes = Encoding.UTF8.GetBytes("Mod A Content v2");
        var modBBytes = Encoding.UTF8.GetBytes("Mod B Content v1");

        var manifestFiles = new[]
        {
            new { path = "mods/modA.jar", sha256 = Sha256(modABytes), size = (long)modABytes.Length, mode = "sync" },
            new { path = "mods/modB.jar", sha256 = Sha256(modBBytes), size = (long)modBBytes.Length, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        // Pre-create obsolete mod and old modA on client
        var modsDir = Path.Combine(_gameDir, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "modA.jar"), "Old Mod A Content v1");
        File.WriteAllText(Path.Combine(modsDir, "oldMod.jar"), "Old Obsolete Mod");

        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("manifest.json"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) });
                if (uri.Contains("mods/modA.jar"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(modABytes) });
                if (uri.Contains("mods/modB.jar"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(modBBytes) });

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        // 2. Act
        var result = await service.CheckAndApplyAsync();

        // 3. Assert
        Assert.Equal(PackUpdateStatus.Updated, result.Status);
        Assert.True(File.Exists(Path.Combine(modsDir, "modA.jar")));
        Assert.True(File.Exists(Path.Combine(modsDir, "modB.jar")));
        Assert.False(File.Exists(Path.Combine(modsDir, "oldMod.jar")));
        Assert.Equal("Mod A Content v2", File.ReadAllText(Path.Combine(modsDir, "modA.jar")));
    }

    [Fact]
    public async Task Sync_UnrelatedJarInModsDeleted_NonJarPreserved()
    {
        var modBytes = Encoding.UTF8.GetBytes("Mod Content");
        var manifestFiles = new[]
        {
            new { path = "mods/mod1.jar", sha256 = Sha256(modBytes), size = (long)modBytes.Length, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        var modsDir = Path.Combine(_gameDir, "mods");
        Directory.CreateDirectory(modsDir);
        File.WriteAllText(Path.Combine(modsDir, "unrelated.jar"), "Unrelated Jar");
        File.WriteAllText(Path.Combine(modsDir, "readme.txt"), "Important Readme");

        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("manifest.json"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) });
                if (uri.Contains("mods/mod1.jar"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(modBytes) });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Updated, result.Status);
        Assert.False(File.Exists(Path.Combine(modsDir, "unrelated.jar")));
        Assert.True(File.Exists(Path.Combine(modsDir, "readme.txt")));
    }

    [Fact]
    public async Task DefaultFiles_NotOverwrittenIfExists_DownloadedIfNotExists()
    {
        var defaultCfg1 = Encoding.UTF8.GetBytes("Default Cfg 1 from manifest");
        var defaultCfg2 = Encoding.UTF8.GetBytes("Default Cfg 2 from manifest");

        var manifestFiles = new[]
        {
            new { path = "config/file1.json", sha256 = Sha256(defaultCfg1), size = (long)defaultCfg1.Length, mode = "default" },
            new { path = "config/file2.json", sha256 = Sha256(defaultCfg2), size = (long)defaultCfg2.Length, mode = "default" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        var cfgDir = Path.Combine(_gameDir, "config");
        Directory.CreateDirectory(cfgDir);
        // Pre-create file1 with user customizations
        File.WriteAllText(Path.Combine(cfgDir, "file1.json"), "User Custom Cfg");

        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("manifest.json"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) });
                if (uri.Contains("config/file2.json"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(defaultCfg2) });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Updated, result.Status);
        Assert.Equal("User Custom Cfg", File.ReadAllText(Path.Combine(cfgDir, "file1.json")));
        Assert.Equal("Default Cfg 2 from manifest", File.ReadAllText(Path.Combine(cfgDir, "file2.json")));
    }

    [Fact]
    public async Task SyncFiles_OverwrittenAlways_IfShaDiffers()
    {
        var serverContent = Encoding.UTF8.GetBytes("Server Force Config");
        var manifestFiles = new[]
        {
            new { path = "config/forced.json", sha256 = Sha256(serverContent), size = (long)serverContent.Length, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        var cfgDir = Path.Combine(_gameDir, "config");
        Directory.CreateDirectory(cfgDir);
        File.WriteAllText(Path.Combine(cfgDir, "forced.json"), "User Modified Forced Config");

        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("manifest.json"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) });
                if (uri.Contains("config/forced.json"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(serverContent) });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Updated, result.Status);
        Assert.Equal("Server Force Config", File.ReadAllText(Path.Combine(cfgDir, "forced.json")));
    }

    [Fact]
    public async Task Shaderpacks_UserZipPreserved()
    {
        var serverShader = Encoding.UTF8.GetBytes("BSL Shader Zip");
        var manifestFiles = new[]
        {
            new { path = "shaderpacks/BSL.zip", sha256 = Sha256(serverShader), size = (long)serverShader.Length, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        var shaderDir = Path.Combine(_gameDir, "shaderpacks");
        Directory.CreateDirectory(shaderDir);
        File.WriteAllText(Path.Combine(shaderDir, "CustomShader.zip"), "Custom Shader Pack");

        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("manifest.json"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) });
                if (uri.Contains("shaderpacks/BSL.zip"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(serverShader) });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Updated, result.Status);
        Assert.True(File.Exists(Path.Combine(shaderDir, "BSL.zip")));
        Assert.True(File.Exists(Path.Combine(shaderDir, "CustomShader.zip")));
    }

    [Theory]
    [InlineData("../x")]
    [InlineData("mods/../../x")]
    [InlineData("/x")]
    [InlineData("C:/x")]
    [InlineData("mods\\x")]
    [InlineData("other.txt")]
    [InlineData("MODS/x")]
    public async Task Security_PathTraversalAndInvalidPaths_Rejected(string invalidPath)
    {
        var manifestFiles = new[]
        {
            new { path = invalidPath, sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", size = 0L, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        var handler = new MockHttpMessageHandler
        {
            Handler = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) })
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Failed, result.Status);
        Assert.False(string.IsNullOrWhiteSpace(result.Message));
    }

    [Fact]
    public async Task Security_OptionsTxt_Allowed()
    {
        var optionsBytes = Encoding.UTF8.GetBytes("lang:ru_ru\nversion:3465");
        var manifestFiles = new[]
        {
            new { path = "options.txt", sha256 = Sha256(optionsBytes), size = (long)optionsBytes.Length, mode = "default" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("manifest.json"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) });
                if (uri.Contains("options.txt"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(optionsBytes) });
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Updated, result.Status);
        Assert.True(File.Exists(Path.Combine(_gameDir, "options.txt")));
    }

    [Fact]
    public async Task Security_FileOver200MB_Rejected()
    {
        var manifestFiles = new[]
        {
            new { path = "mods/huge.jar", sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", size = 250L * 1024 * 1024, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        var handler = new MockHttpMessageHandler
        {
            Handler = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) })
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Failed, result.Status);
        Assert.Contains("Некорректный размер файла", result.Message);
    }

    [Fact]
    public async Task Security_DuplicatePaths_Rejected()
    {
        var manifestFiles = new[]
        {
            new { path = "mods/mod1.jar", sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", size = 10L, mode = "sync" },
            new { path = "mods/mod1.jar", sha256 = "e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855", size = 10L, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        var handler = new MockHttpMessageHandler
        {
            Handler = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) })
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Failed, result.Status);
        Assert.Contains("Дубликат пути", result.Message);
    }

    [Theory]
    [InlineData("invalid repo")]
    [InlineData("no-slash")]
    [InlineData("user/repo/extra")]
    public async Task Security_InvalidPackRepo_Rejected(string invalidRepo)
    {
        var configService = new TestConfigService(_gameDir, invalidRepo);
        using var service = new PackUpdateService(configService, new HttpClient(new MockHttpMessageHandler()), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Failed, result.Status);
        Assert.Contains("Некорректный репозиторий", result.Message);
    }

    [Fact]
    public async Task HttpRange_ResumeSupported_DownloadsRemaining()
    {
        var totalBytes = new byte[100];
        for (int i = 0; i < 100; i++) totalBytes[i] = (byte)i;
        var sha = Sha256(totalBytes);

        var manifestFiles = new[]
        {
            new { path = "mods/range.jar", sha256 = sha, size = 100L, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        // Pre-create partial file with 40 bytes
        var modsDir = Path.Combine(_gameDir, "mods");
        Directory.CreateDirectory(modsDir);
        var partFile = Path.Combine(modsDir, "range.jar.part");
        var existingPart = new byte[40];
        Array.Copy(totalBytes, 0, existingPart, 0, 40);
        File.WriteAllBytes(partFile, existingPart);

        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("manifest.json"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) });

                if (uri.Contains("mods/range.jar"))
                {
                    if (req.Headers.Range != null && req.Headers.Range.Ranges.Count > 0)
                    {
                        var from = req.Headers.Range.Ranges.First().From ?? 0;
                        var remaining = new byte[100 - from];
                        Array.Copy(totalBytes, (int)from, remaining, 0, remaining.Length);

                        var resp = new HttpResponseMessage(HttpStatusCode.PartialContent)
                        {
                            Content = new ByteArrayContent(remaining)
                        };
                        resp.Content.Headers.ContentRange = new ContentRangeHeaderValue(from, 99, 100);
                        return Task.FromResult(resp);
                    }

                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(totalBytes) });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Updated, result.Status);
        var finalJar = Path.Combine(modsDir, "range.jar");
        Assert.True(File.Exists(finalJar));
        Assert.Equal(100, new FileInfo(finalJar).Length);
        Assert.False(File.Exists(partFile));
    }

    [Fact]
    public async Task HttpRange_FallbackOn200Ok_DownloadsEntireFile()
    {
        var totalBytes = new byte[100];
        for (int i = 0; i < 100; i++) totalBytes[i] = (byte)(i + 1);
        var sha = Sha256(totalBytes);

        var manifestFiles = new[]
        {
            new { path = "mods/fallback.jar", sha256 = sha, size = 100L, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        // Pre-create partial file with 30 bytes
        var modsDir = Path.Combine(_gameDir, "mods");
        Directory.CreateDirectory(modsDir);
        var partFile = Path.Combine(modsDir, "fallback.jar.part");
        File.WriteAllBytes(partFile, new byte[30]);

        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("manifest.json"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) });

                // Server ignores Range header and returns 200 OK with full content
                if (uri.Contains("mods/fallback.jar"))
                {
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(totalBytes) });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Updated, result.Status);
        var finalJar = Path.Combine(modsDir, "fallback.jar");
        Assert.True(File.Exists(finalJar));
        Assert.Equal(100, new FileInfo(finalJar).Length);
    }

    [Fact]
    public async Task CorruptedFile_RetriesAndFails()
    {
        var expectedSha = Sha256(Encoding.UTF8.GetBytes("Expected Content"));
        var manifestFiles = new[]
        {
            new { path = "mods/corrupt.jar", sha256 = expectedSha, size = 16L, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        int attempts = 0;
        var handler = new MockHttpMessageHandler
        {
            Handler = req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("manifest.json"))
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) });

                if (uri.Contains("mods/corrupt.jar"))
                {
                    Interlocked.Increment(ref attempts);
                    // Return corrupted bytes
                    return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(Encoding.UTF8.GetBytes("Wrong Hash Data")) });
                }

                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
            }
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Failed, result.Status);
        Assert.True(attempts >= 3);
    }

    [Fact]
    public async Task Offline_WithoutState_ReturnsFailed()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = req => throw new HttpRequestException("No network")
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Failed, result.Status);
        Assert.Contains("первой установки", result.Message);
    }

    [Fact]
    public async Task Offline_WithValidState_ReturnsOfflineContinue()
    {
        // Pre-save valid state
        PackState.SaveState(new PackState
        {
            PackVersion = "20261004.1800",
            GameDir = _gameDir,
            Minecraft = "1.20.1",
            FabricLoader = "0.19.5",
            InstalledAtUtc = DateTime.UtcNow
        }, _stateFilePath);

        var handler = new MockHttpMessageHandler
        {
            Handler = req => throw new HttpRequestException("No network")
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.OfflineContinue, result.Status);
        Assert.Contains("Нет сети", result.Message);
    }

    [Fact]
    public async Task Http404_ManifestNotFound_ReturnsFailed()
    {
        var handler = new MockHttpMessageHandler
        {
            Handler = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound))
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Failed, result.Status);
        Assert.Contains("Манифест сборки не найден", result.Message);
    }

    [Fact]
    public async Task State_MismatchedGameDir_TreatedAsUninstalled()
    {
        var otherGameDir = Path.Combine(_testRoot, "other_game");
        Directory.CreateDirectory(otherGameDir);

        // State saved for otherGameDir
        PackState.SaveState(new PackState
        {
            PackVersion = "20261004.1800",
            GameDir = otherGameDir,
            Minecraft = "1.20.1",
            FabricLoader = "0.19.5",
            InstalledAtUtc = DateTime.UtcNow
        }, _stateFilePath);

        // Check against _gameDir
        var state = PackState.LoadValidState(_gameDir, _stateFilePath);
        Assert.Null(state);

        // Offline attempt with mismatched state must fail
        var handler = new MockHttpMessageHandler
        {
            Handler = req => throw new HttpRequestException("No network")
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();
        Assert.Equal(PackUpdateStatus.Failed, result.Status);
    }

    [Fact]
    public async Task MinecraftVersionMismatch_ReturnsFailed()
    {
        var manifestContent = BuildManifestJson(minecraft: "1.20.4");

        var handler = new MockHttpMessageHandler
        {
            Handler = req => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) })
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var result = await service.CheckAndApplyAsync();

        Assert.Equal(PackUpdateStatus.Failed, result.Status);
        Assert.Contains("Сборка для другой версии Minecraft", result.Message);
    }

    [Fact]
    public async Task Cancellation_LeavesNoPartialFinalFile()
    {
        var modBytes = Encoding.UTF8.GetBytes("Some mod payload");
        var manifestFiles = new[]
        {
            new { path = "mods/canceled.jar", sha256 = Sha256(modBytes), size = (long)modBytes.Length, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        using var cts = new CancellationTokenSource();

        var handler = new MockHttpMessageHandler
        {
            Handler = async req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("manifest.json"))
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) };

                if (uri.Contains("mods/canceled.jar"))
                {
                    cts.Cancel();
                    await Task.Delay(100);
                    throw new OperationCanceledException(cts.Token);
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(async () =>
        {
            await service.CheckAndApplyAsync(ct: cts.Token);
        });

        var finalJar = Path.Combine(_gameDir, "mods", "canceled.jar");
        Assert.False(File.Exists(finalJar));
    }

    [Fact]
    public async Task Concurrency_ParallelCallsExecuteOnce()
    {
        var modBytes = Encoding.UTF8.GetBytes("Mod Parallel");
        var manifestFiles = new[]
        {
            new { path = "mods/parallel.jar", sha256 = Sha256(modBytes), size = (long)modBytes.Length, mode = "sync" }
        };
        var manifestContent = BuildManifestJson(files: manifestFiles);

        int manifestRequests = 0;
        var handler = new MockHttpMessageHandler
        {
            Handler = async req =>
            {
                var uri = req.RequestUri!.ToString();
                if (uri.Contains("manifest.json"))
                {
                    Interlocked.Increment(ref manifestRequests);
                    await Task.Delay(100); // simulate network latency
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(manifestContent) };
                }
                if (uri.Contains("mods/parallel.jar"))
                {
                    await Task.Delay(50);
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new ByteArrayContent(modBytes) };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
        };

        var configService = new TestConfigService(_gameDir);
        using var service = new PackUpdateService(configService, new HttpClient(handler), _stateFilePath);

        var task1 = service.CheckAndApplyAsync();
        var task2 = service.CheckAndApplyAsync();

        var results = await Task.WhenAll(task1, task2);

        Assert.Equal(PackUpdateStatus.Updated, results[0].Status);
        Assert.Equal(PackUpdateStatus.Updated, results[1].Status);
        Assert.Equal(1, manifestRequests);
    }
}

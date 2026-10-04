using System;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using AuraLauncher.Services.Interfaces;
using Xunit;

namespace AuraLauncher.Tests;

public class IntegrationTests
{
    private const string TestGameDir = @"C:\AuraTest\game";

    [Fact]
    public async Task RealRun_CleanInstall_VerifyAndRepair()
    {
        // 1. Prepare clean test folder
        if (Directory.Exists(TestGameDir))
        {
            Directory.Delete(TestGameDir, true);
        }
        Directory.CreateDirectory(TestGameDir);

        var testStatePath = Path.Combine(TestGameDir, "pack-state.json");
        var configService = new TestConfigService(TestGameDir, PackUpdateService.DefaultPackRepo);

        // Real HttpClient pointing to GitHub raw
        using var realClient = new System.Net.Http.HttpClient();
        realClient.DefaultRequestHeaders.UserAgent.Add(new System.Net.Http.Headers.ProductInfoHeaderValue("AuraLauncher", "1.0"));

        using var updateService = new PackUpdateService(configService, realClient, testStatePath);

        // 2. Measure First Install
        var sw = Stopwatch.StartNew();
        var result = await updateService.CheckAndApplyAsync();
        sw.Stop();

        Assert.Equal(PackUpdateStatus.Updated, result.Status);

        // Verify file counts
        var mods = Directory.GetFiles(Path.Combine(TestGameDir, "mods"), "*.jar");
        var shaders = Directory.GetFiles(Path.Combine(TestGameDir, "shaderpacks"), "*.zip");
        var rps = Directory.GetFiles(Path.Combine(TestGameDir, "resourcepacks"), "*.zip");
        var optionsPath = Path.Combine(TestGameDir, "options.txt");

        var allFiles = Directory.GetFiles(TestGameDir, "*", SearchOption.AllDirectories)
            .Where(f => !f.EndsWith("pack-state.json"))
            .ToList();

        // Output metrics
        Trace.WriteLine($"First install time: {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalSeconds:F1} s)");
        Trace.WriteLine($"Total files installed: {allFiles.Count}");
        Trace.WriteLine($"Mods count: {mods.Length}");
        Trace.WriteLine($"Shaders count: {shaders.Length}");
        Trace.WriteLine($"Resourcepacks count: {rps.Length}");

        Assert.Equal(174, allFiles.Count);
        Assert.Equal(60, mods.Length);
        Assert.Equal(4, shaders.Length);
        Assert.Equal(3, rps.Length);
        Assert.True(File.Exists(optionsPath));
        var optionsText = File.ReadAllText(optionsPath);
        Assert.Contains("lang:ru_ru", optionsText);

        // 3. Second run: UpToDate check
        var sw2 = Stopwatch.StartNew();
        var result2 = await updateService.CheckAndApplyAsync();
        sw2.Stop();

        Assert.Equal(PackUpdateStatus.UpToDate, result2.Status);
        Assert.Equal(0, result2.FilesChanged);
        Trace.WriteLine($"Second check time: {sw2.ElapsedMilliseconds} ms (Files changed: {result2.FilesChanged})");

        // 4. Repair test
        // a) Delete 1 mod
        var modToDelete = mods[0];
        var modNameToRestore = Path.GetFileName(modToDelete);
        File.Delete(modToDelete);

        // b) Corrupt 1 jar
        var modToCorrupt = mods[1];
        File.WriteAllText(modToCorrupt, "CORRUPTED CONTENT");

        // c) Add extra jar in mods
        var extraJar = Path.Combine(TestGameDir, "mods", "extra_untracked.jar");
        File.WriteAllText(extraJar, "EXTRA JAR");

        // d) Add user zip in shaderpacks
        var userShader = Path.Combine(TestGameDir, "shaderpacks", "UserCustomShader.zip");
        File.WriteAllText(userShader, "USER SHADER");

        // Run repair update
        var sw3 = Stopwatch.StartNew();
        var result3 = await updateService.CheckAndApplyAsync();
        sw3.Stop();

        Assert.Equal(PackUpdateStatus.Updated, result3.Status);
        Assert.True(File.Exists(modToDelete), $"Deleted mod {modNameToRestore} was not restored");
        Assert.NotEqual("CORRUPTED CONTENT", File.ReadAllText(modToCorrupt));
        Assert.False(File.Exists(extraJar), "Extra untracked jar in mods was not deleted");
        Assert.True(File.Exists(userShader), "User shaderpack was incorrectly deleted");

        Trace.WriteLine($"Repair time: {sw3.ElapsedMilliseconds} ms (Files restored: {result3.FilesChanged})");
    }

    [Fact]
    public async Task RealRun_LaunchGame_VerifyLogs()
    {
        var launchService = new FabricGameLaunchService();
        var config = new LauncherConfig
        {
            GameDir = TestGameDir,
            Nickname = "TestPlayer",
            RamMb = 4096
        };

        var logLines = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(60));

        var process = await launchService.LaunchGameAsync(
            config,
            line =>
            {
                logLines.Add(line);
                Trace.WriteLine(line);
            },
            onGameExited: (code, path) => Trace.WriteLine($"Game exited with code {code}"),
            cancellationToken: cts.Token);

        Assert.NotNull(process);
        Assert.False(process.HasExited);

        // Wait for game to initialize (up to 45 seconds)
        var latestLog = Path.Combine(TestGameDir, "logs", "latest.log");
        bool initialized = false;
        var timeout = DateTime.UtcNow.AddSeconds(45);

        while (DateTime.UtcNow < timeout)
        {
            if (File.Exists(latestLog))
            {
                try
                {
                    using var fs = new FileStream(latestLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    var text = await reader.ReadToEndAsync();
                    if (text.Contains("Backend library: LWJGL") && text.Contains("Setting user: TestPlayer"))
                    {
                        initialized = true;
                        break;
                    }
                }
                catch { }
            }
            await Task.Delay(1000);
        }

        try
        {
            if (!process.HasExited)
            {
                process.Kill(true);
            }
        }
        catch { }

        Assert.True(initialized, "Game did not reach LWJGL initialization in time.");
        Assert.True(File.Exists(latestLog));
        var logContent = File.ReadAllText(latestLog);
        Assert.Contains("Setting user: TestPlayer", logContent);
        Assert.Contains("Backend library: LWJGL", logContent);
        Assert.DoesNotContain("Incompatible mods found", logContent);
        Assert.DoesNotContain("Mod resolution failed", logContent);
        Assert.DoesNotContain("Mixin apply failed", logContent);
    }
}

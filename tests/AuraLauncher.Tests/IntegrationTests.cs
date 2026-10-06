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
    [Trait("Category", "Integration")]
    public async Task RealRun_CleanInstall_VerifyAndRepair()
    {
        // 1. Prepare clean test folder for pack files
        Directory.CreateDirectory(TestGameDir);
        foreach (var sub in new[] { "mods", "config", "shaderpacks", "resourcepacks" })
        {
            var p = Path.Combine(TestGameDir, sub);
            if (Directory.Exists(p)) Directory.Delete(p, true);
        }
        var opt = Path.Combine(TestGameDir, "options.txt");
        if (File.Exists(opt)) File.Delete(opt);
        var st = Path.Combine(TestGameDir, "pack-state.json");
        if (File.Exists(st)) File.Delete(st);

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

        var packFolders = new[] { "mods", "config", "shaderpacks", "resourcepacks" };
        var packFiles = packFolders.SelectMany(f => Directory.Exists(Path.Combine(TestGameDir, f))
            ? Directory.GetFiles(Path.Combine(TestGameDir, f), "*", SearchOption.AllDirectories)
            : Array.Empty<string>()).ToList();
        if (File.Exists(optionsPath)) packFiles.Add(optionsPath);

        // Output metrics
        Trace.WriteLine($"First install time: {sw.ElapsedMilliseconds} ms ({sw.Elapsed.TotalSeconds:F1} s)");
        Trace.WriteLine($"Total pack files installed: {packFiles.Count}");
        Trace.WriteLine($"Mods count: {mods.Length}");
        Trace.WriteLine($"Shaders count: {shaders.Length}");
        Trace.WriteLine($"Resourcepacks count: {rps.Length}");

        Assert.Equal(213, packFiles.Count);
        Assert.Equal(97, mods.Length);
        Assert.Equal(4, shaders.Length);
        Assert.Equal(5, rps.Length);
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
    [Trait("Category", "Integration")]
    public async Task RealRun_LaunchGame_VerifyLogs()
    {
        var launchService = new FabricGameLaunchService();
        var config = new LauncherConfig
        {
            GameDir = TestGameDir,
            Nickname = "TestPlayer",
            RamMb = 4096
        };

        var latestLog = Path.Combine(TestGameDir, "logs", "latest.log");
        if (File.Exists(latestLog))
        {
            try { File.Delete(latestLog); } catch { }
        }

        var logLines = new System.Collections.Concurrent.ConcurrentBag<string>();
        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(180));

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

        // Wait for game to initialize honestly (up to 120 seconds)
        // SUCCESS: java process is ALIVE AND latest.log contains "Reloading ResourceManager" AND "Created: ... atlas".
        // "Backend library: LWJGL" does NOT count as success.
        // If the process exits before success, fail immediately with exit code and the last 40 lines of latest.log.
        bool success = false;
        var timeout = DateTime.UtcNow.AddSeconds(120);

        while (DateTime.UtcNow < timeout)
        {
            if (process.HasExited)
            {
                var last40Lines = Array.Empty<string>();
                if (File.Exists(latestLog))
                {
                    try
                    {
                        var lines = File.ReadAllLines(latestLog);
                        last40Lines = lines.TakeLast(40).ToArray();
                    }
                    catch { }
                }
                var logTail = string.Join(Environment.NewLine, last40Lines);
                Assert.Fail($"Process exited prematurely with code {process.ExitCode} before reaching launch success! Last 40 lines of latest.log:\n{logTail}");
            }

            if (File.Exists(latestLog))
            {
                try
                {
                    using var fs = new FileStream(latestLog, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs);
                    var text = await reader.ReadToEndAsync();
                    bool hasReloading = text.Contains("Reloading ResourceManager");
                    bool hasAtlas = System.Text.RegularExpressions.Regex.IsMatch(text, @"Created:\s+.*atlas", System.Text.RegularExpressions.RegexOptions.IgnoreCase);

                    if (!process.HasExited && hasReloading && hasAtlas)
                    {
                        success = true;
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
                process.WaitForExit(5000);
            }
        }
        catch { }

        static string ReadLogSafe(string path)
        {
            for (int i = 0; i < 5; i++)
            {
                try
                {
                    using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                    using var reader = new StreamReader(fs, System.Text.Encoding.UTF8);
                    return reader.ReadToEnd();
                }
                catch when (i < 4)
                {
                    Thread.Sleep(200);
                }
            }
            return string.Empty;
        }

        if (!success)
        {
            var raw = ReadLogSafe(latestLog);
            var lines = raw.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            var last40Lines = lines.TakeLast(40).ToArray();
            var logTail = string.Join(Environment.NewLine, last40Lines);
            Assert.Fail($"Launch timed out after 120s without honest success. HasExited: {process.HasExited}. Last 40 lines of latest.log:\n{logTail}");
        }

        Assert.True(File.Exists(latestLog));
        var logContent = ReadLogSafe(latestLog);
        Assert.Contains("Setting user: TestPlayer", logContent);
        Assert.Contains("Reloading ResourceManager", logContent);
        Assert.Matches(@"Created:\s+.*atlas", logContent);
        Assert.DoesNotContain("Incompatible mods found", logContent);
        Assert.DoesNotContain("Mod resolution failed", logContent);
        Assert.DoesNotContain("Mixin apply failed", logContent);
    }
}

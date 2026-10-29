using System;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Services.Implementations;
using Xunit;

namespace AuraLauncher.Tests;

public class AppCdsArchiveTests
{
    [Fact]
    public void EnsureBaseCdsArchive_MissingJavaPath_ReturnsFalse()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), "non_existent_java_" + Guid.NewGuid().ToString("N") + ".exe");
        bool result = FabricGameLaunchService.EnsureBaseCdsArchive(missingPath);
        Assert.False(result);
    }

    [Fact]
    public void EnsureBaseCdsArchive_ZeroByteArchive_Rejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AuraTest", "cds-zero-" + Guid.NewGuid().ToString("N"));
        try
        {
            var serverDir = Path.Combine(tempDir, "server");
            Directory.CreateDirectory(serverDir);
            var archivePath = Path.Combine(serverDir, "classes.jsa");
            File.WriteAllBytes(archivePath, Array.Empty<byte>());

            var fakeJava = Path.Combine(tempDir, "java.exe");
            File.WriteAllText(fakeJava, "mock");

            bool isValid = FabricGameLaunchService.IsValidCdsArchive(archivePath);
            Assert.False(isValid, "Zero-byte archive must not be considered valid");

            bool result = FabricGameLaunchService.EnsureBaseCdsArchive(fakeJava, CancellationToken.None, timeoutMs: 500);
            Assert.False(result, "EnsureBaseCdsArchive must reject zero-byte base CDS archive");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void EnsureBaseCdsArchive_CorruptedTinyArchive_Rejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AuraTest", "cds-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            var serverDir = Path.Combine(tempDir, "server");
            Directory.CreateDirectory(serverDir);
            var archivePath = Path.Combine(serverDir, "classes.jsa");
            File.WriteAllBytes(archivePath, new byte[100]); // Tiny corrupted file

            var fakeJava = Path.Combine(tempDir, "java.exe");
            File.WriteAllText(fakeJava, "mock");

            bool isValid = FabricGameLaunchService.IsValidCdsArchive(archivePath);
            Assert.False(isValid, "Tiny/corrupted archive must not be considered valid");

            bool result = FabricGameLaunchService.EnsureBaseCdsArchive(fakeJava, CancellationToken.None, timeoutMs: 500);
            Assert.False(result);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void EnsureBaseCdsArchive_CorruptedLargeArchive16KiB_Rejected()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AuraTest", "cds-corrupt-16k-" + Guid.NewGuid().ToString("N"));
        try
        {
            var serverDir = Path.Combine(tempDir, "server");
            Directory.CreateDirectory(serverDir);
            var archivePath = Path.Combine(serverDir, "classes.jsa");
            // 16 KiB of zero bytes (missing valid Hotspot magic header)
            File.WriteAllBytes(archivePath, new byte[16384]);

            var fakeJava = Path.Combine(tempDir, "java.exe");
            File.WriteAllText(fakeJava, "mock");

            bool isValid = FabricGameLaunchService.IsValidCdsArchive(archivePath);
            Assert.False(isValid, "16 KiB all-zero archive must not be considered valid");

            bool result = FabricGameLaunchService.EnsureBaseCdsArchive(fakeJava, CancellationToken.None, timeoutMs: 500);
            Assert.False(result, "EnsureBaseCdsArchive must reject corrupted 16 KiB archive");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void EnsureBaseCdsArchive_ValidArchive_AcceptedWithoutExecutingDump()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AuraTest", "cds-valid-" + Guid.NewGuid().ToString("N"));
        try
        {
            var serverDir = Path.Combine(tempDir, "server");
            Directory.CreateDirectory(serverDir);
            var archivePath = Path.Combine(serverDir, "classes.jsa");
            // Valid archive with Hotspot CDS magic header (0xF00BABA2)
            var validBytes = new byte[16384];
            var magicBytes = BitConverter.GetBytes(0xF00BABA2);
            Array.Copy(magicBytes, 0, validBytes, 0, 4);
            File.WriteAllBytes(archivePath, validBytes);

            var fakeJava = Path.Combine(tempDir, "java.exe");
            File.WriteAllText(fakeJava, "mock");

            bool isValid = FabricGameLaunchService.IsValidCdsArchive(archivePath);
            Assert.True(isValid);

            bool result = FabricGameLaunchService.EnsureBaseCdsArchive(fakeJava);
            Assert.True(result, "Valid archive must be accepted immediately");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void VerifyArchiveWithJvm_CorruptedArchive_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AuraTest", "cds-jvm-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var archivePath = Path.Combine(tempDir, "classes.jsa");
            File.WriteAllBytes(archivePath, new byte[16384]);

            bool verified = FabricGameLaunchService.VerifyArchiveWithJvm("java.exe", archivePath, timeoutMs: 1000);
            Assert.False(verified);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void EnsureBaseCdsArchive_GenerationTimeout_KillsChildProcessAndReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AuraTest", "cds-timeout-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var scriptPath = Path.Combine(tempDir, "java.cmd");
            // Batch script that sleeps for 60 seconds
            File.WriteAllText(scriptPath, "@echo off\r\npowershell -Command \"Start-Sleep -Seconds 60\"\r\n");

            var sw = Stopwatch.StartNew();
            bool result = FabricGameLaunchService.EnsureBaseCdsArchive(scriptPath, CancellationToken.None, timeoutMs: 1000);
            sw.Stop();

            Assert.False(result, "Timed out generation must return false");
            Assert.InRange(sw.ElapsedMilliseconds, 900, 6000);
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void EnsureBaseCdsArchive_GenerationFailureExitCode_ReturnsFalse()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AuraTest", "cds-fail-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var scriptPath = Path.Combine(tempDir, "java.cmd");
            // Batch script that exits with error code 1
            File.WriteAllText(scriptPath, "@echo off\r\nexit /b 1\r\n");

            bool result = FabricGameLaunchService.EnsureBaseCdsArchive(scriptPath, CancellationToken.None, timeoutMs: 5000);
            Assert.False(result, "Failed generation exit code must return false");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }
}

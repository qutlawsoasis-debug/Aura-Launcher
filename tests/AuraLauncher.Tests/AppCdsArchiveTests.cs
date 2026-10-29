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
    public void EnsureBaseCdsArchive_HeaderOnlyCorruptedArchive_RejectedByEnsureAndJvm()
    {
        var tempDir = Path.Combine(Path.GetTempPath(), "AuraTest", "cds-header-only-" + Guid.NewGuid().ToString("N"));
        try
        {
            var serverDir = Path.Combine(tempDir, "server");
            Directory.CreateDirectory(serverDir);
            var archivePath = Path.Combine(serverDir, "classes.jsa");

            // Header-only archive: valid 4-byte HotSpot CDS magic (0xF00BABA2) followed by 16380 zero bytes
            var headerOnlyBytes = new byte[16384];
            var magicBytes = BitConverter.GetBytes(0xF00BABA2);
            Array.Copy(magicBytes, 0, headerOnlyBytes, 0, 4);
            File.WriteAllBytes(archivePath, headerOnlyBytes);

            // Preliminary filter passes because magic and size match
            bool isValid = FabricGameLaunchService.IsValidCdsArchive(archivePath);
            Assert.True(isValid, "Preliminary header magic check should pass for header-only file");

            // But production EnsureBaseCdsArchive MUST reject it because JVM verification fails!
            string? realJava = FabricGameLaunchService.ResolveExecutable("java.exe");
            if (!string.IsNullOrEmpty(realJava) && File.Exists(realJava))
            {
                // Verify directly that real JVM rejects the header-only archive
                bool jvmVerified = FabricGameLaunchService.VerifyArchiveWithJvm(realJava, archivePath, timeoutMs: 3000);
                Assert.False(jvmVerified, "Real JVM must reject header-only corrupted archive");
            }

            // Using mock java that exits with 1 if dump is attempted
            var mockScript = Path.Combine(tempDir, "java.cmd");
            File.WriteAllText(mockScript, "@echo off\r\nexit /b 1\r\n");

            bool ensureResult = FabricGameLaunchService.EnsureBaseCdsArchive(mockScript, CancellationToken.None, timeoutMs: 2000);
            Assert.False(ensureResult, "EnsureBaseCdsArchive must reject header-only archive and return false");
        }
        finally
        {
            try { Directory.Delete(tempDir, true); } catch { }
        }
    }

    [Fact]
    public void EnsureBaseCdsArchive_RealArchive_AcceptedWithJvmVerification()
    {
        string? realJava = FabricGameLaunchService.ResolveExecutable("java.exe");
        if (string.IsNullOrEmpty(realJava) || !File.Exists(realJava))
        {
            // Skip test if environment has no Java installed
            return;
        }

        var binDir = Path.GetDirectoryName(realJava);
        if (string.IsNullOrEmpty(binDir)) return;

        string[] possibleArchives =
        {
            Path.Combine(binDir, "server", "classes.jsa"),
            Path.Combine(binDir, "..", "lib", "server", "classes.jsa"),
            Path.Combine(binDir, "client", "classes.jsa")
        };

        string? existingArchive = null;
        foreach (var p in possibleArchives)
        {
            if (File.Exists(p) && FabricGameLaunchService.IsValidCdsArchive(p))
            {
                existingArchive = p;
                break;
            }
        }

        if (existingArchive != null)
        {
            bool jvmVerified = FabricGameLaunchService.VerifyArchiveWithJvm(realJava, existingArchive, timeoutMs: 3000);
            Assert.True(jvmVerified, "Real compatible base CDS archive must be verified by real JVM");

            bool ensureResult = FabricGameLaunchService.EnsureBaseCdsArchive(realJava);
            Assert.True(ensureResult, "EnsureBaseCdsArchive must accept a real verified archive");
        }
    }

    [Fact]
    public void VerifyArchiveWithJvm_CorruptedArchive_RealJvmRejects()
    {
        string? realJava = FabricGameLaunchService.ResolveExecutable("java.exe");
        if (string.IsNullOrEmpty(realJava) || !File.Exists(realJava))
        {
            return;
        }

        var tempDir = Path.Combine(Path.GetTempPath(), "AuraTest", "cds-jvm-corrupt-" + Guid.NewGuid().ToString("N"));
        try
        {
            Directory.CreateDirectory(tempDir);
            var archivePath = Path.Combine(tempDir, "classes.jsa");

            // Header-only corrupted archive (4 magic bytes + 16380 zeroes)
            var bytes = new byte[16384];
            var magic = BitConverter.GetBytes(0xF00BABA2);
            Array.Copy(magic, 0, bytes, 0, 4);
            File.WriteAllBytes(archivePath, bytes);

            // Pass resolved absolute javaPath
            bool verified = FabricGameLaunchService.VerifyArchiveWithJvm(realJava, archivePath, timeoutMs: 3000);
            Assert.False(verified, "Real JVM execution must reject corrupted archive with non-zero exit code");
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

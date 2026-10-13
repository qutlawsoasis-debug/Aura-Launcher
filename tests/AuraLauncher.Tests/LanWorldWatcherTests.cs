using System;
using System.IO;
using AuraLauncher.Services.Implementations;
using Xunit;

namespace AuraLauncher.Tests;

public class LanWorldWatcherTests
{
    private static readonly string TestDataDir = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "TestData", "Logs");

    [Fact]
    public void ProcessLine_StartedServing_TriggersWorldOpened()
    {
        using var watcher = new LanWorldWatcher();
        int openedPort = 0;
        watcher.WorldOpened += port => openedPort = port;

        // Строка из реального лога latest.log:1055
        watcher.ProcessLine("[19:45:47] [Render thread/INFO]: Started serving on 25565");

        Assert.True(watcher.IsWorldOpen);
        Assert.Equal(25565, watcher.CurrentPort);
        Assert.Equal(25565, openedPort);
    }

    [Fact]
    public void ProcessLine_CustomPort_TriggersWorldOpened()
    {
        using var watcher = new LanWorldWatcher();
        int openedPort = 0;
        watcher.WorldOpened += port => openedPort = port;

        watcher.ProcessLine("[12:11:05] [Render thread/INFO]: Started serving on 49152");

        Assert.True(watcher.IsWorldOpen);
        Assert.Equal(49152, watcher.CurrentPort);
        Assert.Equal(49152, openedPort);
    }

    [Fact]
    public void ProcessLine_StoppingServer_TriggersWorldClosed()
    {
        using var watcher = new LanWorldWatcher();
        bool closedCalled = false;
        watcher.WorldClosed += () => closedCalled = true;

        watcher.ProcessLine("[19:45:47] [Render thread/INFO]: Started serving on 25565");
        Assert.True(watcher.IsWorldOpen);

        // Строка из реального лога latest.log:1064
        watcher.ProcessLine("[19:48:51] [Server thread/INFO]: Stopping singleplayer server as player logged out");
        Assert.False(watcher.IsWorldOpen);
        Assert.Null(watcher.CurrentPort);
        Assert.True(closedCalled);
    }

    [Fact]
    public void PollLogFile_ReadsRealLspNormalSample_DetectsOpenAndClose()
    {
        var sampleLog = Path.Combine(TestDataDir, "lan_lsp_25565_normal.log");
        Assert.True(File.Exists(sampleLog));

        using var watcher = new LanWorldWatcher();
        int detectedPort = 0;
        bool closed = false;

        watcher.WorldOpened += p => detectedPort = p;
        watcher.WorldClosed += () => closed = true;

        watcher.Start(sampleLog, readFromEnd: false);

        Assert.Equal(25565, detectedPort);
        Assert.True(closed);
        Assert.False(watcher.IsWorldOpen);
    }

    [Fact]
    public void PollLogFile_PortOccupied_DoesNotTriggerWorldOpened()
    {
        var sampleLog = Path.Combine(TestDataDir, "lan_port_occupied.log");
        Assert.True(File.Exists(sampleLog));

        using var watcher = new LanWorldWatcher();
        bool opened = false;
        watcher.WorldOpened += _ => opened = true;

        watcher.Start(sampleLog, readFromEnd: false);

        Assert.False(opened);
        Assert.False(watcher.IsWorldOpen);
        Assert.Null(watcher.CurrentPort);
    }

    [Fact]
    public void PollLogFile_ExitToMenu_DetectsWorldClosed()
    {
        var sampleLog = Path.Combine(TestDataDir, "lan_exit_to_menu.log");
        Assert.True(File.Exists(sampleLog));

        using var watcher = new LanWorldWatcher();
        bool closed = false;
        watcher.WorldClosed += () => closed = true;

        watcher.Start(sampleLog, readFromEnd: false);

        Assert.True(closed);
        Assert.False(watcher.IsWorldOpen);
    }

    [Fact]
    public void PollLogFile_AbruptKill_StaysOpenUntilExplicitStopOrGameExited()
    {
        var sampleLog = Path.Combine(TestDataDir, "lan_abrupt_kill.log");
        Assert.True(File.Exists(sampleLog));

        using var watcher = new LanWorldWatcher();
        watcher.Start(sampleLog, readFromEnd: false);

        Assert.True(watcher.IsWorldOpen);
        Assert.Equal(25565, watcher.CurrentPort);

        watcher.Stop();
        Assert.False(watcher.IsWorldOpen);
    }

    [Fact]
    public void PollLogFile_SplitChunks_BuffersCorrectly()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            using var watcher = new LanWorldWatcher();
            int openedPort = 0;
            watcher.WorldOpened += p => openedPort = p;

            watcher.Start(tempFile);

            // Пишем первую часть строки без перевода строки
            File.AppendAllText(tempFile, "[10:00:00] [Render thread/INFO]: Started ");
            watcher.PollLogFile();
            Assert.False(watcher.IsWorldOpen);

            // Дописываем остаток строки с переводом строки
            File.AppendAllText(tempFile, "serving on 25565\n");
            watcher.PollLogFile();

            Assert.True(watcher.IsWorldOpen);
            Assert.Equal(25565, openedPort);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Fact]
    public void PollLogFile_DefaultSkipsPriorLines_OnlyReadsNewLines()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            // Старая сессия записала открытие мира
            File.WriteAllText(tempFile, "[10:00:00] [Render thread/INFO]: Started serving on 25565\n");

            using var watcher = new LanWorldWatcher();
            int openedPort = 0;
            watcher.WorldOpened += p => openedPort = p;

            // По умолчанию Start() запускается с readFromEnd = true
            watcher.Start(tempFile);

            // Старая строка должна быть проигнорирована
            Assert.False(watcher.IsWorldOpen);
            Assert.Equal(0, openedPort);

            // Новая сессия открывает мир
            File.AppendAllText(tempFile, "[10:05:00] [Render thread/INFO]: Started serving on 25565\n");
            watcher.PollLogFile();

            Assert.True(watcher.IsWorldOpen);
            Assert.Equal(25565, openedPort);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("[19:46:00] [Server thread/INFO]: e4all: Domain assigned: quick-fox-42.e4mc.link", "quick-fox-42.e4mc.link")]
    [InlineData("[19:46:00] [Server thread/INFO]: Domain assigned: swift-bear-12.e4mc.link", "swift-bear-12.e4mc.link")]
    [InlineData("[19:46:00] [Render thread/INFO]: [CHAT] Local game hosted on domain [happy-panda-99.e4mc.link]", "happy-panda-99.e4mc.link")]
    [InlineData("[19:46:00] [Render thread/INFO]: [System] [CHAT] Локальный сервер размещен на домене [margin-amusement.eu.e4mc.link]", "margin-amusement.eu.e4mc.link")]
    [InlineData("[17:08:40] [Render thread/INFO]: [CHAT] Локальная игра размещена на домене [shabby-concierge.eu.e4mc.link]", "shabby-concierge.eu.e4mc.link")]
    [InlineData("[17:40:51] [Render thread/INFO]: [CHAT] Сервер размещен на домене [name-chess.eu.e4mc.link]", "name-chess.eu.e4mc.link")]
    public void ProcessLine_DomainAssigned_SetsCurrentDomainAndFiresEvent(string logLine, string expectedDomain)
    {
        using var watcher = new LanWorldWatcher();
        string? assignedDomain = null;
        watcher.DomainAssigned += d => assignedDomain = d;

        watcher.ProcessLine(logLine);

        Assert.Equal(expectedDomain, watcher.CurrentDomain);
        Assert.Equal(expectedDomain, assignedDomain);
    }

    [Fact]
    public void Start_WithScanRecentTail_DetectsPreExistingWorldAndDomain()
    {
        var tempFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(tempFile, 
                "[10:00:00] [Render thread/INFO]: Started serving on 25565\n" +
                "[10:00:01] [Render thread/INFO]: [System] [CHAT] Локальный сервер размещен на домене [pre-open.eu.e4mc.link]\n");

            using var watcher = new LanWorldWatcher();
            int openedPort = 0;
            string? domain = null;
            watcher.WorldOpened += p => openedPort = p;
            watcher.DomainAssigned += d => domain = d;

            watcher.Start(tempFile, readFromEnd: true, scanRecentTail: true);

            Assert.True(watcher.IsWorldOpen);
            Assert.Equal(25565, watcher.CurrentPort);
            Assert.Equal(25565, openedPort);
            Assert.Equal("pre-open.eu.e4mc.link", watcher.CurrentDomain);
            Assert.Equal("pre-open.eu.e4mc.link", domain);
        }
        finally
        {
            if (File.Exists(tempFile)) File.Delete(tempFile);
        }
    }

    [Theory]
    [InlineData("[19:46:00] [Render thread/INFO]: Connecting to broker.e4mc.link")]
    [InlineData("[19:46:00] [Render thread/INFO]: Broker: broker.e4mc.link")]
    [InlineData("[19:46:00] [Render thread/INFO]: [CHAT] Connecting to broker: broker.e4mc.link")]
    public void ProcessLine_BrokerAddress_DoesNotTriggerDomainAssigned(string logLine)
    {
        using var watcher = new LanWorldWatcher();
        string? domain = null;
        watcher.DomainAssigned += d => domain = d;

        watcher.ProcessLine(logLine);

        Assert.Null(watcher.CurrentDomain);
        Assert.Null(domain);
    }

    [Fact]
    public void ProcessLine_StoppingServer_ResetsCurrentDomain()
    {
        using var watcher = new LanWorldWatcher();
        watcher.ProcessLine("[19:45:47] [Render thread/INFO]: Started serving on 49152");
        watcher.ProcessLine("[19:46:00] [Server thread/INFO]: e4all: Domain assigned: quick-fox-42.e4mc.link");

        Assert.True(watcher.IsWorldOpen);
        Assert.Equal("quick-fox-42.e4mc.link", watcher.CurrentDomain);

        watcher.ProcessLine("[19:48:51] [Server thread/INFO]: Stopping server");

        Assert.False(watcher.IsWorldOpen);
        Assert.Null(watcher.CurrentDomain);
    }
}


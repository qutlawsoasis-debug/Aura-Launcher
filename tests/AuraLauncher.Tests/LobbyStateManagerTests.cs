using System;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Implementations;
using Xunit;

namespace AuraLauncher.Tests;

public class LobbyStateManagerTests
{
    [Fact]
    public void InitialState_IsIdle()
    {
        using var worldWatcher = new LanWorldWatcher();
        using var tunnelProvider = new FakeTunnelProvider();
        using var manager = new LobbyStateManager(worldWatcher, tunnelProvider);

        Assert.Equal(LobbyState.Idle, manager.State);
    }

    [Fact]
    public void WorldOpened_TransitionsToWorldOpen_NoAutoStart()
    {
        using var worldWatcher = new LanWorldWatcher();
        using var tunnelProvider = new FakeTunnelProvider();
        using var manager = new LobbyStateManager(worldWatcher, tunnelProvider);

        worldWatcher.ProcessLine("[19:45:47] [Render thread/INFO]: Started serving on 25565");

        Assert.Equal(LobbyState.WorldOpen, manager.State);
        Assert.Equal(25565, manager.LocalPort);
        Assert.Null(manager.PublicAddress);
    }

    [Fact]
    public async Task StartTunnelAsync_WhenWorldOpen_TransitionsToTunnelActive()
    {
        using var worldWatcher = new LanWorldWatcher();
        using var tunnelProvider = new FakeTunnelProvider();
        using var manager = new LobbyStateManager(worldWatcher, tunnelProvider);

        worldWatcher.ProcessLine("[19:45:47] [Render thread/INFO]: Started serving on 25565");
        Assert.Equal(LobbyState.WorldOpen, manager.State);

        bool started = await manager.StartTunnelAsync();

        Assert.True(started);
        Assert.Equal(LobbyState.TunnelActive, manager.State);
        Assert.Equal("aura-lobby.craft.ply.gg", manager.PublicAddress);
        Assert.Equal(25565, manager.PublicPort);
    }

    [Fact]
    public async Task StartTunnelAsync_WhenAlreadyActive_IsIdempotent()
    {
        using var worldWatcher = new LanWorldWatcher();
        using var tunnelProvider = new FakeTunnelProvider();
        using var manager = new LobbyStateManager(worldWatcher, tunnelProvider);

        worldWatcher.ProcessLine("[19:45:47] [Render thread/INFO]: Started serving on 25565");
        await manager.StartTunnelAsync();
        Assert.Equal(LobbyState.TunnelActive, manager.State);

        // Повторный вызов не ломает состояние и возвращает true
        bool secondCall = await manager.StartTunnelAsync();
        Assert.True(secondCall);
        Assert.Equal(LobbyState.TunnelActive, manager.State);
    }

    [Fact]
    public void WorldClosed_WithoutPriorWorldOpened_IsIgnored()
    {
        using var worldWatcher = new LanWorldWatcher();
        using var tunnelProvider = new FakeTunnelProvider();
        using var manager = new LobbyStateManager(worldWatcher, tunnelProvider);

        Assert.Equal(LobbyState.Idle, manager.State);

        // Приходит строка закрытия, когда мир и так не был открыт
        worldWatcher.ProcessLine("[19:48:51] [Server thread/INFO]: Stopping server");

        Assert.Equal(LobbyState.Idle, manager.State);
    }

    [Fact]
    public async Task GameExited_WhileTunnelActive_StopsTunnelAndTransitionsToIdle()
    {
        using var worldWatcher = new LanWorldWatcher();
        using var tunnelProvider = new FakeTunnelProvider();
        using var manager = new LobbyStateManager(worldWatcher, tunnelProvider);

        worldWatcher.ProcessLine("[19:45:47] [Render thread/INFO]: Started serving on 25565");
        await manager.StartTunnelAsync();
        Assert.Equal(LobbyState.TunnelActive, manager.State);

        // Симуляция завершения процесса игры (включая аварийный крах)
        manager.OnGameExited();

        Assert.Equal(LobbyState.Idle, manager.State);
        Assert.Null(manager.PublicAddress);
    }

    [Fact]
    public async Task StartTunnelAsync_Cancelled_RevertsToWorldOpen()
    {
        using var worldWatcher = new LanWorldWatcher();
        using var tunnelProvider = new FakeTunnelProvider();
        using var manager = new LobbyStateManager(worldWatcher, tunnelProvider);

        worldWatcher.ProcessLine("[19:45:47] [Render thread/INFO]: Started serving on 25565");

        using var cts = new CancellationTokenSource();
        cts.Cancel(); // Мгновенная отмена

        bool started = await manager.StartTunnelAsync(cts.Token);

        Assert.False(started);
        Assert.Equal(LobbyState.WorldOpen, manager.State);
    }

    [Fact]
    public async Task ResetError_ResetsToWorldOpenIfWorldStillOpen()
    {
        using var worldWatcher = new LanWorldWatcher();
        using var tunnelProvider = new FakeTunnelProvider { ShouldFail = true, FailureMessage = "Quota exceeded" };
        using var manager = new LobbyStateManager(worldWatcher, tunnelProvider);

        worldWatcher.ProcessLine("[19:45:47] [Render thread/INFO]: Started serving on 25565");
        await manager.StartTunnelAsync();
        Assert.Equal(LobbyState.Error, manager.State);

        manager.ResetError();
        Assert.Equal(LobbyState.WorldOpen, manager.State);
    }
}

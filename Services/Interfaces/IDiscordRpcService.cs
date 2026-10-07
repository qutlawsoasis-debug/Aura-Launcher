using System;

namespace AuraLauncher.Services.Interfaces;

public interface IDiscordRpcService : IDisposable
{
    void Initialize();
    void SetInLauncher();
    void SetInLobby(int playerCount, string? hostName = null, string? lobbyCode = null);
    void SetPlayingGame(string? worldName = null, DateTime? startTime = null);
    void UpdateSettings();
}

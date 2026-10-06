using System;

namespace AuraLauncher.Services.Interfaces;

public interface IDiscordRpcService : IDisposable
{
    void Initialize();
    void SetInLauncher();
    void SetInLobby(int playerCount);
    void SetPlayingGame(DateTime? startTime = null);
    void UpdateSettings();
}

using System;
using AuraLauncher.Models;

namespace AuraLauncher.Services.Interfaces;

public interface IMusicBridgeService : IDisposable
{
    int Port { get; }
    string Token { get; }
    bool IsRunning { get; }

    void Start();
    void Stop();

    void WriteConfigFile(string instanceDir);
    void DeleteConfigFile(string instanceDir);

    MusicBridgeNowResponse GetNowPlaying();
}

using Xunit;

namespace AuraLauncher.Tests;

/// <summary>
/// Тесты, которые читают или меняют глобальное состояние лобби
/// (переменная окружения AURA_FAKE_TUNNEL, статический LobbyService.UseE4allTunnel,
/// FakeTunnelProvider.DefaultHost), выполняются последовательно, иначе мешают друг другу.
/// </summary>
[CollectionDefinition(Name, DisableParallelization = true)]
public class LobbyGlobalStateCollection
{
    public const string Name = "LobbyGlobalState";
}

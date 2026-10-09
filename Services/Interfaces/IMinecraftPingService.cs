using System.Threading;
using System.Threading.Tasks;

namespace AuraLauncher.Services.Interfaces;

public record ServerPingResult(int? PingMs, int? PlayersOnline, int? PlayersMax)
{
    public bool HasPing => PingMs.HasValue && PingMs.Value >= 0;
    public bool HasPlayers => PlayersOnline.HasValue;
    public string FormattedPing => HasPing ? $"{PingMs!.Value} мс" : "—";
    public string FormattedPlayers => HasPlayers
        ? (PlayersMax.HasValue ? $"{PlayersOnline!.Value} / {PlayersMax!.Value}" : $"{PlayersOnline!.Value}")
        : "—";
}

public interface IMinecraftPingService
{
    Task<ServerPingResult> PingServerAsync(string serverAddress, CancellationToken cancellationToken = default);
}

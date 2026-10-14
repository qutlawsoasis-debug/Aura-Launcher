using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class CloudLobbyService : ICloudLobbyService
{
    private const string DefaultServerHost = "89.168.82.200";
    private const int DefaultMainServerPort = 25565;
    private const int DefaultManagerPort = 25571;
    private const string DefaultSecret = "aura-secret-2026";

    private readonly HttpClient _httpClient;
    private readonly IMinecraftPingService _pingService;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ServerHost { get; } = DefaultServerHost;
    public int MainServerPort { get; } = DefaultMainServerPort;
    public string MainServerAddress => $"{ServerHost}:{MainServerPort}";
    public string ManagerBaseUrl => $"http://{ServerHost}:{DefaultManagerPort}/";

    public CloudLobbyService(IMinecraftPingService pingService, HttpClient? httpClient = null)
    {
        _pingService = pingService ?? throw new ArgumentNullException(nameof(pingService));
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(5) };
    }

    public async Task<ServerPingResult> PingMainServerAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pingService.PingServerAsync(MainServerAddress, cancellationToken);
        }
        catch
        {
            return new ServerPingResult(null, null, null);
        }
    }

    public async Task<ServerPingResult> PingLobbyAsync(int port, CancellationToken cancellationToken = default)
    {
        try
        {
            return await _pingService.PingServerAsync($"{ServerHost}:{port}", cancellationToken);
        }
        catch
        {
            return new ServerPingResult(null, null, null);
        }
    }

    public async Task<CloudLobbiesListResponse?> GetCloudLobbiesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(ManagerBaseUrl), "lobbies"));
            req.Headers.Add("X-Aura-Secret", DefaultSecret);

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<CloudLobbiesListResponse>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<CloudLobbyCreateResponse?> CreateCloudLobbyAsync(
        string owner,
        string lobbyName,
        int maxPlayers = 8,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                owner = string.IsNullOrWhiteSpace(owner) ? "Player" : owner.Trim(),
                lobbyName = string.IsNullOrWhiteSpace(lobbyName) ? $"{owner}'s World" : lobbyName.Trim(),
                maxPlayers
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(ManagerBaseUrl), "lobbies/create"))
            {
                Content = content
            };
            req.Headers.Add("X-Aura-Secret", DefaultSecret);

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            return JsonSerializer.Deserialize<CloudLobbyCreateResponse>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            return new CloudLobbyCreateResponse(null, null, "error", owner, lobbyName, ex.Message);
        }
    }

    public async Task<bool> StopCloudLobbyAsync(int port, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new { port };
            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(ManagerBaseUrl), "lobbies/stop"))
            {
                Content = content
            };
            req.Headers.Add("X-Aura-Secret", DefaultSecret);

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

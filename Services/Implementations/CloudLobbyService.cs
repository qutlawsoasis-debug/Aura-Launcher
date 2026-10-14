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
    private const int DefaultManagerPort = 25571;
    private const string DefaultSecret = "aura-secret-2026";

    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ServerHost { get; } = DefaultServerHost;
    public string ManagerBaseUrl => $"http://{ServerHost}:{DefaultManagerPort}/";

    public CloudLobbyService(HttpClient? httpClient = null)
    {
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromSeconds(6) };
    }

    public async Task<CloudWorldsResponse?> GetWorldsAsync(string playerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"worlds?player={Uri.EscapeDataString(playerName)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, new Uri(new Uri(ManagerBaseUrl), url));
            req.Headers.Add("X-Aura-Secret", DefaultSecret);

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<CloudWorldsResponse>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<CloudLobbyCreateResponse?> CreateOrStartWorldAsync(
        string owner,
        string worldName,
        string? seed = null,
        string? worldId = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                owner = string.IsNullOrWhiteSpace(owner) ? "Player" : owner.Trim(),
                worldName = string.IsNullOrWhiteSpace(worldName) ? "Мир" : worldName.Trim(),
                seed = seed?.Trim() ?? string.Empty,
                worldId = worldId?.Trim()
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
            return new CloudLobbyCreateResponse(null, null, "error", worldId, worldName, owner, ex.Message);
        }
    }

    public async Task<bool> StopWorldAsync(string? worldId = null, int? port = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                worldId = worldId?.Trim(),
                port
            };

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

    public async Task<CloudWorldShareResponse?> ShareWorldAsync(
        string owner,
        string worldId,
        string targetPlayer,
        string action = "add",
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                owner = owner.Trim(),
                worldId = worldId.Trim(),
                targetPlayer = targetPlayer.Trim(),
                action = action.Trim().ToLowerInvariant()
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(new Uri(ManagerBaseUrl), "lobbies/share"))
            {
                Content = content
            };
            req.Headers.Add("X-Aura-Secret", DefaultSecret);

            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            return JsonSerializer.Deserialize<CloudWorldShareResponse>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            return new CloudWorldShareResponse(false, null, ex.Message);
        }
    }
}

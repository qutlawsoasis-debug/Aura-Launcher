using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class LobbyApiClient : ILobbyApiClient
{
    private readonly HttpClient _httpClient;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly IConfigService? _configService;
    private readonly string _defaultBaseUrl;

    public LobbyApiClient(HttpClient? httpClient = null, string baseUrl = "https://lobby-api.vercel.app", IConfigService? configService = null)
    {
        _httpClient = httpClient ?? new HttpClient();
        _defaultBaseUrl = baseUrl.TrimEnd('/') + "/";
        _configService = configService;
    }

    private Uri GetRequestUri(string relativePath)
    {
        string baseStr = _configService?.CurrentConfig?.LobbyApiBaseUrl ?? _defaultBaseUrl;
        if (string.IsNullOrWhiteSpace(baseStr)) baseStr = _defaultBaseUrl;
        baseStr = baseStr.TrimEnd('/') + "/";
        return new Uri(new Uri(baseStr), relativePath);
    }

    public async Task<LobbyCreateResponse?> CreateLobbyAsync(string hostName, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { hostName }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(GetRequestUri("api/lobby"), content, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<LobbyCreateResponse>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<LobbyJoinResponse?> JoinLobbyAsync(string code, string playerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { code, playerName }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(GetRequestUri("api/lobby/join"), content, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<LobbyJoinResponse>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<LobbyStatusResponse?> GetStatusAsync(string code, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(GetRequestUri($"api/lobby/status?code={Uri.EscapeDataString(code)}"), cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<LobbyStatusResponse>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(System.Net.HttpStatusCode? StatusCode, LobbyStatusResponse? Response, string RawBody)> GetStatusDetailedAsync(string code, CancellationToken cancellationToken = default)
    {
        try
        {
            var response = await _httpClient.GetAsync(GetRequestUri($"api/lobby/status?code={Uri.EscapeDataString(code)}"), cancellationToken);
            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            if (response.IsSuccessStatusCode)
            {
                var parsed = JsonSerializer.Deserialize<LobbyStatusResponse>(json, _jsonOptions);
                return (response.StatusCode, parsed, json);
            }
            return (response.StatusCode, null, json);
        }
        catch
        {
            return (null, null, string.Empty);
        }
    }

    public async Task<bool> OpenLobbyAsync(string code, string hostToken, string tunnelAddress, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { code, hostToken, tunnelAddress }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(GetRequestUri("api/lobby/open"), content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> HeartbeatAsync(string code, string hostToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { code, hostToken }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(GetRequestUri("api/lobby/heartbeat"), content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> CloseLobbyAsync(string code, string hostToken, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { code, hostToken }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(GetRequestUri("api/lobby/close"), content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<TunnelConfigResponse?> GetTunnelConfigAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var req = new HttpRequestMessage(HttpMethod.Get, GetRequestUri("api/tunnel-config"));
            req.Headers.Add("X-Aura-Client", "launcher");
            var response = await _httpClient.SendAsync(req, cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<TunnelConfigResponse>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

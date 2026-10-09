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

    public async Task<LobbyCreateResponse?> CreateLobbyAsync(string hostName, IReadOnlyList<ModManifestEntry>? manifest = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = manifest != null ? (object)new { hostName, manifest } : new { hostName };
            var content = new StringContent(
                JsonSerializer.Serialize(payload),
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

    public async Task<LobbyJoinResponse?> JoinLobbyAsync(string code, string playerName, IReadOnlyList<ModManifestEntry>? manifest = null, CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = manifest != null ? (object)new { code, playerName, manifest } : new { code, playerName };
            var content = new StringContent(
                JsonSerializer.Serialize(payload),
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

    public async Task<bool> UpdateManifestAsync(string code, string playerName, IReadOnlyList<ModManifestEntry> manifest, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { code, playerName, manifest }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(GetRequestUri("api/lobby/manifest"), content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<LobbyStatusResponse?> GetStatusAsync(string code, string? playerName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"api/lobby/status?code={Uri.EscapeDataString(code)}";
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                url += $"&player={Uri.EscapeDataString(playerName.Trim())}";
            }

            var response = await _httpClient.GetAsync(GetRequestUri(url), cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<LobbyStatusResponse>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }

    public async Task<(System.Net.HttpStatusCode? StatusCode, LobbyStatusResponse? Response, string RawBody)> GetStatusDetailedAsync(string code, string? playerName = null, CancellationToken cancellationToken = default)
    {
        try
        {
            string url = $"api/lobby/status?code={Uri.EscapeDataString(code)}";
            if (!string.IsNullOrWhiteSpace(playerName))
            {
                url += $"&player={Uri.EscapeDataString(playerName.Trim())}";
            }

            var response = await _httpClient.GetAsync(GetRequestUri(url), cancellationToken);
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
            if (!response.IsSuccessStatusCode)
            {
                var body = await response.Content.ReadAsStringAsync(cancellationToken);
                PlayitTunnelProvider.LogTunnel($"[HOST-OPEN: ERROR] POST api/lobby/open HTTP {(int)response.StatusCode}: {body}");
                return false;
            }
            return true;
        }
        catch (Exception ex)
        {
            PlayitTunnelProvider.LogTunnel($"[HOST-OPEN: ERROR] POST api/lobby/open exception: {ex.Message}");
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

    public async Task<bool> LeaveLobbyAsync(string code, string playerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { code, playerName }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(GetRequestUri("api/lobby/leave"), content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> KickPlayerAsync(string code, string hostToken, string playerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { code, hostToken, player = playerName }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync(GetRequestUri("api/lobby/kick"), content, cancellationToken);
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

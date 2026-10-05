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

    public LobbyApiClient(HttpClient? httpClient = null, string baseUrl = "http://127.0.0.1:3000")
    {
        _httpClient = httpClient ?? new HttpClient();
        if (_httpClient.BaseAddress == null)
        {
            _httpClient.BaseAddress = new Uri(baseUrl.TrimEnd('/') + "/");
        }
    }

    public async Task<LobbyCreateResponse?> CreateLobbyAsync(string hostName, CancellationToken cancellationToken = default)
    {
        try
        {
            var content = new StringContent(
                JsonSerializer.Serialize(new { hostName }),
                Encoding.UTF8,
                "application/json");

            var response = await _httpClient.PostAsync("api/lobby", content, cancellationToken);
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

            var response = await _httpClient.PostAsync("api/lobby/join", content, cancellationToken);
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
            var response = await _httpClient.GetAsync($"api/lobby/status?code={Uri.EscapeDataString(code)}", cancellationToken);
            if (!response.IsSuccessStatusCode) return null;

            var json = await response.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<LobbyStatusResponse>(json, _jsonOptions);
        }
        catch
        {
            return null;
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

            var response = await _httpClient.PostAsync("api/lobby/open", content, cancellationToken);
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

            var response = await _httpClient.PostAsync("api/lobby/heartbeat", content, cancellationToken);
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

            var response = await _httpClient.PostAsync("api/lobby/close", content, cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}

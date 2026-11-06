using System;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Облачные миры. Лаунчер ходит только в lobby-api (/api/cloud/*); секрет менеджера миров хранится
/// на сервере и в окружении lobby-api, в лаунчере его нет. Загрузка zip мира идёт прямо на менеджер
/// по короткому подписанному билету от lobby-api (через Vercel большие файлы не проходят).
/// Подключение к самой игре (ServerHost:порт) не меняется.
/// </summary>
public class CloudLobbyService : ICloudLobbyService
{
    private const string DefaultServerHost = "89.168.82.200";
    private const string DefaultLobbyApiBaseUrl = "https://lobby-api.vercel.app";

    private readonly HttpClient _httpClient;
    private readonly IConfigService? _configService;
    private readonly string? _apiBaseUrlOverride;
    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public string ServerHost { get; } = DefaultServerHost;

    /// <summary>Базовый адрес облачных эндпоинтов lobby-api, со слешем на конце.</summary>
    public string CloudApiBaseUrl
    {
        get
        {
            var root = _apiBaseUrlOverride;
            if (string.IsNullOrWhiteSpace(root))
            {
                root = _configService?.CurrentConfig?.LobbyApiBaseUrl;
            }
            if (string.IsNullOrWhiteSpace(root))
            {
                root = DefaultLobbyApiBaseUrl;
            }
            return root.TrimEnd('/') + "/api/cloud/";
        }
    }

    public CloudLobbyService(IConfigService? configService = null, HttpClient? httpClient = null, string? apiBaseUrl = null)
    {
        _configService = configService;
        _apiBaseUrlOverride = apiBaseUrl;
        _httpClient = httpClient ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
    }

    private Uri Api(string path) => new(new Uri(CloudApiBaseUrl), path);

    public async Task<CloudWorldsResponse?> GetWorldsAsync(string playerName, CancellationToken cancellationToken = default)
    {
        try
        {
            var url = $"worlds?player={Uri.EscapeDataString(playerName)}";
            using var req = new HttpRequestMessage(HttpMethod.Get, Api(url));

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
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                owner = string.IsNullOrWhiteSpace(owner) ? "Player" : owner.Trim(),
                worldName = string.IsNullOrWhiteSpace(worldName) ? "Мир" : worldName.Trim(),
                seed = seed?.Trim() ?? string.Empty,
                worldId = worldId?.Trim(),
                version = version?.Trim()
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var req = new HttpRequestMessage(HttpMethod.Post, Api("create"))
            {
                Content = content
            };
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

            using var req = new HttpRequestMessage(HttpMethod.Post, Api("stop"))
            {
                Content = content
            };
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

            using var req = new HttpRequestMessage(HttpMethod.Post, Api("share"))
            {
                Content = content
            };
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            return JsonSerializer.Deserialize<CloudWorldShareResponse>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            return new CloudWorldShareResponse(false, null, ex.Message);
        }
    }

    public async Task<CloudWorldDeleteResponse?> DeleteWorldAsync(
        string owner,
        string worldId,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var payload = new
            {
                owner = owner.Trim(),
                worldId = worldId.Trim()
            };

            var content = new StringContent(
                JsonSerializer.Serialize(payload),
                Encoding.UTF8,
                "application/json");

            using var req = new HttpRequestMessage(HttpMethod.Post, Api("delete"))
            {
                Content = content
            };
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<CloudWorldDeleteResponse>(json, _jsonOptions);
        }
        catch (Exception ex)
        {
            return new CloudWorldDeleteResponse(false, null, ex.Message);
        }
    }

    public async Task<CloudWorldItem?> UploadWorldAsync(
        string owner,
        string worldName,
        string zipFilePath,
        string? version = null,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var ownerName = owner.Trim();
            var name = worldName.Trim();

            // 1. Билет на одну загрузку от lobby-api
            var ticketContent = new StringContent(
                JsonSerializer.Serialize(new { owner = ownerName, worldName = name }),
                Encoding.UTF8,
                "application/json");
            using var ticketReq = new HttpRequestMessage(HttpMethod.Post, Api("upload-ticket")) { Content = ticketContent };
            using var ticketResp = await _httpClient.SendAsync(ticketReq, cancellationToken);
            if (!ticketResp.IsSuccessStatusCode)
            {
                return null;
            }
            var ticket = JsonSerializer.Deserialize<CloudUploadTicket>(
                await ticketResp.Content.ReadAsStringAsync(cancellationToken), _jsonOptions);
            if (ticket == null || string.IsNullOrWhiteSpace(ticket.UploadUrl) || string.IsNullOrWhiteSpace(ticket.Token))
            {
                return null;
            }

            // 2. Сам архив — прямо на менеджер, с билетом вместо секрета
            using var fileStream = System.IO.File.OpenRead(zipFilePath);
            using var streamContent = new StreamContent(fileStream);
            streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/zip");

            using var req = new HttpRequestMessage(HttpMethod.Post, new Uri(ticket.UploadUrl))
            {
                Content = streamContent
            };
            req.Headers.Add("X-Upload-Token", ticket.Token);
            req.Headers.Add("X-Upload-Expires", ticket.Expires.ToString(System.Globalization.CultureInfo.InvariantCulture));
            req.Headers.Add("X-Owner", Uri.EscapeDataString(ownerName));
            req.Headers.Add("X-World-Name", Uri.EscapeDataString(name));
            if (!string.IsNullOrWhiteSpace(version))
            {
                req.Headers.Add("X-Game-Version", version.Trim());
            }

            using var resp = await _httpClient.SendAsync(req, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            return JsonSerializer.Deserialize<CloudWorldItem>(json, _jsonOptions);
        }
        catch
        {
            return null;
        }
    }
}

/// <summary>Ответ lobby-api /api/cloud/upload-ticket.</summary>
public record CloudUploadTicket(string? UploadUrl, string? Token, long Expires);

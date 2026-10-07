using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Models;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public class FriendService : IFriendService
{
    private readonly HttpClient _httpClient;
    private readonly IConfigService _configService;
    private readonly string _defaultBaseUrl;

    private static readonly JsonSerializerOptions _jsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    private readonly SemaphoreSlim _regLock = new(1, 1);
    private CancellationTokenSource? _loopCts;
    private Task? _loopTask;
    private readonly HashSet<string> _notifiedInviteIds = new();
    private readonly HashSet<string> _notifiedRequestIds = new();

    public bool IsRegistered => !string.IsNullOrWhiteSpace(_configService.CurrentConfig?.UserId) &&
                                !string.IsNullOrWhiteSpace(_configService.CurrentConfig?.UserTokenEncrypted);

    public string? CurrentUserId => _configService.CurrentConfig?.UserId;
    public string? CurrentFriendCode => _configService.CurrentConfig?.FriendCode;

    public bool IsFriendsTabActive { get; set; }
    public bool IsInLobby { get; set; }
    public string? CurrentLobbyCode { get; set; }
    public string? CurrentHostToken { get; set; }
    public bool IsGameRunning { get; set; }

    public event Action<SyncResponse>? SyncUpdated;
    public event Action<IncomingInviteItem>? InviteReceived;
    public event Action<FriendRequestItem>? FriendRequestReceived;

    public FriendService(IConfigService configService, HttpClient? httpClient = null, string baseUrl = "https://lobby-api.vercel.app")
    {
        _configService = configService ?? throw new ArgumentNullException(nameof(configService));
        _httpClient = httpClient ?? new HttpClient();
        _defaultBaseUrl = baseUrl.TrimEnd('/') + "/";
    }

    private Uri GetRequestUri(string relativePath)
    {
        string baseStr = _configService.CurrentConfig?.LobbyApiBaseUrl ?? _defaultBaseUrl;
        if (string.IsNullOrWhiteSpace(baseStr)) baseStr = _defaultBaseUrl;
        baseStr = baseStr.TrimEnd('/') + "/";
        return new Uri(new Uri(baseStr), relativePath);
    }

    private string? GetDecryptedToken()
    {
        var encrypted = _configService.CurrentConfig?.UserTokenEncrypted;
        if (string.IsNullOrWhiteSpace(encrypted)) return null;

        try
        {
            var cipherBytes = Convert.FromBase64String(encrypted);
            var plainBytes = ProtectedData.Unprotect(cipherBytes, null, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return null;
        }
    }

    private void SetAndEncryptToken(string plainToken)
    {
        if (string.IsNullOrWhiteSpace(plainToken))
        {
            _configService.CurrentConfig.UserTokenEncrypted = null;
            return;
        }

        try
        {
            var plainBytes = Encoding.UTF8.GetBytes(plainToken);
            var cipherBytes = ProtectedData.Protect(plainBytes, null, DataProtectionScope.CurrentUser);
            _configService.CurrentConfig.UserTokenEncrypted = Convert.ToBase64String(cipherBytes);
        }
        catch
        {
            // Fallback (если DPAPI недоступен по какой-то причине)
            _configService.CurrentConfig.UserTokenEncrypted = plainToken;
        }
    }

    public async Task EnsureRegisteredAsync(CancellationToken cancellationToken = default)
    {
        if (IsRegistered && !string.IsNullOrWhiteSpace(GetDecryptedToken()))
        {
            return;
        }

        await _regLock.WaitAsync(cancellationToken);
        try
        {
            if (IsRegistered && !string.IsNullOrWhiteSpace(GetDecryptedToken()))
            {
                return;
            }

            var nick = _configService.CurrentConfig?.Nickname;
            if (string.IsNullOrWhiteSpace(nick)) nick = "Player";

            var content = new StringContent(
                JsonSerializer.Serialize(new { nick }),
                Encoding.UTF8,
                "application/json");

            var resp = await _httpClient.PostAsync(GetRequestUri("api/user/register"), content, cancellationToken);
            if (!resp.IsSuccessStatusCode)
            {
                FabricGameLaunchService.LogLauncherEvent($"[FRIEND: ERROR] Registration failed: HTTP {(int)resp.StatusCode}");
                return;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            var reg = JsonSerializer.Deserialize<UserRegisterResponse>(json, _jsonOptions);
            if (reg != null && !string.IsNullOrWhiteSpace(reg.UserId) && !string.IsNullOrWhiteSpace(reg.UserToken))
            {
                var cfg = _configService.CurrentConfig;
                if (cfg != null)
                {
                    cfg.UserId = reg.UserId;
                    cfg.FriendCode = reg.FriendCode;
                    SetAndEncryptToken(reg.UserToken);
                    await _configService.SaveConfigAsync(cfg, cancellationToken);
                }
            }
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[FRIEND: ERROR] Register exception: {ex.Message}");
        }
        finally
        {
            _regLock.Release();
        }
    }

    private HttpRequestMessage CreateAuthenticatedRequest(HttpMethod method, string relativePath, object? body = null)
    {
        var req = new HttpRequestMessage(method, GetRequestUri(relativePath));
        var userId = CurrentUserId;
        var token = GetDecryptedToken();

        if (!string.IsNullOrWhiteSpace(userId))
        {
            req.Headers.TryAddWithoutValidation("X-User-Id", userId);
        }
        if (!string.IsNullOrWhiteSpace(token))
        {
            req.Headers.TryAddWithoutValidation("X-User-Token", token);
        }

        if (body != null)
        {
            req.Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json");
        }

        return req;
    }

    public async Task<SyncResponse?> SyncNowAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRegistered)
        {
            await EnsureRegisteredAsync(cancellationToken);
            if (!IsRegistered) return null;
        }

        try
        {
            string status = IsGameRunning ? "playing" : (IsInLobby ? "lobby" : "online");
            string nick = _configService.CurrentConfig?.Nickname ?? "Player";
            string? lobbyCode = IsInLobby ? CurrentLobbyCode : null;

            var body = new
            {
                nick,
                status,
                lobbyCode
            };

            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "api/sync", body);
            using var resp = await _httpClient.SendAsync(req, cancellationToken);

            if (resp.StatusCode == System.Net.HttpStatusCode.Unauthorized)
            {
                // Токен невалиден или сброшен — перерегистрируем
                FabricGameLaunchService.LogLauncherEvent("[FRIEND] Sync returned 401. Re-registering user...");
                _configService.CurrentConfig.UserId = null;
                _configService.CurrentConfig.UserTokenEncrypted = null;
                await EnsureRegisteredAsync(cancellationToken);
                return null;
            }

            if (!resp.IsSuccessStatusCode)
            {
                return null;
            }

            var json = await resp.Content.ReadAsStringAsync(cancellationToken);
            var syncResult = JsonSerializer.Deserialize<SyncResponse>(json, _jsonOptions);
            if (syncResult != null)
            {
                // Проверяем новые входящие приглашения
                if (syncResult.Invites != null)
                {
                    foreach (var invite in syncResult.Invites)
                    {
                        if (!_notifiedInviteIds.Contains(invite.InviteId))
                        {
                            _notifiedInviteIds.Add(invite.InviteId);
                            InviteReceived?.Invoke(invite);
                        }
                    }
                }

                // Проверяем новые входящие заявки в друзья
                if (syncResult.IncomingRequests != null)
                {
                    foreach (var incomingReq in syncResult.IncomingRequests)
                    {
                        if (!_notifiedRequestIds.Contains(incomingReq.Id))
                        {
                            _notifiedRequestIds.Add(incomingReq.Id);
                            FriendRequestReceived?.Invoke(incomingReq);
                        }
                    }
                }

                SyncUpdated?.Invoke(syncResult);
            }

            return syncResult;
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[FRIEND: SYNC ERROR] {ex.Message}");
            return null;
        }
    }

    public async Task ReportOfflineAsync(CancellationToken cancellationToken = default)
    {
        if (!IsRegistered) return;
        try
        {
            string nick = _configService.CurrentConfig?.Nickname ?? "Player";
            var body = new
            {
                nick,
                status = "offline"
            };

            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "api/sync", body);
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            cts.CancelAfter(2000);
            using var resp = await _httpClient.SendAsync(req, cts.Token);
        }
        catch { }
    }

    public async Task<(bool Success, string? ErrorMessage)> SendFriendRequestAsync(string friendCode, CancellationToken cancellationToken = default)
    {
        if (!IsRegistered) await EnsureRegisteredAsync(cancellationToken);

        try
        {
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "api/friends/request", new { friendCode });
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (resp.IsSuccessStatusCode)
            {
                _ = SyncNowAsync(cancellationToken);
                return (true, null);
            }

            var errResp = JsonSerializer.Deserialize<GenericApiResponse>(json, _jsonOptions);
            return (false, errResp?.Error ?? "Ошибка отправки заявки");
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public async Task<bool> RespondFriendRequestAsync(string fromId, bool accept, CancellationToken cancellationToken = default)
    {
        if (!IsRegistered) await EnsureRegisteredAsync(cancellationToken);

        try
        {
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "api/friends/respond", new { fromId, accept });
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                _ = SyncNowAsync(cancellationToken);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<bool> RemoveFriendAsync(string friendId, CancellationToken cancellationToken = default)
    {
        if (!IsRegistered) await EnsureRegisteredAsync(cancellationToken);

        try
        {
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "api/friends/remove", new { friendId });
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            if (resp.IsSuccessStatusCode)
            {
                _ = SyncNowAsync(cancellationToken);
                return true;
            }
            return false;
        }
        catch
        {
            return false;
        }
    }

    public async Task<(bool Success, string? InviteId, string? ErrorMessage)> SendInviteAsync(string friendId, string lobbyCode, string hostToken, CancellationToken cancellationToken = default)
    {
        if (!IsRegistered) await EnsureRegisteredAsync(cancellationToken);

        try
        {
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "api/invite", new { friendId, lobbyCode, hostToken });
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (resp.IsSuccessStatusCode)
            {
                var inv = JsonSerializer.Deserialize<InviteCreateResponse>(json, _jsonOptions);
                _ = SyncNowAsync(cancellationToken);
                return (true, inv?.InviteId, null);
            }

            var err = JsonSerializer.Deserialize<GenericApiResponse>(json, _jsonOptions);
            return (false, null, err?.Error ?? "Ошибка отправки приглашения");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<(bool Success, string? LobbyCode, string? ErrorMessage)> RespondInviteAsync(string inviteId, bool accept, CancellationToken cancellationToken = default)
    {
        if (!IsRegistered) await EnsureRegisteredAsync(cancellationToken);

        try
        {
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "api/invite/respond", new { inviteId, accept });
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            var json = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (resp.IsSuccessStatusCode)
            {
                var res = JsonSerializer.Deserialize<InviteRespondResponse>(json, _jsonOptions);
                _ = SyncNowAsync(cancellationToken);
                return (true, res?.LobbyCode, null);
            }

            var err = JsonSerializer.Deserialize<GenericApiResponse>(json, _jsonOptions);
            return (false, null, err?.Error ?? "Ошибка ответа на приглашение");
        }
        catch (Exception ex)
        {
            return (false, null, ex.Message);
        }
    }

    public async Task<(bool Success, string? ErrorMessage)> ChangeNicknameAsync(string newNick, CancellationToken cancellationToken = default)
    {
        if (!IsRegistered)
        {
            await EnsureRegisteredAsync(cancellationToken);
            if (!IsRegistered)
            {
                return (false, "Ошибка регистрации пользователя на сервере.");
            }
        }

        try
        {
            var body = new { nick = newNick.Trim() };
            using var req = CreateAuthenticatedRequest(HttpMethod.Post, "api/user/nick", body);
            using var resp = await _httpClient.SendAsync(req, cancellationToken);
            var responseText = await resp.Content.ReadAsStringAsync(cancellationToken);

            if (resp.StatusCode == System.Net.HttpStatusCode.Conflict)
            {
                return (false, "Этот ник уже занят другим игроком, выбери другой");
            }

            if (!resp.IsSuccessStatusCode)
            {
                string err = "Ошибка смены ника на сервере";
                try
                {
                    using var doc = JsonDocument.Parse(responseText);
                    if (doc.RootElement.TryGetProperty("error", out var errElem))
                    {
                        err = errElem.GetString() ?? err;
                    }
                }
                catch { }
                return (false, err);
            }

            return (true, null);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            return (false, $"Сетевая ошибка при смене ника: {ex.Message}");
        }
    }

    public void Start()
    {
        if (_loopCts != null) return;

        _loopCts = new CancellationTokenSource();
        var token = _loopCts.Token;

        _loopTask = Task.Run(async () =>
        {
            // Первый запуск: тихая регистрация и первичный синк
            await EnsureRegisteredAsync(token);
            await SyncNowAsync(token);

            while (!token.IsCancellationRequested)
            {
                try
                {
                    int delayMs = (IsFriendsTabActive || IsInLobby) ? 5000 : 20000;
                    await Task.Delay(delayMs, token);
                    await SyncNowAsync(token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    FabricGameLaunchService.LogLauncherEvent($"[FRIEND: LOOP EXCEPTION] {ex.Message}");
                }
            }
        }, token);
    }

    public void Stop()
    {
        try
        {
            _loopCts?.Cancel();
            _loopCts?.Dispose();
        }
        catch { }
        finally
        {
            _loopCts = null;
            _loopTask = null;
        }
    }

    public void Dispose()
    {
        Stop();
    }
}

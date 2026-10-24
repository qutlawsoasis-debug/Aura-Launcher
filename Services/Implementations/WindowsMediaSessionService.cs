using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using Windows.Media.Control;
using Windows.Storage.Streams;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

public sealed class WindowsMediaSessionService : IDisposable
{
    private static readonly string StateFilePath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        ".aura",
        "music_session.json"
    );

    private static readonly string StateFileTmp = StateFilePath + ".tmp";
    private static readonly string StateFileDir = Path.GetDirectoryName(StateFilePath)!;

    private readonly IConfigService _configService;
    private GlobalSystemMediaTransportControlsSessionManager? _sessionManager;
    private GlobalSystemMediaTransportControlsSession? _currentSession;
    private readonly Timer _pollTimer;
    private readonly object _lock = new();
    private bool _isDisposed;

    public WindowsMediaSessionService(IConfigService configService)
    {
        _configService = configService;
        _pollTimer = new Timer(OnPoll, null, Timeout.Infinite, Timeout.Infinite);
    }

    public void Start()
    {
        try
        {
            if (!Directory.Exists(StateFileDir))
            {
                Directory.CreateDirectory(StateFileDir);
            }

            Task.Run(async () =>
            {
                try
                {
                    _sessionManager = await GlobalSystemMediaTransportControlsSessionManager.RequestAsync();
                    if (_sessionManager != null)
                    {
                        _sessionManager.CurrentSessionChanged += OnCurrentSessionChanged;
                        UpdateCurrentSession();
                        _pollTimer.Change(500, 500);
                    }
                }
                catch (Exception ex)
                {
                    FabricGameLaunchService.LogLauncherEvent($"[WindowsMediaSession] Init error: {ex.Message}");
                }
            });
        }
        catch (Exception ex)
        {
            FabricGameLaunchService.LogLauncherEvent($"[WindowsMediaSession] Start failed: {ex.Message}");
        }
    }

    private void OnCurrentSessionChanged(GlobalSystemMediaTransportControlsSessionManager sender, CurrentSessionChangedEventArgs args)
    {
        UpdateCurrentSession();
    }

    private void UpdateCurrentSession()
    {
        lock (_lock)
        {
            if (_sessionManager == null) return;
            try
            {
                _currentSession = _sessionManager.GetCurrentSession();
            }
            catch
            {
                _currentSession = null;
            }
        }
        _ = PollMediaStateAsync();
    }

    private void OnPoll(object? state)
    {
        _ = CheckIncomingCommandsAsync();
        _ = PollMediaStateAsync();
    }

    private async Task CheckIncomingCommandsAsync()
    {
        string cmdPath = Path.Combine(StateFileDir, "music_command.json");
        if (!File.Exists(cmdPath)) return;

        try
        {
            string json = File.ReadAllText(cmdPath);
            File.Delete(cmdPath);
            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("command", out var cmdProp))
            {
                string cmd = cmdProp.GetString() ?? "";
                double? seek = null;
                if (doc.RootElement.TryGetProperty("seek", out var seekProp))
                {
                    seek = seekProp.GetDouble();
                }
                await ExecuteCommandAsync(cmd, seek);
            }
        }
        catch { }
    }


    public async Task PollMediaStateAsync()
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock)
        {
            session = _currentSession;
        }

        if (session == null)
        {
            WriteMediaState(new MediaSessionState
            {
                HasActiveSession = false,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
            return;
        }

        try
        {
            string appId = session.SourceAppUserModelId ?? "";
            var mediaProperties = await session.TryGetMediaPropertiesAsync();
            var playbackInfo = session.GetPlaybackInfo();
            var timeline = session.GetTimelineProperties();

            bool isPlaying = playbackInfo?.PlaybackStatus == GlobalSystemMediaTransportControlsSessionPlaybackStatus.Playing;
            double durationSec = timeline?.EndTime.TotalSeconds ?? 0;
            double positionSec = timeline?.Position.TotalSeconds ?? 0;

            string title = mediaProperties?.Title ?? "";
            string artist = mediaProperties?.Artist ?? "";
            string album = mediaProperties?.AlbumTitle ?? "";

            // Обложка при наличии
            string? thumbnailBase64 = null;
            if (mediaProperties?.Thumbnail != null)
            {
                try
                {
                    using var stream = await mediaProperties.Thumbnail.OpenReadAsync();
                    if (stream.Size > 0 && stream.Size < 5 * 1024 * 1024)
                    {
                        using var netStream = stream.AsStreamForRead();
                        using var ms = new MemoryStream();
                        await netStream.CopyToAsync(ms);
                        thumbnailBase64 = "data:image/png;base64," + Convert.ToBase64String(ms.ToArray());
                    }
                }
                catch { }
            }

            var state = new MediaSessionState
            {
                HasActiveSession = !string.IsNullOrEmpty(title) || isPlaying,
                SourceApp = DetectAppName(appId),
                AppId = appId,
                Title = title,
                Artist = artist,
                Album = album,
                IsPlaying = isPlaying,
                PositionSeconds = positionSec,
                DurationSeconds = durationSec,
                ThumbnailBase64 = thumbnailBase64,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            };

            WriteMediaState(state);
        }
        catch (Exception)
        {
            // Сессия могла закрыться
            WriteMediaState(new MediaSessionState
            {
                HasActiveSession = false,
                TimestampMs = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds()
            });
        }
    }

    public async Task<bool> ExecuteCommandAsync(string command, double? seekSec = null)
    {
        GlobalSystemMediaTransportControlsSession? session;
        lock (_lock)
        {
            session = _currentSession;
        }
        if (session == null) return false;

        try
        {
            switch (command.ToLowerInvariant())
            {
                case "play":
                    return await session.TryPlayAsync();
                case "pause":
                    return await session.TryPauseAsync();
                case "toggle":
                    return await session.TryTogglePlayPauseAsync();
                case "next":
                    return await session.TrySkipNextAsync();
                case "prev":
                    return await session.TrySkipPreviousAsync();
                case "seek":
                    if (seekSec.HasValue)
                    {
                        long ticks = (long)(seekSec.Value * TimeSpan.TicksPerSecond);
                        return await session.TryChangePlaybackPositionAsync(ticks);
                    }
                    return false;
                default:
                    return false;
            }
        }
        catch
        {
            return false;
        }
    }

    private static string DetectAppName(string appId)
    {
        string lower = appId.ToLowerInvariant();
        if (lower.Contains("yandex") || lower.Contains("music.yandex")) return "Яндекс Музыка";
        if (lower.Contains("spotify")) return "Spotify";
        if (lower.Contains("vk") || lower.Contains("vkontakte")) return "VK Музыка";
        if (lower.Contains("apple") || lower.Contains("itunes")) return "Apple Music";
        if (lower.Contains("chrome")) return "Chrome / YouTube";
        if (lower.Contains("firefox")) return "Firefox";
        if (lower.Contains("msedge") || lower.Contains("edge")) return "Microsoft Edge";
        if (lower.Contains("opera")) return "Opera";
        if (lower.Contains("foobar2000")) return "foobar2000";
        if (lower.Contains("aimp")) return "AIMP";
        return !string.IsNullOrWhiteSpace(appId) ? appId : "Медиаплеер";
    }

    private void WriteMediaState(MediaSessionState state)
    {
        try
        {
            string json = JsonSerializer.Serialize(state, new JsonSerializerOptions
            {
                WriteIndented = true,
                DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
            });

            File.WriteAllText(StateFileTmp, json);
            if (File.Exists(StateFilePath))
            {
                File.Replace(StateFileTmp, StateFilePath, null, true);
            }
            else
            {
                File.Move(StateFileTmp, StateFilePath);
            }
        }
        catch { }
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;
        _pollTimer.Dispose();
        if (_sessionManager != null)
        {
            _sessionManager.CurrentSessionChanged -= OnCurrentSessionChanged;
        }
    }

    public sealed class MediaSessionState
    {
        [JsonPropertyName("has_active_session")]
        public bool HasActiveSession { get; set; }

        [JsonPropertyName("source_app")]
        public string SourceApp { get; set; } = "";

        [JsonPropertyName("app_id")]
        public string AppId { get; set; } = "";

        [JsonPropertyName("title")]
        public string Title { get; set; } = "";

        [JsonPropertyName("artist")]
        public string Artist { get; set; } = "";

        [JsonPropertyName("album")]
        public string Album { get; set; } = "";

        [JsonPropertyName("is_playing")]
        public bool IsPlaying { get; set; }

        [JsonPropertyName("position")]
        public double PositionSeconds { get; set; }

        [JsonPropertyName("duration")]
        public double DurationSeconds { get; set; }

        [JsonPropertyName("thumbnail")]
        public string? ThumbnailBase64 { get; set; }

        [JsonPropertyName("timestamp_ms")]
        public long TimestampMs { get; set; }
    }
}

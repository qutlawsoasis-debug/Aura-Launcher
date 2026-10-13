using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Services.Implementations;

/// <summary>
/// Реализация парсера latest.log с безопасным совместным чтением (FileShare.ReadWrite),
/// построчной буферизацией неполных чанков, обработкой усечения и событиями LAN-мира.
/// </summary>
public class LanWorldWatcher : ILanWorldWatcher
{
    // Строка из реального лога: [19:45:47] [Render thread/INFO]: Started serving on 25565
    private static readonly Regex StartedServingRegex = new(
        @"Started serving on (\d{1,5})",
        RegexOptions.Compiled);

    // Строки из реального лога: [19:48:51] [Server thread/INFO]: Stopping singleplayer server as player logged out
    // [19:48:51] [Server thread/INFO]: Stopping server
    // [19:48:53] [Render thread/INFO]: Stopping!
    private static readonly Regex StoppingServerRegex = new(
        @"Stopping singleplayer server|Stopping server|Stopping!",
        RegexOptions.Compiled);

    // Строки назначения домена e4mc / e4all (на английском и русском):
    // [Server thread/INFO]: e4all: Domain assigned: quick-fox-42.e4mc.link
    // Domain assigned: quick-fox-42.e4mc.link
    // Local game hosted on domain [quick-fox-42.e4mc.link]
    // Локальный сервер размещен на домене [quick-fox-42.eu.e4mc.link]
    private static readonly Regex DomainAssignedRegex = new(
        @"(?:Domain assigned:\s*|e4all: Domain assigned:\s*|hosted on domain\s*\[|размещен на домене\s*\[|размещена на домене\s*\[|домене\s*\[)\s*(?<domain>[a-zA-Z0-9\.\-_]+)|(?<domain>[a-zA-Z0-9\.\-_]+\.e4mc\.link)",
        RegexOptions.Compiled | RegexOptions.IgnoreCase);

    private readonly object _stateLock = new();
    private string? _logFilePath;
    private Timer? _pollTimer;
    private long _lastPosition;
    private readonly StringBuilder _lineBuffer = new();
    private bool _isDisposed;

    public event Action<int>? WorldOpened;
    public event Action? WorldClosed;
    public event Action<string>? DomainAssigned;

    public bool IsWorldOpen { get; private set; }
    public int? CurrentPort { get; private set; }
    public string? CurrentDomain { get; private set; }

    public void Start(string logFilePath, bool readFromEnd = true, bool scanRecentTail = false)
    {
        if (string.IsNullOrWhiteSpace(logFilePath))
            throw new ArgumentNullException(nameof(logFilePath));

        lock (_stateLock)
        {
            StopInternal();
            _logFilePath = logFilePath;
            _lineBuffer.Clear();
            IsWorldOpen = false;
            CurrentPort = null;
            CurrentDomain = null;

            if (scanRecentTail && File.Exists(logFilePath))
            {
                var fi = new FileInfo(logFilePath);
                _lastPosition = Math.Max(0, fi.Length - 64 * 1024);
                PollLogFile();
            }
            else
            {
                _lastPosition = (readFromEnd && File.Exists(logFilePath)) ? new FileInfo(logFilePath).Length : 0;
                PollLogFile();
            }

            _pollTimer = new Timer(_ => PollLogFile(), null, TimeSpan.FromMilliseconds(250), TimeSpan.FromMilliseconds(250));
        }
    }

    public void Stop()
    {
        lock (_stateLock)
        {
            StopInternal();
        }
    }

    private void StopInternal()
    {
        _pollTimer?.Dispose();
        _pollTimer = null;
        _lineBuffer.Clear();
        CurrentDomain = null;
        if (IsWorldOpen)
        {
            IsWorldOpen = false;
            CurrentPort = null;
            WorldClosed?.Invoke();
        }
    }

    /// <summary>
    /// Парсит новые байты из latest.log с буферизацией незавершённых строк.
    /// </summary>
    public void PollLogFile()
    {
        lock (_stateLock)
        {
            if (_isDisposed || _logFilePath == null || !File.Exists(_logFilePath))
                return;

            try
            {
                using var fs = new FileStream(_logFilePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
                
                // Проверка на ротацию или усечение файла (размер стал меньше предыдущей позиции)
                if (fs.Length < _lastPosition)
                {
                    _lastPosition = 0;
                    _lineBuffer.Clear();
                    CurrentDomain = null;
                    if (IsWorldOpen)
                    {
                        IsWorldOpen = false;
                        CurrentPort = null;
                        WorldClosed?.Invoke();
                    }
                }

                if (fs.Length == _lastPosition)
                    return;

                fs.Seek(_lastPosition, SeekOrigin.Begin);
                var buffer = new byte[4096];
                int bytesRead;

                while ((bytesRead = fs.Read(buffer, 0, buffer.Length)) > 0)
                {
                    var text = Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    for (int i = 0; i < text.Length; i++)
                    {
                        char c = text[i];
                        if (c == '\n')
                        {
                            var fullLine = _lineBuffer.ToString().TrimEnd('\r');
                            _lineBuffer.Clear();
                            ProcessLine(fullLine);
                        }
                        else
                        {
                            _lineBuffer.Append(c);
                        }
                    }
                }

                _lastPosition = fs.Position;
            }
            catch (IOException)
            {
                // Игнорируем временную занятость дескриптора при записи Minecraft
            }
            catch (Exception)
            {
            }
        }
    }

    /// <summary>
    /// Парсит отдельную завершённую строку лога игры.
    /// </summary>
    public void ProcessLine(string line)
    {
        if (string.IsNullOrWhiteSpace(line)) return;

        var domainMatch = DomainAssignedRegex.Match(line);
        if (domainMatch.Success)
        {
            var domain = domainMatch.Groups["domain"].Value.Trim();
            if (!string.IsNullOrWhiteSpace(domain))
            {
                CurrentDomain = domain;
                try
                {
                    DomainAssigned?.Invoke(domain);
                }
                catch (Exception ex)
                {
                    PlayitTunnelProvider.LogTunnel($"[EXCEPTION] LanWorldWatcher DomainAssigned event: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }

        var startMatch = StartedServingRegex.Match(line);
        if (startMatch.Success)
        {
            if (int.TryParse(startMatch.Groups[1].Value, out int port) && port >= 1 && port <= 65535)
            {
                IsWorldOpen = true;
                CurrentPort = port;
                try
                {
                    WorldOpened?.Invoke(port);
                }
                catch (Exception ex)
                {
                    PlayitTunnelProvider.LogTunnel($"[EXCEPTION] LanWorldWatcher WorldOpened event: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                }
                return;
            }
        }

        if (StoppingServerRegex.IsMatch(line))
        {
            CurrentDomain = null;
            if (IsWorldOpen)
            {
                IsWorldOpen = false;
                CurrentPort = null;
                try
                {
                    WorldClosed?.Invoke();
                }
                catch (Exception ex)
                {
                    PlayitTunnelProvider.LogTunnel($"[EXCEPTION] LanWorldWatcher WorldClosed event: {ex.GetType().FullName}: {ex.Message}\n{ex.StackTrace}");
                }
            }
        }
    }

    public void Dispose()
    {
        lock (_stateLock)
        {
            if (_isDisposed) return;
            _isDisposed = true;
            StopInternal();
        }
    }
}

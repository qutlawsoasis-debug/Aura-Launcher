using System;
using System.Threading;
using System.Threading.Tasks;
using AuraLauncher.Services.Interfaces;

namespace AuraLauncher.Models;

public enum LobbyState
{
    Idle,            // Мир в игре ещё не открыт для сети
    WorldOpen,       // Мир открыт на локальном порту, туннель не запущен
    TunnelStarting,  // Пользователь нажал "Запустить туннель"
    TunnelActive,    // Туннель активен, публичный адрес доступен
    Error            // Ошибка старта туннеля
}

/// <summary>
/// Чистая доменная модель управления состоянием LAN-лобби.
/// Инварианты:
/// - Нет автозапуска туннеля (только явный вызов StartTunnelAsync).
/// - Повторный StartTunnelAsync идемпотентен.
/// - WorldClosed без предшествующего WorldOpened игнорируется.
/// - Завершение игры (OnGameExited) глушит туннель и сбрасывает в Idle.
/// - Поддержка отмены CancellationToken в TunnelStarting.
/// </summary>
public class LobbyStateManager : IDisposable
{
    private readonly ILanWorldWatcher _worldWatcher;
    private readonly ITunnelProvider _tunnelProvider;
    private readonly object _stateLock = new();

    public LobbyState State { get; private set; } = LobbyState.Idle;
    public int? LocalPort => _worldWatcher.CurrentPort;
    public string? PublicAddress => _tunnelProvider.CurrentInfo.PublicAddress;
    public int? PublicPort => _tunnelProvider.CurrentInfo.PublicPort;
    public string? LastError { get; private set; }

    public event Action<LobbyState>? StateChanged;

    public LobbyStateManager(ILanWorldWatcher worldWatcher, ITunnelProvider tunnelProvider)
    {
        _worldWatcher = worldWatcher ?? throw new ArgumentNullException(nameof(worldWatcher));
        _tunnelProvider = tunnelProvider ?? throw new ArgumentNullException(nameof(tunnelProvider));

        _worldWatcher.WorldOpened += OnWorldOpened;
        _worldWatcher.WorldClosed += OnWorldClosed;
    }

    private void SetState(LobbyState newState, string? error = null)
    {
        lock (_stateLock)
        {
            State = newState;
            LastError = error;
            StateChanged?.Invoke(newState);
        }
    }

    private void OnWorldOpened(int port)
    {
        lock (_stateLock)
        {
            if (State == LobbyState.Idle || State == LobbyState.Error)
            {
                SetState(LobbyState.WorldOpen);
            }
        }
    }

    private void OnWorldClosed()
    {
        lock (_stateLock)
        {
            // WorldClosed без предшествующего WorldOpened/Tunneling игнорируется
            if (State == LobbyState.Idle)
            {
                return;
            }

            // Если мир закрыт, туннель немедленно останавливается
            _ = _tunnelProvider.StopAsync(CancellationToken.None);
            SetState(LobbyState.Idle);
        }
    }

    /// <summary>
    /// Вызывается при закрытии или аварийном крахе процесса игры.
    /// </summary>
    public void OnGameExited()
    {
        lock (_stateLock)
        {
            _ = _tunnelProvider.StopAsync(CancellationToken.None);
            SetState(LobbyState.Idle);
        }
    }

    /// <summary>
    /// Сброс ошибки (Error -> Idle или WorldOpen).
    /// </summary>
    public void ResetError()
    {
        lock (_stateLock)
        {
            if (State == LobbyState.Error)
            {
                SetState(_worldWatcher.IsWorldOpen ? LobbyState.WorldOpen : LobbyState.Idle);
            }
        }
    }

    /// <summary>
    /// Явный запуск туннеля по клику/команде хоста. Идемпотентен.
    /// </summary>
    public async Task<bool> StartTunnelAsync(CancellationToken ct = default)
    {
        lock (_stateLock)
        {
            if (State == LobbyState.TunnelActive)
            {
                return true; // Идемпотентный возврат
            }

            if (State != LobbyState.WorldOpen && State != LobbyState.Error)
            {
                return false;
            }

            if (!_worldWatcher.IsWorldOpen || !_worldWatcher.CurrentPort.HasValue)
            {
                SetState(LobbyState.Error, "Локальный мир не открыт");
                return false;
            }

            SetState(LobbyState.TunnelStarting);
        }

        try
        {
            ct.ThrowIfCancellationRequested();
            var result = await _tunnelProvider.StartAsync(_worldWatcher.CurrentPort!.Value, ct).ConfigureAwait(false);
            
            lock (_stateLock)
            {
                if (result.Status == TunnelStatus.Active)
                {
                    SetState(LobbyState.TunnelActive);
                    return true;
                }
                else
                {
                    SetState(LobbyState.Error, result.ErrorMessage ?? "Не удалось поднять туннель");
                    return false;
                }
            }
        }
        catch (OperationCanceledException)
        {
            lock (_stateLock)
            {
                SetState(_worldWatcher.IsWorldOpen ? LobbyState.WorldOpen : LobbyState.Idle);
            }
            return false;
        }
        catch (Exception ex)
        {
            lock (_stateLock)
            {
                SetState(LobbyState.Error, ex.Message);
            }
            return false;
        }
    }

    /// <summary>
    /// Остановка туннеля хостом.
    /// </summary>
    public async Task StopTunnelAsync(CancellationToken ct = default)
    {
        await _tunnelProvider.StopAsync(ct).ConfigureAwait(false);
        lock (_stateLock)
        {
            if (_worldWatcher.IsWorldOpen)
            {
                SetState(LobbyState.WorldOpen);
            }
            else
            {
                SetState(LobbyState.Idle);
            }
        }
    }

    public void Dispose()
    {
        _worldWatcher.WorldOpened -= OnWorldOpened;
        _worldWatcher.WorldClosed -= OnWorldClosed;
        _worldWatcher.Dispose();
        _tunnelProvider.Dispose();
    }
}

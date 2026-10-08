using System;
using System.Threading.Tasks;
using System.Windows.Input;

namespace AuraLauncher.Core;

/// <summary>
/// Асинхронная команда с флагом IsExecuting для защиты от параллельного/повторного вызова.
/// </summary>
public class AsyncRelayCommand : ICommand
{
    private readonly Func<object?, Task> _executeAsync;
    private readonly Predicate<object?>? _canExecute;
    private bool _isExecuting;

    private event EventHandler? _canExecuteChanged;

    public event EventHandler? CanExecuteChanged
    {
        add
        {
            _canExecuteChanged += value;
            CommandManager.RequerySuggested += value;
        }
        remove
        {
            _canExecuteChanged -= value;
            CommandManager.RequerySuggested -= value;
        }
    }

    public bool IsExecuting
    {
        get => _isExecuting;
        private set
        {
            if (_isExecuting != value)
            {
                _isExecuting = value;
                RaiseCanExecuteChanged();
            }
        }
    }

    public AsyncRelayCommand(Func<object?, Task> executeAsync, Predicate<object?>? canExecute = null)
    {
        _executeAsync = executeAsync ?? throw new ArgumentNullException(nameof(executeAsync));
        _canExecute = canExecute;
    }

    public AsyncRelayCommand(Func<Task> executeAsync, Func<bool>? canExecute = null)
        : this(_ => executeAsync(), canExecute != null ? _ => canExecute() : null)
    {
    }

    public bool CanExecute(object? parameter)
    {
        return !IsExecuting && (_canExecute?.Invoke(parameter) ?? true);
    }

    public async void Execute(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        try
        {
            IsExecuting = true;
            await _executeAsync(parameter);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    public async Task ExecuteAsync(object? parameter)
    {
        if (!CanExecute(parameter)) return;

        try
        {
            IsExecuting = true;
            await _executeAsync(parameter);
        }
        finally
        {
            IsExecuting = false;
        }
    }

    public void RaiseCanExecuteChanged()
    {
        if (System.Windows.Application.Current?.Dispatcher is { } dispatcher && !dispatcher.CheckAccess())
        {
            dispatcher.BeginInvoke(new Action(RaiseCanExecuteChanged));
            return;
        }
        _canExecuteChanged?.Invoke(this, EventArgs.Empty);
        CommandManager.InvalidateRequerySuggested();
    }
}

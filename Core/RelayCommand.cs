using System;
using System.Windows.Input;

namespace AuraLauncher.Core;

/// <summary>
/// Стандартная синхронная реализация ICommand для MVVM.
/// </summary>
public class RelayCommand : ICommand
{
    private readonly Action<object?> _execute;
    private readonly Predicate<object?>? _canExecute;

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

    public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
    {
        _execute = execute ?? throw new ArgumentNullException(nameof(execute));
        _canExecute = canExecute;
    }

    public RelayCommand(Action execute, Func<bool>? canExecute = null)
        : this(_ => execute(), canExecute != null ? _ => canExecute() : null)
    {
    }

    public bool CanExecute(object? parameter) => _canExecute?.Invoke(parameter) ?? true;

    public void Execute(object? parameter) => _execute(parameter);

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

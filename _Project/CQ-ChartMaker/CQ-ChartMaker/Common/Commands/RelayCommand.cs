using System;
using System.Windows.Input;

// namespace TestAppProject1.Commands // 原命名空间
namespace CQ_ChartMaker.Common
{
    public class RelayCommand : ICommand
    {
        private readonly Action<object?> _execute;
        private readonly Predicate<object?>? _canExecute;

        public RelayCommand(Action<object?> execute, Predicate<object?>? canExecute = null)
        {
            _execute = execute ?? throw new ArgumentNullException(nameof(execute));
            _canExecute = canExecute;
        }

        public bool CanExecute(object? parameter) => _canExecute == null || _canExecute(parameter);

        public void Execute(object? parameter) => _execute(parameter);

        // 简单实现 CanExecuteChanged，在实际项目中可用 CommandManager 或 ReactiveUI
        public event EventHandler? CanExecuteChanged
        {
            add { }
            remove { }
        }
    }
}
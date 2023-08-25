using QuikSharp;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Input;


namespace OsEngine.Commands
{
    public class DelegateCommand : ICommand
    {
        public DelegateCommand(DelegateFunction function)
        {
            _function = function;
        }
        public delegate void DelegateFunction(object obj);
        public event EventHandler CanExecuteChanged;
        // ============================================================
        private DelegateFunction _function;
        // ============================================================
        public bool CanExecute(object parameter)
        {
            return true;
        }

        public void Execute(object parameter)
        {
            _function?.Invoke(parameter);
        }

    }
}

using System;
using System.Windows;
using System.Windows.Threading;
using InvAuto.Core.Logging;

namespace InvAuto.Framework
{
    /// <summary>
    /// Страховка процесса Inventor: необработанная ошибка в любом окне InvAuto (в том числе в продолжениях
    /// async-обработчиков) записывается в журнал и показывается сообщением, а не роняет Inventor.
    /// Ошибки чужого кода не перехватываются.
    /// </summary>
    public static class ErrorGuard
    {
        private static ILogger _log;
        private static Dispatcher _dispatcher;

        /// <summary>Ошибка перехвачена — окна сбрасывают своё состояние (например, «занято»).</summary>
        public static event Action<Exception> Failed;

        public static void Install(ILogger log)
        {
            if (_dispatcher != null)
                return;
            _log = log;
            _dispatcher = Dispatcher.CurrentDispatcher;
            _dispatcher.UnhandledException += OnUnhandled;
        }

        public static void Uninstall()
        {
            if (_dispatcher != null)
                _dispatcher.UnhandledException -= OnUnhandled;
            _dispatcher = null;
            Failed = null;
        }

        private static void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            if (e.Handled || !IsOurs(e.Exception))
                return;
            e.Handled = true;
            _log?.Error("Необработанная ошибка InvAuto", e.Exception);
            try
            {
                Failed?.Invoke(e.Exception);
            }
            catch (Exception ex)
            {
                _log?.Error("Ошибка при восстановлении окна после сбоя", ex);
            }
            MessageBox.Show("Произошла ошибка: " + e.Exception.Message + "\n\nПодробности в журнале: %APPDATA%\\InvAuto\\logs",
                "InvAuto", MessageBoxButton.OK, MessageBoxImage.Warning);
        }

        /// <summary>Ошибка из кода InvAuto: в стеке (или во вложенных ошибках) есть его методы.</summary>
        private static bool IsOurs(Exception exception)
        {
            for (var ex = exception; ex != null; ex = ex.InnerException)
                if ((ex.StackTrace ?? string.Empty).IndexOf("InvAuto.", StringComparison.Ordinal) >= 0)
                    return true;
            return false;
        }
    }
}

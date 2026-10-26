using System;
using System.Windows.Forms;
using InvAuto.Core.Logging;
using InvAuto.Core.Settings;

namespace InvAuto.Framework
{
    /// <summary>Всё, что нужно функции для работы: Inventor, лог, настройки, родительское окно для диалогов.</summary>
    public sealed class FeatureContext
    {
        public FeatureContext(Inventor.Application app, ILogger log, XmlSettingsStore settings, string logFolder = null)
        {
            App = app;
            Log = log;
            Settings = settings;
            LogFolder = logFolder ?? System.IO.Path.Combine(settings.Folder, "logs");
        }

        public Inventor.Application App { get; }
        public ILogger Log { get; }
        public XmlSettingsStore Settings { get; }

        /// <summary>Папка журнала: %APPDATA%\InvAuto\logs.</summary>
        public string LogFolder { get; }

        /// <summary>Файл журнала, в который идёт запись сегодня.</summary>
        public string CurrentLogFile => System.IO.Path.Combine(LogFolder, DateTime.Now.ToString("yyyy-MM-dd") + ".log");

        /// <summary>Главное окно Inventor — передавайте в ShowDialog, чтобы диалог не уходил под Inventor.</summary>
        public IWin32Window MainWindow => new WindowHandle(new IntPtr(App.MainFrameHWND));

        public void ShowInfo(string text) =>
            MessageBox.Show(MainWindow, text, "InvAuto", MessageBoxButtons.OK, MessageBoxIcon.Information);

        public void ShowWarning(string text) =>
            MessageBox.Show(MainWindow, text, "InvAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);

        public void ShowError(string text) =>
            MessageBox.Show(MainWindow, text, "InvAuto", MessageBoxButtons.OK, MessageBoxIcon.Error);

        private sealed class WindowHandle : IWin32Window
        {
            public WindowHandle(IntPtr handle) => Handle = handle;
            public IntPtr Handle { get; }
        }
    }
}

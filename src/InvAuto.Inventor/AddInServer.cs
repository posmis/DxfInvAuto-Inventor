using System;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using Inventor;
using InvAuto.Core.Logging;
using InvAuto.Core.Settings;
using InvAuto.Framework;

namespace InvAuto
{
    /// <summary>
    /// Точка входа плагина. Сам ничего не делает: находит все функции (IFeature)
    /// и отдаёт их RibbonBuilder, который создаёт кнопки.
    /// GUID должен совпадать с ClassId в InvAuto.addin.
    /// </summary>
    [Guid(ClientId)]
    [ComVisible(true)]
    public class AddInServer : ApplicationAddInServer
    {
        public const string ClientId = "8BE9472C-C545-4BD1-97D0-211623E9F8B1";

        private RibbonBuilder _ribbon;
        private ILogger _log;

        public void Activate(ApplicationAddInSite addInSiteObject, bool firstTime)
        {
            string dataFolder = System.IO.Path.Combine(
                System.Environment.GetFolderPath(System.Environment.SpecialFolder.ApplicationData), "InvAuto");
            var logger = new FileLogger(System.IO.Path.Combine(dataFolder, "logs"));
            _log = logger;
            ErrorGuard.Install(_log);

            try
            {
                var settings = new XmlSettingsStore(dataFolder, _log);
                var appSettings = settings.Load<AppSettings>(AppSettings.Key);
                appSettings.Normalize();
                logger.Cleanup(TimeSpan.FromDays(appSettings.LogMaxAgeDays));

                var app = addInSiteObject.Application;
                var context = new FeatureContext(app, _log, settings, logger.Folder);

                var features = new FeatureRegistry(_log).Discover(PluginFolder());
                _ribbon = new RibbonBuilder(context, "{" + ClientId + "}");
                _ribbon.Build(features);

                _log.Info($"InvAuto загружен, функций: {features.Count}");
            }
            catch (Exception ex)
            {
                _log.Error("Ошибка при активации InvAuto", ex);
                MessageBox.Show("Ошибка при загрузке InvAuto: " + ex.Message, "InvAuto",
                    MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        public void Deactivate()
        {
            try
            {
                _ribbon?.Dispose();
            }
            catch (Exception ex)
            {
                _log?.Error("Ошибка при выгрузке InvAuto", ex);
            }

            _ribbon = null;
            ErrorGuard.Uninstall();
            GC.Collect();
            GC.WaitForPendingFinalizers();
        }

        public void ExecuteCommand(int commandID)
        {
            // Устаревший механизм Inventor, не используется.
        }

        public object Automation => null;

        private static string PluginFolder()
        {
            string dir = System.IO.Path.GetDirectoryName(typeof(AddInServer).Assembly.Location) ?? string.Empty;
            return System.IO.Path.Combine(dir, "Plugins");
        }
    }
}

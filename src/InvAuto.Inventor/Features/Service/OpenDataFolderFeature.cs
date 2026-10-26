using System.Diagnostics;
using System.IO;
using InvAuto.Framework;

namespace InvAuto.Features.Service
{
    /// <summary>
    /// Открывает папку с настройками и логами плагина.
    /// Заодно — минимальный пример функции: скопируйте этот файл, чтобы начать новую.
    /// </summary>
    public sealed class OpenDataFolderFeature : FeatureBase
    {
        public override string Id => "OpenDataFolder";
        public override string DisplayName => "Логи и настройки";
        public override string Description => "Открыть папку с настройками и логами InvAuto";
        public override string Panel => "Сервис";
        public override RibbonTargets Targets => RibbonTargets.All;
        public override int Order => 900;

        public override void Execute(FeatureContext context)
        {
            string folder = context.Settings.Folder;
            Directory.CreateDirectory(folder);
            Process.Start("explorer.exe", "\"" + folder + "\"");
        }
    }
}

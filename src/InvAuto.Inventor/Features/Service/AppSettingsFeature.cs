using InvAuto.Framework;
using InvAuto.Framework.Ui;

namespace InvAuto.Features.Service
{
    /// <summary>
    /// «Настройки InvAuto»: кэш разверток, журнал, сброс настроек, сведения о программе.
    /// Заодно — минимальный пример функции: скопируйте этот файл, чтобы начать новую.
    /// </summary>
    public sealed class AppSettingsFeature : FeatureBase
    {
        public override string Id => "AppSettings";
        public override string DisplayName => "Настройки InvAuto";
        public override string Description => "Кэш разверток, журнал, сброс настроек";
        public override bool LargeButton => false;
        public override RibbonTargets Targets => RibbonTargets.Part | RibbonTargets.Assembly | RibbonTargets.Drawing;
        public override int Order => 900;

        public override void Execute(FeatureContext context) =>
            new AppSettingsWindow(context).ShowDialogOver(context);
    }
}

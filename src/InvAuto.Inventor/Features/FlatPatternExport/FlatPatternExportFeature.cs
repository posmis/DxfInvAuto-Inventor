using Inventor;
using InvAuto.Core.Export;
using InvAuto.Framework;
using InvAuto.InventorApi;
using IO = System.IO;

namespace InvAuto.Features.FlatPatternExport
{
    /// <summary>
    /// Пакетный экспорт разверток листовых деталей активной сборки (или одной детали) в DXF.
    /// Собирает детали и открывает окно, где пользователь выбирает, что и как экспортировать.
    /// </summary>
    public sealed class FlatPatternExportFeature : FeatureBase
    {
        public override string Id => "FlatPatternExport";
        public override string DisplayName => "Развертки в DXF";
        public override string Description => "Экспорт разверток листовых деталей сборки в DXF";
        public override string Panel => "Листовой металл";
        public override int Order => 10;

        public override void Execute(FeatureContext context)
        {
            Document document = context.App.ActiveDocument;
            if (document == null ||
                (document.DocumentType != DocumentTypeEnum.kAssemblyDocumentObject &&
                 document.DocumentType != DocumentTypeEnum.kPartDocumentObject))
            {
                context.ShowWarning("Откройте сборку или листовую деталь.");
                return;
            }

            var parts = SheetMetalCollector.Collect(document);
            if (parts.Count == 0)
            {
                context.ShowInfo("Листовые детали не найдены.");
                return;
            }

            var settings = context.Settings.Load<FlatPatternExportSettings>(Id);
            string assemblyName = IO.Path.GetFileNameWithoutExtension(document.FullFileName);

            using (var window = new FlatPatternExportWindow(context, Id, parts, settings, DefaultOutputFolder(document), assemblyName))
                window.ShowDialog(context.MainWindow);
        }

        private static string DefaultOutputFolder(Document document)
        {
            string dir = string.IsNullOrEmpty(document.FullFileName)
                ? null
                : IO.Path.GetDirectoryName(document.FullFileName);
            if (string.IsNullOrEmpty(dir))
                dir = System.Environment.GetFolderPath(System.Environment.SpecialFolder.DesktopDirectory);
            return IO.Path.Combine(dir, "DXF");
        }
    }
}

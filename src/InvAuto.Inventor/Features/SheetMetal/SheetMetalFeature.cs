using Inventor;
using InvAuto.Framework;
using InvAuto.Framework.Ui;
using InvAuto.InventorApi;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>
    /// "Листовые детали": дерево состава активной сборки и список её листовых деталей
    /// с превью разверток и выгрузкой в DXF — всех или только выбранной подсборки.
    /// </summary>
    public sealed class SheetMetalFeature : FeatureBase
    {
        public override string Id => "SheetMetal";
        public override string DisplayName => "Листовые детали";
        public override string Description => "Список листовых деталей сборки с превью разверток и выгрузкой в DXF";
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

            AssemblyStructure structure;
            try
            {
                System.Windows.Input.Mouse.OverrideCursor = System.Windows.Input.Cursors.Wait;
                structure = AssemblyScanner.Scan(document);
            }
            finally
            {
                System.Windows.Input.Mouse.OverrideCursor = null;
            }

            new SheetMetalWindow(context, document, structure).ShowDialogOver(context);
        }
    }
}

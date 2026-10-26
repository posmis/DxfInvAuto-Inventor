namespace InvAuto.Core.Export
{
    /// <summary>Настройки экспорта разверток. Сохраняются между запусками.</summary>
    public sealed class FlatPatternExportSettings
    {
        /// <summary>Папка экспорта. Пусто — папка "DXF" рядом с активным документом.</summary>
        public string OutputFolder { get; set; } = string.Empty;

        /// <summary>Шаблон имени файла без расширения.</summary>
        public string FileNameTemplate { get; set; } = "{Name}-t{Thickness:0.0}мм-{Qty}шт";

        /// <summary>Шаблон подпапок, например "{Material}\{Thickness:0.0}мм". Пусто — без подпапок.</summary>
        public string SubfolderTemplate { get; set; } = string.Empty;

        /// <summary>Количество изделий: количество каждой детали умножается на это число.</summary>
        public int QuantityMultiplier { get; set; } = 1;

        public string AcadVersion { get; set; } = "2000";

        public string OuterProfileLayer { get; set; } = "OUTER";
        public string InteriorProfilesLayer { get; set; } = "INNER";
        public string BendUpLayer { get; set; } = "BEND_UP";
        public string BendDownLayer { get; set; } = "BEND_DOWN";

        public bool ExportBendLines { get; set; } = true;
        public bool ExportTangentLines { get; set; }
        public bool ExportArcCenters { get; set; }
        public bool MergeProfilesIntoPolyline { get; set; } = true;
        public bool RebaseGeometry { get; set; } = true;

        /// <summary>Создавать развертку, если её нет (деталь будет изменена и потребует сохранения).</summary>
        public bool CreateMissingFlatPatterns { get; set; } = true;

        public bool OverwriteExisting { get; set; } = true;
        public bool WriteCsvReport { get; set; } = true;
        public bool OpenFolderAfterExport { get; set; } = true;
    }
}

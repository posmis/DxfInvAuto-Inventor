namespace InvAuto.Core.Export
{
    public enum DxfExportMethod
    {
        /// <summary>DXF-транслятор Inventor с ini-файлом настроек (как "Сохранить копию как" → DXF).</summary>
        IniFile,

        /// <summary>SheetMetalComponentDefinition.DataIO со строкой параметров из настроек ниже.</summary>
        FormatString
    }

    /// <summary>Настройки экспорта разверток. Сохраняются между запусками.</summary>
    public sealed class FlatPatternExportSettings
    {
        /// <summary>Папка экспорта. Пусто — папка "DXF" рядом с активным документом.</summary>
        public string OutputFolder { get; set; } = string.Empty;

        /// <summary>Шаблон имени файла без расширения.</summary>
        public string FileNameTemplate { get; set; } = "{Name}-t{Thickness:0.0}мм-{Qty}шт";

        /// <summary>Шаблон подпапок, например "{Material}\{Thickness:0.0}мм". Пусто — без подпапок.</summary>
        public string SubfolderTemplate { get; set; } = string.Empty;

        /// <summary>Раскладывать файлы по папкам толщины («3.0мм», «1.5мм») — перед подпапками шаблона.</summary>
        public bool SortByThickness { get; set; }

        /// <summary>Количество изделий: количество каждой детали умножается на это число.</summary>
        public int QuantityMultiplier { get; set; } = 1;

        public DxfExportMethod Method { get; set; } = DxfExportMethod.IniFile;

        /// <summary>Путь к ini-файлу транслятора. Пусто — dxf_export_config.ini, поставляемый с плагином.</summary>
        public string IniFilePath { get; set; } = string.Empty;

        // Параметры ниже используются только при Method == FormatString.
        // Значения по умолчанию повторяют поставляемый ini: DXF 2004, только контуры и линии гиба,
        // каждый в своём слое Inventor (IV_OUTER_PROFILE, IV_INTERIOR_PROFILES, IV_BEND, IV_BEND_DOWN).

        public string AcadVersion { get; set; } = "2004";

        /// <summary>Переименование слоёв. Пусто — оставить имя слоя Inventor.</summary>
        public string OuterProfileLayer { get; set; } = string.Empty;
        public string InteriorProfilesLayer { get; set; } = string.Empty;
        public string BendUpLayer { get; set; } = string.Empty;
        public string BendDownLayer { get; set; } = string.Empty;

        public bool ExportBendLines { get; set; } = true;
        public bool MergeProfilesIntoPolyline { get; set; }
        public bool RebaseGeometry { get; set; }

        /// <summary>Создавать развертку, если её нет (деталь будет изменена и потребует сохранения).</summary>
        public bool CreateMissingFlatPatterns { get; set; } = true;

        public bool OverwriteExisting { get; set; } = true;
        public bool WriteCsvReport { get; set; } = true;
        public bool OpenFolderAfterExport { get; set; } = true;
    }
}

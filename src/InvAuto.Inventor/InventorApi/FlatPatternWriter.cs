using System;
using Inventor;
using InvAuto.Core.Export;
using IO = System.IO;

namespace InvAuto.InventorApi
{
    /// <summary>Записывает развертку в DXF-файл. Одним и тем же объектом пользуются выгрузка и превью.</summary>
    public interface IFlatPatternWriter
    {
        void Write(SheetMetalComponentDefinition definition, string path);
    }

    public static class FlatPatternWriters
    {
        public static IFlatPatternWriter Create(Application app, FlatPatternExportSettings settings) =>
            settings.Method == DxfExportMethod.IniFile
                ? (IFlatPatternWriter)new TranslatorIniWriter(app, ResolveIniPath(settings))
                : new DataIoWriter(DxfFormatString.Build(settings));

        /// <summary>ini из настроек или поставляемый с плагином Config\dxf_export_config.ini.</summary>
        public static string ResolveIniPath(FlatPatternExportSettings settings) =>
            string.IsNullOrWhiteSpace(settings.IniFilePath) ? BundledIniPath : settings.IniFilePath.Trim();

        public static string BundledIniPath => IO.Path.Combine(
            IO.Path.GetDirectoryName(typeof(FlatPatternWriters).Assembly.Location) ?? string.Empty,
            "Config", "dxf_export_config.ini");
    }

    /// <summary>
    /// DXF-транслятор Inventor с ini-файлом — тот же механизм, что "Сохранить копию как" → DXF
    /// для развертки. Слои, цвета, версия DXF и упрощение сплайнов берутся из ini.
    /// </summary>
    public sealed class TranslatorIniWriter : IFlatPatternWriter
    {
        private const string DxfTranslatorId = "{C24E3AC4-122E-11D5-8E91-0010B541CD80}";

        private readonly Application _app;
        private readonly TranslatorAddIn _translator;
        private readonly string _iniPath;

        public TranslatorIniWriter(Application app, string iniPath)
        {
            if (!IO.File.Exists(iniPath))
                throw new IO.FileNotFoundException("Не найден ini-файл экспорта DXF: " + iniPath, iniPath);

            _app = app;
            _iniPath = iniPath;
            _translator = app.ApplicationAddIns.ItemById[DxfTranslatorId] as TranslatorAddIn
                ?? throw new InvalidOperationException("DXF-транслятор Inventor не найден.");
            if (!_translator.Activated)
                _translator.Activate();
        }

        public void Write(SheetMetalComponentDefinition definition, string path)
        {
            TranslationContext context = _app.TransientObjects.CreateTranslationContext();
            context.Type = IOMechanismEnum.kFileBrowseIOMechanism;

            NameValueMap options = _app.TransientObjects.CreateNameValueMap();
            options.Value["Export_Acad_IniFile"] = _iniPath;

            DataMedium medium = _app.TransientObjects.CreateDataMedium();
            medium.FileName = path;

            _translator.SaveCopyAs(definition.FlatPattern, context, options, medium);
        }
    }

    /// <summary>Запасной способ: DataIO со строкой параметров, собранной из настроек.</summary>
    public sealed class DataIoWriter : IFlatPatternWriter
    {
        private readonly string _format;

        public DataIoWriter(string format) => _format = format;

        public void Write(SheetMetalComponentDefinition definition, string path) =>
            definition.DataIO.WriteDataToFile(_format, path);
    }
}

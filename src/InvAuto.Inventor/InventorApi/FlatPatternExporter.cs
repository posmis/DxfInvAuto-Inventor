using System;
using System.Collections.Generic;
using Inventor;
using InvAuto.Core.Export;
using InvAuto.Core.Naming;
using IO = System.IO;

namespace InvAuto.InventorApi
{
    /// <summary>Экспортирует развертки набора листовых деталей в DXF.</summary>
    public sealed class FlatPatternExporter
    {
        private readonly FlatPatternExportSettings _settings;
        private readonly string _outputFolder;
        private readonly string _assemblyName;
        private readonly string _formatString;
        private readonly HashSet<string> _usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FlatPatternExporter(FlatPatternExportSettings settings, string outputFolder, string assemblyName)
        {
            _settings = settings;
            _outputFolder = outputFolder;
            _assemblyName = assemblyName;
            _formatString = DxfFormatString.Build(settings);
        }

        public ExportResult Export(CollectedPart part)
        {
            var info = part.Info;
            string path = string.Empty;
            try
            {
                path = BuildPath(info);
                var definition = (SheetMetalComponentDefinition)part.Document.ComponentDefinition;

                string note = string.Empty;
                if (!definition.HasFlatPattern)
                {
                    if (!_settings.CreateMissingFlatPatterns)
                        return new ExportResult(info, ExportStatus.Skipped, path, "Нет развертки");

                    definition.Unfold();
                    definition.FlatPattern.ExitEdit();
                    note = "Развертка создана автоматически — сохраните деталь";
                }

                if (IO.File.Exists(path) && !_settings.OverwriteExisting)
                    return new ExportResult(info, ExportStatus.Skipped, path, "Файл уже существует");

                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(path));
                definition.DataIO.WriteDataToFile(_formatString, path);

                return new ExportResult(info, ExportStatus.Exported, path, note);
            }
            catch (Exception ex)
            {
                return new ExportResult(info, ExportStatus.Failed, path, ex.Message);
            }
        }

        private string BuildPath(SheetMetalPart info)
        {
            var values = info.ToTemplateValues(_settings.QuantityMultiplier, _assemblyName);

            string folder = _outputFolder;
            string subfolder = FileNameTemplate.SanitizeRelativePath(
                FileNameTemplate.Render(_settings.SubfolderTemplate, values));
            if (subfolder.Length > 0)
                folder = IO.Path.Combine(folder, subfolder);

            string name = FileNameTemplate.SanitizeFileName(FileNameTemplate.Render(_settings.FileNameTemplate, values));

            // Разные детали могут дать одинаковое имя по шаблону — не затираем друг друга.
            string path = IO.Path.Combine(folder, name + ".dxf");
            for (int i = 2; !_usedPaths.Add(path); i++)
                path = IO.Path.Combine(folder, $"{name} ({i}).dxf");

            return path;
        }
    }
}

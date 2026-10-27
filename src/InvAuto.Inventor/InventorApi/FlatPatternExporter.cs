using System;
using System.Collections.Generic;
using Inventor;
using InvAuto.Core.Bends;
using InvAuto.Core.Export;
using InvAuto.Core.Flat;
using InvAuto.Core.Marking;
using IO = System.IO;

namespace InvAuto.InventorApi
{
    /// <summary>Экспортирует развертки набора листовых деталей в DXF.</summary>
    public sealed class FlatPatternExporter
    {
        private readonly FlatPatternExportSettings _settings;
        private readonly string _outputFolder;
        private readonly string _assemblyName;
        private readonly IFlatPatternWriter _writer;
        private readonly HashSet<string> _usedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public FlatPatternExporter(FlatPatternExportSettings settings, string outputFolder, string assemblyName, IFlatPatternWriter writer)
        {
            _settings = settings;
            _outputFolder = outputFolder;
            _assemblyName = assemblyName;
            _writer = writer;
        }

        /// <summary>
        /// Параметры линий гиба детали: функция «линия → параметры» (с учётом настроек линии, детали и общих).
        /// null — линии гиба не обрабатываются.
        /// </summary>
        public Func<SheetMetalPart, Func<BendLineInfo, BendLineOptions>> BendOptions { get; set; }

        /// <summary>Маркировка детали (null — без маркировки). Текст — обозначение детали.</summary>
        public Func<SheetMetalPart, MarkingOptions> Marking { get; set; }

        /// <summary>Место маркировки, заданное вручную (null — автоматически).</summary>
        public Func<SheetMetalPart, MarkingPosition> MarkingPosition { get; set; }

        /// <summary>Поворот развертки детали, градусы (null — без поворота).</summary>
        public Func<SheetMetalPart, double> Rotation { get; set; }

        /// <summary>Слои, отрезки на которых считаются линиями гиба.</summary>
        public IReadOnlyCollection<string> BendLayers { get; set; } = DxfBendProcessor.DefaultLayers;

        /// <summary>Вызывается с путём к DXF, только что записанному транслятором (до обработки линий гиба).</summary>
        public Action<CollectedPart, string> RawDxfWritten { get; set; }

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
                        return new ExportResult(info, ExportStatus.Skipped, path, "нет развертки");

                    definition.Unfold();
                    definition.FlatPattern.ExitEdit();
                    info.HasFlatPattern = true;
                    note = "развертка создана автоматически, сохраните деталь";
                }

                if (IO.File.Exists(path) && !_settings.OverwriteExisting)
                    return new ExportResult(info, ExportStatus.Skipped, path, "файл уже существует");

                IO.Directory.CreateDirectory(IO.Path.GetDirectoryName(path));
                string raw = path + ".raw.dxf";
                try
                {
                    _writer.Write(definition, raw);
                    RawDxfWritten?.Invoke(part, raw);
                    var (_, marked) = DxfOutput.WriteFinal(raw, path, new FlatRequest
                    {
                        Bends = BendOptions?.Invoke(info),
                        BendLayers = BendLayers,
                        Rotation = Rotation?.Invoke(info) ?? 0,
                        Marking = Marking?.Invoke(info),
                        MarkingText = info.PartNumber,
                        MarkingPosition = MarkingPosition?.Invoke(info)
                    });
                    var marking = marked.Status;
                    if (marked.Conflict)
                        note = Join(note, "маркировка задевает контур");
                    else if (marking == MarkingStatus.NoRoom)
                        note = Join(note, "маркировка не помещается");
                    else if (marking == MarkingStatus.NoText)
                        note = Join(note, "нет обозначения для маркировки");
                }
                finally
                {
                    if (IO.File.Exists(raw))
                        IO.File.Delete(raw);
                }

                return new ExportResult(info, ExportStatus.Exported, path, note);
            }
            catch (Exception ex)
            {
                return new ExportResult(info, ExportStatus.Failed, path, ex.Message);
            }
        }

        private static string Join(string a, string b) => a.Length == 0 ? b : a + "; " + b;

        private string BuildPath(SheetMetalPart info)
        {
            var values = info.ToTemplateValues(_settings.QuantityMultiplier, _assemblyName);
            string folder = IO.Path.Combine(_outputFolder, ExportNaming.RelativeFolder(_settings, values));
            string name = ExportNaming.FileName(_settings, values);

            return ExportNaming.UniquePath(folder, name, _usedPaths);
        }
    }
}

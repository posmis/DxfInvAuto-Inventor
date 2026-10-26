using System;
using System.Collections.Generic;
using System.IO;
using InvAuto.Core.Bends;
using InvAuto.Core.Marking;

namespace InvAuto.Core.Export
{
    /// <summary>Итоговый DXF выгрузки: «сырой» файл транслятора с обработанными линиями гиба.</summary>
    public static class DxfOutput
    {
        /// <summary>Одни параметры для всех линий гиба.</summary>
        public static IReadOnlyList<ProcessedBendLine> WriteFinal(string rawPath, string finalPath, BendLineOptions bends,
            IEnumerable<string> bendLayers) =>
            WriteFinal(rawPath, finalPath, bends == null ? null : (Func<BendLineInfo, BendLineOptions>)(_ => bends), bendLayers);

        /// <summary>
        /// Записывает итоговый файл с параметрами для каждой линии гиба. Если менять нечего (null или все линии
        /// «как есть»), сырой файл переносится байт в байт. Существующий итоговый файл заменяется.
        /// Возвращает найденные линии гиба.
        /// </summary>
        public static IReadOnlyList<ProcessedBendLine> WriteFinal(string rawPath, string finalPath,
            Func<BendLineInfo, BendLineOptions> resolve, IEnumerable<string> bendLayers) =>
            WriteFinal(rawPath, finalPath, resolve, bendLayers, null, null).Lines;

        /// <summary>С маркировкой (автоматическое место).</summary>
        public static (IReadOnlyList<ProcessedBendLine> Lines, MarkingStatus Marking) WriteFinal(string rawPath, string finalPath,
            Func<BendLineInfo, BendLineOptions> resolve, IEnumerable<string> bendLayers, MarkingOptions marking, string markingText)
        {
            var (lines, result) = WriteFinal(rawPath, finalPath, resolve, bendLayers, marking, markingText, null);
            return (lines, result.Status);
        }

        /// <summary>
        /// То же с маркировкой: после обработки линий гиба добавляется текст markingText (обозначение детали),
        /// если маркировка включена и текст помещается; position — место, заданное вручную (null — автоматически).
        /// </summary>
        public static (IReadOnlyList<ProcessedBendLine> Lines, MarkingResult Marking) WriteFinal(string rawPath, string finalPath,
            Func<BendLineInfo, BendLineOptions> resolve, IEnumerable<string> bendLayers, MarkingOptions marking, string markingText,
            MarkingPosition position)
        {
            if (File.Exists(finalPath))
                File.Delete(finalPath);

            bool mark = marking != null && marking.Enabled;
            if (resolve == null && !mark)
            {
                File.Move(rawPath, finalPath);
                return (new ProcessedBendLine[0], new MarkingResult(MarkingStatus.Off, null));
            }

            string text = File.ReadAllText(rawPath, DxfBendProcessor.FileEncoding);
            var processed = resolve == null
                ? new BendProcessResult(text, new ProcessedBendLine[0])
                : DxfBendProcessor.Process(text, bendLayers, resolve);
            var marked = DxfMarking.Apply(processed.Text, markingText, marking, null, position);
            if (ReferenceEquals(marked.Dxf, text))
                File.Move(rawPath, finalPath);
            else
                File.WriteAllText(finalPath, marked.Dxf, DxfBendProcessor.FileEncoding);
            return (processed.Items, marked);
        }
    }
}

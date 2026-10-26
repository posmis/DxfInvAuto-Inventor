using System;
using System.Collections.Generic;
using System.IO;
using InvAuto.Core.Bends;

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
            Func<BendLineInfo, BendLineOptions> resolve, IEnumerable<string> bendLayers)
        {
            if (File.Exists(finalPath))
                File.Delete(finalPath);

            if (resolve == null)
            {
                File.Move(rawPath, finalPath);
                return new ProcessedBendLine[0];
            }

            string text = File.ReadAllText(rawPath, DxfBendProcessor.FileEncoding);
            var processed = DxfBendProcessor.Process(text, bendLayers, resolve);
            if (ReferenceEquals(processed.Text, text))
                File.Move(rawPath, finalPath);
            else
                File.WriteAllText(finalPath, processed.Text, DxfBendProcessor.FileEncoding);
            return processed.Items;
        }
    }
}

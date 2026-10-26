using System.Collections.Generic;
using System.IO;
using InvAuto.Core.Bends;

namespace InvAuto.Core.Export
{
    /// <summary>Итоговый DXF выгрузки: «сырой» файл транслятора с обработанными линиями гиба.</summary>
    public static class DxfOutput
    {
        /// <summary>
        /// Записывает итоговый файл. Без обработки линий гиба сырой файл просто переносится — байт в байт.
        /// Существующий итоговый файл заменяется. Возвращает найденные линии гиба (пусто, если обработки не было).
        /// </summary>
        public static IReadOnlyList<BendLineLayout> WriteFinal(string rawPath, string finalPath, BendLineOptions bends,
            IEnumerable<string> bendLayers)
        {
            if (File.Exists(finalPath))
                File.Delete(finalPath);

            if (bends == null || bends.Mode == BendLineMode.Keep)
            {
                File.Move(rawPath, finalPath);
                return new BendLineLayout[0];
            }

            string text = File.ReadAllText(rawPath, DxfBendProcessor.FileEncoding);
            var processed = DxfBendProcessor.Process(text, bendLayers, bends);
            File.WriteAllText(finalPath, processed.Text, DxfBendProcessor.FileEncoding);
            return processed.Lines;
        }
    }
}

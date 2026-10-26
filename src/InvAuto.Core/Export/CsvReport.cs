using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;

namespace InvAuto.Core.Export
{
    /// <summary>
    /// CSV-отчёт об экспорте. Разделитель ";" и UTF-8 с BOM — так файл корректно
    /// открывается двойным щелчком в русском Excel.
    /// </summary>
    public static class CsvReport
    {
        public static void Write(string path, IEnumerable<ExportResult> results, int quantityMultiplier)
        {
            var sb = new StringBuilder();
            sb.AppendLine("Имя;Обозначение;Наименование;Материал;Толщина, мм;Кол-во;Статус;Файл;Сообщение");

            foreach (var r in results.OrderBy(r => r.Part.Material).ThenBy(r => r.Part.ThicknessMm).ThenBy(r => r.Part.Name))
            {
                var p = r.Part;
                sb.AppendLine(string.Join(";", new[]
                {
                    Escape(p.Name),
                    Escape(p.PartNumber),
                    Escape(p.Description),
                    Escape(p.Material),
                    p.ThicknessMm.ToString("0.###", CultureInfo.GetCultureInfo("ru-RU")),
                    (p.Quantity * System.Math.Max(1, quantityMultiplier)).ToString(CultureInfo.InvariantCulture),
                    StatusText(r.Status),
                    Escape(r.FilePath),
                    Escape(r.Message)
                }));
            }

            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        public static string StatusText(ExportStatus status)
        {
            switch (status)
            {
                case ExportStatus.Exported: return "OK";
                case ExportStatus.Skipped: return "Пропущено";
                default: return "Ошибка";
            }
        }

        internal static string Escape(string value)
        {
            if (string.IsNullOrEmpty(value))
                return string.Empty;
            if (value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) < 0)
                return value;
            return "\"" + value.Replace("\"", "\"\"") + "\"";
        }
    }
}

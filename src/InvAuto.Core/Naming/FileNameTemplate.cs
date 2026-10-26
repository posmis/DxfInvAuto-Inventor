using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace InvAuto.Core.Naming
{
    /// <summary>
    /// Шаблон имени вида "{Name}-t{Thickness:0.0}мм-{Qty}шт".
    /// Имена токенов нечувствительны к регистру, после двоеточия можно указать формат .NET.
    /// Неизвестный токен остаётся в имени как есть, чтобы опечатка в шаблоне была сразу видна.
    /// </summary>
    public static class FileNameTemplate
    {
        private static readonly Regex TokenRegex =
            new Regex(@"\{(?<name>[^{}:]+)(:(?<format>[^{}]+))?\}", RegexOptions.Compiled);

        public static string Render(string template, IReadOnlyDictionary<string, object> values)
        {
            if (string.IsNullOrEmpty(template))
                return string.Empty;

            var lookup = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in values)
                lookup[pair.Key] = pair.Value;

            return TokenRegex.Replace(template, match =>
            {
                string name = match.Groups["name"].Value.Trim();
                if (!lookup.TryGetValue(name, out object value))
                    return match.Value;

                string format = match.Groups["format"].Success ? match.Groups["format"].Value : null;
                return FormatValue(value, format);
            });
        }

        private static string FormatValue(object value, string format)
        {
            if (value == null)
                return string.Empty;
            if (format != null && value is IFormattable formattable)
                return formattable.ToString(format, CultureInfo.InvariantCulture);
            return Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        // Правила Windows независимо от ОС, на которой выполняется код (тесты идут и на Linux).
        private static readonly HashSet<char> InvalidChars =
            new HashSet<char>("<>:\"/\\|?*".Concat(Enumerable.Range(0, 32).Select(c => (char)c)));

        private static readonly HashSet<string> ReservedNames = new HashSet<string>(
            new[] { "CON", "PRN", "AUX", "NUL" }
                .Concat(Enumerable.Range(1, 9).Select(i => "COM" + i))
                .Concat(Enumerable.Range(1, 9).Select(i => "LPT" + i)),
            StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Безопасное имя файла для Windows: недопустимые символы → "_", без пробелов и точек в конце,
        /// зарезервированные имена (CON, NUL, COM1…) получают "_" в начале.
        /// </summary>
        public static string SanitizeFileName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return "_";

            var sb = new StringBuilder(name.Length);
            foreach (char c in name)
                sb.Append(InvalidChars.Contains(c) ? '_' : c);

            // Windows не любит пробелы и точки в конце имени.
            string result = sb.ToString().Trim().TrimEnd('.').TrimEnd();
            if (result.Length == 0)
                return "_";

            int dot = result.IndexOf('.');
            string stem = dot < 0 ? result : result.Substring(0, dot);
            return ReservedNames.Contains(stem) ? "_" + result : result;
        }

        /// <summary>
        /// Превращает отрендеренный шаблон подпапок ("Сталь\3.0мм") в безопасный относительный путь.
        /// Пустые сегменты и попытки выйти наверх ("..") отбрасываются.
        /// </summary>
        public static string SanitizeRelativePath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
                return string.Empty;

            var segments = path
                .Split(new[] { '\\', '/' }, StringSplitOptions.RemoveEmptyEntries)
                .Select(s => s.Trim())
                .Where(s => s.Length > 0 && s != "." && s != "..")
                .Select(SanitizeFileName);

            return string.Join(Path.DirectorySeparatorChar.ToString(), segments);
        }
    }
}

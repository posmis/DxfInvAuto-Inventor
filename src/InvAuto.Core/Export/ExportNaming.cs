using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text.RegularExpressions;
using InvAuto.Core.Naming;

namespace InvAuto.Core.Export
{
    /// <summary>Имена и папки выгружаемых DXF — общие правила для выгрузки и для примера в настройках.</summary>
    public static class ExportNaming
    {
        // Серия пробелов вместе с соседними дефисами: «Деталь - 1» → «Деталь-1», а не «Деталь---1».
        private static readonly Regex SpaceRun = new Regex(@"[\s-]*\s[\s-]*", RegexOptions.Compiled);

        /// <summary>Имя файла без расширения: шаблон, недопустимые символы → «_», пробелы → «-».</summary>
        public static string FileName(FlatPatternExportSettings settings, IReadOnlyDictionary<string, object> values) =>
            NoSpaces(FileNameTemplate.SanitizeFileName(FileNameTemplate.Render(settings.FileNameTemplate, values)));

        /// <summary>Подпапка относительно папки выгрузки: сначала толщина (если включено), затем шаблон подпапок.</summary>
        public static string RelativeFolder(FlatPatternExportSettings settings, IReadOnlyDictionary<string, object> values)
        {
            string folder = FileNameTemplate.SanitizeRelativePath(FileNameTemplate.Render(settings.SubfolderTemplate, values));
            if (!settings.SortByThickness)
                return folder;

            double thickness = values.TryGetValue("Thickness", out object t) && t is double d ? d : 0;
            string byThickness = ThicknessFolder(thickness);
            return folder.Length > 0 ? Path.Combine(byThickness, folder) : byThickness;
        }

        /// <summary>Папка для толщины: «3.0мм», «1.25мм».</summary>
        public static string ThicknessFolder(double thicknessMm) =>
            thicknessMm.ToString("0.0#", CultureInfo.InvariantCulture) + "мм";

        public static string NoSpaces(string name) => SpaceRun.Replace(name, "-");

        /// <summary>
        /// Путь к DXF, ещё не занятый в этой выгрузке: разные детали могут дать одно имя по шаблону —
        /// тогда добавляется «_2», «_3»… Сравнение без учёта регистра, как в Windows.
        /// </summary>
        public static string UniquePath(string folder, string name, ISet<string> usedPaths)
        {
            string path = Path.Combine(folder, name + ".dxf");
            for (int i = 2; !usedPaths.Add(path); i++)
                path = Path.Combine(folder, $"{name}_{i}.dxf");
            return path;
        }
    }
}

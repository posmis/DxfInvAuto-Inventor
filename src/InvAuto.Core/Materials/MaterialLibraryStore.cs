using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Serialization;
using InvAuto.Core.Logging;

namespace InvAuto.Core.Materials
{
    /// <summary>Файл библиотеки есть, но прочитать его нельзя. Перезаписывать такой файл нельзя — потеряем данные.</summary>
    public sealed class MaterialLibraryException : Exception
    {
        public MaterialLibraryException(string message, Exception inner = null) : base(message, inner)
        {
        }
    }

    /// <summary>
    /// Хранение библиотеки материалов: %APPDATA%\InvAuto\library\materials.xml — отдельно от настроек
    /// («Сброс настроек» её не трогает) и от кэша (очистка кэша её не трогает). Автоматически не очищается.
    /// — Первый запуск (файла нет): создаётся стартовая библиотека.
    /// — Файл повреждён: исключение, файл не перезаписывается.
    /// — Каждое сохранение кладёт прежнюю версию в library\backup (хранятся последние MaxBackups копий).
    /// Экспорт и импорт: XML (полная копия) и CSV для Excel (разделитель «;», UTF-8 с BOM).
    /// </summary>
    public sealed class MaterialLibraryStore
    {
        public const int MaxBackups = 50;
        private const string CsvHeader = "Наименование;Толщина, мм;Плотность, кг/м³;По умолчанию";

        private readonly ILogger _log;

        /// <param name="dataFolder">Папка данных InvAuto (%APPDATA%\InvAuto).</param>
        public MaterialLibraryStore(string dataFolder, ILogger log)
        {
            Folder = Path.Combine(dataFolder, "library");
            _log = log;
        }

        public string Folder { get; }
        public string FilePath => Path.Combine(Folder, "materials.xml");
        public string BackupFolder => Path.Combine(Folder, "backup");

        public MaterialLibrary Load()
        {
            if (!File.Exists(FilePath))
            {
                var starter = MaterialLibrary.CreateStarter();
                Save(starter);
                _log.Info("Создана стартовая библиотека материалов: " + FilePath);
                return starter;
            }

            try
            {
                var library = ReadXml(FilePath);
                library.Normalize();
                return library;
            }
            catch (Exception ex)
            {
                throw new MaterialLibraryException(
                    $"Не удалось прочитать библиотеку материалов «{FilePath}». Файл не изменён; " +
                    "восстановите его из папки backup или исправьте вручную.", ex);
            }
        }

        public void Save(MaterialLibrary library)
        {
            library.Normalize();
            var errors = library.Validate();
            if (errors.Count > 0)
                throw new InvalidOperationException("Библиотека не сохранена:\n" + string.Join("\n", errors));

            Directory.CreateDirectory(Folder);
            if (File.Exists(FilePath))
                Backup();

            string temp = FilePath + ".tmp";
            WriteXml(temp, library);
            if (File.Exists(FilePath))
                File.Delete(FilePath);
            File.Move(temp, FilePath);
        }

        private void Backup()
        {
            Directory.CreateDirectory(BackupFolder);
            string name = $"materials_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}";
            string target = Path.Combine(BackupFolder, name + ".xml");
            for (int i = 2; File.Exists(target); i++)
                target = Path.Combine(BackupFolder, $"{name}_{i}.xml");
            File.Copy(FilePath, target);

            // Только резервные копии этой же библиотеки, самые старые; сама библиотека не удаляется никогда.
            foreach (string old in Directory.GetFiles(BackupFolder, "materials_*.xml")
                         .OrderByDescending(File.GetLastWriteTimeUtc).ThenByDescending(f => f, StringComparer.Ordinal)
                         .Skip(MaxBackups))
            {
                try
                {
                    File.Delete(old);
                }
                catch (Exception ex)
                {
                    _log.Warn($"Не удалось удалить старую копию библиотеки {old}: {ex.Message}");
                }
            }
        }

        #region Экспорт и импорт

        /// <summary>Выгружает библиотеку в файл: .csv — таблица для Excel, иначе XML.</summary>
        public static void Export(MaterialLibrary library, string path)
        {
            if (IsCsv(path))
                File.WriteAllText(path, ToCsv(library), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            else
                WriteXml(path, library);
        }

        /// <summary>Читает библиотеку из файла экспорта (.xml или .csv).</summary>
        public static MaterialLibrary Import(string path)
        {
            var library = IsCsv(path) ? FromCsv(File.ReadAllText(path)) : ReadXml(path);
            foreach (var m in library.Materials)
                m.Name = (m.Name ?? string.Empty).Trim();
            library.Normalize();
            return library;
        }

        public static string ToCsv(MaterialLibrary library)
        {
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            var sb = new StringBuilder();
            sb.AppendLine(CsvHeader);
            foreach (var m in library.Materials.OrderBy(m => m.Thickness).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
            {
                sb.AppendLine(string.Join(";",
                    Escape(m.Name),
                    m.Thickness.ToString("0.###", ru),
                    m.Density.ToString("0.###", ru),
                    m.IsDefault ? "да" : string.Empty));
            }
            return sb.ToString();
        }

        public static MaterialLibrary FromCsv(string text)
        {
            var library = new MaterialLibrary();
            var lines = text.Replace("\r\n", "\n").Split('\n').Where(l => l.Trim().Length > 0).ToList();
            int start = lines.Count > 0 && lines[0].TrimStart('﻿').StartsWith("Наименование", StringComparison.OrdinalIgnoreCase) ? 1 : 0;

            for (int i = start; i < lines.Count; i++)
            {
                var cells = SplitCsv(lines[i]);
                if (cells.Count < 3)
                    throw new FormatException($"Строка {i + 1}: нужно не меньше трёх колонок (наименование; толщина; плотность)");
                library.Materials.Add(new Material
                {
                    Name = cells[0].Trim(),
                    Thickness = ParseNumber(cells[1], i + 1, "толщина"),
                    Density = ParseNumber(cells[2], i + 1, "плотность"),
                    IsDefault = cells.Count > 3 && IsYes(cells[3])
                });
            }
            return library;
        }

        private static bool IsCsv(string path) => string.Equals(Path.GetExtension(path), ".csv", StringComparison.OrdinalIgnoreCase);

        private static bool IsYes(string value)
        {
            string v = value.Trim().ToLowerInvariant();
            return v == "да" || v == "1" || v == "true" || v == "yes" || v == "+";
        }

        /// <summary>Число с запятой или точкой: Excel пишет так, как настроено в Windows.</summary>
        private static double ParseNumber(string value, int line, string what)
        {
            string v = value.Trim().Replace(" ", string.Empty).Replace(' '.ToString(), string.Empty).Replace(',', '.');
            if (double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out double result))
                return result;
            throw new FormatException($"Строка {line}: {what} «{value.Trim()}» — не число");
        }

        private static string Escape(string value) =>
            value.IndexOfAny(new[] { ';', '"', '\n', '\r' }) < 0 ? value : "\"" + value.Replace("\"", "\"\"") + "\"";

        private static List<string> SplitCsv(string line)
        {
            var cells = new List<string>();
            var cell = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"' && i + 1 < line.Length && line[i + 1] == '"')
                    {
                        cell.Append('"');
                        i++;
                    }
                    else if (c == '"')
                        quoted = false;
                    else
                        cell.Append(c);
                }
                else if (c == '"')
                    quoted = true;
                else if (c == ';')
                {
                    cells.Add(cell.ToString());
                    cell.Clear();
                }
                else
                    cell.Append(c);
            }
            cells.Add(cell.ToString());
            return cells;
        }

        private static MaterialLibrary ReadXml(string path)
        {
            using (var stream = File.OpenRead(path))
                return (MaterialLibrary)new XmlSerializer(typeof(MaterialLibrary)).Deserialize(stream) ?? new MaterialLibrary();
        }

        private static void WriteXml(string path, MaterialLibrary library)
        {
            using (var stream = File.Create(path))
                new XmlSerializer(typeof(MaterialLibrary)).Serialize(stream, library);
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace InvAuto.Core.Maintenance
{
    /// <summary>Файл, который будет удалён: показывается пользователю до удаления.</summary>
    public sealed class CleanupItem
    {
        public CleanupItem(string fullPath, string relativePath, long length, DateTime lastWriteUtc)
        {
            FullPath = fullPath;
            RelativePath = relativePath;
            Length = length;
            LastWriteUtc = lastWriteUtc;
        }

        public string FullPath { get; }
        public string RelativePath { get; }
        public long Length { get; }
        public DateTime LastWriteUtc { get; }
    }

    /// <summary>План очистки: что именно будет удалено. Удаляется только то, что в плане.</summary>
    public sealed class CleanupPlan
    {
        public CleanupPlan(string root, IReadOnlyList<CleanupItem> files, IReadOnlyList<string> skipped)
        {
            Root = root;
            Files = files;
            Skipped = skipped;
        }

        public string Root { get; }
        public IReadOnlyList<CleanupItem> Files { get; }

        /// <summary>Что найдено, но удаляться не будет (ссылки, чужие типы файлов) — с причиной.</summary>
        public IReadOnlyList<string> Skipped { get; }

        public long TotalBytes => Files.Sum(f => f.Length);

        /// <summary>План без указанного файла (например, текущего журнала, в который идёт запись).</summary>
        public CleanupPlan Except(string fullPath, string reason)
        {
            if (string.IsNullOrEmpty(fullPath))
                return this;
            string target = Path.GetFullPath(fullPath);
            var excluded = Files.Where(f => string.Equals(f.FullPath, target, StringComparison.OrdinalIgnoreCase)).ToList();
            if (excluded.Count == 0)
                return this;
            return new CleanupPlan(Root,
                Files.Except(excluded).ToList(),
                Skipped.Concat(excluded.Select(f => $"{f.RelativePath} — {reason}")).ToList());
        }
    }

    public sealed class CleanupResult
    {
        public int Deleted { get; internal set; }
        public long FreedBytes { get; internal set; }

        /// <summary>Не удалось удалить: путь и причина.</summary>
        public List<(string Path, string Reason)> Failed { get; } = new List<(string, string)>();
    }

    /// <summary>
    /// Безопасная очистка служебной папки (кэш разверток, журнал):
    /// — папка обязана заканчиваться ожидаемым путём (например «InvAuto\FlatCache») и не быть корнем диска;
    /// — символьные ссылки и точки соединения не обходятся и не удаляются;
    /// — удаляются только файлы заданных типов и только из плана, показанного пользователю;
    /// — файл, изменившийся после показа плана, пропускается;
    /// — сама папка не удаляется, только пустые подпапки внутри неё.
    /// </summary>
    public static class SafeCleanup
    {
        /// <summary>Проверяет, что root — именно ожидаемая служебная папка. Иначе исключение, удаление не начинается.</summary>
        public static string ValidateRoot(string root, string requiredSuffix)
        {
            if (string.IsNullOrWhiteSpace(root))
                throw new InvalidOperationException("Папка для очистки не задана.");

            string full = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            string driveRoot = Path.GetPathRoot(full)?.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) ?? string.Empty;
            if (full.Length == 0 || string.Equals(full, driveRoot, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Отказ: «{root}» — корень диска.");

            string suffix = requiredSuffix.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar)
                .Trim(Path.DirectorySeparatorChar);
            if (!full.EndsWith(Path.DirectorySeparatorChar + suffix, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"Отказ: «{full}» не является служебной папкой «…{Path.DirectorySeparatorChar}{suffix}».");

            if (Directory.Exists(full) && (File.GetAttributes(full) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidOperationException($"Отказ: «{full}» — ссылка на другую папку.");

            return full;
        }

        /// <summary>Составляет план: все файлы заданных типов внутри root (без перехода по ссылкам).</summary>
        public static CleanupPlan Plan(string root, string requiredSuffix, params string[] extensions)
        {
            string full = ValidateRoot(root, requiredSuffix);
            var files = new List<CleanupItem>();
            var skipped = new List<string>();
            if (!Directory.Exists(full))
                return new CleanupPlan(full, files, skipped);

            var allowed = new HashSet<string>(extensions.Select(e => e.StartsWith(".") ? e : "." + e), StringComparer.OrdinalIgnoreCase);
            var pending = new Stack<string>();
            pending.Push(full);
            while (pending.Count > 0)
            {
                string dir = pending.Pop();
                foreach (string sub in Directory.GetDirectories(dir))
                {
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                        skipped.Add($"{Relative(full, sub)} — ссылка на другую папку, не обходится");
                    else
                        pending.Push(sub);
                }

                foreach (string file in Directory.GetFiles(dir))
                {
                    var info = new FileInfo(file);
                    string relative = Relative(full, file);
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                        skipped.Add($"{relative} — ссылка на файл, не удаляется");
                    else if (!allowed.Contains(info.Extension))
                        skipped.Add($"{relative} — посторонний тип файла, не удаляется");
                    else
                        files.Add(new CleanupItem(info.FullName, relative, info.Length, info.LastWriteTimeUtc));
                }
            }

            return new CleanupPlan(full, files.OrderBy(f => f.RelativePath, StringComparer.OrdinalIgnoreCase).ToList(), skipped);
        }

        /// <summary>Удаляет файлы плана. Каждый перед удалением перепроверяется.</summary>
        public static CleanupResult Execute(CleanupPlan plan)
        {
            var result = new CleanupResult();
            string rootWithSeparator = plan.Root + Path.DirectorySeparatorChar;

            foreach (var item in plan.Files)
            {
                try
                {
                    string full = Path.GetFullPath(item.FullPath);
                    if (!full.StartsWith(rootWithSeparator, StringComparison.OrdinalIgnoreCase))
                    {
                        result.Failed.Add((item.RelativePath, "вне папки очистки — пропущен"));
                        continue;
                    }

                    var info = new FileInfo(full);
                    if (!info.Exists)
                        continue; // уже удалён — цель достигнута
                    if ((info.Attributes & FileAttributes.ReparsePoint) != 0)
                    {
                        result.Failed.Add((item.RelativePath, "стал ссылкой — пропущен"));
                        continue;
                    }
                    if (info.Length != item.Length || info.LastWriteTimeUtc != item.LastWriteUtc)
                    {
                        result.Failed.Add((item.RelativePath, "изменился после показа списка — пропущен"));
                        continue;
                    }
                    if ((info.Attributes & FileAttributes.ReadOnly) != 0)
                        info.Attributes &= ~FileAttributes.ReadOnly;

                    info.Delete();
                    result.Deleted++;
                    result.FreedBytes += item.Length;
                }
                catch (Exception ex)
                {
                    result.Failed.Add((item.RelativePath, ex.Message));
                }
            }

            RemoveEmptySubfolders(plan.Root);
            return result;
        }

        /// <summary>Сводка папки: количество файлов заданных типов и их объём (без перехода по ссылкам).</summary>
        public static (int Files, long Bytes) Measure(string root, string requiredSuffix, params string[] extensions)
        {
            try
            {
                var plan = Plan(root, requiredSuffix, extensions);
                return (plan.Files.Count, plan.TotalBytes);
            }
            catch
            {
                return (0, 0);
            }
        }

        public static string FormatSize(long bytes)
        {
            if (bytes < 1024)
                return bytes + " Б";
            if (bytes < 1024 * 1024)
                return (bytes / 1024.0).ToString("0.#") + " КБ";
            return (bytes / 1024.0 / 1024.0).ToString("0.#") + " МБ";
        }

        private static void RemoveEmptySubfolders(string root)
        {
            foreach (string sub in Directory.Exists(root) ? Directory.GetDirectories(root) : new string[0])
            {
                try
                {
                    if ((File.GetAttributes(sub) & FileAttributes.ReparsePoint) != 0)
                        continue;
                    RemoveEmptySubfolders(sub);
                    if (!Directory.EnumerateFileSystemEntries(sub).Any())
                        Directory.Delete(sub);
                }
                catch
                {
                    // занято — останется до следующего раза
                }
            }
        }

        private static string Relative(string root, string path) =>
            path.Length > root.Length ? path.Substring(root.Length).TrimStart(Path.DirectorySeparatorChar) : path;
    }
}

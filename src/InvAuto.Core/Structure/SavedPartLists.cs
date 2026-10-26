using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.Structure
{
    /// <summary>Сохранённый список деталей одной сборки.</summary>
    public sealed class SavedPartList
    {
        public string AssemblyPath { get; set; } = string.Empty;
        public List<string> Parts { get; set; } = new List<string>();
        public DateTime SavedUtc { get; set; }
    }

    /// <summary>
    /// Списки деталей окна «Листовые детали» по сборкам (настройка «Запоминать список деталей»).
    /// Хранятся последние MaxAssemblies сборок; пустой список не хранится.
    /// </summary>
    public sealed class SavedPartLists
    {
        public const string Key = "SheetMetal.Lists";
        public const int MaxAssemblies = 100;

        /// <summary>Пустой по умолчанию: XmlSerializer дописывает прочитанное в существующий список.</summary>
        public List<SavedPartList> Lists { get; set; } = new List<SavedPartList>();

        public IReadOnlyList<string> Get(string assemblyPath)
        {
            var entry = Find(assemblyPath);
            return entry?.Parts.ToList() ?? new List<string>();
        }

        /// <summary>Запоминает список сборки (пустой — забывает). Самые старые сверх MaxAssemblies удаляются.</summary>
        public void Set(string assemblyPath, IEnumerable<string> parts, DateTime nowUtc)
        {
            if (string.IsNullOrWhiteSpace(assemblyPath))
                return;
            Lists.RemoveAll(l => string.Equals(l.AssemblyPath, assemblyPath, StringComparison.OrdinalIgnoreCase));

            var items = parts.Where(p => !string.IsNullOrEmpty(p)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            if (items.Count > 0)
                Lists.Add(new SavedPartList { AssemblyPath = assemblyPath, Parts = items, SavedUtc = nowUtc });

            if (Lists.Count > MaxAssemblies)
                Lists = Lists.OrderByDescending(l => l.SavedUtc).Take(MaxAssemblies).ToList();
        }

        private SavedPartList Find(string assemblyPath) =>
            string.IsNullOrWhiteSpace(assemblyPath)
                ? null
                : Lists.FirstOrDefault(l => string.Equals(l.AssemblyPath, assemblyPath, StringComparison.OrdinalIgnoreCase));
    }
}

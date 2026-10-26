using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.Preflight
{
    /// <summary>Пропущенные замечания одной детали и «отпечаток» детали, при котором их пропустили.</summary>
    public sealed class DismissedIssues
    {
        /// <summary>Полный путь к файлу детали.</summary>
        public string PartPath { get; set; } = string.Empty;

        /// <summary>Отпечаток детали (дата сохранения файла и т. п.): изменился — пропуски сбрасываются.</summary>
        public string Stamp { get; set; } = string.Empty;

        /// <summary>Коды пропущенных замечаний (PreflightIssue.Code). Пустой по умолчанию: XmlSerializer дописывает элементы.</summary>
        public List<string> Codes { get; set; } = new List<string>();
    }

    /// <summary>
    /// Замечания проверки, которые пользователь пропустил для отдельных деталей («проверил — всё нормально»).
    /// Привязаны к отпечатку детали: как только деталь изменится (сохранят, начнут править), все её пропуски
    /// сбрасываются и замечания возвращаются.
    /// </summary>
    public sealed class PreflightDismissals
    {
        public const string Key = "SheetMetal.PreflightDismissed";

        /// <summary>Пустой по умолчанию: XmlSerializer дописывает элементы.</summary>
        public List<DismissedIssues> Parts { get; set; } = new List<DismissedIssues>();

        /// <summary>Замечания детали с отметками «пропущено» (по текущему отпечатку).</summary>
        public IReadOnlyList<PreflightIssue> Apply(string partPath, string stamp, IReadOnlyList<PreflightIssue> issues)
        {
            var part = Find(partPath);
            if (part == null || part.Stamp != stamp || issues == null)
                return issues;
            var codes = new HashSet<string>(part.Codes, StringComparer.Ordinal);
            return issues.Select(i => i.WithDismissed(codes.Contains(i.Code))).ToList();
        }

        /// <summary>Деталь изменилась — пропуски сбрасываются. true — что-то сброшено (настройки нужно сохранить).</summary>
        public bool Refresh(string partPath, string stamp)
        {
            var part = Find(partPath);
            if (part == null || part.Stamp == stamp)
                return false;
            Parts.Remove(part);
            return true;
        }

        public void Dismiss(string partPath, string stamp, string code)
        {
            var part = Find(partPath);
            if (part != null && part.Stamp != stamp)
            {
                Parts.Remove(part); // старые пропуски — для прежней версии детали
                part = null;
            }
            if (part == null)
                Parts.Add(part = new DismissedIssues { PartPath = partPath, Stamp = stamp });
            if (!part.Codes.Contains(code))
                part.Codes.Add(code);
        }

        public void Restore(string partPath, string code)
        {
            var part = Find(partPath);
            if (part == null)
                return;
            part.Codes.RemoveAll(c => c == code);
            if (part.Codes.Count == 0)
                Parts.Remove(part);
        }

        public int Count(string partPath) => Find(partPath)?.Codes.Count ?? 0;

        private DismissedIssues Find(string partPath) =>
            Parts.FirstOrDefault(p => string.Equals(p.PartPath, partPath, StringComparison.OrdinalIgnoreCase));
    }
}

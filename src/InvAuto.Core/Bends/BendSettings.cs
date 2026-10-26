using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.Bends
{
    /// <summary>Своя настройка одной линии гиба детали.</summary>
    public sealed class LineBendOptions
    {
        /// <summary>Ключ линии (BendLineInfo.Key): слой и координаты концов.</summary>
        public string LineKey { get; set; } = string.Empty;

        public BendLineOptions Options { get; set; } = new BendLineOptions();
    }

    /// <summary>Персональные параметры линий гиба одной детали.</summary>
    public sealed class PartBendOptions
    {
        /// <summary>Полный путь к файлу детали.</summary>
        public string PartPath { get; set; } = string.Empty;

        /// <summary>Параметры для всей детали. null — общие параметры (без инициализатора: так XmlSerializer сохраняет null).</summary>
        public BendLineOptions Options { get; set; }

        /// <summary>Свои параметры отдельных линий гиба (пустой по умолчанию: XmlSerializer дописывает элементы).</summary>
        public List<LineBendOptions> Lines { get; set; } = new List<LineBendOptions>();
    }

    /// <summary>
    /// Параметры линий гиба в три уровня: линия → деталь → общие.
    /// У линии без своих параметров — параметры детали, у детали без своих — общие.
    /// </summary>
    public sealed class BendSettings
    {
        public BendLineOptions Default { get; set; } = new BendLineOptions();

        /// <summary>Пустой по умолчанию: XmlSerializer дописывает элементы в существующий список.</summary>
        public List<PartBendOptions> Parts { get; set; } = new List<PartBendOptions>();

        /// <summary>Параметры детали (без учёта настроек отдельных линий).</summary>
        public BendLineOptions For(string partPath) => Find(partPath)?.Options ?? Default;

        /// <summary>Параметры конкретной линии гиба детали.</summary>
        public BendLineOptions ForLine(string partPath, string lineKey) => FindLine(partPath, lineKey)?.Options ?? For(partPath);

        /// <summary>Функция для обработки DXF детали: параметры каждой линии с учётом всех уровней.</summary>
        public Func<BendLineInfo, BendLineOptions> Resolver(string partPath)
        {
            var part = Find(partPath);
            var partOptions = part?.Options ?? Default;
            if (part == null || part.Lines.Count == 0)
                return _ => partOptions;

            var lines = part.Lines
                .GroupBy(l => l.LineKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last().Options, StringComparer.Ordinal);
            return line => lines.TryGetValue(line.Key, out var own) ? own : partOptions;
        }

        public bool HasOverride(string partPath) => Find(partPath)?.Options != null;

        public bool HasLineOverride(string partPath, string lineKey) => FindLine(partPath, lineKey) != null;

        public int LineOverrideCount(string partPath) => Find(partPath)?.Lines.Count ?? 0;

        public void SetOverride(string partPath, BendLineOptions options) => GetOrAdd(partPath).Options = options.Clone();

        /// <summary>Деталь возвращается к общим параметрам. Настройки отдельных линий остаются.</summary>
        public void ClearOverride(string partPath)
        {
            var part = Find(partPath);
            if (part == null)
                return;
            part.Options = null;
            RemoveIfEmpty(part);
        }

        public void SetLineOverride(string partPath, string lineKey, BendLineOptions options)
        {
            var part = GetOrAdd(partPath);
            part.Lines.RemoveAll(l => l.LineKey == lineKey);
            part.Lines.Add(new LineBendOptions { LineKey = lineKey, Options = options.Clone() });
        }

        public void ClearLineOverride(string partPath, string lineKey)
        {
            var part = Find(partPath);
            if (part == null)
                return;
            part.Lines.RemoveAll(l => l.LineKey == lineKey);
            RemoveIfEmpty(part);
        }

        /// <summary>Свои параметры линий детали: ключ линии → параметры (копии).</summary>
        public IReadOnlyDictionary<string, BendLineOptions> LineOverrides(string partPath) =>
            (Find(partPath)?.Lines ?? new List<LineBendOptions>())
                .GroupBy(l => l.LineKey, StringComparer.Ordinal)
                .ToDictionary(g => g.Key, g => g.Last().Options.Clone(), StringComparer.Ordinal);

        /// <summary>Заменяет все свои параметры линий детали (пустой набор — все линии как у детали).</summary>
        public void SetLineOverrides(string partPath, IReadOnlyDictionary<string, BendLineOptions> lines)
        {
            ClearLineOverrides(partPath);
            foreach (var pair in lines)
                SetLineOverride(partPath, pair.Key, pair.Value);
        }

        public void ClearLineOverrides(string partPath)
        {
            var part = Find(partPath);
            if (part == null)
                return;
            part.Lines.Clear();
            RemoveIfEmpty(part);
        }

        private PartBendOptions GetOrAdd(string partPath)
        {
            var part = Find(partPath);
            if (part == null)
            {
                part = new PartBendOptions { PartPath = partPath };
                Parts.Add(part);
            }
            return part;
        }

        private void RemoveIfEmpty(PartBendOptions part)
        {
            if (part.Options == null && part.Lines.Count == 0)
                Parts.Remove(part);
        }

        private PartBendOptions Find(string partPath) =>
            Parts.FirstOrDefault(p => string.Equals(p.PartPath, partPath, StringComparison.OrdinalIgnoreCase));

        private LineBendOptions FindLine(string partPath, string lineKey) =>
            Find(partPath)?.Lines.FirstOrDefault(l => l.LineKey == lineKey);
    }
}

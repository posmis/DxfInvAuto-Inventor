using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.Bends
{
    /// <summary>Персональные параметры линий гиба одной детали.</summary>
    public sealed class PartBendOptions
    {
        /// <summary>Полный путь к файлу детали.</summary>
        public string PartPath { get; set; } = string.Empty;

        public BendLineOptions Options { get; set; } = new BendLineOptions();
    }

    /// <summary>
    /// Параметры линий гиба: общие по умолчанию и персональные для отдельных деталей.
    /// Деталь без персональных параметров использует общие.
    /// </summary>
    public sealed class BendSettings
    {
        public BendLineOptions Default { get; set; } = new BendLineOptions();

        /// <summary>Пустой по умолчанию: XmlSerializer дописывает элементы в существующий список.</summary>
        public List<PartBendOptions> Parts { get; set; } = new List<PartBendOptions>();

        public BendLineOptions For(string partPath) => Find(partPath)?.Options ?? Default;

        public bool HasOverride(string partPath) => Find(partPath) != null;

        public void SetOverride(string partPath, BendLineOptions options)
        {
            var existing = Find(partPath);
            if (existing != null)
                existing.Options = options.Clone();
            else
                Parts.Add(new PartBendOptions { PartPath = partPath, Options = options.Clone() });
        }

        public void ClearOverride(string partPath) =>
            Parts.RemoveAll(p => string.Equals(p.PartPath, partPath, StringComparison.OrdinalIgnoreCase));

        private PartBendOptions Find(string partPath) =>
            Parts.FirstOrDefault(p => string.Equals(p.PartPath, partPath, StringComparison.OrdinalIgnoreCase));
    }
}

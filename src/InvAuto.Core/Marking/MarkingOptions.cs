using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace InvAuto.Core.Marking
{
    /// <summary>Маркировка развертки: обозначение детали текстом (TEXT) на отдельном слое DXF — для гравировки лазером.</summary>
    public sealed class MarkingOptions
    {
        public const string DefaultLayer = "MARKING";

        /// <summary>Наносить обозначение. По умолчанию выключено.</summary>
        public bool Enabled { get; set; }

        /// <summary>Высота текста, мм.</summary>
        public double Height { get; set; } = 5;

        /// <summary>Если не помещается — уменьшать высоту до этой, мм.</summary>
        public double MinHeight { get; set; } = 2.5;

        /// <summary>Наименьшее расстояние от текста до контуров, отверстий и линий гиба, мм.</summary>
        public double Margin { get; set; } = 2;

        /// <summary>Слой текста в DXF.</summary>
        public string Layer { get; set; } = DefaultLayer;

        public MarkingOptions Clone() => (MarkingOptions)MemberwiseClone();

        /// <summary>Нормальные значения: высоты и отступ в разумных пределах, слой не пустой.</summary>
        public MarkingOptions Normalized()
        {
            var o = Clone();
            o.Height = Clamp(o.Height, 0.5, 100, 5);
            o.MinHeight = Clamp(o.MinHeight, 0.5, o.Height, Math.Min(2.5, o.Height));
            o.Margin = Clamp(o.Margin, 0, 100, 2);
            o.Layer = string.IsNullOrWhiteSpace(o.Layer) ? DefaultLayer : o.Layer.Trim();
            return o;
        }

        /// <summary>Коротко для интерфейса: «выкл.» или «5 мм (до 2,5), слой MARKING».</summary>
        public string Summary
        {
            get
            {
                if (!Enabled)
                    return "выкл.";
                var c = CultureInfo.CurrentCulture;
                string size = MinHeight < Height
                    ? string.Format(c, "{0:0.##} мм (до {1:0.##})", Height, MinHeight)
                    : string.Format(c, "{0:0.##} мм", Height);
                return "обозначение " + size + ", слой " + (string.IsNullOrWhiteSpace(Layer) ? DefaultLayer : Layer.Trim());
            }
        }

        private static double Clamp(double v, double min, double max, double fallback) =>
            double.IsNaN(v) || double.IsInfinity(v) ? fallback : Math.Max(min, Math.Min(max, v));
    }

    /// <summary>Своя маркировка детали.</summary>
    public sealed class PartMarkingOptions
    {
        /// <summary>Полный путь к файлу детали.</summary>
        public string PartPath { get; set; } = string.Empty;

        public MarkingOptions Options { get; set; } = new MarkingOptions();
    }

    /// <summary>Маркировка в два уровня: деталь → общие. У детали без своих настроек — общие.</summary>
    public sealed class MarkingSettings
    {
        public const string Key = "SheetMetal.Marking";

        public MarkingOptions Default { get; set; } = new MarkingOptions();

        /// <summary>Пустой по умолчанию: XmlSerializer дописывает элементы в существующий список.</summary>
        public List<PartMarkingOptions> Parts { get; set; } = new List<PartMarkingOptions>();

        public MarkingOptions For(string partPath) => (Find(partPath)?.Options ?? Default ?? new MarkingOptions()).Normalized();

        public bool HasOverride(string partPath) => Find(partPath) != null;

        public void SetOverride(string partPath, MarkingOptions options)
        {
            var part = Find(partPath);
            if (part == null)
                Parts.Add(part = new PartMarkingOptions { PartPath = partPath });
            part.Options = options.Clone();
        }

        public void ClearOverride(string partPath) =>
            Parts.RemoveAll(p => string.Equals(p.PartPath, partPath, StringComparison.OrdinalIgnoreCase));

        private PartMarkingOptions Find(string partPath) =>
            Parts.LastOrDefault(p => p.Options != null && string.Equals(p.PartPath, partPath, StringComparison.OrdinalIgnoreCase));
    }
}

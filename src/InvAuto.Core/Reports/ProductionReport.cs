using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using InvAuto.Core.Preview;
using InvAuto.Core.Sorting;

namespace InvAuto.Core.Reports
{
    /// <summary>Позиция ведомости: одна деталь со всеми количествами на изделия.</summary>
    public sealed class ReportItem
    {
        public string Key { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;

        /// <summary>Материал из библиотеки; пусто — не задан.</summary>
        public string Material { get; set; } = string.Empty;

        public double Thickness { get; set; }

        /// <summary>Количество на все изделия.</summary>
        public int Quantity { get; set; }

        public double FlatLength { get; set; }
        public double FlatWidth { get; set; }

        /// <summary>Площадь одной детали, м² (null — развертка не сформирована).</summary>
        public double? Area { get; set; }

        /// <summary>Масса одной детали, кг (null — нет развертки или материала).</summary>
        public double? Mass { get; set; }

        /// <summary>Длина реза одной детали, м.</summary>
        public double? CutLength { get; set; }

        /// <summary>Врезки на одну деталь.</summary>
        public int? Pierces { get; set; }

        /// <summary>Развертка для картинки в отчёте (может быть null).</summary>
        public FlatDrawing Flat { get; set; }

        /// <summary>Замечание: почему нет данных и т. п.</summary>
        public string Note { get; set; } = string.Empty;

        public string Title => PartNumber.Length > 0 ? PartNumber : FileName;

        public double? TotalArea => Area * Quantity;
        public double? TotalMass => Mass * Quantity;
        public double? TotalCutLength => CutLength * Quantity;
        public int? TotalPierces => Pierces * Quantity;

        public string SizeText => FlatLength > 0
            ? string.Format(CultureInfo.CurrentCulture, "{0:0.#} × {1:0.#}", Math.Max(FlatLength, FlatWidth), Math.Min(FlatLength, FlatWidth))
            : string.Empty;
    }

    /// <summary>Строка сводки: материал и толщина.</summary>
    public sealed class ReportGroup
    {
        public string Material { get; set; } = string.Empty;
        public double Thickness { get; set; }

        /// <summary>Разных деталей.</summary>
        public int Positions { get; set; }

        /// <summary>Деталей на все изделия.</summary>
        public int Quantity { get; set; }

        /// <summary>Сумма по позициям; null — ни у одной позиции нет данных (выводится прочерк, а не ноль).</summary>
        public double? Area { get; set; }
        public double? Mass { get; set; }
        public double? CutLength { get; set; }
        public int? Pierces { get; set; }

        /// <summary>Позиций без площади или массы — итог группы неполный.</summary>
        public int Incomplete { get; set; }

        public string MaterialTitle => Material.Length > 0 ? Material : "материал не задан";
    }

    /// <summary>
    /// Ведомость листовых деталей: шапка (название, изделие, дата), позиции и сводка по материалам и толщинам.
    /// Одни и те же данные выводятся в Excel и в PDF.
    /// </summary>
    public sealed class ProductionReport
    {
        public const string DefaultTitle = "Ведомость листовых деталей";

        public string Title { get; set; } = DefaultTitle;
        public string Product { get; set; } = string.Empty;
        public DateTime Date { get; set; } = DateTime.Now;
        public int ProductQuantity { get; set; } = 1;
        public bool IncludeFlats { get; set; } = true;
        public List<ReportItem> Items { get; set; } = new List<ReportItem>();

        /// <summary>Позиции в порядке ведомости: материал, толщина, обозначение (числа по величине).</summary>
        public IReadOnlyList<ReportItem> SortedItems() => Items
            .OrderBy(i => i.Material.Length == 0)
            .ThenBy(i => i.Material, NaturalComparer.Instance)
            .ThenBy(i => i.Thickness)
            .ThenBy(i => i.Title, NaturalComparer.Instance)
            .ToList();

        public IReadOnlyList<ReportGroup> Groups() => Items
            .GroupBy(i => (i.Material, Math.Round(i.Thickness, 2)))
            .Select(g => new ReportGroup
            {
                Material = g.Key.Material,
                Thickness = g.Key.Item2,
                Positions = g.Count(),
                Quantity = g.Sum(i => i.Quantity),
                Area = Sum(g.Select(i => i.TotalArea)),
                Mass = Sum(g.Select(i => i.TotalMass)),
                CutLength = Sum(g.Select(i => i.TotalCutLength)),
                Pierces = Sum(g.Select(i => i.TotalPierces)),
                Incomplete = g.Count(i => i.Area == null || i.Mass == null)
            })
            .OrderBy(g => g.Material.Length == 0)
            .ThenBy(g => g.Material, NaturalComparer.Instance)
            .ThenBy(g => g.Thickness)
            .ToList();

        public ReportGroup Totals()
        {
            var groups = Groups();
            return new ReportGroup
            {
                Material = "Итого",
                Positions = groups.Sum(g => g.Positions),
                Quantity = groups.Sum(g => g.Quantity),
                Area = Sum(groups.Select(g => g.Area)),
                Mass = Sum(groups.Select(g => g.Mass)),
                CutLength = Sum(groups.Select(g => g.CutLength)),
                Pierces = Sum(groups.Select(g => g.Pierces)),
                Incomplete = groups.Sum(g => g.Incomplete)
            };
        }

        private static double? Sum(IEnumerable<double?> values)
        {
            var known = values.Where(v => v.HasValue).ToList();
            return known.Count == 0 ? (double?)null : known.Sum(v => v.Value);
        }

        private static int? Sum(IEnumerable<int?> values)
        {
            var known = values.Where(v => v.HasValue).ToList();
            return known.Count == 0 ? (int?)null : known.Sum(v => v.Value);
        }

        /// <summary>Строка под названием: изделие, количество изделий.</summary>
        public string Subtitle =>
            (Product.Length > 0 ? "Изделие: " + Product : string.Empty) +
            (ProductQuantity > 1 ? (Product.Length > 0 ? " · " : string.Empty) + $"изделий: {ProductQuantity}" : string.Empty);

        public string DateText => Date.ToString("dd.MM.yyyy", CultureInfo.GetCultureInfo("ru-RU"));

        /// <summary>Имя файла по умолчанию: «Название Изделие ГГГГ-ММ-ДД» без недопустимых символов.</summary>
        public string SuggestedFileName()
        {
            string name = string.Join(" ", new[] { Title, Product, Date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) }
                .Where(s => !string.IsNullOrWhiteSpace(s)));
            foreach (char c in System.IO.Path.GetInvalidFileNameChars().Concat(new[] { '\\', '/', ':', '*', '?', '"', '<', '>', '|' }))
                name = name.Replace(c, '-');
            return name.Trim().TrimEnd('.');
        }
    }

    /// <summary>Настройки окна отчёта (сохраняются).</summary>
    public sealed class ReportSettings
    {
        public const string Key = "SheetMetal.Report";

        public string Title { get; set; } = ProductionReport.DefaultTitle;
        public bool IncludeFlats { get; set; } = true;
        public bool OnlyChecked { get; set; } = true;
        public bool Excel { get; set; } = true;
        public bool Pdf { get; set; } = true;
        public bool OpenAfterSave { get; set; } = true;
        public string LastFolder { get; set; } = string.Empty;
    }

    /// <summary>Форматирование чисел отчёта: русская запятая, без лишних нулей.</summary>
    public static class ReportFormat
    {
        private static readonly CultureInfo Ru = CultureInfo.GetCultureInfo("ru-RU");

        public static string Number(double? value, string format) => value is double v ? v.ToString(format, Ru) : "—";
        public static string Integer(int? value) => value is int v ? v.ToString("#,##0", Ru) : "—";
    }
}

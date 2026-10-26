using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using InvAuto.Core.Bends;
using InvAuto.Core.Marking;
using InvAuto.Core.Preview;

namespace InvAuto.Core.Preflight
{
    public enum IssueSeverity
    {
        /// <summary>Для сведения.</summary>
        Info,

        /// <summary>Проверьте: выгрузка пройдёт, но результат может быть не тем.</summary>
        Warning,

        /// <summary>Деталь не выгрузится или DXF будет неверным.</summary>
        Error
    }

    /// <summary>Замечание проверки по детали.</summary>
    public sealed class PreflightIssue
    {
        public PreflightIssue(IssueSeverity severity, string code, string message, IReadOnlyList<Point2> locations = null)
        {
            Severity = severity;
            Code = code;
            Message = message;
            Locations = locations ?? Array.Empty<Point2>();
        }

        public IssueSeverity Severity { get; }

        /// <summary>Постоянный код (для отключения проверок и тестов), например "SmallHole".</summary>
        public string Code { get; }

        public string Message { get; }

        /// <summary>Где на развертке (мм): для отметок в окне просмотра.</summary>
        public IReadOnlyList<Point2> Locations { get; }

        public string SeverityText => Severity == IssueSeverity.Error ? "Ошибка" : Severity == IssueSeverity.Warning ? "Внимание" : "Сведения";

        public override string ToString() => SeverityText + ": " + Message;
    }

    /// <summary>Состояние материала детали для проверки.</summary>
    public enum MaterialCheck
    {
        Ok,
        Missing,
        Mismatch
    }

    /// <summary>Всё, что проверка знает о детали. Собирается из строки списка или из данных Inventor.</summary>
    public sealed class PreflightPart
    {
        public string Key { get; set; } = string.Empty;
        public string FileName { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public double Thickness { get; set; }

        /// <summary>Толщина назначенного материала (для замечания о несовпадении), 0 — неизвестна.</summary>
        public double MaterialThickness { get; set; }

        public bool HasFlatPattern { get; set; }

        /// <summary>При выгрузке развертка будет создана автоматически (настройка выгрузки).</summary>
        public bool WillCreateFlat { get; set; }

        public bool IsModified { get; set; }
        public bool NeedsUpdate { get; set; }

        public MaterialCheck Material { get; set; } = MaterialCheck.Ok;

        /// <summary>Сформированная развертка (null — не сформирована, геометрия не проверяется).</summary>
        public FlatDrawing Drawing { get; set; }

        /// <summary>Ошибка формирования развертки.</summary>
        public string FlatError { get; set; } = string.Empty;

        /// <summary>Линии гиба развертки (как в исходном DXF, до обработки).</summary>
        public IReadOnlyList<BendLineInfo> BendLines { get; set; } = Array.Empty<BendLineInfo>();

        public double FlatLength { get; set; }
        public double FlatWidth { get; set; }

        /// <summary>Маркировка на сформированной развертке (Off — выключена или развертки нет).</summary>
        public MarkingStatus Marking { get; set; }

        public string Title => PartNumber.Length > 0 ? PartNumber : FileName;
    }

    /// <summary>Параметры проверки (окно «Настройки InvAuto» → «Проверка»).</summary>
    public sealed class PreflightSettings
    {
        public const string Key = "SheetMetal.Preflight";

        /// <summary>Проверять детали перед выгрузкой DXF.</summary>
        public bool CheckBeforeExport { get; set; } = true;

        /// <summary>Отверстие должно быть не меньше толщины × этот коэффициент.</summary>
        public double MinHoleFactor { get; set; } = 1.0;

        /// <summary>…и не меньше этого размера, мм.</summary>
        public double MinHoleMm { get; set; } = 1.0;

        /// <summary>От края отверстия до линии гиба — не меньше толщины × этот коэффициент.</summary>
        public double BendClearanceFactor { get; set; } = 2.0;

        /// <summary>Проверять, помещается ли развертка на лист.</summary>
        public bool CheckSheetSize { get; set; } = true;

        public double SheetLength { get; set; } = 3000;
        public double SheetWidth { get; set; } = 1500;

        /// <summary>Предупреждать о деталях без обозначения.</summary>
        public bool RequirePartNumber { get; set; } = true;

        public void Normalize()
        {
            MinHoleFactor = Clamp(MinHoleFactor, 0, 10);
            MinHoleMm = Clamp(MinHoleMm, 0, 100);
            BendClearanceFactor = Clamp(BendClearanceFactor, 0, 20);
            SheetLength = Clamp(SheetLength, 10, 100000);
            SheetWidth = Clamp(SheetWidth, 10, 100000);
        }

        private static double Clamp(double v, double min, double max) => double.IsNaN(v) ? min : Math.Max(min, Math.Min(max, v));
    }

    /// <summary>
    /// Проверка листовых деталей перед выгрузкой: развертка есть и замкнута, модель актуальна и сохранена,
    /// есть обозначение и материал, отверстия не меньше допустимого и не слишком близко к линиям гиба,
    /// развертка помещается на лист, обозначения не повторяются у разных деталей.
    /// </summary>
    public static class PreflightChecker
    {
        private static string N(double v) => v.ToString("0.##", CultureInfo.CurrentCulture);

        public static IReadOnlyList<PreflightIssue> Check(PreflightPart part, PreflightSettings settings)
        {
            var issues = new List<PreflightIssue>();

            if (!part.HasFlatPattern)
            {
                issues.Add(part.WillCreateFlat
                    ? new PreflightIssue(IssueSeverity.Warning, "NoFlat", "Нет развертки — будет создана при выгрузке")
                    : new PreflightIssue(IssueSeverity.Error, "NoFlat", "Нет развертки"));
            }
            else if (part.FlatError.Length > 0)
            {
                issues.Add(new PreflightIssue(IssueSeverity.Error, "FlatFailed", "Ошибка развертки: " + part.FlatError));
            }

            if (part.NeedsUpdate)
                issues.Add(new PreflightIssue(IssueSeverity.Warning, "NeedsUpdate",
                    "Модель не перестроена"));
            if (part.IsModified)
                issues.Add(new PreflightIssue(IssueSeverity.Warning, "Modified",
                    "Деталь не сохранена"));

            if (settings.RequirePartNumber && part.PartNumber.Trim().Length == 0)
                issues.Add(new PreflightIssue(IssueSeverity.Warning, "NoPartNumber",
                    "Нет обозначения"));

            if (part.Marking == MarkingStatus.NoRoom)
                issues.Add(new PreflightIssue(IssueSeverity.Warning, "MarkingNoRoom", "Маркировка не помещается"));
            else if (part.Marking == MarkingStatus.NoText && !issues.Any(i => i.Code == "NoPartNumber"))
                issues.Add(new PreflightIssue(IssueSeverity.Warning, "MarkingNoText", "Нет обозначения для маркировки"));

            if (part.Material == MaterialCheck.Missing)
                issues.Add(new PreflightIssue(IssueSeverity.Warning, "NoMaterial",
                    $"Нет материала {N(part.Thickness)} мм в библиотеке"));
            else if (part.Material == MaterialCheck.Mismatch)
                issues.Add(new PreflightIssue(IssueSeverity.Warning, "MaterialMismatch",
                    part.MaterialThickness > 0
                        ? $"Материал {N(part.MaterialThickness)} мм, деталь {N(part.Thickness)} мм"
                        : "Толщина материала ≠ толщине детали"));

            if (settings.CheckSheetSize && part.FlatLength > 0)
            {
                double big = Math.Max(part.FlatLength, part.FlatWidth), small = Math.Min(part.FlatLength, part.FlatWidth);
                double sheetBig = Math.Max(settings.SheetLength, settings.SheetWidth), sheetSmall = Math.Min(settings.SheetLength, settings.SheetWidth);
                if (big > sheetBig || small > sheetSmall)
                    issues.Add(new PreflightIssue(IssueSeverity.Warning, "SheetSize",
                        $"Больше листа: {N(big)} × {N(small)} > {N(sheetBig)} × {N(sheetSmall)} мм"));
            }

            if (part.HasFlatPattern && part.FlatError.Length == 0)
            {
                if (part.Drawing == null || part.Drawing.IsEmpty)
                    issues.Add(new PreflightIssue(IssueSeverity.Info, "NotFormed",
                        "Развертка не сформирована — геометрия не проверена"));
                else
                    CheckGeometry(part, settings, issues);
            }

            return issues;
        }

        private static void CheckGeometry(PreflightPart part, PreflightSettings settings, List<PreflightIssue> issues)
        {
            var contours = FlatContours.Build(part.Drawing);

            var open = contours.Where(c => !c.Closed).ToList();
            if (open.Count > 0)
                issues.Add(new PreflightIssue(IssueSeverity.Error, "OpenContour",
                    $"Разрыв контура: {open.Count} шт.",
                    open.SelectMany(c => new[] { c.Points[0], c.Points[c.Points.Count - 1] }).ToList()));
            if (!contours.Any(c => c.IsOuter))
                return;

            var holes = contours.Where(c => c.Closed && !c.IsOuter).ToList();

            double minHole = Math.Max(settings.MinHoleMm, settings.MinHoleFactor * part.Thickness);
            if (minHole > 0)
            {
                var small = holes.Where(h => h.Size < minHole - 1e-6).ToList();
                if (small.Count > 0)
                    issues.Add(new PreflightIssue(IssueSeverity.Warning, "SmallHole",
                        $"Отверстия < {N(minHole)} мм: {small.Count} шт., мин. {N(small.Min(h => h.Size))} мм",
                        small.Select(h => h.Center).ToList()));
            }

            double clearance = settings.BendClearanceFactor * part.Thickness;
            if (clearance > 0 && part.BendLines.Count > 0)
            {
                var near = new List<(Contour Hole, double Distance)>();
                foreach (var hole in holes.Where(h => !IsBendEnd(h, part.BendLines)))
                {
                    double d = part.BendLines.Min(l => hole.DistanceTo(new Point2(l.X1, l.Y1), new Point2(l.X2, l.Y2)));
                    if (d < clearance - 1e-6)
                        near.Add((hole, d));
                }
                if (near.Count > 0)
                {
                    double nearest = near.Min(n => n.Distance);
                    issues.Add(new PreflightIssue(IssueSeverity.Warning, "HoleNearBend",
                        $"Отверстия у линии гиба (< {N(clearance)} мм): {near.Count} шт., " +
                        (nearest <= 0 ? "есть на линии" : $"мин. {N(nearest)} мм"),
                        near.Select(n => n.Hole.Center).ToList()));
                }
            }
        }

        /// <summary>
        /// Законцовка линии гиба: вырез с центром на линии гиба не дальше своего размера от её конца
        /// (на краю детали или немного внутрь). Такие вырезы делаются нарочно — замечание «отверстие у линии гиба»
        /// для них не нужно; отверстие дальше от конца линии проверяется как обычно.
        /// </summary>
        private static bool IsBendEnd(Contour hole, IReadOnlyList<BendLineInfo> lines)
        {
            var c = hole.Center;
            foreach (var l in lines)
            {
                var a = new Point2(l.X1, l.Y1);
                var b = new Point2(l.X2, l.Y2);
                if (Geometry2.Distance(c, new Segment2(a, b, null).Nearest(c)) > 0.05)
                    continue;
                double toEnd = Math.Min(Geometry2.Distance(c, a), Geometry2.Distance(c, b));
                double size = Math.Max(hole.MaxX - hole.MinX, hole.MaxY - hole.MinY);
                if (toEnd <= size + 0.05)
                    return true;
            }
            return false;
        }

        /// <summary>Проверка набора деталей: каждая отдельно плюс повторяющиеся обозначения у разных файлов.</summary>
        public static IReadOnlyDictionary<string, IReadOnlyList<PreflightIssue>> CheckAll(IEnumerable<PreflightPart> parts, PreflightSettings settings)
        {
            var list = parts.ToList();
            var result = new Dictionary<string, IReadOnlyList<PreflightIssue>>(StringComparer.OrdinalIgnoreCase);
            foreach (var part in list)
                result[part.Key] = Check(part, settings).ToList();

            foreach (var group in list.Where(p => p.PartNumber.Trim().Length > 0)
                         .GroupBy(p => p.PartNumber.Trim(), StringComparer.OrdinalIgnoreCase)
                         .Where(g => g.Select(p => p.Key).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1))
            {
                foreach (var part in group)
                {
                    string others = string.Join(", ", group.Where(p => !string.Equals(p.Key, part.Key, StringComparison.OrdinalIgnoreCase))
                        .Select(p => Path.GetFileName(p.Key)));
                    var issues = (List<PreflightIssue>)result[part.Key];
                    issues.Add(new PreflightIssue(IssueSeverity.Warning, "DuplicatePartNumber",
                        $"Обозначение «{group.Key}» повторяется: {others}"));
                }
            }
            return result;
        }

        /// <summary>Итог по набору: (ошибок, предупреждений) — по числу деталей с такими замечаниями.</summary>
        public static (int Errors, int Warnings) Summary(IReadOnlyDictionary<string, IReadOnlyList<PreflightIssue>> results) =>
            (results.Values.Count(v => v.Any(i => i.Severity == IssueSeverity.Error)),
             results.Values.Count(v => v.Any(i => i.Severity == IssueSeverity.Warning) && v.All(i => i.Severity != IssueSeverity.Error)));
    }
}

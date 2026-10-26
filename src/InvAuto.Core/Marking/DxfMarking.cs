using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InvAuto.Core.Bends;
using InvAuto.Core.Preview;

namespace InvAuto.Core.Marking
{
    public enum MarkingStatus
    {
        /// <summary>Маркировка выключена.</summary>
        Off,

        /// <summary>Включена, но у детали нет обозначения.</summary>
        NoText,

        /// <summary>Текст не помещается даже минимальной высоты.</summary>
        NoRoom,

        /// <summary>Нанесена.</summary>
        Placed
    }

    /// <summary>Итог маркировки: DXF (тот же экземпляр строки, если текст не добавлялся) и где стоит текст.</summary>
    public sealed class MarkingResult
    {
        public MarkingResult(MarkingStatus status, string dxf, FlatText text = null)
        {
            Status = status;
            Dxf = dxf;
            Text = text;
        }

        public MarkingStatus Status { get; }
        public string Dxf { get; }
        public FlatText Text { get; }

        /// <summary>Для панели свойств.</summary>
        public string Description
        {
            get
            {
                switch (Status)
                {
                    case MarkingStatus.NoText: return "нет обозначения";
                    case MarkingStatus.NoRoom: return "не помещается";
                    case MarkingStatus.Placed:
                        return string.Format(System.Globalization.CultureInfo.CurrentCulture, "«{0}», {1:0.##} мм{2}",
                            Text.Value, Text.Height, Text.Vertical ? ", вертикально" : string.Empty);
                    default: return "выкл.";
                }
            }
        }
    }

    /// <summary>
    /// Маркировка развертки обозначением: место ищется автоматически — как можно ближе к центру детали, на металле
    /// (внутри наружного контура и не в вырезе), с отступом от контура, отверстий и линий гиба. Сначала горизонтально, затем вертикально;
    /// не помещается — высота уменьшается (до MinHeight).
    /// </summary>
    public static class DxfMarking
    {
        /// <summary>Ширина символа относительно высоты — с запасом: шрифт выбирает программа раскроя.</summary>
        public const double CharWidth = 0.9;

        /// <summary>Цвет слоя маркировки, если слоя нет в файле (3 — зелёный).</summary>
        public const int LayerColor = 3;

        /// <summary>Маркировка обработанного DXF; drawing — он же, прочитанный (null — прочитать).</summary>
        public static MarkingResult Apply(string dxf, string text, MarkingOptions options, FlatDrawing drawing = null)
        {
            if (options == null || !options.Enabled)
                return new MarkingResult(MarkingStatus.Off, dxf);
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0)
                return new MarkingResult(MarkingStatus.NoText, dxf);

            var o = options.Normalized();
            drawing = drawing ?? DxfReader.Read(new StringReader(dxf));
            var placed = Place(drawing, text, o);
            return placed == null
                ? new MarkingResult(MarkingStatus.NoRoom, dxf)
                : new MarkingResult(MarkingStatus.Placed, DxfBendProcessor.AddText(dxf, placed, o.Layer, LayerColor), placed);
        }

        /// <summary>Место для текста на развертке; null — не помещается.</summary>
        public static FlatText Place(FlatDrawing drawing, string text, MarkingOptions options)
        {
            var o = options.Normalized();
            var contours = FlatContours.Build(drawing);
            var outer = contours.FirstOrDefault(c => c.IsOuter);
            if (outer == null || string.IsNullOrWhiteSpace(text))
                return null;
            // Вырезы: то, что внутри них, при резке отпадает — маркировать там нельзя.
            var holes = contours.Where(c => c.Closed && !c.IsOuter).ToList();

            var segments = new List<(Point2 A, Point2 B)>();
            foreach (var p in drawing.Polylines.Where(p => p.Kind == LayerKind.Profile || p.Kind == LayerKind.Bend))
            {
                for (int i = 1; i < p.Points.Count; i++)
                    segments.Add((p.Points[i - 1], p.Points[i]));
                if (p.Closed && p.Points.Count > 2)
                    segments.Add((p.Points[p.Points.Count - 1], p.Points[0]));
            }

            double cx = (outer.MinX + outer.MaxX) / 2, cy = (outer.MinY + outer.MaxY) / 2;
            double size = Math.Max(outer.MaxX - outer.MinX, outer.MaxY - outer.MinY);
            for (double h = o.Height; h >= o.MinHeight - 1e-9; h = h > o.MinHeight + 1e-9 ? Math.Max(o.MinHeight, h * 0.8) : 0)
            {
                double w = text.Length * h * CharWidth;
                foreach (bool vertical in new[] { false, true })
                {
                    double bw = vertical ? h : w, bh = vertical ? w : h; // габарит текста
                    double step = Math.Max(h / 4, size / 120);
                    var candidates = new List<(double X, double Y, double D)>();
                    for (double x = outer.MinX + bw / 2; x <= outer.MaxX - bw / 2 + 1e-9; x += step)
                        for (double y = outer.MinY + bh / 2; y <= outer.MaxY - bh / 2 + 1e-9; y += step)
                            candidates.Add((x, y, (x - cx) * (x - cx) + (y - cy) * (y - cy)));
                    // Центр габарита — тоже кандидат: у симметричных деталей текст встаёт точно посередине.
                    candidates.Add((cx, cy, -1));

                    foreach (var c in candidates.OrderBy(c => c.D))
                    {
                        double x0 = c.X - bw / 2, y0 = c.Y - bh / 2, x1 = c.X + bw / 2, y1 = c.Y + bh / 2;
                        // Рамку не пересекает ни одна линия — значит, она целиком там же, где её центр:
                        // достаточно, чтобы центр был на металле (внутри контура и не в вырезе).
                        var center = new Point2(c.X, c.Y);
                        if (!outer.Contains(center) || holes.Any(h => h.Contains(center)) ||
                            !Clear(segments, x0 - o.Margin, y0 - o.Margin, x1 + o.Margin, y1 + o.Margin))
                            continue;
                        // Точка вставки — начало базовой линии; вертикальный текст идёт снизу вверх, верх букв — влево.
                        return vertical
                            ? new FlatText(text, x1, y0, h, 90, w)
                            : new FlatText(text, x0, y0, h, 0, w);
                    }
                }
                if (h <= o.MinHeight + 1e-9)
                    break;
            }
            return null;
        }

        /// <summary>Ни один отрезок не задевает прямоугольник.</summary>
        private static bool Clear(List<(Point2 A, Point2 B)> segments, double x0, double y0, double x1, double y1)
        {
            foreach (var (a, b) in segments)
            {
                if (Math.Max(a.X, b.X) < x0 || Math.Min(a.X, b.X) > x1 || Math.Max(a.Y, b.Y) < y0 || Math.Min(a.Y, b.Y) > y1)
                    continue;
                if (Crosses(a, b, x0, y0, x1, y1))
                    return false;
            }
            return true;
        }

        /// <summary>Отрезок пересекает прямоугольник или лежит в нём (отсечение Лианга — Барски).</summary>
        private static bool Crosses(Point2 a, Point2 b, double x0, double y0, double x1, double y1)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, t0 = 0, t1 = 1;
            bool Clip(double p, double q)
            {
                if (Math.Abs(p) < 1e-15)
                    return q >= 0;
                double r = q / p;
                if (p < 0)
                {
                    if (r > t1) return false;
                    if (r > t0) t0 = r;
                }
                else
                {
                    if (r < t0) return false;
                    if (r < t1) t1 = r;
                }
                return true;
            }
            return Clip(-dx, a.X - x0) && Clip(dx, x1 - a.X) && Clip(-dy, a.Y - y0) && Clip(dy, y1 - a.Y);
        }
    }
}

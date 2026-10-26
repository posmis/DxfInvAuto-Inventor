using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.Preview
{
    /// <summary>Контур развертки: замкнутый (наружный или отверстие) или разомкнутая цепочка.</summary>
    public sealed class Contour
    {
        public Contour(IReadOnlyList<Point2> points, bool closed)
        {
            Points = points;
            Closed = closed;
            if (points.Count == 0)
                return;
            MinX = points.Min(p => p.X);
            MinY = points.Min(p => p.Y);
            MaxX = points.Max(p => p.X);
            MaxY = points.Max(p => p.Y);
            if (closed)
                Area = Math.Abs(SignedArea(points));
        }

        public IReadOnlyList<Point2> Points { get; }
        public bool Closed { get; }
        public double Area { get; }
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }

        /// <summary>Наружный контур детали (самый большой замкнутый).</summary>
        public bool IsOuter { get; internal set; }

        /// <summary>
        /// Характерный размер отверстия: меньшая сторона габарита. Для круглого — диаметр,
        /// для паза — его ширина, то есть то, что ограничивает резку и пробивку.
        /// </summary>
        public double Size => Math.Min(MaxX - MinX, MaxY - MinY);

        public Point2 Center => new Point2((MinX + MaxX) / 2, (MinY + MaxY) / 2);

        public double Length
        {
            get
            {
                double length = 0;
                for (int i = 1; i < Points.Count; i++)
                    length += Geometry2.Distance(Points[i - 1], Points[i]);
                if (Closed && Points.Count > 2)
                    length += Geometry2.Distance(Points[Points.Count - 1], Points[0]);
                return length;
            }
        }

        /// <summary>Точка внутри замкнутого контура (правило чётности пересечений).</summary>
        public bool Contains(Point2 p)
        {
            if (!Closed || p.X < MinX || p.X > MaxX || p.Y < MinY || p.Y > MaxY)
                return false;
            bool inside = false;
            for (int i = 0, j = Points.Count - 1; i < Points.Count; j = i++)
            {
                var a = Points[i];
                var b = Points[j];
                if ((a.Y > p.Y) != (b.Y > p.Y) && p.X < (b.X - a.X) * (p.Y - a.Y) / (b.Y - a.Y) + a.X)
                    inside = !inside;
            }
            return inside;
        }

        /// <summary>
        /// Какая доля площади этого контура лежит внутри other (оценка по сетке 24 × 24): законцовка на краю
        /// детали выходит за контур наполовину — вычитать из площади детали нужно только внутреннюю часть.
        /// </summary>
        public double FractionInside(Contour other)
        {
            if (!Closed || other == null || !other.Closed)
                return 0;
            if (Points.All(other.Contains))
                return 1; // целиком внутри — частый случай, без сетки
            const int n = 24;
            int inSelf = 0, inBoth = 0;
            double w = MaxX - MinX, h = MaxY - MinY;
            for (int i = 0; i < n; i++)
            {
                for (int j = 0; j < n; j++)
                {
                    var p = new Point2(MinX + w * (i + 0.5) / n, MinY + h * (j + 0.5) / n);
                    if (!Contains(p))
                        continue;
                    inSelf++;
                    if (other.Contains(p))
                        inBoth++;
                }
            }
            return inSelf == 0 ? 0 : (double)inBoth / inSelf;
        }

        /// <summary>Наименьшее расстояние от контура до отрезка (0 — пересекает).</summary>
        public double DistanceTo(Point2 a, Point2 b)
        {
            double best = double.MaxValue;
            int count = Closed ? Points.Count : Points.Count - 1;
            for (int i = 0; i < count; i++)
            {
                double d = Geometry2.SegmentDistance(Points[i], Points[(i + 1) % Points.Count], a, b);
                if (d < best)
                    best = d;
                if (best <= 0)
                    return 0;
            }
            return best;
        }

        private static double SignedArea(IReadOnlyList<Point2> points)
        {
            double sum = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2;
        }
    }

    /// <summary>
    /// Сшивает контур развертки в замкнутые контуры: в DXF Inventor наружный контур и отверстия обычно
    /// записаны отдельными отрезками и дугами. Концы считаются совпадающими с точностью 0,01 мм.
    /// </summary>
    public static class FlatContours
    {
        public const double JoinTolerance = 0.01;

        /// <summary>
        /// Контуры резки (слои контура и отверстий). Самый большой замкнутый помечен как наружный.
        /// Считаются один раз на чертёж (FlatDrawing.Contours).
        /// </summary>
        public static IReadOnlyList<Contour> Build(FlatDrawing drawing) =>
            drawing == null || drawing.IsEmpty ? Array.Empty<Contour>() : drawing.Contours;

        internal static IReadOnlyList<Contour> Compute(IReadOnlyList<Polyline2> polylines)
        {
            var result = new List<Contour>();
            var profile = polylines.Where(p => p.Kind == LayerKind.Profile && p.Points.Count >= 2).ToList();
            foreach (var p in profile.Where(p => p.Closed))
                result.Add(new Contour(p.Points.ToList(), true));

            var pieces = profile.Where(p => !p.Closed).Select(p => p.Points.ToList()).ToList();
            var used = new bool[pieces.Count];
            var ends = new EndIndex(pieces);
            for (int i = 0; i < pieces.Count; i++)
            {
                if (used[i])
                    continue;
                used[i] = true;
                var chain = new List<Point2>(pieces[i]);

                while (!Near(chain[0], chain[chain.Count - 1]))
                {
                    var end = chain[chain.Count - 1];
                    var start = chain[0];
                    // Первый по порядку файла неиспользованный кусок, который стыкуется с любым концом цепочки.
                    int j = Math.Min(ends.FirstNear(end, used), ends.FirstNear(start, used));
                    if (j == int.MaxValue)
                        break;
                    var piece = pieces[j];
                    if (Near(end, piece[0]))
                        chain.AddRange(piece.Skip(1));
                    else if (Near(end, piece[piece.Count - 1]))
                        chain.AddRange(Enumerable.Reverse(piece).Skip(1));
                    else if (Near(start, piece[piece.Count - 1]))
                        chain.InsertRange(0, piece.Take(piece.Count - 1));
                    else
                        chain.InsertRange(0, Enumerable.Reverse(piece).Take(piece.Count - 1));
                    used[j] = true;
                }

                bool closed = chain.Count > 2 && Near(chain[0], chain[chain.Count - 1]);
                if (closed)
                    chain.RemoveAt(chain.Count - 1); // последняя точка совпадает с первой
                result.Add(new Contour(chain, closed));
            }

            var outer = result.Where(c => c.Closed).OrderByDescending(c => c.Area).FirstOrDefault();
            if (outer != null)
                outer.IsOuter = true;
            return result;
        }

        /// <summary>
        /// Концы кусков по ячейкам сетки со стороной JoinTolerance: совпадающий конец — в той же или соседней ячейке.
        /// Сшивка без перебора всех кусков на каждом шаге (в развертке бывают тысячи отрезков и дуг).
        /// </summary>
        private sealed class EndIndex
        {
            private readonly List<List<Point2>> _pieces;
            private readonly Dictionary<(long, long), List<int>> _cells = new Dictionary<(long, long), List<int>>();

            public EndIndex(List<List<Point2>> pieces)
            {
                _pieces = pieces;
                for (int j = 0; j < pieces.Count; j++)
                {
                    Add(pieces[j][0], j);
                    Add(pieces[j][pieces[j].Count - 1], j);
                }
            }

            private static (long, long) Cell(Point2 p) =>
                ((long)Math.Floor(p.X / JoinTolerance), (long)Math.Floor(p.Y / JoinTolerance));

            private void Add(Point2 p, int j)
            {
                var key = Cell(p);
                if (!_cells.TryGetValue(key, out var list))
                    _cells[key] = list = new List<int>(2);
                if (list.Count == 0 || list[list.Count - 1] != j)
                    list.Add(j);
            }

            /// <summary>Наименьший номер неиспользованного куска с концом рядом с p; int.MaxValue — нет такого.</summary>
            public int FirstNear(Point2 p, bool[] used)
            {
                int best = int.MaxValue;
                var (cx, cy) = Cell(p);
                for (long x = cx - 1; x <= cx + 1; x++)
                {
                    for (long y = cy - 1; y <= cy + 1; y++)
                    {
                        if (!_cells.TryGetValue((x, y), out var list))
                            continue;
                        foreach (int j in list)
                        {
                            if (j >= best || used[j])
                                continue;
                            var piece = _pieces[j];
                            if (Near(p, piece[0]) || Near(p, piece[piece.Count - 1]))
                                best = j;
                        }
                    }
                }
                return best;
            }
        }

        private static bool Near(Point2 a, Point2 b) => Math.Abs(a.X - b.X) <= JoinTolerance && Math.Abs(a.Y - b.Y) <= JoinTolerance;
    }
}

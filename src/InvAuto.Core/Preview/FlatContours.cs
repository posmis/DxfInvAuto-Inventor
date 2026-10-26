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

        /// <summary>Контуры резки (слои контура и отверстий). Самый большой замкнутый помечен как наружный.</summary>
        public static IReadOnlyList<Contour> Build(FlatDrawing drawing)
        {
            var result = new List<Contour>();
            if (drawing == null || drawing.IsEmpty)
                return result;

            var profile = drawing.Polylines.Where(p => p.Kind == LayerKind.Profile && p.Points.Count >= 2).ToList();
            foreach (var p in profile.Where(p => p.Closed))
                result.Add(new Contour(p.Points.ToList(), true));

            var pieces = profile.Where(p => !p.Closed).Select(p => p.Points.ToList()).ToList();
            var used = new bool[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
            {
                if (used[i])
                    continue;
                used[i] = true;
                var chain = new List<Point2>(pieces[i]);

                bool extended = true;
                while (extended && !Near(chain[0], chain[chain.Count - 1]))
                {
                    extended = false;
                    for (int j = 0; j < pieces.Count; j++)
                    {
                        if (used[j])
                            continue;
                        var piece = pieces[j];
                        var end = chain[chain.Count - 1];
                        var start = chain[0];
                        if (Near(end, piece[0]))
                            chain.AddRange(piece.Skip(1));
                        else if (Near(end, piece[piece.Count - 1]))
                            chain.AddRange(Enumerable.Reverse(piece).Skip(1));
                        else if (Near(start, piece[piece.Count - 1]))
                            chain.InsertRange(0, piece.Take(piece.Count - 1));
                        else if (Near(start, piece[0]))
                            chain.InsertRange(0, Enumerable.Reverse(piece).Take(piece.Count - 1));
                        else
                            continue;
                        used[j] = true;
                        extended = true;
                        break;
                    }
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

        private static bool Near(Point2 a, Point2 b) => Math.Abs(a.X - b.X) <= JoinTolerance && Math.Abs(a.Y - b.Y) <= JoinTolerance;
    }
}

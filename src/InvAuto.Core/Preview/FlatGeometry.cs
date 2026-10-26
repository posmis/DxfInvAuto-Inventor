using System;
using System.Collections.Generic;

namespace InvAuto.Core.Preview
{
    /// <summary>Прямой отрезок чертежа (из LINE или прямого участка полилинии), мм.</summary>
    public readonly struct Segment2
    {
        public Segment2(Point2 a, Point2 b, string layer)
        {
            A = a;
            B = b;
            Layer = layer ?? string.Empty;
        }

        public Point2 A { get; }
        public Point2 B { get; }
        public string Layer { get; }
        public double Length => Geometry2.Distance(A, B);
        public Point2 Mid => new Point2((A.X + B.X) / 2, (A.Y + B.Y) / 2);

        /// <summary>Ближайшая к p точка отрезка.</summary>
        public Point2 Nearest(Point2 p)
        {
            double dx = B.X - A.X, dy = B.Y - A.Y, len2 = dx * dx + dy * dy;
            if (len2 < 1e-24)
                return A;
            double t = Math.Max(0, Math.Min(1, ((p.X - A.X) * dx + (p.Y - A.Y) * dy) / len2));
            return new Point2(A.X + dx * t, A.Y + dy * t);
        }
    }

    /// <summary>Дуга или окружность чертежа: центр, радиус, начальный угол и угол раствора (со знаком, радианы).</summary>
    public sealed class Arc2
    {
        public Arc2(Point2 center, double radius, double startAngle, double sweep, string layer)
        {
            Center = center;
            Radius = radius;
            StartAngle = startAngle;
            Sweep = sweep;
            Layer = layer ?? string.Empty;
        }

        public Point2 Center { get; }
        public double Radius { get; }
        public double StartAngle { get; }

        /// <summary>Положительный — против часовой стрелки; ±2π — окружность.</summary>
        public double Sweep { get; }

        public string Layer { get; }
        public bool IsCircle => Math.Abs(Sweep) >= 2 * Math.PI - 1e-9;
        public Point2 Start => PointAt(StartAngle);
        public Point2 End => PointAt(StartAngle + Sweep);
        public Point2 Mid => PointAt(StartAngle + Sweep / 2);

        public Point2 PointAt(double angle) =>
            new Point2(Center.X + Radius * Math.Cos(angle), Center.Y + Radius * Math.Sin(angle));

        /// <summary>Ближайшая к p точка дуги.</summary>
        public Point2 Nearest(Point2 p)
        {
            double angle = Math.Atan2(p.Y - Center.Y, p.X - Center.X);
            if (IsCircle || Contains(angle))
                return PointAt(angle);
            return Geometry2.Distance(p, Start) <= Geometry2.Distance(p, End) ? Start : End;
        }

        private bool Contains(double angle)
        {
            double from = Sweep >= 0 ? StartAngle : StartAngle + Sweep;
            double offset = (angle - from) % (2 * Math.PI);
            if (offset < 0)
                offset += 2 * Math.PI;
            return offset <= Math.Abs(Sweep) + 1e-12;
        }
    }

    /// <summary>
    /// Точная геометрия развертки для измерений: отрезки и дуги такими, какими они записаны в DXF
    /// (полилинии в FlatDrawing — для рисования, дуги в них разбиты на короткие участки).
    /// </summary>
    public sealed class FlatGeometry
    {
        public List<Segment2> Lines { get; } = new List<Segment2>();
        public List<Arc2> Arcs { get; } = new List<Arc2>();

        /// <summary>Концы кривых, которые не являются ни отрезком, ни дугой (сплайны, эллипсы).</summary>
        public List<Point2> Points { get; } = new List<Point2>();

        /// <summary>Для чертежей, собранных не из DXF: каждая пара соседних точек полилинии — отрезок.</summary>
        public static FlatGeometry FromPolylines(IEnumerable<Polyline2> polylines)
        {
            var geometry = new FlatGeometry();
            foreach (var polyline in polylines)
            {
                var points = polyline.Points;
                for (int i = 0; i + 1 < points.Count; i++)
                    geometry.Lines.Add(new Segment2(points[i], points[i + 1], polyline.Layer));
                if (polyline.Closed && points.Count > 2)
                    geometry.Lines.Add(new Segment2(points[points.Count - 1], points[0], polyline.Layer));
            }
            return geometry;
        }
    }

    public static class Geometry2
    {
        public static double Distance(Point2 a, Point2 b)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y;
            return Math.Sqrt(dx * dx + dy * dy);
        }

        /// <summary>Расстояние между отрезками AB и CD (0 — пересекаются).</summary>
        public static double SegmentDistance(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            if (Intersect(a, b, c, d))
                return 0;
            return Math.Min(
                Math.Min(Distance(a, new Segment2(c, d, null).Nearest(a)), Distance(b, new Segment2(c, d, null).Nearest(b))),
                Math.Min(Distance(c, new Segment2(a, b, null).Nearest(c)), Distance(d, new Segment2(a, b, null).Nearest(d))));
        }

        private static bool Intersect(Point2 a, Point2 b, Point2 c, Point2 d)
        {
            double d1 = Cross(c, d, a), d2 = Cross(c, d, b), d3 = Cross(a, b, c), d4 = Cross(a, b, d);
            return ((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0));
        }

        private static double Cross(Point2 o, Point2 a, Point2 b) => (a.X - o.X) * (b.Y - o.Y) - (a.Y - o.Y) * (b.X - o.X);
    }
}

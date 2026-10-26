using System;
using System.Globalization;
using InvAuto.Core.Preview;

namespace InvAuto.Core.Measuring
{
    /// <summary>Результат измерения на развертке.</summary>
    public abstract class Measurement
    {
        /// <summary>Порядковый номер в списке измерений (0 — ещё не добавлено, предпросмотр).</summary>
        public int Number { get; internal set; }

        /// <summary>Значение коротко — подпись на чертеже.</summary>
        public abstract string Label { get; }

        /// <summary>Полное описание — строка списка измерений.</summary>
        public abstract string Text { get; }

        internal static string Mm(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

        internal static string Deg(double value) => value.ToString("0.##", CultureInfo.CurrentCulture) + "°";
    }

    /// <summary>Расстояние между двумя точками.</summary>
    public sealed class DistanceMeasurement : Measurement
    {
        public DistanceMeasurement(SnapResult from, SnapResult to)
        {
            From = from;
            To = to;
        }

        public SnapResult From { get; }
        public SnapResult To { get; }
        public double Distance => Geometry2.Distance(From.Point, To.Point);
        public double Dx => Math.Abs(To.Point.X - From.Point.X);
        public double Dy => Math.Abs(To.Point.Y - From.Point.Y);

        public override string Label => Mm(Distance) + " мм";

        public override string Text =>
            $"Расстояние {Mm(Distance)} мм · ΔX {Mm(Dx)} · ΔY {Mm(Dy)}";
    }

    /// <summary>
    /// Угол между двумя отрезками. Вершина — пересечение их продолжений; стороны угла идут от вершины
    /// к тем точкам отрезков, по которым щёлкнули, поэтому можно измерить и угол, и смежный с ним.
    /// Параллельные отрезки — угол 0° и расстояние между ними.
    /// </summary>
    public sealed class AngleMeasurement : Measurement
    {
        /// <summary>Отрезки с расхождением меньше этого считаются параллельными, градусы.</summary>
        public const double ParallelToleranceDegrees = 0.01;

        private AngleMeasurement(Segment2 first, Segment2 second)
        {
            First = first;
            Second = second;
        }

        public Segment2 First { get; }
        public Segment2 Second { get; }

        public bool IsParallel { get; private set; }

        /// <summary>Вершина угла (для непараллельных).</summary>
        public Point2 Vertex { get; private set; }

        /// <summary>Единичные направления сторон угла от вершины.</summary>
        public Point2 Direction1 { get; private set; }
        public Point2 Direction2 { get; private set; }

        /// <summary>Угол между сторонами, 0…180°.</summary>
        public double Degrees { get; private set; }

        public double Supplement => 180 - Degrees;

        /// <summary>Для параллельных: расстояние между прямыми и общий перпендикуляр.</summary>
        public double ParallelDistance { get; private set; }
        public Point2 PerpendicularFrom { get; private set; }
        public Point2 PerpendicularTo { get; private set; }

        public override string Label => IsParallel ? "∥ " + Mm(ParallelDistance) + " мм" : Deg(Degrees);

        public override string Text => IsParallel
            ? $"Отрезки параллельны · расстояние {Mm(ParallelDistance)} мм"
            : $"Угол {Deg(Degrees)} · смежный {Deg(Supplement)}";

        public static AngleMeasurement Between(SegmentPick first, SegmentPick second)
        {
            var result = new AngleMeasurement(first.Segment, second.Segment);
            var d1 = Unit(first.Segment);
            var d2 = Unit(second.Segment);
            double cross = Cross(d1, d2);

            if (Math.Abs(cross) < Math.Sin(ParallelToleranceDegrees * Math.PI / 180))
            {
                // Перпендикуляр из точки щелчка на первом отрезке на прямую второго.
                var a2 = second.Segment.A;
                double t = (first.Point.X - a2.X) * d2.X + (first.Point.Y - a2.Y) * d2.Y;
                var foot = new Point2(a2.X + d2.X * t, a2.Y + d2.Y * t);
                result.IsParallel = true;
                result.PerpendicularFrom = first.Point;
                result.PerpendicularTo = foot;
                result.ParallelDistance = Geometry2.Distance(first.Point, foot);
                result.Direction1 = d1;
                result.Direction2 = d2;
                return result;
            }

            var a1 = first.Segment.A;
            var w = new Point2(second.Segment.A.X - a1.X, second.Segment.A.Y - a1.Y);
            double s = Cross(w, d2) / cross;
            var vertex = new Point2(a1.X + d1.X * s, a1.Y + d1.Y * s);

            result.Vertex = vertex;
            result.Direction1 = Side(first, vertex);
            result.Direction2 = Side(second, vertex);
            double dot = result.Direction1.X * result.Direction2.X + result.Direction1.Y * result.Direction2.Y;
            result.Degrees = Math.Acos(Math.Max(-1, Math.Min(1, dot))) * 180 / Math.PI;
            return result;
        }

        /// <summary>Сторона угла: от вершины к точке щелчка; щёлкнули в саму вершину — к дальнему концу отрезка.</summary>
        private static Point2 Side(SegmentPick pick, Point2 vertex)
        {
            var toward = pick.Point;
            double eps = Math.Max(1e-6, pick.Segment.Length * 1e-6);
            if (Geometry2.Distance(toward, vertex) < eps)
                toward = Geometry2.Distance(pick.Segment.A, vertex) >= Geometry2.Distance(pick.Segment.B, vertex)
                    ? pick.Segment.A
                    : pick.Segment.B;
            double dx = toward.X - vertex.X, dy = toward.Y - vertex.Y, len = Math.Sqrt(dx * dx + dy * dy);
            return new Point2(dx / len, dy / len);
        }

        private static Point2 Unit(Segment2 s)
        {
            double len = s.Length;
            return new Point2((s.B.X - s.A.X) / len, (s.B.Y - s.A.Y) / len);
        }

        private static double Cross(Point2 a, Point2 b) => a.X * b.Y - a.Y * b.X;
    }
}

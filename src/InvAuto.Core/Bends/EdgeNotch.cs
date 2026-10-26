using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.Bends
{
    /// <summary>Участок пути выемки: от предыдущей точки до To — отрезком или дугой.</summary>
    public sealed class NotchPiece
    {
        public NotchPiece(double toX, double toY)
        {
            ToX = toX;
            ToY = toY;
        }

        public NotchPiece(double toX, double toY, double centerX, double centerY, double radius, bool counterClockwise)
            : this(toX, toY)
        {
            IsArc = true;
            CenterX = centerX;
            CenterY = centerY;
            Radius = radius;
            CounterClockwise = counterClockwise;
        }

        public double ToX { get; }
        public double ToY { get; }
        public bool IsArc { get; }
        public double CenterX { get; }
        public double CenterY { get; }
        public double Radius { get; }

        /// <summary>Дуга против часовой стрелки (в координатах DXF, ось Y вверх).</summary>
        public bool CounterClockwise { get; }
    }

    /// <summary>
    /// Выемка в прямом ребре наружного контура: законцовка «вычитается» из детали. Ребро A→B пересекается
    /// формой в точках P1 и P2 (по ходу ребра), между ними контур идёт по той части формы, что внутри детали:
    /// у окружности — дугой, у квадратов — по вершинам внутри детали.
    /// </summary>
    public sealed class EdgeNotch
    {
        private const double Eps = 1e-7;

        private EdgeNotch(double t1, double t2, double p1x, double p1y, double p2x, double p2y, IReadOnlyList<NotchPiece> path)
        {
            T1 = t1;
            T2 = t2;
            P1X = p1x;
            P1Y = p1y;
            P2X = p2x;
            P2Y = p2y;
            Path = path;
        }

        /// <summary>Где выемка на ребре: доли длины ребра от A, T1 &lt; T2.</summary>
        public double T1 { get; }
        public double T2 { get; }

        public double P1X { get; }
        public double P1Y { get; }
        public double P2X { get; }
        public double P2Y { get; }

        /// <summary>Путь от P1 до P2 по границе формы внутри детали; последний участок заканчивается в P2.</summary>
        public IReadOnlyList<NotchPiece> Path { get; }

        /// <summary>
        /// Выемка формы outline в ребре A→B. inward — направление внутрь детали (единичный вектор, обычно вдоль
        /// линии гиба от её конца). null — форма не пересекает ребро дважды (сдвинута внутрь, касается, у угла).
        /// </summary>
        public static EdgeNotch Find(double ax, double ay, double bx, double by, EndOutline outline, double inwardX, double inwardY)
        {
            if (outline == null)
                return null;
            double dx = bx - ax, dy = by - ay, len2 = dx * dx + dy * dy;
            if (len2 < 1e-18)
                return null;

            // Сторона «внутрь»: знак векторного произведения ребра и направления внутрь.
            double side = Math.Sign(dx * inwardY - dy * inwardX);
            if (side == 0)
                return null; // направление внутрь вдоль ребра — линия гиба не выходит на это ребро

            return outline.IsCircle
                ? Circle(ax, ay, dx, dy, len2, outline, inwardX, inwardY)
                : Polygon(ax, ay, dx, dy, len2, outline, side);
        }

        private static EdgeNotch Circle(double ax, double ay, double dx, double dy, double len2, EndOutline o, double ix, double iy)
        {
            // |A + t·D − C|² = r²
            double fx = ax - o.CenterX, fy = ay - o.CenterY;
            double b = 2 * (fx * dx + fy * dy), c = fx * fx + fy * fy - o.Radius * o.Radius;
            double disc = b * b - 4 * len2 * c;
            if (disc <= Eps)
                return null;
            double sq = Math.Sqrt(disc);
            double t1 = (-b - sq) / (2 * len2), t2 = (-b + sq) / (2 * len2);
            if (t1 <= Eps || t2 >= 1 - Eps)
                return null; // выходит за ребро — у угла детали, выемку не строим

            double p1x = ax + dx * t1, p1y = ay + dy * t1, p2x = ax + dx * t2, p2y = ay + dy * t2;

            // Дуга от P1 к P2 через самую внутреннюю точку окружности.
            double a1 = Math.Atan2(p1y - o.CenterY, p1x - o.CenterX);
            double a2 = Math.Atan2(p2y - o.CenterY, p2x - o.CenterX);
            double am = Math.Atan2(iy, ix);
            bool ccw = Ccw(a1, am) < Ccw(a1, a2);
            return new EdgeNotch(t1, t2, p1x, p1y, p2x, p2y,
                new[] { new NotchPiece(p2x, p2y, o.CenterX, o.CenterY, o.Radius, ccw) });
        }

        private static EdgeNotch Polygon(double ax, double ay, double dx, double dy, double len2, EndOutline o, double side)
        {
            var v = o.Vertices;
            int n = v.Count;
            double len = Math.Sqrt(len2);
            double Dist(double x, double y) => ((x - ax) * dy - (y - ay) * dx) / len * -side; // > 0 — внутри детали

            var inside = Enumerable.Range(0, n).Where(i => Dist(v[i].X, v[i].Y) > Eps).ToList();
            if (inside.Count == 0 || inside.Count == n)
                return null;

            // Вершины внутри у выпуклой формы идут подряд (по кругу): находим начало этой цепочки.
            int first = inside.First(i => !inside.Contains((i - 1 + n) % n));
            var chain = new List<int>();
            for (int k = 0, i = first; k < inside.Count; k++, i = (i + 1) % n)
                chain.Add(i);

            // Точки входа и выхода — пересечения рёбер формы с прямой ребра контура.
            var enter = Cross(v[(first - 1 + n) % n], v[first], ax, ay, dx, dy, len2);
            var leave = Cross(v[chain[chain.Count - 1]], v[(chain[chain.Count - 1] + 1) % n], ax, ay, dx, dy, len2);
            if (enter == null || leave == null)
                return null;

            var (tIn, inX, inY) = enter.Value;
            var (tOut, outX, outY) = leave.Value;
            if (Math.Min(tIn, tOut) <= Eps || Math.Max(tIn, tOut) >= 1 - Eps || Math.Abs(tIn - tOut) < Eps)
                return null;

            // По ходу ребра первой должна идти точка с меньшим t: иначе цепочку обходим в обратную сторону.
            if (tIn > tOut)
            {
                chain.Reverse();
                (tIn, inX, inY, tOut, outX, outY) = (tOut, outX, outY, tIn, inX, inY);
            }

            var path = chain.Select(i => new NotchPiece(v[i].X, v[i].Y)).ToList();
            path.Add(new NotchPiece(outX, outY));
            return new EdgeNotch(tIn, tOut, inX, inY, outX, outY, path);
        }

        /// <summary>Пересечение отрезка формы p→q с прямой ребра: (t вдоль ребра, точка).</summary>
        private static (double T, double X, double Y)? Cross(EndVertex p, EndVertex q, double ax, double ay, double dx, double dy, double len2)
        {
            double ex = q.X - p.X, ey = q.Y - p.Y;
            double denom = dx * ey - dy * ex;
            if (Math.Abs(denom) < 1e-15)
                return null;
            // A + t·D = P + s·E
            double t = ((p.X - ax) * ey - (p.Y - ay) * ex) / denom;
            double s = ((p.X - ax) * dy - (p.Y - ay) * dx) / denom;
            if (s < -Eps || s > 1 + Eps)
                return null;
            return (t, ax + dx * t, ay + dy * t);
        }

        /// <summary>Угол поворота против часовой стрелки от a до b, [0; 2π).</summary>
        internal static double Ccw(double a, double b)
        {
            double d = (b - a) % (2 * Math.PI);
            return d < 0 ? d + 2 * Math.PI : d;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Clipper2Lib;
using InvAuto.Core.Preview;

namespace InvAuto.Core.Bends
{
    /// <summary>
    /// Законцовки, которые нельзя сделать простой выемкой в одном прямом ребре: перекрываются между собой
    /// (линии гиба сходятся в угол) или задевают несколько рёбер, скругление, рельеф сложной формы.
    /// Такие законцовки объединяются и вычитаются из детали (Clipper2): наружный контур переписывается
    /// одним замкнутым контуром, всё, что попало внутрь законцовок, исчезает. Дуги исходного контура и
    /// окружностей восстанавливаются дугами. Перекрывающиеся законцовки внутри детали — один общий вырез.
    /// </summary>
    public static partial class DxfBendProcessor
    {
        /// <summary>Точность разбиения дуг для булевых операций, мм.</summary>
        private const double ChordTolerance = 0.001;

        /// <summary>Знаков после запятой в булевых операциях (Clipper2): 1e-6 мм.</summary>
        private const int ClipPrecision = 6;

        /// <summary>Решает, как вырезать законцовки каждого блока (раздела) файла.</summary>
        private static void PlanEnds(List<Entity> entities, List<EndWork> ends, List<Polyline2> endFeatures)
        {
            foreach (var byContainer in ends.GroupBy(e => e.Owner.Container))
            {
                int container = byContainer.Key;
                var list = byContainer.ToList();
                var curves = entities.Where(x => x.Container == container && x.Curve != null).ToList();
                var groups = OverlapGroups(list);

                // Простые случаи — выемка в одном прямом ребре; остальные, задевающие контур, — в вычитание.
                var cut = new List<EndWork>();
                foreach (var g in groups)
                {
                    if (g.Count == 1)
                    {
                        g[0].Notch = MergeIntoOuter(entities, container, g[0].Outline, g[0].InX, g[0].InY);
                        if (g[0].Notch != null)
                            continue;
                    }
                    if (g.Any(e => TouchesOuter(curves, e.Outline)))
                        cut.AddRange(g);
                }

                if (cut.Count > 0)
                {
                    // Контур переписывается целиком — простые выемки тоже уходят в общее вычитание.
                    var notched = list.Where(e => e.Notch != null).ToList();
                    foreach (var e in curves.Where(x => x.Outer != null))
                        e.Outer.Notches.Clear();
                    foreach (var e in notched)
                        e.Notch = null;
                    var all = cut.Concat(notched).ToList();
                    if (CutCorner(entities, curves, all, endFeatures))
                    {
                        foreach (var e in all)
                            e.Done = true;
                    }
                    else
                    {
                        // Не получилось (контур не замкнут и т. п.) — как раньше: выемки, где можно, иначе вырезы.
                        foreach (var e in notched)
                            e.Notch = MergeIntoOuter(entities, container, e.Outline, e.InX, e.InY);
                    }
                }

                foreach (var e in list.Where(x => x.Notch != null))
                {
                    e.Done = true;
                    endFeatures.Add(EndFeature(e.Outline, e.Notch));
                }
                foreach (var g in groups)
                {
                    var rest = g.Where(e => !e.Done).ToList();
                    if (rest.Count > 0)
                        AddHoles(rest, endFeatures);
                }
            }
        }

        /// <summary>Отдельные вырезы: одна законцовка — как есть, перекрывающиеся — один общий контур.</summary>
        private static void AddHoles(List<EndWork> group, List<Polyline2> endFeatures)
        {
            if (group.Count == 1)
            {
                group[0].Bend.SeparateEnds.Add(group[0].Outline);
                endFeatures.Add(EndFeature(group[0].Outline, null));
                return;
            }
            var union = Clipper.Union(new PathsD(group.Select(e => e.Poly)), new PathsD(), FillRule.NonZero, ClipPrecision);
            var circles = group.Where(e => e.Outline.IsCircle).Select(e => KnownCircle.Of(e.Outline)).ToList();
            foreach (var path in union)
            {
                group[0].Bend.UnionHoles.Add(Restore(path, circles));
                endFeatures.Add(new Polyline2(EndFeatureLayer, path.Select(p => new Point2(p.x, p.y)).ToList(), closed: true));
            }
        }

        /// <summary>Группы законцовок, перекрывающих друг друга (по цепочке).</summary>
        private static List<List<EndWork>> OverlapGroups(List<EndWork> ends)
        {
            var parent = Enumerable.Range(0, ends.Count).ToArray();
            int Find(int i) => parent[i] == i ? i : parent[i] = Find(parent[i]);
            for (int i = 0; i < ends.Count; i++)
                for (int j = i + 1; j < ends.Count; j++)
                    if (Overlap(ends[i].Poly, ends[j].Poly))
                        parent[Find(i)] = Find(j);
            return Enumerable.Range(0, ends.Count).GroupBy(Find).Select(g => g.Select(i => ends[i]).ToList()).ToList();
        }

        private static bool Overlap(PathD a, PathD b)
        {
            var ba = Clipper.GetBounds(a);
            var bb = Clipper.GetBounds(b);
            if (ba.right < bb.left || bb.right < ba.left || ba.bottom < bb.top || bb.bottom < ba.top)
                return false;
            return Math.Abs(Clipper.Area(Clipper.Intersect(new PathsD { a }, new PathsD { b }, FillRule.NonZero, ClipPrecision))) > 1e-6;
        }

        /// <summary>Законцовка заходит за наружный контур (задевает какой-либо его элемент).</summary>
        private static bool TouchesOuter(List<Entity> curves, EndOutline outline) =>
            curves.Any(c => Touches(c.Curve.Points, outline));

        /// <summary>
        /// Вычитает объединение законцовок из детали. Наружный контур блока должен сшиваться в один замкнутый.
        /// Новый контур записывается на месте первого элемента контура, остальные элементы удаляются.
        /// </summary>
        private static bool CutCorner(List<Entity> entities, List<Entity> curves, List<EndWork> ends, List<Polyline2> endFeatures)
        {
            if (curves.Count == 0)
                return false;
            var pieces = curves.Select(c => new Polyline2("OUTER", c.Curve.Points.Select(p => new Point2(p[0], p[1])).ToList(), c.Curve.Closed)).ToList();
            var contours = FlatContours.Compute(pieces);
            if (contours.Count != 1 || !contours[0].Closed)
                return false; // на слое контура не один замкнутый контур — переписывать небезопасно

            var subject = Positive(new PathD(contours[0].Points.Select(p => new PointD(p.X, p.Y))));
            var clip = Clipper.Union(new PathsD(ends.Select(e => e.Poly)), new PathsD(), FillRule.NonZero, ClipPrecision);
            var result = Clipper.Difference(new PathsD { subject }, clip, FillRule.NonZero, ClipPrecision);
            if (result.Count == 0)
                return false;

            var circles = curves.SelectMany(c => c.Curve.Circles)
                .Concat(ends.Where(e => e.Outline.IsCircle).Select(e => KnownCircle.Of(e.Outline)))
                .ToList();
            var first = curves[0];
            string holeLayer = ends[0].Bend.Options.EndLayer;
            first.NewContours = result
                .Select(path => (Restore(path, circles), Clipper.Area(path) < 0 ? holeLayer : (string)null))
                .ToList();
            foreach (var c in curves)
                c.Removed = true;

            // Линии гиба — до границы общего выреза (внутри пустота).
            foreach (var e in ends)
            {
                double length = Distance(e.Bend.Start, e.Bend.Finish);
                var point = e.K == 0 ? e.Bend.Start : e.Bend.Finish;
                double trim = Math.Min(ExitDistance(clip, point[0], point[1], e.InX, e.InY, length), length / 2);
                if (e.K == 0)
                    e.Bend.TrimStart = Math.Max(e.Bend.TrimStart, trim);
                else
                    e.Bend.TrimEnd = Math.Max(e.Bend.TrimEnd, trim);
            }

            // Для показа — участки нового контура, идущие по границе выреза.
            foreach (var path in result)
                endFeatures.AddRange(OnBoundary(path, clip));
            return true;
        }

        /// <summary>Участки контура path, лежащие на границе clip, — разомкнутыми линиями.</summary>
        private static IEnumerable<Polyline2> OnBoundary(PathD path, PathsD clip)
        {
            int n = path.Count;
            var on = new bool[n];
            for (int i = 0; i < n; i++)
            {
                var a = path[i];
                var b = path[(i + 1) % n];
                var mid = new Point2((a.x + b.x) / 2, (a.y + b.y) / 2);
                on[i] = clip.Any(c => DistanceToPath(c, mid) < 1e-5);
            }
            int start = Enumerable.Range(0, n).FirstOrDefault(i => !on[i] && on[(i + 1) % n]);
            if (on.All(x => x))
            {
                yield return new Polyline2(EndFeatureLayer, path.Select(p => new Point2(p.x, p.y)).ToList(), closed: true);
                yield break;
            }
            List<Point2> run = null;
            for (int k = 1; k <= n; k++)
            {
                int i = (start + k) % n;
                if (on[i])
                {
                    if (run == null)
                        run = new List<Point2> { new Point2(path[i].x, path[i].y) };
                    var b = path[(i + 1) % n];
                    run.Add(new Point2(b.x, b.y));
                }
                else if (run != null)
                {
                    yield return new Polyline2(EndFeatureLayer, run, closed: false);
                    run = null;
                }
            }
            if (run != null)
                yield return new Polyline2(EndFeatureLayer, run, closed: false);
        }

        private static double DistanceToPath(PathD path, Point2 p)
        {
            double best = double.MaxValue;
            for (int i = 0; i < path.Count; i++)
            {
                var a = path[i];
                var b = path[(i + 1) % path.Count];
                best = Math.Min(best, PointToSegment(new[] { a.x, a.y }, new[] { b.x, b.y }, p.X, p.Y));
            }
            return best;
        }

        /// <summary>
        /// Сколько от конца линии гиба (x; y) по направлению внутрь (ix; iy) лежит внутри выреза: до первого выхода.
        /// Конец не в вырезе — 0.
        /// </summary>
        private static double ExitDistance(PathsD clip, double x, double y, double ix, double iy, double maxLength)
        {
            const double step = 1e-4;
            bool Inside(double d) => InsideAny(clip, x + ix * d, y + iy * d);
            if (!Inside(step))
                return 0;
            var hits = new List<double>();
            foreach (var path in clip)
            {
                for (int i = 0; i < path.Count; i++)
                {
                    var a = path[i];
                    var b = path[(i + 1) % path.Count];
                    double ex = b.x - a.x, ey = b.y - a.y, denom = ix * ey - iy * ex;
                    if (Math.Abs(denom) < 1e-15)
                        continue;
                    double t = ((a.x - x) * ey - (a.y - y) * ex) / denom;
                    double s = ((a.x - x) * iy - (a.y - y) * ix) / denom;
                    if (t > step && t <= maxLength && s >= -1e-9 && s <= 1 + 1e-9)
                        hits.Add(t);
                }
            }
            foreach (double t in hits.OrderBy(t => t))
                if (!Inside(t + step))
                    return t;
            return maxLength;
        }

        private static bool InsideAny(PathsD paths, double x, double y)
        {
            int winding = 0;
            foreach (var path in paths)
                if (Clipper.PointInPolygon(new PointD(x, y), path) == PointInPolygonResult.IsInside)
                    winding += Clipper.Area(path) >= 0 ? 1 : -1;
            return winding != 0;
        }

        private static PathD Positive(PathD path)
        {
            if (Clipper.Area(path) < 0)
                path.Reverse();
            return path;
        }

        /// <summary>Многоугольник законцовки для булевых операций (окружность — с точностью ChordTolerance).</summary>
        private static PathD OutlinePath(EndOutline outline)
        {
            if (!outline.IsCircle)
                return Positive(new PathD(outline.Vertices.Select(v => new PointD(v.X, v.Y))));
            double step = FineStep(outline.Radius);
            int n = Math.Max(8, (int)Math.Ceiling(2 * Math.PI / step));
            var path = new PathD(n);
            for (int k = 0; k < n; k++)
            {
                double a = 2 * Math.PI * k / n;
                path.Add(new PointD(outline.CenterX + outline.Radius * Math.Cos(a), outline.CenterY + outline.Radius * Math.Sin(a)));
            }
            return path;
        }

        /// <summary>Шаг разбиения дуги радиуса r: отклонение хорды не больше ChordTolerance, от 0,25° до 5°.</summary>
        private static double FineStep(double r) =>
            r <= ChordTolerance ? Math.PI / 36 : Math.Max(Math.PI / 720, Math.Min(Math.PI / 36, 2 * Math.Acos(1 - ChordTolerance / r)));

        #region Восстановление дуг

        /// <summary>Вершина контура с выпуклостью участка до следующей вершины.</summary>
        private readonly struct CVertex
        {
            public CVertex(double x, double y, double bulge)
            {
                X = x;
                Y = y;
                Bulge = bulge;
            }

            public double X { get; }
            public double Y { get; }
            public double Bulge { get; }
        }

        /// <summary>Окружность, по которой могут идти участки контура (дуги исходного контура, законцовки).</summary>
        private sealed class KnownCircle
        {
            public KnownCircle(double x, double y, double r, double step)
            {
                X = x;
                Y = y;
                R = r;
                // Самая длинная хорда разбиения (с запасом): длиннее — это не участок дуги, а отрезок.
                MaxChord = 2 * r * Math.Sin(Math.Min(Math.PI, step) / 2) * 1.001 + 1e-7;
            }

            public double X { get; }
            public double Y { get; }
            public double R { get; }
            public double MaxChord { get; }

            public static KnownCircle Of(EndOutline o) => new KnownCircle(o.CenterX, o.CenterY, o.Radius, FineStep(o.Radius));

            public bool On(PointD p) => Math.Abs(Math.Sqrt((p.x - X) * (p.x - X) + (p.y - Y) * (p.y - Y)) - R) <= 2 * ChordTolerance;
        }

        /// <summary>
        /// Многоугольник после булевой операции → вершины с выпуклостями: подряд идущие хорды одной известной
        /// окружности (в одну сторону) собираются в дугу; лишние вершины на прямых убираются.
        /// </summary>
        private static List<CVertex> Restore(PathD path, List<KnownCircle> circles)
        {
            int n = path.Count;
            var arc = new int[n];
            var angle = new double[n];
            for (int i = 0; i < n; i++)
            {
                arc[i] = -1;
                var a = path[i];
                var b = path[(i + 1) % n];
                double chord = Math.Sqrt((b.x - a.x) * (b.x - a.x) + (b.y - a.y) * (b.y - a.y));
                for (int c = 0; c < circles.Count; c++)
                {
                    var k = circles[c];
                    if (chord > k.MaxChord || !k.On(a) || !k.On(b))
                        continue;
                    double ax = a.x - k.X, ay = a.y - k.Y, bx = b.x - k.X, by = b.y - k.Y;
                    double sweep = Math.Atan2(ax * by - ay * bx, ax * bx + ay * by);
                    if (Math.Abs(sweep) < 1e-12)
                        continue;
                    arc[i] = c;
                    angle[i] = sweep;
                    break;
                }
            }

            // Начинаем с границы участков, чтобы дуга не разрезалась на стыке начала и конца обхода.
            int start = 0;
            for (int i = 0; i < n; i++)
            {
                int prev = (i - 1 + n) % n;
                if (arc[i] < 0 || arc[i] != arc[prev] || Math.Sign(angle[i]) != Math.Sign(angle[prev]))
                {
                    start = i;
                    break;
                }
            }

            var result = new List<CVertex>();
            for (int done = 0, i = start; done < n;)
            {
                if (arc[i] < 0)
                {
                    result.Add(new CVertex(path[i].x, path[i].y, 0));
                    i = (i + 1) % n;
                    done++;
                    continue;
                }
                int from = i, circle = arc[i];
                double sweep = 0;
                while (done < n && arc[i] == circle && Math.Sign(angle[i]) == Math.Sign(angle[from]) && Math.Abs(sweep + angle[i]) <= Math.PI + 1e-9)
                {
                    sweep += angle[i];
                    i = (i + 1) % n;
                    done++;
                }
                result.Add(new CVertex(path[from].x, path[from].y, Math.Tan(sweep / 4)));
            }

            // Вершины посередине прямой (между двумя прямыми участками на одной линии) не нужны.
            for (int k = 0; k < result.Count && result.Count > 3;)
            {
                var prev = result[(k - 1 + result.Count) % result.Count];
                var cur = result[k];
                var next = result[(k + 1) % result.Count];
                double ux = cur.X - prev.X, uy = cur.Y - prev.Y, vx = next.X - cur.X, vy = next.Y - cur.Y;
                double lu = Math.Sqrt(ux * ux + uy * uy), lv = Math.Sqrt(vx * vx + vy * vy);
                bool straight = Math.Abs(prev.Bulge) < 1e-12 && Math.Abs(cur.Bulge) < 1e-12;
                if (straight && (lu < 1e-9 || lv < 1e-9 || (Math.Abs(ux * vy - uy * vx) / (lu * lv) < 1e-9 && ux * vx + uy * vy > 0)))
                    result.RemoveAt(k);
                else
                    k++;
            }
            return result;
        }

        #endregion

        #region Запись контура

        /// <summary>
        /// Замкнутый контур: DXF R13+ — одна LWPOLYLINE, R12 — отрезки LINE и дуги ARC. Общие свойства (слой
        /// заменяется на layer, цвет и тип линии) — из шаблона.
        /// </summary>
        private static IEnumerable<Pair> ContourEntities(List<Pair> template, string layer, List<CVertex> v, double z, HandleAllocator handles)
        {
            bool modern = template.Any(p => p.Code == 100 && p.Value.Trim() == "AcDbEntity");
            bool hasHandle = template.Any(p => p.Code == 5);
            string owner = template.FirstOrDefault(p => p.Code == 330).Value?.Trim();
            int width = template[0].CodeText.Length;
            Pair New(int code, string value) => new Pair(code.ToString(CultureInfo.InvariantCulture).PadLeft(width), value);
            var style = template.Where(p => p.Code == 6 || p.Code == 62 || p.Code == 370 || p.Code == 48 || p.Code == 420).ToList();

            IEnumerable<Pair> Header(string type)
            {
                yield return New(0, type);
                if (hasHandle)
                    yield return New(5, handles.Next());
                if (modern && owner != null)
                    yield return New(330, owner);
                if (modern)
                    yield return New(100, "AcDbEntity");
                yield return New(8, layer);
                foreach (var p in style)
                    yield return p;
            }

            var result = new List<Pair>();
            if (modern)
            {
                result.AddRange(Header("LWPOLYLINE"));
                result.Add(New(100, "AcDbPolyline"));
                result.Add(New(90, v.Count.ToString(CultureInfo.InvariantCulture)));
                result.Add(New(70, "1"));
                result.Add(New(43, "0.0"));
                if (Math.Abs(z) > 1e-12)
                    result.Add(New(38, Format(z)));
                foreach (var p in v)
                {
                    result.Add(New(10, Format(p.X)));
                    result.Add(New(20, Format(p.Y)));
                    if (Math.Abs(p.Bulge) > 1e-12)
                        result.Add(New(42, Format(p.Bulge)));
                }
                return result;
            }

            for (int k = 0; k < v.Count; k++)
            {
                var a = v[k];
                var b = v[(k + 1) % v.Count];
                if (Math.Abs(a.Bulge) < 1e-12)
                {
                    result.AddRange(Header("LINE"));
                    result.AddRange(new[]
                    {
                        New(10, Format(a.X)), New(20, Format(a.Y)), New(30, Format(z)),
                        New(11, Format(b.X)), New(21, Format(b.Y)), New(31, Format(z))
                    });
                    continue;
                }
                // Дуга по выпуклости: центр на перпендикуляре к хорде, ARC — всегда против часовой.
                double theta = 4 * Math.Atan(a.Bulge);
                double dx = b.X - a.X, dy = b.Y - a.Y, chord = Math.Sqrt(dx * dx + dy * dy);
                double offset = chord / 2 / Math.Tan(theta / 2);
                double cx = (a.X + b.X) / 2 - dy / chord * offset, cy = (a.Y + b.Y) / 2 + dx / chord * offset;
                double r = Math.Sqrt((a.X - cx) * (a.X - cx) + (a.Y - cy) * (a.Y - cy));
                double a1 = Math.Atan2(a.Y - cy, a.X - cx) * 180 / Math.PI, a2 = Math.Atan2(b.Y - cy, b.X - cx) * 180 / Math.PI;
                result.AddRange(Header("ARC"));
                result.AddRange(new[]
                {
                    New(10, Format(cx)), New(20, Format(cy)), New(30, Format(z)), New(40, Format(r)),
                    New(50, Format(Normalize(theta > 0 ? a1 : a2))), New(51, Format(Normalize(theta > 0 ? a2 : a1)))
                });
            }
            return result;
        }

        #endregion

        /// <summary>Законцовка линии гиба в обработке.</summary>
        private sealed class EndWork
        {
            public EndWork(Entity owner, int k, EndOutline outline, double inX, double inY)
            {
                Owner = owner;
                K = k;
                Outline = outline;
                InX = inX;
                InY = inY;
                Poly = OutlinePath(outline);
            }

            public Entity Owner { get; }
            public BendWork Bend => Owner.Bend;

            /// <summary>0 — у начала линии, 1 — у конца.</summary>
            public int K { get; }

            public EndOutline Outline { get; }

            /// <summary>Направление внутрь детали (вдоль линии от этого конца).</summary>
            public double InX { get; }
            public double InY { get; }

            public PathD Poly { get; }
            public EdgeNotch Notch { get; set; }

            /// <summary>Вырезана (выемкой или вычитанием) — отдельный вырез не нужен.</summary>
            public bool Done { get; set; }
        }

        /// <summary>Элемент наружного контура любого типа ломаной (дуги — с точностью ChordTolerance) и его окружности.</summary>
        private sealed class OuterCurve
        {
            private OuterCurve(List<double[]> points, bool closed, List<KnownCircle> circles)
            {
                Points = points;
                Closed = closed;
                Circles = circles;
            }

            public List<double[]> Points { get; }
            public bool Closed { get; }
            public List<KnownCircle> Circles { get; }

            public static OuterCurve From(List<Pair> e, string type)
            {
                var circles = new List<KnownCircle>();
                switch (type)
                {
                    case "LINE":
                        return new OuterCurve(new List<double[]> { new[] { Number(e, 10), Number(e, 20) }, new[] { Number(e, 11), Number(e, 21) } },
                            false, circles);

                    case "ARC":
                    case "CIRCLE":
                    {
                        double cx = Number(e, 10), cy = Number(e, 20), r = Number(e, 40);
                        if (r <= 0)
                            return null;
                        bool full = type == "CIRCLE";
                        double start = full ? 0 : Number(e, 50) * Math.PI / 180;
                        double sweep = full ? 2 * Math.PI : Number(e, 51) * Math.PI / 180 - start;
                        while (sweep <= 0)
                            sweep += 2 * Math.PI;
                        circles.Add(new KnownCircle(cx, cy, r, FineStep(r)));
                        var points = ArcPoints(cx, cy, r, start, sweep);
                        if (full)
                            points.RemoveAt(points.Count - 1);
                        return new OuterCurve(points, full, circles);
                    }

                    case "LWPOLYLINE":
                    {
                        var vertices = new List<(double X, double Y, double B)>();
                        double x = 0;
                        foreach (var p in e)
                        {
                            if (p.Code == 10)
                                x = ParseNumber(p.Value);
                            else if (p.Code == 20)
                                vertices.Add((x, ParseNumber(p.Value), 0));
                            else if (p.Code == 42 && vertices.Count > 0)
                                vertices[vertices.Count - 1] = (vertices[vertices.Count - 1].X, vertices[vertices.Count - 1].Y, ParseNumber(p.Value));
                        }
                        if (vertices.Count < 2)
                            return null;
                        bool closed = (((int)Number(e, 70)) & 1) != 0;
                        var points = new List<double[]>();
                        int segments = closed ? vertices.Count : vertices.Count - 1;
                        for (int k = 0; k < vertices.Count; k++)
                        {
                            points.Add(new[] { vertices[k].X, vertices[k].Y });
                            if (k >= segments || Math.Abs(vertices[k].B) < 1e-12)
                                continue;
                            var next = vertices[(k + 1) % vertices.Count];
                            if (!BulgeArc(vertices[k].X, vertices[k].Y, next.X, next.Y, vertices[k].B, out double cx, out double cy, out double r, out double a0, out double sweep))
                                continue;
                            circles.Add(new KnownCircle(cx, cy, r, FineStep(r)));
                            var arc = ArcPoints(cx, cy, r, a0, sweep);
                            points.AddRange(arc.Skip(1).Take(arc.Count - 2)); // концы дуги — вершины полилинии
                        }
                        return new OuterCurve(points, closed, circles);
                    }

                    default:
                    {
                        // Сплайны, эллипсы, POLYLINE — разбиение и дуги берутся у DxfReader.
                        var text = new System.Text.StringBuilder("  0\nSECTION\n  2\nENTITIES\n");
                        foreach (var p in e)
                            text.Append(p.CodeText).Append('\n').Append(p.Value).Append('\n');
                        text.Append("  0\nENDSEC\n  0\nEOF\n");
                        var drawing = DxfReader.Read(new StringReader(text.ToString()));
                        var poly = drawing.Polylines.FirstOrDefault(p => p.Points.Count >= 2);
                        if (poly == null)
                            return null;
                        circles.AddRange(drawing.Geometry.Arcs.Select(a => new KnownCircle(a.Center.X, a.Center.Y, a.Radius, Math.PI / 36)));
                        return new OuterCurve(poly.Points.Select(p => new[] { p.X, p.Y }).ToList(), poly.Closed, circles);
                    }
                }
            }

            private static List<double[]> ArcPoints(double cx, double cy, double r, double start, double sweep)
            {
                int steps = Math.Max(1, (int)Math.Ceiling(Math.Abs(sweep) / FineStep(r)));
                var points = new List<double[]>(steps + 1);
                for (int k = 0; k <= steps; k++)
                {
                    double a = start + sweep * k / steps;
                    points.Add(new[] { cx + r * Math.Cos(a), cy + r * Math.Sin(a) });
                }
                return points;
            }

            private static double ParseNumber(string value) =>
                double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
        }

        /// <summary>Дуга по выпуклости участка (x1; y1) → (x2; y2): центр, радиус, начальный угол и угол дуги со знаком.</summary>
        private static bool BulgeArc(double x1, double y1, double x2, double y2, double bulge,
            out double cx, out double cy, out double r, out double start, out double sweep)
        {
            sweep = 4 * Math.Atan(bulge);
            double dx = x2 - x1, dy = y2 - y1, chord = Math.Sqrt(dx * dx + dy * dy);
            cx = cy = r = start = 0;
            if (chord < 1e-12 || Math.Abs(sweep) < 1e-12)
                return false;
            double offset = chord / 2 / Math.Tan(sweep / 2);
            cx = (x1 + x2) / 2 - dy / chord * offset;
            cy = (y1 + y2) / 2 + dx / chord * offset;
            r = Math.Sqrt((x1 - cx) * (x1 - cx) + (y1 - cy) * (y1 - cy));
            start = Math.Atan2(y1 - cy, x1 - cx);
            return true;
        }
    }
}

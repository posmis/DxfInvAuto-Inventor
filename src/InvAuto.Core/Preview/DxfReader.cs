using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

namespace InvAuto.Core.Preview
{
    /// <summary>
    /// Минимальная читалка ASCII DXF для превью разверток. Понимает то, что пишет Inventor
    /// и типичные CAD: LINE, ARC, CIRCLE, ELLIPSE, LWPOLYLINE, POLYLINE/VERTEX, SPLINE и блоки (INSERT).
    /// Дуги и кривые разбиваются на отрезки. Неизвестные объекты пропускаются.
    /// </summary>
    public static class DxfReader
    {
        private const double ArcStepRadians = Math.PI / 36; // 5°
        private const int MaxInsertDepth = 8;

        public static FlatDrawing ReadFile(string path)
        {
            using (var reader = new StreamReader(path))
                return Read(reader);
        }

        public static FlatDrawing Read(TextReader reader)
        {
            var pairs = ReadPairs(reader);
            var blocks = new Dictionary<string, List<Entity>>(StringComparer.OrdinalIgnoreCase);
            var entities = new List<Entity>();

            int i = 0;
            while (i < pairs.Count)
            {
                if (pairs[i].Code == 0 && pairs[i].Value == "SECTION" && i + 1 < pairs.Count && pairs[i + 1].Code == 2)
                {
                    string section = pairs[i + 1].Value;
                    i += 2;
                    if (section == "BLOCKS")
                        ReadBlocks(pairs, ref i, blocks);
                    else if (section == "ENTITIES")
                        entities.AddRange(ReadEntities(pairs, ref i));
                }
                else
                {
                    i++;
                }
            }

            var result = new List<Polyline2>();
            foreach (var entity in entities)
                Convert(entity, Transform.Identity, blocks, result, depth: 0, parentLayer: null);
            return new FlatDrawing(result);
        }

        #region Разбор групп

        private struct Pair
        {
            public Pair(int code, string value)
            {
                Code = code;
                Value = value;
            }

            public int Code { get; }
            public string Value { get; }
        }

        private sealed class Entity
        {
            public Entity(string type) => Type = type;

            public string Type { get; }
            public List<Pair> Codes { get; } = new List<Pair>();
            public List<Entity> Vertices { get; } = new List<Entity>();

            public string Layer => Text(8);

            public string Text(int code) => Codes.FirstOrDefault(c => c.Code == code).Value ?? string.Empty;

            public double Number(int code, double fallback = 0)
            {
                foreach (var c in Codes)
                    if (c.Code == code && double.TryParse(c.Value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                        return v;
                return fallback;
            }

            public int Flags => (int)Number(70);
        }

        private static List<Pair> ReadPairs(TextReader reader)
        {
            var pairs = new List<Pair>();
            string codeLine;
            while ((codeLine = reader.ReadLine()) != null)
            {
                string valueLine = reader.ReadLine();
                if (valueLine == null)
                    break;
                if (int.TryParse(codeLine.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int code))
                    pairs.Add(new Pair(code, valueLine.Trim()));
            }
            return pairs;
        }

        private static Entity ReadEntity(List<Pair> pairs, ref int i)
        {
            var entity = new Entity(pairs[i].Value);
            i++;
            while (i < pairs.Count && pairs[i].Code != 0)
                entity.Codes.Add(pairs[i++]);
            return entity;
        }

        private static List<Entity> ReadEntities(List<Pair> pairs, ref int i)
        {
            var list = new List<Entity>();
            Entity openPolyline = null;

            while (i < pairs.Count)
            {
                var pair = pairs[i];
                if (pair.Code != 0)
                {
                    i++;
                    continue;
                }
                if (pair.Value == "ENDSEC" || pair.Value == "ENDBLK")
                    break;

                var entity = ReadEntity(pairs, ref i);
                switch (entity.Type)
                {
                    case "VERTEX" when openPolyline != null:
                        openPolyline.Vertices.Add(entity);
                        break;
                    case "SEQEND":
                        openPolyline = null;
                        break;
                    default:
                        list.Add(entity);
                        if (entity.Type == "POLYLINE")
                            openPolyline = entity;
                        break;
                }
            }
            return list;
        }

        private static void ReadBlocks(List<Pair> pairs, ref int i, Dictionary<string, List<Entity>> blocks)
        {
            while (i < pairs.Count)
            {
                var pair = pairs[i];
                if (pair.Code == 0 && pair.Value == "ENDSEC")
                    return;

                if (pair.Code == 0 && pair.Value == "BLOCK")
                {
                    var header = ReadEntity(pairs, ref i);
                    var content = ReadEntities(pairs, ref i);

                    // Сдвигаем содержимое на базовую точку блока сразу, чтобы INSERT учитывал только свою точку.
                    double baseX = header.Number(10), baseY = header.Number(20);
                    if (baseX != 0 || baseY != 0)
                    {
                        var wrapper = new Entity("$BASE");
                        wrapper.Codes.Add(new Pair(10, Format(-baseX)));
                        wrapper.Codes.Add(new Pair(20, Format(-baseY)));
                        wrapper.Vertices.AddRange(content);
                        content = new List<Entity> { wrapper };
                    }
                    blocks[header.Text(2)] = content;

                    if (i < pairs.Count && pairs[i].Value == "ENDBLK")
                        ReadEntity(pairs, ref i);
                }
                else
                {
                    i++;
                }
            }
        }

        private static string Format(double value) => value.ToString("R", CultureInfo.InvariantCulture);

        #endregion

        #region Преобразование в полилинии

        private static void Convert(Entity e, Transform t, Dictionary<string, List<Entity>> blocks,
            List<Polyline2> output, int depth, string parentLayer)
        {
            // Объекты блока на слое "0" наследуют слой вставки — так принято в DXF.
            string layer = e.Layer;
            if ((layer.Length == 0 || layer == "0") && parentLayer != null)
                layer = parentLayer;

            void Add(IEnumerable<Point2> points, bool closed)
            {
                var transformed = points.Select(t.Apply).ToList();
                if (transformed.Count >= 2)
                    output.Add(new Polyline2(layer, transformed, closed));
            }

            switch (e.Type)
            {
                case "LINE":
                    Add(new[] { new Point2(e.Number(10), e.Number(20)), new Point2(e.Number(11), e.Number(21)) }, false);
                    break;

                case "CIRCLE":
                    Add(Arc(e.Number(10), e.Number(20), e.Number(40), 0, 2 * Math.PI, includeEnd: false), true);
                    break;

                case "ARC":
                {
                    double start = DegToRad(e.Number(50));
                    double end = DegToRad(e.Number(51));
                    if (end <= start)
                        end += 2 * Math.PI;
                    Add(Arc(e.Number(10), e.Number(20), e.Number(40), start, end - start, includeEnd: true), false);
                    break;
                }

                case "ELLIPSE":
                    Add(Ellipse(e), false);
                    break;

                case "LWPOLYLINE":
                    Add(Bulged(LwVertices(e), closed: (e.Flags & 1) != 0), (e.Flags & 1) != 0);
                    break;

                case "POLYLINE":
                {
                    var vertices = e.Vertices
                        .Where(v => (v.Flags & 16) == 0) // контрольные точки сплайна
                        .Select(v => (new Point2(v.Number(10), v.Number(20)), v.Number(42)))
                        .ToList();
                    bool closed = (e.Flags & 1) != 0;
                    Add(Bulged(vertices, closed), closed);
                    break;
                }

                case "SPLINE":
                {
                    var fit = Points(e, 11, 21);
                    Add(fit.Count >= 2 ? fit : Points(e, 10, 20), (e.Flags & 1) != 0);
                    break;
                }

                case "INSERT":
                {
                    if (depth >= MaxInsertDepth || !blocks.TryGetValue(e.Text(2), out var content))
                        break;
                    var local = Transform.Insert(e.Number(10), e.Number(20), e.Number(41, 1), e.Number(42, 1), DegToRad(e.Number(50)));
                    foreach (var child in content)
                        Convert(child, t.Then(local), blocks, output, depth + 1, layer);
                    break;
                }

                case "$BASE":
                {
                    var shift = Transform.Insert(e.Number(10), e.Number(20), 1, 1, 0);
                    foreach (var child in e.Vertices)
                        Convert(child, t.Then(shift), blocks, output, depth, parentLayer);
                    break;
                }
            }
        }

        private static List<Point2> Points(Entity e, int xCode, int yCode)
        {
            var points = new List<Point2>();
            double? x = null;
            foreach (var c in e.Codes)
            {
                if (c.Code == xCode)
                    x = Parse(c.Value);
                else if (c.Code == yCode && x.HasValue)
                {
                    points.Add(new Point2(x.Value, Parse(c.Value)));
                    x = null;
                }
            }
            return points;
        }

        private static List<(Point2 Point, double Bulge)> LwVertices(Entity e)
        {
            var vertices = new List<(Point2, double)>();
            double x = 0;
            foreach (var c in e.Codes)
            {
                switch (c.Code)
                {
                    case 10:
                        x = Parse(c.Value);
                        break;
                    case 20:
                        vertices.Add((new Point2(x, Parse(c.Value)), 0));
                        break;
                    case 42 when vertices.Count > 0:
                        vertices[vertices.Count - 1] = (vertices[vertices.Count - 1].Item1, Parse(c.Value));
                        break;
                }
            }
            return vertices;
        }

        /// <summary>Полилиния с "выпуклостями" (bulge): ненулевой bulge означает дугу до следующей вершины.</summary>
        private static List<Point2> Bulged(List<(Point2 Point, double Bulge)> vertices, bool closed)
        {
            var points = new List<Point2>();
            int count = vertices.Count;
            int segments = closed ? count : count - 1;

            for (int k = 0; k < count; k++)
            {
                points.Add(vertices[k].Point);
                if (k >= segments || Math.Abs(vertices[k].Bulge) < 1e-9)
                    continue;

                var p1 = vertices[k].Point;
                var p2 = vertices[(k + 1) % count].Point;
                var arc = BulgeArc(p1, p2, vertices[k].Bulge);
                points.AddRange(arc.Skip(1).Take(arc.Count - 2)); // концы уже есть
            }
            return points;
        }

        internal static List<Point2> BulgeArc(Point2 p1, Point2 p2, double bulge)
        {
            double dx = p2.X - p1.X, dy = p2.Y - p1.Y;
            double chord = Math.Sqrt(dx * dx + dy * dy);
            if (chord < 1e-12)
                return new List<Point2> { p1, p2 };

            double sweep = 4 * Math.Atan(bulge);
            double h = chord * (1 - bulge * bulge) / (4 * bulge);
            double cx = (p1.X + p2.X) / 2 - dy / chord * h;
            double cy = (p1.Y + p2.Y) / 2 + dx / chord * h;
            double radius = Math.Sqrt((p1.X - cx) * (p1.X - cx) + (p1.Y - cy) * (p1.Y - cy));
            double start = Math.Atan2(p1.Y - cy, p1.X - cx);

            var points = Arc(cx, cy, radius, start, sweep, includeEnd: true);
            points[points.Count - 1] = p2;
            return points;
        }

        private static List<Point2> Arc(double cx, double cy, double r, double start, double sweep, bool includeEnd)
        {
            int steps = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweep) / ArcStepRadians));
            var points = new List<Point2>(steps + 1);
            int last = includeEnd ? steps : steps - 1;
            for (int k = 0; k <= last; k++)
            {
                double a = start + sweep * k / steps;
                points.Add(new Point2(cx + r * Math.Cos(a), cy + r * Math.Sin(a)));
            }
            return points;
        }

        private static List<Point2> Ellipse(Entity e)
        {
            double cx = e.Number(10), cy = e.Number(20);
            double mx = e.Number(11), my = e.Number(21);
            double ratio = e.Number(40, 1);
            double start = e.Number(41, 0);
            double end = e.Number(42, 2 * Math.PI);
            if (end <= start)
                end += 2 * Math.PI;

            int steps = Math.Max(16, (int)Math.Ceiling((end - start) / ArcStepRadians));
            var points = new List<Point2>(steps + 1);
            for (int k = 0; k <= steps; k++)
            {
                double a = start + (end - start) * k / steps;
                double cos = Math.Cos(a), sin = Math.Sin(a);
                points.Add(new Point2(cx + mx * cos - my * ratio * sin, cy + my * cos + mx * ratio * sin));
            }
            return points;
        }

        private static double Parse(string value) =>
            double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

        private static double DegToRad(double degrees) => degrees * Math.PI / 180;

        #endregion

        /// <summary>Аффинное преобразование 2D: x' = A·x + C·y + E, y' = B·x + D·y + F.</summary>
        private readonly struct Transform
        {
            private readonly double _a, _b, _c, _d, _e, _f;

            private Transform(double a, double b, double c, double d, double e, double f)
            {
                _a = a; _b = b; _c = c; _d = d; _e = e; _f = f;
            }

            public static Transform Identity => new Transform(1, 0, 0, 1, 0, 0);

            /// <summary>Масштаб, затем поворот, затем перенос в точку вставки.</summary>
            public static Transform Insert(double x, double y, double sx, double sy, double rotation)
            {
                double cos = Math.Cos(rotation), sin = Math.Sin(rotation);
                return new Transform(cos * sx, sin * sx, -sin * sy, cos * sy, x, y);
            }

            /// <summary>Сначала применяется inner, затем this.</summary>
            public Transform Then(Transform inner) => new Transform(
                _a * inner._a + _c * inner._b,
                _b * inner._a + _d * inner._b,
                _a * inner._c + _c * inner._d,
                _b * inner._c + _d * inner._d,
                _a * inner._e + _c * inner._f + _e,
                _b * inner._e + _d * inner._f + _f);

            public Point2 Apply(Point2 p) => new Point2(_a * p.X + _c * p.Y + _e, _b * p.X + _d * p.Y + _f);
        }
    }
}

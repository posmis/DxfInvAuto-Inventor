using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InvAuto.Core.Bends
{
    /// <summary>Линия гиба в DXF: где лежит и как её узнать при следующей выгрузке.</summary>
    public sealed class BendLineInfo
    {
        public BendLineInfo(int index, string layer, double x1, double y1, double x2, double y2)
        {
            Index = index;
            Layer = layer;
            X1 = x1; Y1 = y1; X2 = x2; Y2 = y2;
            Length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            Key = MakeKey(layer, x1, y1, x2, y2);
        }

        /// <summary>Номер линии гиба в файле (с 1) — для подписи на развертке.</summary>
        public int Index { get; }

        public string Layer { get; }
        public double X1 { get; }
        public double Y1 { get; }
        public double X2 { get; }
        public double Y2 { get; }
        public double Length { get; }

        /// <summary>Линия гиба вниз (слой IV_BEND_DOWN или переименованный «…DOWN»).</summary>
        public bool IsDown => Layer.IndexOf("DOWN", StringComparison.OrdinalIgnoreCase) >= 0;

        /// <summary>
        /// Ключ для персональной настройки линии: слой и концы с точностью 0,01 мм, без учёта направления.
        /// Пока геометрия детали не меняется, ключ тот же; сдвинулась линия — настройка перестаёт действовать.
        /// </summary>
        public string Key { get; }

        private static string MakeKey(string layer, double x1, double y1, double x2, double y2)
        {
            string a = Point(x1, y1), b = Point(x2, y2);
            if (string.CompareOrdinal(a, b) > 0)
            {
                string t = a;
                a = b;
                b = t;
            }
            return layer.ToUpperInvariant() + "|" + a + "|" + b;
        }

        private static string Point(double x, double y) =>
            Round(x).ToString("0.00", CultureInfo.InvariantCulture) + ";" + Round(y).ToString("0.00", CultureInfo.InvariantCulture);

        // −0,00 и 0,00 — один и тот же ключ
        private static double Round(double v) => Math.Round(v, 2) + 0.0;
    }

    /// <summary>Линия гиба после обработки: какие параметры применены и что получилось.</summary>
    public sealed class ProcessedBendLine
    {
        public ProcessedBendLine(BendLineInfo line, BendLineOptions options, BendLineLayout layout)
        {
            Line = line;
            Options = options;
            Layout = layout;
        }

        public BendLineInfo Line { get; }
        public BendLineOptions Options { get; }
        public BendLineLayout Layout { get; }
    }

    public sealed class BendProcessResult
    {
        public BendProcessResult(string text, IReadOnlyList<ProcessedBendLine> items, IReadOnlyList<Preview.Polyline2> endFeatures = null)
        {
            Text = text;
            Items = items;
            EndFeatures = endFeatures ?? Array.Empty<Preview.Polyline2>();
        }

        /// <summary>Законцовки для показа в окне (отдельным цветом): выемки в контуре и отдельные вырезы.</summary>
        public IReadOnlyList<Preview.Polyline2> EndFeatures { get; }

        /// <summary>DXF после обработки. Тот же экземпляр строки, если ничего не менялось.</summary>
        public string Text { get; }

        /// <summary>Найденные линии гиба в порядке файла и что с каждой сделано.</summary>
        public IReadOnlyList<ProcessedBendLine> Items { get; }

        public IReadOnlyList<BendLineLayout> Lines => Items.Select(i => i.Layout).ToList();
    }

    /// <summary>
    /// Обрабатывает линии гиба в готовом DXF: отрезки (LINE) на слоях гиба заменяются
    /// отрезками по раскладке стратегии (или удаляются). Всё остальное в файле не меняется.
    /// Новые объекты получают уникальные handle, $HANDSEED обновляется — файл остаётся корректным
    /// для AutoCAD и программ раскроя.
    /// </summary>
    public static class DxfBendProcessor
    {
        /// <summary>Слои линий гиба в DXF Inventor (сгиб вверх и вниз).</summary>
        public static readonly IReadOnlyList<string> DefaultLayers = new[] { "IV_BEND", "IV_BEND_DOWN" };

        /// <summary>
        /// Кодировка для чтения и записи DXF: Latin-1 переводит байты в символы один к одному,
        /// поэтому русский текст в любой кодовой странице проходит через обработку без искажений.
        /// </summary>
        public static Encoding FileEncoding { get; } = Encoding.GetEncoding("iso-8859-1");

        /// <summary>Одни параметры для всех линий гиба.</summary>
        public static BendProcessResult Process(string dxf, IEnumerable<string> bendLayers, BendLineOptions options) =>
            Process(dxf, bendLayers, _ => options);

        /// <summary>Линии гиба файла без изменений — для списка линий и подписей на развертке.</summary>
        public static IReadOnlyList<BendLineInfo> FindBendLines(string dxf, IEnumerable<string> bendLayers) =>
            Process(dxf, bendLayers, new BendLineOptions()).Items.Select(i => i.Line).ToList();

        /// <summary>Слои наружного контура: законцовки на его рёбрах становятся выемками в контуре.</summary>
        public static readonly IReadOnlyList<string> DefaultOuterLayers = new[] { "IV_OUTER_PROFILE" };

        /// <summary>Слой, на котором законцовки отдаются для показа в окне (не пишется в DXF).</summary>
        public const string EndFeatureLayer = Preview.Polyline2.EndFeatureLayer;

        /// <summary>
        /// Свои параметры для каждой линии: resolve получает линию (слой, координаты, ключ) и возвращает параметры;
        /// null — оставить линию как есть.
        /// Законцовки, пересекающие прямое ребро наружного контура, становятся выемкой в самом контуре
        /// (контур остаётся одним замкнутым); остальные — отдельными вырезами на своём слое.
        /// </summary>
        public static BendProcessResult Process(string dxf, IEnumerable<string> bendLayers, Func<BendLineInfo, BendLineOptions> resolve,
            IEnumerable<string> outerLayers = null)
        {
            var layers = new HashSet<string>(bendLayers, StringComparer.OrdinalIgnoreCase);
            var outer = new HashSet<string>(outerLayers ?? DefaultOuterLayers, StringComparer.OrdinalIgnoreCase);
            string newline = dxf.Contains("\r\n") ? "\r\n" : "\n";
            var pairs = Parse(dxf);
            var handles = new HandleAllocator(pairs);

            // ===== Проход 1: объекты файла, линии гиба, рёбра наружного контура =====
            var entities = new List<Entity>();
            int container = 0, index = 0;
            while (index < pairs.Count)
            {
                int end = index + 1;
                while (end < pairs.Count && pairs[end].Code != 0)
                    end++;
                var e = new Entity(pairs.GetRange(index, end - index));
                string type = e.Pairs[0].Value.Trim();
                if (type == "SECTION" || type == "BLOCK" || type == "ENDBLK" || type == "ENDSEC")
                    container++; // выемки — только в контуре того же блока или раздела, что и линия гиба
                e.Container = container;

                if (IsBendLine(e.Pairs, layers, out var a, out var b, out string layer))
                {
                    e.Bend = new BendWork(a, b, layer);
                }
                else if (IsOn(e.Pairs, outer))
                {
                    if (type == "LINE")
                        e.Outer = OuterShape.FromLine(e.Pairs);
                    else if (type == "LWPOLYLINE")
                        e.Outer = OuterShape.FromPolyline(e.Pairs);
                }
                entities.Add(e);
                index = end;
            }

            // ===== План: параметры линий, выемки, укорочение линий у законцовок =====
            var items = new List<ProcessedBendLine>();
            var endFeatures = new List<Preview.Polyline2>();
            bool changed = false;
            foreach (var e in entities.Where(x => x.Bend != null))
            {
                var w = e.Bend;
                w.Info = new BendLineInfo(items.Count + 1, w.Layer, w.Start[0], w.Start[1], w.Finish[0], w.Finish[1]);
                w.Options = resolve(w.Info) ?? new BendLineOptions();
                double length = Distance(w.Start, w.Finish);
                var outlines = BendEnds.Outlines(w.Start[0], w.Start[1], w.Finish[0], w.Finish[1], w.Options);

                if (outlines.Count == 2 && length > 0)
                {
                    changed = true;
                    double ux = (w.Finish[0] - w.Start[0]) / length, uy = (w.Finish[1] - w.Start[1]) / length;
                    double reach = BendEnds.For(w.Options.EndShape).Reach(w.Options.EndSize);
                    double offset = Math.Min(w.Options.EndOffset, length / 2);
                    for (int k = 0; k < 2; k++)
                    {
                        double ix = k == 0 ? ux : -ux, iy = k == 0 ? uy : -uy;
                        var notch = MergeIntoOuter(entities, e.Container, outlines[k], ix, iy);
                        if (notch == null)
                            w.SeparateEnds.Add(outlines[k]);
                        endFeatures.Add(EndFeature(outlines[k], notch));

                        // Линия гиба внутри выреза не нужна — там пустота: укорачиваем, если вырез накрывает конец.
                        if (offset <= reach + 1e-9)
                        {
                            double trim = Math.Min(offset + reach, length / 2);
                            if (k == 0) w.TrimStart = trim; else w.TrimEnd = trim;
                        }
                    }
                }

                w.Layout = BendLineStrategies.Layout(Math.Max(0, length - w.TrimStart - w.TrimEnd), w.Options);
                items.Add(new ProcessedBendLine(w.Info, w.Options, w.Layout));
                if (w.Options.Mode != BendLineMode.Keep)
                    changed = true;
            }

            if (!changed)
                return new BendProcessResult(dxf, items);

            // ===== Проход 2: запись =====
            var output = new List<Pair>(pairs.Count);
            foreach (var e in entities)
            {
                if (e.Bend != null)
                    output.AddRange(WriteBend(e.Pairs, e.Bend, handles));
                else if (e.Outer != null && e.Outer.Notches.Count > 0)
                    output.AddRange(e.Outer.Write(e.Pairs, handles));
                else
                    output.AddRange(e.Pairs);
            }

            handles.UpdateSeed(output);
            var text = new StringBuilder(dxf.Length);
            foreach (var pair in output)
                text.Append(pair.CodeText).Append(newline).Append(pair.Value).Append(newline);
            return new BendProcessResult(text.ToString(), items, endFeatures);
        }

        /// <summary>Линия гиба: оставшаяся часть (после укорочения у законцовок) по раскладке, затем отдельные законцовки.</summary>
        private static IEnumerable<Pair> WriteBend(List<Pair> entity, BendWork w, HandleAllocator handles)
        {
            var output = new List<Pair>();
            double length = Distance(w.Start, w.Finish);
            double shift = w.TrimStart;
            bool trimmed = w.TrimStart > 0 || w.TrimEnd > 0;

            if (w.Options.Mode == BendLineMode.Keep)
            {
                output.AddRange(trimmed
                    ? Segment(entity, w.Start, w.Finish, length, new BendSegment(w.TrimStart, length - w.TrimEnd), handles)
                    : entity);
            }
            else
            {
                foreach (var seg in w.Layout.Segments)
                {
                    var moved = new BendSegment(seg.Start + shift, seg.End + shift);
                    output.AddRange(w.Layout.SlotHalfWidth <= 0 || length <= 0
                        ? Segment(entity, w.Start, w.Finish, length, moved, handles)
                        : w.Layout.RoundedEnds
                            ? RoundSlot(entity, w.Start, w.Finish, length, moved, w.Layout.SlotHalfWidth, handles)
                            : Slot(entity, w.Start, w.Finish, length, moved, w.Layout.SlotHalfWidth, handles));
                }
            }

            foreach (var outline in w.SeparateEnds)
                output.AddRange(EndEntity(entity, outline, w.Start[2], w.Options.EndLayer, handles));
            return output;
        }

        /// <summary>Ищет прямое ребро наружного контура, которое законцовка пересекает, и планирует в нём выемку.</summary>
        private static EdgeNotch MergeIntoOuter(List<Entity> entities, int container, EndOutline outline, double ix, double iy)
        {
            foreach (var e in entities.Where(x => x.Outer != null && x.Container == container))
            {
                var shape = e.Outer;
                for (int seg = 0; seg < shape.SegmentCount; seg++)
                {
                    if (!shape.IsStraight(seg))
                        continue;
                    var (a, b) = shape.Segment(seg);
                    var notch = EdgeNotch.Find(a[0], a[1], b[0], b[1], outline, ix, iy);
                    if (notch == null)
                        continue;
                    // Две выемки на одном ребре не должны перекрываться — иначе вторая остаётся отдельным вырезом.
                    if (shape.Notches.Any(n => n.Segment == seg && n.Notch.T1 < notch.T2 && notch.T1 < n.Notch.T2))
                        return null;
                    shape.Notches.Add((seg, notch));
                    return notch;
                }
            }
            return null;
        }

        /// <summary>Законцовка для показа в окне (красным): выемка — путь по форме, отдельный вырез — вся форма.</summary>
        private static Preview.Polyline2 EndFeature(EndOutline outline, EdgeNotch notch)
        {
            var points = new List<Preview.Point2>();
            if (notch != null)
            {
                double x = notch.P1X, y = notch.P1Y;
                points.Add(new Preview.Point2(x, y));
                foreach (var piece in notch.Path)
                {
                    if (piece.IsArc)
                    {
                        double a1 = Math.Atan2(y - piece.CenterY, x - piece.CenterX);
                        double a2 = Math.Atan2(piece.ToY - piece.CenterY, piece.ToX - piece.CenterX);
                        double sweep = piece.CounterClockwise ? EdgeNotch.Ccw(a1, a2) : -EdgeNotch.Ccw(a2, a1);
                        int steps = Math.Max(8, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 36)));
                        for (int k = 1; k <= steps; k++)
                        {
                            double a = a1 + sweep * k / steps;
                            points.Add(new Preview.Point2(piece.CenterX + piece.Radius * Math.Cos(a), piece.CenterY + piece.Radius * Math.Sin(a)));
                        }
                    }
                    else
                    {
                        points.Add(new Preview.Point2(piece.ToX, piece.ToY));
                    }
                    x = piece.ToX;
                    y = piece.ToY;
                }
                return new Preview.Polyline2(EndFeatureLayer, points, closed: false);
            }

            if (outline.IsCircle)
            {
                for (int k = 0; k < 72; k++)
                {
                    double a = 2 * Math.PI * k / 72;
                    points.Add(new Preview.Point2(outline.CenterX + outline.Radius * Math.Cos(a), outline.CenterY + outline.Radius * Math.Sin(a)));
                }
            }
            else
            {
                points.AddRange(outline.Vertices.Select(v => new Preview.Point2(v.X, v.Y)));
            }
            return new Preview.Polyline2(EndFeatureLayer, points, closed: true);
        }

        private static bool IsOn(List<Pair> entity, HashSet<string> layers)
        {
            string layer = entity.FirstOrDefault(p => p.Code == 8).Value?.Trim();
            return layer != null && layers.Contains(layer);
        }

        /// <summary>Объект файла и что с ним делать при записи.</summary>
        private sealed class Entity
        {
            public Entity(List<Pair> pairs) => Pairs = pairs;

            public List<Pair> Pairs { get; }
            public int Container { get; set; }
            public BendWork Bend { get; set; }
            public OuterShape Outer { get; set; }
        }

        /// <summary>Линия гиба в обработке.</summary>
        private sealed class BendWork
        {
            public BendWork(double[] start, double[] finish, string layer)
            {
                Start = start;
                Finish = finish;
                Layer = layer;
            }

            public double[] Start { get; }
            public double[] Finish { get; }
            public string Layer { get; }
            public BendLineInfo Info { get; set; }
            public BendLineOptions Options { get; set; }
            public BendLineLayout Layout { get; set; }
            public double TrimStart { get; set; }
            public double TrimEnd { get; set; }
            public List<EndOutline> SeparateEnds { get; } = new List<EndOutline>();
        }

        /// <summary>
        /// Элемент наружного контура (LINE или LWPOLYLINE) и запланированные в нём выемки законцовок.
        /// LINE: выемка — отдельные отрезки и дуги (красные, на слое контура), стыкующиеся с остатками ребра.
        /// LWPOLYLINE: вершины выемки вставляются в саму полилинию — контур остаётся одним объектом.
        /// </summary>
        private sealed class OuterShape
        {
            private readonly List<double[]> _points;
            private readonly List<double> _bulges;
            private readonly bool _closed;
            private readonly bool _isLine;
            private readonly double _z;

            private OuterShape(List<double[]> points, List<double> bulges, bool closed, bool isLine, double z)
            {
                _points = points;
                _bulges = bulges;
                _closed = closed;
                _isLine = isLine;
                _z = z;
            }

            public List<(int Segment, EdgeNotch Notch)> Notches { get; } = new List<(int, EdgeNotch)>();

            public int SegmentCount => _isLine ? 1 : _closed ? _points.Count : _points.Count - 1;

            public bool IsStraight(int segment) => _isLine || Math.Abs(_bulges[segment]) < 1e-12;

            public (double[] A, double[] B) Segment(int segment) =>
                (_points[segment], _points[(segment + 1) % _points.Count]);

            public static OuterShape FromLine(List<Pair> e) => new OuterShape(
                new List<double[]> { new[] { Number(e, 10), Number(e, 20) }, new[] { Number(e, 11), Number(e, 21) } },
                new List<double> { 0 }, closed: false, isLine: true, z: Number(e, 30));

            public static OuterShape FromPolyline(List<Pair> e)
            {
                var points = new List<double[]>();
                var bulges = new List<double>();
                double x = 0;
                foreach (var p in e)
                {
                    if (p.Code == 10)
                        x = Parse(p.Value);
                    else if (p.Code == 20)
                    {
                        points.Add(new[] { x, Parse(p.Value) });
                        bulges.Add(0);
                    }
                    else if (p.Code == 42 && bulges.Count > 0)
                        bulges[bulges.Count - 1] = Parse(p.Value);
                }
                bool closed = (((int)Number(e, 70)) & 1) != 0;
                return points.Count >= 2 ? new OuterShape(points, bulges, closed, isLine: false, z: Number(e, 38)) : null;
            }

            private static double Parse(string value) =>
                double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;

            public IEnumerable<Pair> Write(List<Pair> entity, HandleAllocator handles) =>
                _isLine ? WriteLine(entity, handles) : WritePolyline(entity);

            private IEnumerable<Pair> WriteLine(List<Pair> entity, HandleAllocator handles)
            {
                var result = new List<Pair>();
                var red = WithColor(entity, 1);
                double[] current = _points[0];
                bool first = true;
                foreach (var (_, notch) in Notches.OrderBy(n => n.Notch.T1))
                {
                    // Остаток ребра до выемки: первый — сам исходный объект (handle не меняется), остальные — новые.
                    result.AddRange(first
                        ? Line(entity, current, new[] { notch.P1X, notch.P1Y }, null, keepHandle: true)
                        : Line(entity, current, new[] { notch.P1X, notch.P1Y }, handles));
                    first = false;
                    double x = notch.P1X, y = notch.P1Y;
                    foreach (var piece in notch.Path)
                    {
                        result.AddRange(piece.IsArc
                            ? ArcEntity(red, piece, x, y, _z, handles)
                            : Line(red, new[] { x, y, _z }, new[] { piece.ToX, piece.ToY, _z }, handles));
                        x = piece.ToX;
                        y = piece.ToY;
                    }
                    current = new[] { notch.P2X, notch.P2Y };
                }
                result.AddRange(Line(entity, current, _points[1], handles));
                return result;
            }

            private IEnumerable<Pair> WritePolyline(List<Pair> entity)
            {
                var vertices = new List<(double X, double Y, double Bulge)>();
                for (int i = 0; i < _points.Count; i++)
                {
                    var notches = Notches.Where(n => n.Segment == i).Select(n => n.Notch).OrderBy(n => n.T1).ToList();
                    vertices.Add((_points[i][0], _points[i][1], notches.Count > 0 ? 0 : _bulges[i]));
                    foreach (var notch in notches)
                    {
                        double x = notch.P1X, y = notch.P1Y;
                        for (int k = 0; k < notch.Path.Count; k++)
                        {
                            var piece = notch.Path[k];
                            vertices.Add((x, y, piece.IsArc ? Bulge(piece, x, y) : 0));
                            x = piece.ToX;
                            y = piece.ToY;
                        }
                        vertices.Add((notch.P2X, notch.P2Y, 0));
                    }
                }

                int width = entity[0].CodeText.Length;
                Pair New(int code, string value) => new Pair(code.ToString(CultureInfo.InvariantCulture).PadLeft(width), value);
                var vertexCodes = new HashSet<int> { 10, 20, 40, 41, 42, 91 };
                bool written = false;
                foreach (var pair in entity)
                {
                    if (pair.Code == 90)
                    {
                        yield return pair.With(vertices.Count.ToString(CultureInfo.InvariantCulture));
                        continue;
                    }
                    if (!vertexCodes.Contains(pair.Code))
                    {
                        yield return pair;
                        continue;
                    }
                    if (written || pair.Code != 10)
                        continue;
                    written = true;
                    foreach (var v in vertices)
                    {
                        yield return New(10, Format(v.X));
                        yield return New(20, Format(v.Y));
                        if (Math.Abs(v.Bulge) > 1e-12)
                            yield return New(42, Format(v.Bulge));
                    }
                }
            }

            /// <summary>Выпуклость участка-дуги от (x; y) до piece.To: tg(угол/4), знак — направление.</summary>
            private static double Bulge(NotchPiece piece, double x, double y)
            {
                double a1 = Math.Atan2(y - piece.CenterY, x - piece.CenterX);
                double a2 = Math.Atan2(piece.ToY - piece.CenterY, piece.ToX - piece.CenterX);
                return piece.CounterClockwise ? Math.Tan(EdgeNotch.Ccw(a1, a2) / 4) : -Math.Tan(EdgeNotch.Ccw(a2, a1) / 4);
            }
        }

        /// <summary>Копия объекта с другим цветом (62); нет кода цвета — добавляется после слоя.</summary>
        private static List<Pair> WithColor(List<Pair> entity, int color)
        {
            string value = color.ToString(CultureInfo.InvariantCulture);
            if (entity.Any(p => p.Code == 62))
                return entity.Select(p => p.Code == 62 ? p.With(value) : p).ToList();
            var result = new List<Pair>();
            int width = entity[0].CodeText.Length;
            foreach (var p in entity)
            {
                result.Add(p);
                if (p.Code == 8)
                    result.Add(new Pair("62".PadLeft(width), value));
            }
            return result;
        }

        /// <summary>Дуга выемки ARC из шаблона LINE: от (x; y) до piece.To по окружности piece (R12 и R13+).</summary>
        private static IEnumerable<Pair> ArcEntity(List<Pair> template, NotchPiece piece, double x, double y, double z, HandleAllocator handles)
        {
            double a1 = Math.Atan2(y - piece.CenterY, x - piece.CenterX) * 180 / Math.PI;
            double a2 = Math.Atan2(piece.ToY - piece.CenterY, piece.ToX - piece.CenterX) * 180 / Math.PI;
            // ARC в DXF всегда против часовой стрелки: для обхода по часовой меняем начало и конец.
            double start = piece.CounterClockwise ? a1 : a2, finish = piece.CounterClockwise ? a2 : a1;

            int width = template[0].CodeText.Length;
            Pair New(int code, string value) => new Pair(code.ToString(CultureInfo.InvariantCulture).PadLeft(width), value);
            bool subclasses = template.Any(p => p.Code == 100 && p.Value.Trim() == "AcDbLine");
            bool has30 = template.Any(p => p.Code == 30);
            IEnumerable<Pair> Tail()
            {
                yield return New(40, Format(piece.Radius));
                if (subclasses)
                    yield return New(100, "AcDbArc");
                yield return New(50, Format(Normalize(start)));
                yield return New(51, Format(Normalize(finish)));
            }

            string handle = null;
            var result = new List<Pair>();
            foreach (var pair in template)
            {
                switch (pair.Code)
                {
                    case 0: result.Add(pair.With("ARC")); break;
                    case 5: result.Add(pair.With(handle = handle ?? handles.Next())); break;
                    case 100: result.Add(pair.Value.Trim() == "AcDbLine" ? pair.With("AcDbCircle") : pair); break;
                    case 10: result.Add(pair.With(Format(piece.CenterX))); break;
                    case 20:
                        result.Add(pair.With(Format(piece.CenterY)));
                        if (!has30)
                            result.AddRange(Tail());
                        break;
                    case 30:
                        result.Add(pair.With(Format(z)));
                        result.AddRange(Tail());
                        break;
                    case 11:
                    case 21:
                    case 31:
                        break;
                    default: result.Add(pair); break;
                }
            }
            return result;
        }

        #region Разбор

        private struct Pair
        {
            public Pair(string codeText, string value)
            {
                CodeText = codeText;
                Value = value;
                Code = int.TryParse(codeText.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int c) ? c : -1;
            }

            public string CodeText { get; }
            public string Value { get; }
            public int Code { get; }

            public Pair With(string value) => new Pair(CodeText, value);
        }

        private static List<Pair> Parse(string dxf)
        {
            var lines = dxf.Split('\n');
            var pairs = new List<Pair>(lines.Length / 2);
            for (int k = 0; k + 1 < lines.Length; k += 2)
                pairs.Add(new Pair(lines[k].TrimEnd('\r'), lines[k + 1].TrimEnd('\r')));
            return pairs;
        }

        private static bool IsBendLine(List<Pair> entity, HashSet<string> layers, out double[] start, out double[] end, out string layer)
        {
            start = end = null;
            layer = null;
            if (entity[0].Code != 0 || entity[0].Value.Trim() != "LINE")
                return false;
            layer = entity.FirstOrDefault(p => p.Code == 8).Value?.Trim();
            if (layer == null || !layers.Contains(layer))
                return false;

            start = new[] { Number(entity, 10), Number(entity, 20), Number(entity, 30) };
            end = new[] { Number(entity, 11), Number(entity, 21), Number(entity, 31) };
            return true;
        }

        private static double Number(List<Pair> entity, int code)
        {
            foreach (var p in entity)
                if (p.Code == code && double.TryParse(p.Value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v))
                    return v;
            return 0;
        }

        private static double Distance(double[] a, double[] b)
        {
            double dx = b[0] - a[0], dy = b[1] - a[1], dz = b[2] - a[2];
            return Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        #endregion

        /// <summary>Участок линии гиба отрезком: копия исходного LINE с новыми концами и новым handle.</summary>
        private static IEnumerable<Pair> Segment(List<Pair> entity, double[] start, double[] end, double length,
            BendSegment segment, HandleAllocator handles) =>
            Line(entity, Along(start, end, length, segment.Start), Along(start, end, length, segment.End), handles);

        /// <summary>
        /// Участок линии гиба прямоугольным вырезом: стороны смещены от линии на halfWidth в обе стороны.
        /// В DXF R13+ — замкнутая LWPOLYLINE (программы раскроя видят один контур), в R12 — четыре LINE.
        /// </summary>
        private static IEnumerable<Pair> Slot(List<Pair> entity, double[] start, double[] end, double length,
            BendSegment segment, double halfWidth, HandleAllocator handles)
        {
            double[] a = Along(start, end, length, segment.Start), b = Along(start, end, length, segment.End);
            double nx = -(end[1] - start[1]) / length * halfWidth;
            double ny = (end[0] - start[0]) / length * halfWidth;
            var corners = new[]
            {
                new[] { a[0] + nx, a[1] + ny, a[2] },
                new[] { b[0] + nx, b[1] + ny, b[2] },
                new[] { b[0] - nx, b[1] - ny, b[2] },
                new[] { a[0] - nx, a[1] - ny, a[2] }
            };

            bool hasSubclasses = entity.Any(p => p.Code == 100 && p.Value.Trim() == "AcDbLine");
            if (hasSubclasses)
                return Polyline(entity, corners, handles);

            return Enumerable.Range(0, 4).SelectMany(k => Line(entity, corners[k], corners[(k + 1) % 4], handles));
        }

        /// <summary>
        /// Участок линии гиба вырезом со скруглёнными торцами (паз): прямые стороны смещены на radius,
        /// торцы — полуокружности радиуса radius; общая длина паза — длина участка. Участок короче ширины —
        /// круг диаметром в ширину выреза. В DXF R13+ — замкнутая LWPOLYLINE с выпуклостями (bulge −1 —
        /// полуокружность), в R12 — отрезки и дуги ARC.
        /// </summary>
        private static IEnumerable<Pair> RoundSlot(List<Pair> entity, double[] start, double[] end, double length,
            BendSegment segment, double radius, HandleAllocator handles)
        {
            // Центры скруглений — на линии гиба, на radius внутрь от концов участка (не дальше середины).
            double from = segment.Start + radius, to = segment.End - radius;
            if (to < from)
                from = to = (segment.Start + segment.End) / 2;
            double[] a = Along(start, end, length, from), b = Along(start, end, length, to);
            double ux = (end[0] - start[0]) / length, uy = (end[1] - start[1]) / length;
            double nx = -uy * radius, ny = ux * radius;

            bool hasSubclasses = entity.Any(p => p.Code == 100 && p.Value.Trim() == "AcDbLine");
            if (hasSubclasses)
            {
                // Обход: верхняя сторона → дуга у конца → нижняя сторона → дуга у начала (по часовой — bulge −1).
                var vertices = new[]
                {
                    new[] { a[0] + nx, a[1] + ny, a[2] },
                    new[] { b[0] + nx, b[1] + ny, b[2] },
                    new[] { b[0] - nx, b[1] - ny, b[2] },
                    new[] { a[0] - nx, a[1] - ny, a[2] }
                };
                if (from == to)
                    return Polyline(entity, new[] { vertices[1], vertices[2] }, handles, new[] { -1.0, -1.0 });
                return Polyline(entity, vertices, handles, new[] { 0, -1.0, 0, -1.0 });
            }

            double angle = Math.Atan2(uy, ux) * 180 / Math.PI;
            var parts = new List<IEnumerable<Pair>>();
            if (to > from)
            {
                parts.Add(Line(entity, new[] { a[0] + nx, a[1] + ny, a[2] }, new[] { b[0] + nx, b[1] + ny, b[2] }, handles));
                parts.Add(Line(entity, new[] { b[0] - nx, b[1] - ny, b[2] }, new[] { a[0] - nx, a[1] - ny, a[2] }, handles));
            }
            parts.Add(Arc(entity, b, radius, angle - 90, angle + 90, handles));   // торец у конца участка
            parts.Add(Arc(entity, a, radius, angle + 90, angle + 270, handles));  // торец у начала участка
            return parts.SelectMany(p => p);
        }

        /// <summary>
        /// Законцовка в DXF: окружность — CIRCLE, многоугольник — замкнутая LWPOLYLINE (в R12 — отрезки LINE).
        /// Слой — заданный, цвет, тип и вес линии — «по слою»: это вырез, а не линия гиба.
        /// </summary>
        private static IEnumerable<Pair> EndEntity(List<Pair> entity, EndOutline outline, double z, string layer, HandleAllocator handles)
        {
            var template = AsCut(entity, string.IsNullOrWhiteSpace(layer) ? BendLineOptions.DefaultEndLayer : layer.Trim());
            bool hasSubclasses = template.Any(p => p.Code == 100 && p.Value.Trim() == "AcDbLine");
            if (outline.IsCircle)
                return Circle(template, new[] { outline.CenterX, outline.CenterY, z }, outline.Radius, handles);

            var corners = outline.Vertices.Select(v => new[] { v.X, v.Y, z }).ToArray();
            if (hasSubclasses)
                return Polyline(template, corners, handles, outline.Vertices.Select(v => v.Bulge).ToArray());
            return Enumerable.Range(0, corners.Length).SelectMany(k => Line(template, corners[k], corners[(k + 1) % corners.Length], handles));
        }

        /// <summary>Копия LINE для выреза: другой слой, без цвета, типа и веса линии (6, 62, 370, 420, 48).</summary>
        private static List<Pair> AsCut(List<Pair> entity, string layer) =>
            entity.Where(p => p.Code != 6 && p.Code != 62 && p.Code != 370 && p.Code != 420 && p.Code != 48)
                .Select(p => p.Code == 8 ? p.With(layer) : p)
                .ToList();

        /// <summary>CIRCLE на месте исходного LINE (подкласс AcDbLine заменяется на AcDbCircle).</summary>
        private static IEnumerable<Pair> Circle(List<Pair> entity, double[] center, double radius, HandleAllocator handles)
        {
            int width = entity[0].CodeText.Length;
            Pair New(int code, string value) => new Pair(code.ToString(CultureInfo.InvariantCulture).PadLeft(width), value);
            bool has30 = entity.Any(p => p.Code == 30);
            string handle = null;
            foreach (var pair in entity)
            {
                switch (pair.Code)
                {
                    case 0: yield return pair.With("CIRCLE"); break;
                    case 5: yield return pair.With(handle = handle ?? handles.Next()); break;
                    case 100: yield return pair.Value.Trim() == "AcDbLine" ? pair.With("AcDbCircle") : pair; break;
                    case 10: yield return pair.With(Format(center[0])); break;
                    case 20:
                        yield return pair.With(Format(center[1]));
                        if (!has30)
                            yield return New(40, Format(radius)); // радиус — сразу за центром, как пишет AutoCAD
                        break;
                    case 30:
                        yield return pair.With(Format(center[2]));
                        yield return New(40, Format(radius));
                        break;
                    case 11:
                    case 21:
                    case 31:
                        break;
                    default: yield return pair; break;
                }
            }
        }

        /// <summary>Дуга ARC (R12) на месте исходного LINE: углы в градусах, против часовой стрелки.</summary>
        private static IEnumerable<Pair> Arc(List<Pair> entity, double[] center, double radius, double startAngle, double endAngle,
            HandleAllocator handles)
        {
            int width = entity[0].CodeText.Length;
            Pair New(int code, string value) => new Pair(code.ToString(CultureInfo.InvariantCulture).PadLeft(width), value);
            string handle = null;
            foreach (var pair in entity)
            {
                switch (pair.Code)
                {
                    case 0: yield return pair.With("ARC"); break;
                    case 5: yield return pair.With(handle = handle ?? handles.Next()); break;
                    case 10: yield return pair.With(Format(center[0])); break;
                    case 20: yield return pair.With(Format(center[1])); break;
                    case 30: yield return pair.With(Format(center[2])); break;
                    case 11:
                    case 21:
                    case 31:
                        break;
                    default: yield return pair; break;
                }
            }
            yield return New(40, Format(radius));
            yield return New(50, Format(Normalize(startAngle)));
            yield return New(51, Format(Normalize(endAngle)));
        }

        private static double Normalize(double degrees)
        {
            degrees %= 360;
            return degrees < 0 ? degrees + 360 : degrees;
        }

        private static double[] Along(double[] start, double[] end, double length, double distance)
        {
            double t = length > 0 ? distance / length : 0;
            return new[]
            {
                start[0] + (end[0] - start[0]) * t,
                start[1] + (end[1] - start[1]) * t,
                start[2] + (end[2] - start[2]) * t
            };
        }

        /// <summary>Копия исходного LINE с заданными концами и новым handle (keepHandle — с прежним).</summary>
        private static IEnumerable<Pair> Line(List<Pair> entity, double[] a, double[] b, HandleAllocator handles, bool keepHandle = false)
        {
            string handle = null;
            if (a.Length < 3)
                a = new[] { a[0], a[1], Number(entity, 30) };
            if (b.Length < 3)
                b = new[] { b[0], b[1], Number(entity, 31) };
            foreach (var pair in entity)
            {
                switch (pair.Code)
                {
                    case 5: yield return keepHandle ? pair : pair.With(handle = handle ?? handles.Next()); break;
                    case 10: yield return pair.With(Format(a[0])); break;
                    case 20: yield return pair.With(Format(a[1])); break;
                    case 30: yield return pair.With(Format(a[2])); break;
                    case 11: yield return pair.With(Format(b[0])); break;
                    case 21: yield return pair.With(Format(b[1])); break;
                    case 31: yield return pair.With(Format(b[2])); break;
                    default: yield return pair; break;
                }
            }
        }

        /// <summary>
        /// Замкнутая LWPOLYLINE на месте исходного LINE: общие свойства объекта (слой, цвет, тип линии)
        /// берутся из LINE, геометрия — новая.
        /// </summary>
        private static IEnumerable<Pair> Polyline(List<Pair> entity, double[][] corners, HandleAllocator handles, double[] bulges = null)
        {
            int width = entity[0].CodeText.Length; // сохраняем оформление кодов файла («  0», « 10»)
            Pair New(int code, string value) => new Pair(code.ToString(CultureInfo.InvariantCulture).PadLeft(width), value);

            foreach (var pair in entity)
            {
                if (pair.Code == 100 && pair.Value.Trim() == "AcDbLine")
                    break;
                if (pair.Code == 0)
                    yield return pair.With("LWPOLYLINE");
                else if (pair.Code == 5)
                    yield return pair.With(handles.Next());
                else
                    yield return pair;
            }

            yield return New(100, "AcDbPolyline");
            yield return New(90, corners.Length.ToString(CultureInfo.InvariantCulture));
            yield return New(70, "1"); // замкнутая
            yield return New(43, "0.0");
            if (Math.Abs(corners[0][2]) > 1e-12)
                yield return New(38, Format(corners[0][2]));
            for (int k = 0; k < corners.Length; k++)
            {
                yield return New(10, Format(corners[k][0]));
                yield return New(20, Format(corners[k][1]));
                if (bulges != null && Math.Abs(bulges[k]) > 1e-12)
                    yield return New(42, Format(bulges[k])); // выпуклость участка до следующей вершины
            }
        }

        private static string Format(double value) => value.ToString("0.0#########", CultureInfo.InvariantCulture);

        /// <summary>
        /// Выдаёт новые handle: выше всех существующих и не ниже исходного $HANDSEED
        /// (seed никогда не уменьшается — иначе AutoCAD может выдать уже занятый номер).
        /// </summary>
        private sealed class HandleAllocator
        {
            private ulong _next;

            public HandleAllocator(List<Pair> pairs)
            {
                ulong max = 0;
                for (int k = 0; k < pairs.Count; k++)
                {
                    var p = pairs[k];
                    bool isSeed = k > 0 && pairs[k - 1].Code == 9 && pairs[k - 1].Value.Trim() == "$HANDSEED";
                    if (!isSeed && (p.Code == 5 || p.Code == 105) && TryHex(p.Value, out ulong h))
                        max = Math.Max(max, h);
                }
                // Новые номера — с исходного seed (AutoCAD выдал бы их так же), но всегда выше занятых.
                _next = Math.Max(max + 1, Seed(pairs));
            }

            private static ulong Seed(List<Pair> pairs)
            {
                for (int k = 0; k + 1 < pairs.Count; k++)
                    if (pairs[k].Code == 9 && pairs[k].Value.Trim() == "$HANDSEED" && TryHex(pairs[k + 1].Value, out ulong seed))
                        return seed;
                return 0;
            }

            private static bool TryHex(string value, out ulong result) =>
                ulong.TryParse(value.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out result);

            public string Next() => (_next++).ToString("X", CultureInfo.InvariantCulture);

            public void UpdateSeed(List<Pair> pairs)
            {
                for (int k = 0; k + 1 < pairs.Count; k++)
                {
                    if (pairs[k].Code == 9 && pairs[k].Value.Trim() == "$HANDSEED" && pairs[k + 1].Code == 5)
                    {
                        pairs[k + 1] = pairs[k + 1].With(_next.ToString("X", CultureInfo.InvariantCulture));
                        return;
                    }
                }
            }
        }
    }
}

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
        public BendProcessResult(string text, IReadOnlyList<ProcessedBendLine> items)
        {
            Text = text;
            Items = items;
        }

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

        /// <summary>
        /// Свои параметры для каждой линии: resolve получает линию (слой, координаты, ключ) и возвращает параметры;
        /// null — оставить линию как есть.
        /// </summary>
        public static BendProcessResult Process(string dxf, IEnumerable<string> bendLayers, Func<BendLineInfo, BendLineOptions> resolve)
        {
            var layers = new HashSet<string>(bendLayers, StringComparer.OrdinalIgnoreCase);
            string newline = dxf.Contains("\r\n") ? "\r\n" : "\n";
            var pairs = Parse(dxf);
            var items = new List<ProcessedBendLine>();
            var output = new List<Pair>(pairs.Count);
            var handles = new HandleAllocator(pairs);
            bool changed = false;

            int i = 0;
            while (i < pairs.Count)
            {
                int end = i + 1;
                while (end < pairs.Count && pairs[end].Code != 0)
                    end++;

                var entity = pairs.GetRange(i, end - i);
                if (IsBendLine(entity, layers, out var start, out var finish, out string layer))
                {
                    double length = Distance(start, finish);
                    var info = new BendLineInfo(items.Count + 1, layer, start[0], start[1], finish[0], finish[1]);
                    var options = resolve(info) ?? new BendLineOptions();
                    var layout = BendLineStrategies.Layout(length, options);
                    items.Add(new ProcessedBendLine(info, options, layout));

                    if (options.Mode == BendLineMode.Keep)
                    {
                        output.AddRange(entity);
                    }
                    else
                    {
                        changed = true;
                        foreach (var segment in layout.Segments)
                        {
                            output.AddRange(layout.SlotHalfWidth <= 0 || length <= 0
                                ? Segment(entity, start, finish, length, segment, handles)
                                : layout.RoundedEnds
                                    ? RoundSlot(entity, start, finish, length, segment, layout.SlotHalfWidth, handles)
                                    : Slot(entity, start, finish, length, segment, layout.SlotHalfWidth, handles));
                        }
                    }
                }
                else
                {
                    output.AddRange(entity);
                }
                i = end;
            }

            if (!changed)
                return new BendProcessResult(dxf, items);

            handles.UpdateSeed(output);
            var text = new StringBuilder(dxf.Length);
            foreach (var pair in output)
                text.Append(pair.CodeText).Append(newline).Append(pair.Value).Append(newline);
            return new BendProcessResult(text.ToString(), items);
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

        /// <summary>Копия исходного LINE с заданными концами и новым handle.</summary>
        private static IEnumerable<Pair> Line(List<Pair> entity, double[] a, double[] b, HandleAllocator handles)
        {
            string handle = null;
            foreach (var pair in entity)
            {
                switch (pair.Code)
                {
                    case 5: yield return pair.With(handle = handle ?? handles.Next()); break;
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

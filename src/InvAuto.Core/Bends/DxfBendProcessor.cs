using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InvAuto.Core.Bends
{
    public sealed class BendProcessResult
    {
        public BendProcessResult(string text, IReadOnlyList<BendLineLayout> lines)
        {
            Text = text;
            Lines = lines;
        }

        /// <summary>DXF после обработки.</summary>
        public string Text { get; }

        /// <summary>Найденные линии гиба и что с каждой сделано.</summary>
        public IReadOnlyList<BendLineLayout> Lines { get; }
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

        public static BendProcessResult Process(string dxf, IEnumerable<string> bendLayers, BendLineOptions options)
        {
            var layers = new HashSet<string>(bendLayers, StringComparer.OrdinalIgnoreCase);
            string newline = dxf.Contains("\r\n") ? "\r\n" : "\n";
            var pairs = Parse(dxf);
            var layouts = new List<BendLineLayout>();
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
                if (IsBendLine(entity, layers, out var start, out var finish))
                {
                    double length = Distance(start, finish);
                    var layout = BendLineStrategies.Layout(length, options);
                    layouts.Add(layout);

                    if (options.Mode == BendLineMode.Keep)
                    {
                        output.AddRange(entity);
                    }
                    else
                    {
                        changed = true;
                        foreach (var segment in layout.Segments)
                            output.AddRange(Segment(entity, start, finish, length, segment, handles));
                    }
                }
                else
                {
                    output.AddRange(entity);
                }
                i = end;
            }

            if (!changed)
                return new BendProcessResult(dxf, layouts);

            handles.UpdateSeed(output);
            var text = new StringBuilder(dxf.Length);
            foreach (var pair in output)
                text.Append(pair.CodeText).Append(newline).Append(pair.Value).Append(newline);
            return new BendProcessResult(text.ToString(), layouts);
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

        private static bool IsBendLine(List<Pair> entity, HashSet<string> layers, out double[] start, out double[] end)
        {
            start = end = null;
            if (entity[0].Code != 0 || entity[0].Value.Trim() != "LINE")
                return false;
            string layer = entity.FirstOrDefault(p => p.Code == 8).Value?.Trim();
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

        /// <summary>Копия исходного отрезка с новыми концами и новым handle.</summary>
        private static IEnumerable<Pair> Segment(List<Pair> entity, double[] start, double[] end, double length,
            BendSegment segment, HandleAllocator handles)
        {
            double[] Point(double distance)
            {
                double t = length > 0 ? distance / length : 0;
                return new[]
                {
                    start[0] + (end[0] - start[0]) * t,
                    start[1] + (end[1] - start[1]) * t,
                    start[2] + (end[2] - start[2]) * t
                };
            }

            double[] a = Point(segment.Start), b = Point(segment.End);
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

        private static string Format(double value) => value.ToString("0.0#########", CultureInfo.InvariantCulture);

        /// <summary>Выдаёт новые handle выше всех существующих и обновляет $HANDSEED.</summary>
        private sealed class HandleAllocator
        {
            private ulong _next;

            public HandleAllocator(List<Pair> pairs)
            {
                ulong max = 0;
                foreach (var p in pairs)
                {
                    if ((p.Code == 5 || p.Code == 105) &&
                        ulong.TryParse(p.Value.Trim(), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out ulong h))
                        max = Math.Max(max, h);
                }
                _next = max + 1;
            }

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

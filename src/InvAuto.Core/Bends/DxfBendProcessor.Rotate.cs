using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace InvAuto.Core.Bends
{
    public static partial class DxfBendProcessor
    {
        /// <summary>Объекты, координаты которых записаны в системе объекта (OCS): при выдавливании −Z поворот в ней обратный.</summary>
        private static readonly HashSet<string> OcsTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "CIRCLE", "ARC", "LWPOLYLINE", "POLYLINE", "VERTEX", "TEXT", "ATTRIB", "ATTDEF", "INSERT", "SOLID", "TRACE"
        };

        /// <summary>Объекты, у которых код 50 — угол поворота (ARC: 50 и 51 — начальный и конечный углы).</summary>
        private static readonly HashSet<string> AngleTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "ARC", "TEXT", "ATTRIB", "ATTDEF", "INSERT", "SHAPE"
        };

        /// <summary>Типы, которые поворачиваются: все точки и векторы (коды 10…18 / 20…28) и углы.</summary>
        private static readonly HashSet<string> RotatableTypes = new HashSet<string>(StringComparer.Ordinal)
        {
            "LINE", "CIRCLE", "ARC", "LWPOLYLINE", "POLYLINE", "VERTEX", "SPLINE", "ELLIPSE", "POINT",
            "TEXT", "MTEXT", "ATTRIB", "ATTDEF", "INSERT", "SOLID", "TRACE", "SHAPE"
        };

        /// <summary>
        /// Поворачивает развертку (объекты раздела ENTITIES) на degrees против часовой стрелки вокруг начала координат.
        /// Поворот вокруг начала — линейный, поэтому точки и векторы (ось эллипса, касательные сплайна, направление
        /// MTEXT) поворачиваются одинаково; к углам дуг, текста и вставок прибавляется угол поворота.
        /// 0 — тот же экземпляр строки.
        /// </summary>
        public static string Rotate(string dxf, double degrees)
        {
            degrees %= 360;
            if (Math.Abs(degrees) < 1e-12)
                return dxf;

            var pairs = Parse(dxf);
            int section = Find(pairs, 0, i => i + 1 < pairs.Count && Is(pairs[i], 0, "SECTION") && Is(pairs[i + 1], 2, "ENTITIES"));
            if (section < 0)
                return dxf;
            int endsec = Find(pairs, section, i => Is(pairs[i], 0, "ENDSEC"));
            if (endsec < 0)
                return dxf;

            double polylineSign = 1; // VERTEX — в системе родительской POLYLINE
            for (int start = NextEntity(pairs, section); start < endsec;)
            {
                int end = Math.Min(NextEntity(pairs, start), endsec);
                string type = pairs[start].Value.Trim();
                if (RotatableTypes.Contains(type))
                {
                    double sign = 1;
                    if (type == "VERTEX")
                        sign = polylineSign;
                    else if (OcsTypes.Contains(type) && Number(pairs.GetRange(start, end - start), 230) < 0 &&
                             pairs.Skip(start).Take(end - start).Any(p => p.Code == 230))
                        sign = -1; // выдавливание −Z: ось X системы объекта направлена против X мира
                    if (type == "POLYLINE")
                        polylineSign = sign;
                    RotateEntity(pairs, start, end, type, sign * degrees);
                }
                start = end;
            }

            string newline = dxf.Contains("\r\n") ? "\r\n" : "\n";
            var text = new StringBuilder(dxf.Length + dxf.Length / 8);
            foreach (var pair in pairs)
                text.Append(pair.CodeText).Append(newline).Append(pair.Value).Append(newline);
            return text.ToString();
        }

        private static void RotateEntity(List<Pair> pairs, int start, int end, string type, double degrees)
        {
            double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            var pendingX = new Dictionary<int, int>(); // код X (10…18) → индекс пары
            for (int i = start + 1; i < end; i++)
            {
                int code = pairs[i].Code;
                if (code >= 10 && code <= 18)
                {
                    pendingX[code] = i;
                }
                else if (code >= 20 && code <= 28 && pendingX.TryGetValue(code - 10, out int xi))
                {
                    pendingX.Remove(code - 10);
                    double x = ParseValue(pairs[xi].Value), y = ParseValue(pairs[i].Value);
                    pairs[xi] = pairs[xi].With(Format(x * cos - y * sin));
                    pairs[i] = pairs[i].With(Format(x * sin + y * cos));
                }
                else if (code == 50 && type == "MTEXT")
                {
                    pairs[i] = pairs[i].With(Format(ParseValue(pairs[i].Value) + a)); // у MTEXT угол — в радианах
                }
                else if ((code == 50 && AngleTypes.Contains(type)) || (code == 51 && type == "ARC"))
                {
                    pairs[i] = pairs[i].With(Format(Normalize(ParseValue(pairs[i].Value) + degrees)));
                }
            }
        }

        private static double ParseValue(string value) =>
            double.TryParse(value.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : 0;
    }
}

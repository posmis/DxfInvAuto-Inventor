using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using InvAuto.Core.Preview;

namespace InvAuto.Core.Bends
{
    public static partial class DxfBendProcessor
    {
        /// <summary>
        /// Добавляет в DXF однострочный текст (TEXT) в раздел ENTITIES и, если такого слоя нет, слой в таблицу LAYER
        /// (копия последнего слоя файла с новым именем и цветом). Новые объекты получают свои handle, $HANDSEED
        /// обновляется. Не ASCII-символы: в DXF 2007+ — UTF-8, в более ранних — \U+XXXX (понимают все версии AutoCAD).
        /// </summary>
        public static string AddText(string dxf, FlatText text, string layer, int color)
        {
            var pairs = Parse(dxf);
            int section = Find(pairs, 0, i => i + 1 < pairs.Count && Is(pairs[i], 0, "SECTION") && Is(pairs[i + 1], 2, "ENTITIES"));
            if (section < 0)
                return dxf;
            int endsec = Find(pairs, section, i => Is(pairs[i], 0, "ENDSEC"));
            if (endsec < 0)
                return dxf;

            string newline = dxf.Contains("\r\n") ? "\r\n" : "\n";
            var handles = new HandleAllocator(pairs);
            var body = pairs.GetRange(section, endsec - section);
            bool modern = body.Any(p => Is(p, 100, "AcDbEntity"));
            bool hasHandles = body.Any(p => p.Code == 5);
            string owner = body.FirstOrDefault(p => p.Code == 330).Value?.Trim();
            int width = pairs[endsec].CodeText.Length;
            Pair New(int code, string value) => new Pair(code.ToString(CultureInfo.InvariantCulture).PadLeft(width), value);

            var entity = new List<Pair> { New(0, "TEXT") };
            if (hasHandles)
                entity.Add(New(5, handles.Next()));
            if (modern && owner != null)
                entity.Add(New(330, owner));
            if (modern)
                entity.Add(New(100, "AcDbEntity"));
            entity.Add(New(8, layer));
            if (modern)
                entity.Add(New(100, "AcDbText"));
            entity.Add(New(10, Format(text.X)));
            entity.Add(New(20, Format(text.Y)));
            entity.Add(New(30, "0.0"));
            entity.Add(New(40, Format(text.Height)));
            entity.Add(New(1, Encode(text.Value, Utf8(pairs))));
            if (Math.Abs(text.Angle) > 1e-9)
                entity.Add(New(50, Format(text.Angle)));
            if (modern)
                entity.Add(New(100, "AcDbText"));

            var output = new List<Pair>(pairs.Count + 40);
            output.AddRange(pairs.Take(endsec));
            output.AddRange(entity);
            output.AddRange(pairs.Skip(endsec));
            AddLayer(output, layer, color, handles);

            handles.UpdateSeed(output);
            var result = new StringBuilder(dxf.Length + 512);
            foreach (var pair in output)
                result.Append(pair.CodeText).Append(newline).Append(pair.Value).Append(newline);
            return result.ToString();
        }

        /// <summary>Слой в таблицу LAYER, если его там нет: копия последней записи таблицы с новым именем и цветом.</summary>
        private static void AddLayer(List<Pair> pairs, string layer, int color, HandleAllocator handles)
        {
            int table = Find(pairs, 0, i => i + 1 < pairs.Count && Is(pairs[i], 0, "TABLE") && Is(pairs[i + 1], 2, "LAYER"));
            if (table < 0)
                return; // таблицы слоёв нет (бывает в R12) — слой появится при открытии файла
            int end = Find(pairs, table, i => Is(pairs[i], 0, "ENDTAB"));
            if (end < 0)
                return;

            var records = Enumerable.Range(table + 1, end - table - 1).Where(i => Is(pairs[i], 0, "LAYER")).ToList();
            if (records.Count == 0)
                return;
            for (int r = 0; r < records.Count; r++)
            {
                int recordEnd = r + 1 < records.Count ? records[r + 1] : end;
                for (int i = records[r]; i < recordEnd; i++)
                    if (pairs[i].Code == 2 && string.Equals(pairs[i].Value.Trim(), layer, StringComparison.OrdinalIgnoreCase))
                        return; // слой уже есть
            }

            var last = pairs.GetRange(records[records.Count - 1], end - records[records.Count - 1]);
            string colorText = color.ToString(CultureInfo.InvariantCulture);
            var copy = last.Select(p =>
                p.Code == 5 ? p.With(handles.Next()) :
                p.Code == 2 ? p.With(layer) :
                p.Code == 62 ? p.With(colorText) :
                p.Code == 70 ? p.With("0") : p).ToList();
            pairs.InsertRange(end, copy);

            // Число записей в заголовке таблицы — первый код 70 до первой записи.
            for (int i = table + 2; i < records[0]; i++)
            {
                if (pairs[i].Code == 70 && int.TryParse(pairs[i].Value.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int count))
                {
                    pairs[i] = pairs[i].With((count + 1).ToString(CultureInfo.InvariantCulture));
                    break;
                }
            }
        }

        private static int Find(List<Pair> pairs, int from, Func<int, bool> match)
        {
            for (int i = from; i < pairs.Count; i++)
                if (match(i))
                    return i;
            return -1;
        }

        private static bool Is(Pair pair, int code, string value) => pair.Code == code && pair.Value.Trim() == value;

        /// <summary>DXF 2007 (AC1021) и новее хранит текст в UTF-8.</summary>
        private static bool Utf8(List<Pair> pairs)
        {
            for (int i = 0; i + 1 < pairs.Count; i++)
                if (Is(pairs[i], 9, "$ACADVER"))
                    return string.CompareOrdinal(pairs[i + 1].Value.Trim(), "AC1021") >= 0;
            return false;
        }

        /// <summary>
        /// Текст в виде, в котором он лежит в строке файла (файл читается как Latin-1 — байт к символу).
        /// Управляющие символы убираются: TEXT — одна строка.
        /// </summary>
        private static string Encode(string value, bool utf8)
        {
            var clean = new string((value ?? string.Empty).Where(c => !char.IsControl(c)).ToArray()).Trim();
            if (utf8)
                return new string(Encoding.UTF8.GetBytes(clean).Select(b => (char)b).ToArray());
            var sb = new StringBuilder(clean.Length);
            foreach (char c in clean)
            {
                if (c < 128)
                    sb.Append(c);
                else
                    sb.Append("\\U+").Append(((int)c).ToString("X4", CultureInfo.InvariantCulture));
            }
            return sb.ToString();
        }
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace InvAuto.Core.Reports
{
    /// <summary>
    /// Минимальное чтение шрифта TrueType (.ttf) для PDF: соответствие символов глифам (cmap 4 и 12),
    /// ширины глифов (hmtx), метрики (head, hhea, OS/2) и PostScript-имя. Шрифт встраивается в PDF целиком,
    /// поэтому русский текст в отчёте отображается и копируется на любом компьютере.
    /// </summary>
    public sealed class TrueTypeFont
    {
        private readonly Dictionary<int, ushort> _glyphs = new Dictionary<int, ushort>();
        private ushort[] _advances = new ushort[0];

        private TrueTypeFont(byte[] data)
        {
            Data = data;
        }

        /// <summary>Файл шрифта целиком — встраивается в PDF.</summary>
        public byte[] Data { get; }

        public string PostScriptName { get; private set; } = "InvAutoFont";
        public int UnitsPerEm { get; private set; } = 1000;
        public short Ascent { get; private set; }
        public short Descent { get; private set; }
        public short CapHeight { get; private set; }
        public short[] BoundingBox { get; private set; } = new short[4];
        public int GlyphCount { get; private set; }

        public static TrueTypeFont Load(string path) => Parse(File.ReadAllBytes(path));

        public static TrueTypeFont Parse(byte[] data)
        {
            var font = new TrueTypeFont(data);
            font.Read();
            return font;
        }

        /// <summary>Номер глифа символа (0 — в шрифте символа нет).</summary>
        public ushort GlyphOf(int codePoint) => _glyphs.TryGetValue(codePoint, out ushort g) ? g : (ushort)0;

        /// <summary>Ширина глифа в единицах 1/1000 кегля (как в PDF).</summary>
        public int Width(ushort glyph)
        {
            if (_advances.Length == 0)
                return 500;
            ushort advance = glyph < _advances.Length ? _advances[glyph] : _advances[_advances.Length - 1];
            return (int)Math.Round(advance * 1000.0 / UnitsPerEm);
        }

        /// <summary>Ширина строки в пунктах при кегле size.</summary>
        public double Measure(string text, double size)
        {
            double width = 0;
            foreach (int cp in CodePoints(text))
                width += Width(GlyphOf(cp));
            return width * size / 1000;
        }

        public static IEnumerable<int> CodePoints(string text)
        {
            for (int i = 0; i < text.Length; i++)
            {
                if (char.IsHighSurrogate(text[i]) && i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                {
                    yield return char.ConvertToUtf32(text[i], text[i + 1]);
                    i++;
                }
                else
                {
                    yield return text[i];
                }
            }
        }

        #region Разбор таблиц

        private readonly Dictionary<string, (int Offset, int Length)> _tables = new Dictionary<string, (int, int)>();

        private void Read()
        {
            if (Data.Length < 12)
                throw new InvalidDataException("Файл шрифта слишком короткий");
            uint version = U32(0);
            if (version == 0x74746366) // 'ttcf' — коллекция шрифтов
                throw new InvalidDataException("Коллекции шрифтов (.ttc) не поддерживаются — нужен .ttf");
            if (version != 0x00010000 && version != 0x74727565) // 1.0 или 'true'
                throw new InvalidDataException("Нужен шрифт TrueType (.ttf с контурами glyf)");

            int count = U16(4);
            for (int i = 0; i < count; i++)
            {
                int record = 12 + i * 16;
                string tag = Encoding.ASCII.GetString(Data, record, 4);
                _tables[tag] = ((int)U32(record + 8), (int)U32(record + 12));
            }
            foreach (string required in new[] { "head", "hhea", "hmtx", "maxp", "cmap", "glyf" })
                if (!_tables.ContainsKey(required))
                    throw new InvalidDataException($"В шрифте нет таблицы {required}");

            int head = _tables["head"].Offset;
            UnitsPerEm = U16(head + 18);
            BoundingBox = new[] { Scale(S16(head + 36)), Scale(S16(head + 38)), Scale(S16(head + 40)), Scale(S16(head + 42)) };

            int hhea = _tables["hhea"].Offset;
            Ascent = Scale(S16(hhea + 4));
            Descent = Scale(S16(hhea + 6));
            int metrics = U16(hhea + 34);

            GlyphCount = U16(_tables["maxp"].Offset + 4);

            CapHeight = Ascent;
            if (_tables.TryGetValue("OS/2", out var os2) && os2.Length >= 90 && U16(os2.Offset) >= 2)
                CapHeight = Scale(S16(os2.Offset + 88));

            int hmtx = _tables["hmtx"].Offset;
            _advances = new ushort[Math.Max(1, metrics)];
            for (int i = 0; i < metrics; i++)
                _advances[i] = (ushort)U16(hmtx + i * 4);

            ReadCmap();
            ReadName();
        }

        private short Scale(short value) => (short)Math.Round(value * 1000.0 / UnitsPerEm);

        private void ReadCmap()
        {
            int cmap = _tables["cmap"].Offset;
            int count = U16(cmap + 2);
            int format4 = -1, format12 = -1;
            for (int i = 0; i < count; i++)
            {
                int platform = U16(cmap + 4 + i * 8);
                int encoding = U16(cmap + 6 + i * 8);
                int offset = cmap + (int)U32(cmap + 8 + i * 8);
                int format = U16(offset);
                bool unicode = platform == 0 || (platform == 3 && (encoding == 1 || encoding == 10));
                if (!unicode)
                    continue;
                if (format == 12)
                    format12 = offset;
                else if (format == 4 && format4 < 0)
                    format4 = offset;
            }

            if (format12 >= 0)
                ReadFormat12(format12);
            else if (format4 >= 0)
                ReadFormat4(format4);
            else
                throw new InvalidDataException("В шрифте нет таблицы символов Unicode");
        }

        private void ReadFormat4(int table)
        {
            int segments = U16(table + 6) / 2;
            int ends = table + 14;
            int starts = ends + segments * 2 + 2;
            int deltas = starts + segments * 2;
            int ranges = deltas + segments * 2;
            for (int s = 0; s < segments; s++)
            {
                int end = U16(ends + s * 2), start = U16(starts + s * 2);
                short delta = S16(deltas + s * 2);
                int rangeOffset = U16(ranges + s * 2);
                for (int c = start; c <= end && c != 0xFFFF; c++)
                {
                    int glyph;
                    if (rangeOffset == 0)
                        glyph = (c + delta) & 0xFFFF;
                    else
                    {
                        int address = ranges + s * 2 + rangeOffset + (c - start) * 2;
                        glyph = address + 1 < Data.Length ? U16(address) : 0;
                        if (glyph != 0)
                            glyph = (glyph + delta) & 0xFFFF;
                    }
                    if (glyph != 0)
                        _glyphs[c] = (ushort)glyph;
                }
            }
        }

        private void ReadFormat12(int table)
        {
            uint groups = U32(table + 12);
            for (uint g = 0; g < groups; g++)
            {
                int record = table + 16 + (int)g * 12;
                uint start = U32(record), end = U32(record + 4), glyph = U32(record + 8);
                for (uint c = start; c <= end && c <= 0x10FFFF; c++)
                    _glyphs[(int)c] = (ushort)(glyph + (c - start));
            }
        }

        private void ReadName()
        {
            if (!_tables.TryGetValue("name", out var name))
                return;
            int table = name.Offset;
            int count = U16(table + 2);
            int strings = table + U16(table + 4);
            for (int i = 0; i < count; i++)
            {
                int record = table + 6 + i * 12;
                int platform = U16(record), nameId = U16(record + 6), length = U16(record + 8), offset = U16(record + 10);
                if (nameId != 6)
                    continue;
                string value = platform == 3 || platform == 0
                    ? Encoding.BigEndianUnicode.GetString(Data, strings + offset, length)
                    : Encoding.ASCII.GetString(Data, strings + offset, length);
                var clean = new StringBuilder();
                foreach (char c in value)
                    if (c > 32 && c < 127 && "[](){}<>/%".IndexOf(c) < 0)
                        clean.Append(c);
                if (clean.Length > 0)
                {
                    PostScriptName = clean.ToString();
                    return;
                }
            }
        }

        private int U16(int at) => (Data[at] << 8) | Data[at + 1];
        private short S16(int at) => (short)U16(at);
        private uint U32(int at) => (uint)((Data[at] << 24) | (Data[at + 1] << 16) | (Data[at + 2] << 8) | Data[at + 3]);

        #endregion
    }
}

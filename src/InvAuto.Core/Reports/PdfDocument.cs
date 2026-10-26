using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;

namespace InvAuto.Core.Reports
{
    /// <summary>Цвет для PDF (0…1 по каналам).</summary>
    public readonly struct PdfColor
    {
        public PdfColor(double r, double g, double b)
        {
            R = r;
            G = g;
            B = b;
        }

        public double R { get; }
        public double G { get; }
        public double B { get; }

        public static PdfColor Rgb(int r, int g, int b) => new PdfColor(r / 255.0, g / 255.0, b / 255.0);
        public static readonly PdfColor Black = new PdfColor(0, 0, 0);
    }

    /// <summary>
    /// Минимальный PDF без сторонних библиотек: страницы, текст встроенным шрифтом TrueType (кириллица
    /// отображается и копируется), линии, прямоугольники, полилинии. Координаты страницы — в пунктах
    /// (1/72 дюйма) от левого верхнего угла, ось Y вниз; текст ставится по базовой линии.
    /// </summary>
    public sealed class PdfDocument
    {
        /// <summary>A4 в пунктах.</summary>
        public const double A4Short = 595.28, A4Long = 841.89;

        private readonly List<PdfPage> _pages = new List<PdfPage>();
        private readonly PdfFont[] _fonts;

        public PdfDocument(TrueTypeFont regular, TrueTypeFont bold)
        {
            _fonts = new[] { new PdfFont(regular, "F1"), new PdfFont(bold ?? regular, "F2") };
        }

        public string Title { get; set; } = string.Empty;
        public string Author { get; set; } = string.Empty;
        public DateTime Created { get; set; } = DateTime.Now;

        public IReadOnlyList<PdfPage> Pages => _pages;

        public PdfPage AddPage(double width, double height)
        {
            var page = new PdfPage(width, height, _fonts);
            _pages.Add(page);
            return page;
        }

        /// <summary>Ширина текста в пунктах — для разметки таблиц.</summary>
        public double Measure(string text, double size, bool bold = false) => _fonts[bold ? 1 : 0].Font.Measure(text ?? string.Empty, size);

        public void Save(string path)
        {
            using (var stream = File.Create(path))
                Save(stream);
        }

        public void Save(Stream output)
        {
            var w = new PdfWriter(output);
            w.Raw("%PDF-1.4\n%âãÏÓ\n");

            int catalog = w.Reserve(), pagesId = w.Reserve(), info = w.Reserve();
            var fontIds = _fonts.Select(f => WriteFont(w, f)).ToArray();

            var pageIds = new List<int>();
            foreach (var page in _pages)
            {
                int content = w.Stream(Encoding.ASCII.GetBytes(page.Content), compress: true);
                int id = w.Object(string.Format(CultureInfo.InvariantCulture,
                    "<< /Type /Page /Parent {0} 0 R /MediaBox [0 0 {1} {2}] /Contents {3} 0 R " +
                    "/Resources << /Font << /F1 {4} 0 R /F2 {5} 0 R >> >> >>",
                    pagesId, Num(page.Width), Num(page.Height), content, fontIds[0], fontIds[1]));
                pageIds.Add(id);
            }

            w.Object(pagesId, $"<< /Type /Pages /Count {pageIds.Count} /Kids [{string.Join(" ", pageIds.Select(id => id + " 0 R"))}] >>");
            w.Object(catalog, $"<< /Type /Catalog /Pages {pagesId} 0 R >>");
            w.Object(info, $"<< /Title {Utf16(Title)} /Author {Utf16(Author)} /Producer {Utf16("InvAuto")} " +
                           $"/CreationDate (D:{Created:yyyyMMddHHmmss}) >>");
            w.Finish(catalog, info);
        }

        private static int WriteFont(PdfWriter w, PdfFont f)
        {
            var font = f.Font;
            int file = w.Stream(font.Data, compress: true, extra: $"/Length1 {font.Data.Length}");
            int descriptor = w.Object(string.Format(CultureInfo.InvariantCulture,
                "<< /Type /FontDescriptor /FontName /{0} /Flags 32 /FontBBox [{1} {2} {3} {4}] /ItalicAngle 0 " +
                "/Ascent {5} /Descent {6} /CapHeight {7} /StemV 80 /FontFile2 {8} 0 R >>",
                font.PostScriptName, font.BoundingBox[0], font.BoundingBox[1], font.BoundingBox[2], font.BoundingBox[3],
                font.Ascent, font.Descent, font.CapHeight, file));

            var used = f.Used.OrderBy(p => p.Key).ToList();
            var widths = new StringBuilder();
            foreach (var pair in used)
                widths.Append(pair.Key).Append(" [").Append(font.Width(pair.Key)).Append("] ");
            int cid = w.Object($"<< /Type /Font /Subtype /CIDFontType2 /BaseFont /{font.PostScriptName} " +
                               "/CIDSystemInfo << /Registry (Adobe) /Ordering (Identity) /Supplement 0 >> " +
                               $"/FontDescriptor {descriptor} 0 R /DW 500 /W [{widths}] /CIDToGIDMap /Identity >>");

            int toUnicode = w.Stream(Encoding.ASCII.GetBytes(ToUnicode(used)), compress: true);
            return w.Object($"<< /Type /Font /Subtype /Type0 /BaseFont /{font.PostScriptName} /Encoding /Identity-H " +
                            $"/DescendantFonts [{cid} 0 R] /ToUnicode {toUnicode} 0 R >>");
        }

        /// <summary>Таблица «глиф → символ»: без неё текст из PDF не копируется и не ищется.</summary>
        private static string ToUnicode(IReadOnlyList<KeyValuePair<ushort, int>> used)
        {
            var sb = new StringBuilder();
            sb.Append("/CIDInit /ProcSet findresource begin\n12 dict begin\nbegincmap\n")
              .Append("/CIDSystemInfo << /Registry (Adobe) /Ordering (UCS) /Supplement 0 >> def\n")
              .Append("/CMapName /Adobe-Identity-UCS def\n/CMapType 2 def\n")
              .Append("1 begincodespacerange\n<0000> <FFFF>\nendcodespacerange\n");
            for (int i = 0; i < used.Count; i += 100)
            {
                var chunk = used.Skip(i).Take(100).ToList();
                sb.Append(chunk.Count).Append(" beginbfchar\n");
                foreach (var pair in chunk)
                {
                    sb.Append('<').Append(pair.Key.ToString("X4")).Append("> <");
                    foreach (char c in char.ConvertFromUtf32(pair.Value))
                        sb.Append(((int)c).ToString("X4"));
                    sb.Append(">\n");
                }
                sb.Append("endbfchar\n");
            }
            sb.Append("endcmap\nCMapName currentdict /CMap defineresource pop\nend\nend\n");
            return sb.ToString();
        }

        internal static string Num(double value) => Math.Round(value, 3).ToString("0.###", CultureInfo.InvariantCulture);

        private static string Utf16(string text)
        {
            var sb = new StringBuilder("<FEFF");
            foreach (char c in text ?? string.Empty)
                sb.Append(((int)c).ToString("X4"));
            return sb.Append('>').ToString();
        }

        /// <summary>Шрифт документа и глифы, которые реально использованы (для ширин и копирования текста).</summary>
        internal sealed class PdfFont
        {
            public PdfFont(TrueTypeFont font, string name)
            {
                Font = font;
                Name = name;
            }

            public TrueTypeFont Font { get; }
            public string Name { get; }
            public Dictionary<ushort, int> Used { get; } = new Dictionary<ushort, int>();
        }

        /// <summary>Запись объектов PDF с таблицей смещений (xref).</summary>
        private sealed class PdfWriter
        {
            private readonly Stream _out;
            private readonly Dictionary<int, long> _offsets = new Dictionary<int, long>();
            private int _next = 1;
            private long _position;

            public PdfWriter(Stream output)
            {
                _out = output;
            }

            public int Reserve() => _next++;

            public int Object(string body)
            {
                int id = Reserve();
                Object(id, body);
                return id;
            }

            public void Object(int id, string body)
            {
                _offsets[id] = _position;
                Raw($"{id} 0 obj\n{body}\nendobj\n");
            }

            public int Stream(byte[] data, bool compress, string extra = "")
            {
                int id = Reserve();
                byte[] body = compress ? Zlib(data) : data;
                _offsets[id] = _position;
                Raw($"{id} 0 obj\n<< /Length {body.Length}{(compress ? " /Filter /FlateDecode" : string.Empty)} {extra} >>\nstream\n");
                Bytes(body);
                Raw("\nendstream\nendobj\n");
                return id;
            }

            public void Finish(int catalog, int info)
            {
                long xref = _position;
                var sb = new StringBuilder();
                sb.Append("xref\n0 ").Append(_next).Append("\n0000000000 65535 f \n");
                for (int id = 1; id < _next; id++)
                    sb.Append(_offsets.TryGetValue(id, out long offset) ? offset.ToString("D10") + " 00000 n \n" : "0000000000 65535 f \n");
                sb.Append($"trailer\n<< /Size {_next} /Root {catalog} 0 R /Info {info} 0 R >>\nstartxref\n{xref}\n%%EOF\n");
                Raw(sb.ToString());
            }

            public void Raw(string text) => Bytes(Encoding.GetEncoding("ISO-8859-1").GetBytes(text));

            private void Bytes(byte[] data)
            {
                _out.Write(data, 0, data.Length);
                _position += data.Length;
            }

            /// <summary>FlateDecode в PDF — это zlib: заголовок, deflate, контрольная сумма Adler-32.</summary>
            private static byte[] Zlib(byte[] data)
            {
                using (var result = new MemoryStream())
                {
                    result.WriteByte(0x78);
                    result.WriteByte(0x9C);
                    using (var deflate = new DeflateStream(result, CompressionLevel.Optimal, leaveOpen: true))
                        deflate.Write(data, 0, data.Length);
                    uint a = 1, b = 0;
                    foreach (byte value in data)
                    {
                        a = (a + value) % 65521;
                        b = (b + a) % 65521;
                    }
                    uint adler = (b << 16) | a;
                    result.WriteByte((byte)(adler >> 24));
                    result.WriteByte((byte)(adler >> 16));
                    result.WriteByte((byte)(adler >> 8));
                    result.WriteByte((byte)adler);
                    return result.ToArray();
                }
            }
        }
    }

    /// <summary>Страница PDF: команды рисования в координатах «сверху вниз».</summary>
    public sealed class PdfPage
    {
        private readonly StringBuilder _content = new StringBuilder();
        private readonly PdfDocument.PdfFont[] _fonts;

        internal PdfPage(double width, double height, PdfDocument.PdfFont[] fonts)
        {
            Width = width;
            Height = height;
            _fonts = fonts;
        }

        public double Width { get; }
        public double Height { get; }

        internal string Content => _content.ToString();

        private static string N(double v) => PdfDocument.Num(v);
        private double Y(double y) => Height - y;

        private static string Color(PdfColor c) => $"{N(c.R)} {N(c.G)} {N(c.B)}";

        /// <summary>Текст по базовой линии в точке (x; y).</summary>
        public void Text(double x, double y, string text, double size, bool bold = false, PdfColor? color = null)
        {
            if (string.IsNullOrEmpty(text))
                return;
            var font = _fonts[bold ? 1 : 0];
            var hex = new StringBuilder();
            foreach (int cp in TrueTypeFont.CodePoints(text))
            {
                ushort glyph = font.Font.GlyphOf(cp);
                if (glyph == 0 && cp == 0x00A0)
                    glyph = font.Font.GlyphOf(' ');
                if (!font.Used.ContainsKey(glyph))
                    font.Used[glyph] = cp; // первый символ глифа — для копирования текста из PDF
                hex.Append(glyph.ToString("X4"));
            }
            _content.Append("BT ").Append(Color(color ?? PdfColor.Black)).Append(" rg /").Append(font.Name).Append(' ')
                .Append(N(size)).Append(" Tf ").Append(N(x)).Append(' ').Append(N(Y(y))).Append(" Td <")
                .Append(hex).Append("> Tj ET\n");
        }

        public void Line(double x1, double y1, double x2, double y2, double width, PdfColor color, double[] dash = null)
        {
            _content.Append("q ").Append(N(width)).Append(" w ").Append(Color(color)).Append(" RG ");
            if (dash != null)
                _content.Append('[').Append(string.Join(" ", dash.Select(N))).Append("] 0 d ");
            _content.Append(N(x1)).Append(' ').Append(N(Y(y1))).Append(" m ").Append(N(x2)).Append(' ').Append(N(Y(y2)))
                .Append(" l S Q\n");
        }

        /// <summary>Прямоугольник: заливка и/или обводка (null — без).</summary>
        public void Rect(double x, double y, double width, double height, PdfColor? fill, PdfColor? stroke = null, double lineWidth = 0.5)
        {
            if (fill == null && stroke == null)
                return;
            _content.Append("q ");
            if (fill is PdfColor f)
                _content.Append(Color(f)).Append(" rg ");
            if (stroke is PdfColor s)
                _content.Append(Color(s)).Append(" RG ").Append(N(lineWidth)).Append(" w ");
            _content.Append(N(x)).Append(' ').Append(N(Y(y + height))).Append(' ').Append(N(width)).Append(' ').Append(N(height))
                .Append(" re ").Append(fill != null && stroke != null ? "B" : fill != null ? "f" : "S").Append(" Q\n");
        }

        /// <summary>Ломаная по точкам страницы.</summary>
        public void Polyline(IReadOnlyList<(double X, double Y)> points, bool closed, double width, PdfColor color)
        {
            if (points.Count < 2)
                return;
            _content.Append("q ").Append(N(width)).Append(" w 1 J 1 j ").Append(Color(color)).Append(" RG ")
                .Append(N(points[0].X)).Append(' ').Append(N(Y(points[0].Y))).Append(" m ");
            for (int i = 1; i < points.Count; i++)
                _content.Append(N(points[i].X)).Append(' ').Append(N(Y(points[i].Y))).Append(" l ");
            _content.Append(closed ? "s" : "S").Append(" Q\n");
        }
    }
}

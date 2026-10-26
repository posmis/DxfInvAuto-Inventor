using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security;
using System.Text;

namespace InvAuto.Core.Reports
{
    /// <summary>Готовые стили ячеек (индексы cellXfs в styles.xml).</summary>
    public enum XlsxStyle
    {
        Default = 0,
        Title = 1,
        Subtitle = 2,
        Header = 3,
        Text = 4,
        Integer = 5,
        Decimal3 = 6,
        Decimal2 = 7,
        Thickness = 8,
        TotalText = 9,
        TotalInteger = 10,
        TotalDecimal3 = 11,
        TotalDecimal2 = 12,
        Group = 13,
        Warning = 14,
        TextWrap = 15,
        Decimal4 = 16,
        Muted = 17
    }

    /// <summary>
    /// Минимальная запись .xlsx (Office Open XML) без сторонних библиотек: листы, строки, числа, формулы,
    /// стили, ширина колонок, объединение ячеек, закрепление шапки, автофильтр, картинки PNG,
    /// параметры печати (A4, альбомная, по ширине листа, колонтитулы с номером листа и датой).
    /// </summary>
    public sealed class XlsxWorkbook
    {
        private readonly List<XlsxSheet> _sheets = new List<XlsxSheet>();

        public string Title { get; set; } = string.Empty;

        public XlsxSheet AddSheet(string name)
        {
            var sheet = new XlsxSheet(SafeName(name), _sheets.Count);
            _sheets.Add(sheet);
            return sheet;
        }

        public IReadOnlyList<XlsxSheet> Sheets => _sheets;

        private static string SafeName(string name)
        {
            foreach (char c in "[]:*?/\\")
                name = name.Replace(c, '-');
            return name.Length > 31 ? name.Substring(0, 31) : name;
        }

        public void Save(string path)
        {
            using (var stream = File.Create(path))
                Save(stream);
        }

        public void Save(Stream output)
        {
            using (var zip = new ZipArchive(output, ZipArchiveMode.Create, leaveOpen: true))
            {
                int image = 0;
                var contentTypes = new StringBuilder();
                contentTypes.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
                    .Append("<Types xmlns=\"http://schemas.openxmlformats.org/package/2006/content-types\">")
                    .Append("<Default Extension=\"rels\" ContentType=\"application/vnd.openxmlformats-package.relationships+xml\"/>")
                    .Append("<Default Extension=\"xml\" ContentType=\"application/xml\"/>")
                    .Append("<Default Extension=\"png\" ContentType=\"image/png\"/>")
                    .Append("<Override PartName=\"/xl/workbook.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.sheet.main+xml\"/>")
                    .Append("<Override PartName=\"/xl/styles.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.styles+xml\"/>")
                    .Append("<Override PartName=\"/docProps/core.xml\" ContentType=\"application/vnd.openxmlformats-package.core-properties+xml\"/>");

                var workbookRels = new StringBuilder();
                var sheetsXml = new StringBuilder();
                var definedNames = new StringBuilder();
                for (int i = 0; i < _sheets.Count; i++)
                {
                    var sheet = _sheets[i];
                    int n = i + 1;
                    contentTypes.Append($"<Override PartName=\"/xl/worksheets/sheet{n}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.spreadsheetml.worksheet+xml\"/>");
                    workbookRels.Append($"<Relationship Id=\"rId{n}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/worksheet\" Target=\"worksheets/sheet{n}.xml\"/>");
                    sheetsXml.Append($"<sheet name=\"{X(sheet.Name)}\" sheetId=\"{n}\" r:id=\"rId{n}\"/>");
                    if (sheet.PrintTitleRows.HasValue)
                        definedNames.Append($"<definedName name=\"_xlnm.Print_Titles\" localSheetId=\"{i}\">'{X(sheet.Name.Replace("'", "''"))}'!${sheet.PrintTitleRows.Value.From}:${sheet.PrintTitleRows.Value.To}</definedName>");
                    if (sheet.AutoFilter != null)
                        definedNames.Append($"<definedName name=\"_xlnm._FilterDatabase\" localSheetId=\"{i}\" hidden=\"1\">'{X(sheet.Name.Replace("'", "''"))}'!{Absolute(sheet.AutoFilter)}</definedName>");

                    bool hasImages = sheet.Images.Count > 0;
                    Write(zip, $"xl/worksheets/sheet{n}.xml", sheet.ToXml(hasImages ? "rId1" : null));
                    if (hasImages)
                    {
                        contentTypes.Append($"<Override PartName=\"/xl/drawings/drawing{n}.xml\" ContentType=\"application/vnd.openxmlformats-officedocument.drawing+xml\"/>");
                        Write(zip, $"xl/worksheets/_rels/sheet{n}.xml.rels", Rels($"<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/drawing\" Target=\"../drawings/drawing{n}.xml\"/>"));

                        var drawingRels = new StringBuilder();
                        var anchors = new StringBuilder();
                        for (int k = 0; k < sheet.Images.Count; k++)
                        {
                            var img = sheet.Images[k];
                            image++;
                            WriteBytes(zip, $"xl/media/image{image}.png", img.Png);
                            drawingRels.Append($"<Relationship Id=\"rId{k + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/image\" Target=\"../media/image{image}.png\"/>");
                            anchors.Append(Anchor(img, k + 1));
                        }
                        Write(zip, $"xl/drawings/_rels/drawing{n}.xml.rels", Rels(drawingRels.ToString()));
                        Write(zip, $"xl/drawings/drawing{n}.xml",
                            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                            "<xdr:wsDr xmlns:xdr=\"http://schemas.openxmlformats.org/drawingml/2006/spreadsheetDrawing\" " +
                            "xmlns:a=\"http://schemas.openxmlformats.org/drawingml/2006/main\" " +
                            "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" + anchors + "</xdr:wsDr>");
                    }
                }
                contentTypes.Append("</Types>");
                workbookRels.Append($"<Relationship Id=\"rId{_sheets.Count + 1}\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/styles\" Target=\"styles.xml\"/>");

                Write(zip, "[Content_Types].xml", contentTypes.ToString());
                Write(zip, "_rels/.rels", Rels(
                    "<Relationship Id=\"rId1\" Type=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships/officeDocument\" Target=\"xl/workbook.xml\"/>" +
                    "<Relationship Id=\"rId2\" Type=\"http://schemas.openxmlformats.org/package/2006/relationships/metadata/core-properties\" Target=\"docProps/core.xml\"/>"));
                Write(zip, "docProps/core.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<cp:coreProperties xmlns:cp=\"http://schemas.openxmlformats.org/package/2006/metadata/core-properties\" " +
                    "xmlns:dc=\"http://purl.org/dc/elements/1.1/\" xmlns:dcterms=\"http://purl.org/dc/terms/\" " +
                    "xmlns:xsi=\"http://www.w3.org/2001/XMLSchema-instance\">" +
                    $"<dc:title>{X(Title)}</dc:title><dc:creator>InvAuto</dc:creator>" +
                    $"<dcterms:created xsi:type=\"dcterms:W3CDTF\">{DateTime.UtcNow:yyyy-MM-ddTHH:mm:ssZ}</dcterms:created>" +
                    "</cp:coreProperties>");
                Write(zip, "xl/_rels/workbook.xml.rels", Rels(workbookRels.ToString()));
                Write(zip, "xl/workbook.xml",
                    "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
                    "<workbook xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" " +
                    "xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">" +
                    $"<sheets>{sheetsXml}</sheets>" +
                    (definedNames.Length > 0 ? $"<definedNames>{definedNames}</definedNames>" : string.Empty) +
                    "</workbook>");
                Write(zip, "xl/styles.xml", Styles);
            }
        }

        private static string Absolute(string range) =>
            string.Join(":", range.Split(':').Select(r =>
            {
                int digits = r.IndexOfAny("0123456789".ToCharArray());
                return "$" + r.Substring(0, digits) + "$" + r.Substring(digits);
            }));

        /// <summary>Картинка привязана к ячейке со смещением, размер в пикселях (1 px = 9525 EMU).</summary>
        private static string Anchor(XlsxImage img, int id) =>
            "<xdr:oneCellAnchor>" +
            $"<xdr:from><xdr:col>{img.Column}</xdr:col><xdr:colOff>{img.OffsetX * 9525}</xdr:colOff><xdr:row>{img.Row}</xdr:row><xdr:rowOff>{img.OffsetY * 9525}</xdr:rowOff></xdr:from>" +
            $"<xdr:ext cx=\"{img.Width * 9525}\" cy=\"{img.Height * 9525}\"/>" +
            $"<xdr:pic><xdr:nvPicPr><xdr:cNvPr id=\"{id + 1}\" name=\"Развертка {id}\"/><xdr:cNvPicPr><a:picLocks noChangeAspect=\"1\"/></xdr:cNvPicPr></xdr:nvPicPr>" +
            $"<xdr:blipFill><a:blip r:embed=\"rId{id}\"/><a:stretch><a:fillRect/></a:stretch></xdr:blipFill>" +
            $"<xdr:spPr><a:xfrm><a:off x=\"0\" y=\"0\"/><a:ext cx=\"{img.Width * 9525}\" cy=\"{img.Height * 9525}\"/></a:xfrm><a:prstGeom prst=\"rect\"><a:avLst/></a:prstGeom></xdr:spPr>" +
            "</xdr:pic><xdr:clientData/></xdr:oneCellAnchor>";

        private static string Rels(string body) =>
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<Relationships xmlns=\"http://schemas.openxmlformats.org/package/2006/relationships\">" + body + "</Relationships>";

        private static void Write(ZipArchive zip, string name, string xml) => WriteBytes(zip, name, new UTF8Encoding(false).GetBytes(xml));

        private static void WriteBytes(ZipArchive zip, string name, byte[] data)
        {
            var entry = zip.CreateEntry(name, CompressionLevel.Optimal);
            using (var stream = entry.Open())
                stream.Write(data, 0, data.Length);
        }

        internal static string X(string text) => SecurityElement.Escape(text ?? string.Empty) ?? string.Empty;

        /// <summary>
        /// Шрифты: 0 обычный, 1 полужирный, 2 заголовок, 3 серый мелкий, 4 оранжевый. Заливки: 2 шапка, 3 группа.
        /// Форматы: 164 «0,000», 165 «0,00», 166 «0,0#», 167 «0,0000».
        /// </summary>
        private const string Styles =
            "<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>" +
            "<styleSheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\">" +
            "<numFmts count=\"4\"><numFmt numFmtId=\"164\" formatCode=\"0.000\"/><numFmt numFmtId=\"165\" formatCode=\"0.00\"/>" +
            "<numFmt numFmtId=\"166\" formatCode=\"0.0#\"/><numFmt numFmtId=\"167\" formatCode=\"0.0000\"/></numFmts>" +
            "<fonts count=\"5\">" +
            "<font><sz val=\"10\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"10\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
            "<font><b/><sz val=\"14\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
            "<font><sz val=\"9\"/><color rgb=\"FF6A737D\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
            "<font><sz val=\"9\"/><color rgb=\"FFB26A00\"/><name val=\"Arial\"/><family val=\"2\"/></font>" +
            "</fonts>" +
            "<fills count=\"4\"><fill><patternFill patternType=\"none\"/></fill><fill><patternFill patternType=\"gray125\"/></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFE9EDF1\"/><bgColor indexed=\"64\"/></patternFill></fill>" +
            "<fill><patternFill patternType=\"solid\"><fgColor rgb=\"FFF5F7F9\"/><bgColor indexed=\"64\"/></patternFill></fill></fills>" +
            "<borders count=\"2\"><border><left/><right/><top/><bottom/><diagonal/></border>" +
            "<border><left style=\"thin\"><color rgb=\"FFC8CDD3\"/></left><right style=\"thin\"><color rgb=\"FFC8CDD3\"/></right>" +
            "<top style=\"thin\"><color rgb=\"FFC8CDD3\"/></top><bottom style=\"thin\"><color rgb=\"FFC8CDD3\"/></bottom><diagonal/></border></borders>" +
            "<cellStyleXfs count=\"1\"><xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\"/></cellStyleXfs>" +
            "<cellXfs count=\"18\">" +
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"0\" xfId=\"0\"/>" +                                                       // 0
            "<xf numFmtId=\"0\" fontId=\"2\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                       // 1 title
            "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                       // 2 subtitle
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" + // 3 header
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" + // 4 text
            "<xf numFmtId=\"3\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" + // 5 int
            "<xf numFmtId=\"164\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" + // 6
            "<xf numFmtId=\"165\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" + // 7
            "<xf numFmtId=\"166\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" + // 8
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +       // 9 total text
            "<xf numFmtId=\"3\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" + // 10
            "<xf numFmtId=\"164\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" + // 11
            "<xf numFmtId=\"165\" fontId=\"1\" fillId=\"2\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" + // 12
            "<xf numFmtId=\"0\" fontId=\"1\" fillId=\"3\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyFill=\"1\" applyBorder=\"1\"/>" +       // 13 group
            "<xf numFmtId=\"0\" fontId=\"4\" fillId=\"0\" borderId=\"0\" xfId=\"0\" applyFont=\"1\"/>" +                                       // 14 warning
            "<xf numFmtId=\"0\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\" wrapText=\"1\"/></xf>" + // 15
            "<xf numFmtId=\"167\" fontId=\"0\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyNumberFormat=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment vertical=\"center\"/></xf>" + // 16
            "<xf numFmtId=\"0\" fontId=\"3\" fillId=\"0\" borderId=\"1\" xfId=\"0\" applyFont=\"1\" applyBorder=\"1\" applyAlignment=\"1\"><alignment horizontal=\"right\" vertical=\"center\"/></xf>" + // 17 muted (прочерк)
            "</cellXfs>" +
            "<cellStyles count=\"1\"><cellStyle name=\"Normal\" xfId=\"0\" builtinId=\"0\"/></cellStyles>" +
            "</styleSheet>";
    }

    /// <summary>Картинка на листе: строка и колонка (с нуля), смещение и размер в пикселях.</summary>
    public sealed class XlsxImage
    {
        public int Row { get; set; }
        public int Column { get; set; }
        public int OffsetX { get; set; }
        public int OffsetY { get; set; }
        public int Width { get; set; }
        public int Height { get; set; }
        public byte[] Png { get; set; }
    }

    public sealed class XlsxSheet
    {
        private readonly SortedDictionary<int, SortedDictionary<int, (object Value, XlsxStyle Style, bool Formula)>> _rows =
            new SortedDictionary<int, SortedDictionary<int, (object, XlsxStyle, bool)>>();
        private readonly Dictionary<int, double> _widths = new Dictionary<int, double>();
        private readonly Dictionary<int, double> _heights = new Dictionary<int, double>();
        private readonly List<string> _merges = new List<string>();

        internal XlsxSheet(string name, int index)
        {
            Name = name;
            Index = index;
        }

        public string Name { get; }
        internal int Index { get; }

        /// <summary>Закрепить строки сверху (число строк).</summary>
        public int FreezeRows { get; set; }

        /// <summary>Диапазон автофильтра, например "A5:M40".</summary>
        public string AutoFilter { get; set; }

        /// <summary>Строки, повторяемые на каждой печатной странице (с единицы).</summary>
        public (int From, int To)? PrintTitleRows { get; set; }

        /// <summary>Колонтитулы в кодах Excel (&amp;L, &amp;C, &amp;R, &amp;P — номер страницы, &amp;N — всего, &amp;D — дата).</summary>
        public string Header { get; set; } = string.Empty;
        public string Footer { get; set; } = string.Empty;

        public List<XlsxImage> Images { get; } = new List<XlsxImage>();

        /// <summary>Значение ячейки (строка и колонка — с нуля): string, число или null.</summary>
        public void Set(int row, int column, object value, XlsxStyle style = XlsxStyle.Text)
        {
            if (!_rows.TryGetValue(row, out var cells))
                _rows[row] = cells = new SortedDictionary<int, (object, XlsxStyle, bool)>();
            cells[column] = (value, style, false);
        }

        /// <summary>Формула без «=», например "SUM(K6:K40)"; cached — значение до пересчёта (видно в просмотрщиках).</summary>
        public void Formula(int row, int column, string formula, double? cached, XlsxStyle style)
        {
            if (!_rows.TryGetValue(row, out var cells))
                _rows[row] = cells = new SortedDictionary<int, (object, XlsxStyle, bool)>();
            cells[column] = ((object)(formula + "\u0001" + (cached?.ToString("R", CultureInfo.InvariantCulture) ?? string.Empty)), style, true);
        }

        /// <summary>Ширина колонки в символах (как в Excel).</summary>
        public void Width(int column, double width) => _widths[column] = width;

        /// <summary>Высота строки в пунктах.</summary>
        public void Height(int row, double points) => _heights[row] = points;

        public void Merge(int row, int fromColumn, int toColumn) =>
            _merges.Add($"{Ref(row, fromColumn)}:{Ref(row, toColumn)}");

        public static string Ref(int row, int column) => ColumnName(column) + (row + 1).ToString(CultureInfo.InvariantCulture);

        public static string ColumnName(int column)
        {
            string name = string.Empty;
            for (int c = column + 1; c > 0; c = (c - 1) / 26)
                name = (char)('A' + (c - 1) % 26) + name;
            return name;
        }

        internal string ToXml(string drawingRel)
        {
            var sb = new StringBuilder();
            sb.Append("<?xml version=\"1.0\" encoding=\"UTF-8\" standalone=\"yes\"?>")
              .Append("<worksheet xmlns=\"http://schemas.openxmlformats.org/spreadsheetml/2006/main\" ")
              .Append("xmlns:r=\"http://schemas.openxmlformats.org/officeDocument/2006/relationships\">")
              .Append("<sheetPr><pageSetUpPr fitToPage=\"1\"/></sheetPr>")
              .Append("<sheetViews><sheetView workbookViewId=\"0\"").Append(Index == 0 ? " tabSelected=\"1\"" : string.Empty).Append('>');
            if (FreezeRows > 0)
                sb.Append($"<pane ySplit=\"{FreezeRows}\" topLeftCell=\"A{FreezeRows + 1}\" activePane=\"bottomLeft\" state=\"frozen\"/>");
            sb.Append("</sheetView></sheetViews><sheetFormatPr defaultRowHeight=\"15\"/>");

            if (_widths.Count > 0)
            {
                sb.Append("<cols>");
                foreach (var pair in _widths.OrderBy(p => p.Key))
                    sb.Append($"<col min=\"{pair.Key + 1}\" max=\"{pair.Key + 1}\" width=\"{N(pair.Value)}\" customWidth=\"1\"/>");
                sb.Append("</cols>");
            }

            sb.Append("<sheetData>");
            foreach (int row in _rows.Keys.Union(_heights.Keys).OrderBy(r => r))
            {
                sb.Append($"<row r=\"{row + 1}\"");
                if (_heights.TryGetValue(row, out double height))
                    sb.Append($" ht=\"{N(height)}\" customHeight=\"1\"");
                sb.Append('>');
                if (_rows.TryGetValue(row, out var cells))
                    foreach (var cell in cells)
                        sb.Append(Cell(row, cell.Key, cell.Value.Value, cell.Value.Style, cell.Value.Formula));
                sb.Append("</row>");
            }
            sb.Append("</sheetData>");

            if (AutoFilter != null)
                sb.Append($"<autoFilter ref=\"{AutoFilter}\"/>");
            if (_merges.Count > 0)
                sb.Append($"<mergeCells count=\"{_merges.Count}\">").Append(string.Concat(_merges.Select(m => $"<mergeCell ref=\"{m}\"/>"))).Append("</mergeCells>");

            sb.Append("<pageMargins left=\"0.4\" right=\"0.4\" top=\"0.6\" bottom=\"0.6\" header=\"0.3\" footer=\"0.3\"/>")
              .Append("<pageSetup paperSize=\"9\" orientation=\"landscape\" fitToWidth=\"1\" fitToHeight=\"0\"/>");
            if (Header.Length > 0 || Footer.Length > 0)
                sb.Append("<headerFooter>")
                  .Append(Header.Length > 0 ? $"<oddHeader>{XlsxWorkbook.X(Header)}</oddHeader>" : string.Empty)
                  .Append(Footer.Length > 0 ? $"<oddFooter>{XlsxWorkbook.X(Footer)}</oddFooter>" : string.Empty)
                  .Append("</headerFooter>");
            if (drawingRel != null)
                sb.Append($"<drawing r:id=\"{drawingRel}\"/>");
            sb.Append("</worksheet>");
            return sb.ToString();
        }

        private static string Cell(int row, int column, object value, XlsxStyle style, bool formula)
        {
            string r = Ref(row, column);
            int s = (int)style;
            if (formula)
            {
                var parts = ((string)value).Split('\u0001');
                return $"<c r=\"{r}\" s=\"{s}\"><f>{XlsxWorkbook.X(parts[0])}</f>" + (parts[1].Length > 0 ? $"<v>{parts[1]}</v>" : string.Empty) + "</c>";
            }
            switch (value)
            {
                case null:
                    return $"<c r=\"{r}\" s=\"{s}\"/>";
                case string text:
                    return $"<c r=\"{r}\" s=\"{s}\" t=\"inlineStr\"><is><t xml:space=\"preserve\">{XlsxWorkbook.X(text)}</t></is></c>";
                case bool b:
                    return $"<c r=\"{r}\" s=\"{s}\" t=\"b\"><v>{(b ? 1 : 0)}</v></c>";
                default:
                    double number = Convert.ToDouble(value, CultureInfo.InvariantCulture);
                    return $"<c r=\"{r}\" s=\"{s}\"><v>{number.ToString("R", CultureInfo.InvariantCulture)}</v></c>";
            }
        }

        private static string N(double value) => value.ToString("0.##", CultureInfo.InvariantCulture);
    }
}

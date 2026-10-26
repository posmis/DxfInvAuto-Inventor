using System;
using System.Collections.Generic;
using System.Linq;
using InvAuto.Core.Preview;

namespace InvAuto.Core.Reports
{
    /// <summary>
    /// Ведомость в PDF на листах A4 (альбомная ориентация). На каждом листе — шапка с названием, изделием
    /// и датой, внизу — «Лист N из M». Сначала сводка по материалам и толщинам, затем детали
    /// (сгруппированы по материалу и толщине, с картинкой развертки), в конце — замечания.
    /// Таблица переносится на следующий лист с повтором заголовка.
    /// </summary>
    public sealed class PdfReport
    {
        private const double Margin = 28;         // ≈ 10 мм
        private const double HeaderHeight = 44;
        private const double FooterHeight = 22;
        private const double RowHeight = 16;
        private const double FlatRowHeight = 36;
        private const double Font = 8.5;
        private const double SmallFont = 7.5;

        private static readonly PdfColor Text = PdfColor.Rgb(0x1F, 0x23, 0x28);
        private static readonly PdfColor Muted = PdfColor.Rgb(0x6A, 0x73, 0x7D);
        private static readonly PdfColor Line = PdfColor.Rgb(0xC8, 0xCD, 0xD3);
        private static readonly PdfColor HeaderFill = PdfColor.Rgb(0xE9, 0xED, 0xF1);
        private static readonly PdfColor GroupFill = PdfColor.Rgb(0xF5, 0xF7, 0xF9);
        private static readonly PdfColor Accent = PdfColor.Rgb(0x06, 0x96, 0xD7);
        private static readonly PdfColor Warning = PdfColor.Rgb(0xB2, 0x6A, 0x00);
        private static readonly PdfColor Profile = PdfColor.Rgb(0x2B, 0x2F, 0x33);

        private readonly ProductionReport _report;
        private readonly PdfDocument _pdf;
        private PdfPage _page;
        private double _y;

        private PdfReport(ProductionReport report, TrueTypeFont regular, TrueTypeFont bold)
        {
            _report = report;
            _pdf = new PdfDocument(regular, bold) { Title = report.Title, Author = "InvAuto", Created = report.Date };
        }

        private static double PageWidth => PdfDocument.A4Long;
        private static double PageHeight => PdfDocument.A4Short;
        private static double ContentWidth => PageWidth - 2 * Margin;
        private static double ContentBottom => PageHeight - Margin - FooterHeight;

        public static PdfDocument Build(ProductionReport report, TrueTypeFont regular, TrueTypeFont bold)
        {
            var builder = new PdfReport(report, regular, bold);
            builder.Compose();
            return builder._pdf;
        }

        public static void Write(ProductionReport report, string path, TrueTypeFont regular, TrueTypeFont bold) =>
            Build(report, regular, bold).Save(path);

        private void Compose()
        {
            NewPage();
            Summary();
            Details();
            Notes();
            Footers();
        }

        #region Листы

        private void NewPage()
        {
            _page = _pdf.AddPage(PageWidth, PageHeight);
            double top = Margin;
            _page.Text(Margin, top + 13, Fit(_report.Title, 14, true, ContentWidth - 160), 14, true, Text);
            string date = "Дата: " + _report.DateText;
            _page.Text(PageWidth - Margin - _pdf.Measure(date, 9), top + 12, date, 9, false, Muted);
            if (_report.Subtitle.Length > 0)
                _page.Text(Margin, top + 29, Fit(_report.Subtitle, 9, false, ContentWidth), 9, false, Muted);
            _page.Line(Margin, top + HeaderHeight - 8, PageWidth - Margin, top + HeaderHeight - 8, 0.8, Accent);
            _y = top + HeaderHeight + 4;
        }

        /// <summary>Нумерация ставится в конце, когда известно число листов.</summary>
        private void Footers()
        {
            int total = _pdf.Pages.Count;
            for (int i = 0; i < total; i++)
            {
                var page = _pdf.Pages[i];
                double y = PageHeight - Margin;
                page.Line(Margin, y - 12, PageWidth - Margin, y - 12, 0.4, Line);
                page.Text(Margin, y - 2, Fit("InvAuto · " + _report.Title, SmallFont, false, ContentWidth - 120), SmallFont, false, Muted);
                string number = $"Лист {i + 1} из {total}";
                page.Text(PageWidth - Margin - _pdf.Measure(number, 8, true), y - 2, number, 8, true, Text);
            }
        }

        private bool Fits(double height) => _y + height <= ContentBottom;

        private void SectionTitle(string title)
        {
            if (!Fits(RowHeight * 3 + 14))
                NewPage();
            _page.Text(Margin, _y + 11, title, 11, true, Text);
            _y += 18;
        }

        #endregion

        #region Таблицы

        private sealed class Column
        {
            public Column(string title, double weight, bool right = false)
            {
                Title = title;
                Weight = weight;
                Right = right;
            }

            public string Title { get; }
            public double Weight { get; }
            public bool Right { get; }
            public double X { get; set; }
            public double Width { get; set; }
        }

        private static Column[] Layout(params Column[] columns)
        {
            double total = columns.Sum(c => c.Weight), x = Margin;
            foreach (var c in columns)
            {
                c.X = x;
                c.Width = ContentWidth * c.Weight / total;
                x += c.Width;
            }
            return columns;
        }

        private void HeaderRow(Column[] columns)
        {
            double height = 24;
            _page.Rect(Margin, _y, ContentWidth, height, HeaderFill, Line, 0.5);
            foreach (var c in columns)
            {
                var lines = Wrap(c.Title, SmallFont, true, c.Width - 6, 2);
                double ty = _y + (lines.Count == 1 ? 15 : 10);
                foreach (string line in lines)
                {
                    double x = c.Right ? c.X + c.Width - 3 - _pdf.Measure(line, SmallFont, true) : c.X + 3;
                    _page.Text(x, ty, line, SmallFont, true, Text);
                    ty += 9;
                }
                if (c.X > Margin)
                    _page.Line(c.X, _y, c.X, _y + height, 0.5, Line);
            }
            _y += height;
        }

        /// <summary>Строка таблицы; при нехватке места — новый лист и повтор заголовка.</summary>
        private void Row(Column[] columns, string[] cells, double height, bool bold = false, PdfColor? fill = null,
            Action<double, double, double, double> drawExtra = null, int extraColumn = -1)
        {
            if (!Fits(height))
            {
                NewPage();
                HeaderRow(columns);
            }

            _page.Rect(Margin, _y, ContentWidth, height, fill, Line, 0.4);
            for (int i = 0; i < columns.Length; i++)
            {
                var c = columns[i];
                if (c.X > Margin)
                    _page.Line(c.X, _y, c.X, _y + height, 0.4, Line);
                if (i == extraColumn && drawExtra != null)
                {
                    drawExtra(c.X + 2, _y + 2, c.Width - 4, height - 4);
                    continue;
                }
                string text = Fit(cells[i] ?? string.Empty, Font, bold, c.Width - 6);
                double x = c.Right ? c.X + c.Width - 3 - _pdf.Measure(text, Font, bold) : c.X + 3;
                _page.Text(x, _y + height / 2 + 3, text, Font, bold, text == "—" ? Muted : Text);
            }
            _y += height;
        }

        private void GroupRow(string title)
        {
            if (!Fits(RowHeight * 2))
            {
                NewPage();
                HeaderRow(_detailColumns);
            }
            _page.Rect(Margin, _y, ContentWidth, RowHeight, GroupFill, Line, 0.4);
            _page.Text(Margin + 3, _y + RowHeight / 2 + 3, title, Font, true, Text);
            _y += RowHeight;
        }

        #endregion

        #region Разделы

        private void Summary()
        {
            SectionTitle("Сводка по материалам и толщинам");
            var columns = Layout(
                new Column("Материал", 3),
                new Column("Толщина, мм", 1, true),
                new Column("Позиций", 0.9, true),
                new Column("Деталей, шт", 1, true),
                new Column("Площадь, м²", 1.1, true),
                new Column("Масса, кг", 1.1, true),
                new Column("Длина реза, м", 1.2, true),
                new Column("Врезки, шт", 1, true));
            HeaderRow(columns);

            foreach (var g in _report.Groups())
                Row(columns, SummaryCells(g, g.MaterialTitle), RowHeight);

            var t = _report.Totals();
            Row(columns, SummaryCells(t, "Итого"), RowHeight, bold: true, fill: HeaderFill);

            if (t.Incomplete > 0)
            {
                _y += 4;
                _page.Text(Margin, _y + 9, $"Без площади или массы: {t.Incomplete} поз. — итоги неполные, см. «Замечания».",
                    SmallFont, false, Warning);
                _y += 12;
            }
            _y += 14;
        }

        private static string[] SummaryCells(ReportGroup g, string title) => new[]
        {
            title,
            g.Thickness > 0 ? ReportFormat.Number(g.Thickness, "0.0#") : string.Empty,
            ReportFormat.Integer(g.Positions),
            ReportFormat.Integer(g.Quantity),
            ReportFormat.Number(g.Area, "0.000"),
            ReportFormat.Number(g.Mass, "0.00"),
            ReportFormat.Number(g.CutLength, "0.00"),
            ReportFormat.Integer(g.Pierces)
        };

        private Column[] _detailColumns;

        private void Details()
        {
            SectionTitle("Детали");
            bool flats = _report.IncludeFlats;
            var list = new List<Column> { new Column("№", 0.45, true) };
            if (flats)
                list.Add(new Column("Развертка", 1.5));
            list.AddRange(new[]
            {
                new Column("Обозначение", 2),
                new Column("Наименование", 2.4),
                new Column("Габариты, мм", 1.35),
                new Column("Кол-во, шт", 0.75, true),
                new Column("Площадь 1 шт, м²", 1, true),
                new Column("Масса 1 шт, кг", 0.95, true),
                new Column("Масса всего, кг", 1, true),
                new Column("Рез всего, м", 0.95, true),
                new Column("Врезки всего", 0.85, true)
            });
            _detailColumns = Layout(list.ToArray());
            HeaderRow(_detailColumns);

            int number = 0;
            string group = null;
            foreach (var item in _report.SortedItems())
            {
                string title = (item.Material.Length > 0 ? item.Material : "Материал не задан") + " · " +
                               ReportFormat.Number(item.Thickness, "0.0#") + " мм";
                if (title != group)
                {
                    group = title;
                    GroupRow(title);
                }

                number++;
                var cells = new List<string> { number.ToString() };
                if (flats)
                    cells.Add(string.Empty);
                cells.AddRange(new[]
                {
                    item.Title,
                    item.Description,
                    item.SizeText,
                    ReportFormat.Integer(item.Quantity),
                    ReportFormat.Number(item.Area, "0.0000"),
                    ReportFormat.Number(item.Mass, "0.000"),
                    ReportFormat.Number(item.TotalMass, "0.00"),
                    ReportFormat.Number(item.TotalCutLength, "0.00"),
                    ReportFormat.Integer(item.TotalPierces)
                });

                var flat = item.Flat;
                Row(_detailColumns, cells.ToArray(), flats ? FlatRowHeight : RowHeight,
                    drawExtra: flats && flat != null && !flat.IsEmpty ? (x, y, w, h) => DrawFlat(flat, x, y, w, h) : (Action<double, double, double, double>)null,
                    extraColumn: flats ? 1 : -1);
            }

            var t = _report.Totals();
            var totals = new List<string> { string.Empty };
            if (flats)
                totals.Add(string.Empty);
            totals.AddRange(new[]
            {
                "Итого", string.Empty, string.Empty, ReportFormat.Integer(t.Quantity), string.Empty, string.Empty,
                ReportFormat.Number(t.Mass, "0.00"), ReportFormat.Number(t.CutLength, "0.00"), ReportFormat.Integer(t.Pierces)
            });
            Row(_detailColumns, totals.ToArray(), RowHeight, bold: true, fill: HeaderFill);
            _y += 14;
        }

        private void Notes()
        {
            var notes = _report.SortedItems().Where(i => i.Note.Length > 0).ToList();
            if (notes.Count == 0)
                return;
            SectionTitle("Замечания");
            foreach (var item in notes)
            {
                var lines = Wrap($"{item.Title}: {item.Note}", Font, false, ContentWidth - 12, 3);
                if (!Fits(lines.Count * 11 + 4))
                    NewPage();
                _page.Text(Margin, _y + 9, "•", Font, true, Warning);
                foreach (string line in lines)
                {
                    _page.Text(Margin + 10, _y + 9, line, Font, false, Text);
                    _y += 11;
                }
                _y += 3;
            }
        }

        #endregion

        /// <summary>Развертка, вписанная в ячейку: контур тёмным, линии гиба синим.</summary>
        private void DrawFlat(FlatDrawing flat, double x, double y, double width, double height)
        {
            double w = Math.Max(flat.Width, 1e-6), h = Math.Max(flat.Height, 1e-6);
            double scale = Math.Min(width / w, height / h);
            double ox = x + (width - w * scale) / 2, oy = y + (height - h * scale) / 2;
            foreach (var p in flat.Polylines.Where(p => p.Kind != LayerKind.Auxiliary))
            {
                var points = p.Points.Select(pt => (ox + (pt.X - flat.MinX) * scale, oy + (flat.MaxY - pt.Y) * scale)).ToList();
                bool bend = p.Kind == LayerKind.Bend;
                _page.Polyline(points, p.Closed, bend ? 0.35 : 0.5, bend ? Accent : Profile);
            }
        }

        #region Текст

        /// <summary>Обрезает текст по ширине с многоточием.</summary>
        private string Fit(string text, double size, bool bold, double width)
        {
            if (string.IsNullOrEmpty(text) || _pdf.Measure(text, size, bold) <= width)
                return text ?? string.Empty;
            const string ellipsis = "…";
            int lo = 0, hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (_pdf.Measure(text.Substring(0, mid) + ellipsis, size, bold) <= width)
                    lo = mid;
                else
                    hi = mid - 1;
            }
            return text.Substring(0, lo).TrimEnd() + ellipsis;
        }

        /// <summary>Перенос по словам, не больше maxLines строк (последняя — с многоточием).</summary>
        private List<string> Wrap(string text, double size, bool bold, double width, int maxLines)
        {
            var lines = new List<string>();
            string current = string.Empty;
            foreach (string word in (text ?? string.Empty).Split(' '))
            {
                string candidate = current.Length == 0 ? word : current + " " + word;
                if (_pdf.Measure(candidate, size, bold) <= width || current.Length == 0)
                    current = candidate;
                else
                {
                    lines.Add(current);
                    current = word;
                }
            }
            if (current.Length > 0)
                lines.Add(current);
            if (lines.Count > maxLines)
            {
                lines = lines.Take(maxLines).ToList();
                lines[maxLines - 1] = Fit(lines[maxLines - 1] + "…", size, bold, width);
            }
            return lines.Select(l => Fit(l, size, bold, width)).ToList();
        }

        #endregion
    }
}

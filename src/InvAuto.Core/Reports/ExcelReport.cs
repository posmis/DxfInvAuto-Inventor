using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.Reports
{
    /// <summary>
    /// Ведомость в Excel: лист «Сводка» (по материалам и толщинам) и лист «Детали» (с картинками разверток,
    /// автофильтром и формулами итогов). Печать: A4 альбомная, по ширине листа, шапка таблицы повторяется,
    /// в колонтитулах — название, дата и «Лист N из M».
    /// </summary>
    public static class ExcelReport
    {
        private const int FlatWidthPx = 120, FlatHeightPx = 52;

        /// <param name="flatPng">Картинка развертки позиции (PNG) или null; вызывается, если в отчёте включены развертки.</param>
        public static XlsxWorkbook Build(ProductionReport report, Func<ReportItem, byte[]> flatPng = null)
        {
            var book = new XlsxWorkbook { Title = report.Title };
            Summary(book.AddSheet("Сводка"), report);
            Details(book.AddSheet("Детали"), report, flatPng);
            return book;
        }

        public static void Write(ProductionReport report, string path, Func<ReportItem, byte[]> flatPng = null) =>
            Build(report, flatPng).Save(path);

        private static void PageSetup(XlsxSheet sheet, ProductionReport report)
        {
            sheet.Header = "&L&\"Arial,Bold\"" + report.Title.Replace("&", "&&") + "&R" + report.DateText;
            sheet.Footer = "&LInvAuto&RЛист &P из &N";
        }

        /// <summary>Строки 1–2: название и изделие с датой.</summary>
        private static void Title(XlsxSheet sheet, ProductionReport report, int lastColumn)
        {
            sheet.Set(0, 0, report.Title, XlsxStyle.Title);
            sheet.Merge(0, 0, lastColumn);
            sheet.Height(0, 24);
            string subtitle = string.Join(" · ", new[] { report.Subtitle, "дата: " + report.DateText }.Where(s => s.Length > 0));
            sheet.Set(1, 0, subtitle, XlsxStyle.Subtitle);
            sheet.Merge(1, 0, lastColumn);
        }

        private static void Summary(XlsxSheet sheet, ProductionReport report)
        {
            PageSetup(sheet, report);
            string[] headers = { "Материал", "Толщина, мм", "Позиций", "Деталей, шт", "Площадь, м²", "Масса, кг", "Длина реза, м", "Врезки, шт" };
            double[] widths = { 34, 12, 10, 12, 13, 13, 14, 12 };
            Title(sheet, report, headers.Length - 1);

            const int head = 3;
            for (int c = 0; c < headers.Length; c++)
            {
                sheet.Set(head, c, headers[c], XlsxStyle.Header);
                sheet.Width(c, widths[c]);
            }
            sheet.Height(head, 30);
            sheet.FreezeRows = head + 1;
            sheet.PrintTitleRows = (head + 1, head + 1);

            int row = head + 1;
            foreach (var g in report.Groups())
            {
                sheet.Set(row, 0, g.MaterialTitle, XlsxStyle.Text);
                sheet.Set(row, 1, g.Thickness, XlsxStyle.Thickness);
                sheet.Set(row, 2, g.Positions, XlsxStyle.Integer);
                sheet.Set(row, 3, g.Quantity, XlsxStyle.Integer);
                Number(sheet, row, 4, g.Area, XlsxStyle.Decimal3);
                Number(sheet, row, 5, g.Mass, XlsxStyle.Decimal2);
                Number(sheet, row, 6, g.CutLength, XlsxStyle.Decimal2);
                Number(sheet, row, 7, g.Pierces, XlsxStyle.Integer);
                row++;
            }

            var t = report.Totals();
            sheet.Set(row, 0, "Итого", XlsxStyle.TotalText);
            sheet.Set(row, 1, null, XlsxStyle.TotalText);
            Sum(sheet, row, 2, head + 1, row - 1, t.Positions, XlsxStyle.TotalInteger);
            Sum(sheet, row, 3, head + 1, row - 1, t.Quantity, XlsxStyle.TotalInteger);
            Sum(sheet, row, 4, head + 1, row - 1, t.Area, XlsxStyle.TotalDecimal3);
            Sum(sheet, row, 5, head + 1, row - 1, t.Mass, XlsxStyle.TotalDecimal2);
            Sum(sheet, row, 6, head + 1, row - 1, t.CutLength, XlsxStyle.TotalDecimal2);
            Sum(sheet, row, 7, head + 1, row - 1, t.Pierces, XlsxStyle.TotalInteger);

            if (t.Incomplete > 0)
                sheet.Set(row + 2, 0, $"Без площади или массы: {t.Incomplete} поз. — итоги неполные, см. примечания на листе «Детали».", XlsxStyle.Warning);
        }

        private static void Details(XlsxSheet sheet, ProductionReport report, Func<ReportItem, byte[]> flatPng)
        {
            PageSetup(sheet, report);
            bool flats = report.IncludeFlats && flatPng != null;
            var columns = new List<(string Title, double Width)> { ("№", 5) };
            if (flats)
                columns.Add(("Развертка", 18));
            columns.AddRange(new[]
            {
                ("Обозначение", 22.0), ("Наименование", 30.0), ("Материал", 16.0), ("Толщина, мм", 10.0), ("Кол-во, шт", 9.0),
                ("Габариты, мм", 14.0), ("Площадь 1 шт, м²", 11.0), ("Масса 1 шт, кг", 11.0), ("Масса всего, кг", 11.0),
                ("Длина реза всего, м", 12.0), ("Врезки всего", 10.0), ("Примечание", 30.0)
            });
            int last = columns.Count - 1;
            Title(sheet, report, last);

            const int head = 3;
            for (int c = 0; c < columns.Count; c++)
            {
                sheet.Set(head, c, columns[c].Title, XlsxStyle.Header);
                sheet.Width(c, columns[c].Width);
            }
            sheet.Height(head, 30);
            sheet.FreezeRows = head + 1;
            sheet.PrintTitleRows = (head + 1, head + 1);

            int o = flats ? 1 : 0; // сдвиг колонок, если есть картинки
            int row = head + 1, number = 0;
            foreach (var item in report.SortedItems())
            {
                number++;
                sheet.Set(row, 0, number, XlsxStyle.Integer);
                if (flats)
                {
                    sheet.Set(row, 1, null, XlsxStyle.Text);
                    sheet.Height(row, FlatHeightPx * 0.75 + 6);
                    byte[] png = item.Flat != null && !item.Flat.IsEmpty ? flatPng(item) : null;
                    if (png != null)
                        sheet.Images.Add(new XlsxImage { Row = row, Column = 1, OffsetX = 4, OffsetY = 4, Width = FlatWidthPx, Height = FlatHeightPx, Png = png });
                }
                sheet.Set(row, o + 1, item.Title, XlsxStyle.Text);
                sheet.Set(row, o + 2, item.Description, XlsxStyle.TextWrap);
                sheet.Set(row, o + 3, item.Material.Length > 0 ? item.Material : "не задан", XlsxStyle.Text);
                sheet.Set(row, o + 4, item.Thickness, XlsxStyle.Thickness);
                sheet.Set(row, o + 5, item.Quantity, XlsxStyle.Integer);
                sheet.Set(row, o + 6, item.SizeText, XlsxStyle.Text);
                Number(sheet, row, o + 7, item.Area, XlsxStyle.Decimal4);
                Number(sheet, row, o + 8, item.Mass, XlsxStyle.Decimal3);
                Number(sheet, row, o + 9, item.TotalMass, XlsxStyle.Decimal2);
                Number(sheet, row, o + 10, item.TotalCutLength, XlsxStyle.Decimal2);
                Number(sheet, row, o + 11, item.TotalPierces, XlsxStyle.Integer);
                sheet.Set(row, o + 12, item.Note, XlsxStyle.TextWrap);
                row++;
            }

            int first = head + 1, end = row - 1;
            sheet.AutoFilter = $"{XlsxSheet.Ref(head, 0)}:{XlsxSheet.Ref(Math.Max(end, head), last)}";

            var t = report.Totals();
            for (int c = 0; c <= last; c++)
                sheet.Set(row, c, null, XlsxStyle.TotalText);
            sheet.Set(row, o + 1, "Итого", XlsxStyle.TotalText);
            if (end >= first)
            {
                Sum(sheet, row, o + 5, first, end, t.Quantity, XlsxStyle.TotalInteger);
                Sum(sheet, row, o + 9, first, end, t.Mass, XlsxStyle.TotalDecimal2);
                Sum(sheet, row, o + 10, first, end, t.CutLength, XlsxStyle.TotalDecimal2);
                Sum(sheet, row, o + 11, first, end, t.Pierces, XlsxStyle.TotalInteger);
            }
        }

        /// <summary>Нет значения — прочерк (а не ноль), чтобы неполные данные было видно.</summary>
        private static void Number(XlsxSheet sheet, int row, int column, double? value, XlsxStyle style)
        {
            if (value is double v)
                sheet.Set(row, column, v, style);
            else
                sheet.Set(row, column, "—", XlsxStyle.Muted);
        }

        private static void Number(XlsxSheet sheet, int row, int column, int? value, XlsxStyle style) =>
            Number(sheet, row, column, value.HasValue ? value.Value : (double?)null, style);

        private static void Sum(XlsxSheet sheet, int row, int column, int from, int to, double? cached, XlsxStyle style)
        {
            string col = XlsxSheet.ColumnName(column);
            sheet.Formula(row, column, $"SUM({col}{from + 1}:{col}{to + 1})", cached ?? 0, style);
        }

        private static void Sum(XlsxSheet sheet, int row, int column, int from, int to, int? cached, XlsxStyle style) =>
            Sum(sheet, row, column, from, to, cached.HasValue ? cached.Value : (double?)null, style);
    }
}

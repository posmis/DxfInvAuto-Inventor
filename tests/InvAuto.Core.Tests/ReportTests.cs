using System;
using System.IO;
using System.Linq;
using System.Text;
using InvAuto.Core.Preview;
using InvAuto.Core.Reports;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>Ведомость: группировка и итоги, PDF на листах A4, Excel.</summary>
    public class ReportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InvAutoReport_" + Guid.NewGuid().ToString("N"));

        public ReportTests()
        {
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        /// <summary>Шрифт с кириллицей: Arial в Windows, Liberation Sans в Linux. null — шрифта нет, тест пропускается.</summary>
        internal static (TrueTypeFont Regular, TrueTypeFont Bold) Fonts()
        {
            var pairs = new[]
            {
                (@"C:\Windows\Fonts\arial.ttf", @"C:\Windows\Fonts\arialbd.ttf"),
                ("/usr/share/fonts/truetype/liberation/LiberationSans-Regular.ttf", "/usr/share/fonts/truetype/liberation/LiberationSans-Bold.ttf"),
                ("/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf", "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf")
            };
            foreach (var (regular, bold) in pairs)
                if (File.Exists(regular) && File.Exists(bold))
                    return (TrueTypeFont.Load(regular), TrueTypeFont.Load(bold));
            return (null, null);
        }

        internal static FlatDrawing Plate(double w, double h)
        {
            var sb = new StringBuilder("0\nSECTION\n2\nENTITIES\n");
            void Line(double x1, double y1, double x2, double y2, string layer = "IV_OUTER_PROFILE") =>
                sb.Append(FormattableString.Invariant($"0\nLINE\n8\n{layer}\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}\n"));
            Line(0, 0, w, 0);
            Line(w, 0, w, h);
            Line(w, h, 0, h);
            Line(0, h, 0, 0);
            sb.Append(FormattableString.Invariant($"0\nCIRCLE\n8\nIV_INTERIOR_PROFILES\n10\n{w / 4}\n20\n{h / 2}\n40\n{Math.Min(w, h) / 8}\n"));
            Line(w / 2, 0, w / 2, h, "IV_BEND");
            sb.Append("0\nENDSEC\n0\nEOF\n");
            return DxfReader.Read(new StringReader(sb.ToString()));
        }

        internal static ProductionReport Sample(int extraItems = 0)
        {
            var report = new ProductionReport
            {
                Title = "Ведомость листовых деталей",
                Product = "Стойка опорная АБВГ.301318.001",
                Date = new DateTime(2026, 10, 26),
                ProductQuantity = 2
            };

            void Add(string number, string name, string material, double thickness, int qty, double w, double h, bool complete = true)
            {
                var flat = Plate(w, h);
                var metrics = FlatMetrics.Compute(flat);
                report.Items.Add(new ReportItem
                {
                    Key = number,
                    PartNumber = number,
                    Description = name,
                    FileName = number,
                    Material = material,
                    Thickness = thickness,
                    Quantity = qty,
                    FlatLength = w,
                    FlatWidth = h,
                    Flat = complete ? flat : null,
                    Area = complete ? metrics.Area * 1e-6 : (double?)null,
                    Mass = complete && material.Length > 0 ? metrics.Mass(thickness, 7850) : (double?)null,
                    CutLength = complete ? metrics.CutLength / 1000 : (double?)null,
                    Pierces = complete ? metrics.Pierces : (int?)null,
                    Note = complete ? (material.Length == 0 ? "нет материала толщиной " + thickness + " мм в библиотеке" : string.Empty) : "развертка не сформирована"
                });
            }

            Add("АБВГ.301318.010", "Кронштейн", "Сталь", 3, 4, 200, 100);
            Add("АБВГ.301318.002", "Пластина опорная", "Сталь", 3, 2, 300, 150);
            Add("АБВГ.301318.011", "Ребро жёсткости длинное с очень длинным наименованием для проверки переноса", "Сталь", 2, 8, 120, 60);
            Add("АБВГ.301318.003", "Косынка", "АМг3", 1.5, 6, 80, 80);
            Add("АБВГ.301318.020", "Крышка", "", 5, 1, 400, 250);
            Add("АБВГ.301318.021", "Щиток", "Сталь", 1, 2, 150, 90, complete: false);
            for (int i = 0; i < extraItems; i++)
                Add($"АБВГ.301318.{100 + i:000}", "Пластина " + (i + 1), "Сталь", i % 2 == 0 ? 2 : 3, 1 + i % 5, 100 + i * 7, 60 + i * 3);
            return report;
        }

        [Fact]
        public void Groups_SumByMaterialAndThickness_TotalsMatchItems()
        {
            var report = Sample();
            var groups = report.Groups();

            Assert.Equal(new[] { "АМг3", "Сталь", "Сталь", "Сталь", "" }, groups.Select(g => g.Material));
            Assert.Equal(new[] { 1.5, 1, 2, 3, 5 }, groups.Select(g => g.Thickness));
            var steel3 = groups.Single(g => g.Material == "Сталь" && g.Thickness == 3);
            Assert.Equal(2, steel3.Positions);
            Assert.Equal(6, steel3.Quantity);
            Assert.Equal(1, groups.Single(g => g.Thickness == 1).Incomplete);

            var totals = report.Totals();
            Assert.Equal(report.Items.Sum(i => i.Quantity), totals.Quantity);
            Assert.Equal(report.Items.Sum(i => i.TotalMass ?? 0), totals.Mass.Value, 9);
            Assert.Equal(report.Items.Sum(i => i.TotalCutLength ?? 0), totals.CutLength.Value, 9);
            Assert.Null(groups.Single(g => g.Thickness == 1).Mass);   // у группы нет данных — прочерк, а не ноль
            Assert.Null(groups.Single(g => g.Material == "").Mass);    // нет материала — массы нет
            Assert.NotNull(groups.Single(g => g.Material == "").CutLength);
            Assert.Equal(2, totals.Incomplete); // щиток без развертки, крышка без материала
        }

        [Fact]
        public void SortedItems_ByMaterialThicknessAndNumberNaturally()
        {
            var order = Sample().SortedItems().Select(i => i.PartNumber).ToList();
            Assert.Equal(new[] { "АБВГ.301318.003", "АБВГ.301318.021", "АБВГ.301318.011", "АБВГ.301318.002", "АБВГ.301318.010", "АБВГ.301318.020" }, order);
        }

        [Fact]
        public void SuggestedFileName_HasNoInvalidCharacters()
        {
            var report = new ProductionReport { Title = "Ведомость: \"цех\" 1/2", Product = "Изделие*", Date = new DateTime(2026, 1, 2) };
            string name = report.SuggestedFileName();
            Assert.Equal("Ведомость- -цех- 1-2 Изделие- 2026-01-02", name);
            Assert.True(name.IndexOfAny(Path.GetInvalidFileNameChars()) < 0);
        }

        [Fact]
        public void TrueType_ReadsCyrillicGlyphsAndWidths()
        {
            var (regular, bold) = Fonts();
            if (regular == null)
                return;

            Assert.NotEqual(0, regular.GlyphOf('Ж'));
            Assert.NotEqual(0, regular.GlyphOf('ё'));
            Assert.NotEqual(0, regular.GlyphOf('²'));
            Assert.Equal(0, regular.GlyphOf(0x10FFFD));
            Assert.True(regular.Measure("Ведомость", 10) > regular.Measure("Ведом", 10));
            Assert.True(bold.Measure("Ведомость", 10) >= regular.Measure("Ведомость", 10));
            Assert.NotEqual(regular.PostScriptName, bold.PostScriptName);
            Assert.Throws<InvalidDataException>(() => TrueTypeFont.Parse(new byte[] { 0x74, 0x74, 0x63, 0x66, 0, 0, 0, 0, 0, 0, 0, 0 }));
        }

        [Fact]
        public void Pdf_IsValidStructure_A4Landscape_WithPageNumbers()
        {
            var (regular, bold) = Fonts();
            if (regular == null)
                return;

            var pdf = PdfReport.Build(Sample(extraItems: 40), regular, bold);
            string path = Path.Combine(_dir, "report.pdf");
            pdf.Save(path);

            Assert.True(pdf.Pages.Count >= 3);                    // длинная ведомость переносится на листы
            Assert.All(pdf.Pages, p =>
            {
                Assert.Equal(PdfDocument.A4Long, p.Width, 2);     // альбомный A4
                Assert.Equal(PdfDocument.A4Short, p.Height, 2);
            });

            string text = Encoding.GetEncoding("ISO-8859-1").GetString(File.ReadAllBytes(path));
            Assert.StartsWith("%PDF-1.4", text);
            Assert.EndsWith("%%EOF\n", text);
            Assert.Contains($"/Count {pdf.Pages.Count}", text);
            Assert.Contains("/FontFile2", text);
            Assert.Contains("/ToUnicode", text);

            // Смещения xref указывают на начала объектов.
            int xref = int.Parse(text.Substring(text.LastIndexOf("startxref", StringComparison.Ordinal) + 10).Split('\n')[0]);
            var entries = text.Substring(xref).Split('\n').Skip(3).TakeWhile(l => l.EndsWith(" n ")).ToList();
            for (int id = 1; id <= entries.Count; id++)
            {
                long offset = long.Parse(entries[id - 1].Substring(0, 10));
                Assert.StartsWith($"{id} 0 obj", text.Substring((int)offset));
            }
        }

        /// <summary>PNG 2×2 — заглушка картинки развертки (в Inventor её рисует WPF).</summary>
        internal static readonly byte[] TinyPng = Convert.FromBase64String(
            "iVBORw0KGgoAAAANSUhEUgAAAAIAAAACCAIAAAD91JpzAAAAEElEQVR4nGPQ1jcGIgYIBQAPWgI1W0GngwAAAABJRU5ErkJggg==");

        [Fact]
        public void Excel_IsValidPackage_WithSheetsFormulasImagesAndPrintSetup()
        {
            string path = Path.Combine(_dir, "report.xlsx");
            ExcelReport.Write(Sample(), path, _ => TinyPng);

            using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
            {
                string Read(string name)
                {
                    var entry = zip.GetEntry(name);
                    Assert.NotNull(entry);
                    using (var reader = new StreamReader(entry.Open()))
                        return reader.ReadToEnd();
                }

                // Каждая XML-часть корректна.
                foreach (var entry in zip.Entries.Where(e => e.FullName.EndsWith(".xml") || e.FullName.EndsWith(".rels")))
                    System.Xml.Linq.XDocument.Parse(Read(entry.FullName));

                string workbook = Read("xl/workbook.xml");
                Assert.Contains("name=\"Сводка\"", workbook);
                Assert.Contains("name=\"Детали\"", workbook);
                Assert.Contains("_xlnm.Print_Titles", workbook);

                string details = Read("xl/worksheets/sheet2.xml");
                Assert.Contains("SUM(", details);
                Assert.Contains("<autoFilter", details);
                Assert.Contains("orientation=\"landscape\"", details);
                Assert.Contains("paperSize=\"9\"", details);
                Assert.Contains("Лист &amp;P из &amp;N", details);
                Assert.Contains("Кронштейн", details);
                Assert.Contains("<drawing r:id=", details);

                Assert.Equal(5, zip.Entries.Count(e => e.FullName.StartsWith("xl/media/"))); // у щитка нет развертки — нет и картинки
                Assert.Contains("image/png", Read("[Content_Types].xml"));
            }
        }

        [Fact]
        public void Excel_WithoutFlats_HasNoPictureColumn()
        {
            var report = Sample();
            report.IncludeFlats = false;
            string path = Path.Combine(_dir, "plain.xlsx");
            ExcelReport.Write(report, path, _ => TinyPng);
            using (var zip = System.IO.Compression.ZipFile.OpenRead(path))
            {
                Assert.DoesNotContain(zip.Entries, e => e.FullName.StartsWith("xl/media/"));
                using (var reader = new StreamReader(zip.GetEntry("xl/worksheets/sheet2.xml").Open()))
                    Assert.DoesNotContain("Развертка", reader.ReadToEnd());
            }
        }

        [Fact]
        public void Xlsx_ColumnNamesAndReferences()
        {
            Assert.Equal("A", XlsxSheet.ColumnName(0));
            Assert.Equal("Z", XlsxSheet.ColumnName(25));
            Assert.Equal("AA", XlsxSheet.ColumnName(26));
            Assert.Equal("AZ", XlsxSheet.ColumnName(51));
            Assert.Equal("C5", XlsxSheet.Ref(4, 2));
        }

        /// <summary>Пробный файл для просмотра глазами: INVAUTO_SAMPLE_DIR=папка dotnet test.</summary>
        [Fact]
        public void WriteSamples_WhenRequested()
        {
            string dir = Environment.GetEnvironmentVariable("INVAUTO_SAMPLE_DIR");
            var (regular, bold) = Fonts();
            if (string.IsNullOrEmpty(dir) || regular == null)
                return;
            Directory.CreateDirectory(dir);
            PdfReport.Write(Sample(extraItems: 30), Path.Combine(dir, "sample.pdf"), regular, bold);
            ExcelReport.Write(Sample(extraItems: 30), Path.Combine(dir, "sample.xlsx"), _ => TinyPng);
        }
    }
}

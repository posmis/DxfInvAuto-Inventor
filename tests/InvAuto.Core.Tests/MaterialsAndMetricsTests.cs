using System;
using System.IO;
using System.Linq;
using System.Text;
using InvAuto.Core.Logging;
using InvAuto.Core.Materials;
using InvAuto.Core.Preview;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>Библиотека материалов, назначение деталям, площадь, масса, длина реза и врезки.</summary>
    public class MaterialsAndMetricsTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InvAutoMat_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        private MaterialLibraryStore Store() => new MaterialLibraryStore(_dir, new FileLogger(Path.Combine(_dir, "logs")));

        #region Библиотека

        [Fact]
        public void Starter_IsSteelForEachThickness_DefaultForIt()
        {
            var lib = MaterialLibrary.CreateStarter();

            Assert.Equal(new[] { 0.5, 1.0, 1.5, 2.0, 3.0, 5.0 }, lib.Materials.Select(m => m.Thickness));
            Assert.All(lib.Materials, m =>
            {
                Assert.Equal("Сталь", m.Name);
                Assert.Equal(7850, m.Density);
                Assert.True(m.IsDefault);
            });
            Assert.Equal(6, lib.Materials.Select(m => m.Id).Distinct().Count());
            Assert.Empty(lib.Validate());
        }

        [Fact]
        public void DefaultFor_UsesFlagged_ThenSingle_ToleratesRounding()
        {
            var lib = new MaterialLibrary();
            var a = new Material { Name = "Ст3", Thickness = 2, Density = 7850 };
            var b = new Material { Name = "08пс", Thickness = 2, Density = 7850 };
            var c = new Material { Name = "АМг3", Thickness = 1.5, Density = 2670 };
            lib.Materials.AddRange(new[] { a, b, c });

            Assert.Null(lib.DefaultFor(2));             // два материала, ни один не отмечен — не угадываем
            Assert.Same(c, lib.DefaultFor(1.5004));      // единственный такой толщины
            Assert.Null(lib.DefaultFor(4));

            lib.SetDefault(b);
            Assert.Same(b, lib.DefaultFor(1.9999));
            lib.SetDefault(a);
            Assert.False(b.IsDefault);                   // по умолчанию — только один на толщину
            Assert.True(c.IsDefault == false);           // другие толщины не затронуты
        }

        [Fact]
        public void Normalize_KeepsOneDefaultPerThickness()
        {
            var lib = new MaterialLibrary();
            lib.Materials.Add(new Material { Name = "A", Thickness = 3, Density = 7850, IsDefault = true });
            lib.Materials.Add(new Material { Name = "B", Thickness = 3.001, Density = 7850, IsDefault = true });
            lib.Materials.Add(new Material { Name = "C", Thickness = 5, Density = 7850, IsDefault = true, Id = "" });
            lib.Normalize();

            Assert.Equal(new[] { true, false, true }, lib.Materials.Select(m => m.IsDefault));
            Assert.NotEmpty(lib.Materials[2].Id);
        }

        [Fact]
        public void Validate_ReportsEmptyNamesBadNumbersAndDuplicates()
        {
            var lib = new MaterialLibrary();
            lib.Materials.Add(new Material { Name = " ", Thickness = 1, Density = 7850 });
            lib.Materials.Add(new Material { Name = "Сталь", Thickness = 0, Density = 7850 });
            lib.Materials.Add(new Material { Name = "Сталь", Thickness = 2, Density = 10 });
            lib.Materials.Add(new Material { Name = "сталь ", Thickness = 2, Density = 7850 });

            var errors = lib.Validate();
            Assert.Contains(errors, e => e.Contains("Строка 1") && e.Contains("наименование"));
            Assert.Contains(errors, e => e.Contains("Строка 2") && e.Contains("толщина"));
            Assert.Contains(errors, e => e.Contains("Строка 3") && e.Contains("плотность"));
            Assert.Contains(errors, e => e.Contains("Повторяется"));
        }

        [Fact]
        public void Resolve_ManualThenByThickness_FallsBackWhenManualDeleted()
        {
            var lib = MaterialLibrary.CreateStarter();
            var alu = new Material { Name = "АМг3", Thickness = 2, Density = 2670 };
            lib.Materials.Add(alu);
            var assignments = new MaterialAssignments();
            const string part = @"C:\Детали\Кронштейн.ipt";

            var auto = ResolvedMaterial.Resolve(lib, assignments, part, 2);
            Assert.Equal(MaterialSource.ByThickness, auto.Source);
            Assert.Equal("Сталь", auto.Material.Name);

            assignments.Set(part, alu.Id);
            var manual = ResolvedMaterial.Resolve(lib, assignments, part.ToUpperInvariant(), 2);
            Assert.Equal(MaterialSource.Manual, manual.Source);
            Assert.Same(alu, manual.Material);
            Assert.False(manual.ThicknessMismatch);
            Assert.True(ResolvedMaterial.Resolve(lib, assignments, part, 3).ThicknessMismatch);

            lib.Materials.Remove(alu); // материал удалили из библиотеки — деталь снова по толщине
            Assert.Equal(MaterialSource.ByThickness, ResolvedMaterial.Resolve(lib, assignments, part, 2).Source);
            Assert.Equal(MaterialSource.None, ResolvedMaterial.Resolve(lib, assignments, part, 7).Source);

            assignments.Set(part, null);
            Assert.Empty(assignments.Parts);
        }

        [Fact]
        public void Store_CreatesStarterOnce_AndNeverReseeds()
        {
            var store = Store();
            var first = store.Load();
            Assert.Equal(6, first.Materials.Count);
            Assert.True(File.Exists(store.FilePath));

            first.Materials.Clear(); // пользователь удалил всё — это его выбор, стартовые не возвращаем
            store.Save(first);
            Assert.Empty(store.Load().Materials);
        }

        [Fact]
        public void Store_KeepsBackups_AndRefusesInvalid()
        {
            var store = Store();
            var lib = store.Load();
            lib.Materials[0].Name = "Сталь 08пс ГОСТ 16523-97";
            store.Save(lib);

            Assert.Equal("Сталь 08пс ГОСТ 16523-97", store.Load().Materials[0].Name);
            Assert.NotEmpty(Directory.GetFiles(store.BackupFolder, "materials_*.xml"));

            lib.Materials[1].Name = "";
            Assert.Throws<InvalidOperationException>(() => store.Save(lib));
            Assert.Equal("Сталь", store.Load().Materials[1].Name); // файл не тронут
        }

        [Fact]
        public void Store_CorruptFile_IsNotOverwritten()
        {
            var store = Store();
            Directory.CreateDirectory(store.Folder);
            File.WriteAllText(store.FilePath, "<MaterialLibrary><Materials><Material>обрыв");

            Assert.Throws<MaterialLibraryException>(() => store.Load());
            Assert.Contains("обрыв", File.ReadAllText(store.FilePath));
        }

        [Fact]
        public void ExportImport_XmlAndCsv_RoundTrip()
        {
            Directory.CreateDirectory(_dir);
            var lib = MaterialLibrary.CreateStarter();
            lib.Materials.Add(new Material { Name = "Лист; \"особый\"", Thickness = 1.25, Density = 7900.5 });

            foreach (string ext in new[] { ".xml", ".csv" })
            {
                string path = Path.Combine(_dir, "export" + ext);
                MaterialLibraryStore.Export(lib, path);
                var back = MaterialLibraryStore.Import(path);

                Assert.Equal(7, back.Materials.Count);
                var special = back.Materials.Single(m => m.Thickness == 1.25);
                Assert.Equal("Лист; \"особый\"", special.Name);
                Assert.Equal(7900.5, special.Density, 6);
                Assert.Equal(6, back.Materials.Count(m => m.IsDefault));
            }

            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, File.ReadAllBytes(Path.Combine(_dir, "export.csv")).Take(3)); // Excel видит UTF-8
        }

        [Fact]
        public void Csv_AcceptsExcelStyleNumbers_AndReportsErrors()
        {
            var lib = MaterialLibraryStore.FromCsv("Наименование;Толщина, мм;Плотность, кг/м³;По умолчанию\r\n" +
                                                   "Ст3;2,5;7 850;да\r\n12Х18Н10Т;0.8;7900;\r\n\r\n");
            Assert.Equal(2, lib.Materials.Count);
            Assert.Equal(2.5, lib.Materials[0].Thickness);
            Assert.Equal(7850, lib.Materials[0].Density);
            Assert.True(lib.Materials[0].IsDefault);
            Assert.False(lib.Materials[1].IsDefault);

            var ex = Assert.Throws<FormatException>(() => MaterialLibraryStore.FromCsv("Сталь;два;7850"));
            Assert.Contains("Строка 1", ex.Message);
        }

        [Fact]
        public void Merge_AddsNewUpdatesDensity_NeverDeletes_KeepsLocalDefaults()
        {
            var lib = MaterialLibrary.CreateStarter();
            var incoming = new MaterialLibrary();
            incoming.Materials.Add(new Material { Name = "сталь", Thickness = 2, Density = 7800, IsDefault = true });
            incoming.Materials.Add(new Material { Name = "АМг3", Thickness = 2, Density = 2670, IsDefault = true });
            incoming.Materials.Add(new Material { Name = "АМг3", Thickness = 4, Density = 2670, IsDefault = true });

            var (added, updated) = lib.Merge(incoming);

            Assert.Equal(2, added);
            Assert.Equal(1, updated);
            Assert.Equal(8, lib.Materials.Count);
            Assert.Equal(7800, lib.DefaultFor(2).Density);          // по умолчанию для 2 мм осталась своя сталь
            Assert.Equal("АМг3", lib.DefaultFor(4).Name);            // для новой толщины — импортированный
        }

        #endregion

        #region Показатели развертки

        private static FlatDrawing Read(params string[] entities)
        {
            var sb = new StringBuilder("0\nSECTION\n2\nENTITIES\n");
            foreach (var e in entities)
                sb.Append(e);
            sb.Append("0\nENDSEC\n0\nEOF\n");
            return DxfReader.Read(new StringReader(sb.ToString()));
        }

        private static string Line(double x1, double y1, double x2, double y2, string layer = "IV_OUTER_PROFILE") =>
            FormattableString.Invariant($"0\nLINE\n8\n{layer}\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}\n");

        [Fact]
        public void Metrics_RectangleFromLooseLines_WithHoleAndSlot()
        {
            // Наружный контур 200×100 отдельными отрезками, вперемешку и в разных направлениях.
            var d = Read(
                Line(200, 0, 200, 100), Line(0, 0, 200, 0), Line(0, 100, 0, 0), Line(0, 100, 200, 100),
                "0\nCIRCLE\n8\nIV_INTERIOR_PROFILES\n10\n50\n20\n50\n40\n10\n",
                "0\nLWPOLYLINE\n8\nIV_INTERIOR_PROFILES\n90\n4\n70\n1\n10\n100\n20\n40\n10\n150\n20\n40\n10\n150\n20\n60\n10\n100\n20\n60\n",
                Line(20, 10, 180, 10, "IV_BEND"));

            var m = FlatMetrics.Compute(d);

            Assert.Equal(3, m.Pierces);
            Assert.Equal(0, m.OpenChains);
            Assert.Equal(20000, m.OuterArea, 6);
            Assert.Equal(20000 - Math.PI * 100 - 1000, m.Area, 1.0); // круг разбит на участки — точность до 1 мм²
            Assert.Equal(600 + 2 * Math.PI * 10 + 140, m.CutLength, 1.0);
            Assert.Equal(160, m.BendLength, 9);
            Assert.Equal(200, m.Width, 9);
            Assert.Equal(100, m.Height, 9);

            // 3 мм стали: (20000 − 314 − 1000) мм² × 3 мм × 7850 кг/м³
            Assert.Equal(0.44, m.Mass(3, 7850), 2);
        }

        [Fact]
        public void Metrics_OuterWithFilletArcs_IsClosed()
        {
            // 100×50 со скруглением R10 в одном углу: отрезки + дуга отдельными объектами.
            var d = Read(
                Line(0, 0, 100, 0), Line(100, 0, 100, 40),
                "0\nARC\n8\nIV_OUTER_PROFILE\n10\n90\n20\n40\n40\n10\n50\n0\n51\n90\n",
                Line(90, 50, 0, 50), Line(0, 50, 0, 0));

            var m = FlatMetrics.Compute(d);

            Assert.Equal(1, m.Pierces);
            Assert.Equal(0, m.OpenChains);
            Assert.Equal(5000 - (100 - Math.PI * 100 / 4), m.Area, 1.0);
        }

        [Fact]
        public void Metrics_BrokenContour_CountsOpenChain()
        {
            var d = Read(Line(0, 0, 100, 0), Line(100, 0, 100, 50), Line(100, 50, 0, 50)); // не хватает стороны
            var m = FlatMetrics.Compute(d);
            Assert.Equal(1, m.OpenChains);
            Assert.Equal(1, m.Pierces);
            Assert.Equal(0, m.Area);
            Assert.Equal(250, m.CutLength, 9);
        }

        [Fact]
        public void Metrics_EmptyDrawing_IsZero()
        {
            var m = FlatMetrics.Compute(FlatDrawing.Empty);
            Assert.Equal(0, m.Area);
            Assert.Equal(0, m.Pierces);
            Assert.Equal(0, FlatMetrics.Compute(null).CutLength);
        }

        #endregion
    }
}

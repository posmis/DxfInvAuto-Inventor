using System;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using InvAuto.Core.Bends;
using InvAuto.Core.Export;
using InvAuto.Core.Logging;
using InvAuto.Core.Settings;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class SettingsRoundTripTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InvAutoSettings_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        private XmlSettingsStore Store() => new XmlSettingsStore(_dir, new FileLogger(Path.Combine(_dir, "logs")));

        /// <summary>Каждое свойство переживает сохранение — ни одно не забыто сериализатором.</summary>
        [Fact]
        public void ExportSettings_EveryPropertyRoundTrips()
        {
            var original = new FlatPatternExportSettings
            {
                OutputFolder = @"D:\Раскрой\Заказ 17",
                FileNameTemplate = "{PartNumber}_{Qty}",
                SubfolderTemplate = "{Material}",
                SortByThickness = true,
                QuantityMultiplier = 7,
                Method = DxfExportMethod.FormatString,
                IniFilePath = @"C:\ini\мой.ini",
                AcadVersion = "2013",
                OuterProfileLayer = "CUT",
                InteriorProfilesLayer = "HOLES",
                BendUpLayer = "UP",
                BendDownLayer = "DOWN",
                ExportBendLines = false,
                MergeProfilesIntoPolyline = true,
                RebaseGeometry = true,
                CreateMissingFlatPatterns = false,
                OverwriteExisting = false,
                WriteCsvReport = false,
                OpenFolderAfterExport = false
            };

            Store().Save("export", original);
            var loaded = Store().Load<FlatPatternExportSettings>("export");

            foreach (var property in typeof(FlatPatternExportSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Assert.Equal(property.GetValue(original), property.GetValue(loaded));

            // и все значения в примере отличаются от значений по умолчанию — иначе проверка была бы слабой
            var defaults = new FlatPatternExportSettings();
            foreach (var property in typeof(FlatPatternExportSettings).GetProperties(BindingFlags.Public | BindingFlags.Instance))
                Assert.NotEqual(property.GetValue(defaults), property.GetValue(original));
        }

        [Fact]
        public void BendSettings_DefaultsAndOverridesRoundTrip()
        {
            var settings = new BendSettings
            {
                Default = new BendLineOptions { Mode = BendLineMode.Dashed, Spec = DashSpec.Count, DashCount = 7, DashLength = 12.5, Ends = DashEnds.Bridge }
            };
            settings.SetOverride(@"C:\Детали\Опора.ipt", new BendLineOptions { Mode = BendLineMode.RectSlots, SlotWidth = 3, SlotPattern = SlotPattern.Full });
            settings.SetOverride(@"C:\Детали\Ребро.ipt", new BendLineOptions { Mode = BendLineMode.Remove });

            Store().Save("bends", settings);
            var loaded = Store().Load<BendSettings>("bends");

            Assert.True(loaded.Default.SameAs(settings.Default));
            Assert.Equal(2, loaded.Parts.Count);
            Assert.True(loaded.For(@"c:\детали\опора.IPT").SameAs(settings.For(@"C:\Детали\Опора.ipt")));
            Assert.Equal(BendLineMode.Remove, loaded.For(@"C:\Детали\Ребро.ipt").Mode);
            Assert.True(loaded.For(@"C:\Другая.ipt").SameAs(settings.Default));

            // повторное сохранение не размножает персональные настройки
            Store().Save("bends", loaded);
            Assert.Equal(2, Store().Load<BendSettings>("bends").Parts.Count);
        }

        [Fact]
        public void SaveReplacesFile_AndLeavesNoTemp()
        {
            Store().Save("x", new FlatPatternExportSettings { QuantityMultiplier = 2 });
            Store().Save("x", new FlatPatternExportSettings { QuantityMultiplier = 3 });

            Assert.Equal(3, Store().Load<FlatPatternExportSettings>("x").QuantityMultiplier);
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }
    }

    public class BendLineOptionsTests
    {
        [Theory]
        [InlineData(BendLineMode.Keep, "как есть")]
        [InlineData(BendLineMode.Remove, "удалить")]
        [InlineData(BendLineMode.EdgeCuts, "прорези по краям")]
        [InlineData(BendLineMode.Dashed, "прерывистая")]
        [InlineData(BendLineMode.RectSlots, "вырезы")]
        public void Summary_NamesTheMode(BendLineMode mode, string expected)
        {
            Assert.StartsWith(expected, new BendLineOptions { Mode = mode }.Summary);
        }

        [Fact]
        public void Summary_ShowsSpecificParameters()
        {
            Assert.Contains("4 × 15", new BendLineOptions { Mode = BendLineMode.Dashed, Spec = DashSpec.Count, DashCount = 4, DashLength = 15 }.Summary);
            Assert.Contains("по всей длине", new BendLineOptions { Mode = BendLineMode.RectSlots, SlotPattern = SlotPattern.Full }.Summary);
        }

        [Fact]
        public void Clone_IsIndependent_AndSameAsComparesEveryField()
        {
            var a = new BendLineOptions { Mode = BendLineMode.Dashed, DashLength = 30 };
            var b = a.Clone();
            Assert.True(a.SameAs(b));

            foreach (var property in typeof(BendLineOptions).GetProperties().Where(p => p.CanWrite))
            {
                var c = a.Clone();
                object value = property.GetValue(c);
                object changed = value switch
                {
                    double d => d + 1,
                    int i => i + 1,
                    Enum e => Enum.GetValues(e.GetType()).Cast<object>().First(v => !v.Equals(e)),
                    string s => s + "X",
                    _ => throw new InvalidOperationException(property.Name)
                };
                property.SetValue(c, changed);
                Assert.False(a.SameAs(c), $"SameAs не учитывает {property.Name}");
            }
            Assert.False(a.SameAs(null));
        }
    }

    public class CsvReportTests : IDisposable
    {
        private readonly string _path = Path.Combine(Path.GetTempPath(), "InvAutoReport_" + Guid.NewGuid().ToString("N") + ".csv");

        public void Dispose()
        {
            if (File.Exists(_path))
                File.Delete(_path);
        }

        [Fact]
        public void Report_HasBom_Header_SortedRows_StatusesAndTotals()
        {
            var results = new[]
            {
                new ExportResult(new SheetMetalPart { Name = "Б", Material = "Сталь", ThicknessMm = 3, Quantity = 1 }, ExportStatus.Failed, "", "ошибка \"транслятора\""),
                new ExportResult(new SheetMetalPart { Name = "А", Material = "Сталь", ThicknessMm = 1.5, Quantity = 2 }, ExportStatus.Exported, @"C:\a.dxf", ""),
                new ExportResult(new SheetMetalPart { Name = "В", Material = "Алюминий", ThicknessMm = 2, Quantity = 4 }, ExportStatus.Skipped, "", "нет развертки")
            };

            CsvReport.Write(_path, results, quantityMultiplier: 2);

            byte[] bytes = File.ReadAllBytes(_path);
            Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes.Take(3));
            var lines = File.ReadAllLines(_path, Encoding.UTF8);
            Assert.StartsWith("Имя;Обозначение;Наименование;Материал;Толщина, мм;Кол-во;Статус", lines[0]);
            Assert.Equal(4, lines.Length);
            Assert.StartsWith("В;", lines[1]); // сначала по материалу, затем по толщине
            Assert.StartsWith("А;", lines[2]);
            Assert.StartsWith("Б;", lines[3]);
            Assert.Contains(";8;Пропущено;", lines[1]);
            Assert.Contains(";1,5;4;OK;", lines[2]);
            Assert.EndsWith(";Ошибка;;\"ошибка \"\"транслятора\"\"\"", lines[3]);
        }
    }

    public class FileLoggerTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InvAutoLog_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        [Fact]
        public void Writes_Levels_AndExceptionDetails()
        {
            var log = new FileLogger(_dir);
            log.Info("начало");
            log.Warn("внимание");
            log.Error("сбой", new InvalidOperationException("подробность"));

            string text = File.ReadAllText(log.CurrentLogFile);
            Assert.Contains("INFO начало", text);
            Assert.Contains("WARN внимание", text);
            Assert.Contains("ERROR сбой", text);
            Assert.Contains("подробность", text);
        }

        [Fact]
        public void Cleanup_RemovesOnlyOldLogs()
        {
            var log = new FileLogger(_dir);
            log.Info("сегодня");
            string old = Path.Combine(_dir, "2020-01-01.log");
            File.WriteAllText(old, "");
            File.SetLastWriteTime(old, DateTime.Now.AddDays(-90));

            Assert.Equal(1, log.Cleanup(TimeSpan.FromDays(60)));
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(log.CurrentLogFile));
        }

        [Fact]
        public void BrokenFolder_NeverThrows()
        {
            var log = new FileLogger(Path.Combine(_dir, "file.txt", "logs"));
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "file.txt"), "это файл, а не папка");

            log.Info("не упасть");
            Assert.Equal(0, log.Cleanup(TimeSpan.Zero));
        }
    }
}

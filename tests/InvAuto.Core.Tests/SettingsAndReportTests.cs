using System;
using System.IO;
using InvAuto.Core.Export;
using InvAuto.Core.Logging;
using InvAuto.Core.Settings;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class SettingsAndReportTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InvAutoTests_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        [Fact]
        public void Settings_RoundTrip()
        {
            var store = new XmlSettingsStore(_dir, new FileLogger(_dir));
            store.Save("export", new FlatPatternExportSettings { FileNameTemplate = "{PartNumber}", QuantityMultiplier = 5 });

            var loaded = store.Load<FlatPatternExportSettings>("export");
            Assert.Equal("{PartNumber}", loaded.FileNameTemplate);
            Assert.Equal(5, loaded.QuantityMultiplier);
        }

        [Fact]
        public void Settings_MissingOrCorrupted_ReturnsDefaults()
        {
            var store = new XmlSettingsStore(_dir, new FileLogger(_dir));
            Assert.Equal(1, store.Load<FlatPatternExportSettings>("missing").QuantityMultiplier);

            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "broken.xml"), "<not-xml");
            Assert.Equal(1, store.Load<FlatPatternExportSettings>("broken").QuantityMultiplier);

            // Испорченный файл сохранён в backup — следующее сохранение его не потеряет.
            var copy = Assert.Single(Directory.GetFiles(Path.Combine(_dir, "backup"), "broken_*повреждён*.xml"));
            Assert.Equal("<not-xml", File.ReadAllText(copy));
        }

        [Fact]
        public void Settings_SaveReplacesExistingFile_WithoutTempLeft()
        {
            var store = new XmlSettingsStore(_dir, new FileLogger(_dir));
            store.Save("export", new FlatPatternExportSettings { QuantityMultiplier = 2 });
            store.Save("export", new FlatPatternExportSettings { QuantityMultiplier = 3 });
            Assert.Equal(3, store.Load<FlatPatternExportSettings>("export").QuantityMultiplier);
            Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        }

        [Fact]
        public void CsvReport_EscapesSeparators()
        {
            Directory.CreateDirectory(_dir);
            string path = Path.Combine(_dir, "r.csv");
            var part = new SheetMetalPart { Name = "A;B", Material = "Сталь", ThicknessMm = 2.5, Quantity = 2 };
            CsvReport.Write(path, new[] { new ExportResult(part, ExportStatus.Exported, @"C:\x.dxf", "") }, 3);

            string[] lines = File.ReadAllLines(path);
            Assert.Equal(2, lines.Length);
            Assert.StartsWith("\"A;B\";", lines[1]);
            Assert.Contains(";2,5;6;OK;", lines[1]);
        }
    }
}

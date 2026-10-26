using System;
using System.IO;
using System.Linq;
using InvAuto.Core.Bends;
using InvAuto.Core.Export;
using InvAuto.Core.Logging;
using InvAuto.Core.Preview;
using InvAuto.Core.Settings;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>Индивидуальные параметры отдельных линий гиба детали.</summary>
    public class IndividualBendLineTests : IDisposable
    {
        private const string Part = @"C:\Детали\Опора.ipt";
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InvAutoLines_" + Guid.NewGuid().ToString("N"));

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        private static FlatDrawing Read(string dxf) => DxfReader.Read(new StringReader(dxf));

        [Fact]
        public void FindBendLines_ReportsIndexLayerLengthAndDirection()
        {
            var lines = DxfBendProcessor.FindBendLines(TestDxf.Inventor2004(), DxfBendProcessor.DefaultLayers);

            Assert.Equal(2, lines.Count);
            Assert.Equal(new[] { 1, 2 }, lines.Select(l => l.Index));
            Assert.Equal(200, lines[0].Length, 9);
            Assert.False(lines[0].IsDown);
            Assert.True(lines[1].IsDown);
            Assert.Equal(150, lines[1].X1, 9);
        }

        [Fact]
        public void LineKey_IgnoresDirection_AndTinyNoise_ButNotLayerOrPosition()
        {
            var a = new BendLineInfo(1, "IV_BEND", 0, 40, 200, 40);
            Assert.Equal(a.Key, new BendLineInfo(5, "iv_bend", 200, 40, 0, 40).Key);       // другой номер и направление
            Assert.Equal(a.Key, new BendLineInfo(1, "IV_BEND", 0.001, 40.002, 200, 39.999).Key); // шум расчёта
            Assert.Equal(new BendLineInfo(1, "IV_BEND", -0.001, 0, 1, 0).Key, new BendLineInfo(1, "IV_BEND", 0.001, 0, 1, 0).Key); // −0 и 0
            Assert.NotEqual(a.Key, new BendLineInfo(1, "IV_BEND_DOWN", 0, 40, 200, 40).Key);
            Assert.NotEqual(a.Key, new BendLineInfo(1, "IV_BEND", 0, 41, 200, 41).Key);
        }

        [Fact]
        public void EachLine_GetsItsOwnOptions()
        {
            string dxf = TestDxf.Inventor2004();
            var lines = DxfBendProcessor.FindBendLines(dxf, DxfBendProcessor.DefaultLayers);
            var settings = new BendSettings { Default = new BendLineOptions { Mode = BendLineMode.Dashed } };
            settings.SetLineOverride(Part, lines[1].Key, new BendLineOptions { Mode = BendLineMode.Remove });

            var result = DxfBendProcessor.Process(dxf, DxfBendProcessor.DefaultLayers, settings.Resolver(Part));

            Assert.Equal(BendLineMode.Dashed, result.Items[0].Options.Mode);
            Assert.Equal(BendLineMode.Remove, result.Items[1].Options.Mode);
            var bends = Read(result.Text).Polylines.Where(p => p.Kind == LayerKind.Bend).ToList();
            Assert.All(bends, b => Assert.Equal("IV_BEND", b.Layer)); // линия вниз удалена
            Assert.Equal(result.Items[0].Layout.Segments.Count, bends.Count);
            DxfValidator.AssertValid(result.Text);
        }

        [Fact]
        public void Resolver_UsesLineThenPartThenDefault()
        {
            var lines = DxfBendProcessor.FindBendLines(TestDxf.Inventor2004(), DxfBendProcessor.DefaultLayers);
            var settings = new BendSettings { Default = new BendLineOptions { Mode = BendLineMode.Dashed } };
            var resolve = settings.Resolver(Part);
            Assert.All(lines, l => Assert.Equal(BendLineMode.Dashed, resolve(l).Mode)); // только общие

            settings.SetOverride(Part, new BendLineOptions { Mode = BendLineMode.EdgeCuts });
            settings.SetLineOverride(Part, lines[0].Key, new BendLineOptions { Mode = BendLineMode.RectSlots });
            resolve = settings.Resolver(Part);
            Assert.Equal(BendLineMode.RectSlots, resolve(lines[0]).Mode); // своя у линии
            Assert.Equal(BendLineMode.EdgeCuts, resolve(lines[1]).Mode);  // как у детали
            Assert.Equal(BendLineMode.RectSlots, settings.ForLine(Part, lines[0].Key).Mode);
            Assert.Equal(BendLineMode.EdgeCuts, settings.ForLine(Part, lines[1].Key).Mode);
            Assert.Equal(BendLineMode.Dashed, settings.Resolver(@"C:\Другая.ipt")(lines[0]).Mode);
        }

        [Fact]
        public void LineOverrides_SurviveClearingPartOverride_AndViceVersa()
        {
            var settings = new BendSettings();
            settings.SetLineOverride(Part, "K1", new BendLineOptions { Mode = BendLineMode.Remove });
            Assert.False(settings.HasOverride(Part));          // у самой детали своих нет
            Assert.Equal(1, settings.LineOverrideCount(Part));

            settings.SetOverride(Part, new BendLineOptions { Mode = BendLineMode.Dashed });
            settings.ClearOverride(Part);
            Assert.True(settings.HasLineOverride(Part, "K1")); // настройка линии осталась

            settings.SetLineOverride(Part, "K1", new BendLineOptions { Mode = BendLineMode.EdgeCuts });
            Assert.Equal(1, settings.LineOverrideCount(Part)); // заменилась, а не добавилась

            settings.ClearLineOverride(Part, "K1");
            Assert.Empty(settings.Parts);                      // пустая запись детали убрана
        }

        [Fact]
        public void LineOverrides_ReadAndReplaceAll()
        {
            var settings = new BendSettings();
            settings.SetOverride(Part, new BendLineOptions { Mode = BendLineMode.Dashed });
            settings.SetLineOverrides(Part, new System.Collections.Generic.Dictionary<string, BendLineOptions>
            {
                ["A"] = new BendLineOptions { Mode = BendLineMode.Remove },
                ["B"] = new BendLineOptions { Mode = BendLineMode.EdgeCuts }
            });

            var read = settings.LineOverrides(Part);
            Assert.Equal(new[] { "A", "B" }, read.Keys.OrderBy(k => k));
            read["A"].Mode = BendLineMode.Keep; // это копии — настройки не меняются
            Assert.Equal(BendLineMode.Remove, settings.ForLine(Part, "A").Mode);

            settings.SetLineOverrides(Part, new System.Collections.Generic.Dictionary<string, BendLineOptions>());
            Assert.Equal(0, settings.LineOverrideCount(Part));
            Assert.True(settings.HasOverride(Part)); // настройка детали осталась
            Assert.Empty(settings.LineOverrides(@"C:\Нет.ipt"));
        }

        [Fact]
        public void ChangedGeometry_FallsBackToPartOptions()
        {
            var settings = new BendSettings();
            settings.SetOverride(Part, new BendLineOptions { Mode = BendLineMode.EdgeCuts });
            settings.SetLineOverride(Part, new BendLineInfo(1, "IV_BEND", 0, 40, 200, 40).Key, new BendLineOptions { Mode = BendLineMode.Remove });

            var moved = new BendLineInfo(1, "IV_BEND", 0, 45, 200, 45); // деталь изменили — линия сдвинулась
            Assert.Equal(BendLineMode.EdgeCuts, settings.Resolver(Part)(moved).Mode);
        }

        [Fact]
        public void SettingsWithLines_RoundTripThroughXml()
        {
            var store = new XmlSettingsStore(_dir, new FileLogger(Path.Combine(_dir, "logs")));
            var settings = new BendSettings { Default = new BendLineOptions { Mode = BendLineMode.Dashed } };
            settings.SetLineOverride(Part, "IV_BEND|0.00;40.00|200.00;40.00", new BendLineOptions { Mode = BendLineMode.RectSlots, SlotWidth = 3 });
            settings.SetLineOverride(@"C:\Детали\Ребро.ipt", "K", new BendLineOptions { Mode = BendLineMode.Remove });
            settings.SetOverride(@"C:\Детали\Ребро.ipt", new BendLineOptions { Mode = BendLineMode.EdgeCuts });

            store.Save("bends", settings);
            var loaded = store.Load<BendSettings>("bends");
            store.Save("bends", loaded);
            loaded = store.Load<BendSettings>("bends");

            Assert.False(loaded.HasOverride(Part));          // null у детали пережил сохранение
            Assert.Equal(BendLineMode.Dashed, loaded.For(Part).Mode);
            Assert.Equal(3, loaded.ForLine(Part, "IV_BEND|0.00;40.00|200.00;40.00").SlotWidth);
            Assert.Equal(1, loaded.LineOverrideCount(Part));
            Assert.Equal(BendLineMode.EdgeCuts, loaded.For(@"C:\Детали\Ребро.ipt").Mode);
            Assert.Equal(1, loaded.LineOverrideCount(@"C:\Детали\Ребро.ipt"));
        }

        /// <summary>Файлы настроек прошлой версии (Options у каждой детали, без Lines) читаются как раньше.</summary>
        [Fact]
        public void OldSettingsFile_IsCompatible()
        {
            Directory.CreateDirectory(_dir);
            File.WriteAllText(Path.Combine(_dir, "old.xml"),
                "<?xml version=\"1.0\"?><BendSettings><Default><Mode>Dashed</Mode></Default><Parts><PartBendOptions>" +
                "<PartPath>C:\\Детали\\Опора.ipt</PartPath><Options><Mode>Remove</Mode></Options></PartBendOptions></Parts></BendSettings>");

            var loaded = new XmlSettingsStore(_dir, new FileLogger(_dir)).Load<BendSettings>("old");

            Assert.Equal(BendLineMode.Remove, loaded.For(Part).Mode);
            Assert.True(loaded.HasOverride(Part));
            Assert.Equal(0, loaded.LineOverrideCount(Part));
        }

        [Fact]
        public void WriteFinal_AllLinesKept_MovesFileByteForByte()
        {
            Directory.CreateDirectory(_dir);
            string raw = Path.Combine(_dir, "r.dxf"), final = Path.Combine(_dir, "f.dxf");
            byte[] bytes = TestDxf.Cp1251(TestDxf.Inventor2004());
            File.WriteAllBytes(raw, bytes);

            var items = DxfOutput.WriteFinal(raw, final, _ => new BendLineOptions(), DxfBendProcessor.DefaultLayers);

            Assert.Equal(bytes, File.ReadAllBytes(final));
            Assert.Equal(2, items.Count);
        }
    }
}

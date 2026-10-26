using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InvAuto.Core.Bends;
using InvAuto.Core.Export;
using InvAuto.Core.Preview;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>
    /// Сквозная проверка: реалистичный DXF Inventor → обработка линий гиба → итоговый файл.
    /// Для каждого режима: файл структурно корректен, контур и отверстие не изменились,
    /// линии гиба соответствуют раскладке, кириллица сохранена байт в байт.
    /// </summary>
    public class DxfPipelineTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InvAutoPipeline_" + Guid.NewGuid().ToString("N"));

        public DxfPipelineTests() => Directory.CreateDirectory(_dir);

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        public static IEnumerable<object[]> AllModes()
        {
            yield return new object[] { "как есть", new BendLineOptions { Mode = BendLineMode.Keep } };
            yield return new object[] { "удалить", new BendLineOptions { Mode = BendLineMode.Remove } };
            yield return new object[] { "по краям", new BendLineOptions { Mode = BendLineMode.EdgeCuts, EdgeCutLength = 5 } };
            yield return new object[] { "прерывистая по шагу", new BendLineOptions { Mode = BendLineMode.Dashed, DashLength = 20, BridgeLength = 10 } };
            yield return new object[] { "прерывистая по количеству", new BendLineOptions { Mode = BendLineMode.Dashed, Spec = DashSpec.Count, DashCount = 4, DashLength = 15 } };
            yield return new object[] { "прерывистая, перемычки у краёв", new BendLineOptions { Mode = BendLineMode.Dashed, Ends = DashEnds.Bridge, Fit = DashFit.KeepCut } };
            yield return new object[] { "вырезы прерывисто", new BendLineOptions { Mode = BendLineMode.RectSlots, SlotWidth = 2 } };
            yield return new object[] { "вырез по всей длине", new BendLineOptions { Mode = BendLineMode.RectSlots, SlotPattern = SlotPattern.Full, SlotWidth = 1.5 } };
        }

        private static FlatDrawing Read(string dxf) => DxfReader.Read(new StringReader(dxf));

        private static List<Polyline2> NotBends(FlatDrawing d) => d.Polylines.Where(p => p.Kind != LayerKind.Bend).ToList();

        [Theory]
        [MemberData(nameof(AllModes))]
        public void EveryMode_GivesValidDxf_AndKeepsTheRestOfTheDrawing(string title, BendLineOptions options)
        {
            string source = TestDxf.Inventor2004();
            var result = DxfBendProcessor.Process(source, DxfBendProcessor.DefaultLayers, options);

            var entities = DxfValidator.AssertValid(result.Text);
            Assert.True(entities.Count > 0, title);

            // Контур и отверстие — точно те же точки.
            var before = NotBends(Read(source));
            var after = NotBends(Read(result.Text));
            Assert.Equal(before.Count, after.Count);
            for (int i = 0; i < before.Count; i++)
                Assert.Equal(before[i].Points.Select(p => (Math.Round(p.X, 9), Math.Round(p.Y, 9))),
                             after[i].Points.Select(p => (Math.Round(p.X, 9), Math.Round(p.Y, 9))));

            // Кириллица в тексте не тронута.
            Assert.Contains(TestDxf.CyrillicText, result.Text);

            // Обе линии гиба найдены и посчитаны.
            Assert.Equal(new[] { 200.0, 100.0 }, result.Lines.Select(l => Math.Round(l.LineLength, 6)));
        }

        [Theory]
        [MemberData(nameof(AllModes))]
        public void BendGeometry_MatchesLayout(string title, BendLineOptions options)
        {
            var result = DxfBendProcessor.Process(TestDxf.Inventor2004(), DxfBendProcessor.DefaultLayers, options);
            var bends = Read(result.Text).Polylines.Where(p => p.Kind == LayerKind.Bend).ToList();

            int expected = result.Lines.Sum(l => l.SlotHalfWidth > 0 ? l.Segments.Count : l.Segments.Count);
            Assert.True(expected == bends.Count, $"{title}: ожидалось {expected} участков, в файле {bends.Count}");

            // Горизонтальная линия на y = 40: участки на ней (или прямоугольники вокруг неё).
            var horizontal = bends.Where(b => b.Layer == "IV_BEND").ToList();
            var layout = result.Lines[0];
            for (int i = 0; i < horizontal.Count; i++)
            {
                var xs = horizontal[i].Points.Select(p => p.X).ToList();
                var ys = horizontal[i].Points.Select(p => p.Y).ToList();
                Assert.Equal(layout.Segments[i].Start, xs.Min(), 6);
                Assert.Equal(layout.Segments[i].End, xs.Max(), 6);
                Assert.Equal(40 - layout.SlotHalfWidth, ys.Min(), 6);
                Assert.Equal(40 + layout.SlotHalfWidth, ys.Max(), 6);
                if (layout.SlotHalfWidth > 0)
                    Assert.True(horizontal[i].Closed, $"{title}: вырез должен быть замкнутым контуром");
            }
        }

        [Fact]
        public void KeepMode_ReturnsSourceUnchanged()
        {
            string source = TestDxf.Inventor2004();
            var result = DxfBendProcessor.Process(source, DxfBendProcessor.DefaultLayers, new BendLineOptions());
            Assert.Same(source, result.Text);
        }

        [Fact]
        public void HandSeedAboveAllHandles_IsNeverLowered()
        {
            string source = TestDxf.Inventor2004(handSeed: "4F0");
            var result = DxfBendProcessor.Process(source, DxfBendProcessor.DefaultLayers,
                new BendLineOptions { Mode = BendLineMode.Dashed });

            DxfValidator.AssertValid(result.Text);
            Assert.DoesNotContain("\r\n$HANDSEED\r\n  5\r\n4F0\r\n", result.Text); // seed вырос
            Assert.Contains("\r\n4F0\r\n", result.Text); // новые объекты начинаются с исходного seed
        }

        [Fact]
        public void UnixNewlines_ArePreserved()
        {
            string source = TestDxf.Inventor2004("\n");
            var result = DxfBendProcessor.Process(source, DxfBendProcessor.DefaultLayers, new BendLineOptions { Mode = BendLineMode.EdgeCuts });
            Assert.DoesNotContain("\r", result.Text);
            DxfValidator.AssertValid(result.Text);
        }

        [Fact]
        public void LayerNames_AreCaseInsensitive_AndCustomLayersWork()
        {
            string source = TestDxf.Inventor2004().Replace("IV_BEND_DOWN", "Гиб_вниз");
            var result = DxfBendProcessor.Process(source, new[] { "iv_bend", "ГИБ_ВНИЗ" }, new BendLineOptions { Mode = BendLineMode.Remove });

            Assert.Equal(2, result.Lines.Count);
            Assert.DoesNotContain(Read(result.Text).Polylines, p => p.Kind == LayerKind.Bend);
        }

        [Fact]
        public void WriteFinal_WithoutProcessing_MovesFileByteForByte()
        {
            string raw = Path.Combine(_dir, "a.raw.dxf"), final = Path.Combine(_dir, "a.dxf");
            byte[] bytes = TestDxf.Cp1251(TestDxf.Inventor2004());
            File.WriteAllBytes(raw, bytes);

            var lines = DxfOutput.WriteFinal(raw, final, new BendLineOptions(), DxfBendProcessor.DefaultLayers);

            Assert.False(File.Exists(raw));
            Assert.Equal(bytes, File.ReadAllBytes(final));
            Assert.Empty(lines);
        }

        [Fact]
        public void WriteFinal_WithProcessing_KeepsCp1251BytesOfText_AndReplacesOldFile()
        {
            string raw = Path.Combine(_dir, "b.raw.dxf"), final = Path.Combine(_dir, "b.dxf");
            File.WriteAllBytes(raw, TestDxf.Cp1251(TestDxf.Inventor2004()));
            File.WriteAllText(final, "старый файл");

            var lines = DxfOutput.WriteFinal(raw, final, new BendLineOptions { Mode = BendLineMode.Dashed }, DxfBendProcessor.DefaultLayers);

            byte[] written = File.ReadAllBytes(final);
            byte[] cyrillic = TestDxf.Cp1251(TestDxf.CyrillicText);
            Assert.True(IndexOf(written, cyrillic) >= 0, "текст в cp1251 должен остаться теми же байтами");
            Assert.Equal(2, lines.Count);
            DxfValidator.AssertValid(File.ReadAllText(final, DxfBendProcessor.FileEncoding));
        }

        private static int IndexOf(byte[] haystack, byte[] needle)
        {
            for (int i = 0; i + needle.Length <= haystack.Length; i++)
                if (!needle.Where((b, k) => haystack[i + k] != b).Any())
                    return i;
            return -1;
        }
    }
}

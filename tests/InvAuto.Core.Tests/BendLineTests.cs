using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InvAuto.Core.Bends;
using InvAuto.Core.Preview;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class BendLineLayoutTests
    {
        private static BendLineOptions Dashed(double cut, double bridge, DashEnds ends = DashEnds.Cut, DashFit fit = DashFit.Proportional) =>
            new BendLineOptions { Mode = BendLineMode.Dashed, DashLength = cut, BridgeLength = bridge, Ends = ends, Fit = fit };

        private static void AssertSymmetricAndExact(BendLineLayout layout)
        {
            var s = layout.Segments;
            for (int i = 0; i < s.Count; i++)
            {
                Assert.Equal(s[i].Start, layout.LineLength - s[s.Count - 1 - i].End, 6);
                Assert.Equal(s[0].Length, s[i].Length, 6);
            }
            for (int i = 1; i < s.Count; i++)
                Assert.Equal(s[1].Start - s[0].End, s[i].Start - s[i - 1].End, 6);
        }

        [Fact]
        public void Dashed_ExactFit_KeepsRequestedValues()
        {
            var layout = BendLineStrategies.Layout(200, Dashed(20, 10));

            Assert.Equal(7, layout.Segments.Count); // 7×20 + 6×10 = 200
            Assert.Equal(0, layout.Segments[0].Start, 9);
            Assert.Equal(200, layout.Segments[6].End, 9);
            Assert.Equal(20, layout.Segments[3].Length, 9);
            AssertSymmetricAndExact(layout);
        }

        [Theory]
        [InlineData(205)]
        [InlineData(187.3)]
        [InlineData(333)]
        [InlineData(61)]
        public void Dashed_AnyLength_IsSymmetricEvenAndCoversLine(double length)
        {
            foreach (var fit in new[] { DashFit.Proportional, DashFit.KeepCut, DashFit.KeepBridge })
            foreach (var ends in new[] { DashEnds.Cut, DashEnds.Bridge })
            {
                var layout = BendLineStrategies.Layout(length, Dashed(20, 10, ends, fit));
                AssertSymmetricAndExact(layout);
                Assert.True(layout.Segments.All(seg => seg.Start >= -1e-9 && seg.End <= length + 1e-9));
                if (ends == DashEnds.Cut)
                {
                    Assert.Equal(0, layout.Segments[0].Start, 6);
                    Assert.Equal(length, layout.Segments.Last().End, 6);
                }
                else
                {
                    Assert.True(layout.Segments[0].Start > 0);
                }
            }
        }

        [Fact]
        public void Dashed_KeepCut_KeepsCutLength_AdjustsBridges()
        {
            var layout = BendLineStrategies.Layout(205, Dashed(20, 10, fit: DashFit.KeepCut));
            Assert.All(layout.Segments, s => Assert.Equal(20, s.Length, 9));
            Assert.Equal(7, layout.Segments.Count);
            Assert.Equal(65.0 / 6, layout.Segments[1].Start - layout.Segments[0].End, 9);
        }

        [Fact]
        public void Dashed_BridgeEnds()
        {
            var layout = BendLineStrategies.Layout(190, Dashed(20, 10, DashEnds.Bridge));
            Assert.Equal(6, layout.Segments.Count); // 6×20 + 7×10 = 190
            Assert.Equal(10, layout.Segments[0].Start, 9);
            Assert.Equal(180, layout.Segments.Last().End, 9);
        }

        [Fact]
        public void Dashed_TooShort_KeepsWholeLine()
        {
            var layout = BendLineStrategies.Layout(15, Dashed(20, 10));
            Assert.Single(layout.Segments);
            Assert.Equal(15, layout.Segments[0].Length, 9);
        }

        [Fact]
        public void ByCount_CutEnds_BridgesShareTheRest()
        {
            var o = new BendLineOptions { Mode = BendLineMode.Dashed, Spec = DashSpec.Count, DashCount = 5, DashLength = 20 };
            var layout = BendLineStrategies.Layout(200, o);

            Assert.Equal(5, layout.Segments.Count);
            Assert.All(layout.Segments, s => Assert.Equal(20, s.Length, 9));
            Assert.Equal(25, layout.Segments[1].Start - layout.Segments[0].End, 9); // (200 − 100) / 4
            AssertSymmetricAndExact(layout);
        }

        [Fact]
        public void ByCount_BridgeEnds_AndSingleCutIsCentered()
        {
            var o = new BendLineOptions { Mode = BendLineMode.Dashed, Spec = DashSpec.Count, DashCount = 3, DashLength = 20, Ends = DashEnds.Bridge };
            var layout = BendLineStrategies.Layout(200, o);
            Assert.Equal(35, layout.Segments[0].Start, 9); // (200 − 60) / 4
            AssertSymmetricAndExact(layout);

            o.DashCount = 1;
            o.Ends = DashEnds.Cut;
            var single = BendLineStrategies.Layout(200, o).Segments.Single();
            Assert.Equal(90, single.Start, 9);
            Assert.Equal(110, single.End, 9);
        }

        [Fact]
        public void ByCount_DoesNotFit_KeepsWholeLine()
        {
            var o = new BendLineOptions { Mode = BendLineMode.Dashed, Spec = DashSpec.Count, DashCount = 6, DashLength = 40 };
            var layout = BendLineStrategies.Layout(200, o);
            Assert.Single(layout.Segments);
            Assert.Contains("не помещаются", layout.Description);
        }

        [Fact]
        public void EdgeCuts_TwoCutsAtEnds()
        {
            var options = new BendLineOptions { Mode = BendLineMode.EdgeCuts, EdgeCutLength = 5 };
            var layout = BendLineStrategies.Layout(200, options);

            Assert.Equal(2, layout.Segments.Count);
            Assert.Equal(0, layout.Segments[0].Start, 9);
            Assert.Equal(5, layout.Segments[0].End, 9);
            Assert.Equal(195, layout.Segments[1].Start, 9);
            Assert.Equal(200, layout.Segments[1].End, 9);

            Assert.Single(BendLineStrategies.Layout(8, options).Segments);
        }

        [Fact]
        public void Remove_NoSegments()
        {
            Assert.Empty(BendLineStrategies.Layout(200, new BendLineOptions { Mode = BendLineMode.Remove }).Segments);
        }

        [Fact]
        public void EveryModeHasStrategy()
        {
            foreach (BendLineMode mode in Enum.GetValues(typeof(BendLineMode)))
                Assert.Equal(mode, BendLineStrategies.For(mode).Mode);
        }
    }

    public class DxfBendProcessorTests
    {
        private static string Line(string handle, string layer, double x1, double y1, double x2, double y2) =>
            $"  0\r\nLINE\r\n  5\r\n{handle}\r\n330\r\n1F\r\n100\r\nAcDbEntity\r\n  8\r\n{layer}\r\n100\r\nAcDbLine\r\n" +
            $" 10\r\n{x1}\r\n 20\r\n{y1}\r\n 30\r\n0.0\r\n 11\r\n{x2}\r\n 21\r\n{y2}\r\n 31\r\n0.0\r\n";

        private static string Dxf(params string[] entities) =>
            "  0\r\nSECTION\r\n  2\r\nHEADER\r\n  9\r\n$HANDSEED\r\n  5\r\n30\r\n  0\r\nENDSEC\r\n" +
            "  0\r\nSECTION\r\n  2\r\nENTITIES\r\n" + string.Concat(entities) + "  0\r\nENDSEC\r\n  0\r\nEOF\r\n";

        private static readonly string Sample = Dxf(
            Line("2A", "IV_OUTER_PROFILE", 0, 0, 200, 0),
            Line("2B", "IV_BEND", 0, 50, 200, 50),
            Line("2C", "IV_BEND_DOWN", 100, 0, 100, 100));

        private static FlatDrawing Read(string dxf) => DxfReader.Read(new StringReader(dxf));

        [Fact]
        public void Keep_ReturnsSameText_AndReportsLines()
        {
            var result = DxfBendProcessor.Process(Sample, DxfBendProcessor.DefaultLayers, new BendLineOptions());
            Assert.Same(Sample, result.Text);
            Assert.Equal(new[] { 200.0, 100.0 }, result.Lines.Select(l => l.LineLength));
        }

        [Fact]
        public void Remove_DeletesOnlyBendLines()
        {
            var result = DxfBendProcessor.Process(Sample, DxfBendProcessor.DefaultLayers, new BendLineOptions { Mode = BendLineMode.Remove });
            var drawing = Read(result.Text);

            Assert.Single(drawing.Polylines);
            Assert.Equal("IV_OUTER_PROFILE", drawing.Polylines[0].Layer);
            Assert.Contains("\r\n2A\r\n", result.Text);
        }

        [Fact]
        public void Dashed_SplitsLines_WithUniqueHandles_AndUpdatesSeed()
        {
            var options = new BendLineOptions { Mode = BendLineMode.Dashed, DashLength = 20, BridgeLength = 10 };
            var result = DxfBendProcessor.Process(Sample, DxfBendProcessor.DefaultLayers, options);
            var drawing = Read(result.Text);

            var bends = drawing.Polylines.Where(p => p.Layer.StartsWith("IV_BEND")).ToList();
            Assert.Equal(7 + 4, bends.Count); // 200 мм → 7 прорезей, 100 мм → 4 (4×20 + 3×6,67)
            Assert.All(bends.Where(b => b.Layer == "IV_BEND"), b => Assert.Equal(20, b.Points[1].X - b.Points[0].X, 6));

            var lines = result.Text.Split(new[] { "\r\n" }, StringSplitOptions.None);
            var handles = new List<string>();
            for (int i = 0; i + 1 < lines.Length; i += 2)
                if (lines[i].Trim() == "5")
                    handles.Add(lines[i + 1]);

            // $HANDSEED + 1 контур + 11 отрезков; все handle уникальны
            Assert.Equal(1 + 1 + 11, handles.Count);
            Assert.Equal(handles.Count, handles.Distinct().Count());
            ulong max = handles.Skip(1).Max(h => Convert.ToUInt64(h, 16));
            Assert.True(Convert.ToUInt64(handles[0], 16) > max);
        }

        [Fact]
        public void OtherLayers_AreNotTouched()
        {
            var result = DxfBendProcessor.Process(Sample, new[] { "SOMETHING" }, new BendLineOptions { Mode = BendLineMode.Remove });
            Assert.Same(Sample, result.Text);
            Assert.Empty(result.Lines);
        }
    }

    public class RectSlotTests
    {
        private static string Dxf(string bend) =>
            "  0\r\nSECTION\r\n  2\r\nHEADER\r\n  9\r\n$HANDSEED\r\n  5\r\n30\r\n  0\r\nENDSEC\r\n" +
            "  0\r\nSECTION\r\n  2\r\nENTITIES\r\n" + bend + "  0\r\nENDSEC\r\n  0\r\nEOF\r\n";

        private const string ModernLine =
            "  0\r\nLINE\r\n  5\r\n2B\r\n330\r\n1F\r\n100\r\nAcDbEntity\r\n  8\r\nIV_BEND\r\n 62\r\n2\r\n100\r\nAcDbLine\r\n" +
            " 10\r\n0.0\r\n 20\r\n50.0\r\n 30\r\n0.0\r\n 11\r\n200.0\r\n 21\r\n50.0\r\n 31\r\n0.0\r\n";

        private const string R12Line =
            "  0\r\nLINE\r\n  8\r\nIV_BEND\r\n 10\r\n0.0\r\n 20\r\n50.0\r\n 30\r\n0.0\r\n 11\r\n200.0\r\n 21\r\n50.0\r\n 31\r\n0.0\r\n";

        private static BendLineOptions Slots(SlotPattern pattern) => new BendLineOptions
        {
            Mode = BendLineMode.RectSlots, SlotWidth = 2, SlotPattern = pattern, DashLength = 20, BridgeLength = 10
        };

        [Fact]
        public void Dashed_Slots_AreClosedRectangles_AroundBendLine()
        {
            var result = DxfBendProcessor.Process(Dxf(ModernLine), DxfBendProcessor.DefaultLayers, Slots(SlotPattern.Dashed));
            var slots = DxfReader.Read(new StringReader(result.Text)).Polylines;

            Assert.Equal(7, slots.Count);
            Assert.All(slots, p =>
            {
                Assert.True(p.Closed);
                Assert.Equal("IV_BEND", p.Layer);
                Assert.Equal(4, p.Points.Count);
                Assert.Equal(49, p.Points.Min(q => q.Y), 6);
                Assert.Equal(51, p.Points.Max(q => q.Y), 6);
                Assert.Equal(20, p.Points.Max(q => q.X) - p.Points.Min(q => q.X), 6);
            });
            Assert.Contains("\r\n 62\r\n2\r\n", result.Text); // цвет исходной линии сохранён
            Assert.DoesNotContain("AcDbLine", result.Text);
        }

        [Fact]
        public void Full_Slot_IsOneRectangleAlongWholeLine()
        {
            var result = DxfBendProcessor.Process(Dxf(ModernLine), DxfBendProcessor.DefaultLayers, Slots(SlotPattern.Full));
            var slot = DxfReader.Read(new StringReader(result.Text)).Polylines.Single();

            Assert.Equal(0, slot.Points.Min(q => q.X), 6);
            Assert.Equal(200, slot.Points.Max(q => q.X), 6);
            Assert.Equal(2, slot.Points.Max(q => q.Y) - slot.Points.Min(q => q.Y), 6);
        }

        [Fact]
        public void R12_Slot_IsFourLines()
        {
            var result = DxfBendProcessor.Process(Dxf(R12Line), DxfBendProcessor.DefaultLayers, Slots(SlotPattern.Full));
            var lines = DxfReader.Read(new StringReader(result.Text)).Polylines;

            Assert.Equal(4, lines.Count);
            Assert.DoesNotContain("LWPOLYLINE", result.Text);
        }

        [Fact]
        public void Layout_ReportsSlotWidth()
        {
            var layout = BendLineStrategies.Layout(200, Slots(SlotPattern.Dashed));
            Assert.Equal(1, layout.SlotHalfWidth);
            Assert.Contains("ширина 2 мм", layout.Description);
        }
    }

    public class BendSettingsTests
    {
        [Fact]
        public void Override_FallsBackToDefault()
        {
            var settings = new BendSettings { Default = new BendLineOptions { Mode = BendLineMode.Dashed } };
            Assert.Equal(BendLineMode.Dashed, settings.For(@"C:\A.ipt").Mode);

            settings.SetOverride(@"C:\A.ipt", new BendLineOptions { Mode = BendLineMode.Remove });
            Assert.Equal(BendLineMode.Remove, settings.For(@"c:\a.IPT").Mode);
            Assert.Equal(BendLineMode.Dashed, settings.For(@"C:\B.ipt").Mode);

            settings.SetOverride(@"C:\A.ipt", new BendLineOptions { Mode = BendLineMode.EdgeCuts });
            Assert.Single(settings.Parts);

            settings.ClearOverride(@"C:\A.IPT");
            Assert.False(settings.HasOverride(@"C:\A.ipt"));
        }
    }
}

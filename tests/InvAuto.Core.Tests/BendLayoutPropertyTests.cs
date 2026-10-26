using System;
using System.Collections.Generic;
using System.Linq;
using InvAuto.Core.Bends;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>
    /// Свойства раскладки линий гиба, которые должны выполняться при любых длинах и параметрах.
    /// Тысячи случайных сочетаний с фиксированным зерном — повторяемо и ловит крайние случаи.
    /// </summary>
    public class BendLayoutPropertyTests
    {
        private static IEnumerable<(double Length, BendLineOptions Options)> Cases(int count)
        {
            var random = new Random(20261026);
            var modes = (BendLineMode[])Enum.GetValues(typeof(BendLineMode));
            for (int i = 0; i < count; i++)
            {
                double length = Math.Round(1 + random.NextDouble() * 1500, 2);
                yield return (length, new BendLineOptions
                {
                    Mode = modes[random.Next(modes.Length)],
                    EdgeCutLength = Math.Round(0.5 + random.NextDouble() * 30, 1),
                    DashLength = Math.Round(1 + random.NextDouble() * 60, 1),
                    BridgeLength = Math.Round(0.5 + random.NextDouble() * 30, 1),
                    DashCount = random.Next(1, 25),
                    Spec = random.Next(2) == 0 ? DashSpec.Step : DashSpec.Count,
                    Ends = random.Next(2) == 0 ? DashEnds.Cut : DashEnds.Bridge,
                    Fit = (DashFit)random.Next(3),
                    SlotWidth = Math.Round(0.2 + random.NextDouble() * 5, 1),
                    SlotPattern = random.Next(2) == 0 ? SlotPattern.Full : SlotPattern.Dashed
                });
            }
        }

        [Fact]
        public void Segments_AreInsideLine_Sorted_NonOverlapping_AndFinite()
        {
            foreach (var (length, o) in Cases(5000))
            {
                var layout = BendLineStrategies.Layout(length, o);
                string where = $"{o.Summary}, длина {length}";

                Assert.Equal(length, layout.LineLength);
                Assert.False(string.IsNullOrWhiteSpace(layout.Description), where);
                double previousEnd = double.NegativeInfinity;
                foreach (var s in layout.Segments)
                {
                    Assert.False(double.IsNaN(s.Start) || double.IsNaN(s.End) || double.IsInfinity(s.Start) || double.IsInfinity(s.End), where);
                    Assert.True(s.Start >= -1e-9 && s.End <= length + 1e-9, $"{where}: участок {s.Start}…{s.End} вне линии");
                    Assert.True(s.Length > 0, $"{where}: участок нулевой длины");
                    Assert.True(s.Start >= previousEnd - 1e-9, $"{where}: участки перекрываются");
                    previousEnd = s.End;
                }
            }
        }

        [Fact]
        public void Pattern_IsSymmetric_AndEven()
        {
            foreach (var (length, o) in Cases(5000))
            {
                var segments = BendLineStrategies.Layout(length, o).Segments;
                string where = $"{o.Summary}, длина {length}";

                for (int i = 0; i < segments.Count; i++)
                {
                    var mirror = segments[segments.Count - 1 - i];
                    Assert.True(Math.Abs(segments[i].Start - (length - mirror.End)) < 1e-6, $"{where}: несимметрично");
                }

                bool even = o.Mode == BendLineMode.Dashed || (o.Mode == BendLineMode.RectSlots && o.SlotPattern == SlotPattern.Dashed);
                if (!even || segments.Count < 2)
                    continue;
                for (int i = 1; i < segments.Count; i++)
                {
                    Assert.True(Math.Abs(segments[i].Length - segments[0].Length) < 1e-6, $"{where}: прорези разной длины");
                    double gap = segments[i].Start - segments[i - 1].End;
                    double firstGap = segments[1].Start - segments[0].End;
                    Assert.True(Math.Abs(gap - firstGap) < 1e-6, $"{where}: перемычки разной длины");
                }
            }
        }

        [Fact]
        public void ModeSpecificRules_Hold()
        {
            foreach (var (length, o) in Cases(5000))
            {
                var layout = BendLineStrategies.Layout(length, o);
                var s = layout.Segments;
                string where = $"{o.Summary}, длина {length}";

                switch (o.Mode)
                {
                    case BendLineMode.Keep:
                        Assert.Single(s);
                        Assert.Equal(0, s[0].Start);
                        Assert.Equal(length, s[0].End);
                        break;

                    case BendLineMode.Remove:
                        Assert.Empty(s);
                        break;

                    case BendLineMode.EdgeCuts:
                        if (2 * o.EdgeCutLength >= length)
                            Assert.Single(s);
                        else
                        {
                            Assert.Equal(2, s.Count);
                            Assert.Equal(o.EdgeCutLength, s[0].Length, 9);
                            Assert.Equal(0, s[0].Start);
                            Assert.Equal(length, s[1].End, 9);
                        }
                        break;

                    case BendLineMode.Dashed when o.Spec == DashSpec.Count:
                        // Либо ровно заданное количество прорезей заданной длины, либо линия целиком (не помещаются).
                        if (s.Count == 1 && Math.Abs(s[0].Length - length) < 1e-9 && o.DashCount != 1)
                            Assert.True(o.DashCount * o.DashLength >= length - 0.03 * o.DashCount, where);
                        else
                        {
                            Assert.Equal(o.DashCount, s.Count);
                            Assert.All(s, seg => Assert.Equal(o.DashLength, seg.Length, 6));
                        }
                        break;

                    case BendLineMode.Dashed when o.Fit == DashFit.KeepCut && s.Count > 1:
                        Assert.All(s, seg => Assert.Equal(o.DashLength, seg.Length, 6));
                        break;

                    case BendLineMode.Dashed when o.Spec == DashSpec.Step && s.Count > 1:
                        // Подогнанные размеры не меньше половины заданных.
                        Assert.True(s[0].Length >= o.DashLength * 0.5 - 1e-9, where);
                        Assert.True(s[1].Start - s[0].End >= o.BridgeLength * 0.5 - 1e-9, where);
                        break;

                    case BendLineMode.RectSlots:
                        Assert.Equal(o.SlotWidth / 2, layout.SlotHalfWidth, 9);
                        if (o.SlotPattern == SlotPattern.Full)
                            Assert.Single(s);
                        break;
                }
            }
        }

        [Fact]
        public void DashEnds_AreRespected_WhenPatternFits()
        {
            foreach (var (length, o) in Cases(3000).Where(c => c.Options.Mode == BendLineMode.Dashed))
            {
                var s = BendLineStrategies.Layout(length, o).Segments;
                if (s.Count < 2)
                    continue;
                if (o.Ends == DashEnds.Cut)
                {
                    Assert.Equal(0, s[0].Start, 9);
                    Assert.Equal(length, s[s.Count - 1].End, 6);
                }
                else
                {
                    Assert.True(s[0].Start > 0);
                    Assert.True(s[s.Count - 1].End < length);
                }
            }
        }
    }
}

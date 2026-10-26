using System;
using System.IO;
using System.Linq;
using InvAuto.Core.Bends;
using InvAuto.Core.Preflight;
using InvAuto.Core.Preview;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>Законцовки линий гиба: окружность, квадрат 0° и 45° на обоих концах линии.</summary>
    public class BendEndsTests
    {
        private static string Dxf(string bend) =>
            "  0\r\nSECTION\r\n  2\r\nHEADER\r\n  9\r\n$HANDSEED\r\n  5\r\n30\r\n  0\r\nENDSEC\r\n" +
            "  0\r\nSECTION\r\n  2\r\nENTITIES\r\n" + bend + "  0\r\nENDSEC\r\n  0\r\nEOF\r\n";

        private const string ModernLine =
            "  0\r\nLINE\r\n  5\r\n2B\r\n330\r\n1F\r\n100\r\nAcDbEntity\r\n  8\r\nIV_BEND\r\n  6\r\nDASHED\r\n 62\r\n2\r\n100\r\nAcDbLine\r\n" +
            " 10\r\n0.0\r\n 20\r\n50.0\r\n 30\r\n0.0\r\n 11\r\n200.0\r\n 21\r\n50.0\r\n 31\r\n0.0\r\n";

        private const string R12Line =
            "  0\r\nLINE\r\n  8\r\nIV_BEND\r\n 10\r\n0.0\r\n 20\r\n50.0\r\n 30\r\n0.0\r\n 11\r\n200.0\r\n 21\r\n50.0\r\n 31\r\n0.0\r\n";

        private static BendLineOptions Ends(BendEndShape shape, double size = 4, double offset = 0, BendLineMode mode = BendLineMode.Keep) =>
            new BendLineOptions { Mode = mode, EndShape = shape, EndSize = size, EndOffset = offset };

        private static FlatDrawing Read(string dxf) => DxfReader.Read(new StringReader(dxf));

        [Fact]
        public void Outlines_TwoPerLine_CenteredOnEnds_OrShiftedInside()
        {
            var outlines = BendEnds.Outlines(0, 0, 100, 0, Ends(BendEndShape.Circle, 6));
            Assert.Equal(2, outlines.Count);
            Assert.All(outlines, o => Assert.Equal(3, o.Radius, 9));
            Assert.Equal(new[] { 0.0, 100.0 }, outlines.Select(o => o.CenterX));

            var shifted = BendEnds.Outlines(0, 0, 100, 0, Ends(BendEndShape.Circle, 6, offset: 2));
            Assert.Equal(new[] { 2.0, 98.0 }, shifted.Select(o => o.CenterX));

            var tooFar = BendEnds.Outlines(0, 0, 10, 0, Ends(BendEndShape.Circle, 2, offset: 50)); // не дальше середины
            Assert.All(tooFar, o => Assert.Equal(5, o.CenterX, 9));

            Assert.Empty(BendEnds.Outlines(0, 0, 100, 0, Ends(BendEndShape.None)));
            Assert.Empty(BendEnds.Outlines(0, 0, 100, 0, Ends(BendEndShape.Square, size: 0)));
            Assert.Empty(BendEnds.Outlines(5, 5, 5, 5, Ends(BendEndShape.Square)));
        }

        [Fact]
        public void Square_SidesAlongLine_Diamond_CornersOnLine()
        {
            // Линия под 30°: квадрат — стороны вдоль и поперёк линии, ромб — вершины на линии и на перпендикуляре.
            double a = Math.PI / 6, ux = Math.Cos(a), uy = Math.Sin(a);
            var square = BendEnds.For(BendEndShape.Square).Outline(0, 0, ux, uy, 4).Vertices;
            var diamond = BendEnds.For(BendEndShape.Diamond).Outline(0, 0, ux, uy, 4).Vertices;

            Assert.Equal(4, square.Count);
            for (int i = 0; i < 4; i++)
            {
                var p = square[i];
                var q = square[(i + 1) % 4];
                Assert.Equal(4, Math.Sqrt((q.X - p.X) * (q.X - p.X) + (q.Y - p.Y) * (q.Y - p.Y)), 9); // сторона 4
                double along = Math.Abs((q.X - p.X) * ux + (q.Y - p.Y) * uy);
                Assert.True(along < 1e-9 || Math.Abs(along - 4) < 1e-9);                             // вдоль или поперёк линии
            }

            Assert.Equal(4, Math.Sqrt(Math.Pow(diamond[1].X - diamond[0].X, 2) + Math.Pow(diamond[1].Y - diamond[0].Y, 2)), 9);
            Assert.Equal(4 * Math.Sqrt(2) / 2, diamond[0].X * ux + diamond[0].Y * uy, 9);   // вершина на линии
            Assert.Equal(0, -diamond[0].X * uy + diamond[0].Y * ux, 9);
        }

        [Fact]
        public void Dxf_CircleEnds_AreCirclesOnInteriorLayer_WithoutBendLineStyle()
        {
            var result = DxfBendProcessor.Process(Dxf(ModernLine), DxfBendProcessor.DefaultLayers, Ends(BendEndShape.Circle, 4));
            DxfValidator.AssertValid(result.Text);

            var drawing = Read(result.Text);
            var bends = drawing.Polylines.Where(p => p.Kind == LayerKind.Bend).ToList();
            var circles = drawing.Polylines.Where(p => p.Layer == "IV_INTERIOR_PROFILES").ToList();
            Assert.Single(bends);                              // линия гиба осталась (Keep)
            Assert.Equal(2, circles.Count);
            Assert.All(circles, c => Assert.True(c.Closed));
            Assert.Equal(-2, circles.Min(c => c.Points.Min(p => p.X)), 6);
            Assert.Equal(202, circles.Max(c => c.Points.Max(p => p.X)), 6);
            Assert.Equal(2, drawing.Geometry.Arcs.Count(arc => arc.IsCircle && Math.Abs(arc.Radius - 2) < 1e-9));

            // Тип и цвет линии гиба (DASHED, 62) у законцовок не копируются; подкласс — AcDbCircle.
            var circleEntity = result.Text.Substring(result.Text.IndexOf("CIRCLE", StringComparison.Ordinal));
            Assert.Contains("AcDbCircle", circleEntity);
            Assert.Equal(1, Count(result.Text, "DASHED"));
            Assert.Contains("\r\n 40\r\n2.0\r\n", result.Text);
        }

        [Theory]
        [InlineData(BendEndShape.Square)]
        [InlineData(BendEndShape.Diamond)]
        public void Dxf_SquareEnds_AreClosedPolylines(BendEndShape shape)
        {
            var result = DxfBendProcessor.Process(Dxf(ModernLine), DxfBendProcessor.DefaultLayers, Ends(shape, 4, mode: BendLineMode.Remove));
            DxfValidator.AssertValid(result.Text);
            var ends = Read(result.Text).Polylines;

            Assert.Equal(2, ends.Count);                        // линия удалена, остались только законцовки
            Assert.All(ends, e =>
            {
                Assert.True(e.Closed);
                Assert.Equal("IV_INTERIOR_PROFILES", e.Layer);
                Assert.Equal(4, e.Points.Count);
            });
            double half = shape == BendEndShape.Square ? 2 : 2 * Math.Sqrt(2);
            Assert.Equal(50 + half, ends[0].Points.Max(p => p.Y), 6);
        }

        [Fact]
        public void Dxf_R12_SquaresAreLines_CirclesAreCircles()
        {
            var squares = DxfBendProcessor.Process(Dxf(R12Line), DxfBendProcessor.DefaultLayers, Ends(BendEndShape.Square, mode: BendLineMode.Remove));
            Assert.DoesNotContain("LWPOLYLINE", squares.Text);
            Assert.Equal(8, Read(squares.Text).Polylines.Count);  // 2 квадрата × 4 отрезка

            var circles = DxfBendProcessor.Process(Dxf(R12Line), DxfBendProcessor.DefaultLayers, Ends(BendEndShape.Circle, mode: BendLineMode.Remove));
            Assert.Equal(2, Count(circles.Text, "CIRCLE"));
            DxfValidator.AssertValid(circles.Text);
        }

        [Fact]
        public void Ends_WorkWithAnyMode_AndCustomLayer()
        {
            foreach (BendLineMode mode in Enum.GetValues(typeof(BendLineMode)))
            {
                // На краю детали — выемки в контуре: отдельных вырезов нет, контур один и замкнут.
                var options = Ends(BendEndShape.Diamond, 3, mode: mode);
                options.EndLayer = "RELIEF";
                var merged = DxfBendProcessor.Process(TestDxf.Inventor2004(), DxfBendProcessor.DefaultLayers, options);
                DxfValidator.AssertValid(merged.Text);
                var drawing = Read(merged.Text);
                Assert.Equal(0, drawing.Polylines.Count(p => p.Layer == "RELIEF"));
                Assert.Equal(2, FlatContours.Build(drawing).Count(c => c.Closed));    // контур и отверстие детали
                Assert.DoesNotContain(FlatContours.Build(drawing), c => !c.Closed);
                Assert.Equal(4, merged.EndFeatures.Count);

                // Сдвинуты внутрь дальше своего размера — отдельные вырезы на заданном слое.
                options.EndOffset = 5;
                var separate = DxfBendProcessor.Process(TestDxf.Inventor2004(), DxfBendProcessor.DefaultLayers, options);
                DxfValidator.AssertValid(separate.Text);
                Assert.Equal(4, Read(separate.Text).Polylines.Count(p => p.Layer == "RELIEF")); // 2 линии × 2 конца
                Assert.All(separate.EndFeatures, f => Assert.True(f.Closed));
            }
        }

        private static string LinePlate(bool modern, string bends)
        {
            // Пластина 200×100 из четырёх LINE на слое наружного контура.
            var sb = new System.Text.StringBuilder();
            int handle = 0x40;
            void Edge(double x1, double y1, double x2, double y2)
            {
                sb.Append("  0\r\nLINE\r\n");
                if (modern)
                    sb.Append(FormattableString.Invariant($"  5\r\n{handle++:X}\r\n100\r\nAcDbEntity\r\n"));
                sb.Append("  8\r\nIV_OUTER_PROFILE\r\n");
                if (modern)
                    sb.Append("100\r\nAcDbLine\r\n");
                sb.Append(FormattableString.Invariant($" 10\r\n{x1}\r\n 20\r\n{y1}\r\n 30\r\n0.0\r\n 11\r\n{x2}\r\n 21\r\n{y2}\r\n 31\r\n0.0\r\n"));
            }
            Edge(0, 0, 200, 0);
            Edge(200, 0, 200, 100);
            Edge(200, 100, 0, 100);
            Edge(0, 100, 0, 0);
            return Dxf(sb + bends);
        }

        [Theory]
        [InlineData(BendEndShape.Circle, true)]
        [InlineData(BendEndShape.Circle, false)]
        [InlineData(BendEndShape.Square, true)]
        [InlineData(BendEndShape.Diamond, false)]
        public void LineContour_EndsBecomeNotches_ContourStaysClosed(BendEndShape shape, bool modern)
        {
            var result = DxfBendProcessor.Process(LinePlate(modern, modern ? ModernLine : R12Line), DxfBendProcessor.DefaultLayers, Ends(shape, 4));
            DxfValidator.AssertValid(result.Text);
            var drawing = Read(result.Text);

            Assert.DoesNotContain(drawing.Polylines, p => p.Layer == "IV_INTERIOR_PROFILES"); // отдельных вырезов нет
            var contours = FlatContours.Build(drawing);
            Assert.Single(contours);
            Assert.True(contours[0].Closed);

            // Площадь: из детали вычитаются половинки законцовок (центр на краю).
            double removed = shape == BendEndShape.Circle ? Math.PI * 4 : 16; // два квадрата 4×4 пополам
            var metrics = FlatMetrics.Compute(drawing);
            Assert.Equal(20000 - removed, metrics.Area, 0.5);
            Assert.Equal(1, metrics.Pierces);

            // Линия гиба укорочена: внутри выемки пустота.
            var bend = drawing.Polylines.Single(p => p.Kind == LayerKind.Bend);
            double reach = shape == BendEndShape.Diamond ? 4 / Math.Sqrt(2) : 2;
            Assert.Equal(reach, bend.Points.Min(p => p.X), 6);
            Assert.Equal(200 - reach, bend.Points.Max(p => p.X), 6);

            // Участки выемки — красные (цвет 1), дуга окружности — ARC; для окна — две разомкнутые линии.
            Assert.Contains("\r\n 62\r\n1\r\n", result.Text);
            Assert.Equal(shape == BendEndShape.Circle, result.Text.Contains("\r\nARC\r\n"));
            Assert.Equal(2, result.EndFeatures.Count);
            Assert.All(result.EndFeatures, f => Assert.False(f.Closed));
            Assert.All(result.EndFeatures, f => Assert.Equal(LayerKind.EndFeature, f.Kind));
        }

        [Fact]
        public void PolylineContour_GetsNotchVertices_StaysOneEntity()
        {
            var result = DxfBendProcessor.Process(TestDxf.Inventor2004(), DxfBendProcessor.DefaultLayers, Ends(BendEndShape.Circle, 4));
            DxfValidator.AssertValid(result.Text);
            Assert.Equal(1, Count(result.Text, "IV_OUTER_PROFILE\r\n100\r\nAcDbPolyline"));
            Assert.Single(Read(result.Text).Polylines, p => p.Layer == "IV_OUTER_PROFILE");
            Assert.Contains("\r\n 90\r\n16\r\n", result.Text);                         // 8 вершин + по 2 на выемку
            var drawing = Read(result.Text);
            var contours = FlatContours.Build(drawing);
            Assert.Equal(2, contours.Count(c => c.Closed));
            var outer = contours.Single(c => c.IsOuter);
            Assert.Equal(20000 - 100 + 25 * Math.PI - 2 * Math.PI * 4, outer.Area, 0.5);   // скругления R5 и 4 половинки Ø4
        }

        [Fact]
        public void OverlappingNotches_SecondBecomesSeparateHole()
        {
            string second = ModernLine.Replace("2B", "2C").Replace(" 20\r\n50.0", " 20\r\n51.0").Replace(" 21\r\n50.0", " 21\r\n51.0");
            var result = DxfBendProcessor.Process(LinePlate(true, ModernLine + second), DxfBendProcessor.DefaultLayers, Ends(BendEndShape.Circle, 4));
            DxfValidator.AssertValid(result.Text);
            var drawing = Read(result.Text);
            Assert.Equal(2, drawing.Polylines.Count(p => p.Layer == "IV_INTERIOR_PROFILES"));
            Assert.Single(FlatContours.Build(drawing), c => c.IsOuter);
        }

        [Fact]
        public void Summary_And_Layout_MentionEnds()
        {
            var o = Ends(BendEndShape.Diamond, 3, offset: 1, mode: BendLineMode.Dashed);
            Assert.Contains("квадрат 3 мм под 45°", o.FullSummary);
            Assert.Contains("внутрь 1 мм", o.FullSummary);
            Assert.DoesNotContain("квадрат", o.Summary);
            var layout = BendLineStrategies.Layout(200, o);
            Assert.Equal(BendEndShape.Diamond, layout.EndShape);
            Assert.Contains("законцовки", layout.Description);
            Assert.False(o.SameAs(Ends(BendEndShape.Circle, 3, offset: 1, mode: BendLineMode.Dashed)));
        }

        [Fact]
        public void Metrics_CountOnlyInsidePartOfEdgeEnds()
        {
            // Пластина 200×100, линия гиба по y = 50 от края до края, окружности Ø10 на концах — наполовину снаружи.
            string plate = Dxf(
                "  0\r\nLWPOLYLINE\r\n  8\r\nIV_OUTER_PROFILE\r\n 90\r\n4\r\n 70\r\n1\r\n 10\r\n0\r\n 20\r\n0\r\n 10\r\n200\r\n 20\r\n0\r\n 10\r\n200\r\n 20\r\n100\r\n 10\r\n0\r\n 20\r\n100\r\n" +
                R12Line);
            var result = DxfBendProcessor.Process(plate, DxfBendProcessor.DefaultLayers, Ends(BendEndShape.Circle, 10));
            var metrics = FlatMetrics.Compute(Read(result.Text));

            Assert.Equal(1, metrics.Pierces);                       // законцовки — выемки в контуре, не отверстия
            Assert.Equal(20000 - Math.PI * 25, metrics.Area, 1.5); // две половинки круга = один круг

            // Проверка не считает законцовки «отверстиями у линии гиба».
            var part = new PreflightPart
            {
                Key = "P", PartNumber = "P", Thickness = 3, HasFlatPattern = true,
                Drawing = Read(result.Text),
                BendLines = new[] { new BendLineInfo(1, "IV_BEND", 0, 50, 200, 50) }
            };
            Assert.DoesNotContain(PreflightChecker.Check(part, new PreflightSettings()), i => i.Code == "HoleNearBend");
        }

        /// <summary>Пробные развертки в SVG для просмотра глазами: INVAUTO_SAMPLE_DIR=папка dotnet test.</summary>
        [Fact]
        public void WriteSamples_WhenRequested()
        {
            string dir = Environment.GetEnvironmentVariable("INVAUTO_SAMPLE_DIR");
            if (string.IsNullOrEmpty(dir))
                return;
            Directory.CreateDirectory(dir);
            var svg = new System.Text.StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='-20 -20 1000 330' width='1000' height='330' style='background:#fff'>");
            int x = 0;
            foreach (var (shape, mode) in new[] { (BendEndShape.Circle, BendLineMode.Dashed), (BendEndShape.Square, BendLineMode.RoundSlots), (BendEndShape.Diamond, BendLineMode.Keep) })
            {
                var options = Ends(shape, 8, mode: mode);
                options.SlotWidth = 3;
                var processed = DxfBendProcessor.Process(TestDxf.Inventor2004(), DxfBendProcessor.DefaultLayers, options);
                var drawing = Read(processed.Text).WithEndFeatures(processed.EndFeatures);
                svg.Append(FormattableString.Invariant($"<g transform='translate({x},{drawing.MaxY + 10}) scale(1,-1)'>"));
                foreach (var p in drawing.Polylines)
                {
                    string color = p.Kind == LayerKind.Bend ? "#0696D7" : p.Kind == LayerKind.EndFeature ? "#D32F2F" : "#222";
                    svg.Append("<polyline fill='none' stroke='").Append(color).Append(p.Kind == LayerKind.EndFeature ? "' stroke-width='1' points='" : "' stroke-width='0.6' points='")
                       .Append(string.Join(" ", p.Points.Concat(p.Closed ? new[] { p.Points[0] } : new Point2[0]).Select(q => FormattableString.Invariant($"{q.X - drawing.MinX:0.##},{q.Y - drawing.MinY:0.##}"))))
                       .Append("'/>");
                }
                svg.Append("</g>");
                x += 330;
            }
            File.WriteAllText(Path.Combine(dir, "bend-ends.svg"), svg.Append("</svg>").ToString());
        }

        private static int Count(string text, string value)
        {
            int count = 0;
            for (int i = text.IndexOf(value, StringComparison.Ordinal); i >= 0; i = text.IndexOf(value, i + value.Length, StringComparison.Ordinal))
                count++;
            return count;
        }
    }
}

using System;
using System.IO;
using System.Linq;
using InvAuto.Core.Bends;
using InvAuto.Core.Flat;
using InvAuto.Core.Marking;
using InvAuto.Core.Preview;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>Ручной поворот развертки и маркировка под любым углом.</summary>
    public class FlatRotationTests
    {
        private static FlatDrawing Read(string dxf) => DxfReader.Read(new StringReader(dxf));

        private static string Entities(string body) =>
            "  0\r\nSECTION\r\n  2\r\nHEADER\r\n  9\r\n$HANDSEED\r\n  5\r\n30\r\n  0\r\nENDSEC\r\n" +
            "  0\r\nSECTION\r\n  2\r\nENTITIES\r\n" + body + "  0\r\nENDSEC\r\n  0\r\nEOF\r\n";

        [Fact]
        public void ZeroRotation_SameText()
        {
            string dxf = TestDxf.Inventor2004();
            Assert.Same(dxf, DxfBendProcessor.Rotate(dxf, 0));
            Assert.Same(dxf, DxfBendProcessor.Rotate(dxf, 360));
        }

        [Theory]
        [InlineData(90)]
        [InlineData(30)]
        [InlineData(-135)]
        public void Rotate_MatchesRotatingThePoints(double degrees)
        {
            string dxf = TestDxf.Inventor2004();
            string rotated = DxfBendProcessor.Rotate(dxf, degrees);
            DxfValidator.AssertValid(rotated);

            var before = Read(dxf);
            var after = Read(rotated);
            Assert.Equal(before.Polylines.Count, after.Polylines.Count);
            for (int i = 0; i < before.Polylines.Count; i++)
            {
                // Каждая точка повёрнутого чертежа лежит на повёрнутой исходной линии (окружности разбиваются
                // заново от угла 0°, поэтому сравнивается форма, а не порядок точек).
                var expected = before.Polylines[i].Points.Select(p => FlatPipeline.Turn(p.X, p.Y, degrees)).Select(t => new Point2(t.X, t.Y)).ToList();
                if (before.Polylines[i].Closed)
                    expected.Add(expected[0]);
                foreach (var q in after.Polylines[i].Points)
                {
                    double d = Enumerable.Range(1, expected.Count - 1)
                        .Min(k => Geometry2.Distance(q, new Segment2(expected[k - 1], expected[k], null).Nearest(q)));
                    Assert.True(d < 0.05, $"точка {q} не на повёрнутой линии ({d:0.###} мм)");
                }
            }
            // Площадь и замкнутость не меняются; кириллический текст файла — тот же.
            Assert.Equal(FlatMetrics.Compute(before).Area, FlatMetrics.Compute(after).Area, 6);
            Assert.Contains(TestDxf.CyrillicText, rotated);
        }

        [Fact]
        public void Arc_AnglesTurn_AndOcsMinusZ_TurnsBackwards()
        {
            string arc = "  0\r\nARC\r\n  8\r\nIV_OUTER_PROFILE\r\n 10\r\n10.0\r\n 20\r\n0.0\r\n 30\r\n0.0\r\n 40\r\n5.0\r\n 50\r\n0.0\r\n 51\r\n90.0\r\n";
            var turned = Read(DxfBendProcessor.Rotate(Entities(arc), 90)).Geometry.Arcs.Single();
            Assert.Equal(0, turned.Center.X, 9);
            Assert.Equal(10, turned.Center.Y, 9);
            Assert.Equal(Math.PI / 2, turned.StartAngle, 9);

            // Выдавливание −Z: в системе объекта X направлен против X мира — поворот в ней обратный.
            string mirrored = arc.Replace(" 51\r\n90.0\r\n", " 51\r\n90.0\r\n210\r\n0.0\r\n220\r\n0.0\r\n230\r\n-1.0\r\n");
            string result = DxfBendProcessor.Rotate(Entities(mirrored), 90);
            Assert.Contains("\r\n 10\r\n0.0\r\n 20\r\n-10.0\r\n", result);
            Assert.Contains("\r\n 50\r\n270.0\r\n 51\r\n0.0\r\n", result);
        }

        [Fact]
        public void TextAndInsert_GetAngle_SplineAndEllipseVectorsTurn()
        {
            string body =
                "  0\r\nTEXT\r\n  8\r\n0\r\n 10\r\n1.0\r\n 20\r\n0.0\r\n 30\r\n0.0\r\n 40\r\n3.0\r\n  1\r\nA\r\n 50\r\n10.0\r\n" +
                "  0\r\nELLIPSE\r\n  8\r\n0\r\n 10\r\n0.0\r\n 20\r\n0.0\r\n 30\r\n0.0\r\n 11\r\n4.0\r\n 21\r\n0.0\r\n 31\r\n0.0\r\n 40\r\n0.5\r\n" +
                "  0\r\nMTEXT\r\n  8\r\n0\r\n 10\r\n0.0\r\n 20\r\n0.0\r\n 30\r\n0.0\r\n 50\r\n0.0\r\n";
            string result = DxfBendProcessor.Rotate(Entities(body), 90);
            Assert.Contains("\r\n 10\r\n0.0\r\n 20\r\n1.0\r\n", result);          // точка вставки текста
            Assert.Contains("\r\n 50\r\n100.0\r\n", result);                       // угол текста, градусы
            Assert.Contains("\r\n 11\r\n0.0\r\n 21\r\n4.0\r\n", result);          // ось эллипса — вектор
            Assert.Contains("\r\n 50\r\n" + (Math.PI / 2).ToString("0.0#########", System.Globalization.CultureInfo.InvariantCulture), result); // MTEXT — радианы
        }

        [Theory]
        [InlineData(0, 30, -30)]      // наклонная — горизонтально, на наименьший угол
        [InlineData(0, 100, 80)]
        [InlineData(0, -30, 30)]
        [InlineData(0, 90, -90)]
        [InlineData(0, 0, 180)]       // уже горизонтальна — разворот на 180°
        [InlineData(180, 180, 0)]     // и обратно
        [InlineData(45, 0.0004, -135)]
        public void AlignToLine(double current, double line, double expected) =>
            Assert.Equal(expected, FlatRotationSettings.AlignToLine(current, line), 6);

        [Fact]
        public void Settings_NormalizeAndRemoveZero()
        {
            var s = new FlatRotationSettings();
            s.Set(@"C:\a.ipt", 270);
            Assert.Equal(-90, s.For(@"c:\A.ipt"));
            s.Set(@"C:\a.ipt", 360);
            Assert.Empty(s.Parts);
            Assert.Equal(0, s.For(@"C:\a.ipt"));
        }

        [Fact]
        public void Pipeline_LineKeysKept_LinesTurned_ManualMarkingTurnsWithPart()
        {
            // Своя настройка линии y = 40 (ключ — по исходным координатам) действует и на повёрнутой развертке.
            var lineKey = new BendLineInfo(1, "IV_BEND", 0, 40, 200, 40).Key;
            var request = new FlatRequest
            {
                Bends = l => l.Key == lineKey ? new BendLineOptions { Mode = BendLineMode.Remove } : new BendLineOptions(),
                Rotation = 90,
                Marking = new MarkingOptions { Enabled = true },
                MarkingText = "A-1",
                MarkingPosition = new MarkingPosition { X = 50, Y = 20, Angle = 0 } // в исходных координатах
            };
            var result = FlatPipeline.Run(TestDxf.Inventor2004(), request, withDrawing: true);
            DxfValidator.AssertValid(result.Dxf);

            var removed = result.BendLines.Single(b => b.Line.Key == lineKey);
            Assert.Equal(BendLineMode.Remove, removed.Options.Mode);
            Assert.Equal(-40, removed.Line.X1, 9);                       // (0; 40) → (−40; 0)
            Assert.Equal(0, removed.Line.Y1, 9);
            Assert.Single(result.Drawing.Polylines, p => p.Kind == LayerKind.Bend); // осталась только линия вниз

            var text = result.Marking.Text;
            Assert.Equal(-20, text.Center.X, 6);                          // (50; 20) → (−20; 50)
            Assert.Equal(50, text.Center.Y, 6);
            Assert.Equal(90, text.Angle, 6);
            Assert.Same(text, result.Drawing.Texts.Single());

            // Обратно: место, выбранное на повёрнутой развертке, хранится в исходных координатах.
            var stored = FlatPipeline.FromDrawing("p", text.Center, text.Angle, text.Height, 90);
            Assert.Equal(50, stored.X, 6);
            Assert.Equal(20, stored.Y, 6);
            Assert.Equal(0, stored.Angle, 6);
        }

        [Fact]
        public void AutoMarking_OnRotatedPart_IsHorizontalOnScreen()
        {
            var result = FlatPipeline.Run(TestDxf.Inventor2004(), new FlatRequest
            {
                Rotation = 90, Marking = new MarkingOptions { Enabled = true }, MarkingText = "A-1"
            }, withDrawing: true);
            Assert.Equal(MarkingStatus.Placed, result.Marking.Status);
            Assert.Equal(0, result.Marking.Text.Angle, 9);
            Assert.InRange(result.Marking.Text.Center.X, -100, 0);         // на повёрнутой детали
        }

        [Fact]
        public void TiltedText_FitsAndConflicts()
        {
            var drawing = Read(TestDxf.Inventor2004());
            var area = new MarkingArea(drawing);
            var tilted = DxfMarking.At("AB-12", 60, 75, 5, 30);
            Assert.True(area.Fits(tilted, 2));                            // наклонный текст в свободном месте
            Assert.Equal(60, tilted.Center.X, 9);
            Assert.Equal(30, tilted.Angle);
            var onHole = DxfMarking.At("AB-12", 100, 70, 5, 30);           // на отверстии Ø12 (100; 70)
            Assert.False(area.Fits(onHole, 2));
            var acrossBend = DxfMarking.At("AB-12", 60, 41, 5, 30);        // поперёк линии гиба y = 40
            Assert.False(area.Fits(acrossBend, 2));

            var placed = DxfMarking.Apply(TestDxf.Inventor2004(), "AB-12", new MarkingOptions { Enabled = true }, null,
                new MarkingPosition { X = 60, Y = 75, Angle = 30 });
            Assert.False(placed.Conflict);
            Assert.Contains("\r\n 50\r\n30.0\r\n", placed.Dxf);
            Assert.Contains("под 30°", placed.Description);
        }
    }
}

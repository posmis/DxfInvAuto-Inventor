using System;
using System.IO;
using System.Linq;
using InvAuto.Core.Bends;
using InvAuto.Core.Export;
using InvAuto.Core.Marking;
using InvAuto.Core.Preflight;
using InvAuto.Core.Preview;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>Маркировка обозначением: место на развертке, TEXT и слой в DXF, настройки общие и по детали.</summary>
    public class MarkingTests
    {
        private static MarkingOptions On(double height = 5, double min = 2.5, double margin = 2) =>
            new MarkingOptions { Enabled = true, Height = height, MinHeight = min, Margin = margin };

        private static FlatDrawing Read(string dxf) => DxfReader.Read(new StringReader(dxf));

        /// <summary>Прямоугольная пластина w × h: LWPOLYLINE (DXF 2004) или четыре LINE (R12), без таблицы слоёв.</summary>
        private static string Rect(double w, double h, bool modern = true)
        {
            string head = "  0\r\nSECTION\r\n  2\r\nHEADER\r\n  9\r\n$ACADVER\r\n  1\r\n" + (modern ? "AC1018" : "AC1009") +
                          "\r\n  9\r\n$HANDSEED\r\n  5\r\n30\r\n  0\r\nENDSEC\r\n  0\r\nSECTION\r\n  2\r\nENTITIES\r\n";
            string poly = "  0\r\nLWPOLYLINE\r\n  5\r\n20\r\n330\r\n1F\r\n100\r\nAcDbEntity\r\n  8\r\nIV_OUTER_PROFILE\r\n100\r\nAcDbPolyline\r\n 90\r\n4\r\n 70\r\n1\r\n" +
                FormattableString.Invariant($" 10\r\n0\r\n 20\r\n0\r\n 10\r\n{w}\r\n 20\r\n0\r\n 10\r\n{w}\r\n 20\r\n{h}\r\n 10\r\n0\r\n 20\r\n{h}\r\n");
            if (!modern) // в R12 контур — отрезки
            {
                poly = string.Concat(new[] { (0.0, 0.0, w, 0.0), (w, 0.0, w, h), (w, h, 0.0, h), (0.0, h, 0.0, 0.0) }
                    .Select(l => FormattableString.Invariant(
                        $"  0\r\nLINE\r\n  8\r\nIV_OUTER_PROFILE\r\n 10\r\n{l.Item1}\r\n 20\r\n{l.Item2}\r\n 30\r\n0.0\r\n 11\r\n{l.Item3}\r\n 21\r\n{l.Item4}\r\n 31\r\n0.0\r\n")));
            }
            return head + poly + "  0\r\nENDSEC\r\n  0\r\nEOF\r\n";
        }

        [Fact]
        public void Off_ByDefault_FileUnchanged()
        {
            Assert.False(new MarkingOptions().Enabled);
            Assert.False(new MarkingSettings().For("any.ipt").Enabled);
            string dxf = TestDxf.Inventor2004();
            var result = DxfMarking.Apply(dxf, "АБВГ.001", new MarkingOptions());
            Assert.Equal(MarkingStatus.Off, result.Status);
            Assert.Same(dxf, result.Dxf);
        }

        [Fact]
        public void Text_IsPlacedAwayFromHolesAndBendLines_AndWrittenAsText()
        {
            string dxf = TestDxf.Inventor2004();
            var result = DxfMarking.Apply(dxf, "PART-001", On());
            Assert.Equal(MarkingStatus.Placed, result.Status);
            var entities = DxfValidator.AssertValid(result.Dxf);

            Assert.Single(entities, e => e.Type == "TEXT" && e.Layer == MarkingOptions.DefaultLayer);
            Assert.Contains("\r\n  1\r\nPART-001\r\n", result.Dxf);
            Assert.Contains("\r\n 40\r\n5.0\r\n", result.Dxf);

            // Слой добавлен в таблицу слоёв (зелёный), число записей увеличено.
            Assert.Contains("\r\n  2\r\nMARKING\r\n 70\r\n0\r\n 62\r\n3\r\n", result.Dxf);
            Assert.Contains("AcDbSymbolTable\r\n 70\r\n5\r\n", result.Dxf);

            // Не задевает отверстие Ø12 (100; 70), линии гиба y = 40 и x = 150 и контур — с отступом 2 мм.
            var t = result.Text;
            Assert.Equal(0, t.Angle);
            Assert.True(t.MinX >= 2 && t.MaxX <= 198 && t.MinY >= 2 && t.MaxY <= 98);
            Assert.False(t.MaxX > 150 - 2 && t.MinX < 150 + 2, "текст на линии гиба x = 150");
            Assert.False(t.MaxY > 40 - 2 && t.MinY < 40 + 2, "текст на линии гиба y = 40");
            Assert.False(t.MaxX > 94 - 2 && t.MinX < 106 + 2 && t.MaxY > 64 - 2 && t.MinY < 76 + 2, "текст на отверстии");
        }

        [Fact]
        public void SymmetricPlate_TextInTheCenter()
        {
            var result = DxfMarking.Apply(Rect(200, 100), "A1", On(5));
            var t = result.Text;
            Assert.Equal(100, (t.MinX + t.MaxX) / 2, 6);
            Assert.Equal(50, (t.MinY + t.MaxY) / 2, 6);
            DxfValidator.AssertValid(result.Dxf);
        }

        [Fact]
        public void Cyrillic_EscapedBefore2007_Utf8From2007()
        {
            var old = DxfMarking.Apply(Rect(200, 100), "АБ-1", On());
            Assert.Contains("\r\n  1\r\n\\U+0410\\U+0411-1\r\n", old.Dxf);

            var modern = DxfMarking.Apply(Rect(200, 100).Replace("AC1018", "AC1021"), "АБ-1", On());
            string utf8 = new string(System.Text.Encoding.UTF8.GetBytes("АБ-1").Select(b => (char)b).ToArray());
            Assert.Contains("\r\n  1\r\n" + utf8 + "\r\n", modern.Dxf);
            Assert.Equal("АБ-1", modern.Text.Value);
        }

        [Fact]
        public void NarrowPart_TextShrinks_ThenTurnsVertical_ThenNoRoom()
        {
            // 40 мм в ширину: 10 символов × 5 × 0,9 = 45 мм не входят, 3,5 мм × 0,9 × 10 = 31,5 + 4 — входят.
            var shrunk = DxfMarking.Apply(Rect(40, 30), "1234567890", On(5, 2.5));
            Assert.Equal(MarkingStatus.Placed, shrunk.Status);
            Assert.True(shrunk.Text.Height < 5);
            Assert.Equal(0, shrunk.Text.Angle);

            // Высокая узкая полоса: горизонтально не входит, вертикально — да.
            var vertical = DxfMarking.Apply(Rect(12, 100), "1234567890", On(5, 5));
            Assert.Equal(MarkingStatus.Placed, vertical.Status);
            Assert.Equal(90, vertical.Text.Angle);
            Assert.True(vertical.Text.MinX >= 2 - 1e-9 && vertical.Text.MaxX <= 10 + 1e-9);
            Assert.Contains("\r\n 50\r\n90.0\r\n", vertical.Dxf);

            var none = DxfMarking.Apply(Rect(10, 10), "1234567890", On(5, 4));
            Assert.Equal(MarkingStatus.NoRoom, none.Status);
            Assert.Null(none.Text);
        }

        [Fact]
        public void Frame_TextGoesOnMetal_NotIntoBigCutout()
        {
            // Рамка 200×100 с вырезом 20…180 × 20…80: центр детали вырезается, текст — на полосе рамки.
            string frame = Rect(200, 100).Replace("  0\r\nENDSEC\r\n  0\r\nEOF",
                "  0\r\nLWPOLYLINE\r\n  5\r\n21\r\n330\r\n1F\r\n100\r\nAcDbEntity\r\n  8\r\nIV_INTERIOR_PROFILES\r\n100\r\nAcDbPolyline\r\n 90\r\n4\r\n 70\r\n1\r\n" +
                " 10\r\n20\r\n 20\r\n20\r\n 10\r\n180\r\n 20\r\n20\r\n 10\r\n180\r\n 20\r\n80\r\n 10\r\n20\r\n 20\r\n80\r\n" +
                "  0\r\nENDSEC\r\n  0\r\nEOF");
            var result = DxfMarking.Apply(frame, "АПФД.В9.13.00.012", On());
            Assert.Equal(MarkingStatus.Placed, result.Status);
            var t = result.Text;
            bool inCutout = t.MaxX > 20 && t.MinX < 180 && t.MaxY > 20 && t.MinY < 80;
            Assert.False(inCutout, "текст попал в вырез");
            Assert.True(t.MinX >= 2 && t.MaxX <= 198 && t.MinY >= 2 && t.MaxY <= 98);

            // Узкая рамка, где текст не помещается ни на одной полосе, — «не помещается», а не в вырез.
            string thin = frame.Replace("\r\n20\r\n", "\r\n4\r\n").Replace("\r\n180\r\n", "\r\n196\r\n").Replace("\r\n80\r\n", "\r\n96\r\n");
            Assert.Equal(MarkingStatus.NoRoom, DxfMarking.Apply(thin, "АПФД.В9.13.00.012", On()).Status);
        }

        private static string Frame() => Rect(200, 100).Replace("  0\r\nENDSEC\r\n  0\r\nEOF",
            "  0\r\nLWPOLYLINE\r\n  5\r\n21\r\n330\r\n1F\r\n100\r\nAcDbEntity\r\n  8\r\nIV_INTERIOR_PROFILES\r\n100\r\nAcDbPolyline\r\n 90\r\n4\r\n 70\r\n1\r\n" +
            " 10\r\n20\r\n 20\r\n20\r\n 10\r\n180\r\n 20\r\n20\r\n 10\r\n180\r\n 20\r\n80\r\n 10\r\n20\r\n 20\r\n80\r\n" +
            "  0\r\nENDSEC\r\n  0\r\nEOF");

        [Fact]
        public void CenterCut_IsReported_AndPreflightWarns()
        {
            var result = DxfMarking.Apply(Frame(), "АПФД.В9.13.00.012", On());
            Assert.True(result.CenterCut);
            Assert.False(result.Manual);
            Assert.Contains("центр детали вырезан", result.Description);
            var issues = PreflightChecker.Check(new PreflightPart { Key = "P", PartNumber = "P", HasFlatPattern = true, Marking = result },
                new PreflightSettings());
            var issue = Assert.Single(issues, i => i.Code == "MarkingCenterCut");
            Assert.Equal(IssueSeverity.Warning, issue.Severity);
            Assert.Single(issue.Locations); // отметка на развертке — где стоит текст

            Assert.False(DxfMarking.Apply(Rect(200, 100), "A1", On()).CenterCut);
        }

        [Fact]
        public void ManualPosition_UsedAsIs_ConflictWhenInCutoutOrOnLine()
        {
            // На нижней полосе рамки — годится.
            var ok = DxfMarking.Apply(Frame(), "A-1", On(5), position: new MarkingPosition { X = 100, Y = 10 });
            Assert.Equal(MarkingStatus.Placed, ok.Status);
            Assert.True(ok.Manual);
            Assert.False(ok.Conflict);
            Assert.Equal(100, (ok.Text.MinX + ok.Text.MaxX) / 2, 6);
            Assert.Equal(10, (ok.Text.MinY + ok.Text.MaxY) / 2, 6);
            Assert.Contains("\r\n  1\r\nA-1\r\n", ok.Dxf);
            DxfValidator.AssertValid(ok.Dxf);

            // Вертикально на левой полосе.
            var vertical = DxfMarking.Apply(Frame(), "A-1", On(5), position: new MarkingPosition { X = 10, Y = 50, Vertical = true });
            Assert.Equal(90, vertical.Text.Angle);
            Assert.False(vertical.Conflict);

            // В вырезе и поперёк края выреза — ставится, как просили, но с пометкой и замечанием.
            var inCutout = DxfMarking.Apply(Frame(), "A-1", On(5), position: new MarkingPosition { X = 100, Y = 50 });
            Assert.True(inCutout.Conflict);
            Assert.Contains("\r\n  1\r\nA-1\r\n", inCutout.Dxf);
            var onEdge = DxfMarking.Apply(Frame(), "A-1", On(5), position: new MarkingPosition { X = 100, Y = 20 });
            Assert.True(onEdge.Conflict);
            var issues = PreflightChecker.Check(new PreflightPart { Key = "P", PartNumber = "P", HasFlatPattern = true, Marking = onEdge },
                new PreflightSettings());
            Assert.Contains(issues, i => i.Code == "MarkingConflict");
            Assert.DoesNotContain(issues, i => i.Code == "MarkingCenterCut"); // место выбрано вручную
        }

        [Fact]
        public void ManualPosition_KeepsHeight_WithinLimits()
        {
            var shrunk = DxfMarking.Apply(Frame(), "A-1", On(5, 2.5), position: new MarkingPosition { X = 100, Y = 10, Height = 3 });
            Assert.Equal(3, shrunk.Text.Height, 9);
            var tooBig = DxfMarking.Apply(Frame(), "A-1", On(5, 2.5), position: new MarkingPosition { X = 100, Y = 10, Height = 9 });
            Assert.Equal(5, tooBig.Text.Height, 9);
            var tooSmall = DxfMarking.Apply(Frame(), "A-1", On(5, 2.5), position: new MarkingPosition { X = 100, Y = 10, Height = 1 });
            Assert.Equal(2.5, tooSmall.Text.Height, 9);
        }

        [Fact]
        public void Shrunk_IsInfo()
        {
            var shrunk = DxfMarking.Apply(Rect(40, 30), "1234567890", On(5, 2.5));
            Assert.True(shrunk.Shrunk);
            var issues = PreflightChecker.Check(new PreflightPart { Key = "P", PartNumber = "P", HasFlatPattern = true, Marking = shrunk },
                new PreflightSettings());
            Assert.Equal(IssueSeverity.Info, Assert.Single(issues, i => i.Code == "MarkingShrunk").Severity);
        }

        [Fact]
        public void Settings_RoundTripThroughXml_TwiceWithoutDuplicates()
        {
            string dir = Path.Combine(Path.GetTempPath(), "invauto-marking-xml-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new InvAuto.Core.Settings.XmlSettingsStore(dir, new InvAuto.Core.Logging.FileLogger(Path.Combine(dir, "logs")));
                var s = new MarkingSettings { Default = On(4) };
                s.SetOverride(@"C:\Детали\Рамка.ipt", new MarkingOptions { Enabled = false });
                s.SetPosition(@"C:\Детали\Опора.ipt", 12.5, 30, 90, 3);

                store.Save(MarkingSettings.Key, s);
                var loaded = store.Load<MarkingSettings>(MarkingSettings.Key);
                store.Save(MarkingSettings.Key, loaded);
                loaded = store.Load<MarkingSettings>(MarkingSettings.Key);

                Assert.True(loaded.For(@"C:\Детали\Опора.ipt").Enabled);
                Assert.Equal(4, loaded.Default.Height);
                Assert.False(loaded.For(@"C:\Детали\Рамка.ipt").Enabled);
                Assert.Single(loaded.Parts);
                var p = Assert.Single(loaded.Positions);
                Assert.Equal(12.5, p.X);
                Assert.Equal(90, p.EffectiveAngle);
                Assert.Equal(3, p.Height);
                Assert.False(new MarkingSettings().Default.Enabled); // по умолчанию выключена
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Settings_ManualPositions()
        {
            var s = new MarkingSettings();
            Assert.Null(s.PositionFor("C:\\a.ipt"));
            s.SetPosition("C:\\a.ipt", 10, 20, 90);
            s.SetPosition("c:\\A.ipt", 30, 40, 0); // заменяет, а не добавляет
            var p = s.PositionFor("C:\\a.ipt");
            Assert.Single(s.Positions);
            Assert.Equal(30, p.X);
            Assert.Equal(0, p.EffectiveAngle);
            s.ClearPosition("C:\\a.ipt");
            Assert.Null(s.PositionFor("C:\\a.ipt"));
        }

        [Fact]
        public void NoPartNumber_NoText()
        {
            var result = DxfMarking.Apply(Rect(200, 100), "  ", On());
            Assert.Equal(MarkingStatus.NoText, result.Status);
        }

        [Fact]
        public void R12_TextWithoutHandlesAndSubclasses()
        {
            var result = DxfMarking.Apply(Rect(200, 100, modern: false), "A1", On());
            Assert.Equal(MarkingStatus.Placed, result.Status);
            string text = result.Dxf.Substring(result.Dxf.IndexOf("TEXT", StringComparison.Ordinal));
            Assert.DoesNotContain("AcDbText", text);
            Assert.DoesNotContain("\r\n  5\r\n", text.Substring(0, text.IndexOf("ENDSEC", StringComparison.Ordinal)));
        }

        [Fact]
        public void TextAvoidsBendSlots()
        {
            // После обработки линий гиба (вырезы) текст не попадает на вырезы.
            var processed = DxfBendProcessor.Process(TestDxf.Inventor2004(), DxfBendProcessor.DefaultLayers,
                new BendLineOptions { Mode = BendLineMode.RectSlots, SlotWidth = 3 });
            var result = DxfMarking.Apply(processed.Text, "PART-001", On());
            var t = result.Text;
            Assert.False(t.MaxY > 40 - 1.5 - 2 && t.MinY < 40 + 1.5 + 2);
            DxfValidator.AssertValid(result.Dxf);
        }

        [Fact]
        public void WriteFinal_MarksEvenWithoutBendProcessing()
        {
            string dir = Path.Combine(Path.GetTempPath(), "invauto-marking-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                string raw = Path.Combine(dir, "a.raw.dxf"), final = Path.Combine(dir, "a.dxf");
                File.WriteAllBytes(raw, TestDxf.Cp1251(TestDxf.Inventor2004()));
                var (_, status) = DxfOutput.WriteFinal(raw, final, null, DxfBendProcessor.DefaultLayers, On(), "PART-7");
                Assert.Equal(MarkingStatus.Placed, status);
                string text = File.ReadAllText(final, DxfBendProcessor.FileEncoding);
                Assert.Contains("PART-7", text);
                // Кириллический текст файла (cp1251) остался теми же байтами.
                string cp1251 = new string(TestDxf.Cp1251(TestDxf.CyrillicText).Select(b => (char)b).ToArray());
                Assert.Contains(cp1251, text);
                DxfValidator.AssertValid(text);
            }
            finally
            {
                Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void Settings_PartOverride_And_Reset()
        {
            var s = new MarkingSettings();
            s.SetOverride("C:\\a.ipt", On(7));
            Assert.True(s.For("c:\\A.IPT").Enabled);
            Assert.Equal(7, s.For("C:\\a.ipt").Height);
            Assert.False(s.For("C:\\b.ipt").Enabled);
            Assert.True(s.HasOverride("C:\\a.ipt"));
            s.ClearOverride("C:\\A.ipt");
            Assert.False(s.HasOverride("C:\\a.ipt"));
            Assert.Empty(s.Parts);

            var bad = new MarkingOptions { Enabled = true, Height = 3, MinHeight = 10, Layer = " " }.Normalized();
            Assert.Equal(3, bad.MinHeight);
            Assert.Equal(MarkingOptions.DefaultLayer, bad.Layer);
            Assert.Equal("выкл.", new MarkingOptions().Summary);
        }

        /// <summary>Пример для просмотра глазами: INVAUTO_SAMPLE_DIR=папка dotnet test → marking.svg.</summary>
        [Fact]
        public void WriteSample_WhenRequested()
        {
            string dir = Environment.GetEnvironmentVariable("INVAUTO_SAMPLE_DIR");
            if (string.IsNullOrEmpty(dir))
                return;
            Directory.CreateDirectory(dir);
            var svg = new System.Text.StringBuilder("<svg xmlns='http://www.w3.org/2000/svg' viewBox='-10 -10 420 130' width='1260' height='390' style='background:#fff'>");
            var bends = new BendLineOptions { Mode = BendLineMode.RoundSlots, SlotWidth = 3, EndShape = BendEndShape.Circle, EndSize = 4 };
            var parts = new[]
            {
                (Dxf: DxfBendProcessor.Process(TestDxf.Inventor2004(), DxfBendProcessor.DefaultLayers, bends).Text, Text: "АБВГ.301261.001", X: 0.0),
                (Dxf: Rect(16, 100), Text: "ПЛ-12-0045", X: 230.0),
                (Dxf: Rect(120, 22), Text: "КР.002", X: 270.0)
            };
            foreach (var (dxf, value, x) in parts)
            {
                var drawing = Read(dxf);
                var t = DxfMarking.Apply(dxf, value, On(), drawing).Text;
                svg.Append(FormattableString.Invariant($"<g transform='translate({x},{drawing.MaxY - drawing.MinY}) scale(1,-1) translate({-drawing.MinX},{-drawing.MinY})'>"));
                foreach (var p in drawing.Polylines)
                    svg.Append("<polyline fill='none' stroke='").Append(p.Kind == LayerKind.Bend ? "#0696D7" : "#222").Append("' stroke-width='0.5' points='")
                       .Append(string.Join(" ", p.Points.Concat(p.Closed ? new[] { p.Points[0] } : new Point2[0]).Select(q => FormattableString.Invariant($"{q.X:0.##},{q.Y:0.##}"))))
                       .Append("'/>");
                svg.Append(FormattableString.Invariant($"<rect x='{t.MinX}' y='{t.MinY}' width='{t.MaxX - t.MinX}' height='{t.MaxY - t.MinY}' fill='none' stroke='#2E7D32' stroke-width='0.3' stroke-dasharray='1 1'/>"));
                svg.Append(FormattableString.Invariant($"<text transform='translate({t.X},{t.Y}) scale(1,-1) rotate({-t.Angle})' font-family='Arial' font-size='{t.Height / 0.716}' fill='#2E7D32'>{t.Value}</text>"));
                svg.Append("</g>");
            }
            File.WriteAllText(Path.Combine(dir, "marking.svg"), svg.Append("</svg>").ToString());
        }

        [Fact]
        public void Preflight_WarnsWhenMarkingDoesNotFit()
        {
            var part = new PreflightPart { Key = "P", PartNumber = "P", HasFlatPattern = true, Marking = new MarkingResult(MarkingStatus.NoRoom, "") };
            Assert.Contains(PreflightChecker.Check(part, new PreflightSettings()), i => i.Code == "MarkingNoRoom");

            var noNumber = new PreflightPart { Key = "Q", HasFlatPattern = true, Marking = new MarkingResult(MarkingStatus.NoText, "") };
            Assert.DoesNotContain(PreflightChecker.Check(noNumber, new PreflightSettings()), i => i.Code == "MarkingNoText"); // уже «Нет обозначения»
            Assert.Contains(PreflightChecker.Check(noNumber, new PreflightSettings { RequirePartNumber = false }), i => i.Code == "MarkingNoText");
        }
    }
}

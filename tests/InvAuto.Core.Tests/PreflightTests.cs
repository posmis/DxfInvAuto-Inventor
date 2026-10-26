using System;
using System.IO;
using System.Linq;
using System.Text;
using InvAuto.Core.Bends;
using InvAuto.Core.Preflight;
using InvAuto.Core.Preview;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>Проверка деталей перед выгрузкой.</summary>
    public class PreflightTests
    {
        private static readonly PreflightSettings Defaults = new PreflightSettings();

        private static string Line(double x1, double y1, double x2, double y2, string layer = "IV_OUTER_PROFILE") =>
            FormattableString.Invariant($"0\nLINE\n8\n{layer}\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}\n");

        private static string Circle(double x, double y, double r) =>
            FormattableString.Invariant($"0\nCIRCLE\n8\nIV_INTERIOR_PROFILES\n10\n{x}\n20\n{y}\n40\n{r}\n");

        private static FlatDrawing Read(params string[] entities)
        {
            var sb = new StringBuilder("0\nSECTION\n2\nENTITIES\n");
            foreach (var e in entities)
                sb.Append(e);
            sb.Append("0\nENDSEC\n0\nEOF\n");
            return DxfReader.Read(new StringReader(sb.ToString()));
        }

        /// <summary>Пластина 200×100, толщина 3 мм, линия гиба по x = 100.</summary>
        private static PreflightPart Plate(params string[] holes)
        {
            var entities = new[] { Line(0, 0, 200, 0), Line(200, 0, 200, 100), Line(200, 100, 0, 100), Line(0, 100, 0, 0) }
                .Concat(holes).ToArray();
            return new PreflightPart
            {
                Key = @"C:\Детали\Пластина.ipt",
                FileName = "Пластина",
                PartNumber = "АБВГ.001",
                Thickness = 3,
                HasFlatPattern = true,
                Drawing = Read(entities),
                BendLines = new[] { new BendLineInfo(1, "IV_BEND", 100, 0, 100, 100) },
                FlatLength = 200,
                FlatWidth = 100
            };
        }

        private static string[] Codes(PreflightPart part, PreflightSettings settings = null) =>
            PreflightChecker.Check(part, settings ?? Defaults).Select(i => i.Code).ToArray();

        [Fact]
        public void CleanPart_HasNoIssues()
        {
            Assert.Empty(Codes(Plate(Circle(40, 50, 5))));
        }

        [Fact]
        public void NoFlat_IsError_OrWarningWhenItWillBeCreated()
        {
            var part = Plate();
            part.HasFlatPattern = false;
            var issue = PreflightChecker.Check(part, Defaults).Single();
            Assert.Equal("NoFlat", issue.Code);
            Assert.Equal(IssueSeverity.Error, issue.Severity);

            part.WillCreateFlat = true;
            Assert.Equal(IssueSeverity.Warning, PreflightChecker.Check(part, Defaults).Single().Severity);
        }

        [Fact]
        public void FlatFailed_AndNotFormed()
        {
            var part = Plate();
            part.FlatError = "транслятор вернул ошибку";
            Assert.Equal(new[] { "FlatFailed" }, Codes(part));

            part.FlatError = string.Empty;
            part.Drawing = null;
            var issue = PreflightChecker.Check(part, Defaults).Single();
            Assert.Equal("NotFormed", issue.Code);
            Assert.Equal(IssueSeverity.Info, issue.Severity);
        }

        [Fact]
        public void ModelState_PartNumber_Material()
        {
            var part = Plate();
            part.NeedsUpdate = true;
            part.IsModified = true;
            part.PartNumber = " ";
            part.Material = MaterialCheck.Missing;
            Assert.Equal(new[] { "NeedsUpdate", "Modified", "NoPartNumber", "NoMaterial" }, Codes(part));

            part.Material = MaterialCheck.Mismatch;
            part.MaterialThickness = 2;
            Assert.Contains("MaterialMismatch", Codes(part));
            Assert.Contains(PreflightChecker.Check(part, Defaults), i => i.Message == "Материал 2 мм, деталь 3 мм");
            Assert.DoesNotContain("NoPartNumber", Codes(part, new PreflightSettings { RequirePartNumber = false }));
        }

        [Fact]
        public void OpenContour_IsError_WithLocations()
        {
            var part = Plate();
            part.Drawing = Read(Line(0, 0, 200, 0), Line(200, 0, 200, 100), Line(200, 100, 0, 100)); // нет левой стороны
            var issue = PreflightChecker.Check(part, Defaults).Single(i => i.Code == "OpenContour");
            Assert.Equal(IssueSeverity.Error, issue.Severity);
            Assert.Equal(2, issue.Locations.Count);
        }

        [Fact]
        public void SmallHoles_UseThicknessOrMinimum()
        {
            // Толщина 3: отверстия Ø2 и Ø2.5 меньше 3 мм, Ø4 — нормальное.
            var part = Plate(Circle(30, 30, 1), Circle(30, 70, 1.25), Circle(60, 50, 2));
            var issue = PreflightChecker.Check(part, Defaults).Single(i => i.Code == "SmallHole");
            Assert.Equal(2, issue.Locations.Count);
            Assert.Equal("Отверстия < 3 мм: 2 шт., мин. 2 мм", issue.Message);

            Assert.DoesNotContain("SmallHole", Codes(part, new PreflightSettings { MinHoleFactor = 0.5, MinHoleMm = 1 }));
            Assert.Contains("SmallHole", Codes(part, new PreflightSettings { MinHoleFactor = 0, MinHoleMm = 5 }));
        }

        [Fact]
        public void HolesNearBendLine_IncludingOnTheLine()
        {
            // Линия гиба x = 100, толщина 3 → зазор 6 мм. Ø10 в x = 112 — край в 7 мм (норма),
            // Ø10 в x = 108 — край в 3 мм, паз поперёк линии — на самой линии.
            var slot = "0\nLWPOLYLINE\n8\nIV_INTERIOR_PROFILES\n90\n4\n70\n1\n10\n95\n20\n80\n10\n105\n20\n80\n10\n105\n20\n85\n10\n95\n20\n85\n";
            var part = Plate(Circle(112, 20, 5), Circle(108, 50, 5), slot);

            var issue = PreflightChecker.Check(part, Defaults).Single(i => i.Code == "HoleNearBend");
            Assert.Equal(2, issue.Locations.Count);
            Assert.Equal("Отверстия у линии гиба (< 6 мм): 2 шт., есть на линии", issue.Message);

            Assert.DoesNotContain("HoleNearBend", Codes(part, new PreflightSettings { BendClearanceFactor = 0 }));
            part.BendLines = Array.Empty<BendLineInfo>();
            Assert.DoesNotContain("HoleNearBend", Codes(part));
        }

        [Fact]
        public void SheetSize_ComparesLongAndShortSides()
        {
            var part = Plate();
            part.FlatLength = 1400;
            part.FlatWidth = 2900; // повернём — помещается на 3000 × 1500
            Assert.DoesNotContain("SheetSize", Codes(part));

            part.FlatWidth = 3100;
            Assert.Contains("SheetSize", Codes(part));
            Assert.DoesNotContain("SheetSize", Codes(part, new PreflightSettings { CheckSheetSize = false }));
        }

        [Fact]
        public void CheckAll_FindsDuplicatePartNumbers_ButNotSameFile()
        {
            var a = Plate();
            var b = Plate();
            b.Key = @"C:\Детали\Пластина-2.ipt";
            b.PartNumber = "абвг.001 ";
            var c = Plate();
            c.Key = @"C:\Детали\Другая.ipt";
            c.PartNumber = "АБВГ.002";

            var results = PreflightChecker.CheckAll(new[] { a, b, c }, Defaults);

            Assert.Contains(results[a.Key], i => i.Code == "DuplicatePartNumber" && i.Message.Contains("Пластина-2.ipt"));
            Assert.Contains(results[b.Key], i => i.Code == "DuplicatePartNumber");
            Assert.DoesNotContain(results[c.Key], i => i.Code == "DuplicatePartNumber");

            var single = PreflightChecker.CheckAll(new[] { a, Plate() }, Defaults); // один и тот же файл дважды — не повтор
            Assert.DoesNotContain(single[a.Key], i => i.Code == "DuplicatePartNumber");
        }

        [Fact]
        public void Summary_CountsPartsBySeverity()
        {
            var ok = Plate();
            var warn = Plate();
            warn.Key = "W";
            warn.PartNumber = "W";
            warn.IsModified = true;
            var err = Plate();
            err.Key = "E";
            err.PartNumber = "E";
            err.HasFlatPattern = false;
            err.IsModified = true;

            var (errors, warnings) = PreflightChecker.Summary(PreflightChecker.CheckAll(new[] { ok, warn, err }, Defaults));
            Assert.Equal(1, errors);
            Assert.Equal(1, warnings);
        }

        [Fact]
        public void Settings_Normalize()
        {
            var s = new PreflightSettings { MinHoleFactor = -1, BendClearanceFactor = double.NaN, SheetLength = 0 };
            s.Normalize();
            Assert.Equal(0, s.MinHoleFactor);
            Assert.Equal(0, s.BendClearanceFactor);
            Assert.Equal(10, s.SheetLength);
        }

        [Fact]
        public void ViewSettings_AddsCheckColumnOnce_KeepingUserColumns()
        {
            var old = new InvAuto.Core.SheetMetal.SheetMetalViewSettings
            {
                ColumnsVersion = 3,
                Columns = { "Thumbnail", "FileName", "Mass" }
            };
            old.Normalize();
            Assert.Equal(new[] { "Thumbnail", "FileName", "Check", "Mass" }, old.Columns);

            old.Columns.Remove("Check"); // пользователь убрал колонку — больше не возвращаем
            old.Normalize();
            Assert.DoesNotContain("Check", old.Columns);

            var fresh = new InvAuto.Core.SheetMetal.SheetMetalViewSettings();
            fresh.Normalize();
            Assert.Equal(1, fresh.Columns.IndexOf("Check"));
        }

        [Fact]
        public void SegmentDistance_ParallelCrossingAndApart()
        {
            Assert.Equal(0, Geometry2.SegmentDistance(new Point2(0, 0), new Point2(10, 10), new Point2(0, 10), new Point2(10, 0)));
            Assert.Equal(5, Geometry2.SegmentDistance(new Point2(0, 0), new Point2(10, 0), new Point2(0, 5), new Point2(10, 5)), 9);
            Assert.Equal(5, Geometry2.SegmentDistance(new Point2(0, 0), new Point2(10, 0), new Point2(15, 0), new Point2(20, 0)), 9);
        }
    }
}

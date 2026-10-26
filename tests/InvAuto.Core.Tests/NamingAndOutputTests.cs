using System;
using System.Collections.Generic;
using System.IO;
using InvAuto.Core.Export;
using InvAuto.Core.Naming;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class WindowsFileNameTests
    {
        [Theory]
        [InlineData("A<B>C:D\"E/F\\G|H?I*J", "A_B_C_D_E_F_G_H_I_J")]
        [InlineData("Деталь\tс табуляцией", "Деталь_с табуляцией")]
        [InlineData("Опора. ", "Опора")]
        [InlineData("...", "_")]
        [InlineData("", "_")]
        public void InvalidCharacters_FollowWindowsRules_OnAnyOs(string input, string expected)
        {
            Assert.Equal(expected, FileNameTemplate.SanitizeFileName(input));
        }

        [Theory]
        [InlineData("CON", "_CON")]
        [InlineData("nul", "_nul")]
        [InlineData("COM1", "_COM1")]
        [InlineData("lpt9.txt", "_lpt9.txt")]
        [InlineData("CONSOLE", "CONSOLE")]
        [InlineData("COM10", "COM10")]
        public void ReservedWindowsNames_GetPrefix(string input, string expected)
        {
            Assert.Equal(expected, FileNameTemplate.SanitizeFileName(input));
        }

        [Fact]
        public void RelativePath_SanitizesEverySegment_AndDropsParentLinks()
        {
            string sep = Path.DirectorySeparatorChar.ToString();
            Assert.Equal("Сталь_3" + sep + "_CON", FileNameTemplate.SanitizeRelativePath(@"..\Сталь:3\CON\."));
        }
    }

    public class UniquePathTests
    {
        [Fact]
        public void SameNameInOneExport_GetsNumberSuffix_CaseInsensitive()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string a = ExportNaming.UniquePath("out", "Опора", used);
            string b = ExportNaming.UniquePath("out", "ОПОРА", used);
            string c = ExportNaming.UniquePath("out", "Опора", used);
            string other = ExportNaming.UniquePath(Path.Combine("out", "3.0мм"), "Опора", used);

            Assert.Equal(Path.Combine("out", "Опора.dxf"), a);
            Assert.Equal(Path.Combine("out", "ОПОРА_2.dxf"), b);
            Assert.Equal(Path.Combine("out", "Опора_3.dxf"), c);
            Assert.Equal(Path.Combine("out", "3.0мм", "Опора.dxf"), other);
        }

        [Fact]
        public void FullPipeline_TemplateFolderNameAndSuffix()
        {
            var settings = new FlatPatternExportSettings
            {
                FileNameTemplate = "{PartNumber} {Name}",
                SubfolderTemplate = "{Material}",
                SortByThickness = true,
                QuantityMultiplier = 3
            };
            var part = new SheetMetalPart { Name = "Опора левая", PartNumber = "ЕЛГ.001", Material = "Ст3", ThicknessMm = 2, Quantity = 2 };
            var values = part.ToTemplateValues(settings.QuantityMultiplier);

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            string folder = Path.Combine("out", ExportNaming.RelativeFolder(settings, values));
            string first = ExportNaming.UniquePath(folder, ExportNaming.FileName(settings, values), used);
            string second = ExportNaming.UniquePath(folder, ExportNaming.FileName(settings, values), used);

            Assert.Equal(Path.Combine("out", "2.0мм", "Ст3", "ЕЛГ.001-Опора-левая.dxf"), first);
            Assert.EndsWith("ЕЛГ.001-Опора-левая_2.dxf", second);
            Assert.Equal(6, values["Qty"]);
        }
    }

    public class TemplateEdgeCaseTests
    {
        [Fact]
        public void NullValue_IsEmpty_AndFormatIgnoredForStrings()
        {
            var values = new Dictionary<string, object> { ["A"] = null, ["B"] = "текст" };
            Assert.Equal("[]-[текст]", FileNameTemplate.Render("[{A}]-[{B:0.00}]", values));
        }

        [Fact]
        public void TokenWithSpaces_AndUnknownToken()
        {
            var values = new Dictionary<string, object> { ["Qty"] = 5 };
            Assert.Equal("5шт {Нет}", FileNameTemplate.Render("{ Qty }шт {Нет}", values));
        }

        [Fact]
        public void EmptyTemplate_IsEmpty()
        {
            Assert.Equal(string.Empty, FileNameTemplate.Render(null, new Dictionary<string, object>()));
        }

        [Fact]
        public void LengthAndWidthTokens_FromFlatSize()
        {
            var part = new SheetMetalPart { FlatLengthMm = 1250.5, FlatWidthMm = 640 };
            Assert.Equal("1251x640", FileNameTemplate.Render("{Length:0}x{Width:0}", part.ToTemplateValues()));
        }
    }
}

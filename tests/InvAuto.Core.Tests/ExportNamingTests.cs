using System.IO;
using InvAuto.Core.Export;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class ExportNamingTests
    {
        private static SheetMetalPart Part(string name, double thickness = 3) =>
            new SheetMetalPart { Name = name, Material = "Сталь 09Г2С", ThicknessMm = thickness, Quantity = 2 };

        [Theory]
        [InlineData("Опора верхняя", "Опора-верхняя-t3.0мм-2шт")]
        [InlineData("Деталь - 1", "Деталь-1-t3.0мм-2шт")]
        [InlineData("A--B", "A--B-t3.0мм-2шт")]
        [InlineData("  Кронштейн   левый ", "Кронштейн-левый-t3.0мм-2шт")]
        public void FileName_HasNoSpaces(string name, string expected)
        {
            var settings = new FlatPatternExportSettings();
            Assert.Equal(expected, ExportNaming.FileName(settings, Part(name).ToTemplateValues()));
        }

        [Fact]
        public void SortByThickness_AddsThicknessFolderFirst()
        {
            var values = Part("A", 1.25).ToTemplateValues();
            Assert.Equal("", ExportNaming.RelativeFolder(new FlatPatternExportSettings(), values));
            Assert.Equal("1.25мм", ExportNaming.RelativeFolder(new FlatPatternExportSettings { SortByThickness = true }, values));
            Assert.Equal(Path.Combine("1.25мм", "Сталь 09Г2С"),
                ExportNaming.RelativeFolder(new FlatPatternExportSettings { SortByThickness = true, SubfolderTemplate = "{Material}" }, values));
            Assert.Equal("3.0мм", ExportNaming.ThicknessFolder(3));
        }
    }
}

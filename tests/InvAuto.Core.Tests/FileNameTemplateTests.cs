using System.Collections.Generic;
using InvAuto.Core.Export;
using InvAuto.Core.Naming;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class FileNameTemplateTests
    {
        private static SheetMetalPart Part() => new SheetMetalPart
        {
            Name = "Кронштейн-01",
            PartNumber = "АБВГ.001",
            Material = "Сталь 09Г2С",
            ThicknessMm = 3,
            Quantity = 4
        };

        [Fact]
        public void DefaultTemplate_MatchesOldPluginNaming()
        {
            var template = new FlatPatternExportSettings().FileNameTemplate;
            Assert.Equal("Кронштейн-01-t3.0мм-4шт", FileNameTemplate.Render(template, Part().ToTemplateValues()));
        }

        [Fact]
        public void Qty_IsMultipliedByProductCount()
        {
            Assert.Equal("12", FileNameTemplate.Render("{Qty}", Part().ToTemplateValues(quantityMultiplier: 3)));
        }

        [Fact]
        public void Tokens_AreCaseInsensitive_AndSupportFormat()
        {
            var part = Part();
            part.ThicknessMm = 1.5;
            Assert.Equal("1.50 Сталь 09Г2С", FileNameTemplate.Render("{thickness:0.00} {MATERIAL}", part.ToTemplateValues()));
        }

        [Fact]
        public void CustomProperties_AreAvailable_ButBuiltInsWin()
        {
            var part = Part();
            part.CustomProperties["Заказ"] = "З-17";
            part.CustomProperties["Name"] = "не должно попасть";
            Assert.Equal("З-17_Кронштейн-01", FileNameTemplate.Render("{Заказ}_{Name}", part.ToTemplateValues()));
        }

        [Fact]
        public void UnknownToken_IsLeftAsIs()
        {
            Assert.Equal("{Nmae}", FileNameTemplate.Render("{Nmae}", new Dictionary<string, object>()));
        }

        [Fact]
        public void SanitizeFileName_ReplacesInvalidChars()
        {
            Assert.Equal("A_B_C", FileNameTemplate.SanitizeFileName("A/B\0C"));
            Assert.Equal("Деталь", FileNameTemplate.SanitizeFileName(" Деталь. "));
            Assert.Equal("_", FileNameTemplate.SanitizeFileName("  "));
        }

        [Fact]
        public void SanitizeRelativePath_DropsEmptyAndParentSegments()
        {
            string sep = System.IO.Path.DirectorySeparatorChar.ToString();
            Assert.Equal("Сталь" + sep + "3.0мм", FileNameTemplate.SanitizeRelativePath(@"..\Сталь\\3.0мм\"));
            Assert.Equal(string.Empty, FileNameTemplate.SanitizeRelativePath(""));
        }
    }
}

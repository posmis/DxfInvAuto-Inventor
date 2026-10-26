using InvAuto.Core.Export;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class DxfFormatStringTests
    {
        [Fact]
        public void Default_ContainsVersionLayersAndHidesTangents()
        {
            string s = DxfFormatString.Build(new FlatPatternExportSettings());

            Assert.StartsWith("FLAT PATTERN DXF?AcadVersion=2000&", s);
            Assert.Contains("OuterProfileLayer=OUTER", s);
            Assert.Contains("InteriorProfilesLayer=INNER", s);
            Assert.Contains("BendLayer=BEND_UP", s);
            Assert.Contains("IV_TANGENT", s);
            Assert.DoesNotContain("IV_BEND;", s);
        }

        [Fact]
        public void WithoutBendLines_HidesBendLayers()
        {
            string s = DxfFormatString.Build(new FlatPatternExportSettings { ExportBendLines = false });

            Assert.DoesNotContain("BendLayer=", s);
            Assert.Contains("IV_BEND;IV_BEND_DOWN", s);
        }

        [Fact]
        public void EmptyLayerName_IsOmitted()
        {
            string s = DxfFormatString.Build(new FlatPatternExportSettings { OuterProfileLayer = " " });
            Assert.DoesNotContain("OuterProfileLayer", s);
        }
    }
}

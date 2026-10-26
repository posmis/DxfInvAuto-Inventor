using InvAuto.Core.Export;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class DxfFormatStringTests
    {
        [Fact]
        public void Default_MatchesBundledIni_OnlyProfilesAndBendsInInventorLayers()
        {
            string s = DxfFormatString.Build(new FlatPatternExportSettings());

            Assert.StartsWith("FLAT PATTERN DXF?AcadVersion=2004&", s);
            // Слои не переименовываются — остаются IV_OUTER_PROFILE, IV_INTERIOR_PROFILES, IV_BEND, IV_BEND_DOWN.
            Assert.DoesNotContain("ProfileLayer=", s);
            Assert.DoesNotContain("BendLayer=", s);
            Assert.Contains("RebaseGeometry=False", s);
            Assert.Contains("MergeProfilesIntoPolyline=False", s);

            foreach (var hidden in new[] { "IV_TANGENT", "IV_ARC_CENTERS", "IV_FEATURE_PROFILES", "IV_ROLL", "IV_MARK_SURFACE" })
                Assert.Contains(hidden, s);
            Assert.DoesNotContain("IV_BEND", s);
            Assert.DoesNotContain("IV_OUTER_PROFILE", s);
            Assert.DoesNotContain("IV_INTERIOR_PROFILES", s);
        }

        [Fact]
        public void WithoutBendLines_HidesBendLayers()
        {
            string s = DxfFormatString.Build(new FlatPatternExportSettings { ExportBendLines = false });
            Assert.Contains("IV_BEND;IV_BEND_DOWN", s);
        }

        [Fact]
        public void RenamedLayers_AreWritten()
        {
            string s = DxfFormatString.Build(new FlatPatternExportSettings { OuterProfileLayer = "CUT", BendUpLayer = "BEND" });
            Assert.Contains("OuterProfileLayer=CUT", s);
            Assert.Contains("BendLayer=BEND", s);
        }
    }
}

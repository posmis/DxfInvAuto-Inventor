using System;
using System.IO;
using System.Linq;
using System.Text;
using InvAuto.Core.Export;
using InvAuto.Core.Preview;
using InvAuto.Core.Structure;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class DxfReaderMoreTests
    {
        private static FlatDrawing Entities(string body) =>
            DxfReader.Read(new StringReader("0\nSECTION\n2\nENTITIES\n" + body + "0\nENDSEC\n0\nEOF\n"));

        [Fact]
        public void RealisticInventorFile_ReadsAllGeometry()
        {
            var d = DxfReader.Read(new StringReader(TestDxf.Inventor2004()));

            Assert.Equal(4, d.Polylines.Count); // контур, отверстие, две линии гиба
            Assert.Equal(200, d.Width, 6);
            Assert.Equal(100, d.Height, 6);
            Assert.Equal(2, d.Polylines.Count(p => p.Kind == LayerKind.Bend));
            var outline = d.Polylines.Single(p => p.Layer == "IV_OUTER_PROFILE");
            Assert.True(outline.Closed);
            Assert.True(outline.Points.Count > 8, "скругления разбиты на отрезки");
        }

        [Fact]
        public void Spline_UsesFitPoints_OrControlPoints()
        {
            var fit = Entities("0\nSPLINE\n8\nA\n70\n0\n10\n0\n20\n0\n10\n50\n20\n50\n11\n1\n21\n1\n11\n2\n21\n3\n11\n4\n21\n4\n");
            Assert.Equal(3, fit.Polylines.Single().Points.Count);
            Assert.Equal(1, fit.MinX, 9);

            var control = Entities("0\nSPLINE\n8\nA\n70\n1\n10\n0\n20\n0\n10\n10\n20\n0\n10\n10\n20\n10\n");
            Assert.Equal(3, control.Polylines.Single().Points.Count);
            Assert.True(control.Polylines.Single().Closed);
        }

        [Fact]
        public void Ellipse_FullAndPartial()
        {
            var full = Entities("0\nELLIPSE\n8\nA\n10\n0\n20\n0\n11\n10\n21\n0\n40\n0.5\n41\n0\n42\n6.283185307179586\n");
            Assert.Equal(20, full.Width, 3);
            Assert.Equal(10, full.Height, 3);

            var half = Entities("0\nELLIPSE\n8\nA\n10\n0\n20\n0\n11\n10\n21\n0\n40\n0.5\n41\n0\n42\n3.141592653589793\n");
            Assert.True(half.MinY > -1e-6, "верхняя половина эллипса");
        }

        [Fact]
        public void NegativeBulge_ArcBulgesToTheLeft()
        {
            var d = Entities("0\nLWPOLYLINE\n8\nA\n90\n2\n70\n0\n10\n0\n20\n0\n42\n-1\n10\n2\n20\n0\n");
            Assert.Equal(1, d.MaxY, 6);
            Assert.True(d.MinY > -1e-9);
        }

        [Fact]
        public void NestedInsert_AndSelfReferencingBlock_DoNotHang()
        {
            string dxf =
                "0\nSECTION\n2\nBLOCKS\n" +
                "0\nBLOCK\n2\nINNER\n10\n0\n20\n0\n0\nLINE\n8\n0\n10\n0\n20\n0\n11\n1\n21\n0\n0\nENDBLK\n" +
                "0\nBLOCK\n2\nOUTER\n10\n0\n20\n0\n0\nINSERT\n8\n0\n2\nINNER\n10\n5\n20\n0\n0\nENDBLK\n" +
                "0\nBLOCK\n2\nLOOP\n10\n0\n20\n0\n0\nINSERT\n8\n0\n2\nLOOP\n10\n1\n20\n0\n0\nENDBLK\n" +
                "0\nENDSEC\n" +
                "0\nSECTION\n2\nENTITIES\n" +
                "0\nINSERT\n8\nIV_BEND\n2\nOUTER\n10\n10\n20\n0\n" +
                "0\nINSERT\n8\nX\n2\nLOOP\n10\n0\n20\n0\n" +
                "0\nENDSEC\n0\nEOF\n";

            var d = DxfReader.Read(new StringReader(dxf));
            var line = d.Polylines.Single();
            Assert.Equal("IV_BEND", line.Layer); // слой "0" внутри блоков наследует слой вставки
            Assert.Equal(15, line.Points[0].X, 9);
            Assert.Equal(16, line.Points[1].X, 9);
        }

        [Fact]
        public void UnknownEntities_AndTruncatedFile_AreTolerated()
        {
            var d = DxfReader.Read(new StringReader("0\nSECTION\n2\nENTITIES\n0\nMTEXT\n8\nA\n1\nтекст\n0\nLINE\n8\nA\n10\n0\n20\n0\n11\n5\n21\n5\n"));
            Assert.Single(d.Polylines);
        }

        [Theory]
        [InlineData("IV_BEND", LayerKind.Bend)]
        [InlineData("iv_bend_down", LayerKind.Bend)]
        [InlineData("IV_TANGENT", LayerKind.Auxiliary)]
        [InlineData("IV_FEATURE_PROFILES", LayerKind.Auxiliary)]
        [InlineData("IV_ARC_CENTERS", LayerKind.Auxiliary)]
        [InlineData("IV_OUTER_PROFILE", LayerKind.Profile)]
        [InlineData("CUT", LayerKind.Profile)]
        public void LayerKinds(string layer, LayerKind kind)
        {
            Assert.Equal(kind, Polyline2.ClassifyLayer(layer));
        }

        [Fact]
        public void EmptyDrawing()
        {
            Assert.True(FlatDrawing.Empty.IsEmpty);
            Assert.True(DxfReader.Read(new StringReader("")).IsEmpty);
        }

        [Fact]
        public void ReadFile_Works()
        {
            string path = Path.Combine(Path.GetTempPath(), "InvAutoRead_" + Guid.NewGuid().ToString("N") + ".dxf");
            try
            {
                File.WriteAllBytes(path, TestDxf.Cp1251(TestDxf.Inventor2004()));
                Assert.Equal(4, DxfReader.ReadFile(path).Polylines.Count);
            }
            finally
            {
                File.Delete(path);
            }
        }
    }

    public class AssemblyNodeMoreTests
    {
        [Fact]
        public void PartRoot_CollectsItself()
        {
            var root = new AssemblyNode("Лист.ipt", NodeKind.SheetMetal, 1, null)
            {
                SheetMetal = new SheetMetalPart { FullFileName = "Лист.ipt", Name = "Лист" }
            };
            var parts = root.CollectSheetMetal();
            Assert.Equal(1, parts.Single().Quantity);
        }

        [Fact]
        public void NoSheetMetal_GivesEmptyList()
        {
            var root = new AssemblyNode("A.iam", NodeKind.Assembly, 1, null);
            root.Children.Add(new AssemblyNode("Болт.ipt", NodeKind.Standard, 4, root));
            Assert.Empty(root.CollectSheetMetal());
        }

        [Fact]
        public void DeepNesting_MultipliesQuantities()
        {
            var root = new AssemblyNode("A.iam", NodeKind.Assembly, 1, null);
            var level = root;
            for (int i = 0; i < 5; i++)
            {
                var next = new AssemblyNode($"L{i}.iam", NodeKind.Assembly, 2, level);
                level.Children.Add(next);
                level = next;
            }
            level.Children.Add(new AssemblyNode("P.ipt", NodeKind.SheetMetal, 3, level)
            {
                SheetMetal = new SheetMetalPart { FullFileName = "P.ipt", Name = "P" }
            });

            Assert.Equal(96, root.CollectSheetMetal().Single().Quantity); // 2^5 × 3
            Assert.Equal(6, root.Descendants().Count());
        }

        [Fact]
        public void Collect_SortsByPartNumberThenName()
        {
            var root = new AssemblyNode("A.iam", NodeKind.Assembly, 1, null);
            // Обозначения латиницей: порядок кириллицы и латиницы зависит от языка Windows (в русской кириллица раньше).
            foreach (var (file, number) in new[] { ("c.ipt", "ELG.003"), ("a.ipt", ""), ("b.ipt", "ELG.001") })
            {
                root.Children.Add(new AssemblyNode(file, NodeKind.SheetMetal, 1, root)
                {
                    SheetMetal = new SheetMetalPart { FullFileName = file, Name = file, PartNumber = number }
                });
            }
            Assert.Equal(new[] { "a.ipt", "b.ipt", "c.ipt" }, root.CollectSheetMetal().Select(p => p.Name));
        }
    }

    public class DxfFormatStringMoreTests
    {
        [Fact]
        public void EveryHiddenLayer_IsListed_AndCuttingLayersAreNot()
        {
            string s = DxfFormatString.Build(new FlatPatternExportSettings());
            foreach (string layer in DxfFormatString.AlwaysHiddenLayers)
                Assert.Contains(layer, s);
            string hidden = s.Substring(s.IndexOf("InvisibleLayers=", StringComparison.Ordinal));
            Assert.DoesNotContain("IV_OUTER_PROFILE", hidden);
            Assert.DoesNotContain("IV_INTERIOR_PROFILES", hidden);
        }

        [Fact]
        public void AcadVersion_AndFlags()
        {
            string s = DxfFormatString.Build(new FlatPatternExportSettings { AcadVersion = "R12", MergeProfilesIntoPolyline = true, RebaseGeometry = true });
            Assert.Contains("AcadVersion=R12", s);
            Assert.Contains("MergeProfilesIntoPolyline=True", s);
            Assert.Contains("RebaseGeometry=True", s);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using InvAuto.Core.Preview;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class DxfReaderTests
    {
        private static FlatDrawing Read(params string[] entities)
        {
            var sb = new StringBuilder();
            sb.Append("0\nSECTION\n2\nHEADER\n0\nENDSEC\n0\nSECTION\n2\nENTITIES\n");
            foreach (var e in entities)
                sb.Append(e);
            sb.Append("0\nENDSEC\n0\nEOF\n");
            return DxfReader.Read(new StringReader(sb.ToString()));
        }

        private const string Rectangle =
            "0\nLWPOLYLINE\n8\nIV_OUTER_PROFILE\n90\n4\n70\n1\n10\n0\n20\n0\n10\n100\n20\n0\n10\n100\n20\n50\n10\n0\n20\n50\n";

        [Fact]
        public void ClosedLwPolyline_GivesBounds()
        {
            var d = Read(Rectangle);

            Assert.Single(d.Polylines);
            Assert.True(d.Polylines[0].Closed);
            Assert.Equal(LayerKind.Profile, d.Polylines[0].Kind);
            Assert.Equal(100, d.Width, 6);
            Assert.Equal(50, d.Height, 6);
        }

        [Fact]
        public void Circle_IsTessellatedOnRadius()
        {
            var d = Read("0\nCIRCLE\n8\nIV_INTERIOR_PROFILES\n10\n10\n20\n20\n40\n5\n");

            Assert.True(d.Polylines[0].Points.All(p => Math.Abs(Math.Sqrt((p.X - 10) * (p.X - 10) + (p.Y - 20) * (p.Y - 20)) - 5) < 1e-9));
            Assert.Equal(10, d.Width, 2);
        }

        [Fact]
        public void Arc_GoesCounterClockwiseAcrossZero()
        {
            var d = Read("0\nARC\n8\n0\n10\n0\n20\n0\n40\n1\n50\n270\n51\n90\n");
            var points = d.Polylines[0].Points;

            Assert.Equal(0, points[0].X, 6);
            Assert.Equal(-1, points[0].Y, 6);
            Assert.Equal(1, d.MaxX, 6);     // проходит через (1; 0)
            Assert.True(d.MinX > -1e-6);    // и не уходит влево
        }

        [Fact]
        public void BulgeOne_IsSemicircleToTheRight()
        {
            var arc = DxfReader.BulgeArc(new Point2(0, 0), new Point2(2, 0), 1);

            Assert.Equal(-1, arc.Min(p => p.Y), 6);
            Assert.True(arc.Max(p => p.Y) < 1e-9);
            Assert.Equal(2, arc.Last().X, 9);
        }

        [Fact]
        public void BendLayer_IsClassified()
        {
            var d = Read("0\nLINE\n8\nIV_BEND_DOWN\n10\n0\n20\n0\n11\n0\n21\n10\n");
            Assert.Equal(LayerKind.Bend, d.Polylines[0].Kind);
        }

        [Fact]
        public void Insert_AppliesBlockBaseScaleRotationAndLayer()
        {
            string dxf =
                "0\nSECTION\n2\nBLOCKS\n" +
                "0\nBLOCK\n8\n0\n2\nB1\n10\n1\n20\n0\n" +
                "0\nLINE\n8\n0\n10\n1\n20\n0\n11\n2\n21\n0\n" +
                "0\nENDBLK\n0\nENDSEC\n" +
                "0\nSECTION\n2\nENTITIES\n" +
                "0\nINSERT\n8\nIV_OUTER_PROFILE\n2\nB1\n10\n10\n20\n10\n41\n2\n42\n2\n50\n90\n" +
                "0\nENDSEC\n0\nEOF\n";

            var d = DxfReader.Read(new StringReader(dxf));
            var line = d.Polylines.Single();

            // Отрезок (0;0)-(1;0) относительно базы, x2 и поворот на 90° → (10;10)-(10;12).
            Assert.Equal("IV_OUTER_PROFILE", line.Layer);
            Assert.Equal(10, line.Points[0].X, 6);
            Assert.Equal(10, line.Points[0].Y, 6);
            Assert.Equal(10, line.Points[1].X, 6);
            Assert.Equal(12, line.Points[1].Y, 6);
        }

        [Fact]
        public void OldStylePolyline_WithVertices()
        {
            var d = Read("0\nPOLYLINE\n8\nA\n66\n1\n70\n1\n" +
                         "0\nVERTEX\n8\nA\n10\n0\n20\n0\n" +
                         "0\nVERTEX\n8\nA\n10\n5\n20\n0\n" +
                         "0\nVERTEX\n8\nA\n10\n5\n20\n5\n" +
                         "0\nSEQEND\n8\nA\n");

            Assert.Equal(3, d.Polylines.Single().Points.Count);
            Assert.True(d.Polylines[0].Closed);
        }

        [Fact]
        public void Garbage_GivesEmptyDrawing()
        {
            Assert.True(DxfReader.Read(new StringReader("hello\nworld\n")).IsEmpty);
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Text;
using InvAuto.Core.Measuring;
using InvAuto.Core.Preview;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>Измерения в окне просмотра: точная геометрия из DXF, привязки, расстояния, углы.</summary>
    public class MeasuringTests
    {
        private static FlatDrawing Read(params string[] entities)
        {
            var sb = new StringBuilder("0\nSECTION\n2\nENTITIES\n");
            foreach (var e in entities)
                sb.Append(e);
            sb.Append("0\nENDSEC\n0\nEOF\n");
            return DxfReader.Read(new StringReader(sb.ToString()));
        }

        private static string Line(double x1, double y1, double x2, double y2) =>
            $"0\nLINE\n8\nIV_OUTER_PROFILE\n10\n{x1}\n20\n{y1}\n11\n{x2}\n21\n{y2}\n";

        private static string Circle(double x, double y, double r) =>
            $"0\nCIRCLE\n8\nIV_INTERIOR_PROFILES\n10\n{x}\n20\n{y}\n40\n{r}\n";

        /// <summary>Пластина 100×50, правый верхний угол скруглён R10 (bulge), отверстие Ø10 в (20; 25).</summary>
        private static FlatDrawing Plate() => Read(
            "0\nLWPOLYLINE\n8\nIV_OUTER_PROFILE\n90\n5\n70\n1\n" +
            "10\n0\n20\n0\n10\n100\n20\n0\n10\n100\n20\n40\n42\n0.41421356237\n10\n90\n20\n50\n10\n0\n20\n50\n",
            Circle(20, 25, 5));

        private static void Near(Point2 expected, Point2 actual, double eps = 1e-6)
        {
            Assert.Equal(expected.X, actual.X, eps);
            Assert.Equal(expected.Y, actual.Y, eps);
        }

        private static SegmentPick Pick(FlatSnapper snapper, double x, double y) => snapper.PickLine(new Point2(x, y), 1);

        #region Геометрия

        [Fact]
        public void Reader_KeepsExactLinesAndArcs()
        {
            var g = Plate().Geometry;

            Assert.Equal(4, g.Lines.Count); // скругление — дуга, а не 18 коротких участков
            Assert.Equal(2, g.Arcs.Count);
            var fillet = g.Arcs.Single(a => !a.IsCircle);
            Near(new Point2(90, 40), fillet.Center);
            Assert.Equal(10, fillet.Radius, 6);
            Near(new Point2(100, 40), fillet.Start);
            Near(new Point2(90, 50), fillet.End);
            var hole = g.Arcs.Single(a => a.IsCircle);
            Near(new Point2(20, 25), hole.Center);
            Assert.Equal(5, hole.Radius, 9);
        }

        [Fact]
        public void Reader_TransformsArcsInMirroredRotatedBlocks()
        {
            var d = Read(
                "0\nINSERT\n8\n0\n2\nB\n10\n100\n20\n0\n41\n-1\n42\n1\n50\n90\n");
            Assert.Empty(d.Geometry.Arcs); // блока нет — ничего

            string dxf = "0\nSECTION\n2\nBLOCKS\n0\nBLOCK\n2\nB\n10\n0\n20\n0\n" +
                         "0\nARC\n8\n0\n10\n10\n20\n0\n40\n5\n50\n0\n51\n90\n0\nENDBLK\n0\nENDSEC\n" +
                         "0\nSECTION\n2\nENTITIES\n0\nINSERT\n8\nIV_OUTER_PROFILE\n2\nB\n10\n100\n20\n0\n41\n-1\n42\n1\n50\n90\n0\nENDSEC\n0\nEOF\n";
            var arc = DxfReader.Read(new StringReader(dxf)).Geometry.Arcs.Single();

            // (10;0) → зеркально (−10;0) → поворот на 90° (0;−10) → перенос (100;−10).
            Near(new Point2(100, -10), arc.Center);
            Assert.Equal(5, arc.Radius, 9);
            Assert.True(arc.Sweep < 0); // зеркало меняет направление обхода
            Near(new Point2(100, -15), arc.Start); // (15;0) → (−15;0) → (0;−15) → (100;−15)
            Near(new Point2(95, -10), arc.End);    // (10;5) → (−10;5) → (−5;−10) → (95;−10)
            Assert.Equal("IV_OUTER_PROFILE", arc.Layer); // слой «0» в блоке — слой вставки
        }

        [Fact]
        public void Geometry_FromPolylines_ForDrawingsWithoutDxf()
        {
            var d = new FlatDrawing(new[]
            {
                new Polyline2("P", new[] { new Point2(0, 0), new Point2(10, 0), new Point2(10, 10) }, closed: true)
            });
            Assert.Equal(3, d.Geometry.Lines.Count);
            Assert.Empty(d.Geometry.Arcs);
        }

        [Fact]
        public void Arc_NearestPoint_StaysOnArc()
        {
            var arc = new Arc2(new Point2(0, 0), 10, 0, Math.PI / 2, "L");
            Near(new Point2(10 / Math.Sqrt(2), 10 / Math.Sqrt(2)), arc.Nearest(new Point2(20, 20)));
            Near(new Point2(10, 0), arc.Nearest(new Point2(20, -1)));  // за пределами дуги — ближайший конец
            Near(new Point2(0, 10), arc.Nearest(new Point2(-5, 20)));
        }

        #endregion

        #region Привязки

        [Fact]
        public void Snap_PrefersPointsWithinTolerance()
        {
            var snapper = new FlatSnapper(Plate());

            var end = snapper.Snap(new Point2(0.4, -0.3), 1);
            Assert.Equal(SnapKind.Endpoint, end.Kind);
            Near(new Point2(0, 0), end.Point);

            var mid = snapper.Snap(new Point2(50.5, 0.5), 1);
            Assert.Equal(SnapKind.Midpoint, mid.Kind);
            Near(new Point2(50, 0), mid.Point);

            var center = snapper.Snap(new Point2(20.3, 25.2), 1);
            Assert.Equal(SnapKind.Center, center.Kind);
            Near(new Point2(20, 25), center.Point);

            var filletEnd = snapper.Snap(new Point2(99.6, 40.4), 1);
            Assert.Equal(SnapKind.Endpoint, filletEnd.Kind);
            Near(new Point2(100, 40), filletEnd.Point);
        }

        [Fact]
        public void Snap_FallsBackToNearestOnLine_ThenToFreePoint()
        {
            var snapper = new FlatSnapper(Plate());

            var onLine = snapper.Snap(new Point2(30, 0.6), 1);
            Assert.Equal(SnapKind.Nearest, onLine.Kind);
            Near(new Point2(30, 0), onLine.Point);

            var onHole = snapper.Snap(new Point2(25.5, 25), 1); // окружность — без лишних «вершин» разбиения
            Assert.Equal(SnapKind.Nearest, onHole.Kind);
            Near(new Point2(25, 25), onHole.Point);

            var free = snapper.Snap(new Point2(60, 30), 1);
            Assert.Equal(SnapKind.None, free.Kind);
            Near(new Point2(60, 30), free.Point);

            Assert.Equal(SnapKind.None, snapper.Snap(new Point2(0.1, 0.1), 0).Kind); // Ctrl — без привязки
        }

        [Fact]
        public void Snap_EndpointWinsOverMidpointAtSamePlace()
        {
            // Короткий отрезок, его середина совпадает с концом другого.
            var snapper = new FlatSnapper(Read(Line(0, 0, 10, 0), Line(5, 0, 5, 10)));
            var snap = snapper.Snap(new Point2(5, 0.2), 1);
            Assert.Equal(SnapKind.Endpoint, snap.Kind);
        }

        [Fact]
        public void PickLine_FindsNearestStraightSegment()
        {
            var snapper = new FlatSnapper(Plate());
            var pick = Pick(snapper, 40, 0.5);
            Assert.NotNull(pick);
            Near(new Point2(40, 0), pick.Point);
            Assert.Null(snapper.PickLine(new Point2(60, 30), 1));
        }

        #endregion

        #region Расстояние и угол

        [Fact]
        public void Distance_ReportsLengthAndProjections()
        {
            var m = new DistanceMeasurement(new SnapResult(new Point2(1, 1), SnapKind.Endpoint), new SnapResult(new Point2(4, 5), SnapKind.Center));
            Assert.Equal(5, m.Distance, 12);
            Assert.Equal(3, m.Dx, 12);
            Assert.Equal(4, m.Dy, 12);
            Assert.Contains("5", m.Label);
        }

        [Fact]
        public void Angle_AtCorner_IsInteriorAngle()
        {
            var snapper = new FlatSnapper(Read(Line(0, 0, 100, 0), Line(0, 0, 50, 50)));
            var m = AngleMeasurement.Between(Pick(snapper, 60, 0), Pick(snapper, 20, 20));

            Assert.False(m.IsParallel);
            Assert.Equal(45, m.Degrees, 9);
            Assert.Equal(135, m.Supplement, 9);
            Near(new Point2(0, 0), m.Vertex);
        }

        [Fact]
        public void Angle_SideFollowsPickedPart_ForCrossingLines()
        {
            // Крест: горизонталь через (0;0) и диагональ, пересекающиеся в середине.
            var snapper = new FlatSnapper(Read(Line(-50, 0, 50, 0), Line(-30, -30, 30, 30)));

            Assert.Equal(45, AngleMeasurement.Between(Pick(snapper, 40, 0), Pick(snapper, 20, 20)).Degrees, 9);
            Assert.Equal(135, AngleMeasurement.Between(Pick(snapper, -40, 0), Pick(snapper, 20, 20)).Degrees, 9);
        }

        [Fact]
        public void Angle_UsesExtensions_WhenSegmentsDoNotTouch()
        {
            var snapper = new FlatSnapper(Read(Line(10, 0, 100, 0), Line(0, 10, 0, 100)));
            var m = AngleMeasurement.Between(Pick(snapper, 50, 0), Pick(snapper, 0, 50));
            Assert.Equal(90, m.Degrees, 9);
            Near(new Point2(0, 0), m.Vertex);
        }

        [Fact]
        public void Angle_PickAtVertex_UsesFarEnd()
        {
            var snapper = new FlatSnapper(Read(Line(0, 0, 100, 0), Line(0, 0, 0, 100)));
            var m = AngleMeasurement.Between(new SegmentPick(0, snapper.Lines[0], new Point2(0, 0)), Pick(snapper, 0, 50));
            Assert.Equal(90, m.Degrees, 9);
        }

        [Fact]
        public void Angle_ParallelSegments_GiveDistance()
        {
            var snapper = new FlatSnapper(Read(Line(0, 0, 100, 0), Line(100, 12.5, 30, 12.5)));
            var m = AngleMeasurement.Between(Pick(snapper, 50, 0), Pick(snapper, 50, 12.5));

            Assert.True(m.IsParallel);
            Assert.Equal(0, m.Degrees);
            Assert.Equal(12.5, m.ParallelDistance, 9);
            Near(new Point2(50, 0), m.PerpendicularFrom);
            Near(new Point2(50, 12.5), m.PerpendicularTo);
        }

        #endregion

        #region Ход измерения

        [Fact]
        public void Session_Distance_TwoClicksMakeOneResult()
        {
            var session = new MeasureSession();
            session.SetDrawing(Plate());
            session.SetMode(MeasureMode.Distance);

            Assert.Null(session.Click(new Point2(0.2, 0.2), 1));
            Assert.NotNull(session.FirstPoint);
            Assert.Null(session.Click(new Point2(0.1, 0.1), 1)); // та же точка — не измерение

            session.Move(new Point2(20.2, 25), 1);
            Assert.IsType<DistanceMeasurement>(session.Preview);
            Assert.Equal(SnapKind.Center, session.Hover?.Kind);

            var m = Assert.IsType<DistanceMeasurement>(session.Click(new Point2(20.2, 25), 1));
            Assert.Equal(Math.Sqrt(20 * 20 + 25 * 25), m.Distance, 9);
            Assert.Equal(1, m.Number);
            Assert.Null(session.FirstPoint);
            Assert.Single(session.Results);
        }

        [Fact]
        public void Session_Angle_IgnoresMissesAndSameLine()
        {
            var session = new MeasureSession();
            session.SetDrawing(Read(Line(0, 0, 100, 0), Line(0, 0, 0, 100)));
            session.SetMode(MeasureMode.Angle);

            Assert.Null(session.Click(new Point2(50, 50), 1)); // мимо
            Assert.Null(session.FirstLine);
            Assert.Null(session.Click(new Point2(50, 0.3), 1));
            Assert.Null(session.Click(new Point2(60, 0.3), 1)); // тот же отрезок
            Assert.NotNull(session.FirstLine);

            var m = Assert.IsType<AngleMeasurement>(session.Click(new Point2(0.3, 50), 1));
            Assert.Equal(90, m.Degrees, 9);
        }

        [Fact]
        public void Session_CancelModeChangeAndNewDrawing()
        {
            var session = new MeasureSession();
            Assert.Null(session.Click(new Point2(0, 0), 1)); // развертки нет — ничего не происходит
            session.SetDrawing(Plate());
            session.SetMode(MeasureMode.Distance);

            session.Click(new Point2(0, 0), 1);
            Assert.True(session.Cancel());
            Assert.False(session.Cancel());

            session.Click(new Point2(0, 0), 1);
            session.Click(new Point2(100, 0), 1);
            session.Click(new Point2(0, 0), 1);
            session.SetMode(MeasureMode.Angle); // смена режима сбрасывает начатое, но не готовые
            Assert.Null(session.FirstPoint);
            Assert.Single(session.Results);
            Assert.Contains("Угол", session.Prompt);

            session.SetDrawing(Plate());
            Assert.Empty(session.Results);
            session.SetMode(MeasureMode.Distance);
            session.Click(new Point2(0, 0), 1);
            Assert.Equal(1, session.Click(new Point2(100, 0), 1).Number); // нумерация заново
        }

        #endregion
    }
}

using InvAuto.Core.Preview;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class ViewportTests
    {
        // Развертка 200 × 100 мм в окне 410 × 410 px с полем 5 px: вписанный масштаб 2 px/мм.
        private static Viewport Make()
        {
            var v = new Viewport(padding: 5);
            v.SetViewSize(410, 410);
            v.SetBounds(0, 0, 200, 100);
            return v;
        }

        [Fact]
        public void Fit_CentersDrawing_AndFlipsY()
        {
            var v = Make();
            Assert.Equal(2, v.Scale, 9);
            var center = v.ToScreen(new Point2(100, 50));
            Assert.Equal(205, center.X, 9);
            Assert.Equal(205, center.Y, 9);
            var topLeft = v.ToScreen(new Point2(0, 100)); // верх чертежа — наверху экрана
            Assert.Equal(5, topLeft.X, 9);
            Assert.Equal(105, topLeft.Y, 9);
        }

        [Fact]
        public void ScreenAndModel_AreInverse_AtAnyZoomAndPan()
        {
            var v = Make();
            v.ZoomAt(100, 300, 3.7);
            v.Pan(-42, 17);
            var model = new Point2(123.4, -56.7);
            var screen = v.ToScreen(model);
            var back = v.ToModel(screen.X, screen.Y);
            Assert.Equal(model.X, back.X, 9);
            Assert.Equal(model.Y, back.Y, 9);
        }

        [Fact]
        public void ZoomAt_KeepsPointUnderCursor()
        {
            var v = Make();
            var before = v.ToModel(320, 120);
            v.ZoomAt(320, 120, 1.25);
            v.ZoomAt(320, 120, 1.25);
            var after = v.ToModel(320, 120);
            Assert.Equal(before.X, after.X, 9);
            Assert.Equal(before.Y, after.Y, 9);
            Assert.Equal(1.5625, v.Zoom, 9);
        }

        [Fact]
        public void Pan_MovesDrawingWithMouse()
        {
            var v = Make();
            var p = new Point2(50, 50);
            var s1 = v.ToScreen(p);
            v.Pan(30, -20);
            var s2 = v.ToScreen(p);
            Assert.Equal(s1.X + 30, s2.X, 9);
            Assert.Equal(s1.Y - 20, s2.Y, 9);
        }

        [Fact]
        public void Zoom_IsClamped_AndFitResets()
        {
            var v = Make();
            for (int i = 0; i < 200; i++)
                v.ZoomAt(0, 0, 2);
            Assert.Equal(Viewport.MaxZoom, v.Zoom);
            for (int i = 0; i < 200; i++)
                v.ZoomAt(0, 0, 0.5);
            Assert.Equal(Viewport.MinZoom, v.Zoom);

            v.Fit();
            Assert.Equal(1, v.Zoom);
            Assert.Equal(205, v.ToScreen(new Point2(100, 50)).X, 9);
        }

        [Fact]
        public void Resize_KeepsZoomAndCenter()
        {
            var v = Make();
            v.ZoomAt(205, 205, 2);
            var center = v.Center;
            v.SetViewSize(810, 410);
            Assert.Equal(2, v.Zoom);
            Assert.Equal(center.X, v.Center.X, 9);
            Assert.Equal(405, v.ToScreen(center).X, 9);
        }

        [Fact]
        public void DegenerateDrawing_DoesNotBreak()
        {
            var v = new Viewport();
            v.SetViewSize(100, 100);
            v.SetBounds(10, 10, 10, 10); // точка
            var s = v.ToScreen(new Point2(10, 10));
            Assert.Equal(50, s.X, 9);
            Assert.False(double.IsNaN(v.Scale) || double.IsInfinity(v.Scale));

            v.SetViewSize(0, 0); // окно ещё не показано
            Assert.True(v.Scale > 0);
        }
    }
}

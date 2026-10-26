using System.Windows;
using System.Windows.Media;
using InvAuto.Core.Bends;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>Схема одной линии гиба: тонкая серая линия во всю длину и синие участки, которые прорежет лазер.</summary>
    public sealed class BendLineBar : FrameworkElement
    {
        public static readonly DependencyProperty LayoutProperty = DependencyProperty.Register(
            nameof(Layout), typeof(BendLineLayout), typeof(BendLineBar),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        private static readonly Pen BasePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xC5, 0xCB, 0xD1)), 1));
        private static readonly Brush CutBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x1C, 0x7C, 0xC4)));

        public BendLineLayout Layout
        {
            get => (BendLineLayout)GetValue(LayoutProperty);
            set => SetValue(LayoutProperty, value);
        }

        public BendLineBar()
        {
            Height = 14;
            SnapsToDevicePixels = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            var layout = Layout;
            double width = ActualWidth, middle = ActualHeight / 2;
            if (layout == null || layout.LineLength <= 0 || width < 2)
                return;

            dc.DrawLine(BasePen, new Point(0, middle + 0.5), new Point(width, middle + 0.5));
            double scale = width / layout.LineLength;
            double height = layout.SlotHalfWidth > 0 ? 9 : 5; // вырезы — заметно шире прорезей
            double corner = layout.RoundedEnds ? height / 2 : 0; // пазы со скруглёнными торцами
            foreach (var segment in layout.Segments)
            {
                double x = segment.Start * scale;
                double w = System.Math.Max(1, segment.Length * scale);
                double r = System.Math.Min(corner, w / 2);
                dc.DrawRoundedRectangle(CutBrush, null, new Rect(x, middle - height / 2 + 0.5, w, height), r, r);
            }

            // Законцовки — значок формы на каждом конце (с учётом смещения внутрь).
            if (layout.EndShape != InvAuto.Core.Bends.BendEndShape.None)
            {
                double offset = System.Math.Min(layout.EndOffset * scale, width / 2);
                foreach (double cx in new[] { offset, width - offset })
                    DrawEnd(dc, layout.EndShape, new Point(cx, middle + 0.5), 5.5);
            }
        }

        private static readonly Pen EndPen = Freeze(new Pen(Brushes.White, 1));

        private void DrawEnd(DrawingContext dc, InvAuto.Core.Bends.BendEndShape shape, Point c, double r)
        {
            switch (shape)
            {
                case InvAuto.Core.Bends.BendEndShape.Circle:
                    dc.DrawEllipse(CutBrush, EndPen, c, r, r);
                    break;
                case InvAuto.Core.Bends.BendEndShape.Square:
                    dc.DrawRectangle(CutBrush, EndPen, new Rect(c.X - r, c.Y - r, 2 * r, 2 * r));
                    break;
                default:
                    var g = new StreamGeometry();
                    using (var ctx = g.Open())
                    {
                        ctx.BeginFigure(new Point(c.X, c.Y - r * 1.3), true, true);
                        ctx.PolyLineTo(new[] { new Point(c.X + r * 1.3, c.Y), new Point(c.X, c.Y + r * 1.3), new Point(c.X - r * 1.3, c.Y) }, true, true);
                    }
                    g.Freeze();
                    dc.DrawGeometry(CutBrush, EndPen, g);
                    break;
            }
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}

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
            Height = 12;
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
            foreach (var segment in layout.Segments)
            {
                double x = segment.Start * scale;
                double w = System.Math.Max(1, segment.Length * scale);
                dc.DrawRectangle(CutBrush, null, new Rect(x, middle - 2, w, 5));
            }
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}

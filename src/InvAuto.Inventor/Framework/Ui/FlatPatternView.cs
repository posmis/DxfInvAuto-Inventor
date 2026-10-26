using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using InvAuto.Core.Preview;

namespace InvAuto.Framework.Ui
{
    /// <summary>Подпись на развертке (например, номер линии гиба) в координатах чертежа.</summary>
    public sealed class FlatLabel
    {
        public FlatLabel(Point2 position, string text)
        {
            Position = position;
            Text = text;
        }

        public Point2 Position { get; }
        public string Text { get; }
    }

    /// <summary>
    /// Рисует развертку линиями постоянной толщины 1 px при любом масштабе:
    /// контур — тёмным, линии гиба — акцентным цветом, вспомогательные — серым.
    /// </summary>
    public sealed class FlatPatternView : FrameworkElement
    {
        public static readonly DependencyProperty DrawingProperty = DependencyProperty.Register(
            nameof(Drawing), typeof(FlatDrawing), typeof(FlatPatternView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty ProfileBrushProperty = DependencyProperty.Register(
            nameof(ProfileBrush), typeof(Brush), typeof(FlatPatternView),
            new FrameworkPropertyMetadata(Brushes.Black, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty BendBrushProperty = DependencyProperty.Register(
            nameof(BendBrush), typeof(Brush), typeof(FlatPatternView),
            new FrameworkPropertyMetadata(Brushes.SteelBlue, FrameworkPropertyMetadataOptions.AffectsRender));

        public static readonly DependencyProperty AuxiliaryBrushProperty = DependencyProperty.Register(
            nameof(AuxiliaryBrush), typeof(Brush), typeof(FlatPatternView),
            new FrameworkPropertyMetadata(Brushes.Gray, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Подсвеченные линии (например, выбранные линии гиба) — широкая полупрозрачная полоса под чертежом.</summary>
        public static readonly DependencyProperty HighlightsProperty = DependencyProperty.Register(
            nameof(Highlights), typeof(IReadOnlyList<Polyline2>), typeof(FlatPatternView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        /// <summary>Подписи поверх чертежа.</summary>
        public static readonly DependencyProperty LabelsProperty = DependencyProperty.Register(
            nameof(Labels), typeof(IReadOnlyList<FlatLabel>), typeof(FlatPatternView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));

        public IReadOnlyList<Polyline2> Highlights
        {
            get => (IReadOnlyList<Polyline2>)GetValue(HighlightsProperty);
            set => SetValue(HighlightsProperty, value);
        }

        public IReadOnlyList<FlatLabel> Labels
        {
            get => (IReadOnlyList<FlatLabel>)GetValue(LabelsProperty);
            set => SetValue(LabelsProperty, value);
        }

        public FlatDrawing Drawing
        {
            get => (FlatDrawing)GetValue(DrawingProperty);
            set => SetValue(DrawingProperty, value);
        }

        public Brush ProfileBrush
        {
            get => (Brush)GetValue(ProfileBrushProperty);
            set => SetValue(ProfileBrushProperty, value);
        }

        public Brush BendBrush
        {
            get => (Brush)GetValue(BendBrushProperty);
            set => SetValue(BendBrushProperty, value);
        }

        public Brush AuxiliaryBrush
        {
            get => (Brush)GetValue(AuxiliaryBrushProperty);
            set => SetValue(AuxiliaryBrushProperty, value);
        }

        public FlatPatternView()
        {
            SnapsToDevicePixels = true;
        }

        protected override void OnRender(DrawingContext dc)
        {
            // Прозрачный фон — чтобы элемент ловил мышь по всей площади (подсказки, клики).
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

            var drawing = Drawing;
            if (drawing == null || drawing.IsEmpty || ActualWidth < 4 || ActualHeight < 4)
                return;

            const double padding = 3;
            double width = Math.Max(drawing.Width, 1e-9);
            double height = Math.Max(drawing.Height, 1e-9);
            double scale = Math.Min((ActualWidth - 2 * padding) / width, (ActualHeight - 2 * padding) / height);
            double offsetX = (ActualWidth - drawing.Width * scale) / 2;
            double offsetY = (ActualHeight - drawing.Height * scale) / 2;

            // В DXF ось Y направлена вверх, на экране — вниз.
            Point Map(Point2 p) => new Point(
                offsetX + (p.X - drawing.MinX) * scale,
                offsetY + (drawing.MaxY - p.Y) * scale);

            var highlights = Highlights;
            if (highlights != null && highlights.Count > 0)
                DrawKind(dc, highlights, Map, HighlightPen);

            DrawKind(dc, drawing.Polylines.Where(p => p.Kind == LayerKind.Auxiliary), Map, Pen(AuxiliaryBrush, null));
            // Линии гиба — сплошные: в превью уже реальные участки, которые прорежет лазер.
            DrawKind(dc, drawing.Polylines.Where(p => p.Kind == LayerKind.Bend), Map, Pen(BendBrush, null, 1.5));
            DrawKind(dc, drawing.Polylines.Where(p => p.Kind == LayerKind.Profile), Map, Pen(ProfileBrush, null));

            var labels = Labels;
            if (labels != null)
            {
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                foreach (var label in labels)
                    DrawLabel(dc, Map(label.Position), label.Text, dpi);
            }
        }

        private static readonly Pen HighlightPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xF0, 0x9A, 0x1A)), 7)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        });

        private static readonly Brush LabelBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xFF, 0xFF, 0xFF)));
        private static readonly Pen LabelPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0x2E, 0x39, 0x43)), 1));
        private static readonly Typeface LabelFont = new Typeface("Segoe UI");

        /// <summary>Номер в белом кружке — читается поверх любых линий.</summary>
        private static void DrawLabel(DrawingContext dc, Point at, string text, double dpi)
        {
            var formatted = new FormattedText(text, System.Globalization.CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                LabelFont, 10, LabelPen.Brush, dpi);
            double radius = Math.Max(formatted.Width, formatted.Height) / 2 + 3;
            dc.DrawEllipse(LabelBrush, LabelPen, at, radius, radius);
            dc.DrawText(formatted, new Point(at.X - formatted.Width / 2, at.Y - formatted.Height / 2));
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        private static Pen Pen(Brush brush, DashStyle dash, double thickness = 1)
        {
            var pen = new Pen(brush, thickness) { LineJoin = PenLineJoin.Round };
            if (dash != null)
                pen.DashStyle = dash;
            pen.Freeze();
            return pen;
        }

        private static void DrawKind(DrawingContext dc, IEnumerable<Polyline2> polylines, Func<Point2, Point> map, Pen pen)
        {
            var geometry = new StreamGeometry();
            using (var ctx = geometry.Open())
            {
                foreach (var polyline in polylines)
                {
                    ctx.BeginFigure(map(polyline.Points[0]), false, polyline.Closed);
                    ctx.PolyLineTo(polyline.Points.Skip(1).Select(map).ToList(), true, true);
                }
            }
            geometry.Freeze();
            dc.DrawGeometry(null, pen, geometry);
        }
    }
}

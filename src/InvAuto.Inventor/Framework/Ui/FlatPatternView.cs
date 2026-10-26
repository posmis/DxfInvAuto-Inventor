using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using InvAuto.Core.Preview;

namespace InvAuto.Framework.Ui
{
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

            DrawKind(dc, drawing.Polylines.Where(p => p.Kind == LayerKind.Auxiliary), Map, Pen(AuxiliaryBrush, null));
            // Линии гиба — сплошные: в превью уже реальные участки, которые прорежет лазер.
            DrawKind(dc, drawing.Polylines.Where(p => p.Kind == LayerKind.Bend), Map, Pen(BendBrush, null, 1.5));
            DrawKind(dc, drawing.Polylines.Where(p => p.Kind == LayerKind.Profile), Map, Pen(ProfileBrush, null));
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

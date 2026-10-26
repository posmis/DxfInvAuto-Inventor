using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
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

    /// <summary>Слой поверх развертки (например, измерения). map переводит мм в пиксели элемента.</summary>
    public interface IFlatOverlay
    {
        void Render(DrawingContext dc, Func<Point2, Point> map, double pixelsPerMm);
    }

    /// <summary>
    /// Рисует развертку линиями постоянной толщины 1 px при любом масштабе:
    /// контур — тёмным, линии гиба — акцентным цветом, вспомогательные — серым.
    /// </summary>
    public sealed class FlatPatternView : FrameworkElement
    {
        public static readonly DependencyProperty DrawingProperty = DependencyProperty.Register(
            nameof(Drawing), typeof(FlatDrawing), typeof(FlatPatternView),
            new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender, OnDrawingChanged));

        /// <summary>
        /// Интерактивный режим (окно просмотра): колесо — масштаб вокруг курсора, левая/средняя кнопка —
        /// перемещение, двойной щелчок — вписать целиком.
        /// </summary>
        public static readonly DependencyProperty InteractiveProperty = DependencyProperty.Register(
            nameof(Interactive), typeof(bool), typeof(FlatPatternView), new PropertyMetadata(false));

        public bool Interactive
        {
            get => (bool)GetValue(InteractiveProperty);
            set => SetValue(InteractiveProperty, value);
        }

        /// <summary>Сдвиг мыши, после которого нажатие левой кнопки считается перетаскиванием, а не щелчком, px.</summary>
        private const double DragThreshold = 4;

        private readonly Viewport _viewport = new Viewport();
        private Point? _dragFrom;
        private Point? _pressedAt;
        private Cursor _cursorBeforeDrag;
        private IFlatOverlay _overlay;

        /// <summary>Щелчок левой кнопкой без перетаскивания: точка в мм.</summary>
        public event EventHandler<Point2> ModelClicked;

        /// <summary>Двойной щелчок вписывает чертёж (в режиме измерений выключается).</summary>
        public bool DoubleClickFits { get; set; } = true;

        /// <summary>Слой поверх развертки; после изменения его данных вызовите InvalidateVisual.</summary>
        public IFlatOverlay Overlay
        {
            get => _overlay;
            set
            {
                _overlay = value;
                InvalidateVisual();
            }
        }

        /// <summary>Точка элемента (px) → координаты чертежа (мм).</summary>
        public Point2 ToModel(Point screen)
        {
            _viewport.SetViewSize(ActualWidth, ActualHeight);
            return _viewport.ToModel(screen.X, screen.Y);
        }

        /// <summary>Масштаб или положение изменились (для подписи масштаба в окне).</summary>
        public event EventHandler ViewChanged;

        /// <summary>Курсор над чертежом: координаты в мм (null — курсор ушёл).</summary>
        public event EventHandler<Point2?> CursorMoved;

        /// <summary>Масштаб относительно «вписать целиком».</summary>
        public double Zoom => _viewport.Zoom;

        /// <summary>Пикселей на миллиметр.</summary>
        public double PixelsPerMm => _viewport.Scale;

        /// <summary>Чертёж целиком в окне.</summary>
        public void Fit()
        {
            _viewport.Fit();
            Changed();
        }

        /// <summary>Масштаб вокруг центра окна (кнопки «+» и «−»).</summary>
        public void ZoomBy(double factor)
        {
            _viewport.SetViewSize(ActualWidth, ActualHeight);
            _viewport.ZoomAt(ActualWidth / 2, ActualHeight / 2, factor);
            Changed();
        }

        private void Changed()
        {
            InvalidateVisual();
            ViewChanged?.Invoke(this, EventArgs.Empty);
        }

        private static void OnDrawingChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        {
            var view = (FlatPatternView)d;
            if (e.NewValue is FlatDrawing drawing && !drawing.IsEmpty)
            {
                // Тот же чертёж после обработки линий гиба — оставляем текущий масштаб и положение.
                bool sameBounds = e.OldValue is FlatDrawing old && !old.IsEmpty &&
                                  Math.Abs(old.MinX - drawing.MinX) < 1e-6 && Math.Abs(old.MaxX - drawing.MaxX) < 1e-6 &&
                                  Math.Abs(old.MinY - drawing.MinY) < 1e-6 && Math.Abs(old.MaxY - drawing.MaxY) < 1e-6;
                if (!sameBounds || !view.Interactive)
                    view._viewport.SetBounds(drawing.MinX, drawing.MinY, drawing.MaxX, drawing.MaxY);
            }
            view.ViewChanged?.Invoke(view, EventArgs.Empty);
        }

        #region Мышь

        protected override void OnMouseWheel(MouseWheelEventArgs e)
        {
            base.OnMouseWheel(e);
            if (!Interactive || Drawing == null)
                return;
            var p = e.GetPosition(this);
            _viewport.SetViewSize(ActualWidth, ActualHeight);
            _viewport.ZoomAt(p.X, p.Y, Math.Pow(1.0015, e.Delta)); // один щелчок колеса (120) — около ×1,2
            Changed();
            e.Handled = true;
        }

        protected override void OnMouseDown(MouseButtonEventArgs e)
        {
            base.OnMouseDown(e);
            if (!Interactive || (e.ChangedButton != MouseButton.Left && e.ChangedButton != MouseButton.Middle))
                return;
            if (e.ChangedButton == MouseButton.Left && e.ClickCount == 2 && DoubleClickFits)
            {
                Fit();
                e.Handled = true;
                return;
            }

            // Средняя кнопка — сразу перемещение; левая — щелчок, пока мышь не сдвинулась дальше порога.
            CaptureMouse();
            if (e.ChangedButton == MouseButton.Middle)
                BeginDrag(e.GetPosition(this));
            else
                _pressedAt = e.GetPosition(this);
            e.Handled = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!Interactive)
                return;
            var p = e.GetPosition(this);
            if (_pressedAt is Point pressed && (p - pressed).Length > DragThreshold)
            {
                _pressedAt = null;
                BeginDrag(pressed);
            }
            if (_dragFrom is Point from)
            {
                _viewport.Pan(p.X - from.X, p.Y - from.Y);
                _dragFrom = p;
                Changed();
            }
            CursorMoved?.Invoke(this, Drawing == null ? (Point2?)null : _viewport.ToModel(p.X, p.Y));
        }

        protected override void OnMouseUp(MouseButtonEventArgs e)
        {
            base.OnMouseUp(e);
            if (_pressedAt is Point pressed && e.ChangedButton == MouseButton.Left)
            {
                _pressedAt = null;
                ReleaseMouseCapture();
                if (Drawing != null)
                    ModelClicked?.Invoke(this, ToModel(pressed));
                return;
            }
            if (_dragFrom == null)
                return;
            _dragFrom = null;
            ReleaseMouseCapture();
            Cursor = _cursorBeforeDrag;
        }

        protected override void OnLostMouseCapture(MouseEventArgs e)
        {
            base.OnLostMouseCapture(e);
            _pressedAt = null;
            if (_dragFrom != null)
            {
                _dragFrom = null;
                Cursor = _cursorBeforeDrag;
            }
        }

        private void BeginDrag(Point from)
        {
            _dragFrom = from;
            _cursorBeforeDrag = Cursor; // в окне просмотра — перекрестие, его и вернём
            Cursor = Cursors.SizeAll;
        }

        protected override void OnMouseLeave(MouseEventArgs e)
        {
            base.OnMouseLeave(e);
            if (Interactive && _dragFrom == null)
                CursorMoved?.Invoke(this, null);
        }

        #endregion

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
            ClipToBounds = true; // при увеличении чертёж не вылезает за свою область
        }

        protected override void OnRender(DrawingContext dc)
        {
            // Прозрачный фон — чтобы элемент ловил мышь по всей площади (подсказки, клики).
            dc.DrawRectangle(Brushes.Transparent, null, new Rect(RenderSize));

            var drawing = Drawing;
            if (drawing == null || drawing.IsEmpty || ActualWidth < 4 || ActualHeight < 4)
                return;

            // Пересчёт мм → пиксели (ось Y в DXF вверх, на экране — вниз). Без интерактива — всегда «вписать».
            _viewport.SetViewSize(ActualWidth, ActualHeight);
            Point Map(Point2 p)
            {
                var screen = _viewport.ToScreen(p);
                return new Point(screen.X, screen.Y);
            }

            var highlights = Highlights;
            if (highlights != null && highlights.Count > 0)
                DrawKind(dc, highlights, Map, HighlightPen);

            DrawKind(dc, drawing.Polylines.Where(p => p.Kind == LayerKind.Auxiliary), Map, Pen(AuxiliaryBrush, null));
            // Линии гиба — сплошные: в превью уже реальные участки, которые прорежет лазер.
            DrawKind(dc, drawing.Polylines.Where(p => p.Kind == LayerKind.Bend), Map, Pen(BendBrush, null, 1.5));
            DrawKind(dc, drawing.Polylines.Where(p => p.Kind == LayerKind.Profile), Map, Pen(ProfileBrush, null));
            // Законцовки линий гиба — красным поверх контура.
            DrawKind(dc, drawing.Polylines.Where(p => p.Kind == LayerKind.EndFeature), Map, EndFeaturePen);

            var labels = Labels;
            if (labels != null)
            {
                double dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
                foreach (var label in labels)
                    DrawLabel(dc, Map(label.Position), label.Text, dpi);
            }

            _overlay?.Render(dc, Map, _viewport.Scale);
        }

        private static readonly Pen HighlightPen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x90, 0xF0, 0x9A, 0x1A)), 7)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        });

        private static readonly Pen EndFeaturePen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)), 1.6) { LineJoin = PenLineJoin.Round });

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

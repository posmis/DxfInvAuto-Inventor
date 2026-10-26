using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using InvAuto.Core.Measuring;
using InvAuto.Core.Preflight;
using InvAuto.Core.Preview;
using InvAuto.Framework.Ui;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>
    /// Рисует измерения поверх развертки: готовые — сплошными линиями с подписью, предпросмотр — пунктиром,
    /// привязку под курсором — маркером (квадрат — конец, треугольник — середина, круг — центр,
    /// «песочные часы» — точка на линии). Размеры маркеров и подписей не зависят от масштаба.
    /// </summary>
    public sealed class MeasureOverlay : IFlatOverlay
    {
        private const double AngleArcRadius = 34;   // px
        private const double MarkerSize = 5;        // половина стороны маркера, px

        private static readonly Brush MeasureBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD9, 0x48, 0x0F)));
        private static readonly Brush SnapBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x8A, 0x3E)));
        private static readonly Brush LabelBackground = Freeze(new SolidColorBrush(Color.FromArgb(0xF0, 0xFF, 0xFF, 0xFF)));
        private static readonly Pen MeasurePen = Freeze(new Pen(MeasureBrush, 1.5));
        private static readonly Pen PreviewPen = Freeze(new Pen(MeasureBrush, 1.2) { DashStyle = new DashStyle(new[] { 4.0, 3.0 }, 0) });
        private static readonly Pen ExtensionPen = Freeze(new Pen(MeasureBrush, 1) { DashStyle = new DashStyle(new[] { 2.0, 3.0 }, 0) });
        private static readonly Pen PickedLinePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x70, 0xD9, 0x48, 0x0F)), 6)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        });
        private static readonly Pen HoverLinePen = Freeze(new Pen(new SolidColorBrush(Color.FromArgb(0x80, 0x2B, 0x8A, 0x3E)), 5)
        {
            StartLineCap = PenLineCap.Round,
            EndLineCap = PenLineCap.Round
        });
        private static readonly Pen SnapPen = Freeze(new Pen(SnapBrush, 1.6));
        private static readonly Typeface LabelFont = new Typeface("Segoe UI");

        private readonly MeasureSession _session;
        private readonly Visual _host;

        public MeasureOverlay(MeasureSession session, Visual host)
        {
            _session = session;
            _host = host;
        }

        /// <summary>Замечания проверки текущей детали: места отмечаются кружками (оранжевый — внимание, красный — ошибка).</summary>
        public IReadOnlyList<PreflightIssue> Issues { get; set; }

        private static readonly Pen WarningPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xE0, 0x8A, 0x00)), 2));
        private static readonly Pen ErrorPen = Freeze(new Pen(new SolidColorBrush(Color.FromRgb(0xC6, 0x28, 0x28)), 2));
        private static readonly Brush IssueFill = Freeze(new SolidColorBrush(Color.FromArgb(0x30, 0xE0, 0x8A, 0x00)));

        private void DrawIssues(DrawingContext dc, Func<Point2, Point> map)
        {
            if (Issues == null)
                return;
            foreach (var issue in Issues)
            {
                if (issue.Severity == IssueSeverity.Info)
                    continue;
                var pen = issue.Severity == IssueSeverity.Error ? ErrorPen : WarningPen;
                foreach (var location in issue.Locations)
                    dc.DrawEllipse(IssueFill, pen, map(location), 11, 11);
            }
        }

        public void Render(DrawingContext dc, Func<Point2, Point> map, double pixelsPerMm)
        {
            double dpi = VisualTreeHelper.GetDpi(_host).PixelsPerDip;
            var ctx = new RenderContext(dc, map, pixelsPerMm, dpi);

            DrawIssues(dc, map);

            foreach (var measurement in _session.Results)
                Draw(ctx, measurement, preview: false);

            if (_session.FirstLine != null)
                dc.DrawLine(PickedLinePen, map(_session.FirstLine.Segment.A), map(_session.FirstLine.Segment.B));
            if (_session.HoverLine != null)
                dc.DrawLine(HoverLinePen, map(_session.HoverLine.Segment.A), map(_session.HoverLine.Segment.B));
            if (_session.FirstPoint is SnapResult first)
                dc.DrawEllipse(MeasureBrush, null, map(first.Point), 3, 3);
            if (_session.Preview != null)
                Draw(ctx, _session.Preview, preview: true);
            if (_session.Hover is SnapResult hover)
                DrawMarker(dc, map(hover.Point), hover.Kind);
        }

        #region Измерения

        private static void Draw(RenderContext ctx, Measurement measurement, bool preview)
        {
            switch (measurement)
            {
                case DistanceMeasurement distance:
                    DrawDistance(ctx, distance, preview);
                    break;
                case AngleMeasurement angle:
                    DrawAngle(ctx, angle, preview);
                    break;
            }
        }

        private static void DrawDistance(RenderContext ctx, DistanceMeasurement m, bool preview)
        {
            Point a = ctx.Map(m.From.Point), b = ctx.Map(m.To.Point);
            ctx.Dc.DrawLine(preview ? PreviewPen : MeasurePen, a, b);
            ctx.Dc.DrawEllipse(MeasureBrush, null, a, 2.5, 2.5);
            ctx.Dc.DrawEllipse(MeasureBrush, null, b, 2.5, 2.5);

            // Подпись — над серединой, со сдвигом перпендикулярно линии, чтобы не закрывать её.
            var mid = new Point((a.X + b.X) / 2, (a.Y + b.Y) / 2);
            var along = b - a;
            var normal = along.Length > 1e-9 ? new Vector(along.Y, -along.X) / along.Length : new Vector(0, -1);
            if (normal.Y > 0)
                normal = -normal;
            DrawLabel(ctx, mid + normal * 14, Caption(m, preview));
        }

        private static void DrawAngle(RenderContext ctx, AngleMeasurement m, bool preview)
        {
            ctx.Dc.DrawLine(PickedLinePen, ctx.Map(m.First.A), ctx.Map(m.First.B));
            ctx.Dc.DrawLine(PickedLinePen, ctx.Map(m.Second.A), ctx.Map(m.Second.B));
            var pen = preview ? PreviewPen : MeasurePen;

            if (m.IsParallel)
            {
                Point from = ctx.Map(m.PerpendicularFrom), to = ctx.Map(m.PerpendicularTo);
                ctx.Dc.DrawLine(pen, from, to);
                ctx.Dc.DrawEllipse(MeasureBrush, null, from, 2.5, 2.5);
                ctx.Dc.DrawEllipse(MeasureBrush, null, to, 2.5, 2.5);
                DrawLabel(ctx, new Point((from.X + to.X) / 2 + 8, (from.Y + to.Y) / 2), Caption(m, preview), alignLeft: true);
                return;
            }

            // Продолжения отрезков до вершины, если они её не касаются.
            var vertex = ctx.Map(m.Vertex);
            DrawExtension(ctx, m.First, m.Vertex, vertex);
            DrawExtension(ctx, m.Second, m.Vertex, vertex);

            // Дуга угла от первой стороны ко второй по кратчайшему пути.
            double r = AngleArcRadius / ctx.PixelsPerMm;
            double a1 = Math.Atan2(m.Direction1.Y, m.Direction1.X);
            double sweep = Math.Atan2(m.Direction2.Y, m.Direction2.X) - a1;
            while (sweep > Math.PI) sweep -= 2 * Math.PI;
            while (sweep < -Math.PI) sweep += 2 * Math.PI;

            var arc = new StreamGeometry();
            using (var g = arc.Open())
            {
                int steps = Math.Max(4, (int)Math.Ceiling(Math.Abs(sweep) / (Math.PI / 60)));
                g.BeginFigure(ctx.Map(new Point2(m.Vertex.X + r * Math.Cos(a1), m.Vertex.Y + r * Math.Sin(a1))), false, false);
                var points = new List<Point>(steps);
                for (int k = 1; k <= steps; k++)
                {
                    double a = a1 + sweep * k / steps;
                    points.Add(ctx.Map(new Point2(m.Vertex.X + r * Math.Cos(a), m.Vertex.Y + r * Math.Sin(a))));
                }
                g.PolyLineTo(points, true, true);
            }
            arc.Freeze();
            ctx.Dc.DrawGeometry(null, pen, arc);
            ctx.Dc.DrawLine(pen, vertex, ctx.Map(new Point2(m.Vertex.X + r * 1.25 * m.Direction1.X, m.Vertex.Y + r * 1.25 * m.Direction1.Y)));
            ctx.Dc.DrawLine(pen, vertex, ctx.Map(new Point2(m.Vertex.X + r * 1.25 * m.Direction2.X, m.Vertex.Y + r * 1.25 * m.Direction2.Y)));

            double bisector = a1 + sweep / 2;
            double labelR = (AngleArcRadius + 16) / ctx.PixelsPerMm;
            DrawLabel(ctx, ctx.Map(new Point2(m.Vertex.X + labelR * Math.Cos(bisector), m.Vertex.Y + labelR * Math.Sin(bisector))),
                Caption(m, preview));
        }

        private static void DrawExtension(RenderContext ctx, Segment2 segment, Point2 vertex, Point vertexOnScreen)
        {
            var nearest = segment.Nearest(vertex);
            if (Geometry2.Distance(nearest, vertex) * ctx.PixelsPerMm > 1)
                ctx.Dc.DrawLine(ExtensionPen, ctx.Map(nearest), vertexOnScreen);
        }

        private static string Caption(Measurement m, bool preview) =>
            preview || m.Number == 0 ? m.Label : m.Number.ToString(CultureInfo.CurrentCulture) + ") " + m.Label;

        private static void DrawLabel(RenderContext ctx, Point at, string text, bool alignLeft = false)
        {
            var formatted = new FormattedText(text, CultureInfo.CurrentCulture, FlowDirection.LeftToRight,
                LabelFont, 11, MeasureBrush, ctx.PixelsPerDip);
            double x = alignLeft ? at.X : at.X - formatted.Width / 2;
            var box = new Rect(x - 4, at.Y - formatted.Height / 2 - 1, formatted.Width + 8, formatted.Height + 2);
            ctx.Dc.DrawRoundedRectangle(LabelBackground, new Pen(MeasureBrush, 1), box, 2, 2);
            ctx.Dc.DrawText(formatted, new Point(x, at.Y - formatted.Height / 2));
        }

        #endregion

        #region Маркер привязки

        private static void DrawMarker(DrawingContext dc, Point p, SnapKind kind)
        {
            double s = MarkerSize;
            switch (kind)
            {
                case SnapKind.Endpoint:
                    dc.DrawRectangle(null, SnapPen, new Rect(p.X - s, p.Y - s, 2 * s, 2 * s));
                    break;
                case SnapKind.Midpoint:
                    dc.DrawGeometry(null, SnapPen, Polygon(new Point(p.X, p.Y - s - 1), new Point(p.X + s + 1, p.Y + s), new Point(p.X - s - 1, p.Y + s)));
                    break;
                case SnapKind.Center:
                    dc.DrawEllipse(null, SnapPen, p, s + 1, s + 1);
                    dc.DrawLine(SnapPen, new Point(p.X - 2.5, p.Y), new Point(p.X + 2.5, p.Y));
                    dc.DrawLine(SnapPen, new Point(p.X, p.Y - 2.5), new Point(p.X, p.Y + 2.5));
                    break;
                case SnapKind.Nearest:
                    dc.DrawGeometry(null, SnapPen, Polygon(new Point(p.X - s, p.Y - s), new Point(p.X + s, p.Y - s),
                        new Point(p.X - s, p.Y + s), new Point(p.X + s, p.Y + s)));
                    break;
            }
        }

        private static Geometry Polygon(params Point[] points)
        {
            var geometry = new StreamGeometry();
            using (var g = geometry.Open())
            {
                g.BeginFigure(points[0], false, true);
                g.PolyLineTo(points.Skip(1).ToList(), true, true);
            }
            geometry.Freeze();
            return geometry;
        }

        #endregion

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }

        private readonly struct RenderContext
        {
            public RenderContext(DrawingContext dc, Func<Point2, Point> map, double pixelsPerMm, double pixelsPerDip)
            {
                Dc = dc;
                Map = map;
                PixelsPerMm = pixelsPerMm;
                PixelsPerDip = pixelsPerDip;
            }

            public DrawingContext Dc { get; }
            public Func<Point2, Point> Map { get; }
            public double PixelsPerMm { get; }
            public double PixelsPerDip { get; }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using InvAuto.Core.Preview;
using InvAuto.Core.Reports;

namespace InvAuto.Features.Reports
{
    /// <summary>То, что для отчёта даёт Windows: картинка развертки (WPF) и шрифт для PDF.</summary>
    public static class ReportResources
    {
        private static readonly Brush ProfileBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x2B, 0x2F, 0x33)));
        private static readonly Brush BendBrush = Freeze(new SolidColorBrush(Color.FromRgb(0x06, 0x96, 0xD7)));
        private static readonly Brush EndBrush = Freeze(new SolidColorBrush(Color.FromRgb(0xD3, 0x2F, 0x2F)));

        /// <summary>
        /// PNG развертки для Excel. Рисуется вдвое крупнее (width × height в пикселях ячейки), чтобы картинка
        /// оставалась чёткой при печати и масштабе листа.
        /// </summary>
        public static byte[] FlatPng(FlatDrawing flat, int width, int height)
        {
            if (flat == null || flat.IsEmpty)
                return null;
            const int k = 2, pad = 4;
            int w = width * k, h = height * k;
            double scale = Math.Min((w - 2 * pad * k) / Math.Max(flat.Width, 1e-6), (h - 2 * pad * k) / Math.Max(flat.Height, 1e-6));
            double ox = (w - flat.Width * scale) / 2, oy = (h - flat.Height * scale) / 2;
            Point Map(Point2 p) => new Point(ox + (p.X - flat.MinX) * scale, oy + (flat.MaxY - p.Y) * scale);

            var visual = new DrawingVisual();
            using (var dc = visual.RenderOpen())
            {
                dc.DrawRectangle(Brushes.White, null, new Rect(0, 0, w, h));
                foreach (var kind in new[] { LayerKind.Bend, LayerKind.Profile, LayerKind.EndFeature })
                {
                    var brush = kind == LayerKind.Bend ? BendBrush : kind == LayerKind.EndFeature ? EndBrush : ProfileBrush;
                    var pen = new Pen(brush, kind == LayerKind.Bend ? 1.2 : 1.6) { LineJoin = PenLineJoin.Round };
                    pen.Freeze();
                    var geometry = new StreamGeometry();
                    using (var g = geometry.Open())
                    {
                        foreach (var p in flat.Polylines.Where(p => p.Kind == kind))
                        {
                            g.BeginFigure(Map(p.Points[0]), false, p.Closed);
                            g.PolyLineTo(p.Points.Skip(1).Select(Map).ToList(), true, true);
                        }
                    }
                    geometry.Freeze();
                    dc.DrawGeometry(null, pen, geometry);
                }
            }

            var bitmap = new RenderTargetBitmap(w, h, 96, 96, PixelFormats.Pbgra32);
            bitmap.Render(visual);
            var encoder = new PngBitmapEncoder();
            encoder.Frames.Add(BitmapFrame.Create(bitmap));
            using (var stream = new MemoryStream())
            {
                encoder.Save(stream);
                return stream.ToArray();
            }
        }

        /// <summary>Шрифт для PDF с кириллицей: Arial, иначе Segoe UI, иначе Calibri (есть в любой Windows).</summary>
        public static (TrueTypeFont Regular, TrueTypeFont Bold) Fonts()
        {
            string folder = Environment.GetFolderPath(Environment.SpecialFolder.Fonts);
            foreach (var (regular, bold) in new[] { ("arial.ttf", "arialbd.ttf"), ("segoeui.ttf", "segoeuib.ttf"), ("calibri.ttf", "calibrib.ttf") })
            {
                string r = Path.Combine(folder, regular), b = Path.Combine(folder, bold);
                if (File.Exists(r))
                    return (TrueTypeFont.Load(r), File.Exists(b) ? TrueTypeFont.Load(b) : TrueTypeFont.Load(r));
            }
            throw new FileNotFoundException("Не найден шрифт для PDF (Arial, Segoe UI или Calibri) в папке " + folder);
        }

        private static T Freeze<T>(T freezable) where T : Freezable
        {
            freezable.Freeze();
            return freezable;
        }
    }
}

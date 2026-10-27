using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InvAuto.Core.Bends;
using InvAuto.Core.Marking;
using InvAuto.Core.Preview;

namespace InvAuto.Core.Flat
{
    /// <summary>Что сделать с разверткой: линии гиба, поворот, маркировка.</summary>
    public sealed class FlatRequest
    {
        /// <summary>Параметры каждой линии гиба; null — линии гиба не обрабатываются.</summary>
        public Func<BendLineInfo, BendLineOptions> Bends { get; set; }

        public IEnumerable<string> BendLayers { get; set; } = DxfBendProcessor.DefaultLayers;

        /// <summary>Поворот развертки, градусы против часовой (вокруг начала координат).</summary>
        public double Rotation { get; set; }

        public MarkingOptions Marking { get; set; }

        /// <summary>Текст маркировки (обозначение детали).</summary>
        public string MarkingText { get; set; }

        /// <summary>Ручное место маркировки — в координатах развертки до поворота; null — автоматически.</summary>
        public MarkingPosition MarkingPosition { get; set; }
    }

    /// <summary>Развертка после обработки: итоговый DXF и всё, что нужно для показа.</summary>
    public sealed class FlatResult
    {
        internal FlatResult(string dxf, IReadOnlyList<ProcessedBendLine> bendLines, IReadOnlyList<Polyline2> endFeatures,
            MarkingResult marking, FlatDrawing drawing)
        {
            Dxf = dxf;
            BendLines = bendLines;
            EndFeatures = endFeatures;
            Marking = marking;
            Drawing = drawing;
        }

        /// <summary>Итоговый DXF. Тот же экземпляр строки, что исходный, если ничего не менялось.</summary>
        public string Dxf { get; }

        /// <summary>Линии гиба (координаты — на повёрнутой развертке, ключи — исходные).</summary>
        public IReadOnlyList<ProcessedBendLine> BendLines { get; }

        /// <summary>Законцовки для показа красным (на повёрнутой развертке).</summary>
        public IReadOnlyList<Polyline2> EndFeatures { get; }

        public MarkingResult Marking { get; }

        /// <summary>Чертёж для показа: итоговый DXF, законцовки и текст маркировки. null — не запрашивался.</summary>
        public FlatDrawing Drawing { get; }
    }

    /// <summary>
    /// Обработка развертки — одна и та же для превью и выгрузки: линии гиба (в исходных координатах, чтобы
    /// ключи линий не менялись) → поворот → маркировка (место ищется уже на повёрнутой развертке, ручное место
    /// поворачивается вместе с ней).
    /// </summary>
    public static class FlatPipeline
    {
        public static FlatResult Run(string raw, FlatRequest request, bool withDrawing)
        {
            request = request ?? new FlatRequest();
            var processed = request.Bends == null
                ? new BendProcessResult(raw, Array.Empty<ProcessedBendLine>())
                : DxfBendProcessor.Process(raw, request.BendLayers ?? DxfBendProcessor.DefaultLayers, request.Bends);

            double angle = request.Rotation;
            string rotated = DxfBendProcessor.Rotate(processed.Text, angle);

            bool mark = request.Marking != null && request.Marking.Enabled;
            var drawing = withDrawing || mark ? DxfReader.Read(new StringReader(rotated)) : null;
            var marking = DxfMarking.Apply(rotated, request.MarkingText, request.Marking, drawing, ToDrawing(request.MarkingPosition, angle));

            var bendLines = processed.Items.Select(i => new ProcessedBendLine(i.Line.Rotated(angle), i.Options, i.Layout)).ToList();
            var endFeatures = processed.EndFeatures.Select(f => Rotate(f, angle)).ToList();
            var shown = withDrawing ? drawing.WithEndFeatures(endFeatures).WithText(marking.Text) : null;
            return new FlatResult(marking.Dxf, bendLines, endFeatures, marking, shown);
        }

        /// <summary>Ручное место маркировки на повёрнутой развертке.</summary>
        public static MarkingPosition ToDrawing(MarkingPosition position, double rotation)
        {
            if (position == null || Math.Abs(rotation % 360) < 1e-12)
                return position;
            var (x, y) = Turn(position.X, position.Y, rotation);
            return new MarkingPosition
            {
                PartPath = position.PartPath, X = x, Y = y, Angle = position.EffectiveAngle + rotation, Height = position.Height
            };
        }

        /// <summary>Место, выбранное на повёрнутой развертке (центр и угол текста), — для хранения в исходных координатах.</summary>
        public static MarkingPosition FromDrawing(string partPath, Point2 center, double angle, double height, double rotation)
        {
            var (x, y) = Turn(center.X, center.Y, -rotation);
            return new MarkingPosition { PartPath = partPath, X = x, Y = y, Angle = FlatRotationSettings.Normalize(angle - rotation), Height = height };
        }

        /// <summary>Поворот точки вокруг начала координат на degrees против часовой.</summary>
        public static (double X, double Y) Turn(double x, double y, double degrees)
        {
            double a = degrees * Math.PI / 180, cos = Math.Cos(a), sin = Math.Sin(a);
            return (x * cos - y * sin, x * sin + y * cos);
        }

        private static Polyline2 Rotate(Polyline2 p, double degrees)
        {
            if (Math.Abs(degrees % 360) < 1e-12)
                return p;
            return new Polyline2(p.Layer, p.Points.Select(q =>
            {
                var (x, y) = Turn(q.X, q.Y, degrees);
                return new Point2(x, y);
            }).ToList(), p.Closed);
        }
    }
}

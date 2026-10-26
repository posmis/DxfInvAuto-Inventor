using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.Preview
{
    public struct Point2
    {
        public Point2(double x, double y)
        {
            X = x;
            Y = y;
        }

        public double X { get; }
        public double Y { get; }

        public override string ToString() => $"({X:0.###}; {Y:0.###})";
    }

    /// <summary>Как рисовать линии слоя в превью.</summary>
    public enum LayerKind
    {
        /// <summary>Контур и отверстия — то, что режется.</summary>
        Profile,

        /// <summary>Линии гиба.</summary>
        Bend,

        /// <summary>Элементы формовки, касательные и прочие вспомогательные линии.</summary>
        Auxiliary
    }

    public sealed class Polyline2
    {
        public Polyline2(string layer, IReadOnlyList<Point2> points, bool closed)
        {
            Layer = layer ?? string.Empty;
            Points = points;
            Closed = closed;
            Kind = ClassifyLayer(Layer);
        }

        public string Layer { get; }
        public IReadOnlyList<Point2> Points { get; }
        public bool Closed { get; }
        public LayerKind Kind { get; }

        public static LayerKind ClassifyLayer(string layer)
        {
            string upper = layer.ToUpperInvariant();
            if (upper.Contains("BEND"))
                return LayerKind.Bend;
            if (upper.Contains("FEATURE") || upper.Contains("TANGENT") || upper.Contains("ROLL") || upper.Contains("CENTER"))
                return LayerKind.Auxiliary;
            return LayerKind.Profile;
        }
    }

    /// <summary>Плоский чертёж развертки: набор полилиний в миллиметрах (единицы DXF).</summary>
    public sealed class FlatDrawing
    {
        public FlatDrawing(IReadOnlyList<Polyline2> polylines)
        {
            Polylines = polylines;

            var points = polylines.SelectMany(p => p.Points).ToList();
            if (points.Count == 0)
                return;

            MinX = points.Min(p => p.X);
            MinY = points.Min(p => p.Y);
            MaxX = points.Max(p => p.X);
            MaxY = points.Max(p => p.Y);
        }

        public static FlatDrawing Empty { get; } = new FlatDrawing(Array.Empty<Polyline2>());

        public IReadOnlyList<Polyline2> Polylines { get; }
        public double MinX { get; }
        public double MinY { get; }
        public double MaxX { get; }
        public double MaxY { get; }
        public double Width => MaxX - MinX;
        public double Height => MaxY - MinY;
        public bool IsEmpty => Polylines.Count == 0 || (Width <= 0 && Height <= 0);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using InvAuto.Core.Preview;

namespace InvAuto.Core.Measuring
{
    /// <summary>К чему привязалась точка.</summary>
    public enum SnapKind
    {
        /// <summary>Без привязки — точка под курсором.</summary>
        None,

        /// <summary>Конец отрезка или дуги, вершина контура.</summary>
        Endpoint,

        /// <summary>Середина отрезка или дуги.</summary>
        Midpoint,

        /// <summary>Центр дуги или отверстия.</summary>
        Center,

        /// <summary>Ближайшая точка на линии.</summary>
        Nearest
    }

    public readonly struct SnapResult
    {
        public SnapResult(Point2 point, SnapKind kind)
        {
            Point = point;
            Kind = kind;
        }

        public Point2 Point { get; }
        public SnapKind Kind { get; }

        public string KindName
        {
            get
            {
                switch (Kind)
                {
                    case SnapKind.Endpoint: return "конечная точка";
                    case SnapKind.Midpoint: return "середина";
                    case SnapKind.Center: return "центр";
                    case SnapKind.Nearest: return "на линии";
                    default: return "без привязки";
                }
            }
        }
    }

    /// <summary>Отрезок, выбранный щелчком: сам отрезок, его номер и точка щелчка на нём.</summary>
    public sealed class SegmentPick
    {
        public SegmentPick(int index, Segment2 segment, Point2 point)
        {
            Index = index;
            Segment = segment;
            Point = point;
        }

        public int Index { get; }
        public Segment2 Segment { get; }

        /// <summary>Точка отрезка, ближайшая к щелчку: по ней выбирается сторона угла.</summary>
        public Point2 Point { get; }
    }

    /// <summary>
    /// Объектные привязки по точной геометрии развертки. Порядок важности: конечные точки, центры
    /// и середины (ближайшая из них в пределах допуска), затем ближайшая точка на линии, иначе — без привязки.
    /// </summary>
    public sealed class FlatSnapper
    {
        private readonly List<(Point2 Point, SnapKind Kind)> _points = new List<(Point2, SnapKind)>();
        private readonly List<Segment2> _lines;
        private readonly List<Arc2> _arcs;

        public FlatSnapper(FlatDrawing drawing)
        {
            var geometry = drawing.Geometry;
            _lines = geometry.Lines.Where(l => l.Length > 1e-9).ToList();
            _arcs = geometry.Arcs.Where(a => a.Radius > 1e-9).ToList();

            var seen = new HashSet<(long, long, SnapKind)>();
            void Add(Point2 p, SnapKind kind)
            {
                // Общая вершина соседних отрезков — одна точка привязки.
                if (seen.Add((Key(p.X), Key(p.Y), kind)))
                    _points.Add((p, kind));
            }

            foreach (var line in _lines)
            {
                Add(line.A, SnapKind.Endpoint);
                Add(line.B, SnapKind.Endpoint);
                Add(line.Mid, SnapKind.Midpoint);
            }
            foreach (var arc in _arcs)
            {
                Add(arc.Center, SnapKind.Center);
                if (arc.IsCircle)
                    continue;
                Add(arc.Start, SnapKind.Endpoint);
                Add(arc.End, SnapKind.Endpoint);
                Add(arc.Mid, SnapKind.Midpoint);
            }
            foreach (var point in geometry.Points)
                Add(point, SnapKind.Endpoint);
        }

        public IReadOnlyList<Segment2> Lines => _lines;

        /// <param name="tolerance">Допуск привязки в мм (обычно несколько пикселей экрана); 0 — без привязки.</param>
        public SnapResult Snap(Point2 p, double tolerance)
        {
            if (tolerance <= 0)
                return new SnapResult(p, SnapKind.None);

            var best = new SnapResult(p, SnapKind.None);
            double bestDistance = tolerance;
            foreach (var (point, kind) in _points)
            {
                double d = Geometry2.Distance(p, point);
                // Конечные точки важнее середин и центров, если почти совпадают.
                if (d < bestDistance - 1e-9 ||
                    (Math.Abs(d - bestDistance) <= 1e-9 && kind == SnapKind.Endpoint && best.Kind != SnapKind.Endpoint))
                {
                    best = new SnapResult(point, kind);
                    bestDistance = d;
                }
            }
            if (best.Kind != SnapKind.None)
                return best;

            foreach (var line in _lines)
                Consider(line.Nearest(p));
            foreach (var arc in _arcs)
                Consider(arc.Nearest(p));
            return best;

            void Consider(Point2 candidate)
            {
                double d = Geometry2.Distance(p, candidate);
                if (d <= bestDistance)
                {
                    best = new SnapResult(candidate, SnapKind.Nearest);
                    bestDistance = d;
                }
            }
        }

        /// <summary>Ближайший прямой отрезок в пределах допуска (для измерения угла); null — рядом нет.</summary>
        public SegmentPick PickLine(Point2 p, double tolerance)
        {
            SegmentPick best = null;
            double bestDistance = tolerance;
            for (int i = 0; i < _lines.Count; i++)
            {
                var nearest = _lines[i].Nearest(p);
                double d = Geometry2.Distance(p, nearest);
                if (d <= bestDistance)
                {
                    best = new SegmentPick(i, _lines[i], nearest);
                    bestDistance = d;
                }
            }
            return best;
        }

        private static long Key(double value) => (long)Math.Round(value * 1e6);
    }
}

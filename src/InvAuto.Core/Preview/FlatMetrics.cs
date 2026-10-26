using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.Preview
{
    /// <summary>
    /// Производственные показатели развертки, посчитанные по DXF: площадь, длина реза, число врезок.
    /// Контур в DXF Inventor обычно записан отдельными отрезками и дугами — они сшиваются в замкнутые
    /// контуры по совпадающим концам (точность 0,01 мм). Дуги уже разбиты на участки по 5°, поэтому
    /// длина и площадь у дуг занижены меньше чем на 0,1 %.
    /// </summary>
    public sealed class FlatMetrics
    {
        public const double JoinTolerance = 0.01;

        /// <summary>Площадь детали за вычетом отверстий, мм².</summary>
        public double Area { get; private set; }

        /// <summary>Площадь по наружному контуру, мм².</summary>
        public double OuterArea { get; private set; }

        /// <summary>Длина реза: наружный контур и все отверстия, мм.</summary>
        public double CutLength { get; private set; }

        /// <summary>Длина линий гиба в DXF (прорези, пунктир, вырезы — что попадёт в файл), мм.</summary>
        public double BendLength { get; private set; }

        /// <summary>Число врезок: один замкнутый контур (или разомкнутая цепочка) — одна врезка.</summary>
        public int Pierces { get; private set; }

        /// <summary>Цепочки, которые не удалось замкнуть: признак неполного контура в DXF.</summary>
        public int OpenChains { get; private set; }

        public double Width { get; private set; }
        public double Height { get; private set; }

        /// <summary>Масса, кг: площадь × толщина × плотность.</summary>
        public double Mass(double thicknessMm, double densityKgM3) => Area * thicknessMm * densityKgM3 * 1e-9;

        public static FlatMetrics Compute(FlatDrawing drawing)
        {
            var metrics = new FlatMetrics();
            if (drawing == null || drawing.IsEmpty)
                return metrics;

            metrics.Width = drawing.Width;
            metrics.Height = drawing.Height;

            var profile = drawing.Polylines.Where(p => p.Kind == LayerKind.Profile && p.Points.Count >= 2).ToList();
            metrics.CutLength = profile.Sum(Length);
            metrics.BendLength = drawing.Polylines.Where(p => p.Kind == LayerKind.Bend).Sum(Length);

            var loops = new List<List<Point2>>();
            var open = new List<List<Point2>>();
            foreach (var p in profile.Where(p => p.Closed))
                loops.Add(p.Points.ToList());
            Chain(profile.Where(p => !p.Closed).Select(p => p.Points.ToList()).ToList(), loops, open);

            metrics.Pierces = loops.Count + open.Count;
            metrics.OpenChains = open.Count;

            // Наружный контур — с наибольшей площадью, остальные замкнутые — отверстия внутри него.
            var areas = loops.Select(l => Math.Abs(SignedArea(l))).OrderByDescending(a => a).ToList();
            if (areas.Count > 0)
            {
                metrics.OuterArea = areas[0];
                metrics.Area = Math.Max(0, areas[0] - areas.Skip(1).Sum());
            }
            return metrics;
        }

        /// <summary>Сшивает разомкнутые полилинии в цепочки по совпадающим концам.</summary>
        private static void Chain(List<List<Point2>> pieces, List<List<Point2>> loops, List<List<Point2>> open)
        {
            var used = new bool[pieces.Count];
            for (int i = 0; i < pieces.Count; i++)
            {
                if (used[i])
                    continue;
                used[i] = true;
                var chain = new List<Point2>(pieces[i]);

                bool extended = true;
                while (extended && !Near(chain[0], chain[chain.Count - 1]))
                {
                    extended = false;
                    for (int j = 0; j < pieces.Count; j++)
                    {
                        if (used[j])
                            continue;
                        var piece = pieces[j];
                        var end = chain[chain.Count - 1];
                        var start = chain[0];
                        if (Near(end, piece[0]))
                            chain.AddRange(piece.Skip(1));
                        else if (Near(end, piece[piece.Count - 1]))
                            chain.AddRange(Enumerable.Reverse(piece).Skip(1));
                        else if (Near(start, piece[piece.Count - 1]))
                            chain.InsertRange(0, piece.Take(piece.Count - 1));
                        else if (Near(start, piece[0]))
                            chain.InsertRange(0, Enumerable.Reverse(piece).Take(piece.Count - 1));
                        else
                            continue;
                        used[j] = true;
                        extended = true;
                        break;
                    }
                }

                if (chain.Count > 2 && Near(chain[0], chain[chain.Count - 1]))
                    loops.Add(chain);
                else
                    open.Add(chain);
            }
        }

        private static bool Near(Point2 a, Point2 b) => Math.Abs(a.X - b.X) <= JoinTolerance && Math.Abs(a.Y - b.Y) <= JoinTolerance;

        private static double Length(Polyline2 p)
        {
            double length = 0;
            for (int i = 1; i < p.Points.Count; i++)
                length += Geometry2.Distance(p.Points[i - 1], p.Points[i]);
            if (p.Closed && p.Points.Count > 2)
                length += Geometry2.Distance(p.Points[p.Points.Count - 1], p.Points[0]);
            return length;
        }

        /// <summary>Площадь многоугольника по формуле Гаусса (знак — направление обхода).</summary>
        private static double SignedArea(IReadOnlyList<Point2> points)
        {
            double sum = 0;
            for (int i = 0; i < points.Count; i++)
            {
                var a = points[i];
                var b = points[(i + 1) % points.Count];
                sum += a.X * b.Y - b.X * a.Y;
            }
            return sum / 2;
        }
    }
}

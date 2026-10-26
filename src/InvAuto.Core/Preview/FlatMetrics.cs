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

            var contours = FlatContours.Build(drawing);
            metrics.Pierces = contours.Count;
            metrics.OpenChains = contours.Count(c => !c.Closed);

            // Наружный контур — с наибольшей площадью, остальные замкнутые — отверстия внутри него.
            var outer = contours.FirstOrDefault(c => c.IsOuter);
            if (outer != null)
            {
                metrics.OuterArea = outer.Area;
                // Отверстие на краю (например, законцовка линии гиба) вычитается только своей частью внутри детали.
                metrics.Area = Math.Max(0, outer.Area - contours.Where(c => c.Closed && !c.IsOuter).Sum(c => c.Area * c.FractionInside(outer)));
            }
            return metrics;
        }

        private static double Length(Polyline2 p)
        {
            double length = 0;
            for (int i = 1; i < p.Points.Count; i++)
                length += Geometry2.Distance(p.Points[i - 1], p.Points[i]);
            if (p.Closed && p.Points.Count > 2)
                length += Geometry2.Distance(p.Points[p.Points.Count - 1], p.Points[0]);
            return length;
        }
    }
}

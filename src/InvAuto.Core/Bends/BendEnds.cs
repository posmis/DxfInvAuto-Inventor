using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace InvAuto.Core.Bends
{
    /// <summary>Форма законцовки линии гиба — выреза на каждом конце линии (там, где она выходит на контур).</summary>
    public enum BendEndShape
    {
        /// <summary>Без законцовок.</summary>
        None,

        /// <summary>Окружность диаметром EndSize.</summary>
        Circle,

        /// <summary>Квадрат со стороной EndSize, стороны вдоль и поперёк линии гиба (0°).</summary>
        Square,

        /// <summary>Квадрат со стороной EndSize, повёрнутый на 45° к линии гиба (ромб).</summary>
        Diamond
    }

    /// <summary>Вершина контура законцовки: точка и выпуклость участка до следующей вершины (0 — прямой).</summary>
    public readonly struct EndVertex
    {
        public EndVertex(double x, double y, double bulge = 0)
        {
            X = x;
            Y = y;
            Bulge = bulge;
        }

        public double X { get; }
        public double Y { get; }
        public double Bulge { get; }
    }

    /// <summary>Контур одной законцовки: окружность (центр и радиус) или замкнутый многоугольник.</summary>
    public sealed class EndOutline
    {
        private EndOutline(double cx, double cy, double radius, IReadOnlyList<EndVertex> vertices)
        {
            CenterX = cx;
            CenterY = cy;
            Radius = radius;
            Vertices = vertices;
        }

        public double CenterX { get; }
        public double CenterY { get; }

        /// <summary>Больше нуля — окружность.</summary>
        public double Radius { get; }

        /// <summary>Вершины замкнутого контура (для окружности — пусто).</summary>
        public IReadOnlyList<EndVertex> Vertices { get; }

        public bool IsCircle => Radius > 0;

        public static EndOutline Circle(double cx, double cy, double radius) =>
            new EndOutline(cx, cy, radius, Array.Empty<EndVertex>());

        public static EndOutline Polygon(double cx, double cy, IReadOnlyList<EndVertex> vertices) =>
            new EndOutline(cx, cy, 0, vertices);
    }

    /// <summary>
    /// Форма законцовки. Чтобы добавить новую: значение в BendEndShape, класс здесь и регистрация в BendEnds.All.
    /// Контур строится в координатах развертки по центру и направлению линии гиба (ux, uy — единичный вектор).
    /// </summary>
    public interface IBendEndShape
    {
        BendEndShape Shape { get; }

        /// <summary>Название для интерфейса.</summary>
        string Title { get; }

        /// <summary>Как называется размер: «Диаметр», «Сторона».</summary>
        string SizeTitle { get; }

        EndOutline Outline(double cx, double cy, double ux, double uy, double size);

        /// <summary>Насколько форма выступает от центра вдоль линии гиба, мм.</summary>
        double Reach(double size);
    }

    public static class BendEnds
    {
        public static IReadOnlyList<IBendEndShape> All { get; } = new IBendEndShape[]
        {
            new NoEnds(),
            new CircleEnds(),
            new SquareEnds(),
            new DiamondEnds()
        };

        public static IBendEndShape For(BendEndShape shape) => All.FirstOrDefault(s => s.Shape == shape) ?? All[0];

        /// <summary>
        /// Законцовки линии гиба от (x1; y1) до (x2; y2): по одной на каждом конце. Центр — на линии,
        /// смещён внутрь на EndOffset (0 — точно на конце линии, то есть на краю детали).
        /// Пусто — законцовки выключены, размер не задан или линия нулевой длины.
        /// </summary>
        public static IReadOnlyList<EndOutline> Outlines(double x1, double y1, double x2, double y2, BendLineOptions options)
        {
            double length = Math.Sqrt((x2 - x1) * (x2 - x1) + (y2 - y1) * (y2 - y1));
            if (options == null || options.EndShape == BendEndShape.None || options.EndSize <= 0 || length < 1e-9)
                return Array.Empty<EndOutline>();

            double ux = (x2 - x1) / length, uy = (y2 - y1) / length;
            double offset = Math.Max(0, Math.Min(options.EndOffset, length / 2)); // не дальше середины линии
            var shape = For(options.EndShape);
            return new[]
            {
                shape.Outline(x1 + ux * offset, y1 + uy * offset, ux, uy, options.EndSize),
                shape.Outline(x2 - ux * offset, y2 - uy * offset, ux, uy, options.EndSize)
            };
        }

        /// <summary>Короткое описание: «окружность Ø3», «квадрат 3 мм под 45°», с отступом, если задан.</summary>
        public static string Describe(BendLineOptions o)
        {
            if (o.EndShape == BendEndShape.None || o.EndSize <= 0)
                return string.Empty;
            var c = CultureInfo.CurrentCulture;
            string shape;
            switch (o.EndShape)
            {
                case BendEndShape.Circle: shape = string.Format(c, "окружность Ø{0:0.##}", o.EndSize); break;
                case BendEndShape.Square: shape = string.Format(c, "квадрат {0:0.##} мм", o.EndSize); break;
                default: shape = string.Format(c, "квадрат {0:0.##} мм под 45°", o.EndSize); break;
            }
            return o.EndOffset > 0 ? shape + string.Format(c, ", внутрь {0:0.##} мм", o.EndOffset) : shape;
        }

        private sealed class NoEnds : IBendEndShape
        {
            public BendEndShape Shape => BendEndShape.None;
            public string Title => "Нет";
            public string SizeTitle => "Размер";
            public EndOutline Outline(double cx, double cy, double ux, double uy, double size) => null;
            public double Reach(double size) => 0;
        }

        private sealed class CircleEnds : IBendEndShape
        {
            public BendEndShape Shape => BendEndShape.Circle;
            public string Title => "Окружность";
            public string SizeTitle => "Диаметр";
            public EndOutline Outline(double cx, double cy, double ux, double uy, double size) => EndOutline.Circle(cx, cy, size / 2);
            public double Reach(double size) => size / 2;
        }

        /// <summary>Квадрат: стороны вдоль линии гиба и поперёк неё.</summary>
        private sealed class SquareEnds : IBendEndShape
        {
            public BendEndShape Shape => BendEndShape.Square;
            public string Title => "Квадрат 0°";
            public string SizeTitle => "Сторона";
            public double Reach(double size) => size / 2;

            public EndOutline Outline(double cx, double cy, double ux, double uy, double size)
            {
                double h = size / 2;
                double nx = -uy, ny = ux;
                return EndOutline.Polygon(cx, cy, new[]
                {
                    new EndVertex(cx + (ux + nx) * h, cy + (uy + ny) * h),
                    new EndVertex(cx + (-ux + nx) * h, cy + (-uy + ny) * h),
                    new EndVertex(cx + (-ux - nx) * h, cy + (-uy - ny) * h),
                    new EndVertex(cx + (ux - nx) * h, cy + (uy - ny) * h)
                });
            }
        }

        /// <summary>Квадрат под 45° к линии гиба: вершины на линии и на перпендикуляре к ней.</summary>
        private sealed class DiamondEnds : IBendEndShape
        {
            public BendEndShape Shape => BendEndShape.Diamond;
            public string Title => "Квадрат 45°";
            public string SizeTitle => "Сторона";
            public double Reach(double size) => size / Math.Sqrt(2);

            public EndOutline Outline(double cx, double cy, double ux, double uy, double size)
            {
                double d = size / Math.Sqrt(2); // половина диагонали квадрата
                double nx = -uy, ny = ux;
                return EndOutline.Polygon(cx, cy, new[]
                {
                    new EndVertex(cx + ux * d, cy + uy * d),
                    new EndVertex(cx + nx * d, cy + ny * d),
                    new EndVertex(cx - ux * d, cy - uy * d),
                    new EndVertex(cx - nx * d, cy - ny * d)
                });
            }
        }
    }
}

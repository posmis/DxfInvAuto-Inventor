using System;
using System.Collections.Generic;
using Inventor;
using InvAuto.Core.Preview;

namespace InvAuto.InventorApi
{
    /// <summary>
    /// Превью развертки прямо из геометрии Inventor, без записи файлов:
    /// контуры верхней грани развертки и линии гиба, спроецированные на плоскость развертки.
    /// Имена слоёв — как в DXF Inventor, чтобы превью раскрашивалось так же.
    /// </summary>
    public static class FlatPatternGeometry
    {
        /// <summary>Точность разбиения кривых на отрезки, см (внутренние единицы Inventor).</summary>
        private const double StrokeTolerance = 0.01;

        public static FlatDrawing Read(SheetMetalComponentDefinition definition)
        {
            if (!definition.HasFlatPattern)
                return null;

            FlatPattern flat = definition.FlatPattern;
            Face top = flat.TopFace;
            var projection = Projection.FromFace(top);
            var polylines = new List<Polyline2>();

            foreach (EdgeLoop loop in top.EdgeLoops)
            {
                string layer = loop.IsOuterEdgeLoop ? "IV_OUTER_PROFILE" : "IV_INTERIOR_PROFILES";
                foreach (Edge edge in loop.Edges)
                    AddEdge(polylines, edge, layer, projection);
            }

            AddBendLines(polylines, flat, FlatPatternEdgeTypeEnum.kBendUpFlatPatternEdge, "IV_BEND", projection);
            AddBendLines(polylines, flat, FlatPatternEdgeTypeEnum.kBendDownFlatPatternEdge, "IV_BEND_DOWN", projection);

            return new FlatDrawing(polylines);
        }

        private static void AddBendLines(List<Polyline2> polylines, FlatPattern flat, FlatPatternEdgeTypeEnum type,
            string layer, Projection projection)
        {
            try
            {
                foreach (Edge edge in flat.GetEdgesOfType(type, true))
                    AddEdge(polylines, edge, layer, projection);
            }
            catch
            {
                // Линии гиба в превью необязательны.
            }
        }

        private static void AddEdge(List<Polyline2> polylines, Edge edge, string layer, Projection projection)
        {
            CurveEvaluator evaluator = edge.Evaluator;
            evaluator.GetParamExtents(out double from, out double to);
            evaluator.GetStrokes(from, to, StrokeTolerance, out int count, out double[] coordinates);

            var points = new List<Point2>(count);
            for (int i = 0; i + 2 < coordinates.Length && points.Count < count; i += 3)
                points.Add(projection.Apply(coordinates[i], coordinates[i + 1], coordinates[i + 2]));

            if (points.Count >= 2)
                polylines.Add(new Polyline2(layer, points, false));
        }

        /// <summary>Проекция точек на плоскость грани в её собственных осях.</summary>
        private readonly struct Projection
        {
            private readonly double _ox, _oy, _oz, _ux, _uy, _uz, _vx, _vy, _vz;

            private Projection(double[] origin, double[] u, double[] v)
            {
                _ox = origin[0]; _oy = origin[1]; _oz = origin[2];
                _ux = u[0]; _uy = u[1]; _uz = u[2];
                _vx = v[0]; _vy = v[1]; _vz = v[2];
            }

            public static Projection FromFace(Face face)
            {
                double[] origin = { 0, 0, 0 };
                double[] normal = { 0, 0, 1 };
                if (face.Geometry is Plane plane)
                {
                    origin = new[] { plane.RootPoint.X, plane.RootPoint.Y, plane.RootPoint.Z };
                    normal = new[] { plane.Normal.X, plane.Normal.Y, plane.Normal.Z };
                }

                // Для развертки в плоскости XY оси совпадают с X и Y детали — как в DXF.
                double[] helper = Math.Abs(normal[2]) >= 0.9 ? new double[] { 0, 1, 0 } : new double[] { 0, 0, 1 };
                double[] u = Normalize(Cross(helper, normal));
                double[] v = Cross(normal, u);
                return new Projection(origin, u, v);
            }

            public Point2 Apply(double x, double y, double z)
            {
                double dx = x - _ox, dy = y - _oy, dz = z - _oz;
                return new Point2(dx * _ux + dy * _uy + dz * _uz, dx * _vx + dy * _vy + dz * _vz);
            }

            private static double[] Cross(double[] a, double[] b) => new[]
            {
                a[1] * b[2] - a[2] * b[1],
                a[2] * b[0] - a[0] * b[2],
                a[0] * b[1] - a[1] * b[0]
            };

            private static double[] Normalize(double[] a)
            {
                double length = Math.Sqrt(a[0] * a[0] + a[1] * a[1] + a[2] * a[2]);
                return length < 1e-12 ? new double[] { 1, 0, 0 } : new[] { a[0] / length, a[1] / length, a[2] / length };
            }
        }
    }
}

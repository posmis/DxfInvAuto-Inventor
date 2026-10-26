using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using InvAuto.Core.Bends;
using InvAuto.Core.Preview;

namespace InvAuto.Core.Marking
{
    public enum MarkingStatus
    {
        /// <summary>Маркировка выключена.</summary>
        Off,

        /// <summary>Включена, но у детали нет обозначения.</summary>
        NoText,

        /// <summary>Текст не помещается даже минимальной высоты.</summary>
        NoRoom,

        /// <summary>Нанесена.</summary>
        Placed
    }

    /// <summary>Место маркировки, заданное вручную в окне просмотра: центр текста (мм, координаты развертки) и поворот.</summary>
    public sealed class MarkingPosition
    {
        /// <summary>Полный путь к файлу детали.</summary>
        public string PartPath { get; set; } = string.Empty;

        public double X { get; set; }
        public double Y { get; set; }

        /// <summary>Текст снизу вверх (90°).</summary>
        public bool Vertical { get; set; }

        /// <summary>Высота текста, мм (как было при переносе; 0 — высота из параметров). Не выходит за MinHeight…Height.</summary>
        public double Height { get; set; }

        public MarkingPosition Clone() => (MarkingPosition)MemberwiseClone();
    }

    /// <summary>Итог маркировки: DXF (тот же экземпляр строки, если текст не добавлялся) и где стоит текст.</summary>
    public sealed class MarkingResult
    {
        public MarkingResult(MarkingStatus status, string dxf, FlatText text = null, bool manual = false, bool conflict = false,
            bool centerCut = false, double requestedHeight = 0)
        {
            Status = status;
            Dxf = dxf;
            Text = text;
            Manual = manual;
            Conflict = conflict;
            CenterCut = centerCut;
            RequestedHeight = requestedHeight;
        }

        public MarkingStatus Status { get; }
        public string Dxf { get; }
        public FlatText Text { get; }

        /// <summary>Место задано вручную.</summary>
        public bool Manual { get; }

        /// <summary>Ручное место не годится: текст задевает контур, отверстие или линию гиба, попал в вырез или вне детали.</summary>
        public bool Conflict { get; }

        /// <summary>Центр детали вырезан — автоматическое место смещено от центра, стоит проверить.</summary>
        public bool CenterCut { get; }

        /// <summary>Заданная высота текста (если текст уменьшен, Text.Height меньше).</summary>
        public double RequestedHeight { get; }

        public bool Shrunk => Text != null && RequestedHeight > 0 && Text.Height < RequestedHeight - 1e-9;

        /// <summary>Центр текста — для отметок замечаний на развертке.</summary>
        public Point2? Center => Text == null ? (Point2?)null : new Point2((Text.MinX + Text.MaxX) / 2, (Text.MinY + Text.MaxY) / 2);

        /// <summary>Для панели свойств.</summary>
        public string Description
        {
            get
            {
                switch (Status)
                {
                    case MarkingStatus.NoText: return "нет обозначения";
                    case MarkingStatus.NoRoom: return "не помещается";
                    case MarkingStatus.Placed:
                        var notes = new List<string>();
                        if (Text.Vertical)
                            notes.Add("вертикально");
                        if (Manual)
                            notes.Add("вручную");
                        if (Conflict)
                            notes.Add("задевает контур");
                        if (CenterCut && !Manual)
                            notes.Add("центр детали вырезан");
                        return string.Format(CultureInfo.CurrentCulture, "«{0}», {1:0.##} мм", Text.Value, Text.Height) +
                               (notes.Count > 0 ? ", " + string.Join(", ", notes) : string.Empty);
                    default: return "выкл.";
                }
            }
        }
    }

    /// <summary>
    /// Где на развертке можно ставить текст: на металле (внутри наружного контура, не в вырезе) и с отступом
    /// от контура, отверстий и линий гиба. Строится один раз для развертки — для поиска места и для проверки
    /// ручного места при перетаскивании.
    /// </summary>
    public sealed class MarkingArea
    {
        private readonly Contour _outer;
        private readonly List<Contour> _holes;
        private readonly List<(Point2 A, Point2 B)> _segments = new List<(Point2, Point2)>();

        // Сетка по габариту детали: в ячейке — номера отрезков, задевающих её. Проверка места смотрит только
        // отрезки рядом с текстом — при тысячах отверстий перебор всех отрезков на каждое место слишком долог.
        private const int GridSize = 64;
        private List<int>[] _grid;
        private List<int>[] _holeGrid;
        private double _gridX, _gridY, _cell;
        private int[] _seen;
        private int _query;

        public MarkingArea(FlatDrawing drawing)
        {
            var contours = FlatContours.Build(drawing ?? FlatDrawing.Empty);
            _outer = contours.FirstOrDefault(c => c.IsOuter);
            // Вырезы: то, что внутри них, при резке отпадает — маркировать там нельзя.
            _holes = contours.Where(c => c.Closed && !c.IsOuter).ToList();
            foreach (var p in (drawing ?? FlatDrawing.Empty).Polylines.Where(p => p.Kind == LayerKind.Profile || p.Kind == LayerKind.Bend))
            {
                for (int i = 1; i < p.Points.Count; i++)
                    _segments.Add((p.Points[i - 1], p.Points[i]));
                if (p.Closed && p.Points.Count > 2)
                    _segments.Add((p.Points[p.Points.Count - 1], p.Points[0]));
            }
            BuildGrid();
        }

        private void BuildGrid()
        {
            if (_outer == null)
                return;
            _gridX = _outer.MinX;
            _gridY = _outer.MinY;
            _cell = Math.Max(Math.Max(_outer.MaxX - _outer.MinX, _outer.MaxY - _outer.MinY) / GridSize, 1e-6);
            _grid = new List<int>[GridSize * GridSize];
            _holeGrid = new List<int>[GridSize * GridSize];
            _seen = new int[_segments.Count];
            for (int h = 0; h < _holes.Count; h++)
            {
                Cells(_holes[h].MinX, _holes[h].MinY, _holes[h].MaxX, _holes[h].MaxY, out int i0, out int j0, out int i1, out int j1);
                for (int x = i0; x <= i1; x++)
                    for (int y = j0; y <= j1; y++)
                        (_holeGrid[x * GridSize + y] ?? (_holeGrid[x * GridSize + y] = new List<int>())).Add(h);
            }
            for (int i = 0; i < _segments.Count; i++)
            {
                var (a, b) = _segments[i];
                Cells(Math.Min(a.X, b.X), Math.Min(a.Y, b.Y), Math.Max(a.X, b.X), Math.Max(a.Y, b.Y), out int i0, out int j0, out int i1, out int j1);
                for (int x = i0; x <= i1; x++)
                    for (int y = j0; y <= j1; y++)
                        (_grid[x * GridSize + y] ?? (_grid[x * GridSize + y] = new List<int>())).Add(i);
            }
        }

        /// <summary>Ячейки, которые задевает прямоугольник (за пределами габарита — крайние).</summary>
        private void Cells(double x0, double y0, double x1, double y1, out int i0, out int j0, out int i1, out int j1)
        {
            int Clamp(double v) => (int)Math.Max(0, Math.Min(GridSize - 1, Math.Floor(v)));
            i0 = Clamp((x0 - _gridX) / _cell);
            i1 = Clamp((x1 - _gridX) / _cell);
            j0 = Clamp((y0 - _gridY) / _cell);
            j1 = Clamp((y1 - _gridY) / _cell);
        }

        /// <summary>Есть наружный контур — можно маркировать.</summary>
        public bool HasOuter => _outer != null;

        /// <summary>Центр габарита детали.</summary>
        public Point2 Center => _outer == null ? new Point2(0, 0) : new Point2((_outer.MinX + _outer.MaxX) / 2, (_outer.MinY + _outer.MaxY) / 2);

        /// <summary>Точка на металле: внутри наружного контура и не в вырезе.</summary>
        public bool OnMetal(Point2 p)
        {
            if (_outer == null || !_outer.Contains(p))
                return false;
            Cells(p.X, p.Y, p.X, p.Y, out int i, out int j, out _, out _);
            var holes = _holeGrid[i * GridSize + j];
            return holes == null || !holes.Any(h => _holes[h].Contains(p));
        }

        /// <summary>Центр детали вырезан (попадает в отверстие).</summary>
        public bool CenterCut => _outer != null && _holes.Any(h => h.Contains(Center));

        /// <summary>Текст на металле и с отступом margin от всех линий.</summary>
        public bool Fits(FlatText text, double margin)
        {
            if (text == null || _outer == null)
                return false;
            var center = new Point2((text.MinX + text.MaxX) / 2, (text.MinY + text.MaxY) / 2);
            // Рамку не пересекает ни одна линия — значит, она целиком там же, где её центр.
            return Clear(text.MinX - margin, text.MinY - margin, text.MaxX + margin, text.MaxY + margin) && OnMetal(center);
        }

        /// <summary>Место для текста; null — не помещается.</summary>
        public FlatText Place(string text, MarkingOptions options)
        {
            var o = options.Normalized();
            if (_outer == null || string.IsNullOrWhiteSpace(text))
                return null;

            var center = Center;
            double size = Math.Max(_outer.MaxX - _outer.MinX, _outer.MaxY - _outer.MinY);
            for (double h = o.Height; h >= o.MinHeight - 1e-9; h = h > o.MinHeight + 1e-9 ? Math.Max(o.MinHeight, h * 0.8) : 0)
            {
                double w = DxfMarking.TextWidth(text, h);
                foreach (bool vertical in new[] { false, true })
                {
                    double bw = vertical ? h : w, bh = vertical ? w : h; // габарит текста
                    double step = Math.Max(h / 4, size / 120);
                    var candidates = new List<(double X, double Y, double D)>();
                    for (double x = _outer.MinX + bw / 2; x <= _outer.MaxX - bw / 2 + 1e-9; x += step)
                        for (double y = _outer.MinY + bh / 2; y <= _outer.MaxY - bh / 2 + 1e-9; y += step)
                            candidates.Add((x, y, (x - center.X) * (x - center.X) + (y - center.Y) * (y - center.Y)));
                    // Центр габарита — тоже кандидат: у симметричных деталей текст встаёт точно посередине.
                    candidates.Add((center.X, center.Y, -1));

                    foreach (var c in candidates.OrderBy(c => c.D))
                    {
                        var candidate = DxfMarking.At(text, c.X, c.Y, h, vertical);
                        if (Fits(candidate, o.Margin))
                            return candidate;
                    }
                }
                if (h <= o.MinHeight + 1e-9)
                    break;
            }
            return null;
        }

        /// <summary>Ни один отрезок не задевает прямоугольник.</summary>
        private bool Clear(double x0, double y0, double x1, double y1)
        {
            if (_grid == null)
                return true;
            _query++;
            Cells(x0, y0, x1, y1, out int i0, out int j0, out int i1, out int j1);
            for (int x = i0; x <= i1; x++)
            {
                for (int y = j0; y <= j1; y++)
                {
                    var cell = _grid[x * GridSize + y];
                    if (cell == null)
                        continue;
                    foreach (int i in cell)
                    {
                        if (_seen[i] == _query)
                            continue; // отрезок в нескольких ячейках — проверяем один раз
                        _seen[i] = _query;
                        var (a, b) = _segments[i];
                        if (Math.Max(a.X, b.X) < x0 || Math.Min(a.X, b.X) > x1 || Math.Max(a.Y, b.Y) < y0 || Math.Min(a.Y, b.Y) > y1)
                            continue;
                        if (Crosses(a, b, x0, y0, x1, y1))
                            return false;
                    }
                }
            }
            return true;
        }

        /// <summary>Отрезок пересекает прямоугольник или лежит в нём (отсечение Лианга — Барски).</summary>
        private static bool Crosses(Point2 a, Point2 b, double x0, double y0, double x1, double y1)
        {
            double dx = b.X - a.X, dy = b.Y - a.Y, t0 = 0, t1 = 1;
            bool Clip(double p, double q)
            {
                if (Math.Abs(p) < 1e-15)
                    return q >= 0;
                double r = q / p;
                if (p < 0)
                {
                    if (r > t1) return false;
                    if (r > t0) t0 = r;
                }
                else
                {
                    if (r < t0) return false;
                    if (r < t1) t1 = r;
                }
                return true;
            }
            return Clip(-dx, a.X - x0) && Clip(dx, x1 - a.X) && Clip(-dy, a.Y - y0) && Clip(dy, y1 - a.Y);
        }
    }

    /// <summary>
    /// Маркировка развертки обозначением. Место — заданное вручную (окно просмотра) или найденное автоматически:
    /// как можно ближе к центру детали, на металле (внутри наружного контура и не в вырезе), с отступом от контура,
    /// отверстий и линий гиба. Сначала горизонтально, затем вертикально; не помещается — высота уменьшается (до MinHeight).
    /// </summary>
    public static class DxfMarking
    {
        /// <summary>Ширина символа относительно высоты — с запасом: шрифт выбирает программа раскроя.</summary>
        public const double CharWidth = 0.9;

        /// <summary>Цвет слоя маркировки, если слоя нет в файле (3 — зелёный).</summary>
        public const int LayerColor = 3;

        public static double TextWidth(string text, double height) => (text ?? string.Empty).Trim().Length * height * CharWidth;

        /// <summary>Текст с центром в (cx; cy). Вертикальный идёт снизу вверх, верх букв — влево.</summary>
        public static FlatText At(string text, double cx, double cy, double height, bool vertical)
        {
            double w = TextWidth(text, height);
            return vertical
                ? new FlatText(text.Trim(), cx + height / 2, cy - w / 2, height, 90, w)
                : new FlatText(text.Trim(), cx - w / 2, cy - height / 2, height, 0, w);
        }

        /// <summary>
        /// Маркировка обработанного DXF; drawing — он же, прочитанный (null — прочитать). position — место, заданное
        /// вручную: текст ставится туда высотой Height, даже если задевает контур (тогда Conflict).
        /// </summary>
        public static MarkingResult Apply(string dxf, string text, MarkingOptions options, FlatDrawing drawing = null,
            MarkingPosition position = null)
        {
            if (options == null || !options.Enabled)
                return new MarkingResult(MarkingStatus.Off, dxf);
            text = (text ?? string.Empty).Trim();
            if (text.Length == 0)
                return new MarkingResult(MarkingStatus.NoText, dxf);

            var o = options.Normalized();
            var area = new MarkingArea(drawing ?? DxfReader.Read(new StringReader(dxf)));
            if (position != null)
            {
                double height = position.Height > 0 ? Math.Max(o.MinHeight, Math.Min(o.Height, position.Height)) : o.Height;
                var manual = At(text, position.X, position.Y, height, position.Vertical);
                return new MarkingResult(MarkingStatus.Placed, DxfBendProcessor.AddText(dxf, manual, o.Layer, LayerColor), manual,
                    manual: true, conflict: !area.Fits(manual, o.Margin), requestedHeight: o.Height);
            }

            var placed = area.Place(text, o);
            return placed == null
                ? new MarkingResult(MarkingStatus.NoRoom, dxf, centerCut: area.CenterCut, requestedHeight: o.Height)
                : new MarkingResult(MarkingStatus.Placed, DxfBendProcessor.AddText(dxf, placed, o.Layer, LayerColor), placed,
                    centerCut: area.CenterCut, requestedHeight: o.Height);
        }
    }
}

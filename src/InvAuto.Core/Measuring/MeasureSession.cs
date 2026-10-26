using System.Collections.ObjectModel;
using InvAuto.Core.Preview;

namespace InvAuto.Core.Measuring
{
    public enum MeasureMode
    {
        /// <summary>Измерения выключены: щелчки только перемещают вид.</summary>
        None,

        /// <summary>Расстояние между двумя точками.</summary>
        Distance,

        /// <summary>Угол между двумя отрезками.</summary>
        Angle
    }

    /// <summary>
    /// Ход измерения в окне просмотра: режим, привязка под курсором, первая указанная точка или отрезок,
    /// предпросмотр результата и список готовых измерений. Не зависит от WPF — окно только передаёт
    /// координаты курсора в мм и рисует то, что здесь лежит.
    /// </summary>
    public sealed class MeasureSession
    {
        private FlatSnapper _snapper;
        private int _nextNumber = 1;

        public MeasureMode Mode { get; private set; }

        /// <summary>Готовые измерения текущей развертки.</summary>
        public ObservableCollection<Measurement> Results { get; } = new ObservableCollection<Measurement>();

        /// <summary>Привязка под курсором (режим «Расстояние»).</summary>
        public SnapResult? Hover { get; private set; }

        /// <summary>Отрезок под курсором (режим «Угол»).</summary>
        public SegmentPick HoverLine { get; private set; }

        /// <summary>Первая точка расстояния, пока не указана вторая.</summary>
        public SnapResult? FirstPoint { get; private set; }

        /// <summary>Первый отрезок угла, пока не указан второй.</summary>
        public SegmentPick FirstLine { get; private set; }

        /// <summary>Результат, который получится при щелчке в текущей точке.</summary>
        public Measurement Preview { get; private set; }

        public bool HasDrawing => _snapper != null;

        /// <summary>Подсказка для строки состояния: что сделать дальше.</summary>
        public string Prompt
        {
            get
            {
                switch (Mode)
                {
                    case MeasureMode.Distance:
                        return FirstPoint == null
                            ? "Расстояние: укажите первую точку · Ctrl — без привязки · Esc — выход"
                            : "Расстояние: укажите вторую точку · Esc — отменить";
                    case MeasureMode.Angle:
                        return FirstLine == null
                            ? "Угол: выберите первый отрезок (сторону угла) · Esc — выход"
                            : "Угол: выберите второй отрезок · Esc — отменить";
                    default:
                        return string.Empty;
                }
            }
        }

        /// <summary>Новая развертка: прежние измерения к ней не относятся.</summary>
        public void SetDrawing(FlatDrawing drawing)
        {
            _snapper = drawing == null || drawing.IsEmpty ? null : new FlatSnapper(drawing);
            Clear();
        }

        public void SetMode(MeasureMode mode)
        {
            Mode = mode;
            Reset();
        }

        /// <param name="tolerance">Допуск привязки в мм; 0 — без привязки.</param>
        public void Move(Point2 cursor, double tolerance)
        {
            Hover = null;
            HoverLine = null;
            Preview = null;
            if (_snapper == null)
                return;

            switch (Mode)
            {
                case MeasureMode.Distance:
                {
                    var snap = _snapper.Snap(cursor, tolerance);
                    Hover = snap;
                    if (FirstPoint is SnapResult first && Geometry2.Distance(first.Point, snap.Point) > 1e-9)
                        Preview = new DistanceMeasurement(first, snap);
                    break;
                }
                case MeasureMode.Angle:
                {
                    var line = _snapper.PickLine(cursor, tolerance);
                    HoverLine = line;
                    if (FirstLine != null && line != null && line.Index != FirstLine.Index)
                        Preview = AngleMeasurement.Between(FirstLine, line);
                    break;
                }
            }
        }

        /// <summary>Курсор ушёл с чертежа: убрать привязку и предпросмотр.</summary>
        public void Leave()
        {
            Hover = null;
            HoverLine = null;
            Preview = null;
        }

        /// <summary>Щелчок по чертежу. Возвращает готовое измерение, если оно завершено этим щелчком.</summary>
        public Measurement Click(Point2 cursor, double tolerance)
        {
            if (_snapper == null)
                return null;

            Measurement done = null;
            switch (Mode)
            {
                case MeasureMode.Distance:
                {
                    var snap = _snapper.Snap(cursor, tolerance);
                    if (FirstPoint is SnapResult first)
                    {
                        if (Geometry2.Distance(first.Point, snap.Point) <= 1e-9)
                            break; // та же точка — ждём другую
                        done = new DistanceMeasurement(first, snap);
                        FirstPoint = null;
                    }
                    else
                    {
                        FirstPoint = snap;
                    }
                    break;
                }
                case MeasureMode.Angle:
                {
                    var line = _snapper.PickLine(cursor, tolerance);
                    if (line == null)
                        break; // мимо отрезков
                    if (FirstLine == null)
                        FirstLine = line;
                    else if (line.Index != FirstLine.Index)
                    {
                        done = AngleMeasurement.Between(FirstLine, line);
                        FirstLine = null;
                    }
                    break;
                }
            }

            if (done != null)
            {
                done.Number = _nextNumber++;
                Results.Add(done);
            }
            Move(cursor, tolerance);
            return done;
        }

        /// <summary>Отменяет начатое измерение. false — отменять нечего.</summary>
        public bool Cancel()
        {
            if (FirstPoint == null && FirstLine == null)
                return false;
            FirstPoint = null;
            FirstLine = null;
            Preview = null;
            return true;
        }

        public void Remove(Measurement measurement) => Results.Remove(measurement);

        /// <summary>Удаляет все измерения; нумерация начинается заново.</summary>
        public void Clear()
        {
            Results.Clear();
            _nextNumber = 1;
            Reset();
        }

        private void Reset()
        {
            FirstPoint = null;
            FirstLine = null;
            Preview = null;
            Hover = null;
            HoverLine = null;
        }
    }
}

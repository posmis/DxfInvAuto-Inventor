using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InvAuto.Core.Marking;
using InvAuto.Core.Measuring;
using InvAuto.Core.Preview;
using InvAuto.Framework.Ui;
using InvAuto.InventorApi;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>
    /// Просмотр развертки: масштаб колесом мыши вокруг курсора, перемещение мышью, «вписать»,
    /// координаты курсора в мм. Показывает развертку так, как она будет выгружена (с обработанными
    /// линиями гиба). Листает детали текущего списка, не закрывая окно.
    /// Измерения: расстояние между точками и угол между отрезками, с привязками к геометрии DXF.
    /// </summary>
    public partial class FlatViewerWindow : Window, IFlatDragHandler
    {
        private const string MarkingHint =
            "Перетащите текст или щёлкните, куда его поставить · R — повернуть · «Авто» — автоматическое место · Esc — выйти";

        /// <summary>Радиус захвата привязки, px экрана.</summary>
        private const double SnapPixels = 10;

        private const string NavigationHint =
            "Колесо мыши — масштаб · левая или средняя кнопка — перемещение · двойной щелчок — вписать";

        private readonly MeasureSession _measure = new MeasureSession();
        private readonly MeasureOverlay _overlay;
        private readonly IReadOnlyList<PartRow> _rows;
        private readonly Func<PartRow, Task<bool>> _ensureFlat;
        private readonly Func<PartRow, string, bool> _isOwnLine;
        private readonly Action<PartRow, MarkingPosition> _setMarkingPosition;
        private bool _markingMode;
        private object _markingToolTip;
        private MarkingArea _markingArea;
        private FlatText _draft;
        private double _dragDx, _dragDy;
        private int _index;
        private int _loadToken;

        /// <param name="rows">Детали, которые можно листать (текущий список).</param>
        /// <param name="index">С какой начать.</param>
        /// <param name="ensureFlat">Сформировать развертку, если её ещё нет; false — не получилось.</param>
        /// <param name="collapsedSections">Свёрнутые группы таблицы свойств — общие с окном деталей.</param>
        /// <param name="isOwnLine">Есть ли у линии гиба детали свои параметры: (деталь, ключ линии).</param>
        /// <param name="setMarkingPosition">Сохранить место маркировки детали (null — автоматически); null — без ручной маркировки.</param>
        public FlatViewerWindow(IReadOnlyList<PartRow> rows, int index, Func<PartRow, Task<bool>> ensureFlat,
            ISet<string> collapsedSections = null, Func<PartRow, string, bool> isOwnLine = null,
            Action<PartRow, MarkingPosition> setMarkingPosition = null)
        {
            InitializeComponent();
            _rows = rows;
            _ensureFlat = ensureFlat;
            _isOwnLine = isOwnLine;
            _setMarkingPosition = setMarkingPosition;
            View.DragHandler = this;
            _markingToolTip = MarkingTool.ToolTip;
            if (collapsedSections != null)
                PropertiesTable.CollapsedSections = collapsedSections;
            _overlay = new MeasureOverlay(_measure, View);
            View.Overlay = _overlay;

            // Замечания пересчитываются в окне деталей после формирования развертки — показываем свежие.
            foreach (var r in rows)
                r.PropertyChanged += OnRowChanged;
            Closed += (s, e) =>
            {
                foreach (var r in _rows)
                    r.PropertyChanged -= OnRowChanged;
            };
            MeasureList.ItemsSource = _measure.Results;
            _measure.Results.CollectionChanged += (s, e) => UpdateMeasureState();
            UpdateMeasureState();
            UpdateHint();
            Loaded += async (s, e) => await ShowPartAsync(Math.Max(0, Math.Min(index, rows.Count - 1)));
        }

        private PartRow Current => _rows.Count > 0 ? _rows[_index] : null;

        private void OnRowChanged(object sender, System.ComponentModel.PropertyChangedEventArgs e)
        {
            if (sender != Current)
                return;
            if (e.PropertyName == nameof(PartRow.Flat) && Current.Flat != null && !Current.Flat.IsEmpty && EmptyText.Visibility != Visibility.Visible)
            {
                // Развертка перерисована (например, после переноса маркировки) — показываем новую, не сбрасывая вид.
                _draft = null;
                ShowFlat(Current);
                FillProperties(Current);
                return;
            }
            if (e.PropertyName != nameof(PartRow.Issues))
                return;
            _overlay.Issues = Current.Issues;
            FillProperties(Current);
            View.InvalidateVisual();
        }

        private async Task ShowPartAsync(int index)
        {
            if (_rows.Count == 0)
                return;
            _index = index;
            int token = ++_loadToken;
            var row = Current;

            PartTitle.Text = row.PartNumber.Length > 0 ? $"{row.PartNumber} — {row.FileName}" : row.FileName;
            PositionText.Text = $"Деталь {_index + 1} из {_rows.Count}";
            PrevButton.IsEnabled = _index > 0;
            NextButton.IsEnabled = _index < _rows.Count - 1;
            FillProperties(row);

            if (row.HasFlatPattern && row.FlatPreviewState != FlatPreviewState.Ready)
            {
                ShowMessage("Формирую развертку…");
                bool ok = await _ensureFlat(row);
                if (token != _loadToken)
                    return; // пока формировали, пользователь перешёл к другой детали
                if (!ok)
                {
                    ShowMessage("Не удалось сформировать развертку: " + row.FlatPreviewError);
                    return;
                }
            }

            if (!row.HasFlatPattern)
            {
                ShowMessage("У детали нет развертки");
                return;
            }
            if (row.Flat == null || row.Flat.IsEmpty)
            {
                ShowMessage(row.FlatPreviewError.Length > 0 ? row.FlatPreviewError : "Развертка пуста");
                return;
            }

            EmptyText.Visibility = Visibility.Collapsed;
            _draft = null;
            ShowFlat(row);
            _measure.SetDrawing(row.Flat); // измерения относятся к одной детали
            _overlay.Issues = row.Issues;   // места замечаний проверки
            ApplyLabels();
            FillProperties(row); // после формирования появились площадь, масса, рез и линии гиба
            View.Fit();
        }

        /// <summary>Развертка строки и всё, что зависит от маркировки: подсветка конфликта, доступность инструмента.</summary>
        private void ShowFlat(PartRow row)
        {
            View.Drawing = row.Flat;
            View.MarkingConflict = row.Marking?.Conflict == true;
            _markingArea = null;
            UpdateMarkingTool();
        }

        private void ShowMessage(string text)
        {
            View.Drawing = null;
            View.MarkingConflict = false;
            _markingArea = null;
            UpdateMarkingTool();
            _measure.SetDrawing(null);
            View.Labels = null;
            EmptyText.Text = text;
            EmptyText.Visibility = Visibility.Visible;
            UpdateZoomText();
        }

        private void ApplyLabels()
        {
            var row = Current;
            View.Labels = NumbersToggle.IsChecked == true && row != null
                ? row.BendLines.Select(b => new FlatLabel(
                        new Point2((b.Line.X1 + b.Line.X2) / 2, (b.Line.Y1 + b.Line.Y2) / 2),
                        b.Line.Index.ToString(CultureInfo.InvariantCulture)))
                    .ToList()
                : null;
        }

        /// <summary>Таблица свойств — та же, что в панели «Свойства» окна деталей.</summary>
        private void FillProperties(PartRow row) =>
            PartDetails.Fill(PropertiesTable, row, _isOwnLine == null ? null : (Func<string, bool>)(key => _isOwnLine(row, key)));

        #region Масштаб и курсор

        private void OnViewChanged(object sender, EventArgs e) => UpdateZoomText();

        private void UpdateZoomText() =>
            ZoomText.Text = View.Drawing == null ? string.Empty : $"Масштаб {View.Zoom * 100:0} %";

        private void OnCursorMoved(object sender, Point2? position)
        {
            if (!(position is Point2 p))
            {
                CursorText.Text = string.Empty;
                _measure.Leave();
                View.InvalidateVisual();
                return;
            }

            if (_measure.Mode == MeasureMode.None)
            {
                CursorText.Text = Coordinates(p);
                return;
            }

            _measure.Move(p, SnapTolerance);
            CursorText.Text = _measure.Hover is SnapResult hover
                ? Coordinates(hover.Point) + " · " + hover.KindName
                : Coordinates(p);
            View.InvalidateVisual();
        }

        private static string Coordinates(Point2 p) =>
            string.Format(CultureInfo.CurrentCulture, "X {0:0.0}   Y {1:0.0} мм", p.X, p.Y);

        private void OnFit(object sender, RoutedEventArgs e) => View.Fit();

        private void OnZoomIn(object sender, RoutedEventArgs e) => View.ZoomBy(1.25);

        private void OnZoomOut(object sender, RoutedEventArgs e) => View.ZoomBy(0.8);

        private void OnNumbersToggle(object sender, RoutedEventArgs e) => ApplyLabels();

        #endregion

        #region Измерения

        /// <summary>Допуск привязки в мм; Ctrl при измерении расстояния — без привязки.</summary>
        private double SnapTolerance =>
            _measure.Mode == MeasureMode.Distance && (Keyboard.Modifiers & ModifierKeys.Control) != 0
                ? 0
                : SnapPixels / Math.Max(View.PixelsPerMm, 1e-9);

        private void OnDistanceTool(object sender, RoutedEventArgs e) =>
            SetMeasureMode(DistanceTool.IsChecked == true ? MeasureMode.Distance : MeasureMode.None);

        private void OnAngleTool(object sender, RoutedEventArgs e) =>
            SetMeasureMode(AngleTool.IsChecked == true ? MeasureMode.Angle : MeasureMode.None);

        private void SetMeasureMode(MeasureMode mode)
        {
            if (mode != MeasureMode.None && _markingMode)
                SetMarkingMode(false);
            _measure.SetMode(mode);
            DistanceTool.IsChecked = mode == MeasureMode.Distance;
            AngleTool.IsChecked = mode == MeasureMode.Angle;
            View.DoubleClickFits = mode == MeasureMode.None; // при измерении двойной щелчок — это две точки
            UpdateHint();
            View.InvalidateVisual();
        }

        private void OnModelClicked(object sender, Point2 position)
        {
            if (_markingMode)
            {
                // Щелчок — поставить текст центром в эту точку.
                var text = CurrentMarkingText();
                if (text != null)
                    CommitMarking(text.Vertical, position.X, position.Y);
                return;
            }
            if (_measure.Mode == MeasureMode.None)
                return;
            _measure.Click(position, SnapTolerance);
            UpdateHint();
            View.InvalidateVisual();
        }

        private void OnClearMeasures(object sender, RoutedEventArgs e)
        {
            _measure.Clear();
            UpdateHint();
            View.InvalidateVisual();
        }

        private void OnRemoveMeasure(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.Tag is Measurement measurement)
            {
                _measure.Remove(measurement);
                View.InvalidateVisual();
            }
        }

        private void OnCopyMeasures(object sender, RoutedEventArgs e)
        {
            var text = new StringBuilder();
            text.AppendLine(PartTitle.Text);
            foreach (var m in _measure.Results)
                text.Append(m.Number.ToString(CultureInfo.CurrentCulture)).Append(") ").AppendLine(m.Text);
            try
            {
                Clipboard.SetText(text.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось скопировать: " + ex.Message, "InvAuto", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void UpdateMeasureState()
        {
            bool any = _measure.Results.Count > 0;
            NoMeasuresText.Visibility = any ? Visibility.Collapsed : Visibility.Visible;
            ClearMeasuresButton.IsEnabled = any;
            CopyMeasuresButton.IsEnabled = any;
        }

        private void UpdateHint() =>
            HintText.Text = _markingMode ? MarkingHint : _measure.Mode == MeasureMode.None ? NavigationHint : _measure.Prompt;

        #endregion

        #region Маркировка

        /// <summary>Маркировку можно двигать: она включена у детали, есть обозначение и развертка.</summary>
        private bool CanMoveMarking
        {
            get
            {
                var row = Current;
                return _setMarkingPosition != null && row != null && View.Drawing != null &&
                       row.MarkingOptions.Enabled && row.PartNumber.Trim().Length > 0 &&
                       (row.Marking?.Status == MarkingStatus.Placed || row.Marking?.Status == MarkingStatus.NoRoom);
            }
        }

        private void UpdateMarkingTool()
        {
            bool can = CanMoveMarking;
            MarkingTool.IsEnabled = can;
            MarkingTool.ToolTip = can || _setMarkingPosition == null
                ? _markingToolTip
                : "Место маркировки вручную (M). Сейчас недоступно: маркировка детали выключена или нет обозначения.";
            if (!can && _markingMode)
                SetMarkingMode(false);
            MarkingAutoButton.IsEnabled = Current?.MarkingPosition != null;
        }

        private void OnMarkingTool(object sender, RoutedEventArgs e) => SetMarkingMode(MarkingTool.IsChecked == true);

        private void SetMarkingMode(bool on)
        {
            on = on && CanMoveMarking;
            _markingMode = on;
            MarkingTool.IsChecked = on;
            MarkingRotateButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            MarkingAutoButton.Visibility = on ? Visibility.Visible : Visibility.Collapsed;
            if (on && _measure.Mode != MeasureMode.None)
                SetMeasureMode(MeasureMode.None);
            View.DoubleClickFits = !on && _measure.Mode == MeasureMode.None;
            UpdateHint();
        }

        /// <summary>Текст маркировки как он сейчас стоит (или в черновике при перетаскивании); null — не поставлен.</summary>
        private FlatText CurrentMarkingText()
        {
            var row = Current;
            if (_draft != null)
                return _draft;
            if (row?.Marking?.Text != null)
                return row.Marking.Text;
            // Не поместился автоматически — начинаем с центра детали, полной высоты.
            if (row?.Marking?.Status == MarkingStatus.NoRoom && row.Flat != null)
            {
                var c = Area().Center;
                return DxfMarking.At(row.PartNumber, c.X, c.Y, row.MarkingOptions.Normalized().Height, false);
            }
            return null;
        }

        private MarkingArea Area() => _markingArea ?? (_markingArea = new MarkingArea(Current?.Flat));

        bool IFlatDragHandler.BeginDrag(Point2 at)
        {
            if (!_markingMode)
                return false;
            var text = CurrentMarkingText();
            double tolerance = 4 / Math.Max(View.PixelsPerMm, 1e-9); // захват с запасом 4 px
            if (text == null || at.X < text.MinX - tolerance || at.X > text.MaxX + tolerance ||
                at.Y < text.MinY - tolerance || at.Y > text.MaxY + tolerance)
                return false; // не по тексту — обычное перемещение вида
            _dragDx = (text.MinX + text.MaxX) / 2 - at.X;
            _dragDy = (text.MinY + text.MaxY) / 2 - at.Y;
            return true;
        }

        void IFlatDragHandler.Drag(Point2 at)
        {
            var text = CurrentMarkingText();
            if (text == null)
                return;
            ShowDraft(DxfMarking.At(text.Value, at.X + _dragDx, at.Y + _dragDy, text.Height, text.Vertical));
        }

        void IFlatDragHandler.EndDrag(Point2 at)
        {
            var text = CurrentMarkingText();
            if (text != null)
                CommitMarking(text.Vertical, at.X + _dragDx, at.Y + _dragDy);
        }

        /// <summary>Черновик при перетаскивании: развертка без текста + текст на новом месте, красный — если не годится.</summary>
        private void ShowDraft(FlatText draft)
        {
            var row = Current;
            if (row?.Flat == null)
                return;
            _draft = draft;
            View.Drawing = row.Flat.WithoutTexts().WithText(draft);
            bool fits = Area().Fits(draft, row.MarkingOptions.Normalized().Margin);
            View.MarkingConflict = !fits;
            CursorText.Text = fits ? "Маркировка: место подходит" : "Маркировка: задевает контур или в вырезе";
        }

        /// <summary>Сохранить место (центр текста) — деталь перерисуется с текстом там же, где и при выгрузке.</summary>
        private void CommitMarking(bool vertical, double cx, double cy)
        {
            var row = Current;
            if (row == null || _setMarkingPosition == null)
                return;
            _setMarkingPosition(row, new MarkingPosition { PartPath = row.FilePath, X = cx, Y = cy, Vertical = vertical });
            _draft = null;
            UpdateMarkingTool();
        }

        private void OnMarkingRotate(object sender, RoutedEventArgs e)
        {
            var text = CurrentMarkingText();
            if (!_markingMode || text == null)
                return;
            CommitMarking(!text.Vertical, (text.MinX + text.MaxX) / 2, (text.MinY + text.MaxY) / 2);
        }

        private void OnMarkingAuto(object sender, RoutedEventArgs e)
        {
            var row = Current;
            if (row == null || _setMarkingPosition == null || row.MarkingPosition == null)
                return;
            _setMarkingPosition(row, null);
            _draft = null;
            UpdateMarkingTool();
        }

        #endregion

        #region Листание

        private async void OnPrev(object sender, RoutedEventArgs e)
        {
            if (_index > 0)
                await ShowPartAsync(_index - 1);
        }

        private async void OnNext(object sender, RoutedEventArgs e)
        {
            if (_index < _rows.Count - 1)
                await ShowPartAsync(_index + 1);
        }

        private void OnKeyDown(object sender, KeyEventArgs e)
        {
            switch (e.Key)
            {
                case Key.PageUp:
                    OnPrev(sender, e);
                    break;
                case Key.PageDown:
                    OnNext(sender, e);
                    break;
                case Key.Home:
                    View.Fit();
                    break;
                case Key.Add:
                case Key.OemPlus:
                    View.ZoomBy(1.25);
                    break;
                case Key.Subtract:
                case Key.OemMinus:
                    View.ZoomBy(0.8);
                    break;
                case Key.D:
                    SetMeasureMode(_measure.Mode == MeasureMode.Distance ? MeasureMode.None : MeasureMode.Distance);
                    break;
                case Key.A:
                    SetMeasureMode(_measure.Mode == MeasureMode.Angle ? MeasureMode.None : MeasureMode.Angle);
                    break;
                case Key.M:
                    SetMarkingMode(!_markingMode);
                    break;
                case Key.R when _markingMode:
                    OnMarkingRotate(sender, e);
                    break;
                case Key.Escape:
                    // Сначала отменяется начатое измерение, затем выключается инструмент, затем закрывается окно.
                    if (_markingMode)
                        SetMarkingMode(false);
                    else if (_measure.Cancel())
                    {
                        UpdateHint();
                        View.InvalidateVisual();
                    }
                    else if (_measure.Mode != MeasureMode.None)
                        SetMeasureMode(MeasureMode.None);
                    else
                        Close();
                    break;
                default:
                    return;
            }
            e.Handled = true;
        }

        #endregion
    }
}

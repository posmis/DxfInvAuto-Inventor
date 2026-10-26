using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
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
    public partial class FlatViewerWindow : Window
    {
        /// <summary>Радиус захвата привязки, px экрана.</summary>
        private const double SnapPixels = 10;

        private const string NavigationHint =
            "Колесо мыши — масштаб · левая или средняя кнопка — перемещение · двойной щелчок — вписать";

        private readonly MeasureSession _measure = new MeasureSession();
        private readonly IReadOnlyList<PartRow> _rows;
        private readonly Func<PartRow, Task<bool>> _ensureFlat;
        private int _index;
        private int _loadToken;

        /// <param name="rows">Детали, которые можно листать (текущий список).</param>
        /// <param name="index">С какой начать.</param>
        /// <param name="ensureFlat">Сформировать развертку, если её ещё нет; false — не получилось.</param>
        public FlatViewerWindow(IReadOnlyList<PartRow> rows, int index, Func<PartRow, Task<bool>> ensureFlat)
        {
            InitializeComponent();
            _rows = rows;
            _ensureFlat = ensureFlat;
            View.Overlay = new MeasureOverlay(_measure, View);
            MeasureList.ItemsSource = _measure.Results;
            _measure.Results.CollectionChanged += (s, e) => UpdateMeasureState();
            UpdateMeasureState();
            UpdateHint();
            Loaded += async (s, e) => await ShowPartAsync(Math.Max(0, Math.Min(index, rows.Count - 1)));
        }

        private PartRow Current => _rows.Count > 0 ? _rows[_index] : null;

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
            View.Drawing = row.Flat;
            _measure.SetDrawing(row.Flat); // измерения относятся к одной детали
            ApplyLabels();
            FillBends(row);
            View.Fit();
        }

        private void ShowMessage(string text)
        {
            View.Drawing = null;
            _measure.SetDrawing(null);
            View.Labels = null;
            EmptyText.Text = text;
            EmptyText.Visibility = Visibility.Visible;
            BendList.ItemsSource = null;
            NoBendsText.Visibility = Visibility.Collapsed;
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

        private void FillBends(PartRow row)
        {
            BendList.ItemsSource = row.BendLines
                .Select(b => new
                {
                    Title = $"{b.Line.Index}. {(b.Line.IsDown ? "Вниз" : "Вверх")} · {b.Line.Length:0.#} мм",
                    Description = b.Layout.Description
                })
                .ToList();
            NoBendsText.Visibility = row.BendLines.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        }

        private void FillProperties(PartRow row)
        {
            PropertiesGrid.Children.Clear();
            PropertiesGrid.RowDefinitions.Clear();
            Add("Обозначение", row.PartNumber);
            Add("Наименование", row.Description);
            Add("Материал", row.Material);
            Add("Толщина", row.Thickness.ToString("0.##", CultureInfo.CurrentCulture) + " мм");
            Add("Габариты", row.FlatSizeText);
            Add("Количество", row.Quantity.ToString(CultureInfo.CurrentCulture));
            Add("Линии гиба", row.BendSummary);
            Add("Файл", row.FilePath);

            void Add(string name, string value)
            {
                if (string.IsNullOrEmpty(value))
                    return;
                int index = PropertiesGrid.RowDefinitions.Count;
                PropertiesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = name, Style = (Style)FindResource("PropertyName") };
                var text = new TextBlock { Text = value, Style = (Style)FindResource("PropertyValue") };
                Grid.SetRow(label, index);
                Grid.SetRow(text, index);
                Grid.SetColumn(text, 1);
                PropertiesGrid.Children.Add(label);
                PropertiesGrid.Children.Add(text);
            }
        }

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
            _measure.SetMode(mode);
            DistanceTool.IsChecked = mode == MeasureMode.Distance;
            AngleTool.IsChecked = mode == MeasureMode.Angle;
            View.DoubleClickFits = mode == MeasureMode.None; // при измерении двойной щелчок — это две точки
            UpdateHint();
            View.InvalidateVisual();
        }

        private void OnModelClicked(object sender, Point2 position)
        {
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
            HintText.Text = _measure.Mode == MeasureMode.None ? NavigationHint : _measure.Prompt;

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
                case Key.Escape:
                    // Сначала отменяется начатое измерение, затем выключается инструмент, затем закрывается окно.
                    if (_measure.Cancel())
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

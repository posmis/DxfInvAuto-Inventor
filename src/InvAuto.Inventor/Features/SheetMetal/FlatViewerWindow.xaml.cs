using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InvAuto.Core.Preview;
using InvAuto.Framework.Ui;
using InvAuto.InventorApi;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>
    /// Просмотр развертки: масштаб колесом мыши вокруг курсора, перемещение мышью, «вписать»,
    /// координаты курсора в мм. Показывает развертку так, как она будет выгружена (с обработанными
    /// линиями гиба). Листает детали текущего списка, не закрывая окно.
    /// </summary>
    public partial class FlatViewerWindow : Window
    {
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
            ApplyLabels();
            FillBends(row);
            View.Fit();
        }

        private void ShowMessage(string text)
        {
            View.Drawing = null;
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

        private void OnCursorMoved(object sender, Point2? position) =>
            CursorText.Text = position is Point2 p
                ? string.Format(CultureInfo.CurrentCulture, "X {0:0.0}   Y {1:0.0} мм", p.X, p.Y)
                : string.Empty;

        private void OnFit(object sender, RoutedEventArgs e) => View.Fit();

        private void OnZoomIn(object sender, RoutedEventArgs e) => View.ZoomBy(1.25);

        private void OnZoomOut(object sender, RoutedEventArgs e) => View.ZoomBy(0.8);

        private void OnNumbersToggle(object sender, RoutedEventArgs e) => ApplyLabels();

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
                case Key.Escape:
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

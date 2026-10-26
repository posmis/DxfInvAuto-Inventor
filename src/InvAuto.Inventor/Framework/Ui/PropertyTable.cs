using System;
using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;

namespace InvAuto.Framework.Ui
{
    /// <summary>Как выделить значение в таблице свойств.</summary>
    public enum PropertyKind
    {
        Normal,

        /// <summary>Второстепенное (подсказка, «нет данных»).</summary>
        Muted,

        /// <summary>Главный результат (масса, длина реза) — полужирным.</summary>
        Emphasis,

        Warning,
        Error
    }

    /// <summary>
    /// Таблица свойств в стиле панели Details в Ansys и Inventor: группы с заголовком, внутри строки
    /// «параметр | значение | единица» с линиями сетки. Группу можно свернуть щелчком по заголовку;
    /// свёрнутые группы запоминаются в CollapsedSections (общий набор, например, из настроек окна).
    /// Значения можно выделить и скопировать.
    /// Использование:
    ///   table.Clear();
    ///   table.Section("Развертка");
    ///   table.Row("Масса", "1,25", "кг", PropertyKind.Emphasis);
    /// </summary>
    public sealed class PropertyTable : Border
    {
        private readonly Grid _grid = new Grid();
        private readonly List<(string Section, UIElement Element)> _rows = new List<(string, UIElement)>();
        private readonly Dictionary<string, TextBlock> _chevrons = new Dictionary<string, TextBlock>();
        private string _section = string.Empty;

        public PropertyTable()
        {
            BorderThickness = new Thickness(1, 1, 1, 0);
            SnapsToDevicePixels = true;
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star), MinWidth = 90 });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1.3, GridUnitType.Star), MinWidth = 70 });
            _grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            Child = _grid;
            Loaded += (s, e) => BorderBrush = Brush("BorderBrush", Brushes.LightGray);
        }

        /// <summary>Ключи (названия) свёрнутых групп. Если не задан — группы не запоминаются.</summary>
        public ISet<string> CollapsedSections { get; set; } = new HashSet<string>();

        /// <summary>Пользователь свернул или развернул группу — можно сохранить настройки.</summary>
        public event EventHandler SectionToggled;

        public bool IsEmpty => _rows.Count == 0;

        public void Clear()
        {
            _grid.Children.Clear();
            _grid.RowDefinitions.Clear();
            _rows.Clear();
            _chevrons.Clear();
            _section = string.Empty;
        }

        /// <summary>Начинает группу. Строки до следующего Section относятся к ней.</summary>
        public void Section(string title)
        {
            _section = title;
            bool collapsed = CollapsedSections.Contains(title);

            var chevron = new TextBlock
            {
                Text = collapsed ? "" : "",
                FontFamily = TryFindResource("IconFont") as FontFamily ?? new FontFamily("Segoe MDL2 Assets"),
                FontSize = 9,
                Width = 14,
                VerticalAlignment = VerticalAlignment.Center,
                Foreground = Brush("MutedTextBrush", Brushes.Gray)
            };
            _chevrons[title] = chevron;

            var header = new Border
            {
                Background = Brush("PanelHeaderBrush", Brushes.WhiteSmoke),
                BorderBrush = Brush("BorderBrush", Brushes.LightGray),
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(6, 4, 6, 4),
                Cursor = Cursors.Hand,
                ToolTip = "Свернуть или развернуть группу",
                Child = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    Children =
                    {
                        chevron,
                        new TextBlock { Text = title, FontWeight = FontWeights.SemiBold, Foreground = Brush("TextBrush", Brushes.Black) }
                    }
                }
            };
            header.MouseLeftButtonUp += (s, e) => Toggle(title);
            Add(header, 0, 3, isHeader: true);
        }

        /// <summary>Строка «параметр | значение | единица». Пустое значение не выводится.</summary>
        public void Row(string name, string value, string unit = null, PropertyKind kind = PropertyKind.Normal, string toolTip = null)
        {
            if (string.IsNullOrEmpty(value))
                return;

            var line = Brush("GridLineBrush", Brushes.Gainsboro);
            var nameCell = new Border
            {
                BorderBrush = line,
                BorderThickness = new Thickness(0, 0, 1, 1),
                Padding = new Thickness(18, 3, 6, 3),
                Child = new TextBlock
                {
                    Text = name,
                    TextWrapping = TextWrapping.Wrap,
                    Foreground = Brush("MutedTextBrush", Brushes.Gray),
                    VerticalAlignment = VerticalAlignment.Center
                },
                ToolTip = toolTip
            };

            var valueBox = new TextBox
            {
                Text = value,
                IsReadOnly = true,
                BorderThickness = new Thickness(0),
                Background = Brushes.Transparent,
                Padding = new Thickness(0),
                MinHeight = 0,
                TextWrapping = TextWrapping.Wrap,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = toolTip
            };
            switch (kind)
            {
                case PropertyKind.Muted:
                    valueBox.Foreground = Brush("MutedTextBrush", Brushes.Gray);
                    break;
                case PropertyKind.Emphasis:
                    valueBox.FontWeight = FontWeights.SemiBold;
                    break;
                case PropertyKind.Warning:
                    valueBox.Foreground = Brush("WarningBrush", Brushes.DarkOrange);
                    break;
                case PropertyKind.Error:
                    valueBox.Foreground = Brush("ErrorBrush", Brushes.Red);
                    break;
            }

            var valueCell = new Border
            {
                BorderBrush = line,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(6, 3, 4, 3),
                Child = valueBox
            };
            var unitCell = new Border
            {
                BorderBrush = line,
                BorderThickness = new Thickness(0, 0, 0, 1),
                Padding = new Thickness(0, 3, 6, 3),
                Child = new TextBlock
                {
                    Text = unit ?? string.Empty,
                    Foreground = Brush("MutedTextBrush", Brushes.Gray),
                    VerticalAlignment = VerticalAlignment.Center
                }
            };

            int row = _grid.RowDefinitions.Count;
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Place(nameCell, row, 0, 1);
            Place(valueCell, row, 1, 1);
            Place(unitCell, row, 2, 1);
        }

        /// <summary>Строка с действием-ссылкой в последней колонке (например, «пропустить» у замечания).</summary>
        public void ActionRow(string name, string value, PropertyKind kind, string action, Action onClick, string toolTip = null)
        {
            Row(name, value, null, kind, toolTip);
            if (string.IsNullOrEmpty(value) || onClick == null)
                return;
            var button = new Button
            {
                Content = action,
                Style = TryFindResource("LinkButton") as Style,
                VerticalAlignment = VerticalAlignment.Center,
                ToolTip = toolTip
            };
            button.Click += (s, e) => onClick();
            var cell = (Border)_grid.Children[_grid.Children.Count - 1]; // ячейка единиц только что добавленной строки
            cell.Child = button;
        }

        private void Add(UIElement element, int column, int span, bool isHeader)
        {
            int row = _grid.RowDefinitions.Count;
            _grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);
            _grid.Children.Add(element);
            if (!isHeader)
                _rows.Add((_section, element));
        }

        private void Place(UIElement element, int row, int column, int span)
        {
            Grid.SetRow(element, row);
            Grid.SetColumn(element, column);
            Grid.SetColumnSpan(element, span);
            _grid.Children.Add(element);
            _rows.Add((_section, element));
            if (CollapsedSections.Contains(_section))
                element.Visibility = Visibility.Collapsed;
        }

        private void Toggle(string section)
        {
            bool collapse = !CollapsedSections.Contains(section);
            if (collapse)
                CollapsedSections.Add(section);
            else
                CollapsedSections.Remove(section);

            foreach (var (owner, element) in _rows)
                if (owner == section)
                    element.Visibility = collapse ? Visibility.Collapsed : Visibility.Visible;
            if (_chevrons.TryGetValue(section, out var chevron))
                chevron.Text = collapse ? "" : "";
            SectionToggled?.Invoke(this, EventArgs.Empty);
        }

        private Brush Brush(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;
    }
}

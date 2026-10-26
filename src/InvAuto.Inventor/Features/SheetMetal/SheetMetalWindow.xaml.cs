using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using InvAuto.Core.Bends;
using InvAuto.Core.Export;
using InvAuto.Core.Materials;
using InvAuto.Core.Preview;
using InvAuto.Core.Settings;
using InvAuto.Core.SheetMetal;
using InvAuto.Core.Sorting;
using InvAuto.Core.Structure;
using InvAuto.Features.Materials;
using InvAuto.Features.Service;
using InvAuto.Framework;
using InvAuto.InventorApi;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>
    /// Окно "Листовые детали": слева дерево состава изделия, справа листовые детали выбранного узла
    /// с превью, справа — свойства выбранной детали. Сверху лента команд в стиле Inventor.
    /// </summary>
    public partial class SheetMetalWindow : Window
    {
        public const string ExportSettingsKey = "SheetMetal.Export";
        public const string ViewSettingsKey = "SheetMetal.View";
        public const string BendSettingsKey = "SheetMetal.Bends";

        private readonly FeatureContext _context;
        private readonly Inventor.Document _document;
        // Не readonly: после сброса в «Настройках InvAuto» перечитываются из файла.
        private FlatPatternExportSettings _exportSettings;
        private SheetMetalViewSettings _viewSettings;
        private BendSettings _bendSettings;

        private readonly FlatTree<TreeRow> _tree = new FlatTree<TreeRow>();
        private readonly ObservableCollection<PartRow> _rows = new ObservableCollection<PartRow>();
        private readonly ICollectionView _rowsView;
        private readonly Dictionary<DataGridColumn, string> _columnIds = new Dictionary<DataGridColumn, string>();

        // Состояние, которое должно переживать переключение узлов дерева.
        // По умолчанию детали не отмечены: помним только отмеченные пользователем.
        private readonly HashSet<string> _checked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ExportResult> _results = new Dictionary<string, ExportResult>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ImageSource> _thumbnails = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FlatPreview> _flatPreviews = new Dictionary<string, FlatPreview>(StringComparer.OrdinalIgnoreCase);

        private AssemblyStructure _structure;
        private Dictionary<string, SheetMetalPart> _originals;

        /// <summary>Листовые детали изделия с количеством на всё изделие — из них собирается список.</summary>
        private Dictionary<string, SheetMetalPart> _productParts = new Dictionary<string, SheetMetalPart>(StringComparer.OrdinalIgnoreCase);

        /// <summary>Детали, которые пользователь добавил в список (средняя панель).</summary>
        private readonly WorkList _list = new WorkList();

        private DataGridColumn _checkColumn;
        private AppSettings _appSettings;

        // Библиотека материалов (отдельный файл, не очищается) и материалы, назначенные деталям вручную.
        private readonly MaterialLibraryStore _materialStore;
        private MaterialLibrary _library = new MaterialLibrary();
        private string _libraryError;
        private MaterialAssignments _assignments;
        private int _previewGeneration;
        private bool _busy;
        private bool _cancelRequested;
        private FlatPreviewService _flatService;
        private string _lastOutputFolder;

        public SheetMetalWindow(FeatureContext context, Inventor.Document document, AssemblyStructure structure)
        {
            InitializeComponent();

            _context = context;
            _document = document;
            _exportSettings = context.Settings.Load<FlatPatternExportSettings>(ExportSettingsKey);
            _viewSettings = context.Settings.Load<SheetMetalViewSettings>(ViewSettingsKey);
            _viewSettings.Normalize();
            _bendSettings = context.Settings.Load<BendSettings>(BendSettingsKey);

            Title = $"Листовые детали — {AssemblyName}";
            ApplyViewSettings();

            TreeGrid.ItemsSource = _tree.Rows;
            _rowsView = CollectionViewSource.GetDefaultView(_rows);
            _rowsView.Filter = FilterRow;
            PartsGrid.ItemsSource = _rowsView;
            ProductQuantityBox.Text = Math.Max(1, _exportSettings.QuantityMultiplier).ToString(CultureInfo.InvariantCulture);

            BuildPartColumns();
            _appSettings = LoadAppSettings();
            _materialStore = new MaterialLibraryStore(context.Settings.Folder, context.Log);
            LoadMaterials();
            CleanupFlatCache();
            LoadStructure(structure);
            RestoreSavedList();
            Closing += OnWindowClosing;

            // Необработанное исключение в обработчике окна ушло бы в диспетчер Inventor и могло уронить его.
            // Пока окно открыто, перехватываем, пишем в лог и показываем сообщение.
            Dispatcher.UnhandledException += OnDispatcherUnhandledException;
            Closed += (s, e) => Dispatcher.UnhandledException -= OnDispatcherUnhandledException;
        }

        private string AssemblyName => string.IsNullOrEmpty(_document.FullFileName)
            ? _document.DisplayName
            : Path.GetFileNameWithoutExtension(_document.FullFileName);

        private string DefaultOutputFolder
        {
            get
            {
                string dir = string.IsNullOrEmpty(_document.FullFileName) ? null : Path.GetDirectoryName(_document.FullFileName);
                if (string.IsNullOrEmpty(dir))
                    dir = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                return Path.Combine(dir, "DXF");
            }
        }

        private string OutputFolder =>
            string.IsNullOrWhiteSpace(_exportSettings.OutputFolder) ? DefaultOutputFolder : _exportSettings.OutputFolder;

        private int ProductQuantity => Math.Max(1, _exportSettings.QuantityMultiplier);

        #region Дерево состава

        private void LoadStructure(AssemblyStructure structure)
        {
            _structure = structure;
            _originals = new[] { structure.Root }.Concat(structure.Root.Descendants())
                .Where(n => n.SheetMetal != null)
                .GroupBy(n => n.FullFileName, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => g.First().SheetMetal, StringComparer.OrdinalIgnoreCase);

            _productParts = structure.Root.CollectSheetMetal()
                .ToDictionary(p => p.FullFileName, StringComparer.OrdinalIgnoreCase);

            _tree.Load(new TreeRow(structure.Root, 0, _list.Contains), expandLevels: 1);
            TreeGrid.SelectedItem = _tree.Root;

            // После обновления состава в списке остаются только детали, которые ещё есть в изделии.
            int gone = _list.Retain(_productParts.ContainsKey);
            RebuildRows();

            SetStatus(DefaultStatus() + (gone > 0 ? $" · убрано из списка (больше нет в изделии): {gone}" : string.Empty));
        }

        private string DefaultStatus()
        {
            int total = _originals.Count;
            int withoutFlat = _originals.Values.Count(p => !p.HasFlatPattern);
            string text = $"Листовых деталей в изделии: {total}";
            if (withoutFlat > 0)
                text += $", без развертки: {withoutFlat}";
            if (_list.Count == 0)
                text += " · двойной щелчок по узлу дерева добавляет его детали в список";
            return text;
        }

        private void OnTreeToggle(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TreeRow row)
                _tree.Toggle(row);
            e.Handled = true;
        }

        private void OnExpandAll(object sender, RoutedEventArgs e) => _tree.ExpandAll();

        private void OnCollapseAll(object sender, RoutedEventArgs e)
        {
            _tree.CollapseAll();
            if (!(TreeGrid.SelectedItem is TreeRow row) || !_tree.Rows.Contains(row))
                TreeGrid.SelectedItem = _tree.Root;
        }

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            if (_busy)
                return;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                _thumbnails.Clear();
                _flatPreviews.Clear();
                LoadStructure(AssemblyScanner.Scan(_document));
            }
            catch (Exception ex)
            {
                _context.Log.Error("Не удалось обновить состав", ex);
                ShowError("Не удалось обновить состав: " + ex.Message);
            }
            finally
            {
                Mouse.OverrideCursor = null;
            }
        }

        #endregion

        #region Список листовых деталей

        private PartRow CreateRow(SheetMetalPart part)
        {
            var row = new PartRow(part, ProductQuantity) { IsChecked = _checked.Contains(part.FullFileName) };
            ApplyBends(row);
            ApplyMaterial(row);
            if (_results.TryGetValue(part.FullFileName, out var result))
                row.ApplyResult(result);
            if (_thumbnails.TryGetValue(part.FullFileName, out var thumbnail))
            {
                row.Thumbnail = thumbnail;
                row.ThumbnailLoaded = true;
            }
            if (_flatPreviews.TryGetValue(part.FullFileName, out var preview))
                row.ApplyPreview(preview);
            row.PropertyChanged += OnRowPropertyChanged;
            return row;
        }

        /// <summary>Строки заново по списку (после обновления состава или выгрузки); выделение сохраняется.</summary>
        private void RebuildRows()
        {
            var selected = new HashSet<string>(SelectedRows.Select(r => r.Part.FullFileName), StringComparer.OrdinalIgnoreCase);
            foreach (var row in _rows)
                row.PropertyChanged -= OnRowPropertyChanged;
            _rows.Clear();

            foreach (string path in _list.Items)
                if (_productParts.TryGetValue(path, out var part))
                    _rows.Add(CreateRow(part));

            foreach (var row in _rows.Where(r => selected.Contains(r.Part.FullFileName)))
                PartsGrid.SelectedItems.Add(row);
            ListChanged();
            StartPreviewLoading();
        }

        /// <summary>Добавляет детали в конец списка; уже добавленные пропускаются. source — что добавляли, для сообщения.</summary>
        private void AddToList(IEnumerable<SheetMetalPart> parts, string source)
        {
            var known = parts.Where(p => _productParts.ContainsKey(p.FullFileName)).ToList();
            var (added, already) = _list.Add(known.Select(p => p.FullFileName));

            var newRows = added.Select(path => CreateRow(_productParts[path])).ToList();
            foreach (var row in newRows)
                _rows.Add(row);

            if (newRows.Count > 0)
            {
                PartsGrid.SelectedItems.Clear();
                foreach (var row in newRows)
                    PartsGrid.SelectedItems.Add(row);
                PartsGrid.ScrollIntoView(newRows[0]);
                StartPreviewLoading();
            }
            ListChanged();

            if (known.Count == 0)
                SetStatus($"В «{source}» нет листовых деталей");
            else if (newRows.Count == 0)
                SetStatus($"Детали из «{source}» уже в списке ({already})");
            else
                SetStatus($"Добавлено в список из «{source}»: {newRows.Count}" + (already > 0 ? $", уже были в списке: {already}" : string.Empty));
        }

        private void RemoveFromList(IReadOnlyList<PartRow> rows)
        {
            if (_busy || rows.Count == 0)
                return;

            // После удаления выделяется строка, стоявшая на месте первой удалённой, — как в проводнике.
            var before = VisibleRows.ToList();
            int index = rows.Select(r => before.IndexOf(r)).Where(i => i >= 0).DefaultIfEmpty(-1).Min();
            _list.Remove(rows.Select(r => r.Part.FullFileName));
            foreach (var row in rows)
            {
                row.PropertyChanged -= OnRowPropertyChanged;
                _checked.Remove(row.Part.FullFileName);
                _rows.Remove(row);
            }

            var visible = VisibleRows.ToList();
            if (visible.Count > 0 && index >= 0)
            {
                var next = visible[Math.Min(index, visible.Count - 1)];
                PartsGrid.SelectedItem = next;
                PartsGrid.ScrollIntoView(next);
            }
            ListChanged();
            SetStatus($"Удалено из списка: {rows.Count}");
        }

        private void ListChanged()
        {
            foreach (var node in _tree.Rows)
                node.RefreshListMark();
            PartsTitle.Text = "Список деталей";
            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateCounter();
            if (PartsGrid.SelectedItem == null)
                ShowDetails(null);
        }

        private List<TreeRow> SelectedTreeRows => TreeGrid.SelectedItems.Cast<TreeRow>().ToList();

        private void AddTreeNodes(IReadOnlyList<TreeRow> nodes)
        {
            if (_busy || nodes.Count == 0)
                return;
            var parts = nodes.SelectMany(n => n.Node.CollectSheetMetal()).ToList();
            AddToList(parts, nodes.Count == 1 ? nodes[0].Title : $"выделенные узлы ({nodes.Count})");
        }

        private void OnAddSelected(object sender, RoutedEventArgs e) => AddTreeNodes(SelectedTreeRows);

        private void OnAddAll(object sender, RoutedEventArgs e)
        {
            if (_busy || _structure == null)
                return;
            AddToList(_structure.Root.CollectSheetMetal(), "всё изделие");
        }

        private void OnRemoveSelected(object sender, RoutedEventArgs e) => RemoveFromList(SelectedRows);

        private void OnClearList(object sender, RoutedEventArgs e)
        {
            if (_busy || _rows.Count == 0)
                return;
            if (MessageBox.Show(this, $"Убрать из списка все детали ({_rows.Count})?\n\nФайлы деталей и выгруженные DXF не затрагиваются.",
                    "InvAuto", MessageBoxButton.YesNo, MessageBoxImage.Question, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            RemoveFromList(_rows.ToList());
            SetStatus("Список очищен");
        }

        private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Двойной щелчок по строке дерева (но не по стрелке раскрытия и не по заголовку) — добавить узел.
            var element = e.OriginalSource as DependencyObject;
            while (element != null && !(element is DataGridRow) && !(element is System.Windows.Controls.Primitives.ToggleButton) &&
                   !(element is System.Windows.Controls.Primitives.DataGridColumnHeader))
                element = VisualTreeHelper.GetParent(element);
            if (element is DataGridRow row && row.Item is TreeRow node)
            {
                AddTreeNodes(new[] { node });
                e.Handled = true;
            }
        }

        private void OnTreeKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter || e.Key == Key.Insert)
            {
                AddTreeNodes(SelectedTreeRows);
                e.Handled = true;
            }
        }

        private void OnWindowKeyDown(object sender, KeyEventArgs e)
        {
            // Ctrl+Shift+A — добавить все листовые детали изделия.
            if (e.Key == Key.A && Keyboard.Modifiers == (ModifierKeys.Control | ModifierKeys.Shift))
            {
                OnAddAll(sender, e);
                e.Handled = true;
            }
            // Ctrl+F — к поиску в списке, как в проводнике.
            else if (e.Key == Key.F && Keyboard.Modifiers == ModifierKeys.Control)
            {
                SearchBox.Focus();
                SearchBox.SelectAll();
                e.Handled = true;
            }
        }

        /// <summary>Esc в поиске — очистить фильтр и вернуться к списку.</summary>
        private void OnSearchKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape)
                return;
            SearchBox.Clear();
            PartsGrid.Focus();
            e.Handled = true;
        }

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PartRow.IsChecked))
                return;
            var row = (PartRow)sender;
            if (row.IsChecked)
                _checked.Add(row.Part.FullFileName);
            else
                _checked.Remove(row.Part.FullFileName);
            UpdateCounter();
        }

        private bool FilterRow(object item)
        {
            string text = SearchBox.Text.Trim();
            if (text.Length == 0)
                return true;
            var row = (PartRow)item;
            return new[] { row.FileName, row.PartNumber, row.Description, row.Material }
                .Concat(row.Part.CustomProperties.Values)
                .Any(v => v != null && v.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
        }

        private IEnumerable<PartRow> VisibleRows => _rowsView.Cast<PartRow>();

        private void OnSearchChanged(object sender, TextChangedEventArgs e)
        {
            _rowsView?.Refresh();
            UpdateCounter();
        }

        private void UpdateCounter()
        {
            int visible = VisibleRows.Count();
            string text = $"В списке {_rows.Count} · отмечено {_rows.Count(r => r.IsChecked)}";
            if (visible != _rows.Count)
                text += $" · показано {visible}";
            CounterText.Text = text;
        }

        private void SetChecked(IEnumerable<PartRow> rows, bool value)
        {
            foreach (var row in rows.ToList())
                row.IsChecked = value;
        }

        private List<PartRow> SelectedRows => PartsGrid.SelectedItems.Cast<PartRow>().ToList();

        private void OnCheckAll(object sender, RoutedEventArgs e) => SetChecked(VisibleRows, true);

        private void OnCheckNone(object sender, RoutedEventArgs e) => SetChecked(VisibleRows, false);

        private void OnCheckSelected(object sender, RoutedEventArgs e)
        {
            var selected = new HashSet<PartRow>(SelectedRows);
            foreach (var row in VisibleRows)
                row.IsChecked = selected.Contains(row);
        }

        private void OnCheckSelectedRows(object sender, RoutedEventArgs e) => SetChecked(SelectedRows, true);

        private void OnUncheckSelectedRows(object sender, RoutedEventArgs e) => SetChecked(SelectedRows, false);

        private void OnPartsKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Delete)
            {
                RemoveFromList(SelectedRows);
                e.Handled = true;
                return;
            }

            // Пробел переключает отметку выделенных строк, как в проводнике.
            if (e.Key != Key.Space)
                return;
            var selected = SelectedRows;
            if (selected.Count == 0)
                return;
            SetChecked(selected, !selected.All(r => r.IsChecked));
            e.Handled = true;
        }

        private void OnRevealPart(object sender, RoutedEventArgs e)
        {
            if (PartsGrid.SelectedItem is PartRow row)
                Reveal(row.Part.FullFileName);
        }

        private void OnRevealDxf(object sender, RoutedEventArgs e)
        {
            if (PartsGrid.SelectedItem is PartRow row && _results.TryGetValue(row.Part.FullFileName, out var result) && File.Exists(result.FilePath))
                Reveal(result.FilePath);
            else
                SetStatus("Эта деталь ещё не выгружалась");
        }

        private static void Reveal(string path) => Process.Start("explorer.exe", $"/select,\"{path}\"");

        #endregion

        #region Колонки

        private void BuildPartColumns()
        {
            PartsGrid.Columns.Clear();
            _columnIds.Clear();

            _checkColumn = new DataGridTemplateColumn
            {
                // Значок вместо пустого заголовка: видно, что это колонка отметки, и по ней можно сортировать.
                Header = new TextBlock
                {
                    Text = "\uE73E",
                    FontFamily = (FontFamily)FindResource("IconFont"),
                    FontSize = 11,
                    VerticalAlignment = VerticalAlignment.Center,
                    ToolTip = "Отметка для выгрузки (пробел — отметить выделенные строки)"
                },
                CellTemplate = (DataTemplate)FindResource("CheckCell"),
                Width = 42,
                CanUserReorder = false,
                CanUserResize = false,
                SortMemberPath = nameof(PartRow.IsChecked)
            };
            PartsGrid.Columns.Add(_checkColumn);

            foreach (string id in _viewSettings.Columns)
            {
                var column = CreateColumn(id);
                if (column == null)
                    continue;
                _columnIds[column] = id;
                PartsGrid.Columns.Add(column);
            }

            PartsGrid.RowHeight = _viewSettings.Columns.Any(PartColumns.IsImage) ? 56 : 26;
            ApplySort();
        }

        private DataGridColumn CreateColumn(string id)
        {
            if (id.StartsWith(PartColumns.PropertyPrefix, StringComparison.Ordinal))
            {
                string name = id.Substring(PartColumns.PropertyPrefix.Length);
                return new DataGridTextColumn
                {
                    Header = name,
                    Binding = new Binding(".") { Converter = CustomPropertyConverter.Instance, ConverterParameter = name },
                    Width = new DataGridLength(1, DataGridLengthUnitType.Star),
                    MinWidth = 60,
                    SortMemberPath = id,
                    ElementStyle = TextStyle(false)
                };
            }

            string header = PartColumns.All.FirstOrDefault(c => c.Id == id).Header;
            switch (id)
            {
                case PartColumns.Flat: return TemplateColumn(header, "FlatCell", 112, nameof(PartRow.HasFlatPattern));
                case PartColumns.Thumbnail: return TemplateColumn(header, "ThumbnailCell", 80, null);
                case PartColumns.FlatState: return TemplateColumn(header, "FlatStateCell", 120, nameof(PartRow.HasFlatPattern));
                case PartColumns.Status: return TemplateColumn(header, "StatusCell", new DataGridLength(1.2, DataGridLengthUnitType.Star), nameof(PartRow.Status));
                case PartColumns.FileName: return TextColumn(header, nameof(PartRow.FileName), Star(1.3));
                case PartColumns.PartNumber: return TextColumn(header, nameof(PartRow.PartNumber), Star(1.2));
                case PartColumns.Description: return TextColumn(header, nameof(PartRow.Description), Star(1.4));
                case PartColumns.Material: return TextColumn(header, nameof(PartRow.Material), Star(1));
                case PartColumns.FilePath: return TextColumn(header, nameof(PartRow.FilePath), Star(2));
                case PartColumns.Bends: return TextColumn(header, nameof(PartRow.BendSummary), Star(1.1));
                case PartColumns.LibraryMaterial: return TemplateColumn(header, "LibraryMaterialCell", Star(1.2), nameof(PartRow.LibraryMaterialText));
                case PartColumns.Area: return TextColumn(header, nameof(PartRow.AreaM2), 100, "0.####", right: true);
                case PartColumns.Mass: return TextColumn(header, nameof(PartRow.Mass), 90, "0.###", right: true);
                case PartColumns.TotalMass: return TextColumn(header, nameof(PartRow.TotalMass), 130, "0.##", right: true);
                case PartColumns.CutLength: return TextColumn(header, nameof(PartRow.CutLengthM), 110, "0.##", right: true);
                case PartColumns.Pierces: return TextColumn(header, nameof(PartRow.Pierces), 80, right: true);
                case PartColumns.Thickness: return TextColumn(header, nameof(PartRow.Thickness), 90, "0.##", right: true);
                case PartColumns.FlatSize: return TextColumn(header, nameof(PartRow.FlatSize), 150);
                case PartColumns.Quantity: return TextColumn(header, nameof(PartRow.Quantity), 90, right: true);
                case PartColumns.Total: return TextColumn(header, nameof(PartRow.Total), 110, right: true);
                default: return null;
            }
        }

        private static DataGridLength Star(double value) => new DataGridLength(value, DataGridLengthUnitType.Star);

        private DataGridColumn TemplateColumn(string header, string templateKey, DataGridLength width, string sortPath) =>
            new DataGridTemplateColumn
            {
                Header = header,
                CellTemplate = (DataTemplate)FindResource(templateKey),
                Width = width,
                MinWidth = 50,
                SortMemberPath = sortPath,
                CanUserSort = sortPath != null
            };

        private static DataGridColumn TextColumn(string header, string path, DataGridLength width, string format = null, bool right = false) =>
            new DataGridTextColumn
            {
                Header = header,
                Binding = new Binding(path) { StringFormat = format },
                Width = width,
                MinWidth = 50,
                ElementStyle = TextStyle(right)
            };

        private static Style TextStyle(bool right)
        {
            var style = new Style(typeof(TextBlock));
            style.Setters.Add(new Setter(TextBlock.TextTrimmingProperty, TextTrimming.CharacterEllipsis));
            style.Setters.Add(new Setter(VerticalAlignmentProperty, VerticalAlignment.Center));
            if (right)
                style.Setters.Add(new Setter(HorizontalAlignmentProperty, HorizontalAlignment.Right));
            return style;
        }

        #region Сортировка

        /// <summary>Значение, по которому сортируется колонка; null — колонка не сортируется.</summary>
        private static Func<PartRow, object> SortKey(string id)
        {
            if (string.IsNullOrEmpty(id))
                return null;
            if (id.StartsWith(PartColumns.PropertyPrefix, StringComparison.Ordinal))
            {
                string name = id.Substring(PartColumns.PropertyPrefix.Length);
                return r => r.Property(name);
            }
            switch (id)
            {
                case PartColumns.Checked: return r => r.IsChecked;
                case PartColumns.FileName: return r => r.FileName;
                case PartColumns.PartNumber: return r => r.PartNumber;
                case PartColumns.Description: return r => r.Description;
                case PartColumns.Material: return r => r.Material;
                case PartColumns.FilePath: return r => r.FilePath;
                case PartColumns.Bends: return r => r.BendSummary;
                case PartColumns.LibraryMaterial: return r => r.MaterialMissing ? null : r.LibraryMaterialText;
                case PartColumns.Area: return r => r.AreaM2;
                case PartColumns.Mass: return r => r.Mass;
                case PartColumns.TotalMass: return r => r.TotalMass;
                case PartColumns.CutLength: return r => r.CutLengthM;
                case PartColumns.Pierces: return r => r.Pierces;
                case PartColumns.Status: return r => r.Status;
                case PartColumns.Thickness: return r => r.Thickness;
                case PartColumns.Quantity: return r => r.Quantity;
                case PartColumns.Total: return r => r.Total;
                case PartColumns.Flat:
                case PartColumns.FlatState: return r => r.HasFlatPattern;
                // Габариты — по большей стороне, при равенстве — по меньшей.
                case PartColumns.FlatSize:
                    return r => r.Part.FlatLengthMm > 0
                        ? Math.Round(Math.Max(r.Part.FlatLengthMm, r.Part.FlatWidthMm), 3) * 1e6 + Math.Min(r.Part.FlatLengthMm, r.Part.FlatWidthMm)
                        : (object)null;
                default: return null;
            }
        }

        private static string SortTitle(string id)
        {
            if (id == PartColumns.Checked)
                return "Отметка";
            if (id.StartsWith(PartColumns.PropertyPrefix, StringComparison.Ordinal))
                return id.Substring(PartColumns.PropertyPrefix.Length);
            return PartColumns.All.FirstOrDefault(c => c.Id == id).Header ?? id;
        }

        private string ColumnId(DataGridColumn column) =>
            column == _checkColumn ? PartColumns.Checked : _columnIds.TryGetValue(column, out string id) ? id : null;

        /// <summary>Щелчок по заголовку: та же колонка — смена направления, другая — по возрастанию. Как в проводнике.</summary>
        private void OnPartsSorting(object sender, DataGridSortingEventArgs e)
        {
            e.Handled = true; // стандартная сортировка WPF не умеет «Деталь 2 раньше Деталь 10»
            string id = ColumnId(e.Column);
            if (SortKey(id) == null)
                return;
            SetSort(id, id == _viewSettings.SortColumn ? !_viewSettings.SortDescending : false);
        }

        private void SetSort(string id, bool descending)
        {
            _viewSettings.SortColumn = id ?? string.Empty;
            _viewSettings.SortDescending = descending;
            ApplySort();
            SaveViewSettings();
            if (PartsGrid.SelectedItem != null)
                PartsGrid.ScrollIntoView(PartsGrid.SelectedItem);
        }

        private void ApplySort()
        {
            string id = _viewSettings.SortColumn;
            var key = SortKey(id);
            if (_rowsView is ListCollectionView view)
                view.CustomSort = key == null ? null : new RowComparer(key, _viewSettings.SortDescending);

            foreach (var column in PartsGrid.Columns)
                column.SortDirection = key != null && ColumnId(column) == id
                    ? _viewSettings.SortDescending ? ListSortDirection.Descending : ListSortDirection.Ascending
                    : (ListSortDirection?)null;
        }

        /// <summary>Меню «Сортировка»: по какой колонке и в каком порядке — как «Сортировать» в проводнике.</summary>
        private void OnSortMenu(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = SortButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };
            string current = _viewSettings.SortColumn;

            var ids = PartColumns.All.Select(c => c.Id)
                .Where(id => SortKey(id) != null && id != PartColumns.Flat)
                .Concat(_viewSettings.Columns.Where(id => id.StartsWith(PartColumns.PropertyPrefix, StringComparison.Ordinal)))
                .Concat(new[] { PartColumns.Checked });
            foreach (string id in ids)
            {
                var item = new MenuItem { Header = SortTitle(id), IsCheckable = true, IsChecked = id == current };
                item.Click += (s, a) => SetSort(id, id == current && _viewSettings.SortDescending);
                menu.Items.Add(item);
            }

            menu.Items.Add(new Separator());
            bool sorted = SortKey(current) != null;
            var ascending = new MenuItem { Header = "По возрастанию", IsCheckable = true, IsEnabled = sorted, IsChecked = sorted && !_viewSettings.SortDescending };
            ascending.Click += (s, a) => SetSort(current, false);
            var descending = new MenuItem { Header = "По убыванию", IsCheckable = true, IsEnabled = sorted, IsChecked = sorted && _viewSettings.SortDescending };
            descending.Click += (s, a) => SetSort(current, true);
            menu.Items.Add(ascending);
            menu.Items.Add(descending);

            menu.Items.Add(new Separator());
            var none = new MenuItem { Header = "Без сортировки (порядок добавления)", IsCheckable = true, IsChecked = !sorted };
            none.Click += (s, a) => SetSort(string.Empty, false);
            menu.Items.Add(none);

            menu.IsOpen = true;
        }

        /// <summary>Сравнение строк по ключу колонки; при равенстве — по имени файла и пути, чтобы порядок был устойчивым.</summary>
        private sealed class RowComparer : System.Collections.IComparer
        {
            private readonly Func<PartRow, object> _key;
            private readonly bool _descending;

            public RowComparer(Func<PartRow, object> key, bool descending)
            {
                _key = key;
                _descending = descending;
            }

            public int Compare(object x, object y)
            {
                var a = (PartRow)x;
                var b = (PartRow)y;
                int result = SortValues.Compare(_key(a), _key(b), _descending);
                if (result == 0)
                    result = NaturalComparer.Instance.Compare(a.FileName, b.FileName);
                if (result == 0)
                    result = StringComparer.OrdinalIgnoreCase.Compare(a.FilePath, b.FilePath);
                return result;
            }
        }

        #endregion

        private void OnColumnReordered(object sender, DataGridColumnEventArgs e)
        {
            _viewSettings.Columns = PartsGrid.Columns
                .Where(c => _columnIds.ContainsKey(c))
                .OrderBy(c => c.DisplayIndex)
                .Select(c => _columnIds[c])
                .ToList();
        }

        private void OnColumns(object sender, RoutedEventArgs e)
        {
            var menu = new ContextMenu { PlacementTarget = ColumnsButton, Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom };

            foreach (var (id, header) in PartColumns.All)
                menu.Items.Add(ColumnMenuItem(id, header));

            menu.Items.Add(new Separator());
            var properties = new MenuItem { Header = "Свойства iProperties" };
            var names = _originals.Values
                .SelectMany(p => p.CustomProperties.Keys)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .OrderBy(n => n, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            foreach (string name in names)
                properties.Items.Add(ColumnMenuItem(PartColumns.PropertyPrefix + name, name));
            if (names.Count == 0)
                properties.Items.Add(new MenuItem { Header = "В деталях нет пользовательских свойств", IsEnabled = false });
            menu.Items.Add(properties);

            menu.Items.Add(new Separator());
            var reset = new MenuItem { Header = "Колонки по умолчанию" };
            reset.Click += (s, a) =>
            {
                _viewSettings.ResetColumns();
                ColumnsChanged();
            };
            menu.Items.Add(reset);

            menu.IsOpen = true;
        }

        private MenuItem ColumnMenuItem(string id, string header)
        {
            var item = new MenuItem
            {
                Header = header,
                IsCheckable = true,
                IsChecked = _viewSettings.Columns.Contains(id),
                StaysOpenOnClick = true
            };
            item.Click += (s, a) =>
            {
                if (item.IsChecked && !_viewSettings.Columns.Contains(id))
                    _viewSettings.Columns.Add(id);
                else
                    _viewSettings.Columns.Remove(id);
                ColumnsChanged();
            };
            return item;
        }

        private void ColumnsChanged()
        {
            BuildPartColumns();
            SaveViewSettings();
        }

        #endregion

        #region Превью и развертки

        /// <summary>
        /// Сервис разверток для просмотра. Создаётся по текущим настройкам выгрузки,
        /// чтобы развертка в окне совпадала с будущим DXF. null — если способ выгрузки не настроен.
        /// </summary>
        private FlatPreviewService FlatService
        {
            get
            {
                if (_flatService != null)
                    return _flatService;
                try
                {
                    var writer = FlatPatternWriters.Create(_context.App, _exportSettings);
                    _flatService = new FlatPreviewService(writer, new FlatPreviewCache(FlatPreviewService.DefaultCacheFolder));
                }
                catch (Exception ex)
                {
                    _context.Log.Error("Развертки недоступны", ex);
                    SetStatus("Развертки недоступны: " + ex.Message);
                }
                return _flatService;
            }
        }

        private AppSettings LoadAppSettings()
        {
            var settings = _context.Settings.Load<AppSettings>(AppSettings.Key);
            settings.Normalize();
            return settings;
        }

        private void CleanupFlatCache()
        {
            try
            {
                int removed = new FlatPreviewCache(FlatPreviewService.DefaultCacheFolder).Cleanup(TimeSpan.FromDays(_appSettings.CacheMaxAgeDays));
                if (removed > 0)
                    _context.Log.Info($"Удалено старых разверток из кэша: {removed}");
            }
            catch (Exception ex)
            {
                _context.Log.Warn("Не удалось очистить кэш разверток: " + ex.Message);
            }
        }

        /// <summary>
        /// Фоном подгружает миниатюры и уже сформированные развертки из кэша.
        /// Новые DXF здесь не создаются — только по кнопке «Сформировать развертки».
        /// </summary>
        private async void StartPreviewLoading()
        {
            int generation = ++_previewGeneration;
            var pending = _rows.Where(r => !r.ThumbnailLoaded || !r.FlatCacheChecked).ToList();
            foreach (var row in pending)
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
                if (generation != _previewGeneration)
                    return;
                LoadCachedPreview(row);
            }
        }

        private void LoadCachedPreview(PartRow row)
        {
            string key = row.Part.FullFileName;
            var document = _structure.SheetMetalDocuments[key];

            if (!row.ThumbnailLoaded)
            {
                if (!_thumbnails.TryGetValue(key, out var thumbnail))
                {
                    thumbnail = PreviewService.Thumbnail((Inventor.Document)document);
                    _thumbnails[key] = thumbnail;
                }
                row.Thumbnail = thumbnail;
                row.ThumbnailLoaded = true;
            }

            if (row.FlatCacheChecked)
                return;

            if (_flatPreviews.TryGetValue(key, out var preview))
            {
                row.ApplyPreview(preview);
            }
            else if (!row.HasFlatPattern)
            {
                row.ApplyPreview(FlatPreview.NoFlatPattern);
            }
            else if (FlatService is FlatPreviewService service)
            {
                preview = service.FromCache(document);
                if (preview.State == FlatPreviewState.Ready)
                    _flatPreviews[key] = preview;
                row.ApplyPreview(preview);
            }
            else
            {
                row.FlatCacheChecked = true;
            }
        }

        private async void OnGenerateFlats(object sender, RoutedEventArgs e)
        {
            var rows = _rows.Where(r => r.IsChecked).ToList();
            if (rows.Count == 0)
            {
                SetStatus("Нет отмеченных деталей — отметьте нужные галочкой или кнопками группы «Отметка»");
                return;
            }
            await GenerateFlatsAsync(rows, force: false);
        }

        private async void OnRegenerateSelected(object sender, RoutedEventArgs e) =>
            await GenerateFlatsAsync(SelectedRows, force: true);

        /// <summary>Формирует развертки (DXF в кэш) тем же способом, что и выгрузка, и показывает их.</summary>
        private async Task GenerateFlatsAsync(List<PartRow> rows, bool force)
        {
            if (_busy)
                return;

            var service = FlatService;
            if (service == null)
            {
                ShowError("Не удалось подготовить формирование разверток. Проверьте способ выгрузки в «Параметрах выгрузки».");
                return;
            }

            var todo = rows.Where(r => r.HasFlatPattern && (force || r.FlatPreviewState != FlatPreviewState.Ready)).ToList();
            foreach (var row in rows.Where(r => !r.HasFlatPattern))
                row.ApplyPreview(FlatPreview.NoFlatPattern);
            if (todo.Count == 0)
            {
                SetStatus("Развертки отмеченных деталей уже сформированы");
                return;
            }

            _previewGeneration++; // фоновая подгрузка не должна вклиниваться
            SetBusy(true, todo.Count);
            int ok = 0, failed = 0;
            bool screenUpdating = _context.App.ScreenUpdating;
            try
            {
                _context.App.ScreenUpdating = false;
                for (int i = 0; i < todo.Count && !_cancelRequested; i++)
                {
                    var row = todo[i];
                    SetStatus($"Формирование разверток {i + 1} из {todo.Count}: {row.FileName}");
                    await Dispatcher.Yield(DispatcherPriority.Background);

                    string key = row.Part.FullFileName;
                    FlatPreview preview;
                    try
                    {
                        preview = service.Generate(_structure.SheetMetalDocuments[key]);
                    }
                    catch (Exception ex)
                    {
                        _context.Log.Error($"Развертка {row.FileName}", ex);
                        preview = FlatPreview.Failed(ex.Message);
                    }

                    if (preview.State == FlatPreviewState.Ready)
                    {
                        _flatPreviews[key] = preview;
                        ok++;
                    }
                    else
                    {
                        _flatPreviews.Remove(key);
                        if (preview.State == FlatPreviewState.Failed)
                            failed++;
                    }
                    row.ApplyPreview(preview);
                    Progress.Value++;
                }
            }
            finally
            {
                _context.App.ScreenUpdating = screenUpdating;
                SetBusy(false, 0);
            }

            SetStatus($"Сформировано разверток: {ok}" +
                      (failed > 0 ? $", ошибок: {failed} (причина — в подсказке ячейки и в логе)" : string.Empty) +
                      (_cancelRequested ? " · остановлено" : string.Empty));
            if (PartsGrid.SelectedItem is PartRow selected)
                ShowDetails(selected);
            StartPreviewLoading();
        }

        /// <summary>Настройки выгрузки изменились — развертки в окне больше не соответствуют будущему DXF.</summary>
        private void ResetFlatPreviews()
        {
            _flatService = null;
            _flatPreviews.Clear();
            foreach (var row in _rows)
                row.FlatCacheChecked = false;
            StartPreviewLoading();
        }

        private void OnCancel(object sender, RoutedEventArgs e)
        {
            _cancelRequested = true;
            SetStatus("Останавливаю…");
        }

        #endregion

        #region Линии гиба

        /// <summary>Слои линий гиба: стандартные слои Inventor и переименованные в параметрах выгрузки.</summary>
        private IReadOnlyCollection<string> BendLayers =>
            DxfBendProcessor.DefaultLayers
                .Concat(new[] { _exportSettings.BendUpLayer, _exportSettings.BendDownLayer }.Where(l => !string.IsNullOrWhiteSpace(l)))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();

        private void ApplyBends(PartRow row)
        {
            string key = row.Part.FullFileName;
            row.ApplyBends(_bendSettings.Resolver(key), _bendSettings.For(key), _bendSettings.HasOverride(key),
                _bendSettings.LineOverrideCount(key), BendLayers);
        }

        private void SaveBendSettings()
        {
            _context.Settings.Save(BendSettingsKey, _bendSettings);
            foreach (var row in _rows)
                ApplyBends(row);
            if (PartsGrid.SelectedItem is PartRow selected)
                ShowDetails(selected);
        }

        /// <summary>Пример для расчёта в диалоге: первая выбранная деталь со сформированной разверткой.</summary>
        private (string Name, IReadOnlyList<double> Lines) BendSample(IEnumerable<PartRow> rows)
        {
            var list = rows.ToList();
            var withLines = list.FirstOrDefault(r => r.BendLines.Count > 0);
            if (withLines != null)
                return (withLines.FileName, withLines.BendLines.Select(l => l.Line.Length).ToList());
            return (list.FirstOrDefault()?.FileName, Array.Empty<double>());
        }

        private void OnBendDefaults(object sender, RoutedEventArgs e)
        {
            if (_busy)
                return;
            var (name, lines) = BendSample(SelectedRows.Count > 0 ? SelectedRows : _rows.ToList());
            var dialog = new BendSettingsWindow(_bendSettings.Default, _bendSettings.Default, personal: false, usesDefaults: false,
                "Общие настройки — для всех деталей без персональных настроек.", name, lines) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            _bendSettings.Default = dialog.Result;
            SaveBendSettings();
            SetStatus("Линии гиба по умолчанию: " + _bendSettings.Default.Summary);
        }

        private void OnBendSelected(object sender, RoutedEventArgs e)
        {
            if (_busy)
                return;
            var rows = SelectedRows;
            if (rows.Count == 0)
            {
                SetStatus("Выделите детали в списке (Ctrl/Shift + щелчок), чтобы задать им свои линии гиба");
                return;
            }

            var first = rows[0];
            bool usesDefaults = rows.All(r => !r.BendsPersonal);
            var (name, lines) = BendSample(rows);
            string scope = rows.Count == 1 ? $"Деталь «{first.FileName}»." : $"Выделено деталей: {rows.Count}.";
            int ownLines = rows.Sum(r => r.PersonalLineCount);
            if (ownLines > 0)
                scope += $" Линий со своими параметрами: {ownLines} — они остаются как есть (кнопка «По линиям»).";
            var dialog = new BendSettingsWindow(first.Bends, _bendSettings.Default, personal: true, usesDefaults,
                scope, name, lines) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            foreach (var row in rows)
            {
                if (dialog.Result == null)
                    _bendSettings.ClearOverride(row.Part.FullFileName);
                else
                    _bendSettings.SetOverride(row.Part.FullFileName, dialog.Result);
            }
            SaveBendSettings();
            SetStatus(dialog.Result == null
                ? $"Деталей на общих настройках линий гиба: {rows.Count}"
                : $"Свои линии гиба ({dialog.Result.Summary}) для деталей: {rows.Count}");
        }

        #region Просмотр развертки

        private void OnOpenViewer(object sender, RoutedEventArgs e) => OpenViewer(PartsGrid.SelectedItem as PartRow);

        private void OnPartsDoubleClick(object sender, MouseButtonEventArgs e)
        {
            // Только по строке: двойной щелчок по заголовку колонки — это подгонка ширины, а не просмотр.
            var element = e.OriginalSource as DependencyObject;
            while (element != null && !(element is DataGridRow) && !(element is System.Windows.Controls.Primitives.DataGridColumnHeader))
                element = System.Windows.Media.VisualTreeHelper.GetParent(element);
            if (element is DataGridRow row && row.Item is PartRow part)
            {
                OpenViewer(part);
                e.Handled = true;
            }
        }

        private void OnDetailsFlatClick(object sender, MouseButtonEventArgs e)
        {
            if (e.ClickCount == 2 && Details.DataContext is PartRow row)
                OpenViewer(row);
        }

        /// <summary>Окно просмотра: листает детали текущего списка (с учётом поиска), начиная с выбранной.</summary>
        private void OpenViewer(PartRow start)
        {
            if (_busy)
                return;
            var rows = VisibleRows.ToList();
            if (rows.Count == 0)
            {
                SetStatus("В списке нет деталей для просмотра");
                return;
            }
            int index = start == null ? 0 : Math.Max(0, rows.IndexOf(start));

            var viewer = new FlatViewerWindow(rows, index, async row =>
            {
                // Быстро листают, пока формируется предыдущая развертка, — дожидаемся её.
                while (_busy)
                    await Task.Delay(100);
                if (row.FlatPreviewState == FlatPreviewState.Ready)
                    return true;
                await GenerateFlatsAsync(new List<PartRow> { row }, force: false);
                return row.FlatPreviewState == FlatPreviewState.Ready;
            }, _collapsedDetails, (r, key) => _bendSettings.HasLineOverride(r.Part.FullFileName, key)) { Owner = this };
            viewer.ShowDialog();
        }

        #endregion

        /// <summary>Индивидуальная настройка линий гиба выделенной детали.</summary>
        private async void OnBendLines(object sender, RoutedEventArgs e)
        {
            if (_busy)
                return;
            if (!(PartsGrid.SelectedItem is PartRow row))
            {
                SetStatus("Выделите деталь в списке, чтобы настроить её линии гиба по отдельности");
                return;
            }
            if (!row.HasFlatPattern)
            {
                SetStatus($"У детали «{row.FileName}» нет развертки");
                return;
            }

            // Линии берутся из сформированной развертки — если её ещё нет, формируем.
            if (row.FlatPreviewState != FlatPreviewState.Ready)
            {
                await GenerateFlatsAsync(new List<PartRow> { row }, force: false);
                if (row.FlatPreviewState != FlatPreviewState.Ready)
                {
                    ShowError($"Не удалось сформировать развертку «{row.FileName}»: {row.FlatPreviewError}");
                    return;
                }
            }

            string key = row.Part.FullFileName;
            string partSummary = (_bendSettings.HasOverride(key) ? "своя — " : "общие — ") + _bendSettings.For(key).Summary;
            var dialog = new BendLinesWindow(row.FileName, row.Preview, _bendSettings.For(key), partSummary,
                _bendSettings.LineOverrides(key), BendLayers) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            _bendSettings.SetLineOverrides(key, dialog.Result);
            SaveBendSettings();
            SetStatus(dialog.Result.Count == 0
                ? $"Все линии гиба «{row.FileName}» — как у детали"
                : $"Своих линий гиба у «{row.FileName}»: {dialog.Result.Count}");
        }

        private void OnBendReset(object sender, RoutedEventArgs e)
        {
            var rows = SelectedRows.Where(r => r.BendsPersonal || r.PersonalLineCount > 0).ToList();
            if (rows.Count == 0)
            {
                SetStatus("У выделенных деталей нет своих настроек линий гиба");
                return;
            }
            foreach (var row in rows)
            {
                _bendSettings.ClearOverride(row.Part.FullFileName);
                _bendSettings.ClearLineOverrides(row.Part.FullFileName);
            }
            SaveBendSettings();
            SetStatus($"Возвращены общие настройки линий гиба для деталей: {rows.Count}");
        }

        #endregion

        #region Свойства детали

        private void OnPartSelectionChanged(object sender, SelectionChangedEventArgs e) =>
            ShowDetails(PartsGrid.SelectedItems.Count == 1 ? PartsGrid.SelectedItem as PartRow : null);

        private void ShowDetails(PartRow row)
        {
            Details.DataContext = row;
            DetailsHint.Visibility = row == null ? Visibility.Visible : Visibility.Collapsed;
            DetailsContent.Visibility = row == null ? Visibility.Collapsed : Visibility.Visible;
            if (row == null)
            {
                PropertiesTable.Clear();
                return;
            }

            if (_viewSettings.ShowDetails && !_busy)
                LoadCachedPreview(row);

            PartDetails.Fill(PropertiesTable, row, key => _bendSettings.HasLineOverride(row.Part.FullFileName, key));
        }


        private void OnToggleDetails(object sender, RoutedEventArgs e)
        {
            _viewSettings.ShowDetails = DetailsToggle.IsChecked == true;
            ApplyDetailsVisibility();
            if (_viewSettings.ShowDetails && PartsGrid.SelectedItem is PartRow row)
                ShowDetails(row);
        }

        private void ApplyDetailsVisibility()
        {
            bool show = _viewSettings.ShowDetails;
            DetailsToggle.IsChecked = show;
            DetailsPanel.Visibility = show ? Visibility.Visible : Visibility.Collapsed;
            DetailsSplitter.Visibility = DetailsPanel.Visibility;
            DetailsColumn.Width = show ? new GridLength(Math.Max(220, _viewSettings.DetailsWidth)) : new GridLength(0);
        }

        #endregion

        #region Выгрузка

        private async void OnExportChecked(object sender, RoutedEventArgs e)
        {
            var parts = _rows.Where(r => r.IsChecked).Select(r => r.Part).ToList();
            if (parts.Count == 0)
            {
                SetStatus("Нет отмеченных деталей — отметьте нужные галочкой или кнопками группы «Отметка»");
                return;
            }
            await ExportAsync(parts);
        }

        private async void OnExportAll(object sender, RoutedEventArgs e) =>
            await ExportAsync(_structure.Root.CollectSheetMetal());

        private async Task ExportAsync(IReadOnlyList<SheetMetalPart> parts)
        {
            if (_busy || parts.Count == 0)
                return;

            IFlatPatternWriter writer;
            try
            {
                writer = FlatPatternWriters.Create(_context.App, _exportSettings);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message + "\n\nПроверьте способ выгрузки в «Параметрах выгрузки».");
                return;
            }

            string folder = OutputFolder;
            var exporter = new FlatPatternExporter(_exportSettings, folder, AssemblyName, writer)
            {
                BendOptions = part => _bendSettings.Resolver(part.FullFileName),
                BendLayers = BendLayers,
                RawDxfWritten = (collected, raw) => RememberRawFlat(collected.Info.FullFileName, raw)
            };
            var results = new List<ExportResult>(parts.Count);
            bool flatsCreated = false;

            _previewGeneration++; // останавливаем загрузку превью на время выгрузки
            SetBusy(true, parts.Count);
            bool screenUpdating = _context.App.ScreenUpdating;
            try
            {
                _context.App.ScreenUpdating = false;
                foreach (var part in parts)
                {
                    if (_cancelRequested)
                        break;
                    SetStatus($"Выгрузка {results.Count + 1} из {parts.Count}: {part.Name}");
                    await Dispatcher.Yield(DispatcherPriority.Background);

                    var result = exporter.Export(_structure.ToCollected(part));
                    results.Add(result);
                    _results[part.FullFileName] = result;
                    Log(result);

                    if (result.Part.HasFlatPattern && _originals.TryGetValue(part.FullFileName, out var original) && !original.HasFlatPattern)
                    {
                        original.HasFlatPattern = true;
                        _flatPreviews.Remove(part.FullFileName);
                        flatsCreated = true;
                    }

                    var row = _rows.FirstOrDefault(r => string.Equals(r.Part.FullFileName, part.FullFileName, StringComparison.OrdinalIgnoreCase));
                    row?.ApplyResult(result);
                    Progress.Value++;
                }
            }
            finally
            {
                _context.App.ScreenUpdating = screenUpdating;
                SetBusy(false, 0);
            }

            _lastOutputFolder = folder;
            string report = WriteReport(results, folder);
            Summarize(results, folder, report);

            if (flatsCreated)
                RebuildRows();
            else
                StartPreviewLoading();

            if (_exportSettings.OpenFolderAfterExport && Directory.Exists(folder))
                Process.Start("explorer.exe", $"\"{folder}\"");
        }

        /// <summary>
        /// DXF, записанный транслятором при выгрузке, — это и есть развертка (до обработки линий гиба):
        /// кладём его в кэш, чтобы она сразу была видна в окне.
        /// </summary>
        private void RememberRawFlat(string key, string rawPath)
        {
            try
            {
                var preview = FlatService?.StoreRaw(_structure.SheetMetalDocuments[key], rawPath);
                if (preview == null)
                    return;
                _flatPreviews[key] = preview;
                RowFor(key)?.ApplyPreview(preview);
            }
            catch (Exception ex)
            {
                _context.Log.Warn($"Развертка {key} не сохранена в кэш: {ex.Message}");
            }
        }

        private PartRow RowFor(string key) =>
            _rows.FirstOrDefault(r => string.Equals(r.Part.FullFileName, key, StringComparison.OrdinalIgnoreCase));

        private string WriteReport(List<ExportResult> results, string folder)
        {
            if (!_exportSettings.WriteCsvReport)
                return null;
            try
            {
                Directory.CreateDirectory(folder);
                string path = Path.Combine(folder, $"Отчет_{AssemblyName}_{DateTime.Now.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture)}.csv");
                CsvReport.Write(path, results, _exportSettings.QuantityMultiplier);
                return path;
            }
            catch (Exception ex)
            {
                _context.Log.Error("Не удалось записать CSV-отчёт", ex);
                return null;
            }
        }

        private void Summarize(List<ExportResult> results, string folder, string report)
        {
            int ok = results.Count(r => r.Status == ExportStatus.Exported);
            int skipped = results.Count(r => r.Status == ExportStatus.Skipped);
            int failed = results.Count(r => r.Status == ExportStatus.Failed);

            SetStatus($"Выгружено {ok} из {results.Count}" +
                      (skipped > 0 ? $", пропущено {skipped}" : string.Empty) +
                      (failed > 0 ? $", ошибок {failed}" : string.Empty) +
                      $" · {folder}" +
                      (report != null ? $" · отчёт {Path.GetFileName(report)}" : string.Empty));

            if (failed > 0)
            {
                var lines = results.Where(r => r.Status == ExportStatus.Failed).Take(10)
                    .Select(r => $"• {r.Part.Name}: {r.Message}");
                ShowError($"Не удалось выгрузить деталей: {failed}\n\n{string.Join("\n", lines)}" +
                          (failed > 10 ? "\n…" : string.Empty));
            }
        }

        private void Log(ExportResult result)
        {
            string line = $"{result.Part.Name}: {CsvReport.StatusText(result.Status)} {result.FilePath} {result.Message}";
            if (result.Status == ExportStatus.Failed)
                _context.Log.Error(line);
            else
                _context.Log.Info(line);
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            string folder = _lastOutputFolder ?? OutputFolder;
            if (Directory.Exists(folder))
                Process.Start("explorer.exe", $"\"{folder}\"");
            else
                SetStatus($"Папка ещё не создана: {folder}");
        }

        private void OnProductQuantityKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                OnProductQuantityChanged(sender, e);
        }

        private void OnProductQuantityChanged(object sender, RoutedEventArgs e)
        {
            if (!int.TryParse(ProductQuantityBox.Text.Trim(), NumberStyles.Integer, CultureInfo.CurrentCulture, out int value) || value < 1 || value > 100000)
            {
                ProductQuantityBox.Text = ProductQuantity.ToString(CultureInfo.InvariantCulture);
                return;
            }
            if (value == _exportSettings.QuantityMultiplier)
                return;

            _exportSettings.QuantityMultiplier = value;
            _context.Settings.Save(ExportSettingsKey, _exportSettings);
            foreach (var row in _rows)
                row.Total = row.Quantity * value;
        }

        private void OnSettings(object sender, RoutedEventArgs e)
        {
            if (_busy)
                return;
            var sample = (PartsGrid.SelectedItem as PartRow)?.Part ?? _rows.FirstOrDefault()?.Part;
            var dialog = new ExportSettingsWindow(_exportSettings, DefaultOutputFolder, sample, AssemblyName) { Owner = this };
            if (dialog.ShowDialog() != true)
                return;

            _context.Settings.Save(ExportSettingsKey, _exportSettings);
            foreach (var row in _rows)
                ApplyBends(row);
            ResetFlatPreviews();
        }

        private void OnAppSettings(object sender, RoutedEventArgs e)
        {
            if (_busy)
                return;
            var dialog = new AppSettingsWindow(_context) { Owner = this };
            dialog.ShowDialog();
            _appSettings = LoadAppSettings();

            var reset = dialog.ResetKeys;
            if (reset.Contains(MaterialAssignments.Key))
            {
                _assignments = _context.Settings.Load<MaterialAssignments>(MaterialAssignments.Key);
                ApplyMaterials();
            }
            if (reset.Contains(ExportSettingsKey))
            {
                _exportSettings = _context.Settings.Load<FlatPatternExportSettings>(ExportSettingsKey);
                ProductQuantityBox.Text = ProductQuantity.ToString(CultureInfo.InvariantCulture);
            }
            if (reset.Contains(BendSettingsKey))
                _bendSettings = _context.Settings.Load<BendSettings>(BendSettingsKey);
            if (reset.Contains(ViewSettingsKey))
            {
                _viewSettings = _context.Settings.Load<SheetMetalViewSettings>(ViewSettingsKey);
                _viewSettings.Normalize();
                WindowState = WindowState.Normal;
                ApplyViewSettings();
                BuildPartColumns();
            }
            if (reset.Contains(ExportSettingsKey) || reset.Contains(BendSettingsKey))
            {
                foreach (var row in _rows)
                    ApplyBends(row);
            }

            // Развертки зависят от параметров выгрузки и лежат в кэше: после сброса или очистки перечитываем.
            if (dialog.CacheCleared || reset.Contains(ExportSettingsKey))
                ResetFlatPreviews();
            else if (reset.Contains(BendSettingsKey) && PartsGrid.SelectedItem is PartRow selected)
                ShowDetails(selected);

            if (dialog.CacheCleared)
                SetStatus("Кэш разверток очищен — сформируйте развертки заново");
            else if (reset.Count > 0)
                SetStatus("Настройки сброшены к значениям по умолчанию");
        }

        #region Материалы

        private void LoadMaterials()
        {
            _assignments = _context.Settings.Load<MaterialAssignments>(MaterialAssignments.Key);
            try
            {
                _library = _materialStore.Load();
                _libraryError = null;
            }
            catch (Exception ex)
            {
                // Повреждённый файл не перезаписываем: работаем без библиотеки и сообщаем пользователю.
                _context.Log.Error("Библиотека материалов недоступна", ex);
                _library = new MaterialLibrary();
                _libraryError = ex.Message;
                Dispatcher.BeginInvoke(new Action(() => ShowError(ex.Message)), DispatcherPriority.Background);
            }
        }

        private void ApplyMaterial(PartRow row) =>
            row.ApplyMaterial(ResolvedMaterial.Resolve(_library, _assignments, row.Part.FullFileName, row.Thickness));

        private void ApplyMaterials()
        {
            foreach (var row in _rows)
                ApplyMaterial(row);
            if (PartsGrid.SelectedItem is PartRow selected)
                ShowDetails(selected);
        }

        private void OnMaterialLibrary(object sender, RoutedEventArgs e)
        {
            if (_busy)
                return;
            if (_libraryError != null)
            {
                ShowError(_libraryError);
                return;
            }
            var dialog = new MaterialLibraryWindow(_library, _materialStore, _context.Log) { Owner = this };
            if (dialog.ShowDialog() != true || dialog.Result == null)
                return;
            _library = dialog.Result;
            ApplyMaterials();
            SetStatus($"Библиотека материалов сохранена: {_library.Materials.Count}");
        }

        private void OnAssignMaterial(object sender, RoutedEventArgs e)
        {
            var rows = SelectedRows;
            if (_busy || rows.Count == 0)
            {
                SetStatus("Выделите детали в списке, чтобы назначить им материал");
                return;
            }
            if (_libraryError != null)
            {
                ShowError(_libraryError);
                return;
            }

            var ids = rows.Select(r => _assignments.For(r.Part.FullFileName)).Distinct().ToList();
            var dialog = new MaterialPickerWindow(_library, rows.Select(r => r.Thickness).ToList(), rows.Count, ids.Count == 1 ? ids[0] : null)
            {
                Owner = this
            };
            if (dialog.ShowDialog() != true)
                return;
            SetMaterial(rows, dialog.SelectedId);
        }

        private void OnMaterialByThickness(object sender, RoutedEventArgs e)
        {
            var rows = SelectedRows;
            if (_busy || rows.Count == 0)
            {
                SetStatus("Выделите детали в списке");
                return;
            }
            SetMaterial(rows, null);
        }

        private void SetMaterial(IReadOnlyList<PartRow> rows, string materialId)
        {
            foreach (var row in rows)
                _assignments.Set(row.Part.FullFileName, materialId);
            try
            {
                _context.Settings.Save(MaterialAssignments.Key, _assignments);
            }
            catch (Exception ex)
            {
                _context.Log.Error("Не удалось сохранить материалы деталей", ex);
                ShowError("Не удалось сохранить материалы деталей: " + ex.Message);
            }
            foreach (var row in rows)
                ApplyMaterial(row);
            if (PartsGrid.SelectedItem is PartRow selected)
                ShowDetails(selected);

            var material = _library.Find(materialId);
            SetStatus(material == null
                ? $"Материал по толщине: {rows.Count} дет."
                : $"Назначен материал «{material.Name}»: {rows.Count} дет.");
        }

        #endregion

        private void SetBusy(bool busy, int steps)
        {
            _busy = busy;
            if (busy)
                _cancelRequested = false; // сбрасываем в начале операции, чтобы итог мог сообщить «остановлено»
            CancelButton.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Mouse.OverrideCursor = busy ? Cursors.Wait : null;
            Body.IsEnabled = !busy;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Progress.Maximum = Math.Max(1, steps);
            Progress.Value = 0;
        }

        #endregion

        #region Окно и настройки вида

        /// <summary>Свёрнутые группы таблицы свойств — общие для панели и окна просмотра.</summary>
        private readonly HashSet<string> _collapsedDetails = new HashSet<string>(StringComparer.Ordinal);

        private void ApplyViewSettings()
        {
            _collapsedDetails.Clear();
            _collapsedDetails.UnionWith(_viewSettings.CollapsedDetails ?? new List<string>());
            PropertiesTable.CollapsedSections = _collapsedDetails;

            RibbonTabs.SelectedIndex = Math.Max(0, Math.Min(RibbonTabs.Items.Count - 1, _viewSettings.RibbonTab));

            // Не больше рабочей области экрана: на ноутбуке окно не должно уходить за край.
            var area = SystemParameters.WorkArea;
            Width = Math.Max(MinWidth, Math.Min(area.Width, _viewSettings.WindowWidth));
            Height = Math.Max(MinHeight, Math.Min(area.Height, _viewSettings.WindowHeight));
            if (_viewSettings.Maximized)
                WindowState = WindowState.Maximized;
            TreeColumn.Width = new GridLength(Math.Max(220, _viewSettings.TreeWidth));
            ApplyDetailsVisibility();
        }

        private void SaveViewSettings()
        {
            try
            {
                _context.Settings.Save(ViewSettingsKey, _viewSettings);
            }
            catch (Exception ex)
            {
                _context.Log.Error("Не удалось сохранить настройки вида", ex);
            }
        }

        private void OnWindowClosing(object sender, CancelEventArgs e)
        {
            if (_busy)
            {
                e.Cancel = true;
                return;
            }

            _previewGeneration++;
            _viewSettings.Maximized = WindowState == WindowState.Maximized;
            if (WindowState == WindowState.Normal)
            {
                _viewSettings.WindowWidth = ActualWidth;
                _viewSettings.WindowHeight = ActualHeight;
            }
            _viewSettings.TreeWidth = TreeColumn.ActualWidth;
            if (_viewSettings.ShowDetails)
                _viewSettings.DetailsWidth = DetailsColumn.ActualWidth;
            _viewSettings.RibbonTab = RibbonTabs.SelectedIndex;
            _viewSettings.CollapsedDetails = _collapsedDetails.OrderBy(k => k, StringComparer.Ordinal).ToList();
            SaveViewSettings();
            SaveList();
        }

        #region Сохранение списка (настройка «Запоминать список деталей»)

        private string AssemblyPath => _document.FullFileName ?? string.Empty;

        /// <summary>Восстанавливает список этой сборки, если включено запоминание.</summary>
        private void RestoreSavedList()
        {
            if (!_appSettings.RememberPartLists || AssemblyPath.Length == 0)
                return;
            try
            {
                var saved = _context.Settings.Load<SavedPartLists>(SavedPartLists.Key).Get(AssemblyPath);
                var present = saved.Where(_productParts.ContainsKey).ToList();
                if (present.Count == 0)
                    return;
                _list.Add(present);
                RebuildRows();
                int missing = saved.Count - present.Count;
                SetStatus($"Восстановлен сохранённый список: {present.Count} дет." +
                          (missing > 0 ? $" Больше нет в сборке: {missing}." : string.Empty));
            }
            catch (Exception ex)
            {
                _context.Log.Warn("Не удалось восстановить список деталей: " + ex.Message);
            }
        }

        private void SaveList()
        {
            if (!_appSettings.RememberPartLists || AssemblyPath.Length == 0)
                return;
            try
            {
                var lists = _context.Settings.Load<SavedPartLists>(SavedPartLists.Key);
                lists.Set(AssemblyPath, _list.Items, DateTime.UtcNow);
                _context.Settings.Save(SavedPartLists.Key, lists);
            }
            catch (Exception ex)
            {
                _context.Log.Warn("Не удалось сохранить список деталей: " + ex.Message);
            }
        }

        #endregion

        private void OnDispatcherUnhandledException(object sender, DispatcherUnhandledExceptionEventArgs e)
        {
            e.Handled = true;
            _context.Log.Error("Необработанная ошибка в окне «Листовые детали»", e.Exception);
            if (_busy)
                SetBusy(false, 0);
            ShowError("Произошла ошибка: " + e.Exception.Message + "\n\nПодробности в логе: %APPDATA%\\InvAuto\\logs");
        }

        private void SetStatus(string text) => StatusText.Text = text;

        private void ShowError(string text) =>
            MessageBox.Show(this, text, "InvAuto", MessageBoxButton.OK, MessageBoxImage.Warning);

        #endregion

        /// <summary>Значение пользовательского свойства для колонки "prop:Имя".</summary>
        private sealed class CustomPropertyConverter : IValueConverter
        {
            public static readonly CustomPropertyConverter Instance = new CustomPropertyConverter();

            public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
                (value as PartRow)?.Property((string)parameter) ?? string.Empty;

            public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) =>
                throw new NotSupportedException();
        }
    }
}

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
using InvAuto.Core.Export;
using InvAuto.Core.Preview;
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

        private readonly FeatureContext _context;
        private readonly Inventor.Document _document;
        private readonly FlatPatternExportSettings _exportSettings;
        private readonly SheetMetalViewSettings _viewSettings;

        private readonly TreeModel _tree = new TreeModel();
        private readonly ObservableCollection<PartRow> _rows = new ObservableCollection<PartRow>();
        private readonly ICollectionView _rowsView;
        private readonly Dictionary<DataGridColumn, string> _columnIds = new Dictionary<DataGridColumn, string>();

        // Состояние, которое должно переживать переключение узлов дерева.
        private readonly HashSet<string> _unchecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ExportResult> _results = new Dictionary<string, ExportResult>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, (ImageSource Thumbnail, FlatDrawing Flat)> _previews =
            new Dictionary<string, (ImageSource, FlatDrawing)>(StringComparer.OrdinalIgnoreCase);

        private AssemblyStructure _structure;
        private Dictionary<string, SheetMetalPart> _originals;
        private TreeRow _currentNode;
        private IFlatPatternWriter _previewWriter;
        private int _previewGeneration;
        private bool _busy;
        private string _lastOutputFolder;

        public SheetMetalWindow(FeatureContext context, Inventor.Document document, AssemblyStructure structure)
        {
            InitializeComponent();

            _context = context;
            _document = document;
            _exportSettings = context.Settings.Load<FlatPatternExportSettings>(ExportSettingsKey);
            _viewSettings = context.Settings.Load<SheetMetalViewSettings>(ViewSettingsKey);

            Title = $"Листовые детали — {AssemblyName}";
            ApplyViewSettings();

            TreeGrid.ItemsSource = _tree.Rows;
            _rowsView = CollectionViewSource.GetDefaultView(_rows);
            _rowsView.Filter = FilterRow;
            PartsGrid.ItemsSource = _rowsView;
            ProductQuantityBox.Text = Math.Max(1, _exportSettings.QuantityMultiplier).ToString(CultureInfo.InvariantCulture);

            BuildPartColumns();
            LoadStructure(structure);
            Closing += OnWindowClosing;
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

            _tree.Load(structure.Root, expandLevels: 2);
            TreeGrid.SelectedItem = _tree.Root;
            if (_currentNode != _tree.Root)
                ShowNode(_tree.Root);

            SetStatus(DefaultStatus());
        }

        private string DefaultStatus()
        {
            int total = _originals.Count;
            int withoutFlat = _originals.Values.Count(p => !p.HasFlatPattern);
            string text = $"Листовых деталей в изделии: {total}";
            if (withoutFlat > 0)
                text += $", без развертки: {withoutFlat}";
            return text;
        }

        private void OnTreeToggle(object sender, RoutedEventArgs e)
        {
            if ((sender as FrameworkElement)?.DataContext is TreeRow row)
                _tree.Toggle(row);
            e.Handled = true;
        }

        private void OnTreeSelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TreeGrid.SelectedItem is TreeRow row && row != _currentNode)
                ShowNode(row);
        }

        private void OnExpandAll(object sender, RoutedEventArgs e) => _tree.ExpandAll();

        private void OnCollapseAll(object sender, RoutedEventArgs e)
        {
            _tree.CollapseAll();
            if (!_tree.Rows.Contains(_currentNode))
                TreeGrid.SelectedItem = _tree.Root;
        }

        private void OnRefresh(object sender, RoutedEventArgs e)
        {
            if (_busy)
                return;
            try
            {
                Mouse.OverrideCursor = Cursors.Wait;
                _previews.Clear();
                _currentNode = null;
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

        private void ShowNode(TreeRow node)
        {
            _currentNode = node;

            foreach (var row in _rows)
                row.PropertyChanged -= OnRowPropertyChanged;
            _rows.Clear();

            foreach (var part in node.Node.CollectSheetMetal())
            {
                var row = new PartRow(part, ProductQuantity) { IsChecked = !_unchecked.Contains(part.FullFileName) };
                if (_results.TryGetValue(part.FullFileName, out var result))
                    row.ApplyResult(result);
                if (_previews.TryGetValue(part.FullFileName, out var preview))
                {
                    row.Thumbnail = preview.Thumbnail;
                    row.Flat = preview.Flat;
                    row.PreviewLoaded = true;
                }
                row.PropertyChanged += OnRowPropertyChanged;
                _rows.Add(row);
            }

            PartsTitle.Text = node.Description.Length > 0 ? $"{node.Title} — {node.Description}" : node.Title;
            EmptyText.Visibility = _rows.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            UpdateCounter();
            ShowDetails(null);
            StartPreviewLoading();
        }

        private void OnRowPropertyChanged(object sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(PartRow.IsChecked))
                return;
            var row = (PartRow)sender;
            if (row.IsChecked)
                _unchecked.Remove(row.Part.FullFileName);
            else
                _unchecked.Add(row.Part.FullFileName);
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
            string text = $"Отмечено {_rows.Count(r => r.IsChecked)} из {_rows.Count}";
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

            PartsGrid.Columns.Add(new DataGridTemplateColumn
            {
                Header = "",
                CellTemplate = (DataTemplate)FindResource("CheckCell"),
                Width = 32,
                CanUserReorder = false,
                CanUserResize = false,
                SortMemberPath = nameof(PartRow.IsChecked)
            });

            foreach (string id in _viewSettings.Columns)
            {
                var column = CreateColumn(id);
                if (column == null)
                    continue;
                _columnIds[column] = id;
                PartsGrid.Columns.Add(column);
            }

            PartsGrid.RowHeight = _viewSettings.Columns.Any(PartColumns.IsImage) ? 56 : 26;
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
                    CanUserSort = false,
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
                _viewSettings.Columns = new SheetMetalViewSettings().Columns;
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
                if (item.IsChecked)
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
            StartPreviewLoading();
        }

        #endregion

        #region Превью

        private bool PreviewsNeeded =>
            _viewSettings.Columns.Any(PartColumns.IsImage) || _viewSettings.ShowDetails;

        private IFlatPatternWriter PreviewWriter
        {
            get
            {
                if (_previewWriter != null)
                    return _previewWriter;
                try
                {
                    _previewWriter = FlatPatternWriters.Create(_context.App, _exportSettings);
                }
                catch (Exception ex)
                {
                    // Например, не найден ini — превью всё равно покажем через DataIO.
                    _context.Log.Warn("Превью разверток строится через DataIO: " + ex.Message);
                    _previewWriter = new DataIoWriter(DxfFormatString.Build(_exportSettings));
                }
                return _previewWriter;
            }
        }

        /// <summary>
        /// Превью загружаются по одному в фоне диспетчера: окно остаётся отзывчивым,
        /// а все обращения к Inventor идут из основного потока, как требует его API.
        /// </summary>
        private async void StartPreviewLoading()
        {
            int generation = ++_previewGeneration;
            if (!PreviewsNeeded)
                return;

            var pending = _rows.Where(r => !r.PreviewLoaded).ToList();
            for (int i = 0; i < pending.Count; i++)
            {
                await Dispatcher.Yield(DispatcherPriority.Background);
                if (generation != _previewGeneration)
                    return;

                SetStatus($"Загрузка превью: {i + 1} из {pending.Count}");
                LoadPreview(pending[i]);
            }

            if (pending.Count > 0)
                SetStatus(DefaultStatus());
        }

        private void LoadPreview(PartRow row)
        {
            if (row.PreviewLoaded)
                return;

            string key = row.Part.FullFileName;
            if (!_previews.TryGetValue(key, out var preview))
            {
                var document = _structure.SheetMetalDocuments[key];
                var thumbnail = PreviewService.Thumbnail((Inventor.Document)document);
                var flat = row.HasFlatPattern ? PreviewService.FlatPattern(document, PreviewWriter) : null;
                preview = (thumbnail, flat);
                _previews[key] = preview;
            }

            row.Thumbnail = preview.Thumbnail;
            row.Flat = preview.Flat;
            row.PreviewLoaded = true;
        }

        private void ResetPreviews()
        {
            _previewWriter = null;
            _previews.Clear();
            foreach (var row in _rows)
                row.PreviewLoaded = false;
            StartPreviewLoading();
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
            PropertiesGrid.Children.Clear();
            PropertiesGrid.RowDefinitions.Clear();
            if (row == null)
                return;

            if (_viewSettings.ShowDetails && !_busy)
                LoadPreview(row);

            AddProperty("Обозначение", row.PartNumber);
            AddProperty("Наименование", row.Description);
            AddProperty("Материал", row.Material);
            AddProperty("Толщина", row.Thickness.ToString("0.##", CultureInfo.CurrentCulture) + " мм");
            AddProperty("Количество", row.Quantity.ToString(CultureInfo.CurrentCulture));
            if (ProductQuantity > 1)
                AddProperty("На изделия", row.Total.ToString(CultureInfo.CurrentCulture));
            AddProperty("Развертка", row.HasFlatPattern ? "есть" : "нет");
            if (row.Status.Length > 0)
                AddProperty("Выгрузка", row.Status);
            foreach (var pair in row.Part.CustomProperties.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
                AddProperty(pair.Key, pair.Value);
            AddProperty("Файл", row.FilePath);
        }

        private void AddProperty(string name, string value)
        {
            int index = PropertiesGrid.RowDefinitions.Count;
            PropertiesGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var label = new TextBlock { Text = name, Style = (Style)FindResource("PropertyName") };
            var text = new TextBox { Text = value ?? string.Empty, Style = (Style)FindResource("PropertyValue") };
            Grid.SetRow(label, index);
            Grid.SetRow(text, index);
            Grid.SetColumn(text, 1);
            PropertiesGrid.Children.Add(label);
            PropertiesGrid.Children.Add(text);
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
                SetStatus("Нет отмеченных деталей");
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
            var exporter = new FlatPatternExporter(_exportSettings, folder, AssemblyName, writer);
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
                    SetStatus($"Выгрузка {results.Count + 1} из {parts.Count}: {part.Name}");
                    await Dispatcher.Yield(DispatcherPriority.Background);

                    var result = exporter.Export(_structure.ToCollected(part));
                    results.Add(result);
                    _results[part.FullFileName] = result;
                    Log(result);

                    if (result.Part.HasFlatPattern && _originals.TryGetValue(part.FullFileName, out var original) && !original.HasFlatPattern)
                    {
                        original.HasFlatPattern = true;
                        _previews.Remove(part.FullFileName);
                        flatsCreated = true;
                    }

                    _rows.FirstOrDefault(r => string.Equals(r.Part.FullFileName, part.FullFileName, StringComparison.OrdinalIgnoreCase))
                        ?.ApplyResult(result);
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
                ShowNode(_currentNode);
            else
                StartPreviewLoading();

            if (_exportSettings.OpenFolderAfterExport && Directory.Exists(folder))
                Process.Start("explorer.exe", $"\"{folder}\"");
        }

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
            if (dialog.DxfMethodChanged)
                ResetPreviews();
        }

        private void SetBusy(bool busy, int steps)
        {
            _busy = busy;
            Mouse.OverrideCursor = busy ? Cursors.Wait : null;
            Body.IsEnabled = !busy;
            Progress.Visibility = busy ? Visibility.Visible : Visibility.Collapsed;
            Progress.Maximum = Math.Max(1, steps);
            Progress.Value = 0;
        }

        #endregion

        #region Окно и настройки вида

        private void ApplyViewSettings()
        {
            Width = Math.Max(MinWidth, _viewSettings.WindowWidth);
            Height = Math.Max(MinHeight, _viewSettings.WindowHeight);
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
            SaveViewSettings();
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

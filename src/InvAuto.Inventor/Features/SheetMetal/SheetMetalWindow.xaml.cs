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
using InvAuto.Core.Preview;
using InvAuto.Core.SheetMetal;
using InvAuto.Core.Structure;
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
        private readonly FlatPatternExportSettings _exportSettings;
        private readonly SheetMetalViewSettings _viewSettings;
        private readonly BendSettings _bendSettings;

        private readonly FlatTree<TreeRow> _tree = new FlatTree<TreeRow>();
        private readonly ObservableCollection<PartRow> _rows = new ObservableCollection<PartRow>();
        private readonly ICollectionView _rowsView;
        private readonly Dictionary<DataGridColumn, string> _columnIds = new Dictionary<DataGridColumn, string>();

        // Состояние, которое должно переживать переключение узлов дерева.
        private readonly HashSet<string> _unchecked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ExportResult> _results = new Dictionary<string, ExportResult>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ImageSource> _thumbnails = new Dictionary<string, ImageSource>(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, FlatPreview> _flatPreviews = new Dictionary<string, FlatPreview>(StringComparer.OrdinalIgnoreCase);

        private AssemblyStructure _structure;
        private Dictionary<string, SheetMetalPart> _originals;
        private TreeRow _currentNode;
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
            CleanupFlatCache();
            LoadStructure(structure);
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

            _tree.Load(new TreeRow(structure.Root, 0), expandLevels: 1);
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
                _thumbnails.Clear();
                _flatPreviews.Clear();
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
                ApplyBends(row);
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
                case PartColumns.Bends: return TextColumn(header, nameof(PartRow.BendSummary), Star(1.1));
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

        private void CleanupFlatCache()
        {
            try
            {
                int removed = new FlatPreviewCache(FlatPreviewService.DefaultCacheFolder).Cleanup(TimeSpan.FromDays(30));
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
                SetStatus("Нет отмеченных деталей");
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
            PropertiesGrid.Children.Clear();
            PropertiesGrid.RowDefinitions.Clear();
            if (row == null)
                return;

            if (_viewSettings.ShowDetails && !_busy)
                LoadCachedPreview(row);

            AddProperty("Обозначение", row.PartNumber);
            AddProperty("Наименование", row.Description);
            AddProperty("Материал", row.Material);
            AddProperty("Толщина", row.Thickness.ToString("0.##", CultureInfo.CurrentCulture) + " мм");
            AddProperty("Количество", row.Quantity.ToString(CultureInfo.CurrentCulture));
            if (ProductQuantity > 1)
                AddProperty("На изделия", row.Total.ToString(CultureInfo.CurrentCulture));
            AddProperty("Развертка", row.HasFlatPattern ? "есть" : "нет");
            if (row.FlatPreviewError.Length > 0)
                AddProperty("Ошибка развертки", row.FlatPreviewError);
            AddProperty("Линии гиба", row.BendSummary);
            foreach (var item in row.BendLines)
            {
                bool own = _bendSettings.HasLineOverride(row.Part.FullFileName, item.Line.Key);
                AddProperty($"  линия {item.Line.Index} ({(item.Line.IsDown ? "вниз" : "вверх")})",
                    $"{item.Line.Length:0.#} мм — {item.Layout.Description}" + (own ? " (своя)" : string.Empty));
            }
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
                ShowNode(_currentNode);
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

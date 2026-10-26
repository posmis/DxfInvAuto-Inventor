using System;
using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using InvAuto.Core.Logging;
using InvAuto.Core.Materials;
using InvAuto.Framework.Ui;
using Microsoft.Win32;

namespace InvAuto.Features.Materials
{
    /// <summary>
    /// Редактор библиотеки материалов: наименование, толщина, плотность, материал по умолчанию для толщины.
    /// Правится копия; в файл библиотеки попадает только по ОК, после проверки.
    /// </summary>
    public partial class MaterialLibraryWindow : Window
    {
        private readonly MaterialLibraryStore _store;
        private readonly ILogger _log;
        private readonly MaterialLibrary _library;
        private readonly ObservableCollection<MaterialItem> _items = new ObservableCollection<MaterialItem>();

        public MaterialLibraryWindow(MaterialLibrary library, MaterialLibraryStore store, ILogger log)
        {
            InitializeComponent();
            _library = library.Clone();
            _store = store;
            _log = log;

            foreach (var m in _library.Materials.OrderBy(m => m.Thickness).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
                AddItem(m);
            MaterialsGrid.ItemsSource = _items;
            Title = "Библиотека материалов — " + store.FilePath;
        }

        /// <summary>Сохранённая библиотека (после ОК).</summary>
        public MaterialLibrary Result { get; private set; }

        private MaterialItem AddItem(Material material)
        {
            var item = new MaterialItem(material);
            item.DefaultChecked += OnDefaultChecked;
            _items.Add(item);
            return item;
        }

        /// <summary>По умолчанию для толщины — только один материал: у остальных той же толщины отметка снимается.</summary>
        private void OnDefaultChecked(MaterialItem checkedItem)
        {
            foreach (var item in _items.Where(i => i != checkedItem && i.IsDefault &&
                                                   MaterialLibrary.SameThickness(i.Material.Thickness, checkedItem.Material.Thickness)))
                item.IsDefault = false;
        }

        private void Select(MaterialItem item)
        {
            MaterialsGrid.SelectedItem = item;
            MaterialsGrid.ScrollIntoView(item);
            MaterialsGrid.CurrentCell = new DataGridCellInfo(item, MaterialsGrid.Columns[1]);
            MaterialsGrid.Focus();
        }

        private void OnAdd(object sender, RoutedEventArgs e)
        {
            var selected = MaterialsGrid.SelectedItem as MaterialItem;
            double thickness = selected?.Material.Thickness ?? 1;
            var item = AddItem(new Material { Name = "Сталь", Thickness = thickness, Density = MaterialLibrary.SteelDensity });
            Select(item);
            MaterialsGrid.BeginEdit();
        }

        private void OnDuplicate(object sender, RoutedEventArgs e)
        {
            if (!(MaterialsGrid.SelectedItem is MaterialItem selected))
            {
                ErrorText.Text = "Выделите материал, который нужно скопировать";
                return;
            }
            var copy = selected.Material.Clone();
            copy.Id = Guid.NewGuid().ToString("N");
            copy.IsDefault = false;
            Select(AddItem(copy));
        }

        private void OnDelete(object sender, RoutedEventArgs e)
        {
            var selected = MaterialsGrid.SelectedItems.Cast<MaterialItem>().ToList();
            if (selected.Count == 0)
                return;
            string names = string.Join("\n", selected.Take(8).Select(i => "  " + i.Material.Title)) +
                           (selected.Count > 8 ? $"\n  … и ещё {selected.Count - 8}" : string.Empty);
            if (MessageBox.Show(this,
                    $"Удалить материалы ({selected.Count})?\n\n{names}\n\nДетали, которым они назначены вручную, получат материал по толщине. " +
                    "Удаление окончательное после нажатия ОК.",
                    "InvAuto", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;
            foreach (var item in selected)
                _items.Remove(item);
        }

        private void OnGridKeyDown(object sender, KeyEventArgs e)
        {
            // Пока ячейка редактируется, Delete и Insert — клавиши текстового поля.
            if (e.OriginalSource is TextBox)
                return;
            if (e.Key == Key.Delete)
            {
                OnDelete(sender, e);
                e.Handled = true;
            }
            else if (e.Key == Key.Insert)
            {
                OnAdd(sender, e);
                e.Handled = true;
            }
        }

        /// <summary>Собирает библиотеку из таблицы. null — в таблице есть ошибки (показаны пользователю).</summary>
        private MaterialLibrary Collect()
        {
            MaterialsGrid.CommitEdit(DataGridEditingUnit.Row, true);
            var bad = _items.FirstOrDefault(i => i.HasError);
            if (bad != null)
            {
                ErrorText.Text = bad.Error;
                Select(bad);
                return null;
            }

            var library = new MaterialLibrary { Materials = _items.Select(i => i.Material.Clone()).ToList() };
            foreach (var m in library.Materials)
                m.Name = m.Name.Trim();
            library.Normalize();
            var errors = library.Validate();
            if (errors.Count > 0)
            {
                ErrorText.Text = errors[0] + (errors.Count > 1 ? $" (и ещё {errors.Count - 1})" : string.Empty);
                return null;
            }
            ErrorText.Text = string.Empty;
            return library;
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var library = Collect();
            if (library == null)
                return;
            try
            {
                _store.Save(library);
                _log.Info($"Библиотека материалов сохранена: {library.Materials.Count} материалов");
                Result = library;
                DialogResult = true;
            }
            catch (Exception ex)
            {
                _log.Error("Не удалось сохранить библиотеку материалов", ex);
                ErrorText.Text = "Не удалось сохранить: " + ex.Message;
            }
        }

        private void OnExport(object sender, RoutedEventArgs e)
        {
            var library = Collect();
            if (library == null)
                return;
            var dialog = new SaveFileDialog
            {
                Title = "Экспорт библиотеки материалов",
                Filter = "Таблица Excel (CSV)|*.csv|XML — полная копия для другого компьютера|*.xml",
                FileName = $"Материалы InvAuto {DateTime.Now:yyyy-MM-dd}",
                AddExtension = true
            };
            if (dialog.ShowDialog(this) != true)
                return;
            try
            {
                MaterialLibraryStore.Export(library, dialog.FileName);
                _log.Info("Библиотека материалов выгружена: " + dialog.FileName);
                MessageBox.Show(this, $"Сохранено материалов: {library.Materials.Count}\n{dialog.FileName}", "InvAuto",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _log.Error("Не удалось выгрузить библиотеку материалов", ex);
                ErrorText.Text = "Не удалось сохранить файл: " + ex.Message;
            }
        }

        private void OnImport(object sender, RoutedEventArgs e)
        {
            var current = Collect();
            if (current == null)
                return;
            var dialog = new OpenFileDialog
            {
                Title = "Импорт материалов",
                Filter = "Библиотека материалов (*.csv, *.xml)|*.csv;*.xml|Все файлы|*.*"
            };
            if (dialog.ShowDialog(this) != true)
                return;
            try
            {
                var incoming = MaterialLibraryStore.Import(dialog.FileName);
                var errors = incoming.Validate();
                if (errors.Count > 0)
                {
                    MessageBox.Show(this, "В файле есть ошибки, импорт не выполнен:\n\n" + string.Join("\n", errors.Take(10)),
                        "InvAuto", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }

                var (added, updated) = current.Merge(incoming);
                _items.Clear();
                foreach (var m in current.Materials.OrderBy(m => m.Thickness).ThenBy(m => m.Name, StringComparer.CurrentCultureIgnoreCase))
                    AddItem(m);
                ErrorText.Text = string.Empty;
                MessageBox.Show(this, $"Добавлено материалов: {added}, обновлена плотность: {updated}.\n\nИзменения сохранятся кнопкой ОК.",
                    "InvAuto", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _log.Error("Не удалось импортировать материалы из " + dialog.FileName, ex);
                MessageBox.Show(this, "Не удалось прочитать файл:\n" + ex.Message, "InvAuto", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnOpenFolder(object sender, RoutedEventArgs e)
        {
            try
            {
                Directory.CreateDirectory(_store.Folder);
                Process.Start("explorer.exe", "\"" + _store.Folder + "\"");
            }
            catch (Exception ex)
            {
                ErrorText.Text = "Не удалось открыть папку: " + ex.Message;
            }
        }

        /// <summary>Строка таблицы: числа вводятся текстом (с запятой или точкой) и проверяются сразу.</summary>
        private sealed class MaterialItem : ObservableObject
        {
            private string _thicknessText;
            private string _densityText;
            private string _thicknessError = string.Empty;
            private string _densityError = string.Empty;

            public MaterialItem(Material material)
            {
                Material = material;
                _thicknessText = material.Thickness.ToString("0.###", CultureInfo.CurrentCulture);
                _densityText = material.Density.ToString("0.###", CultureInfo.CurrentCulture);
            }

            public event Action<MaterialItem> DefaultChecked;

            public Material Material { get; }

            public string Name
            {
                get => Material.Name;
                set
                {
                    Material.Name = value ?? string.Empty;
                    OnPropertyChanged();
                }
            }

            public double Thickness => Material.Thickness;
            public double Density => Material.Density;

            public string ThicknessText
            {
                get => _thicknessText;
                set
                {
                    _thicknessText = value;
                    _thicknessError = Parse(value, 0.01, 200, out double v) ? string.Empty : "Толщина — число от 0,01 до 200 мм";
                    if (_thicknessError.Length == 0)
                        Material.Thickness = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Thickness));
                    OnErrorChanged();
                }
            }

            public string DensityText
            {
                get => _densityText;
                set
                {
                    _densityText = value;
                    _densityError = Parse(value, 100, 25000, out double v) ? string.Empty : "Плотность — число от 100 до 25 000 кг/м³";
                    if (_densityError.Length == 0)
                        Material.Density = v;
                    OnPropertyChanged();
                    OnPropertyChanged(nameof(Density));
                    OnErrorChanged();
                }
            }

            public bool IsDefault
            {
                get => Material.IsDefault;
                set
                {
                    if (Material.IsDefault == value)
                        return;
                    Material.IsDefault = value;
                    OnPropertyChanged();
                    if (value)
                        DefaultChecked?.Invoke(this);
                }
            }

            public string Error =>
                string.Join("; ", new[] { _thicknessError, _densityError }.Where(e => e.Length > 0));

            public bool HasError => Error.Length > 0;

            private void OnErrorChanged()
            {
                OnPropertyChanged(nameof(Error));
                OnPropertyChanged(nameof(HasError));
            }

            private static bool Parse(string text, double min, double max, out double value)
            {
                string v = (text ?? string.Empty).Trim().Replace(" ", string.Empty).Replace(' '.ToString(), string.Empty).Replace(',', '.');
                return double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out value) && value >= min && value <= max;
            }
        }
    }
}

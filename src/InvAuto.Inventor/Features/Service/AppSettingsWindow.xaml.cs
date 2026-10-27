using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using InvAuto.Core.Maintenance;
using InvAuto.Core.Materials;
using InvAuto.Core.Preflight;
using InvAuto.Core.Settings;
using InvAuto.Core.Structure;
using InvAuto.Features.SheetMetal;
using InvAuto.Framework;
using InvAuto.InventorApi;

namespace InvAuto.Features.Service
{
    /// <summary>
    /// Настройки приложения: кэш разверток, журнал, сброс настроек, сведения о программе.
    /// Очистка и сброс — сразу после отдельного подтверждения; сроки хранения — по ОК.
    /// </summary>
    public partial class AppSettingsWindow : Window
    {
        public const string CacheSuffix = @"InvAuto\FlatCache";
        public const string LogSuffix = @"InvAuto\logs";

        private readonly FeatureContext _context;
        private readonly AppSettings _settings;
        private readonly string _cacheFolder = FlatPreviewService.DefaultCacheFolder;

        /// <summary>Настройки, которые можно сбросить: ключ, название, что входит.</summary>
        private static readonly (string Key, string Title, string Description)[] ResettableSettings =
        {
            (SheetMetalWindow.ExportSettingsKey, "Параметры выгрузки DXF", "папка, шаблоны имён, ini-файл, раскладка по толщине"),
            (SheetMetalWindow.BendSettingsKey, "Линии гиба", "общие параметры, параметры деталей и отдельных линий"),
            (InvAuto.Core.Marking.MarkingSettings.Key, "Маркировка", "общие параметры и параметры деталей"),
            (InvAuto.Core.Flat.FlatRotationSettings.Key, "Поворот разверток", "повороты разверток, заданные в окне просмотра"),
            (SheetMetalWindow.ViewSettingsKey, "Вид окна «Листовые детали»", "колонки, размеры окна и панелей"),
            (AppSettings.Key, "Настройки приложения", "сроки хранения кэша и журнала, запоминание списков"),
            (SavedPartLists.Key, "Сохранённые списки деталей", "списки окна «Листовые детали» по сборкам"),
            (MaterialAssignments.Key, "Назначенные материалы", "материалы, назначенные деталям вручную (библиотека материалов не затрагивается)"),
            (PreflightSettings.Key, "Параметры проверки", "размер отверстий, расстояние до линий гиба, размер листа"),
            (PreflightDismissals.Key, "Пропущенные замечания", "замечания проверки, пропущенные для отдельных деталей")
        };

        public AppSettingsWindow(FeatureContext context)
        {
            InitializeComponent();
            _context = context;
            _settings = context.Settings.Load<AppSettings>(AppSettings.Key);
            _settings.Normalize();
            _preflight = context.Settings.Load<PreflightSettings>(PreflightSettings.Key);
            _preflight.Normalize();
            ShowPreflight();

            CacheFolderText.Text = _cacheFolder;
            LogFolderText.Text = context.LogFolder;
            SettingsFolderText.Text = context.Settings.Folder;
            ShowDays();
            BuildResetRows();
            BuildAbout();

            NavList.SelectedIndex = 0;
            Loaded += async (s, e) => await RefreshSizesAsync();
        }

        /// <summary>Кэш разверток был очищен — открытые развертки надо перечитать.</summary>
        public bool CacheCleared { get; private set; }

        /// <summary>Ключи настроек, сброшенных к значениям по умолчанию.</summary>
        public ISet<string> ResetKeys { get; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        private void ShowDays()
        {
            CacheDaysBox.Text = _settings.CacheMaxAgeDays.ToString(CultureInfo.InvariantCulture);
            LogDaysBox.Text = _settings.LogMaxAgeDays.ToString(CultureInfo.InvariantCulture);
            RememberListsBox.IsChecked = _settings.RememberPartLists;
        }

        private void OnSectionChanged(object sender, SelectionChangedEventArgs e)
        {
            string page = (NavList.SelectedItem as ListBoxItem)?.Tag as string;
            foreach (var panel in new FrameworkElement[] { GeneralPage, PreflightPage, CachePage, LogPage, ResetPage, AboutPage })
                panel.Visibility = panel.Name == page ? Visibility.Visible : Visibility.Collapsed;
        }

        private async Task RefreshSizesAsync()
        {
            CacheSizeText.Text = LogSizeText.Text = "подсчёт…";
            var cache = await Task.Run(() => SafeCleanup.Measure(_cacheFolder, CacheSuffix, ".dxf"));
            var logs = await Task.Run(() => SafeCleanup.Measure(_context.LogFolder, LogSuffix, ".log"));
            CacheSizeText.Text = Describe(cache);
            LogSizeText.Text = Describe(logs);

            string Describe((int Files, long Bytes) m) =>
                m.Files == 0 ? "пусто" : $"{m.Files} файл(ов), {SafeCleanup.FormatSize(m.Bytes)}";
        }

        #region Очистка

        private async void OnClearCache(object sender, RoutedEventArgs e)
        {
            bool done = Clean("кэш разверток", _cacheFolder, CacheSuffix, null,
                "После удаления развертки в окне «Листовые детали» придётся сформировать заново. " +
                "Детали Inventor, параметры и выгруженные DXF не затрагиваются.", ".dxf");
            if (done)
                CacheCleared = true;
            await RefreshSizesAsync();
        }

        private async void OnClearLog(object sender, RoutedEventArgs e)
        {
            Clean("журнал", _context.LogFolder, LogSuffix, _context.CurrentLogFile,
                "Записи о прошлой работе InvAuto будут потеряны. Сегодняшний файл журнала сохраняется.", ".log");
            await RefreshSizesAsync();
        }

        /// <summary>Составляет план, показывает его и удаляет после подтверждения. true — что-то удалено.</summary>
        private bool Clean(string what, string folder, string suffix, string keepFile, string consequence, params string[] extensions)
        {
            CleanupPlan plan;
            try
            {
                plan = SafeCleanup.Plan(folder, suffix, extensions).Except(keepFile, "текущий файл, в него идёт запись");
            }
            catch (Exception ex)
            {
                _context.Log.Error($"Очистка ({what}) отменена при проверке папки", ex);
                Warn("Очистка отменена: " + ex.Message);
                return false;
            }

            if (plan.Files.Count == 0)
            {
                MessageBox.Show(this, $"Удалять нечего: {what} пуст.", "InvAuto", MessageBoxButton.OK, MessageBoxImage.Information);
                return false;
            }

            var dialog = new CleanupConfirmWindow(what, consequence, plan, _context.Log) { Owner = this };
            dialog.ShowDialog();
            return dialog.Result != null && dialog.Result.Deleted > 0;
        }

        #endregion

        #region Сброс настроек

        private void BuildResetRows()
        {
            ResetGrid.Children.Clear();
            ResetGrid.RowDefinitions.Clear();
            foreach (var item in ResettableSettings)
            {
                int row = ResetGrid.RowDefinitions.Count;
                ResetGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                bool changed = _context.Settings.Exists(item.Key);

                var text = new StackPanel { Margin = new Thickness(0, 6, 12, 6) };
                text.Children.Add(new TextBlock { Text = item.Title, FontWeight = FontWeights.SemiBold });
                text.Children.Add(new TextBlock { Text = item.Description, Style = (Style)FindResource("Hint") });

                var state = new TextBlock
                {
                    Text = changed ? "изменены" : "по умолчанию",
                    Style = (Style)FindResource(changed ? "Label" : "Hint"),
                    VerticalAlignment = VerticalAlignment.Center
                };

                var button = new Button
                {
                    Content = "Сбросить…",
                    Style = (Style)FindResource("DangerAction"),
                    IsEnabled = changed,
                    VerticalAlignment = VerticalAlignment.Center,
                    Tag = item
                };
                button.Click += OnReset;

                Grid.SetRow(text, row);
                Grid.SetRow(state, row);
                Grid.SetColumn(state, 1);
                Grid.SetRow(button, row);
                Grid.SetColumn(button, 2);
                ResetGrid.Children.Add(text);
                ResetGrid.Children.Add(state);
                ResetGrid.Children.Add(button);
            }
        }

        private void OnReset(object sender, RoutedEventArgs e)
        {
            var item = ((ValueTuple<string, string, string>)((Button)sender).Tag);
            string key = item.Item1, title = item.Item2;

            if (MessageBox.Show(this,
                    $"Сбросить «{title}» к значениям по умолчанию?\n\n" +
                    $"Текущие настройки будут перенесены в папку резервных копий:\n{Path.Combine(_context.Settings.Folder, "backup")}",
                    "InvAuto", MessageBoxButton.YesNo, MessageBoxImage.Warning, MessageBoxResult.No) != MessageBoxResult.Yes)
                return;

            try
            {
                string backup = _context.Settings.Reset(key);
                ResetKeys.Add(key);
                if (key == AppSettings.Key)
                {
                    var defaults = new AppSettings();
                    _settings.CacheMaxAgeDays = defaults.CacheMaxAgeDays;
                    _settings.LogMaxAgeDays = defaults.LogMaxAgeDays;
                    _settings.RememberPartLists = defaults.RememberPartLists;
                    ShowDays();
                }
                if (key == PreflightSettings.Key)
                {
                    _preflight = new PreflightSettings();
                    ShowPreflight();
                }
                BuildResetRows();
                MessageBox.Show(this, $"«{title}» — значения по умолчанию." +
                                      (backup != null ? "\n\nРезервная копия:\n" + backup : string.Empty),
                    "InvAuto", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _context.Log.Error($"Не удалось сбросить настройки '{key}'", ex);
                Warn("Не удалось сбросить настройки: " + ex.Message);
            }
        }

        #endregion

        #region О программе

        /// <summary>Версия продукта (1.0.0) из Directory.Build.props; нет — версия сборки.</summary>
        internal static string Version(System.Reflection.Assembly assembly)
        {
            var info = (System.Reflection.AssemblyInformationalVersionAttribute)Attribute.GetCustomAttribute(
                assembly, typeof(System.Reflection.AssemblyInformationalVersionAttribute));
            string version = info?.InformationalVersion ?? assembly.GetName().Version?.ToString() ?? "—";
            int plus = version.IndexOf('+');
            return plus > 0 ? version.Substring(0, plus) : version;
        }

        private void BuildAbout()
        {
            var assembly = typeof(AppSettingsWindow).Assembly;
            Add("Версия InvAuto", Version(assembly));
            Add("Inventor", InventorVersion());
            Add("Плагин", Path.GetDirectoryName(assembly.Location) ?? "—");
            Add("Настройки", _context.Settings.Folder);
            Add("Журнал", _context.LogFolder);
            Add("Кэш разверток", _cacheFolder);
            Add("Библиотека материалов", new MaterialLibraryStore(_context.Settings.Folder, _context.Log).FilePath);

            void Add(string name, string value)
            {
                int row = AboutGrid.RowDefinitions.Count;
                AboutGrid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
                var label = new TextBlock { Text = name, Style = (Style)FindResource("Label") };
                var text = new TextBox { Text = value, Style = (Style)FindResource("PathBox") };
                Grid.SetRow(label, row);
                Grid.SetRow(text, row);
                Grid.SetColumn(text, 1);
                AboutGrid.Children.Add(label);
                AboutGrid.Children.Add(text);
            }
        }

        private string InventorVersion()
        {
            try
            {
                return ((dynamic)_context.App).SoftwareVersion.DisplayVersion;
            }
            catch
            {
                return "—";
            }
        }

        #endregion

        #region Папки

        private void OnOpenCacheFolder(object sender, RoutedEventArgs e) => OpenFolder(_cacheFolder);

        private void OnOpenLogFolder(object sender, RoutedEventArgs e) => OpenFolder(_context.LogFolder);

        private void OnOpenSettingsFolder(object sender, RoutedEventArgs e) => OpenFolder(_context.Settings.Folder);

        private void OnOpenCurrentLog(object sender, RoutedEventArgs e)
        {
            string file = _context.CurrentLogFile;
            if (!File.Exists(file))
            {
                Warn("Сегодня записей в журнале ещё нет.");
                return;
            }
            Start(new ProcessStartInfo(file) { UseShellExecute = true });
        }

        private void OpenFolder(string folder)
        {
            Directory.CreateDirectory(folder);
            Start(new ProcessStartInfo("explorer.exe", "\"" + folder + "\""));
        }

        private void Start(ProcessStartInfo info)
        {
            try
            {
                Process.Start(info);
            }
            catch (Exception ex)
            {
                Warn("Не удалось открыть: " + ex.Message);
            }
        }

        #endregion

        #region Проверка

        private PreflightSettings _preflight;

        private static string Num(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

        private void ShowPreflight()
        {
            CheckBeforeExportBox.IsChecked = _preflight.CheckBeforeExport;
            HoleFactorBox.Text = Num(_preflight.MinHoleFactor);
            HoleMinBox.Text = Num(_preflight.MinHoleMm);
            BendClearanceBox.Text = Num(_preflight.BendClearanceFactor);
            SheetSizeBox.IsChecked = _preflight.CheckSheetSize;
            SheetLengthBox.Text = Num(_preflight.SheetLength);
            SheetWidthBox.Text = Num(_preflight.SheetWidth);
            RequirePartNumberBox.IsChecked = _preflight.RequirePartNumber;
        }

        private bool ReadNumber(TextBox box, double min, double max, string what, out double value)
        {
            if (InvAuto.Core.Formatting.NumberText.TryParse(box.Text, min, max, out value))
                return true;
            NavList.SelectedIndex = 1;
            Warn($"{what} — число от {Num(min)} до {Num(max)}.");
            box.Focus();
            box.SelectAll();
            return false;
        }

        /// <summary>Читает и сохраняет параметры проверки. false — ошибка ввода (окно не закрывается).</summary>
        private bool SavePreflight()
        {
            if (!ReadNumber(HoleFactorBox, 0, 10, "Коэффициент размера отверстия", out double factor) ||
                !ReadNumber(HoleMinBox, 0, 100, "Минимальный размер отверстия", out double minHole) ||
                !ReadNumber(BendClearanceBox, 0, 20, "Коэффициент расстояния до линии гиба", out double clearance) ||
                !ReadNumber(SheetLengthBox, 10, 100000, "Длина листа", out double length) ||
                !ReadNumber(SheetWidthBox, 10, 100000, "Ширина листа", out double width))
                return false;

            var updated = new PreflightSettings
            {
                CheckBeforeExport = CheckBeforeExportBox.IsChecked == true,
                MinHoleFactor = factor,
                MinHoleMm = minHole,
                BendClearanceFactor = clearance,
                CheckSheetSize = SheetSizeBox.IsChecked == true,
                SheetLength = length,
                SheetWidth = width,
                RequirePartNumber = RequirePartNumberBox.IsChecked == true
            };
            // Сохраняем только изменения: иначе в «Сбросе настроек» параметры числились бы изменёнными.
            bool changed = updated.CheckBeforeExport != _preflight.CheckBeforeExport || updated.MinHoleFactor != _preflight.MinHoleFactor ||
                           updated.MinHoleMm != _preflight.MinHoleMm || updated.BendClearanceFactor != _preflight.BendClearanceFactor ||
                           updated.CheckSheetSize != _preflight.CheckSheetSize || updated.SheetLength != _preflight.SheetLength ||
                           updated.SheetWidth != _preflight.SheetWidth || updated.RequirePartNumber != _preflight.RequirePartNumber;
            if (changed)
            {
                _preflight = updated;
                _context.Settings.Save(PreflightSettings.Key, _preflight);
            }
            return true;
        }

        #endregion

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (!ReadDays(CacheDaysBox, "кэша", out int cacheDays) || !ReadDays(LogDaysBox, "журнала", out int logDays))
                return;
            if (!SavePreflight())
                return;

            bool remember = RememberListsBox.IsChecked == true;
            if (cacheDays != _settings.CacheMaxAgeDays || logDays != _settings.LogMaxAgeDays || remember != _settings.RememberPartLists)
            {
                _settings.CacheMaxAgeDays = cacheDays;
                _settings.LogMaxAgeDays = logDays;
                _settings.RememberPartLists = remember;
                _context.Settings.Save(AppSettings.Key, _settings);
            }
            DialogResult = true;
        }

        private bool ReadDays(TextBox box, string what, out int days)
        {
            if (int.TryParse(box.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out days) && days >= 1 && days <= 3650)
                return true;
            Warn($"Срок хранения {what} — целое число дней от 1 до 3650.");
            box.Focus();
            box.SelectAll();
            return false;
        }

        private void Warn(string text) =>
            MessageBox.Show(this, text, "InvAuto", MessageBoxButton.OK, MessageBoxImage.Warning);
    }
}

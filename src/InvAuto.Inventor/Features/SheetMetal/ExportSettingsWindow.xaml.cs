using System.IO;
using System.Linq;
using System.Windows;
using InvAuto.Core.Export;
using InvAuto.InventorApi;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>Параметры выгрузки: папка, шаблоны имён, способ записи DXF.</summary>
    public partial class ExportSettingsWindow : Window
    {
        private static readonly string[] AcadVersions = { "2018", "2013", "2010", "2007", "2004", "2000", "R12" };

        private readonly FlatPatternExportSettings _settings;
        private readonly string _defaultFolder;
        private readonly SheetMetalPart _sample;
        private readonly string _assemblyName;

        public ExportSettingsWindow(FlatPatternExportSettings settings, string defaultFolder, SheetMetalPart sample, string assemblyName)
        {
            InitializeComponent();
            _settings = settings;
            _defaultFolder = defaultFolder;
            _sample = sample ?? new SheetMetalPart { Name = "Деталь", PartNumber = "АБВГ.001", Material = "Сталь", ThicknessMm = 3, Quantity = 2 };
            _assemblyName = assemblyName;

            FolderHint.Text = "Пусто — папка DXF рядом со сборкой: " + defaultFolder;
            IniHint.Text = "Пусто — файл, поставляемый с плагином: " + FlatPatternWriters.BundledIniPath;
            AcadBox.ItemsSource = AcadVersions;
            Load();
        }

        private void Load()
        {
            FolderBox.Text = _settings.OutputFolder;
            NameBox.Text = _settings.FileNameTemplate;
            SubfolderBox.Text = _settings.SubfolderTemplate;
            ThicknessFolderBox.IsChecked = _settings.SortByThickness;

            IniMethod.IsChecked = _settings.Method == DxfExportMethod.IniFile;
            FormatMethod.IsChecked = _settings.Method == DxfExportMethod.FormatString;
            IniBox.Text = _settings.IniFilePath;

            AcadBox.SelectedItem = AcadVersions.Contains(_settings.AcadVersion) ? _settings.AcadVersion : "2004";
            OuterLayerBox.Text = _settings.OuterProfileLayer;
            InnerLayerBox.Text = _settings.InteriorProfilesLayer;
            BendUpLayerBox.Text = _settings.BendUpLayer;
            BendDownLayerBox.Text = _settings.BendDownLayer;
            BendLinesBox.IsChecked = _settings.ExportBendLines;

            CreateFlatBox.IsChecked = _settings.CreateMissingFlatPatterns;
            OverwriteBox.IsChecked = _settings.OverwriteExisting;
            CsvBox.IsChecked = _settings.WriteCsvReport;
            OpenFolderBox.IsChecked = _settings.OpenFolderAfterExport;

            UpdatePreview();
            UpdateMethodPanels();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            var method = IniMethod.IsChecked == true ? DxfExportMethod.IniFile : DxfExportMethod.FormatString;
            string ini = IniBox.Text.Trim();

            if (method == DxfExportMethod.IniFile)
            {
                string resolved = ini.Length > 0 ? ini : FlatPatternWriters.BundledIniPath;
                if (!File.Exists(resolved))
                {
                    MessageBox.Show(this, "Не найден ini-файл:\n" + resolved, "InvAuto", MessageBoxButton.OK, MessageBoxImage.Warning);
                    return;
                }
            }

            _settings.OutputFolder = FolderBox.Text.Trim();
            _settings.FileNameTemplate = NameBox.Text.Trim().Length > 0 ? NameBox.Text.Trim() : new FlatPatternExportSettings().FileNameTemplate;
            _settings.SubfolderTemplate = SubfolderBox.Text.Trim();
            _settings.SortByThickness = ThicknessFolderBox.IsChecked == true;
            _settings.Method = method;
            _settings.IniFilePath = ini;
            _settings.AcadVersion = (string)AcadBox.SelectedItem ?? "2004";
            _settings.OuterProfileLayer = OuterLayerBox.Text.Trim();
            _settings.InteriorProfilesLayer = InnerLayerBox.Text.Trim();
            _settings.BendUpLayer = BendUpLayerBox.Text.Trim();
            _settings.BendDownLayer = BendDownLayerBox.Text.Trim();
            _settings.ExportBendLines = BendLinesBox.IsChecked == true;
            _settings.CreateMissingFlatPatterns = CreateFlatBox.IsChecked == true;
            _settings.OverwriteExisting = OverwriteBox.IsChecked == true;
            _settings.WriteCsvReport = CsvBox.IsChecked == true;
            _settings.OpenFolderAfterExport = OpenFolderBox.IsChecked == true;

            DialogResult = true;
        }

        private void OnTemplateChanged(object sender, RoutedEventArgs e) => UpdatePreview();

        private void UpdatePreview()
        {
            if (PreviewText == null)
                return;
            var values = _sample.ToTemplateValues(_settings.QuantityMultiplier, _assemblyName);
            var draft = new FlatPatternExportSettings
            {
                FileNameTemplate = NameBox.Text,
                SubfolderTemplate = SubfolderBox.Text,
                SortByThickness = ThicknessFolderBox.IsChecked == true
            };
            string sub = ExportNaming.RelativeFolder(draft, values);
            string name = ExportNaming.FileName(draft, values);
            PreviewText.Text = (sub.Length > 0 ? sub + "\\" : string.Empty) + name + ".dxf";
        }

        private void OnMethodChanged(object sender, RoutedEventArgs e) => UpdateMethodPanels();

        private void UpdateMethodPanels()
        {
            if (IniPanel == null || FormatPanel == null)
                return;
            IniPanel.IsEnabled = IniMethod.IsChecked == true;
            FormatPanel.IsEnabled = FormatMethod.IsChecked == true;
        }

        private void OnBrowseFolder(object sender, RoutedEventArgs e)
        {
            using (var dialog = new System.Windows.Forms.FolderBrowserDialog
            {
                SelectedPath = FolderBox.Text.Trim().Length > 0 ? FolderBox.Text.Trim() : _defaultFolder
            })
            {
                if (dialog.ShowDialog() == System.Windows.Forms.DialogResult.OK)
                    FolderBox.Text = dialog.SelectedPath;
            }
        }

        private void OnBrowseIni(object sender, RoutedEventArgs e)
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Filter = "Настройки экспорта DXF (*.ini)|*.ini|Все файлы (*.*)|*.*",
                FileName = IniBox.Text.Trim()
            };
            if (dialog.ShowDialog(this) == true)
                IniBox.Text = dialog.FileName;
        }
    }
}

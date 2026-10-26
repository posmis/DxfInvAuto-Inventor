using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;
using InvAuto.Core.Export;
using InvAuto.Core.Naming;
using InvAuto.Framework;
using InvAuto.InventorApi;

namespace InvAuto.Features.FlatPatternExport
{
    /// <summary>
    /// Главное окно экспорта: слева таблица листовых деталей (выбор, количество, статус),
    /// справа настройки. Собрано кодом, без дизайнера, чтобы его было легко править в любом редакторе.
    /// </summary>
    internal sealed class FlatPatternExportWindow : Form
    {
        private static readonly string[] AcadVersions = { "2018", "2013", "2010", "2007", "2004", "2000", "R12" };

        private readonly FeatureContext _context;
        private readonly string _settingsKey;
        private readonly IReadOnlyList<CollectedPart> _parts;
        private readonly FlatPatternExportSettings _settings;
        private readonly string _defaultFolder;
        private readonly string _assemblyName;

        // Таблица
        private readonly DataGridView _grid = new DataGridView();
        private readonly TextBox _filter = new TextBox { Width = 220 };
        private readonly Label _counter = new Label { AutoSize = true, Anchor = AnchorStyles.Left, Padding = new Padding(10, 6, 0, 0) };

        // Настройки
        private readonly TextBox _folder = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox _nameTemplate = new TextBox { Dock = DockStyle.Fill };
        private readonly TextBox _subfolderTemplate = new TextBox { Dock = DockStyle.Fill };
        private readonly Label _preview = new Label { AutoSize = true, ForeColor = SystemColors.HotTrack, MaximumSize = new Size(300, 0) };
        private readonly NumericUpDown _multiplier = new NumericUpDown { Minimum = 1, Maximum = 10000, Width = 80 };
        private readonly ComboBox _acadVersion = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 80 };
        private readonly TextBox _outerLayer = new TextBox { Width = 120 };
        private readonly TextBox _innerLayer = new TextBox { Width = 120 };
        private readonly CheckBox _bendLines = new CheckBox { Text = "Линии гиба", AutoSize = true };
        private readonly CheckBox _tangentLines = new CheckBox { Text = "Касательные линии", AutoSize = true };
        private readonly CheckBox _createFlat = new CheckBox { Text = "Создавать отсутствующие развертки", AutoSize = true };
        private readonly CheckBox _overwrite = new CheckBox { Text = "Перезаписывать существующие файлы", AutoSize = true };
        private readonly CheckBox _csv = new CheckBox { Text = "CSV-отчёт", AutoSize = true };
        private readonly CheckBox _openFolder = new CheckBox { Text = "Открыть папку после экспорта", AutoSize = true };

        // Низ окна
        private readonly ProgressBar _progress = new ProgressBar { Dock = DockStyle.Fill, Visible = false };
        private readonly Label _status = new Label { AutoSize = true, Anchor = AnchorStyles.Left };
        private readonly Button _export = new Button { Text = "Экспорт", AutoSize = true, MinimumSize = new Size(100, 0) };
        private readonly Button _close = new Button { Text = "Закрыть", AutoSize = true, MinimumSize = new Size(100, 0), DialogResult = DialogResult.Cancel };

        public FlatPatternExportWindow(FeatureContext context, string settingsKey, IReadOnlyList<CollectedPart> parts,
            FlatPatternExportSettings settings, string defaultFolder, string assemblyName)
        {
            _context = context;
            _settingsKey = settingsKey;
            _parts = parts;
            _settings = settings;
            _defaultFolder = defaultFolder;
            _assemblyName = assemblyName;

            Text = $"Развертки в DXF — {assemblyName}";
            StartPosition = FormStartPosition.CenterParent;
            ShowInTaskbar = false;
            Font = SystemFonts.MessageBoxFont;
            AutoScaleMode = AutoScaleMode.Font;
            Size = new Size(1200, 700);
            MinimumSize = new Size(900, 500);
            CancelButton = _close;

            var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2, RowCount = 2, Padding = new Padding(8) };
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 360));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            root.Controls.Add(BuildPartsPanel(), 0, 0);
            root.Controls.Add(BuildSettingsPanel(), 1, 0);
            var bottom = BuildBottomPanel();
            root.Controls.Add(bottom, 0, 1);
            root.SetColumnSpan(bottom, 2);
            Controls.Add(root);

            LoadSettings();
            FillGrid();
            UpdatePreview();
            UpdateCounter();
        }

        #region Построение интерфейса

        private Control BuildPartsPanel()
        {
            var selectAll = new Button { Text = "Выбрать все", AutoSize = true };
            var selectNone = new Button { Text = "Снять все", AutoSize = true };
            selectAll.Click += (s, e) => SetChecked(true);
            selectNone.Click += (s, e) => SetChecked(false);
            _filter.TextChanged += (s, e) => ApplyFilter();

            var toolbar = new FlowLayoutPanel { Dock = DockStyle.Fill, AutoSize = true, WrapContents = false };
            toolbar.Controls.Add(new Label { Text = "Поиск:", AutoSize = true, Padding = new Padding(0, 6, 0, 0) });
            toolbar.Controls.AddRange(new Control[] { _filter, selectAll, selectNone, _counter });

            ConfigureGrid();

            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, RowCount = 2 };
            panel.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            panel.Controls.Add(toolbar, 0, 0);
            panel.Controls.Add(_grid, 0, 1);
            return panel;
        }

        private void ConfigureGrid()
        {
            _grid.Dock = DockStyle.Fill;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.RowHeadersVisible = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.BackgroundColor = SystemColors.Window;
            _grid.EditMode = DataGridViewEditMode.EditOnEnter;

            _grid.Columns.Add(new DataGridViewCheckBoxColumn { Name = Col.Export, HeaderText = "", FillWeight = 4, MinimumWidth = 30 });
            AddTextColumn(Col.Name, "Имя", 22);
            AddTextColumn(Col.PartNumber, "Обозначение", 16);
            AddTextColumn(Col.Description, "Наименование", 16);
            AddTextColumn(Col.Material, "Материал", 14);
            AddTextColumn(Col.Thickness, "Толщина, мм", 8);
            AddTextColumn(Col.Qty, "Кол-во", 6, readOnly: false);
            AddTextColumn(Col.Total, "Итого", 6);
            AddTextColumn(Col.Status, "Статус", 18);

            _grid.Columns[Col.Thickness].ValueType = typeof(double);
            _grid.Columns[Col.Qty].ValueType = typeof(int);
            _grid.Columns[Col.Total].ValueType = typeof(int);

            _grid.CellValidating += OnCellValidating;
            _grid.CellEndEdit += OnCellEndEdit;
            _grid.CurrentCellDirtyStateChanged += (s, e) =>
            {
                // Чтобы галочка применялась сразу, а не после ухода из ячейки.
                if (_grid.CurrentCell is DataGridViewCheckBoxCell)
                    _grid.CommitEdit(DataGridViewDataErrorContexts.Commit);
            };
            _grid.CellValueChanged += (s, e) =>
            {
                if (e.RowIndex >= 0 && _grid.Columns[e.ColumnIndex].Name == Col.Export)
                    UpdateCounter();
            };
        }

        private void AddTextColumn(string name, string header, float weight, bool readOnly = true)
        {
            var column = new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = header,
                FillWeight = weight,
                ReadOnly = readOnly,
                SortMode = DataGridViewColumnSortMode.Automatic
            };
            _grid.Columns.Add(column);
        }

        private Control BuildSettingsPanel()
        {
            var browse = new Button { Text = "…", Width = 30 };
            browse.Click += (s, e) => Browse();

            var files = NewGrid();
            AddRow(files, "Папка:", WithButton(_folder, browse));
            AddHint(files, "Пусто — " + _defaultFolder);
            AddRow(files, "Имя файла:", _nameTemplate);
            AddRow(files, "Подпапки:", _subfolderTemplate);
            AddHint(files, "Поля: {Name} {PartNumber} {Description} {Material}\n{Thickness:0.0} {Qty} {Assembly}\nи пользовательские iProperties, например {Заказ}.\n" +
                           @"Подпапки: {Material}\{Thickness:0.0}мм");
            AddRow(files, "Пример:", _preview);
            AddRow(files, "Кол-во изделий:", _multiplier);

            var dxf = NewGrid();
            AddRow(dxf, "Версия AutoCAD:", _acadVersion);
            AddRow(dxf, "Слой контура:", _outerLayer);
            AddRow(dxf, "Слой отверстий:", _innerLayer);
            AddRow(dxf, "", _bendLines);
            AddRow(dxf, "", _tangentLines);

            var other = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true, Dock = DockStyle.Fill, WrapContents = false };
            other.Controls.AddRange(new Control[] { _createFlat, _overwrite, _csv, _openFolder });

            _nameTemplate.TextChanged += (s, e) => UpdatePreview();
            _subfolderTemplate.TextChanged += (s, e) => UpdatePreview();
            _multiplier.ValueChanged += (s, e) => { UpdatePreview(); UpdateTotals(); };

            var panel = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown, WrapContents = false, AutoScroll = true };
            panel.Controls.Add(Group("Файлы", files));
            panel.Controls.Add(Group("Параметры DXF", dxf));
            panel.Controls.Add(Group("Дополнительно", other));
            return panel;
        }

        private Control BuildBottomPanel()
        {
            _export.Click += (s, e) => RunExport();

            var panel = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 4, AutoSize = true };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 60));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 40));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.Controls.Add(_status, 0, 0);
            panel.Controls.Add(_progress, 1, 0);
            panel.Controls.Add(_export, 2, 0);
            panel.Controls.Add(_close, 3, 0);
            return panel;
        }

        private static TableLayoutPanel NewGrid()
        {
            var grid = new TableLayoutPanel { ColumnCount = 2, AutoSize = true, Dock = DockStyle.Fill };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            return grid;
        }

        private static GroupBox Group(string title, Control content)
        {
            var box = new GroupBox { Text = title, Width = 330, AutoSize = true, Padding = new Padding(6) };
            box.Controls.Add(content);
            return box;
        }

        private static Control WithButton(Control control, Button button)
        {
            var panel = new TableLayoutPanel { ColumnCount = 2, Dock = DockStyle.Fill, AutoSize = true, Margin = Padding.Empty };
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
            panel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
            panel.Controls.Add(control, 0, 0);
            panel.Controls.Add(button, 1, 0);
            return panel;
        }

        private static void AddRow(TableLayoutPanel grid, string caption, Control control)
        {
            int row = grid.RowCount++;
            grid.Controls.Add(new Label { Text = caption, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
            grid.Controls.Add(control, 1, row);
        }

        private static void AddHint(TableLayoutPanel grid, string text)
        {
            grid.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                ForeColor = SystemColors.GrayText,
                MaximumSize = new Size(220, 0)
            }, 1, grid.RowCount++);
        }

        #endregion

        #region Таблица деталей

        private void FillGrid()
        {
            foreach (var part in _parts)
            {
                var p = part.Info;
                int index = _grid.Rows.Add(true, p.Name, p.PartNumber, p.Description, p.Material,
                    p.ThicknessMm, p.Quantity, p.Quantity * (int)_multiplier.Value, "");
                _grid.Rows[index].Tag = part;
            }
            _grid.Columns[Col.Thickness].DefaultCellStyle.Format = "0.##";
        }

        private void OnCellValidating(object sender, DataGridViewCellValidatingEventArgs e)
        {
            if (_grid.Columns[e.ColumnIndex].Name != Col.Qty)
                return;
            if (!int.TryParse(Convert.ToString(e.FormattedValue), out int qty) || qty < 1)
            {
                e.Cancel = true;
                _status.Text = "Количество должно быть целым числом больше нуля";
            }
        }

        private void OnCellEndEdit(object sender, DataGridViewCellEventArgs e)
        {
            if (_grid.Columns[e.ColumnIndex].Name != Col.Qty)
                return;
            var row = _grid.Rows[e.RowIndex];
            ((CollectedPart)row.Tag).Info.Quantity = (int)row.Cells[Col.Qty].Value;
            row.Cells[Col.Total].Value = (int)row.Cells[Col.Qty].Value * (int)_multiplier.Value;
            _status.Text = string.Empty;
        }

        private void UpdateTotals()
        {
            foreach (DataGridViewRow row in _grid.Rows)
                row.Cells[Col.Total].Value = ((CollectedPart)row.Tag).Info.Quantity * (int)_multiplier.Value;
        }

        private void ApplyFilter()
        {
            string text = _filter.Text.Trim();
            _grid.CurrentCell = null;
            foreach (DataGridViewRow row in _grid.Rows)
            {
                var p = ((CollectedPart)row.Tag).Info;
                row.Visible = text.Length == 0 || new[] { p.Name, p.PartNumber, p.Description, p.Material }
                    .Any(v => v != null && v.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0);
            }
        }

        /// <summary>Выбор/снятие действует только на видимые строки — удобно вместе с поиском.</summary>
        private void SetChecked(bool value)
        {
            _grid.EndEdit();
            foreach (DataGridViewRow row in _grid.Rows)
                if (row.Visible)
                    row.Cells[Col.Export].Value = value;
            UpdateCounter();
        }

        private List<DataGridViewRow> CheckedRows() =>
            _grid.Rows.Cast<DataGridViewRow>().Where(r => true.Equals(r.Cells[Col.Export].Value)).ToList();

        private void UpdateCounter() => _counter.Text = $"Выбрано: {CheckedRows().Count} из {_grid.Rows.Count}";

        private static void ShowResult(DataGridViewRow row, ExportResult result)
        {
            string text = CsvReport.StatusText(result.Status);
            if (result.Message.Length > 0)
                text += " — " + result.Message;
            row.Cells[Col.Status].Value = text;
            row.Cells[Col.Status].ToolTipText = result.FilePath;

            switch (result.Status)
            {
                case ExportStatus.Exported:
                    row.DefaultCellStyle.BackColor = Color.FromArgb(226, 245, 226);
                    break;
                case ExportStatus.Skipped:
                    row.DefaultCellStyle.BackColor = Color.FromArgb(255, 244, 214);
                    break;
                default:
                    row.DefaultCellStyle.BackColor = Color.FromArgb(253, 222, 222);
                    break;
            }
        }

        #endregion

        #region Настройки

        private void LoadSettings()
        {
            _folder.Text = _settings.OutputFolder;
            _nameTemplate.Text = _settings.FileNameTemplate;
            _subfolderTemplate.Text = _settings.SubfolderTemplate;
            _multiplier.Value = Math.Max(1, Math.Min(10000, _settings.QuantityMultiplier));
            _acadVersion.Items.AddRange(AcadVersions);
            _acadVersion.SelectedItem = AcadVersions.Contains(_settings.AcadVersion) ? _settings.AcadVersion : "2000";
            _outerLayer.Text = _settings.OuterProfileLayer;
            _innerLayer.Text = _settings.InteriorProfilesLayer;
            _bendLines.Checked = _settings.ExportBendLines;
            _tangentLines.Checked = _settings.ExportTangentLines;
            _createFlat.Checked = _settings.CreateMissingFlatPatterns;
            _overwrite.Checked = _settings.OverwriteExisting;
            _csv.Checked = _settings.WriteCsvReport;
            _openFolder.Checked = _settings.OpenFolderAfterExport;
        }

        private void ApplySettings()
        {
            _settings.OutputFolder = _folder.Text.Trim();
            _settings.FileNameTemplate = string.IsNullOrWhiteSpace(_nameTemplate.Text)
                ? new FlatPatternExportSettings().FileNameTemplate
                : _nameTemplate.Text.Trim();
            _settings.SubfolderTemplate = _subfolderTemplate.Text.Trim();
            _settings.QuantityMultiplier = (int)_multiplier.Value;
            _settings.AcadVersion = (string)_acadVersion.SelectedItem;
            _settings.OuterProfileLayer = _outerLayer.Text.Trim();
            _settings.InteriorProfilesLayer = _innerLayer.Text.Trim();
            _settings.ExportBendLines = _bendLines.Checked;
            _settings.ExportTangentLines = _tangentLines.Checked;
            _settings.CreateMissingFlatPatterns = _createFlat.Checked;
            _settings.OverwriteExisting = _overwrite.Checked;
            _settings.WriteCsvReport = _csv.Checked;
            _settings.OpenFolderAfterExport = _openFolder.Checked;
        }

        private void UpdatePreview()
        {
            var sample = _parts.FirstOrDefault()?.Info ?? new SheetMetalPart { Name = "Деталь", ThicknessMm = 3, Quantity = 1 };
            var values = sample.ToTemplateValues((int)_multiplier.Value, _assemblyName);
            string sub = FileNameTemplate.SanitizeRelativePath(FileNameTemplate.Render(_subfolderTemplate.Text, values));
            string name = FileNameTemplate.SanitizeFileName(FileNameTemplate.Render(_nameTemplate.Text, values));
            _preview.Text = (sub.Length > 0 ? sub + "\\" : string.Empty) + name + ".dxf";
        }

        private void Browse()
        {
            using (var dialog = new FolderBrowserDialog { SelectedPath = _folder.Text.Length > 0 ? _folder.Text : _defaultFolder })
            {
                if (dialog.ShowDialog(this) == DialogResult.OK)
                    _folder.Text = dialog.SelectedPath;
            }
        }

        #endregion

        #region Экспорт

        private void RunExport()
        {
            _grid.EndEdit();
            var rows = CheckedRows();
            if (rows.Count == 0)
            {
                _status.Text = "Ничего не выбрано";
                return;
            }

            ApplySettings();
            _context.Settings.Save(_settingsKey, _settings);

            string outputFolder = _settings.OutputFolder.Length > 0 ? _settings.OutputFolder : _defaultFolder;
            var exporter = new FlatPatternExporter(_settings, outputFolder, _assemblyName);
            var results = new List<ExportResult>(rows.Count);

            SetBusy(true, rows.Count);
            bool screenUpdating = _context.App.ScreenUpdating;
            try
            {
                _context.App.ScreenUpdating = false;
                foreach (var row in rows)
                {
                    var part = (CollectedPart)row.Tag;
                    _status.Text = part.Info.Name;
                    _status.Refresh();

                    var result = exporter.Export(part);
                    results.Add(result);
                    Log(result);
                    ShowResult(row, result);

                    _progress.Value++;
                    _grid.Refresh();
                }
            }
            finally
            {
                _context.App.ScreenUpdating = screenUpdating;
                SetBusy(false, 0);
            }

            string reportPath = WriteReport(results, outputFolder);
            Summarize(results, outputFolder, reportPath);

            if (_settings.OpenFolderAfterExport && Directory.Exists(outputFolder))
                Process.Start("explorer.exe", "\"" + outputFolder + "\"");
        }

        private string WriteReport(List<ExportResult> results, string outputFolder)
        {
            if (!_settings.WriteCsvReport)
                return null;
            try
            {
                Directory.CreateDirectory(outputFolder);
                string path = Path.Combine(outputFolder, $"Отчет_{_assemblyName}_{DateTime.Now.ToString("yyyy-MM-dd_HH-mm", CultureInfo.InvariantCulture)}.csv");
                CsvReport.Write(path, results, _settings.QuantityMultiplier);
                return path;
            }
            catch (Exception ex)
            {
                _context.Log.Error("Не удалось записать CSV-отчёт", ex);
                return null;
            }
        }

        private void Summarize(List<ExportResult> results, string outputFolder, string reportPath)
        {
            int exported = results.Count(r => r.Status == ExportStatus.Exported);
            int skipped = results.Count(r => r.Status == ExportStatus.Skipped);
            int failed = results.Count(r => r.Status == ExportStatus.Failed);

            _status.Text = $"Готово: экспортировано {exported}, пропущено {skipped}, ошибок {failed}" +
                           (reportPath != null ? $". Отчёт: {Path.GetFileName(reportPath)}" : string.Empty);

            if (failed > 0)
                MessageBox.Show(this, $"Не удалось экспортировать деталей: {failed}.\nПричины — в колонке «Статус».\nПапка: {outputFolder}",
                    "InvAuto", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        private void Log(ExportResult result)
        {
            string line = $"{result.Part.Name}: {CsvReport.StatusText(result.Status)} {result.FilePath} {result.Message}";
            if (result.Status == ExportStatus.Failed)
                _context.Log.Error(line);
            else
                _context.Log.Info(line);
        }

        private void SetBusy(bool busy, int steps)
        {
            UseWaitCursor = busy;
            _export.Enabled = _close.Enabled = _grid.Enabled = !busy;
            _progress.Visible = busy;
            _progress.Maximum = Math.Max(1, steps);
            _progress.Value = 0;
            Refresh();
        }

        #endregion

        private static class Col
        {
            public const string Export = "Export";
            public const string Name = "Name";
            public const string PartNumber = "PartNumber";
            public const string Description = "Description";
            public const string Material = "Material";
            public const string Thickness = "Thickness";
            public const string Qty = "Qty";
            public const string Total = "Total";
            public const string Status = "Status";
        }
    }
}

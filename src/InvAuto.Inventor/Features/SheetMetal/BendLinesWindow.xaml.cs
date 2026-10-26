using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using InvAuto.Core.Bends;
using InvAuto.Core.Preview;
using InvAuto.Framework.Ui;
using InvAuto.InventorApi;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>
    /// Индивидуальная настройка линий гиба одной детали. Слева — линии (номер, направление, длина,
    /// что применено), в центре — параметры выделенных линий, справа — развертка с номерами линий
    /// и подсветкой выделенных и расчёт. Линия без своих параметров берёт параметры детали.
    /// </summary>
    public partial class BendLinesWindow : Window
    {
        private readonly FlatPreview _preview;
        private readonly BendLineOptions _partOptions;
        private readonly IReadOnlyCollection<string> _layers;
        private readonly List<LineItem> _items;
        private readonly Dictionary<string, BendLineOptions> _own;
        private bool _loading;

        /// <param name="partName">Имя детали для заголовка.</param>
        /// <param name="preview">Сформированная развертка детали (исходный DXF).</param>
        /// <param name="partOptions">Параметры детали — то, что действует для линий без своих.</param>
        /// <param name="partSummary">Описание параметров детали: «общие: …» или «своя: …».</param>
        /// <param name="ownLines">Текущие свои параметры линий: ключ линии → параметры.</param>
        public BendLinesWindow(string partName, FlatPreview preview, BendLineOptions partOptions, string partSummary,
            IReadOnlyDictionary<string, BendLineOptions> ownLines, IReadOnlyCollection<string> layers)
        {
            _loading = true;
            InitializeComponent();
            _preview = preview;
            _partOptions = partOptions;
            _layers = layers;

            var lines = DxfBendProcessor.FindBendLines(preview.RawText, layers);
            _own = ownLines
                .Where(p => lines.Any(l => l.Key == p.Key)) // настройки линий, которых больше нет в детали, не переносим
                .ToDictionary(p => p.Key, p => p.Value.Clone());
            _items = lines.Select(l => new LineItem(l)).ToList();

            PartTitle.Text = $"Линии гиба детали «{partName}»";
            PartText.Text = $"Параметры детали: {partSummary}. Линия со своими параметрами обрабатывается по ним, " +
                            "остальные — как деталь.";
            AsPartBox.Content = "Как у детали (" + partOptions.FullSummary + ")";
            Editor.Fallback = partOptions;

            Preview.Labels = lines.Select(l => new FlatLabel(new Point2((l.X1 + l.X2) / 2, (l.Y1 + l.Y2) / 2), l.Index.ToString())).ToList();
            LinesList.ItemsSource = _items;
            RefreshItems();
            _loading = false;

            if (_items.Count > 0)
                LinesList.SelectedIndex = 0;
            else
                ShowSelection();
        }

        /// <summary>Свои параметры линий после «ОК»: ключ линии → параметры. Остальные линии — как деталь.</summary>
        public IReadOnlyDictionary<string, BendLineOptions> Result { get; private set; }

        private List<LineItem> Selected => LinesList.SelectedItems.Cast<LineItem>().OrderBy(i => i.Line.Index).ToList();

        private BendLineOptions Effective(BendLineInfo line) => _own.TryGetValue(line.Key, out var own) ? own : _partOptions;

        #region Выделение и редактирование

        private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_loading)
                ShowSelection();
        }

        /// <summary>Показывает параметры выделенных линий в форме и подсвечивает их на развертке.</summary>
        private void ShowSelection()
        {
            var selected = Selected;
            SelectionTitle.Text = selected.Count == 0 ? "Выделите линию в списке"
                : selected.Count == 1 ? $"Параметры линии {selected[0].Line.Index}"
                : $"Параметры линий: {string.Join(", ", selected.Select(s => s.Line.Index))}";

            bool inherit = selected.All(s => !_own.ContainsKey(s.Line.Key));
            _loading = true;
            try
            {
                AsPartBox.IsChecked = inherit;
                Editor.SetOptions(selected.Count == 0 ? _partOptions : Effective(selected[0].Line));
            }
            finally
            {
                _loading = false;
            }

            AsPartBox.IsEnabled = selected.Count > 0;
            Editor.IsEnabled = selected.Count > 0 && !inherit;
            ErrorText.Text = string.Empty;
            UpdatePreview();
        }

        private void OnAsPartClick(object sender, RoutedEventArgs e)
        {
            var selected = Selected;
            if (AsPartBox.IsChecked == true)
            {
                foreach (var item in selected)
                    _own.Remove(item.Line.Key);
                _loading = true;
                Editor.SetOptions(_partOptions);
                _loading = false;
            }
            else
            {
                // Своя настройка начинается с того, что действовало, — дальше правится в форме.
                var start = Editor.GetOptions(out _);
                foreach (var item in selected)
                    _own[item.Line.Key] = start.Clone();
            }
            Editor.IsEnabled = AsPartBox.IsChecked != true && selected.Count > 0;
            RefreshItems();
            UpdatePreview();
        }

        private void OnEditorChanged(object sender, EventArgs e)
        {
            if (_loading)
                return;
            var options = Editor.GetOptions(out string error);
            ErrorText.Text = error;
            foreach (var item in Selected)
                _own[item.Line.Key] = options.Clone();
            RefreshItems();
            UpdatePreview();
        }

        private void OnResetAll(object sender, RoutedEventArgs e)
        {
            _own.Clear();
            RefreshItems();
            ShowSelection();
        }

        #endregion

        #region Развертка и расчёт

        private void RefreshItems()
        {
            foreach (var item in _items)
                item.Update(_own.TryGetValue(item.Line.Key, out var own) ? own : null, _partOptions);
        }

        private void UpdatePreview()
        {
            try
            {
                var (drawing, processed) = _preview.Render(Effective, _layers);
                Preview.Drawing = drawing;

                var selected = new HashSet<string>(Selected.Select(s => s.Line.Key));
                Preview.Highlights = processed
                    .Where(p => selected.Contains(p.Line.Key))
                    .Select(p => new Polyline2(p.Line.Layer, new[] { new Point2(p.Line.X1, p.Line.Y1), new Point2(p.Line.X2, p.Line.Y2) }, false))
                    .ToList();

                LayoutList.ItemsSource = processed
                    .Where(p => selected.Contains(p.Line.Key))
                    .Select(p => new
                    {
                        Title = $"Линия {p.Line.Index} · {BendOptionsEditor.Format(p.Line.Length)} мм",
                        p.Layout
                    })
                    .ToList();
            }
            catch (Exception ex)
            {
                ErrorText.Text = "Не удалось показать развертку: " + ex.Message;
            }
        }

        #endregion

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (_own.Count > 0 && Editor.IsEnabled)
            {
                Editor.GetOptions(out string error);
                if (error.Length > 0)
                {
                    ErrorText.Text = error;
                    return;
                }
            }
            Result = _own;
            DialogResult = true;
        }

        /// <summary>Строка списка линий.</summary>
        public sealed class LineItem : ObservableObject
        {
            private string _summary = string.Empty;
            private bool _isOwn;

            public LineItem(BendLineInfo line) => Line = line;

            public BendLineInfo Line { get; }
            public int Number => Line.Index;
            public string Title => $"{(Line.IsDown ? "Вниз" : "Вверх")} · {BendOptionsEditor.Format(Line.Length)} мм";

            public string Summary
            {
                get => _summary;
                private set => Set(ref _summary, value);
            }

            public bool IsOwn
            {
                get => _isOwn;
                private set => Set(ref _isOwn, value);
            }

            public void Update(BendLineOptions own, BendLineOptions part)
            {
                IsOwn = own != null;
                Summary = own != null ? "своя: " + own.FullSummary : "как у детали: " + part.FullSummary;
            }
        }
    }
}

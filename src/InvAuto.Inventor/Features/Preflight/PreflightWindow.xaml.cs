using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using System.Windows.Input;
using InvAuto.Core.Preflight;

namespace InvAuto.Features.Preflight
{
    /// <summary>
    /// Замечания проверки по деталям. Два режима: просмотр (кнопка «Проверить») и подтверждение перед
    /// выгрузкой — тогда есть кнопка «Выгрузить всё равно». Из окна можно перейти к детали или её развертке.
    /// </summary>
    public partial class PreflightWindow : Window
    {
        private readonly List<Entry> _entries;

        /// <param name="results">Замечания по ключу детали (путь файла).</param>
        /// <param name="titles">Подпись детали по ключу (обозначение или имя файла).</param>
        /// <param name="exportCount">Не null — режим подтверждения выгрузки этого числа деталей.</param>
        public PreflightWindow(IReadOnlyDictionary<string, IReadOnlyList<PreflightIssue>> results,
            IReadOnlyDictionary<string, string> titles, int? exportCount = null)
        {
            InitializeComponent();
            _entries = results
                .SelectMany(r => r.Value.Select(i => new Entry(r.Key, titles.TryGetValue(r.Key, out string t) ? t : r.Key, i)))
                .OrderByDescending(e => e.Rank).ThenBy(e => e.Part, StringComparer.CurrentCultureIgnoreCase)
                .ToList();

            var (errors, warnings) = PreflightChecker.Summary(results);
            int clean = results.Count - errors - warnings;
            SummaryText.Text = $"Деталей: {results.Count} · с ошибками: {errors} · с предупреждениями: {warnings} · без замечаний: {clean}";

            if (exportCount is int count)
            {
                Title = "Проверка перед выгрузкой";
                HeadlineText.Text = errors > 0 ? "Есть ошибки" : "Есть предупреждения";
                ExportButton.Content = $"Выгрузить всё равно ({count})";
                ExportButton.Visibility = Visibility.Visible;
                CloseButton.Content = "Отмена";
            }
            else
            {
                HeadlineText.Text = errors > 0 ? "Есть ошибки" : warnings > 0 ? "Есть предупреждения" : "Замечаний нет";
            }

            Fill();
        }

        /// <summary>Деталь, к которой пользователь хочет перейти (null — не выбрана).</summary>
        public string SelectedKey { get; private set; }

        /// <summary>Открыть развертку выбранной детали.</summary>
        public bool OpenViewer { get; private set; }

        /// <summary>Подтверждена выгрузка несмотря на замечания.</summary>
        public bool ExportConfirmed { get; private set; }

        private void Fill()
        {
            bool info = InfoBox.IsChecked == true;
            var visible = _entries.Where(e => info || e.Severity != IssueSeverity.Info).ToList();
            IssuesGrid.ItemsSource = visible;
            EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (visible.Count > 0)
                IssuesGrid.SelectedIndex = 0;
        }

        private void OnFilterChanged(object sender, RoutedEventArgs e)
        {
            if (IsLoaded)
                Fill();
        }

        private bool Pick(bool viewer)
        {
            if (!(IssuesGrid.SelectedItem is Entry entry))
                return false;
            SelectedKey = entry.Key;
            OpenViewer = viewer;
            DialogResult = false;
            return true;
        }

        private void OnShowPart(object sender, RoutedEventArgs e) => Pick(viewer: false);

        private void OnOpenViewer(object sender, RoutedEventArgs e) => Pick(viewer: true);

        private void OnRowDoubleClick(object sender, MouseButtonEventArgs e)
        {
            if (Pick(viewer: false))
                e.Handled = true;
        }

        private void OnExport(object sender, RoutedEventArgs e)
        {
            ExportConfirmed = true;
            DialogResult = true;
        }

        private sealed class Entry
        {
            public Entry(string key, string part, PreflightIssue issue)
            {
                Key = key;
                Part = part;
                Issue = issue;
            }

            public string Key { get; }
            public string Part { get; }
            public PreflightIssue Issue { get; }
            public IssueSeverity Severity => Issue.Severity;
            public string SeverityText => Issue.SeverityText;
            public string Message => Issue.Message;
            public int Rank => (int)Issue.Severity;
        }
    }
}

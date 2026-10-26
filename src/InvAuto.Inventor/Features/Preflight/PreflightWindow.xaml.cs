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
        private readonly int? _exportCount;
        private readonly Action<string, PreflightIssue, bool> _dismiss;

        /// <param name="results">Замечания по ключу детали (путь файла).</param>
        /// <param name="titles">Подпись детали по ключу (обозначение или имя файла).</param>
        /// <param name="exportCount">Не null — режим подтверждения выгрузки этого числа деталей.</param>
        /// <param name="dismiss">Пропустить (true) или вернуть (false) замечание детали (ключ детали); null — без пропуска.</param>
        public PreflightWindow(IReadOnlyDictionary<string, IReadOnlyList<PreflightIssue>> results,
            IReadOnlyDictionary<string, string> titles, int? exportCount = null, Action<string, PreflightIssue, bool> dismiss = null)
        {
            InitializeComponent();
            _exportCount = exportCount;
            _totalParts = results.Count;
            _dismiss = dismiss;
            _entries = results
                .SelectMany(r => r.Value.Select(i => new Entry(r.Key, titles.TryGetValue(r.Key, out string t) ? t : r.Key, i)))
                .OrderByDescending(e => e.Rank).ThenBy(e => e.Part, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
            DismissButton.Visibility = dismiss == null ? Visibility.Collapsed : Visibility.Visible;
            DismissedBox.Visibility = _entries.Any(e => e.Issue.CanDismiss) && dismiss != null ? Visibility.Visible : Visibility.Collapsed;

            if (exportCount is int count)
            {
                Title = "Проверка перед выгрузкой";
                ExportButton.Content = $"Выгрузить всё равно ({count})";
                ExportButton.Visibility = Visibility.Visible;
                CloseButton.Content = "Отмена";
            }

            UpdateSummary();
            Fill();
        }

        /// <summary>Итог по текущим отметкам: пропущенные замечания не считаются.</summary>
        private void UpdateSummary()
        {
            var results = _entries.GroupBy(e => e.Key, StringComparer.OrdinalIgnoreCase)
                .ToDictionary(g => g.Key, g => (IReadOnlyList<PreflightIssue>)g.Select(e => e.Issue).ToList(), StringComparer.OrdinalIgnoreCase);
            int parts = Math.Max(results.Count, _totalParts);
            var (errors, warnings) = PreflightChecker.Summary(results);
            int dismissed = _entries.Count(e => e.Dismissed);
            SummaryText.Text = $"Деталей: {parts} · с ошибками: {errors} · с предупреждениями: {warnings} · без замечаний: {parts - errors - warnings}" +
                               (dismissed > 0 ? $" · пропущено замечаний: {dismissed}" : string.Empty);
            HeadlineText.Text = errors > 0 ? "Есть ошибки" : warnings > 0 ? "Есть предупреждения"
                : _exportCount != null ? "Замечаний не осталось — можно выгружать" : "Замечаний нет";
            if (_exportCount is int count)
                ExportButton.Content = errors + warnings > 0 ? $"Выгрузить всё равно ({count})" : $"Выгрузить ({count})";
        }

        private int _totalParts;

        /// <summary>Деталь, к которой пользователь хочет перейти (null — не выбрана).</summary>
        public string SelectedKey { get; private set; }

        /// <summary>Открыть развертку выбранной детали.</summary>
        public bool OpenViewer { get; private set; }

        /// <summary>Подтверждена выгрузка несмотря на замечания.</summary>
        public bool ExportConfirmed { get; private set; }

        private void Fill(Entry select = null)
        {
            bool info = InfoBox.IsChecked == true;
            bool dismissed = DismissedBox.IsChecked == true;
            var visible = _entries.Where(e => (info || e.Severity != IssueSeverity.Info) && (dismissed || !e.Dismissed)).ToList();
            IssuesGrid.ItemsSource = visible;
            EmptyText.Visibility = visible.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
            if (visible.Count > 0)
                IssuesGrid.SelectedIndex = select != null && visible.Contains(select) ? visible.IndexOf(select) : 0;
            UpdateDismissButton();
        }

        private void OnSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e) => UpdateDismissButton();

        private void UpdateDismissButton()
        {
            var entry = IssuesGrid.SelectedItem as Entry;
            DismissButton.IsEnabled = entry != null && entry.Issue.CanDismiss;
            DismissButton.Content = entry != null && entry.Dismissed ? "Вернуть" : "Пропустить";
        }

        /// <summary>Пропустить выбранное замечание (или вернуть пропущенное) — для этой детали, до её изменения.</summary>
        private void OnDismiss(object sender, RoutedEventArgs e)
        {
            if (_dismiss == null || !(IssuesGrid.SelectedItem is Entry entry) || !entry.Issue.CanDismiss)
                return;
            bool dismiss = !entry.Dismissed;
            _dismiss(entry.Key, entry.Issue, dismiss);
            entry.Issue = entry.Issue.WithDismissed(dismiss);

            // Следующее непропущенное — чтобы можно было пропускать подряд.
            int index = _entries.IndexOf(entry);
            var next = dismiss && DismissedBox.IsChecked != true
                ? _entries.Skip(index + 1).Concat(_entries.Take(index)).FirstOrDefault(x => !x.Dismissed)
                : entry;
            UpdateSummary();
            Fill(next);
        }

        private void OnGridKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Delete)
                return;
            OnDismiss(sender, e);
            e.Handled = true;
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

            /// <summary>Меняется при пропуске (строки пересоздаются заново через ItemsSource).</summary>
            public PreflightIssue Issue { get; set; }

            public bool Dismissed => Issue.Dismissed;
            public IssueSeverity Severity => Issue.Severity;
            public string SeverityText => Issue.SeverityText;
            public string Message => Issue.Message;
            public int Rank => (int)Issue.Severity;
        }
    }
}

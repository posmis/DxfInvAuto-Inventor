using System;
using System.ComponentModel;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Input;
using InvAuto.Core.Logging;
using InvAuto.Core.Maintenance;
using InvAuto.Framework.Ui;

namespace InvAuto.Features.Service
{
    /// <summary>
    /// Подтверждение очистки служебной папки: полный список файлов до удаления, явное согласие
    /// флажком, затем итог по каждому файлу. Удаляется только то, что показано в списке.
    /// </summary>
    public partial class CleanupConfirmWindow : Window
    {
        private readonly CleanupPlan _plan;
        private readonly string _what;
        private readonly ILogger _log;
        private readonly FileRow[] _rows;
        private bool _running;

        /// <param name="what">Что очищается, для заголовка и журнала: «кэш разверток», «журнал».</param>
        /// <param name="consequence">Что будет после удаления и что не затрагивается.</param>
        public CleanupConfirmWindow(string what, string consequence, CleanupPlan plan, ILogger log)
        {
            InitializeComponent();
            _plan = plan;
            _what = what;
            _log = log;
            _rows = plan.Files.Select(f => new FileRow(f)).ToArray();

            Title = "Очистка: " + what;
            HeadlineText.Text = $"Будет удалено файлов: {plan.Files.Count} ({SafeCleanup.FormatSize(plan.TotalBytes)}) — {what}";
            FolderText.Text = "Папка: " + plan.Root;
            ConsequenceText.Text = consequence;
            DeleteButton.Content = $"Удалить файлы ({plan.Files.Count})";
            FilesGrid.ItemsSource = _rows;

            if (plan.Skipped.Count > 0)
            {
                SkippedTitle.Text = $"Не будут удалены ({plan.Skipped.Count}):";
                SkippedTitle.Visibility = Visibility.Visible;
                SkippedList.ItemsSource = plan.Skipped;
                SkippedList.Visibility = Visibility.Visible;
            }

            Closing += OnClosing;
            Loaded += (s, e) => CloseButton.Focus(); // по умолчанию в фокусе «Отмена», а не удаление
        }

        /// <summary>Итог удаления; null — пользователь отказался.</summary>
        public CleanupResult Result { get; private set; }

        private void OnConfirmChanged(object sender, RoutedEventArgs e) =>
            DeleteButton.IsEnabled = ConfirmBox.IsChecked == true && Result == null && !_running;

        private async void OnDelete(object sender, RoutedEventArgs e)
        {
            if (ConfirmBox.IsChecked != true || Result != null || _running)
                return;

            _running = true;
            DeleteButton.IsEnabled = false;
            CloseButton.IsEnabled = false;
            ConfirmBox.IsEnabled = false;
            Mouse.OverrideCursor = Cursors.Wait;
            _log.Info($"Очистка ({_what}): {_plan.Root}, файлов в списке: {_plan.Files.Count}, {SafeCleanup.FormatSize(_plan.TotalBytes)}");

            CleanupResult result;
            try
            {
                result = await Task.Run(() => SafeCleanup.Execute(_plan));
            }
            catch (Exception ex)
            {
                _log.Error($"Очистка ({_what}) прервана", ex);
                result = new CleanupResult();
                result.Failed.Add((_plan.Root, ex.Message));
            }
            finally
            {
                Mouse.OverrideCursor = null;
                _running = false;
            }

            ShowResult(result);
        }

        private void ShowResult(CleanupResult result)
        {
            Result = result;
            var failed = result.Failed.ToDictionary(f => f.Path, f => f.Reason, StringComparer.OrdinalIgnoreCase);
            var journal = new StringBuilder();
            foreach (var row in _rows)
            {
                row.State = failed.TryGetValue(row.Item.RelativePath, out string reason) ? "не удалён: " + reason : "удалён";
                journal.AppendLine().Append("  ").Append(row.State).Append(" — ").Append(row.Item.FullPath);
            }
            _log.Info($"Очистка ({_what}) завершена: удалено {result.Deleted}, освобождено {SafeCleanup.FormatSize(result.FreedBytes)}, " +
                      $"не удалено {result.Failed.Count}{journal}");

            StateColumn.Visibility = Visibility.Visible;
            ConfirmBox.Visibility = Visibility.Collapsed;
            ResultText.Visibility = Visibility.Visible;
            ResultText.Text = $"Удалено файлов: {result.Deleted}, освобождено {SafeCleanup.FormatSize(result.FreedBytes)}." +
                              (result.Failed.Count > 0
                                  ? $" Не удалось удалить: {result.Failed.Count} — причины в колонке «Итог» и в журнале."
                                  : string.Empty);
            ResultText.Foreground = (System.Windows.Media.Brush)FindResource(result.Failed.Count > 0 ? "WarningBrush" : "OkBrush");
            HeadlineText.Text = "Очистка завершена — " + _what;
            WarningMark.Visibility = Visibility.Collapsed;
            DeleteButton.Visibility = Visibility.Collapsed;
            CloseButton.Content = "Закрыть";
            CloseButton.IsEnabled = true;
            CloseButton.Focus();
        }

        private void OnCopy(object sender, RoutedEventArgs e)
        {
            var text = new StringBuilder();
            text.AppendLine(_plan.Root);
            foreach (var row in _rows)
            {
                text.Append(row.Item.RelativePath).Append('\t').Append(row.Size).Append('\t').Append(row.Modified);
                if (!string.IsNullOrEmpty(row.State))
                    text.Append('\t').Append(row.State);
                text.AppendLine();
            }
            try
            {
                Clipboard.SetText(text.ToString());
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Не удалось скопировать: " + ex.Message, "InvAuto", MessageBoxButton.OK, MessageBoxImage.Warning);
            }
        }

        private void OnClosing(object sender, CancelEventArgs e)
        {
            if (_running)
                e.Cancel = true; // не бросаем удаление на середине
        }

        private sealed class FileRow : ObservableObject
        {
            private string _state;

            public FileRow(CleanupItem item)
            {
                Item = item;
            }

            public CleanupItem Item { get; }
            public string Name => Item.RelativePath;
            public long Length => Item.Length;
            public string Size => SafeCleanup.FormatSize(Item.Length);
            public DateTime Time => Item.LastWriteUtc;
            public string Modified => Item.LastWriteUtc.ToLocalTime().ToString("dd.MM.yyyy HH:mm", CultureInfo.CurrentCulture);

            public string State
            {
                get => _state;
                set => Set(ref _state, value);
            }
        }
    }
}

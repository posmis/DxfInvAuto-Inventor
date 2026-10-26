using System;
using System.Windows;
using InvAuto.Core.Reports;

namespace InvAuto.Features.Reports
{
    /// <summary>Параметры ведомости: шапка (название, изделие, дата), какие детали, картинки, форматы.</summary>
    public partial class ReportWindow : Window
    {
        private readonly ReportSettings _settings;

        public ReportWindow(ReportSettings settings, string product, int checkedCount, int totalCount)
        {
            InitializeComponent();
            _settings = settings;

            TitleBox.Text = settings.Title;
            ProductBox.Text = product;
            DateBox.SelectedDate = DateTime.Today;
            CheckedOnly.Content = $"Отмеченные галочкой ({checkedCount})";
            AllRows.Content = $"Весь список ({totalCount})";
            CheckedOnly.IsEnabled = checkedCount > 0;
            CheckedOnly.IsChecked = settings.OnlyChecked && checkedCount > 0;
            AllRows.IsChecked = CheckedOnly.IsChecked != true;
            FlatsBox.IsChecked = settings.IncludeFlats;
            ExcelBox.IsChecked = settings.Excel;
            PdfBox.IsChecked = settings.Pdf;
            OpenBox.IsChecked = settings.OpenAfterSave;
            Loaded += (s, e) => TitleBox.Focus();
        }

        public string ReportTitle { get; private set; }
        public string Product { get; private set; }
        public DateTime Date { get; private set; }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (ExcelBox.IsChecked != true && PdfBox.IsChecked != true)
            {
                ErrorText.Text = "Выберите хотя бы один формат";
                return;
            }

            string title = TitleBox.Text.Trim();
            ReportTitle = title.Length > 0 ? title : ProductionReport.DefaultTitle;
            Product = ProductBox.Text.Trim();
            Date = DateBox.SelectedDate ?? DateTime.Today;

            _settings.Title = ReportTitle;
            _settings.OnlyChecked = CheckedOnly.IsChecked == true;
            _settings.IncludeFlats = FlatsBox.IsChecked == true;
            _settings.Excel = ExcelBox.IsChecked == true;
            _settings.Pdf = PdfBox.IsChecked == true;
            _settings.OpenAfterSave = OpenBox.IsChecked == true;
            DialogResult = true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows;
using InvAuto.Core.Bends;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>
    /// Параметры линий гиба детали: общие для всех деталей или персональные для выделенных.
    /// Слева — форма параметров, справа — расчёт по реальным линиям гиба детали или на примере.
    /// Отдельные линии настраиваются в окне BendLinesWindow.
    /// </summary>
    public partial class BendSettingsWindow : Window
    {
        private readonly BendLineOptions _defaults;
        private readonly bool _personal;
        private readonly IReadOnlyList<double> _sampleLines;
        private readonly bool _loading;

        /// <param name="initial">С чего начать редактирование.</param>
        /// <param name="defaults">Общие параметры (для персонального режима — что будет при «использовать общие»).</param>
        /// <param name="personal">true — настройка выделенных деталей, false — общие параметры.</param>
        /// <param name="usesDefaults">Выделенные детали сейчас без персональных параметров.</param>
        /// <param name="scope">Кого касается: «Деталь …» / «Выделено деталей: 3».</param>
        /// <param name="sampleName">Деталь, по линиям гиба которой считается расчёт; null — расчёт на примере.</param>
        /// <param name="sampleLines">Длины линий гиба этой детали, мм.</param>
        public BendSettingsWindow(BendLineOptions initial, BendLineOptions defaults, bool personal, bool usesDefaults,
            string scope, string sampleName, IReadOnlyList<double> sampleLines)
        {
            _loading = true;
            InitializeComponent();
            _defaults = defaults;
            _personal = personal;
            _sampleLines = sampleLines ?? Array.Empty<double>();

            ScopeTitle.Text = personal ? "Свои настройки для выделенных деталей" : "Общие настройки для всех деталей";
            ScopeText.Text = scope;
            UseDefaultBox.Content = "Использовать общие настройки (" + defaults.FullSummary + ")";
            UseDefaultBox.Visibility = personal ? Visibility.Visible : Visibility.Collapsed;
            UseDefaultBox.IsChecked = personal && usesDefaults;

            Editor.Fallback = defaults;
            Editor.SetOptions(initial);

            bool hasSample = _sampleLines.Count > 0;
            SampleTitle.Text = hasSample
                ? $"Линии гиба детали «{sampleName}»: {_sampleLines.Count}"
                : sampleName != null
                    ? $"У детали «{sampleName}» развертка не сформирована — расчёт на примере."
                    : "Расчёт на примере.";
            SampleLengthPanel.Visibility = hasSample ? Visibility.Collapsed : Visibility.Visible;

            _loading = false;
            Update();
        }

        /// <summary>Результат: параметры (null — использовать общие).</summary>
        public BendLineOptions Result { get; private set; }

        private void OnChanged(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                Update();
        }

        private void OnEditorChanged(object sender, EventArgs e)
        {
            if (!_loading)
                Update();
        }

        private void Update()
        {
            bool useDefaults = _personal && UseDefaultBox.IsChecked == true;
            OptionsPanel.IsEnabled = !useDefaults;

            var edited = Editor.GetOptions(out string error);
            var shown = useDefaults ? _defaults : edited;
            ErrorText.Text = useDefaults ? string.Empty : error;

            IEnumerable<double> lengths = _sampleLines.Count > 0
                ? _sampleLines
                : new[] { BendOptionsEditor.Parse(SampleLengthBox.Text) is double l && l > 0 ? l : 200 };
            LayoutList.ItemsSource = lengths
                .Select((length, i) => new
                {
                    Title = (_sampleLines.Count > 1 ? $"Линия {i + 1} · " : string.Empty) + BendOptionsEditor.Format(length) + " мм",
                    Layout = BendLineStrategies.Layout(length, shown)
                })
                .ToList();
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (_personal && UseDefaultBox.IsChecked == true)
            {
                Result = null;
                DialogResult = true;
                return;
            }

            var options = Editor.GetOptions(out string error);
            if (error.Length > 0)
            {
                ErrorText.Text = error;
                return;
            }
            Result = options;
            DialogResult = true;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using InvAuto.Core.Bends;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>
    /// Параметры линий гиба: общие для всех деталей или персональные для выделенных.
    /// Расчёт показывается по реальным линиям гиба детали (если развертка сформирована) или на примере.
    /// </summary>
    public partial class BendSettingsWindow : Window
    {
        private readonly BendLineOptions _defaults;
        private readonly bool _personal;
        private readonly IReadOnlyList<double> _sampleLines;
        private readonly List<RadioButton> _modeButtons = new List<RadioButton>();
        private bool _loading = true;

        /// <param name="initial">С чего начать редактирование.</param>
        /// <param name="defaults">Общие параметры (для персонального режима — что будет при «использовать общие»).</param>
        /// <param name="personal">true — настройка выделенных деталей, false — общие параметры.</param>
        /// <param name="usesDefaults">Выделенные детали сейчас без персональных параметров.</param>
        /// <param name="scope">Кого касается: «3 детали» / «по умолчанию для всех деталей».</param>
        /// <param name="sampleName">Деталь, по линиям гиба которой считается пример; null — пример по длине.</param>
        /// <param name="sampleLines">Длины линий гиба этой детали, мм.</param>
        public BendSettingsWindow(BendLineOptions initial, BendLineOptions defaults, bool personal, bool usesDefaults,
            string scope, string sampleName, IReadOnlyList<double> sampleLines)
        {
            InitializeComponent();
            _defaults = defaults;
            _personal = personal;
            _sampleLines = sampleLines ?? Array.Empty<double>();

            Title = personal ? "Линии гиба — выделенные детали" : "Линии гиба — по умолчанию";
            ScopeText.Text = scope;
            UseDefaultBox.Content = "Использовать общие настройки (" + defaults.Summary + ")";
            UseDefaultBox.Visibility = personal ? Visibility.Visible : Visibility.Collapsed;
            UseDefaultBox.IsChecked = personal && usesDefaults;

            foreach (var strategy in BendLineStrategies.All)
            {
                var button = new RadioButton { Content = strategy.Title, Tag = strategy.Mode, GroupName = "Mode" };
                button.Checked += OnChanged;
                _modeButtons.Add(button);
                ModesPanel.Children.Add(button);
            }

            FitBox.ItemsSource = new[]
            {
                new KeyValuePair<DashFit, string>(DashFit.Proportional, "прорези и перемычки пропорционально"),
                new KeyValuePair<DashFit, string>(DashFit.KeepCut, "сохранять длину прорези"),
                new KeyValuePair<DashFit, string>(DashFit.KeepBridge, "сохранять длину перемычки")
            };
            FitBox.DisplayMemberPath = "Value";
            FitBox.SelectedValuePath = "Key";

            bool hasSample = _sampleLines.Count > 0;
            SampleTitle.Text = hasSample
                ? $"По линиям гиба детали «{sampleName}»:"
                : sampleName != null
                    ? $"У детали «{sampleName}» развертка не сформирована — расчёт на примере."
                    : "Расчёт на примере.";
            SampleLengthPanel.Visibility = hasSample ? Visibility.Collapsed : Visibility.Visible;

            Display(initial);
            _loading = false;
            Update();
        }

        /// <summary>Результат: параметры (null — использовать общие).</summary>
        public BendLineOptions Result { get; private set; }

        private void Display(BendLineOptions o)
        {
            foreach (var button in _modeButtons)
                button.IsChecked = (BendLineMode)button.Tag == o.Mode;
            EdgeCutBox.Text = Format(o.EdgeCutLength);
            DashBox.Text = Format(o.DashLength);
            BridgeBox.Text = Format(o.BridgeLength);
            EndsCut.IsChecked = o.Ends == DashEnds.Cut;
            EndsBridge.IsChecked = o.Ends == DashEnds.Bridge;
            FitBox.SelectedValue = o.Fit;
        }

        private void OnChanged(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                Update();
        }

        private void Update()
        {
            bool useDefaults = _personal && UseDefaultBox.IsChecked == true;
            OptionsPanel.IsEnabled = !useDefaults;

            var options = useDefaults ? _defaults : Read(out _);
            EdgeCutsPanel.Visibility = options.Mode == BendLineMode.EdgeCuts ? Visibility.Visible : Visibility.Collapsed;
            DashedPanel.Visibility = options.Mode == BendLineMode.Dashed ? Visibility.Visible : Visibility.Collapsed;

            Read(out string error);
            ErrorText.Text = useDefaults ? string.Empty : error;

            IEnumerable<double> lengths = _sampleLines.Count > 0
                ? _sampleLines
                : new[] { Parse(SampleLengthBox.Text) ?? 200 };
            LayoutList.ItemsSource = lengths
                .Select((length, i) => new
                {
                    Title = (_sampleLines.Count > 1 ? $"Линия {i + 1}: " : "Линия: ") + Format(length) + " мм",
                    Layout = BendLineStrategies.Layout(length, options)
                })
                .ToList();
        }

        /// <summary>Параметры из полей. Некорректные числа заменяются текущими значениями по умолчанию.</summary>
        private BendLineOptions Read(out string error)
        {
            error = string.Empty;
            var o = new BendLineOptions
            {
                Mode = _modeButtons.Where(b => b.IsChecked == true).Select(b => (BendLineMode)b.Tag).DefaultIfEmpty(BendLineMode.Keep).First(),
                Ends = EndsBridge.IsChecked == true ? DashEnds.Bridge : DashEnds.Cut,
                Fit = FitBox.SelectedValue is DashFit fit ? fit : DashFit.Proportional
            };

            o.EdgeCutLength = Positive(EdgeCutBox.Text, _defaults.EdgeCutLength, "длина прорези у края", ref error);
            o.DashLength = Positive(DashBox.Text, _defaults.DashLength, "длина прорези", ref error);
            o.BridgeLength = Positive(BridgeBox.Text, _defaults.BridgeLength, "длина перемычки", ref error);
            return o;
        }

        private static double Positive(string text, double fallback, string name, ref string error)
        {
            double? value = Parse(text);
            if (value > 0)
                return value.Value;
            if (error.Length == 0)
                error = $"Проверьте поле «{name}»: нужно число больше нуля.";
            return fallback;
        }

        private static double? Parse(string text)
        {
            text = (text ?? string.Empty).Trim().Replace(',', '.');
            return double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) ? v : (double?)null;
        }

        private static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (_personal && UseDefaultBox.IsChecked == true)
            {
                Result = null;
                DialogResult = true;
                return;
            }

            var options = Read(out string error);
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

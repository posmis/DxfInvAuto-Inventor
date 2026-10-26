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
    /// Слева — параметры (показываются только поля выбранного способа), справа — расчёт
    /// по реальным линиям гиба детали (если развертка сформирована) или на примере.
    /// </summary>
    public partial class BendSettingsWindow : Window
    {
        private static readonly Dictionary<BendLineMode, string> Hints = new Dictionary<BendLineMode, string>
        {
            { BendLineMode.Keep, "Линии гиба остаются в DXF без изменений." },
            { BendLineMode.Remove, "Линии гиба удаляются — лазер по ним не режет." },
            { BendLineMode.EdgeCuts, "Линия удаляется, у каждого края остаётся короткая прорезь." },
            { BendLineMode.Dashed, "Прорези одинаковой длины с одинаковыми перемычками, симметрично по длине линии." },
            { BendLineMode.RectSlots, "Вместо прорези-отрезка вырезается прямоугольник; линия гиба проходит по его середине." }
        };

        private readonly BendLineOptions _defaults;
        private readonly bool _personal;
        private readonly IReadOnlyList<double> _sampleLines;
        private bool _loading = true;

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
            InitializeComponent();
            _defaults = defaults;
            _personal = personal;
            _sampleLines = sampleLines ?? Array.Empty<double>();

            Title = "Линии гиба";
            ScopeTitle.Text = personal ? "Свои настройки для выделенных деталей" : "Общие настройки для всех деталей";
            ScopeText.Text = scope;
            UseDefaultBox.Content = "Использовать общие настройки (" + defaults.Summary + ")";
            UseDefaultBox.Visibility = personal ? Visibility.Visible : Visibility.Collapsed;
            UseDefaultBox.IsChecked = personal && usesDefaults;

            ModeBox.ItemsSource = BendLineStrategies.All;
            Fill(SlotPatternBox, (SlotPattern.Dashed, "прерывисто, с перемычками"), (SlotPattern.Full, "один вырез по всей длине"));
            Fill(SpecBox, (DashSpec.Step, "длину и перемычку"), (DashSpec.Count, "длину и количество"));
            Fill(EndsBox, (DashEnds.Cut, "прорезь"), (DashEnds.Bridge, "перемычка"));
            Fill(FitBox, (DashFit.Proportional, "пропорционально"), (DashFit.KeepCut, "сохранять длину прорези"),
                (DashFit.KeepBridge, "сохранять длину перемычки"));

            bool hasSample = _sampleLines.Count > 0;
            SampleTitle.Text = hasSample
                ? $"Линии гиба детали «{sampleName}»: {_sampleLines.Count}"
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

        private static void Fill<T>(ComboBox box, params (T Value, string Text)[] items)
        {
            box.ItemsSource = items.Select(i => new KeyValuePair<T, string>(i.Value, i.Text)).ToList();
            box.DisplayMemberPath = "Value";
            box.SelectedValuePath = "Key";
        }

        private void Display(BendLineOptions o)
        {
            ModeBox.SelectedItem = BendLineStrategies.For(o.Mode);
            SlotWidthBox.Text = Format(o.SlotWidth);
            SlotPatternBox.SelectedValue = o.SlotPattern;
            EdgeCutBox.Text = Format(o.EdgeCutLength);
            SpecBox.SelectedValue = o.Spec;
            DashBox.Text = Format(o.DashLength);
            BridgeBox.Text = Format(o.BridgeLength);
            CountBox.Text = o.DashCount.ToString(CultureInfo.CurrentCulture);
            EndsBox.SelectedValue = o.Ends;
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

            var edited = Read(out string error);
            var shown = useDefaults ? _defaults : edited;
            ShowFieldsFor(edited);
            ErrorText.Text = useDefaults ? string.Empty : error;

            IEnumerable<double> lengths = _sampleLines.Count > 0
                ? _sampleLines
                : new[] { Parse(SampleLengthBox.Text) is double l && l > 0 ? l : 200 };
            LayoutList.ItemsSource = lengths
                .Select((length, i) => new
                {
                    Title = (_sampleLines.Count > 1 ? $"Линия {i + 1} · " : string.Empty) + Format(length) + " мм",
                    Layout = BendLineStrategies.Layout(length, shown)
                })
                .ToList();
        }

        /// <summary>Показывает только поля выбранного способа.</summary>
        private void ShowFieldsFor(BendLineOptions o)
        {
            bool slots = o.Mode == BendLineMode.RectSlots;
            bool dashed = o.Mode == BendLineMode.Dashed || (slots && o.SlotPattern == SlotPattern.Dashed);
            bool byCount = dashed && o.Spec == DashSpec.Count;

            ModeHint.Text = Hints.TryGetValue(o.Mode, out var hint) ? hint : string.Empty;
            SetVisible(SlotWidthRow, slots);
            SetVisible(SlotPatternRow, slots);
            SetVisible(EdgeCutRow, o.Mode == BendLineMode.EdgeCuts);
            SetVisible(SpecRow, dashed);
            SetVisible(DashRow, dashed);
            SetVisible(BridgeRow, dashed && !byCount);
            SetVisible(CountRow, byCount);
            SetVisible(EndsRow, dashed);
            SetVisible(FitRow, dashed && !byCount);

            DashLabel.Text = slots ? "Длина выреза" : "Длина прорези";
            CountLabel.Text = slots ? "Количество вырезов" : "Количество прорезей";
        }

        private static void SetVisible(UIElement element, bool visible) =>
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        /// <summary>Параметры из полей. Некорректные числа заменяются общими значениями, ошибка — в error.</summary>
        private BendLineOptions Read(out string error)
        {
            error = string.Empty;
            var o = new BendLineOptions
            {
                Mode = (ModeBox.SelectedItem as IBendLineStrategy)?.Mode ?? BendLineMode.Keep,
                SlotPattern = SlotPatternBox.SelectedValue is SlotPattern pattern ? pattern : SlotPattern.Dashed,
                Spec = SpecBox.SelectedValue is DashSpec spec ? spec : DashSpec.Step,
                Ends = EndsBox.SelectedValue is DashEnds ends ? ends : DashEnds.Cut,
                Fit = FitBox.SelectedValue is DashFit fit ? fit : DashFit.Proportional
            };

            // Проверяем только поля, которые видны для выбранного способа.
            bool slots = o.Mode == BendLineMode.RectSlots;
            bool dashed = o.Mode == BendLineMode.Dashed || (slots && o.SlotPattern == SlotPattern.Dashed);
            bool byCount = dashed && o.Spec == DashSpec.Count;

            o.SlotWidth = Positive(SlotWidthBox.Text, _defaults.SlotWidth, "Ширина выреза", slots, ref error);
            o.EdgeCutLength = Positive(EdgeCutBox.Text, _defaults.EdgeCutLength, "Длина прорези у края", o.Mode == BendLineMode.EdgeCuts, ref error);
            o.DashLength = Positive(DashBox.Text, _defaults.DashLength, slots ? "Длина выреза" : "Длина прорези", dashed, ref error);
            o.BridgeLength = Positive(BridgeBox.Text, _defaults.BridgeLength, "Длина перемычки", dashed && !byCount, ref error);

            double? count = Parse(CountBox.Text);
            if (count >= 1 && count <= 1000 && Math.Abs(count.Value - Math.Round(count.Value)) < 1e-9)
            {
                o.DashCount = (int)Math.Round(count.Value);
            }
            else
            {
                o.DashCount = _defaults.DashCount;
                if (byCount && error.Length == 0)
                    error = "«Количество»: целое число от 1.";
            }
            return o;
        }

        private static double Positive(string text, double fallback, string name, bool check, ref string error)
        {
            double? value = Parse(text);
            if (value > 0)
                return value.Value;
            if (check && error.Length == 0)
                error = $"«{name}»: нужно число больше нуля.";
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

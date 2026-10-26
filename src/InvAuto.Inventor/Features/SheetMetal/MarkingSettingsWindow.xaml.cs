using System;
using System.Windows;
using InvAuto.Core.Formatting;
using InvAuto.Core.Marking;
using InvAuto.Core.Preview;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>
    /// Маркировка обозначением: общие параметры или свои для выделенных деталей.
    /// Справа — развертка детали-примера с текстом там, где он встанет при выгрузке.
    /// </summary>
    public partial class MarkingSettingsWindow : Window
    {
        private readonly MarkingOptions _defaults;
        private readonly bool _personal;
        private readonly Func<MarkingOptions, (FlatDrawing Drawing, MarkingResult Marking)> _render;
        private readonly string _sampleName;
        private readonly bool _loading;

        /// <param name="initial">С чего начать редактирование.</param>
        /// <param name="defaults">Общие параметры.</param>
        /// <param name="personal">true — настройка выделенных деталей, false — общие параметры.</param>
        /// <param name="usesDefaults">Выделенные детали сейчас без своих параметров.</param>
        /// <param name="scope">Кого касается.</param>
        /// <param name="sampleName">Деталь для примера (null — нет сформированной развертки).</param>
        /// <param name="render">Развертка примера с маркировкой по заданным параметрам.</param>
        public MarkingSettingsWindow(MarkingOptions initial, MarkingOptions defaults, bool personal, bool usesDefaults, string scope,
            string sampleName, Func<MarkingOptions, (FlatDrawing Drawing, MarkingResult Marking)> render)
        {
            _loading = true;
            InitializeComponent();
            _defaults = defaults;
            _personal = personal;
            _render = render;
            _sampleName = sampleName;

            ScopeTitle.Text = personal ? "Своя маркировка для выделенных деталей" : "Маркировка для всех деталей";
            ScopeText.Text = scope;
            UseDefaultBox.Content = "Использовать общие настройки (" + defaults.Summary + ")";
            UseDefaultBox.Visibility = personal ? Visibility.Visible : Visibility.Collapsed;
            UseDefaultBox.IsChecked = personal && usesDefaults;

            var o = initial ?? new MarkingOptions();
            EnabledBox.IsChecked = o.Enabled;
            HeightBox.Text = NumberText.Format(o.Height);
            MinHeightBox.Text = NumberText.Format(o.MinHeight);
            MarginBox.Text = NumberText.Format(o.Margin);
            LayerBox.Text = o.Layer;

            _loading = false;
            Update();
        }

        /// <summary>Результат: параметры (null — использовать общие).</summary>
        public MarkingOptions Result { get; private set; }

        private void OnChanged(object sender, RoutedEventArgs e)
        {
            if (!_loading)
                Update();
        }

        private MarkingOptions Edited(out string error)
        {
            error = string.Empty;
            var o = new MarkingOptions { Enabled = EnabledBox.IsChecked == true };
            o.Height = Positive(HeightBox.Text, _defaults.Height, "Высота текста", o.Enabled, ref error);
            o.MinHeight = Positive(MinHeightBox.Text, Math.Min(_defaults.MinHeight, o.Height), "Если не помещается — до", o.Enabled, ref error);
            if (o.MinHeight > o.Height)
            {
                if (o.Enabled && error.Length == 0)
                    error = "Наименьшая высота больше высоты текста.";
                o.MinHeight = o.Height;
            }
            double? margin = NumberText.Parse(MarginBox.Text);
            if (margin >= 0)
                o.Margin = margin.Value;
            else
            {
                o.Margin = _defaults.Margin;
                if (o.Enabled && error.Length == 0)
                    error = "«Отступ от контуров»: число не меньше нуля.";
            }
            string layer = LayerBox.Text.Trim();
            o.Layer = layer.Length > 0 ? layer : MarkingOptions.DefaultLayer;
            return o;
        }

        private static double Positive(string text, double fallback, string name, bool check, ref string error)
        {
            double? value = NumberText.Parse(text);
            if (value > 0)
                return value.Value;
            if (check && error.Length == 0)
                error = $"«{name}»: нужно число больше нуля.";
            return fallback;
        }

        private void Update()
        {
            bool useDefaults = _personal && UseDefaultBox.IsChecked == true;
            OptionsPanel.IsEnabled = !useDefaults;
            var edited = Edited(out string error);
            FieldsPanel.IsEnabled = edited.Enabled;
            var shown = useDefaults ? _defaults : edited;
            ErrorText.Text = useDefaults ? string.Empty : error;

            if (_render == null)
            {
                SampleTitle.Text = _sampleName == null
                    ? "Нет детали со сформированной разверткой — пример не показан."
                    : $"У детали «{_sampleName}» развертка не сформирована — пример не показан.";
                Preview.Drawing = null;
                return;
            }

            var (drawing, marking) = _render(shown);
            Preview.Drawing = drawing;
            string state = marking == null || !shown.Enabled ? "маркировка выключена" : "маркировка: " + marking.Description;
            SampleTitle.Text = $"Деталь «{_sampleName}» — {state}";
        }

        private void OnOk(object sender, RoutedEventArgs e)
        {
            if (_personal && UseDefaultBox.IsChecked == true)
            {
                Result = null;
                DialogResult = true;
                return;
            }
            var options = Edited(out string error);
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

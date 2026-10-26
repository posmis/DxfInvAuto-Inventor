using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using InvAuto.Core.Bends;
using InvAuto.Core.Formatting;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>
    /// Форма параметров линий гиба: способ (список из BendLineStrategies) и только его поля.
    /// Используется в общих настройках, в настройках детали и при настройке отдельных линий.
    /// </summary>
    public partial class BendOptionsEditor : UserControl
    {
        private static readonly Dictionary<BendLineMode, string> Hints = new Dictionary<BendLineMode, string>
        {
            { BendLineMode.Keep, "Линии гиба остаются в DXF без изменений." },
            { BendLineMode.Remove, "Линии гиба удаляются — лазер по ним не режет." },
            { BendLineMode.EdgeCuts, "Линия удаляется, у каждого края остаётся короткая прорезь." },
            { BendLineMode.Dashed, "Прорези одинаковой длины с одинаковыми перемычками, симметрично по длине линии." },
            { BendLineMode.RectSlots, "Вместо прорези-отрезка вырезается прямоугольник; линия гиба проходит по его середине." },
            { BendLineMode.RoundSlots, "Паз со скруглёнными торцами: дуга — отрезок — дуга — отрезок. Радиус скругления — половина ширины, длина паза — вместе со скруглениями." }
        };

        private bool _loading;

        public BendOptionsEditor()
        {
            InitializeComponent();
            ModeBox.ItemsSource = BendLineStrategies.All;
            EndShapeBox.ItemsSource = BendEnds.All;
            Fill(SlotPatternBox, (SlotPattern.Dashed, "прерывисто, с перемычками"), (SlotPattern.Full, "один вырез по всей длине"));
            Fill(SpecBox, (DashSpec.Step, "длину и перемычку"), (DashSpec.Count, "длину и количество"));
            Fill(EndsBox, (DashEnds.Cut, "прорезь"), (DashEnds.Bridge, "перемычка"));
            Fill(FitBox, (DashFit.Proportional, "пропорционально"), (DashFit.KeepCut, "сохранять длину прорези"),
                (DashFit.KeepBridge, "сохранять длину перемычки"));
            SetOptions(new BendLineOptions());
        }

        /// <summary>Любое изменение в форме.</summary>
        public event EventHandler Changed;

        /// <summary>Чем заменять некорректно введённые числа (обычно — общие параметры).</summary>
        public BendLineOptions Fallback { get; set; } = new BendLineOptions();

        public void SetOptions(BendLineOptions o)
        {
            _loading = true;
            try
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
                EndShapeBox.SelectedItem = BendEnds.For(o.EndShape);
                EndSizeBox.Text = Format(o.EndSize);
                EndOffsetBox.Text = Format(o.EndOffset);
                EndLayerBox.Text = o.EndLayer;
            }
            finally
            {
                _loading = false;
            }
            ShowFieldsFor(GetOptions(out _));
        }

        /// <summary>Параметры из полей. Некорректные числа заменяются значениями Fallback, описание ошибки — в error.</summary>
        public BendLineOptions GetOptions(out string error)
        {
            error = string.Empty;
            var o = new BendLineOptions
            {
                Mode = (ModeBox.SelectedItem as IBendLineStrategy)?.Mode ?? BendLineMode.Keep,
                SlotPattern = SlotPatternBox.SelectedValue is SlotPattern pattern ? pattern : SlotPattern.Dashed,
                Spec = SpecBox.SelectedValue is DashSpec spec ? spec : DashSpec.Step,
                Ends = EndsBox.SelectedValue is DashEnds ends ? ends : DashEnds.Cut,
                Fit = FitBox.SelectedValue is DashFit fit ? fit : DashFit.Proportional,
                EndShape = (EndShapeBox.SelectedItem as IBendEndShape)?.Shape ?? BendEndShape.None
            };

            bool hasEnds = o.EndShape != BendEndShape.None;
            o.EndSize = Positive(EndSizeBox.Text, Fallback.EndSize, BendEnds.For(o.EndShape).SizeTitle + " законцовки", hasEnds, ref error);
            double? offset = Parse(EndOffsetBox.Text);
            if (offset >= 0)
                o.EndOffset = offset.Value;
            else
            {
                o.EndOffset = Fallback.EndOffset;
                if (hasEnds && error.Length == 0)
                    error = "«Центр внутрь от края»: число не меньше нуля.";
            }
            string layer = EndLayerBox.Text.Trim();
            o.EndLayer = layer.Length > 0 ? layer : BendLineOptions.DefaultEndLayer;

            // Проверяем только поля, которые видны для выбранного способа.
            bool slots = o.IsSlots;
            bool dashed = o.Mode == BendLineMode.Dashed || (slots && o.SlotPattern == SlotPattern.Dashed);
            bool byCount = dashed && o.Spec == DashSpec.Count;

            o.SlotWidth = Positive(SlotWidthBox.Text, Fallback.SlotWidth, "Ширина выреза", slots, ref error);
            o.EdgeCutLength = Positive(EdgeCutBox.Text, Fallback.EdgeCutLength, "Длина прорези у края", o.Mode == BendLineMode.EdgeCuts, ref error);
            o.DashLength = Positive(DashBox.Text, Fallback.DashLength, slots ? "Длина выреза" : "Длина прорези", dashed, ref error);
            o.BridgeLength = Positive(BridgeBox.Text, Fallback.BridgeLength, "Длина перемычки", dashed && !byCount, ref error);

            double? count = Parse(CountBox.Text);
            if (count >= 1 && count <= 1000 && Math.Abs(count.Value - Math.Round(count.Value)) < 1e-9)
            {
                o.DashCount = (int)Math.Round(count.Value);
            }
            else
            {
                o.DashCount = Fallback.DashCount;
                if (byCount && error.Length == 0)
                    error = "«Количество»: целое число от 1.";
            }
            return o;
        }

        private void OnChanged(object sender, RoutedEventArgs e)
        {
            if (_loading)
                return;
            ShowFieldsFor(GetOptions(out _));
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Показывает только поля выбранного способа.</summary>
        private void ShowFieldsFor(BendLineOptions o)
        {
            bool slots = o.IsSlots;
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

            bool hasEnds = o.EndShape != BendEndShape.None;
            SetVisible(EndSizeRow, hasEnds);
            SetVisible(EndOffsetRow, hasEnds);
            SetVisible(EndLayerRow, hasEnds);
            EndSizeLabel.Text = BendEnds.For(o.EndShape).SizeTitle;

            DashLabel.Text = slots ? "Длина выреза" : "Длина прорези";
            CountLabel.Text = slots ? "Количество вырезов" : "Количество прорезей";
        }

        private static void SetVisible(UIElement element, bool visible) =>
            element.Visibility = visible ? Visibility.Visible : Visibility.Collapsed;

        private static void Fill<T>(ComboBox box, params (T Value, string Text)[] items)
        {
            box.ItemsSource = items.Select(i => new KeyValuePair<T, string>(i.Value, i.Text)).ToList();
            box.DisplayMemberPath = "Value";
            box.SelectedValuePath = "Key";
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

        internal static double? Parse(string text) => NumberText.Parse(text);

        internal static string Format(double value) => NumberText.Format(value);
    }
}

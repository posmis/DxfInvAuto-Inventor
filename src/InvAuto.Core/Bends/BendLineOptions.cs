using System.Globalization;

namespace InvAuto.Core.Bends
{
    /// <summary>Что сделать с линией гиба в DXF. Новая модификация — новое значение и своя стратегия.</summary>
    public enum BendLineMode
    {
        /// <summary>Оставить как есть.</summary>
        Keep,

        /// <summary>Удалить полностью, без прорезей.</summary>
        Remove,

        /// <summary>Удалить линию, оставить прорези заданной длины у краёв.</summary>
        EdgeCuts,

        /// <summary>Прерывистая линия: прорези и перемычки, симметрично по длине.</summary>
        Dashed,

        /// <summary>Прямоугольные вырезы вдоль линии гиба заданной ширины; линия гиба — по середине выреза.</summary>
        RectSlots,

        /// <summary>
        /// Вырезы со скруглёнными торцами (паз: дуга — отрезок — дуга — отрезок). Радиус дуг — половина ширины,
        /// длина паза вместе со скруглениями — как у прямоугольного выреза. Параметры — те же, что у RectSlots.
        /// </summary>
        RoundSlots
    }

    /// <summary>Как расположены прямоугольные вырезы вдоль линии гиба.</summary>
    public enum SlotPattern
    {
        /// <summary>Один вырез во всю длину линии.</summary>
        Full,

        /// <summary>Вырезы с перемычками — та же симметричная раскладка, что у прерывистой линии.</summary>
        Dashed
    }

    /// <summary>Чем начинается и заканчивается прерывистая линия (одинаково с обеих сторон).</summary>
    public enum DashEnds
    {
        /// <summary>У краёв прорезь.</summary>
        Cut,

        /// <summary>У краёв перемычка.</summary>
        Bridge
    }

    /// <summary>Как задаётся прерывистая раскладка.</summary>
    public enum DashSpec
    {
        /// <summary>Желаемые прорезь и перемычка; количество подбирается, размеры подгоняются (DashFit).</summary>
        Step,

        /// <summary>Длина прорези и количество; перемычки рассчитываются.</summary>
        Count
    }

    /// <summary>Что подгонять, чтобы шаблон ровно уложился в длину линии.</summary>
    public enum DashFit
    {
        /// <summary>Прорези и перемычки в одной пропорции — ближе всего к заданным значениям.</summary>
        Proportional,

        /// <summary>Длина прорези точно как задана, подгоняются перемычки.</summary>
        KeepCut,

        /// <summary>Длина перемычки точно как задана, подгоняются прорези.</summary>
        KeepBridge
    }

    /// <summary>
    /// Параметры обработки линий гиба (в миллиметрах). Один класс для всех режимов —
    /// каждый режим читает свои поля; так настройки просто сохраняются и копируются.
    /// </summary>
    public sealed class BendLineOptions
    {
        public BendLineMode Mode { get; set; } = BendLineMode.Keep;

        /// <summary>EdgeCuts: длина прорези у каждого края.</summary>
        public double EdgeCutLength { get; set; } = 5;

        /// <summary>Dashed: желаемая длина прорези.</summary>
        public double DashLength { get; set; } = 20;

        /// <summary>Dashed: желаемая длина перемычки (при задании по шагу).</summary>
        public double BridgeLength { get; set; } = 10;

        public DashSpec Spec { get; set; } = DashSpec.Step;

        /// <summary>Dashed: количество прорезей на линии (при задании по количеству).</summary>
        public int DashCount { get; set; } = 5;

        public DashEnds Ends { get; set; } = DashEnds.Cut;
        public DashFit Fit { get; set; } = DashFit.Proportional;

        /// <summary>RectSlots и RoundSlots: общая ширина выреза, мм (линия гиба проходит по середине).</summary>
        public double SlotWidth { get; set; } = 2;

        /// <summary>RectSlots: по всей длине или прерывисто (длины выреза и перемычки — DashLength и BridgeLength).</summary>
        public SlotPattern SlotPattern { get; set; } = SlotPattern.Dashed;

        /// <summary>Законцовки — вырезы на обоих концах линии гиба (работают с любым способом обработки линии).</summary>
        public BendEndShape EndShape { get; set; } = BendEndShape.None;

        /// <summary>Размер законцовки, мм: диаметр окружности или сторона квадрата.</summary>
        public double EndSize { get; set; } = 3;

        /// <summary>Смещение центра законцовки внутрь детали вдоль линии гиба, мм (0 — на конце линии, на краю детали).</summary>
        public double EndOffset { get; set; }

        /// <summary>Слой законцовок в DXF. Законцовки — сквозные вырезы, поэтому по умолчанию слой отверстий.</summary>
        public string EndLayer { get; set; } = DefaultEndLayer;

        public const string DefaultEndLayer = "IV_INTERIOR_PROFILES";

        /// <summary>Режим с вырезами заданной ширины (прямоугольными или со скруглениями).</summary>
        public bool IsSlots => Mode == BendLineMode.RectSlots || Mode == BendLineMode.RoundSlots;

        public BendLineOptions Clone() => (BendLineOptions)MemberwiseClone();

        /// <summary>Короткое описание для таблицы: «прерывистая 20/10».</summary>
        public string Summary
        {
            get
            {
                var c = CultureInfo.CurrentCulture;
                switch (Mode)
                {
                    case BendLineMode.Remove: return "удалить";
                    case BendLineMode.EdgeCuts: return string.Format(c, "прорези по краям {0:0.##}", EdgeCutLength);
                    case BendLineMode.Dashed: return "прерывистая " + DashSummary(c);
                    case BendLineMode.RectSlots:
                        return SlotPattern == SlotPattern.Full
                            ? string.Format(c, "вырез {0:0.##} мм по всей длине", SlotWidth)
                            : string.Format(c, "вырезы {0:0.##} мм, ", SlotWidth) + DashSummary(c);
                    case BendLineMode.RoundSlots:
                        return SlotPattern == SlotPattern.Full
                            ? string.Format(c, "паз R{0:0.##} по всей длине", SlotWidth / 2)
                            : string.Format(c, "пазы {0:0.##} мм (R{1:0.##}), ", SlotWidth, SlotWidth / 2) + DashSummary(c);
                    default: return "как есть";
                }
            }
        }

        /// <summary>Описание вместе с законцовками: «прерывистая 20/10 + окружность Ø3».</summary>
        public string FullSummary
        {
            get
            {
                string ends = BendEnds.Describe(this);
                return ends.Length > 0 ? Summary + " + " + ends : Summary;
            }
        }

        private string DashSummary(CultureInfo c) => Spec == DashSpec.Count
            ? string.Format(c, "{0} × {1:0.##}", DashCount, DashLength)
            : string.Format(c, "{0:0.##}/{1:0.##}", DashLength, BridgeLength);

        public bool SameAs(BendLineOptions other) =>
            other != null &&
            Mode == other.Mode &&
            EdgeCutLength.Equals(other.EdgeCutLength) &&
            DashLength.Equals(other.DashLength) &&
            BridgeLength.Equals(other.BridgeLength) &&
            Ends == other.Ends &&
            Fit == other.Fit &&
            Spec == other.Spec &&
            DashCount == other.DashCount &&
            SlotWidth.Equals(other.SlotWidth) &&
            SlotPattern == other.SlotPattern &&
            EndShape == other.EndShape &&
            EndSize.Equals(other.EndSize) &&
            EndOffset.Equals(other.EndOffset) &&
            string.Equals(EndLayer, other.EndLayer, System.StringComparison.OrdinalIgnoreCase);
    }
}

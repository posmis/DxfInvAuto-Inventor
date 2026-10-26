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

        /// <summary>Прямоугольные вырезы вдоль линии гиба: ширина — смещение в каждую сторону от линии.</summary>
        RectSlots
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

        /// <summary>Dashed: желаемая длина перемычки.</summary>
        public double BridgeLength { get; set; } = 10;

        public DashEnds Ends { get; set; } = DashEnds.Cut;
        public DashFit Fit { get; set; } = DashFit.Proportional;

        /// <summary>RectSlots: смещение стороны выреза от линии гиба (ширина выреза — вдвое больше).</summary>
        public double SlotOffset { get; set; } = 1;

        /// <summary>RectSlots: по всей длине или прерывисто (длины выреза и перемычки — DashLength и BridgeLength).</summary>
        public SlotPattern SlotPattern { get; set; } = SlotPattern.Dashed;

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
                    case BendLineMode.Dashed: return string.Format(c, "прерывистая {0:0.##}/{1:0.##}", DashLength, BridgeLength);
                    case BendLineMode.RectSlots:
                        return SlotPattern == SlotPattern.Full
                            ? string.Format(c, "вырез ±{0:0.##} по всей длине", SlotOffset)
                            : string.Format(c, "вырезы ±{0:0.##}, {1:0.##}/{2:0.##}", SlotOffset, DashLength, BridgeLength);
                    default: return "как есть";
                }
            }
        }

        public bool SameAs(BendLineOptions other) =>
            other != null &&
            Mode == other.Mode &&
            EdgeCutLength.Equals(other.EdgeCutLength) &&
            DashLength.Equals(other.DashLength) &&
            BridgeLength.Equals(other.BridgeLength) &&
            Ends == other.Ends &&
            Fit == other.Fit &&
            SlotOffset.Equals(other.SlotOffset) &&
            SlotPattern == other.SlotPattern;
    }
}

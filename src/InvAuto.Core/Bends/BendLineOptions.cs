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
            Fit == other.Fit;
    }
}

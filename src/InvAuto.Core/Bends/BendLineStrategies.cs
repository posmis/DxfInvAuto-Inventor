using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace InvAuto.Core.Bends
{
    /// <summary>Участок линии гиба, который остаётся в DXF (будет прорезан), от начала линии, мм.</summary>
    public readonly struct BendSegment
    {
        public BendSegment(double start, double end)
        {
            Start = start;
            End = end;
        }

        public double Start { get; }
        public double End { get; }
        public double Length => End - Start;
    }

    /// <summary>Результат расчёта для одной линии гиба.</summary>
    public sealed class BendLineLayout
    {
        public BendLineLayout(double lineLength, IReadOnlyList<BendSegment> segments, string description, double slotHalfWidth = 0)
        {
            LineLength = lineLength;
            Segments = segments;
            Description = description;
            SlotHalfWidth = slotHalfWidth;
        }

        /// <summary>
        /// 0 — участки остаются отрезками на линии гиба; больше нуля — каждый участок становится
        /// прямоугольным вырезом с этим смещением сторон от линии.
        /// </summary>
        public double SlotHalfWidth { get; }

        public double LineLength { get; }
        public IReadOnlyList<BendSegment> Segments { get; }

        /// <summary>Понятное описание результата: «7 прорезей по 20,4 мм, перемычки 10,2 мм».</summary>
        public string Description { get; }

        public static BendLineLayout Whole(double length, string description) =>
            new BendLineLayout(length, new[] { new BendSegment(0, length) }, description);
    }

    /// <summary>
    /// Способ обработки линии гиба. Чтобы добавить новый: значение в BendLineMode,
    /// параметры в BendLineOptions, класс-стратегия здесь и регистрация в BendLineStrategies.All.
    /// </summary>
    public interface IBendLineStrategy
    {
        BendLineMode Mode { get; }

        /// <summary>Название для интерфейса.</summary>
        string Title { get; }

        BendLineLayout Layout(double lineLength, BendLineOptions options);
    }

    public static class BendLineStrategies
    {
        public static IReadOnlyList<IBendLineStrategy> All { get; } = new IBendLineStrategy[]
        {
            new KeepStrategy(),
            new RemoveStrategy(),
            new EdgeCutsStrategy(),
            new DashedStrategy(),
            new RectSlotsStrategy()
        };

        public static IBendLineStrategy For(BendLineMode mode) =>
            All.FirstOrDefault(s => s.Mode == mode) ?? All[0];

        public static BendLineLayout Layout(double lineLength, BendLineOptions options) =>
            For(options.Mode).Layout(lineLength, options);

        internal static string Mm(double value) => value.ToString("0.#", CultureInfo.CurrentCulture);
    }

    internal sealed class KeepStrategy : IBendLineStrategy
    {
        public BendLineMode Mode => BendLineMode.Keep;
        public string Title => "Оставить как есть";

        public BendLineLayout Layout(double length, BendLineOptions options) =>
            BendLineLayout.Whole(length, "без изменений");
    }

    internal sealed class RemoveStrategy : IBendLineStrategy
    {
        public BendLineMode Mode => BendLineMode.Remove;
        public string Title => "Удалить полностью";

        public BendLineLayout Layout(double length, BendLineOptions options) =>
            new BendLineLayout(length, Array.Empty<BendSegment>(), "удалена");
    }

    internal sealed class EdgeCutsStrategy : IBendLineStrategy
    {
        public BendLineMode Mode => BendLineMode.EdgeCuts;
        public string Title => "Прорези по краям";

        public BendLineLayout Layout(double length, BendLineOptions options)
        {
            double cut = options.EdgeCutLength;
            if (cut <= 0)
                return new BendLineLayout(length, Array.Empty<BendSegment>(), "удалена (длина прорези 0)");
            if (2 * cut >= length)
                return BendLineLayout.Whole(length, "короче двух прорезей — оставлена целиком");

            return new BendLineLayout(length,
                new[] { new BendSegment(0, cut), new BendSegment(length - cut, length) },
                $"2 прорези по {BendLineStrategies.Mm(cut)} мм у краёв");
        }
    }

    /// <summary>
    /// Прерывистая линия. Количество прорезей подбирается так, чтобы заданный шаг уложился ровно,
    /// а затем прорези/перемычки чуть подгоняются (DashFit): все прорези одинаковые, все перемычки
    /// одинаковые, с обоих концов одно и то же — рисунок симметричен относительно середины линии.
    /// </summary>
    internal sealed class DashedStrategy : IBendLineStrategy
    {
        /// <summary>Подогнанные прорезь и перемычка — не меньше этой доли от заданных.</summary>
        private const double MinShare = 0.5;

        public BendLineMode Mode => BendLineMode.Dashed;
        public string Title => "Прерывистая линия";

        public BendLineLayout Layout(double length, BendLineOptions o)
        {
            double cut = o.DashLength, bridge = o.BridgeLength;
            if (cut <= 0 || bridge <= 0 || length <= 0)
                return BendLineLayout.Whole(length, "не заданы прорезь или перемычка — оставлена целиком");

            bool cutEnds = o.Ends == DashEnds.Cut;
            // При прорезях по краям перемычек на одну меньше, чем прорезей; при перемычках — на одну больше.
            int GapsFor(int n) => cutEnds ? n - 1 : n + 1;

            int count = (int)Math.Round(cutEnds ? (length + bridge) / (cut + bridge) : (length - bridge) / (cut + bridge),
                MidpointRounding.AwayFromZero);
            int minCount = cutEnds ? 2 : 1;
            count = Math.Max(minCount, count);

            for (; count >= minCount; count--)
            {
                if (Fit(length, count, GapsFor(count), cut, bridge, o.Fit, out double actualCut, out double actualBridge))
                    return Build(length, count, actualCut, actualBridge, cutEnds);
            }

            return BendLineLayout.Whole(length, "короче шаблона — оставлена целиком");
        }

        private static bool Fit(double length, int count, int gaps, double cut, double bridge, DashFit fit,
            out double actualCut, out double actualBridge)
        {
            switch (fit)
            {
                case DashFit.KeepCut:
                    actualCut = cut;
                    actualBridge = (length - count * cut) / gaps;
                    break;
                case DashFit.KeepBridge:
                    actualBridge = bridge;
                    actualCut = (length - gaps * bridge) / count;
                    break;
                default:
                    double scale = length / (count * cut + gaps * bridge);
                    actualCut = cut * scale;
                    actualBridge = bridge * scale;
                    break;
            }
            // Подгонка не должна уводить размеры далеко от заданных: на коротких линиях
            // это дало бы крошечные прорези и перемычки. Лучше меньше прорезей или целая линия.
            return actualCut >= cut * MinShare && actualBridge >= bridge * MinShare;
        }

        private static BendLineLayout Build(double length, int count, double cut, double bridge, bool cutEnds)
        {
            var segments = new List<BendSegment>(count);
            double position = cutEnds ? 0 : bridge;
            for (int i = 0; i < count; i++)
            {
                double end = i == count - 1 && cutEnds ? length : position + cut; // последний конец — ровно в край
                segments.Add(new BendSegment(position, end));
                position = end + bridge;
            }

            string ends = cutEnds ? "у краёв прорези" : "у краёв перемычки";
            return new BendLineLayout(length, segments,
                $"{count} × {BendLineStrategies.Mm(cut)} мм, перемычки {BendLineStrategies.Mm(bridge)} мм, {ends}");
        }
    }

    /// <summary>
    /// Прямоугольные вырезы: участки по всей длине или с перемычками (раскладка прерывистой линии),
    /// каждый участок вырезается прямоугольником шириной 2 × смещение, по центру линии гиба.
    /// </summary>
    internal sealed class RectSlotsStrategy : IBendLineStrategy
    {
        public BendLineMode Mode => BendLineMode.RectSlots;
        public string Title => "Прямоугольные вырезы";

        public BendLineLayout Layout(double length, BendLineOptions o)
        {
            var layout = o.SlotPattern == SlotPattern.Full
                ? BendLineLayout.Whole(length, "один вырез во всю длину")
                : new DashedStrategy().Layout(length, o);

            if (o.SlotOffset <= 0)
                return new BendLineLayout(length, layout.Segments, layout.Description + " (смещение 0 — отрезками)");

            string width = BendLineStrategies.Mm(2 * o.SlotOffset);
            return new BendLineLayout(length, layout.Segments, $"{layout.Description}; вырезы шириной {width} мм", o.SlotOffset);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.Flat
{
    /// <summary>Поворот развертки детали, градусы против часовой.</summary>
    public sealed class PartRotation
    {
        /// <summary>Полный путь к файлу детали.</summary>
        public string PartPath { get; set; } = string.Empty;

        public double Angle { get; set; }
    }

    /// <summary>
    /// Ручной поворот разверток (окно просмотра — «Повернуть по линии»): у каждой детали свой. Сохраняется и после
    /// изменения детали. Применяется в превью, выгрузке DXF, ведомости и проверке.
    /// </summary>
    public sealed class FlatRotationSettings
    {
        public const string Key = "SheetMetal.Rotation";

        /// <summary>Пустой по умолчанию: XmlSerializer дописывает элементы.</summary>
        public List<PartRotation> Parts { get; set; } = new List<PartRotation>();

        /// <summary>Поворот детали в (−180°; 180°]; 0 — без поворота.</summary>
        public double For(string partPath) =>
            Parts.LastOrDefault(p => string.Equals(p.PartPath, partPath, StringComparison.OrdinalIgnoreCase))?.Angle ?? 0;

        /// <summary>Задаёт поворот (приводится к (−180°; 180°]); 0 — убирает запись.</summary>
        public void Set(string partPath, double angle)
        {
            Parts.RemoveAll(p => string.Equals(p.PartPath, partPath, StringComparison.OrdinalIgnoreCase));
            angle = Normalize(angle);
            if (Math.Abs(angle) > 1e-9)
                Parts.Add(new PartRotation { PartPath = partPath, Angle = angle });
        }

        /// <summary>Угол в (−180°; 180°].</summary>
        public static double Normalize(double angle)
        {
            angle %= 360;
            if (angle <= -180)
                angle += 360;
            else if (angle > 180)
                angle -= 360;
            return Math.Abs(angle) < 1e-9 ? 0 : angle;
        }

        /// <summary>
        /// Поворот после выбора отрезка с углом lineAngle (на развертке, как она показана сейчас): отрезок становится
        /// горизонтальным; если он уже горизонтален — деталь разворачивается на 180°.
        /// </summary>
        public static double AlignToLine(double current, double lineAngle)
        {
            double a = ((lineAngle % 180) + 180) % 180; // направление отрезка без учёта стороны: [0; 180)
            bool horizontal = a < 1e-3 || a > 180 - 1e-3;
            if (horizontal)
                return Normalize(current + 180);
            // Поворот на наименьший угол: к ближайшему горизонтальному направлению.
            double turn = a <= 90 ? -a : 180 - a;
            return Normalize(current + turn);
        }
    }
}

using System.Globalization;
using System.Linq;

namespace InvAuto.Core.Formatting
{
    /// <summary>Числа, введённые пользователем или пришедшие из Excel: запятая или точка, любые пробелы-разделители.</summary>
    public static class NumberText
    {
        /// <summary>«1 234,5», «1 234.5», «1234,5» → 1234,5. Пробелы (в том числе неразрывные) убираются.</summary>
        public static bool TryParse(string text, out double value)
        {
            string clean = new string((text ?? string.Empty).Where(c => !char.IsWhiteSpace(c)).ToArray()).Replace(',', '.');
            return double.TryParse(clean, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
        }

        /// <summary>Число или null.</summary>
        public static double? Parse(string text) => TryParse(text, out double value) ? value : (double?)null;

        /// <summary>Число в пределах [min; max].</summary>
        public static bool TryParse(string text, double min, double max, out double value) =>
            TryParse(text, out value) && value >= min && value <= max;

        /// <summary>Для полей ввода: до двух знаков после запятой, в текущей культуре.</summary>
        public static string Format(double value) => value.ToString("0.##", CultureInfo.CurrentCulture);
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;

namespace InvAuto.Core.Sorting
{
    /// <summary>
    /// Сравнение строк как в проводнике Windows: числа внутри строки сравниваются по величине
    /// («Деталь 2» раньше «Деталь 10»), буквы — без учёта регистра по правилам текущего языка.
    /// </summary>
    public sealed class NaturalComparer : IComparer<string>
    {
        public static NaturalComparer Instance { get; } = new NaturalComparer(CultureInfo.CurrentCulture);

        private readonly CompareInfo _compare;

        public NaturalComparer(CultureInfo culture)
        {
            _compare = culture.CompareInfo;
        }

        public int Compare(string x, string y)
        {
            if (ReferenceEquals(x, y))
                return 0;
            if (x == null)
                return -1;
            if (y == null)
                return 1;

            int i = 0, j = 0;
            while (i < x.Length && j < y.Length)
            {
                if (char.IsDigit(x[i]) && char.IsDigit(y[j]))
                {
                    int startX = i, startY = j;
                    while (i < x.Length && char.IsDigit(x[i])) i++;
                    while (j < y.Length && char.IsDigit(y[j])) j++;
                    int result = CompareNumbers(x.Substring(startX, i - startX), y.Substring(startY, j - startY));
                    if (result != 0)
                        return result;
                }
                else
                {
                    int startX = i, startY = j;
                    while (i < x.Length && !char.IsDigit(x[i])) i++;
                    while (j < y.Length && !char.IsDigit(y[j])) j++;
                    int result = _compare.Compare(x.Substring(startX, i - startX), y.Substring(startY, j - startY),
                        CompareOptions.IgnoreCase);
                    if (result != 0)
                        return Math.Sign(result);
                }
            }

            if (i < x.Length || j < y.Length)
                return i < x.Length ? 1 : -1; // общее начало — короче раньше
            // Равны по-проводниковски («a01» и «a1») — для устойчивого порядка сравниваем обычным образом.
            return Math.Sign(string.CompareOrdinal(x, y));
        }

        /// <summary>Числа любой длины: без ведущих нулей сначала по количеству цифр, затем по цифрам.</summary>
        private static int CompareNumbers(string a, string b)
        {
            string ta = a.TrimStart('0'), tb = b.TrimStart('0');
            if (ta.Length != tb.Length)
                return ta.Length < tb.Length ? -1 : 1;
            int result = string.CompareOrdinal(ta, tb);
            if (result != 0)
                return Math.Sign(result);
            return 0;
        }
    }

    /// <summary>
    /// Сравнение значений колонки: числа — по величине, строки — естественно, логические — «нет» раньше «да».
    /// Пустые значения всегда в конце, при любом направлении сортировки.
    /// </summary>
    public static class SortValues
    {
        public static int Compare(object x, object y, bool descending)
        {
            bool emptyX = IsEmpty(x), emptyY = IsEmpty(y);
            if (emptyX || emptyY)
                return emptyX == emptyY ? 0 : emptyX ? 1 : -1;

            int result;
            if (IsNumber(x) && IsNumber(y))
                result = Convert.ToDouble(x, CultureInfo.InvariantCulture).CompareTo(Convert.ToDouble(y, CultureInfo.InvariantCulture));
            else if (x is bool bx && y is bool by)
                result = bx.CompareTo(by);
            else
                result = NaturalComparer.Instance.Compare(Convert.ToString(x, CultureInfo.CurrentCulture),
                    Convert.ToString(y, CultureInfo.CurrentCulture));

            return descending ? -result : result;
        }

        private static bool IsEmpty(object value) => value == null || value is string s && s.Trim().Length == 0;

        private static bool IsNumber(object value) =>
            value is int || value is long || value is double || value is float || value is decimal;
    }
}

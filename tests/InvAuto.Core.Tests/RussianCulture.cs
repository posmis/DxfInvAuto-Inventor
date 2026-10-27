using System.Globalization;
using System.Runtime.CompilerServices;

namespace InvAuto.Core.Tests
{
    /// <summary>
    /// Все тесты — в русской локали, как у пользователей: запятая в дробных числах, кириллица в сортировке раньше
    /// латиницы. Так ошибки форматирования и сравнения строк видны на любой машине, а не только на русской Windows.
    /// </summary>
    internal static class RussianCulture
    {
        [ModuleInitializer]
        internal static void Apply()
        {
            var ru = CultureInfo.GetCultureInfo("ru-RU");
            CultureInfo.DefaultThreadCurrentCulture = ru;
            CultureInfo.DefaultThreadCurrentUICulture = ru;
            CultureInfo.CurrentCulture = ru;
            CultureInfo.CurrentUICulture = ru;
        }
    }
}

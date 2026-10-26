namespace InvAuto.Core.Settings
{
    /// <summary>Настройки приложения в целом (окно «Настройки InvAuto»).</summary>
    public sealed class AppSettings
    {
        public const string Key = "App";

        /// <summary>Развертки в кэше, которые не открывались дольше, удаляются автоматически, дней.</summary>
        public int CacheMaxAgeDays { get; set; } = 30;

        /// <summary>Файлы журнала старше удаляются при запуске Inventor, дней.</summary>
        public int LogMaxAgeDays { get; set; } = 60;

        /// <summary>Значения в разумных пределах (на случай ручной правки файла).</summary>
        public void Normalize()
        {
            CacheMaxAgeDays = Clamp(CacheMaxAgeDays, 1, 3650);
            LogMaxAgeDays = Clamp(LogMaxAgeDays, 1, 3650);
        }

        private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
    }
}

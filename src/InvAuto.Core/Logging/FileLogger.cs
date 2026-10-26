using System;
using System.IO;
using System.Text;

namespace InvAuto.Core.Logging
{
    /// <summary>Пишет лог в файл на каждый день: {папка}\yyyy-MM-dd.log. Ошибки записи игнорируются.</summary>
    public sealed class FileLogger : ILogger
    {
        private readonly string _folder;
        private readonly object _sync = new object();

        public FileLogger(string folder)
        {
            _folder = folder;
        }

        /// <summary>Удаляет логи старше maxAge. Возвращает количество удалённых файлов; ошибки игнорируются.</summary>
        public int Cleanup(TimeSpan maxAge)
        {
            int removed = 0;
            try
            {
                if (!Directory.Exists(_folder))
                    return 0;
                DateTime limit = DateTime.Now - maxAge;
                foreach (string file in Directory.GetFiles(_folder, "*.log"))
                {
                    try
                    {
                        if (File.GetLastWriteTime(file) < limit)
                        {
                            File.Delete(file);
                            removed++;
                        }
                    }
                    catch
                    {
                        // Файл занят — удалим в следующий раз.
                    }
                }
            }
            catch
            {
                // Лог не должен ронять Inventor.
            }
            return removed;
        }

        public string Folder => _folder;

        public string CurrentLogFile => Path.Combine(_folder, DateTime.Now.ToString("yyyy-MM-dd") + ".log");

        public void Info(string message) => Write("INFO", message, null);
        public void Warn(string message) => Write("WARN", message, null);
        public void Error(string message, Exception ex = null) => Write("ERROR", message, ex);

        private void Write(string level, string message, Exception ex)
        {
            try
            {
                var line = new StringBuilder()
                    .Append(DateTime.Now.ToString("HH:mm:ss.fff")).Append(' ')
                    .Append(level).Append(' ')
                    .Append(message);
                if (ex != null)
                    line.AppendLine().Append(ex);
                line.AppendLine();

                lock (_sync)
                {
                    Directory.CreateDirectory(_folder);
                    File.AppendAllText(CurrentLogFile, line.ToString(), Encoding.UTF8);
                }
            }
            catch
            {
                // Лог не должен ронять Inventor.
            }
        }
    }
}

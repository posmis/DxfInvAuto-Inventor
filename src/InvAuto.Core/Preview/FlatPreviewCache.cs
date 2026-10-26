using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace InvAuto.Core.Preview
{
    /// <summary>
    /// Кэш сформированных разверток: DXF во временной папке.
    /// Имя файла — хэш от пути детали, даты её сохранения и настроек выгрузки, поэтому
    /// изменённая деталь или другой ini дают новый файл, а старый просто перестаёт использоваться
    /// и со временем удаляется Cleanup.
    /// </summary>
    public sealed class FlatPreviewCache
    {
        public FlatPreviewCache(string folder)
        {
            Folder = folder;
        }

        public string Folder { get; }

        /// <param name="partPath">Полный путь к файлу детали.</param>
        /// <param name="partWriteTimeUtc">Дата последнего сохранения файла детали.</param>
        /// <param name="writerSignature">Всё, от чего зависит содержимое DXF: способ выгрузки, ini и его дата.</param>
        public string PathFor(string partPath, DateTime partWriteTimeUtc, string writerSignature)
        {
            string key = string.Join("|",
                partPath.Trim().ToUpperInvariant(),
                partWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture),
                writerSignature ?? string.Empty);

            using (var sha = SHA1.Create())
            {
                byte[] hash = sha.ComputeHash(Encoding.UTF8.GetBytes(key));
                var name = new StringBuilder(hash.Length * 2);
                foreach (byte b in hash)
                    name.Append(b.ToString("x2", CultureInfo.InvariantCulture));
                return Path.Combine(Folder, name + ".dxf");
            }
        }

        /// <summary>Удаляет развертки, которые не использовались дольше maxAge. Ошибки игнорируются.</summary>
        public int Cleanup(TimeSpan maxAge)
        {
            if (!Directory.Exists(Folder))
                return 0;

            int removed = 0;
            DateTime limit = DateTime.UtcNow - maxAge;
            foreach (string file in Directory.GetFiles(Folder, "*.dxf", SearchOption.AllDirectories))
            {
                try
                {
                    if (File.GetLastWriteTimeUtc(file) < limit)
                    {
                        File.Delete(file);
                        removed++;
                    }
                }
                catch
                {
                    // Файл может быть занят — удалим в следующий раз.
                }
            }
            return removed;
        }
    }
}

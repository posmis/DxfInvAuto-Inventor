using System;
using System.IO;
using System.Xml.Serialization;
using InvAuto.Core.Logging;

namespace InvAuto.Core.Settings
{
    /// <summary>
    /// Хранит настройки каждой функции в отдельном XML-файле: {папка}\{key}.xml.
    /// XmlSerializer выбран намеренно: он есть в .NET Framework, и плагину не нужны
    /// сторонние сборки, которые в процессе Inventor часто конфликтуют по версиям.
    /// </summary>
    public sealed class XmlSettingsStore
    {
        private readonly string _folder;
        private readonly ILogger _log;

        public XmlSettingsStore(string folder, ILogger log)
        {
            _folder = folder;
            _log = log;
        }

        public string Folder => _folder;

        public T Load<T>(string key) where T : class, new()
        {
            string path = PathFor(key);
            if (!File.Exists(path))
                return new T();

            try
            {
                using (var stream = File.OpenRead(path))
                    return (T)new XmlSerializer(typeof(T)).Deserialize(stream) ?? new T();
            }
            catch (Exception ex)
            {
                // Испорченный файл не затираем молча при следующем сохранении — откладываем копию в backup.
                string copy = Backup(key, path, "повреждён", move: false);
                _log.Error($"Не удалось прочитать настройки '{path}', используются значения по умолчанию" +
                           (copy != null ? $"; копия файла: {copy}" : string.Empty), ex);
                return new T();
            }
        }

        public void Save<T>(string key, T settings) where T : class
        {
            Directory.CreateDirectory(_folder);
            string path = PathFor(key);
            string temp = path + ".tmp";

            using (var stream = File.Create(temp))
                new XmlSerializer(typeof(T)).Serialize(stream, settings);

            // Замена одним действием: при сбое остаётся либо старый файл, либо новый — не пустота.
            if (File.Exists(path))
                File.Replace(temp, path, null);
            else
                File.Move(temp, path);
        }

        public bool Exists(string key) => File.Exists(PathFor(key));

        /// <summary>
        /// Сбрасывает настройки к значениям по умолчанию: файл переносится в папку backup с отметкой времени.
        /// Возвращает путь к резервной копии (null — настроек не было).
        /// </summary>
        public string Reset(string key)
        {
            string path = PathFor(key);
            if (!File.Exists(path))
                return null;

            string backup = Backup(key, path, null, move: true);
            _log.Info($"Настройки '{key}' сброшены, резервная копия: {backup}");
            return backup;
        }

        /// <summary>Копия (или перенос) файла настроек в папку backup с отметкой времени. null — не удалось.</summary>
        private string Backup(string key, string path, string note, bool move)
        {
            try
            {
                string backupFolder = Path.Combine(_folder, "backup");
                Directory.CreateDirectory(backupFolder);
                string stamp = $"{key}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}" + (note == null ? string.Empty : "_" + note);
                string backup = Path.Combine(backupFolder, stamp + ".xml");
                for (int i = 2; File.Exists(backup); i++)
                    backup = Path.Combine(backupFolder, $"{stamp}_{i}.xml");
                if (move)
                    File.Move(path, backup);
                else
                    File.Copy(path, backup);
                return backup;
            }
            catch (Exception ex) when (!move)
            {
                _log.Error($"Не удалось сохранить копию повреждённых настроек '{path}'", ex);
                return null;
            }
        }

        private string PathFor(string key) => Path.Combine(_folder, key + ".xml");
    }
}

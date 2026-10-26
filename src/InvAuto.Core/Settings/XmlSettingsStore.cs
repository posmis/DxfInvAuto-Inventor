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
                _log.Error($"Не удалось прочитать настройки '{path}', используются значения по умолчанию", ex);
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

            if (File.Exists(path))
                File.Delete(path);
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

            string backupFolder = Path.Combine(_folder, "backup");
            Directory.CreateDirectory(backupFolder);
            string backup = Path.Combine(backupFolder, $"{key}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}.xml");
            for (int i = 2; File.Exists(backup); i++)
                backup = Path.Combine(backupFolder, $"{key}_{DateTime.Now:yyyy-MM-dd_HH-mm-ss}_{i}.xml");

            File.Move(path, backup);
            _log.Info($"Настройки '{key}' сброшены, резервная копия: {backup}");
            return backup;
        }

        private string PathFor(string key) => Path.Combine(_folder, key + ".xml");
    }
}

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

        private string PathFor(string key) => Path.Combine(_folder, key + ".xml");
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using InvAuto.Core.Logging;

namespace InvAuto.Framework
{
    /// <summary>
    /// Находит функции: все неабстрактные классы IFeature в InvAuto.dll
    /// и во внешних сборках из папки Plugins рядом с плагином.
    /// </summary>
    public sealed class FeatureRegistry
    {
        private readonly ILogger _log;

        public FeatureRegistry(ILogger log)
        {
            _log = log;
        }

        public IReadOnlyList<IFeature> Discover(string pluginFolder)
        {
            var assemblies = new List<Assembly> { typeof(FeatureRegistry).Assembly };
            assemblies.AddRange(LoadPluginAssemblies(pluginFolder));

            var features = new List<IFeature>();
            var ids = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            foreach (var type in assemblies.SelectMany(SafeGetTypes))
            {
                if (!typeof(IFeature).IsAssignableFrom(type) || type.IsAbstract || type.IsInterface)
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                try
                {
                    var feature = (IFeature)Activator.CreateInstance(type);
                    if (!ids.Add(feature.Id))
                    {
                        _log.Warn($"Функция с Id '{feature.Id}' уже зарегистрирована, {type.FullName} пропущен");
                        continue;
                    }
                    features.Add(feature);
                }
                catch (Exception ex)
                {
                    _log.Error($"Не удалось создать функцию {type.FullName}", ex);
                }
            }

            return features.OrderBy(f => f.Panel).ThenBy(f => f.Order).ToList();
        }

        private IEnumerable<Assembly> LoadPluginAssemblies(string folder)
        {
            if (!Directory.Exists(folder))
                yield break;

            foreach (string file in Directory.GetFiles(folder, "*.dll"))
            {
                Assembly assembly = null;
                try
                {
                    assembly = Assembly.LoadFrom(file);
                }
                catch (Exception ex)
                {
                    _log.Error($"Не удалось загрузить внешний модуль {file}", ex);
                }
                if (assembly != null)
                    yield return assembly;
            }
        }

        private IEnumerable<Type> SafeGetTypes(Assembly assembly)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex)
            {
                _log.Error($"Часть типов из {assembly.FullName} не загрузилась", ex);
                return ex.Types.Where(t => t != null);
            }
        }
    }
}

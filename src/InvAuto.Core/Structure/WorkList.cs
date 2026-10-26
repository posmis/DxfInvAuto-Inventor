using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.Structure
{
    /// <summary>
    /// Список деталей, собранный пользователем: пути файлов в порядке добавления, без повторов
    /// (регистр пути не важен). Не зависит от выбора в дереве.
    /// </summary>
    public sealed class WorkList
    {
        private readonly List<string> _items = new List<string>();
        private readonly HashSet<string> _set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public IReadOnlyList<string> Items => _items;
        public int Count => _items.Count;

        public bool Contains(string path) => path != null && _set.Contains(path);

        /// <summary>Добавляет новые пути в конец. Возвращает добавленные и количество уже бывших в списке.</summary>
        public (IReadOnlyList<string> Added, int AlreadyListed) Add(IEnumerable<string> paths)
        {
            var added = new List<string>();
            int already = 0;
            foreach (string path in paths.Where(p => !string.IsNullOrEmpty(p)))
            {
                if (_set.Add(path))
                {
                    _items.Add(path);
                    added.Add(path);
                }
                else if (!added.Contains(path, StringComparer.OrdinalIgnoreCase))
                {
                    already++;
                }
            }
            return (added, already);
        }

        /// <summary>Удаляет пути из списка. Возвращает, сколько было удалено.</summary>
        public int Remove(IEnumerable<string> paths)
        {
            int removed = 0;
            foreach (string path in paths.ToList())
            {
                if (path != null && _set.Remove(path))
                {
                    _items.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
                    removed++;
                }
            }
            return removed;
        }

        public void Clear()
        {
            _items.Clear();
            _set.Clear();
        }

        /// <summary>Оставляет только то, что ещё есть в изделии (после обновления состава). Возвращает число удалённых.</summary>
        public int Retain(Func<string, bool> exists) => Remove(_items.Where(p => !exists(p)));
    }
}

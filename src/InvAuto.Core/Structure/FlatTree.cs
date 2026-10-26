using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;

namespace InvAuto.Core.Structure
{
    /// <summary>Узел дерева, которое показывается плоским списком с отступами (дерево с колонками).</summary>
    public interface IFlatTreeItem<T> where T : IFlatTreeItem<T>
    {
        int Level { get; }
        bool IsExpanded { get; set; }
        bool HasChildren { get; }
        IReadOnlyList<T> Children { get; }
    }

    /// <summary>
    /// Плоский список видимых строк дерева: раскрытие и сворачивание вставляют и убирают строки потомков.
    /// Состояние раскрытия потомков запоминается: свернули и снова раскрыли — вложенные узлы как были.
    /// </summary>
    public sealed class FlatTree<T> where T : class, IFlatTreeItem<T>
    {
        public ObservableCollection<T> Rows { get; } = new ObservableCollection<T>();

        public T Root { get; private set; }

        /// <summary>Показывает дерево: раскрыты узлы с потомками-контейнерами на expandLevels уровней.</summary>
        public void Load(T root, int expandLevels)
        {
            Rows.Clear();
            Root = root;
            Rows.Add(root);
            ExpandTo(root, expandLevels);
        }

        private void ExpandTo(T row, int levels)
        {
            if (levels <= 0 || !row.HasChildren)
                return;
            Expand(row);
            foreach (var child in row.Children.Where(c => c.HasChildren))
                ExpandTo(child, levels - 1);
        }

        public void Toggle(T row)
        {
            if (row.IsExpanded)
                Collapse(row);
            else
                Expand(row);
        }

        public void Expand(T row)
        {
            if (row.IsExpanded || !row.HasChildren)
                return;
            row.IsExpanded = true;
            int index = Rows.IndexOf(row);
            if (index < 0)
                return; // строка не видна — раскроется, когда покажется родитель
            foreach (var child in row.Children)
                InsertVisible(child, ref index);
        }

        public void Collapse(T row)
        {
            if (!row.IsExpanded)
                return;
            row.IsExpanded = false;
            int index = Rows.IndexOf(row);
            if (index < 0)
                return;
            index++;
            while (index < Rows.Count && Rows[index].Level > row.Level)
                Rows.RemoveAt(index);
        }

        public void ExpandAll()
        {
            if (Root != null)
                ExpandRecursive(Root);
        }

        /// <summary>Сворачивает всё, кроме корня: видны корень и его прямые потомки.</summary>
        public void CollapseAll()
        {
            if (Root == null)
                return;
            foreach (var child in Root.Children)
                CollapseRecursive(child);
        }

        private void ExpandRecursive(T row)
        {
            Expand(row);
            foreach (var child in row.Children)
                ExpandRecursive(child);
        }

        private void CollapseRecursive(T row)
        {
            foreach (var child in row.Children)
                CollapseRecursive(child);
            Collapse(row);
        }

        private void InsertVisible(T row, ref int index)
        {
            Rows.Insert(++index, row);
            if (!row.IsExpanded)
                return;
            foreach (var child in row.Children)
                InsertVisible(child, ref index);
        }
    }
}

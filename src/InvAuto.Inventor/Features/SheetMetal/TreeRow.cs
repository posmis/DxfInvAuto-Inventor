using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Windows;
using InvAuto.Core.Structure;
using InvAuto.Framework.Ui;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>Строка дерева состава. Дерево показывается плоским списком с отступами — так у него есть колонки.</summary>
    public sealed class TreeRow : ObservableObject
    {
        private bool _isExpanded;
        private List<TreeRow> _children;

        public TreeRow(AssemblyNode node, int level)
        {
            Node = node;
            Level = level;
        }

        public AssemblyNode Node { get; }
        public int Level { get; }

        public string Title => Node.PartNumber.Length > 0 ? Node.PartNumber : Node.Name;
        public string Description => Node.Description;
        public int QuantityInParent => Node.QuantityInParent;
        public int TotalQuantity => Node.TotalQuantity;
        public bool HasChildren => Node.Children.Count > 0;
        public bool IsAssembly => Node.Kind == NodeKind.Assembly;
        public bool IsSheetMetal => Node.Kind == NodeKind.SheetMetal;
        public Thickness Indent => new Thickness(Level * 16, 0, 0, 0);

        public string ToolTip => $"{Node.Name}\n{Node.FullFileName}";

        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public IReadOnlyList<TreeRow> Children =>
            _children ?? (_children = Node.Children.Select(c => new TreeRow(c, Level + 1)).ToList());
    }

    /// <summary>Плоский список видимых строк дерева с раскрытием/сворачиванием.</summary>
    public sealed class TreeModel
    {
        public ObservableCollection<TreeRow> Rows { get; } = new ObservableCollection<TreeRow>();

        public TreeRow Root { get; private set; }

        public void Load(AssemblyNode root, int expandLevels)
        {
            Rows.Clear();
            Root = new TreeRow(root, 0);
            Rows.Add(Root);
            ExpandTo(Root, expandLevels);
        }

        private void ExpandTo(TreeRow row, int levels)
        {
            if (levels <= 0 || !row.HasChildren)
                return;
            Expand(row);
            foreach (var child in row.Children.Where(c => c.IsAssembly))
                ExpandTo(child, levels - 1);
        }

        public void Toggle(TreeRow row)
        {
            if (row.IsExpanded)
                Collapse(row);
            else
                Expand(row);
        }

        public void Expand(TreeRow row)
        {
            if (row.IsExpanded || !row.HasChildren)
                return;
            row.IsExpanded = true;
            int index = Rows.IndexOf(row);
            foreach (var child in row.Children)
                InsertVisible(child, ref index);
        }

        public void Collapse(TreeRow row)
        {
            if (!row.IsExpanded)
                return;
            row.IsExpanded = false;
            int index = Rows.IndexOf(row) + 1;
            while (index < Rows.Count && Rows[index].Level > row.Level)
                Rows.RemoveAt(index);
        }

        public void ExpandAll()
        {
            foreach (var row in Rows.ToList())
                ExpandRecursive(row);
        }

        public void CollapseAll()
        {
            if (Root == null)
                return;
            foreach (var child in Root.Children)
                Collapse(child);
        }

        private void ExpandRecursive(TreeRow row)
        {
            Expand(row);
            if (!row.HasChildren)
                return;
            foreach (var child in row.Children)
                ExpandRecursive(child);
        }

        private void InsertVisible(TreeRow row, ref int index)
        {
            Rows.Insert(++index, row);
            if (!row.IsExpanded)
                return;
            foreach (var child in row.Children)
                InsertVisible(child, ref index);
        }
    }
}

using System.Collections.Generic;
using System.Linq;
using System.Windows;
using InvAuto.Core.Structure;
using InvAuto.Framework.Ui;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>Строка дерева состава. Дерево показывается плоским списком с отступами — так у него есть колонки.</summary>
    public sealed class TreeRow : ObservableObject, IFlatTreeItem<TreeRow>
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
        public bool IsStandard => Node.Kind == NodeKind.Standard;
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
}

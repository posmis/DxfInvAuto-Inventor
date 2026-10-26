using System;
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
        private readonly Func<string, bool> _isListed;
        private bool _isExpanded;
        private List<TreeRow> _children;

        /// <param name="isListed">Есть ли деталь (по пути файла) в списке окна — для отметки в дереве.</param>
        public TreeRow(AssemblyNode node, int level, Func<string, bool> isListed = null)
        {
            Node = node;
            Level = level;
            _isListed = isListed;
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

        public string ToolTip => $"{Node.Name}\n{Node.FullFileName}" + (InList ? "\nВ списке деталей" : string.Empty);

        /// <summary>
        /// Листовая деталь уже в списке; для сборки — в списке все её листовые детали.
        /// </summary>
        public bool InList
        {
            get
            {
                if (_isListed == null)
                    return false;
                if (IsSheetMetal)
                    return _isListed(Node.FullFileName);
                if (!IsAssembly)
                    return false;
                var parts = Node.CollectSheetMetal();
                return parts.Count > 0 && parts.All(p => _isListed(p.FullFileName));
            }
        }

        /// <summary>Список деталей изменился — перечитать отметку.</summary>
        public void RefreshListMark()
        {
            OnPropertyChanged(nameof(InList));
            OnPropertyChanged(nameof(ToolTip));
        }

        public bool IsExpanded
        {
            get => _isExpanded;
            set => Set(ref _isExpanded, value);
        }

        public IReadOnlyList<TreeRow> Children =>
            _children ?? (_children = Node.Children.Select(c => new TreeRow(c, Level + 1, _isListed)).ToList());
    }
}

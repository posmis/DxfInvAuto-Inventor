using System;
using System.Collections.Generic;
using System.Linq;
using InvAuto.Core.Export;

namespace InvAuto.Core.Structure
{
    public enum NodeKind
    {
        Assembly,
        SheetMetal,
        Part
    }

    /// <summary>
    /// Узел дерева состава. Одинаковые компоненты одного родителя объединены в один узел с количеством,
    /// как в структурированной спецификации.
    /// </summary>
    public sealed class AssemblyNode
    {
        public AssemblyNode(string fullFileName, NodeKind kind, int quantityInParent, AssemblyNode parent)
        {
            FullFileName = fullFileName ?? string.Empty;
            Kind = kind;
            QuantityInParent = quantityInParent;
            Parent = parent;
            TotalQuantity = (parent?.TotalQuantity ?? 1) * quantityInParent;
        }

        public string FullFileName { get; }
        public NodeKind Kind { get; }
        public AssemblyNode Parent { get; }

        public string Name { get; set; } = string.Empty;
        public string PartNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;

        /// <summary>Сколько штук в родительском узле.</summary>
        public int QuantityInParent { get; }

        /// <summary>Сколько штук во всём изделии.</summary>
        public int TotalQuantity { get; }

        /// <summary>Данные листовой детали (только для Kind == SheetMetal).</summary>
        public SheetMetalPart SheetMetal { get; set; }

        public List<AssemblyNode> Children { get; } = new List<AssemblyNode>();

        public IEnumerable<AssemblyNode> Descendants()
        {
            foreach (var child in Children)
            {
                yield return child;
                foreach (var d in child.Descendants())
                    yield return d;
            }
        }

        /// <summary>
        /// Все листовые детали этого узла и его подсборок. Количество — на всё изделие:
        /// если подсборка входит дважды, её детали тоже считаются дважды.
        /// </summary>
        public IReadOnlyList<SheetMetalPart> CollectSheetMetal()
        {
            var totals = new Dictionary<string, (SheetMetalPart Part, int Quantity)>(StringComparer.OrdinalIgnoreCase);
            foreach (var node in new[] { this }.Concat(Descendants()))
            {
                if (node.SheetMetal == null)
                    continue;
                totals.TryGetValue(node.FullFileName, out var current);
                totals[node.FullFileName] = (node.SheetMetal, current.Quantity + node.TotalQuantity);
            }

            return totals.Values
                .Select(t => t.Part.WithQuantity(t.Quantity))
                .OrderBy(p => p.PartNumber.Length > 0 ? p.PartNumber : p.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
    }
}

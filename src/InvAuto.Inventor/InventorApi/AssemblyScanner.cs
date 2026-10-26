using System;
using System.Collections;
using System.Collections.Generic;
using Inventor;
using InvAuto.Core.Export;
using InvAuto.Core.Structure;

namespace InvAuto.InventorApi
{
    /// <summary>Листовая деталь вместе с её документом Inventor — то, что нужно для экспорта.</summary>
    public sealed class CollectedPart
    {
        public CollectedPart(PartDocument document, SheetMetalPart info)
        {
            Document = document;
            Info = info;
        }

        public PartDocument Document { get; }
        public SheetMetalPart Info { get; }
    }

    /// <summary>Результат сканирования: дерево состава и документы листовых деталей.</summary>
    public sealed class AssemblyStructure
    {
        public AssemblyStructure(AssemblyNode root, IReadOnlyDictionary<string, PartDocument> sheetMetalDocuments)
        {
            Root = root;
            SheetMetalDocuments = sheetMetalDocuments;
        }

        public AssemblyNode Root { get; }

        /// <summary>Документы листовых деталей по полному имени файла.</summary>
        public IReadOnlyDictionary<string, PartDocument> SheetMetalDocuments { get; }

        public CollectedPart ToCollected(SheetMetalPart part) =>
            new CollectedPart(SheetMetalDocuments[part.FullFileName], part);
    }

    /// <summary>
    /// Строит дерево состава активного документа. Подавленные, справочные (Reference)
    /// и виртуальные компоненты пропускаются. Одинаковые компоненты одного родителя
    /// объединяются в один узел с количеством.
    /// </summary>
    public static class AssemblyScanner
    {
        public const string SheetMetalSubType = "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}";

        public static AssemblyStructure Scan(Document document)
        {
            var scan = new Scanner();
            AssemblyNode root;

            switch (document)
            {
                case AssemblyDocument assembly:
                    root = scan.CreateNode(document, null, 1);
                    scan.AddChildren(root, assembly.ComponentDefinition.Occurrences);
                    break;
                case PartDocument _:
                    root = scan.CreateNode(document, null, 1);
                    break;
                default:
                    throw new InvalidOperationException("Откройте сборку или деталь.");
            }

            root.SortChildren();
            return new AssemblyStructure(root, scan.SheetMetalDocuments);
        }

        /// <summary>Стандартное изделие из Библиотеки компонентов (Content Center).</summary>
        private static bool IsContentMember(Document doc)
        {
            try
            {
                return doc is PartDocument part && part.ComponentDefinition.IsContentMember;
            }
            catch
            {
                return false;
            }
        }

        public static bool IsSheetMetal(Document doc) =>
            doc.DocumentType == DocumentTypeEnum.kPartDocumentObject &&
            string.Equals(doc.SubType, SheetMetalSubType, StringComparison.OrdinalIgnoreCase);

        private sealed class Scanner
        {
            // Одна деталь встречается в дереве много раз — свойства читаем один раз.
            private readonly Dictionary<string, SheetMetalPart> _infos =
                new Dictionary<string, SheetMetalPart>(StringComparer.OrdinalIgnoreCase);

            public Dictionary<string, PartDocument> SheetMetalDocuments { get; } =
                new Dictionary<string, PartDocument>(StringComparer.OrdinalIgnoreCase);

            public void AddChildren(AssemblyNode parent, IEnumerable occurrences)
            {
                // Группируем по файлу, сохраняя порядок браузера.
                var groups = new List<(Document Document, List<ComponentOccurrence> Occurrences)>();
                var index = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

                foreach (ComponentOccurrence occurrence in occurrences)
                {
                    if (occurrence.Suppressed ||
                        occurrence.BOMStructure == BOMStructureEnum.kReferenceBOMStructure ||
                        occurrence.Definition is VirtualComponentDefinition)
                        continue;

                    if (!(occurrence.Definition.Document is Document doc))
                        continue;

                    if (index.TryGetValue(doc.FullFileName, out int i))
                    {
                        groups[i].Occurrences.Add(occurrence);
                    }
                    else
                    {
                        index[doc.FullFileName] = groups.Count;
                        groups.Add((doc, new List<ComponentOccurrence> { occurrence }));
                    }
                }

                foreach (var (doc, list) in groups)
                {
                    bool purchased = list[0].BOMStructure == BOMStructureEnum.kPurchasedBOMStructure;
                    var node = CreateNode(doc, parent, list.Count, purchased);
                    parent.Children.Add(node);
                    if (node.Kind == NodeKind.Assembly)
                        AddChildren(node, list[0].SubOccurrences);
                }
            }

            public AssemblyNode CreateNode(Document doc, AssemblyNode parent, int quantity, bool purchased = false)
            {
                NodeKind kind = doc.DocumentType == DocumentTypeEnum.kAssemblyDocumentObject ? NodeKind.Assembly
                    : IsSheetMetal(doc) ? NodeKind.SheetMetal
                    : purchased || IsContentMember(doc) ? NodeKind.Standard
                    : NodeKind.Part;

                var node = new AssemblyNode(doc.FullFileName, kind, quantity, parent)
                {
                    Name = System.IO.Path.GetFileNameWithoutExtension(doc.FullFileName),
                    PartNumber = DocumentProperties.PartNumber(doc),
                    Description = DocumentProperties.Description(doc)
                };

                if (kind == NodeKind.SheetMetal)
                    node.SheetMetal = Describe((PartDocument)doc);

                return node;
            }

            private SheetMetalPart Describe(PartDocument part)
            {
                if (_infos.TryGetValue(part.FullFileName, out var cached))
                    return cached;

                var doc = (Document)part;
                var definition = (SheetMetalComponentDefinition)part.ComponentDefinition;
                var info = new SheetMetalPart
                {
                    FullFileName = part.FullFileName,
                    Name = System.IO.Path.GetFileNameWithoutExtension(part.FullFileName),
                    PartNumber = DocumentProperties.PartNumber(doc),
                    Description = DocumentProperties.Description(doc),
                    Material = DocumentProperties.Material(doc),
                    // Внутренние единицы Inventor — сантиметры.
                    ThicknessMm = Math.Round(Convert.ToDouble(definition.Thickness.Value) * 10.0, 4),
                    HasFlatPattern = definition.HasFlatPattern,
                    Quantity = 1
                };

                if (info.HasFlatPattern)
                {
                    try
                    {
                        info.FlatLengthMm = Math.Round(definition.FlatPattern.Length * 10.0, 1);
                        info.FlatWidthMm = Math.Round(definition.FlatPattern.Width * 10.0, 1);
                    }
                    catch
                    {
                        // Размер необязателен.
                    }
                }

                foreach (var pair in DocumentProperties.Custom(doc))
                    info.CustomProperties[pair.Key] = pair.Value;

                _infos[part.FullFileName] = info;
                SheetMetalDocuments[part.FullFileName] = part;
                return info;
            }
        }
    }
}

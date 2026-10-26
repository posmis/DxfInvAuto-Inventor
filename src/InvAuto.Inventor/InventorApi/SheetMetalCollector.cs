using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Inventor;
using InvAuto.Core.Export;

namespace InvAuto.InventorApi
{
    /// <summary>Листовая деталь вместе с её документом Inventor.</summary>
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

    /// <summary>
    /// Собирает уникальные листовые детали из активного документа с количеством вхождений.
    /// В отличие от простого обхода, пропускает подавленные, справочные (Reference)
    /// и виртуальные компоненты, а одинаковые имена файлов из разных папок не склеивает.
    /// </summary>
    public static class SheetMetalCollector
    {
        public const string SheetMetalSubType = "{9C464203-9BAE-11D3-8BAD-0060B0CE6BB4}";

        public static IReadOnlyList<CollectedPart> Collect(Document document)
        {
            var parts = new Dictionary<string, CollectedPart>(StringComparer.OrdinalIgnoreCase);

            switch (document)
            {
                case PartDocument part when IsSheetMetal(part):
                    AddOrIncrement(parts, part);
                    break;
                case AssemblyDocument assembly:
                    Walk(assembly.ComponentDefinition.Occurrences, parts);
                    break;
            }

            return parts.Values.OrderBy(p => p.Info.Name).ToList();
        }

        public static bool IsSheetMetal(PartDocument doc) =>
            string.Equals(doc.SubType, SheetMetalSubType, StringComparison.OrdinalIgnoreCase);

        private static void Walk(IEnumerable occurrences, Dictionary<string, CollectedPart> parts)
        {
            foreach (ComponentOccurrence occurrence in occurrences)
            {
                if (occurrence.Suppressed)
                    continue;
                if (occurrence.BOMStructure == BOMStructureEnum.kReferenceBOMStructure)
                    continue;
                if (occurrence.Definition is VirtualComponentDefinition)
                    continue;

                if (occurrence.DefinitionDocumentType == DocumentTypeEnum.kAssemblyDocumentObject)
                {
                    Walk(occurrence.SubOccurrences, parts);
                }
                else if (occurrence.Definition.Document is PartDocument part && IsSheetMetal(part))
                {
                    AddOrIncrement(parts, part);
                }
            }
        }

        private static void AddOrIncrement(Dictionary<string, CollectedPart> parts, PartDocument doc)
        {
            if (parts.TryGetValue(doc.FullFileName, out var existing))
            {
                existing.Info.Quantity++;
                return;
            }

            var info = Describe(doc);
            info.Quantity = 1;
            parts.Add(doc.FullFileName, new CollectedPart(doc, info));
        }

        private static SheetMetalPart Describe(PartDocument doc)
        {
            var definition = (SheetMetalComponentDefinition)doc.ComponentDefinition;
            var info = new SheetMetalPart
            {
                FullFileName = doc.FullFileName,
                Name = System.IO.Path.GetFileNameWithoutExtension(doc.FullFileName),
                PartNumber = DocumentProperties.PartNumber((Document)doc),
                Description = DocumentProperties.Description((Document)doc),
                Material = DocumentProperties.Material((Document)doc),
                // Внутренние единицы Inventor — сантиметры.
                ThicknessMm = Math.Round(Convert.ToDouble(definition.Thickness.Value) * 10.0, 4)
            };

            foreach (var pair in DocumentProperties.Custom((Document)doc))
                info.CustomProperties[pair.Key] = pair.Value;

            return info;
        }
    }
}

using System;
using System.Collections.Generic;

namespace InvAuto.Core.Export
{
    /// <summary>Данные листовой детали, собранные из Inventor. Не содержит COM-объектов.</summary>
    public sealed class SheetMetalPart
    {
        public string FullFileName { get; set; } = string.Empty;

        /// <summary>Имя файла без расширения.</summary>
        public string Name { get; set; } = string.Empty;

        public string PartNumber { get; set; } = string.Empty;
        public string Description { get; set; } = string.Empty;
        public string Material { get; set; } = string.Empty;
        public double ThicknessMm { get; set; }

        /// <summary>Количество вхождений в сборку (для детали, открытой отдельно, равно 1).</summary>
        public int Quantity { get; set; }

        /// <summary>Пользовательские iProperties детали.</summary>
        public IDictionary<string, string> CustomProperties { get; } =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        /// <summary>
        /// Значения для подстановки в шаблоны имён. Пользовательские свойства доступны по своему имени,
        /// встроенные токены имеют приоритет при совпадении имён.
        /// </summary>
        public IReadOnlyDictionary<string, object> ToTemplateValues(int quantityMultiplier = 1, string assemblyName = "")
        {
            var values = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            foreach (var pair in CustomProperties)
                values[pair.Key] = pair.Value;

            values["Name"] = Name;
            values["PartNumber"] = PartNumber;
            values["Description"] = Description;
            values["Material"] = Material;
            values["Thickness"] = ThicknessMm;
            values["Qty"] = Quantity * Math.Max(1, quantityMultiplier);
            values["Assembly"] = assemblyName ?? string.Empty;
            return values;
        }
    }
}

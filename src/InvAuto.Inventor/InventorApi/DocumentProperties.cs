using System;
using System.Collections.Generic;
using System.Globalization;
using Inventor;

namespace InvAuto.InventorApi
{
    /// <summary>Чтение iProperties. Имена наборов и свойств внутренние, от языка Inventor не зависят.</summary>
    public static class DocumentProperties
    {
        public const string DesignTracking = "Design Tracking Properties";
        public const string Summary = "Inventor Summary Information";
        public const string UserDefined = "Inventor User Defined Properties";

        public static string PartNumber(Document doc) => Get(doc, DesignTracking, "Part Number");
        public static string Description(Document doc) => Get(doc, DesignTracking, "Description");
        public static string Material(Document doc) => Get(doc, DesignTracking, "Material");

        public static string Get(Document doc, string setName, string propertyName)
        {
            try
            {
                object value = doc.PropertySets[setName][propertyName].Value;
                return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch
            {
                return string.Empty;
            }
        }

        public static IDictionary<string, string> Custom(Document doc)
        {
            var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                foreach (Property property in doc.PropertySets[UserDefined])
                    result[property.Name] = Convert.ToString(property.Value, CultureInfo.InvariantCulture) ?? string.Empty;
            }
            catch
            {
                // У документа может не быть пользовательских свойств.
            }
            return result;
        }
    }
}

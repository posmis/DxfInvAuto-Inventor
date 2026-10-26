using System.Collections.Generic;

namespace InvAuto.Core.Export
{
    /// <summary>
    /// Строит строку формата для SheetMetalComponentDefinition.DataIO.WriteDataToFile,
    /// например "FLAT PATTERN DXF?AcadVersion=2004&amp;InvisibleLayers=IV_TANGENT;...".
    /// Используется в режиме DxfExportMethod.FormatString.
    /// </summary>
    public static class DxfFormatString
    {
        /// <summary>
        /// Все слои развертки Inventor, кроме контуров и линий гиба. Они скрываются всегда —
        /// в файл попадает только то, что нужно для резки и гибки.
        /// </summary>
        public static readonly IReadOnlyList<string> AlwaysHiddenLayers = new[]
        {
            "IV_TANGENT",
            "IV_TOOL_CENTER",
            "IV_TOOL_CENTER_DOWN",
            "IV_ARC_CENTERS",
            "IV_FEATURE_PROFILES",
            "IV_FEATURE_PROFILES_DOWN",
            "IV_ALTREP_FRONT",
            "IV_ALTREP_BACK",
            "IV_UNCONSUMED_SKETCHES",
            "IV_UNCONSUMED_SKETCH_CONSTRUCTION",
            "IV_ROLL_TANGENT",
            "IV_ROLL",
            "IV_MARK_SURFACE",
            "IV_MARK_SURFACE_BACK"
        };

        public static string Build(FlatPatternExportSettings s)
        {
            var options = new List<string> { "AcadVersion=" + s.AcadVersion };

            AddLayer(options, "OuterProfileLayer", s.OuterProfileLayer);
            AddLayer(options, "InteriorProfilesLayer", s.InteriorProfilesLayer);
            if (s.ExportBendLines)
            {
                AddLayer(options, "BendLayer", s.BendUpLayer);
                AddLayer(options, "BendDownLayer", s.BendDownLayer);
            }

            options.Add("MergeProfilesIntoPolyline=" + (s.MergeProfilesIntoPolyline ? "True" : "False"));
            options.Add("RebaseGeometry=" + (s.RebaseGeometry ? "True" : "False"));
            options.Add("InvisibleLayers=" + string.Join(";", InvisibleLayers(s)));

            return "FLAT PATTERN DXF?" + string.Join("&", options);
        }

        private static IEnumerable<string> InvisibleLayers(FlatPatternExportSettings s)
        {
            foreach (var layer in AlwaysHiddenLayers)
                yield return layer;

            if (!s.ExportBendLines)
            {
                yield return "IV_BEND";
                yield return "IV_BEND_DOWN";
            }
        }

        private static void AddLayer(List<string> options, string option, string layer)
        {
            if (!string.IsNullOrWhiteSpace(layer))
                options.Add(option + "=" + layer.Trim());
        }
    }
}

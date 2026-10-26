using System.Collections.Generic;

namespace InvAuto.Core.Export
{
    /// <summary>
    /// Строит строку формата для SheetMetalComponentDefinition.DataIO.WriteDataToFile,
    /// например "FLAT PATTERN DXF?AcadVersion=2000&amp;OuterProfileLayer=OUTER&amp;InvisibleLayers=IV_TANGENT".
    /// Позволяет обойтись без внешнего .ini-файла.
    /// </summary>
    public static class DxfFormatString
    {
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
            if (!s.ExportTangentLines)
            {
                yield return "IV_TANGENT";
                yield return "IV_ROLL_TANGENT";
            }
            if (!s.ExportBendLines)
            {
                yield return "IV_BEND";
                yield return "IV_BEND_DOWN";
            }
            if (!s.ExportArcCenters)
                yield return "IV_ARC_CENTERS";

            yield return "IV_TOOL_CENTER";
            yield return "IV_TOOL_CENTER_DOWN";
            yield return "IV_UNCONSUMED_SKETCHES";
        }

        private static void AddLayer(List<string> options, string option, string layer)
        {
            if (!string.IsNullOrWhiteSpace(layer))
                options.Add(option + "=" + layer.Trim());
        }
    }
}

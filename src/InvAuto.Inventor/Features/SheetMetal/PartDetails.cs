using System;
using System.Globalization;
using System.Linq;
using InvAuto.Core.Materials;
using InvAuto.Core.Preflight;
using InvAuto.Framework.Ui;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>
    /// Свойства детали для таблицы свойств — одинаково в панели «Свойства» окна деталей и в окне просмотра.
    /// Группы: Деталь, Материал, Развертка, Резка, Линии гиба, Выгрузка, iProperties.
    /// </summary>
    public static class PartDetails
    {
        private static string N(double value, string format) => value.ToString(format, CultureInfo.CurrentCulture);

        /// <param name="isOwnLine">Есть ли у линии гиба свои параметры (по ключу линии); null — не показывать.</param>
        public static void Fill(PropertyTable table, PartRow row, Func<string, bool> isOwnLine = null)
        {
            table.Clear();
            if (row == null)
                return;

            if (row.Issues != null)
            {
                table.Section("Проверка");
                if (row.Issues.Count == 0)
                    table.Row("Результат", "нет замечаний");
                foreach (var issue in row.Issues.OrderByDescending(i => i.Severity))
                    table.Row(issue.SeverityText, issue.Message, null,
                        issue.Severity == IssueSeverity.Error ? PropertyKind.Error
                        : issue.Severity == IssueSeverity.Warning ? PropertyKind.Warning : PropertyKind.Muted);
            }

            table.Section("Деталь");
            table.Row("Обозначение", row.PartNumber);
            table.Row("Наименование", row.Description);
            table.Row("Толщина", N(row.Thickness, "0.##"), "мм");
            table.Row("Количество", row.Quantity.ToString(CultureInfo.CurrentCulture), "шт");
            if (row.Total != row.Quantity)
                table.Row("На изделия", row.Total.ToString(CultureInfo.CurrentCulture), "шт");
            table.Row("Материал в Inventor", row.Material, null, PropertyKind.Muted);
            table.Row("Файл", row.FilePath, null, PropertyKind.Muted);

            table.Section("Материал");
            var material = row.LibraryMaterial.Material;
            if (material == null)
            {
                table.Row("Материал", "не задан", null, PropertyKind.Warning, row.LibraryMaterialHint);
            }
            else
            {
                table.Row("Материал", material.Name, null, row.MaterialMismatch ? PropertyKind.Error : PropertyKind.Normal, row.LibraryMaterialHint);
                table.Row("Назначен", row.MaterialManual ? "вручную" : "по толщине детали");
                table.Row("Плотность", N(material.Density, "0.#"), "кг/м³");
                if (row.MaterialMismatch)
                    table.Row("Толщина материала", N(material.Thickness, "0.##") + " — не совпадает с деталью", "мм", PropertyKind.Error);
            }

            table.Section("Развертка");
            if (!row.HasFlatPattern)
                table.Row("Развертка", "нет в детали", null, PropertyKind.Warning);
            table.Row("Габариты", row.FlatSize, "мм");

            var m = row.Metrics;
            bool hasMetrics = m != null && m.Pierces > 0;
            if (hasMetrics)
            {
                table.Row("Площадь", N(m.Area * 1e-6, "0.####"), "м²");
                table.Row("Площадь по контуру", N(m.OuterArea * 1e-6, "0.####"), "м²", PropertyKind.Muted);
                if (row.Mass is double mass)
                {
                    table.Row("Масса", N(mass, "0.###"), "кг", PropertyKind.Emphasis);
                    if (row.Total > 1 && row.TotalMass is double total)
                        table.Row("Масса на изделия", N(total, "0.##"), "кг", PropertyKind.Emphasis);
                }
                else
                {
                    table.Row("Масса", "нет материала в библиотеке", null, PropertyKind.Warning);
                }
            }
            else if (row.HasFlatPattern)
            {
                table.Row("Состояние", row.FlatPreviewError.Length > 0 ? row.FlatPreviewError : "не сформирована",
                    null, row.FlatPreviewError.Length > 0 ? PropertyKind.Error : PropertyKind.Muted);
                table.Row("Площадь, масса, рез", "после «Сформировать развертки»", null, PropertyKind.Muted);
            }

            if (hasMetrics)
            {
                table.Section("Резка");
                table.Row("Длина реза", N(m.CutLength / 1000, "0.###"), "м", PropertyKind.Emphasis, "Наружный контур и все отверстия, на одну деталь");
                table.Row("Врезки", m.Pierces.ToString(CultureInfo.CurrentCulture), "шт", PropertyKind.Normal, "Один замкнутый контур — одна врезка");
                if (row.Total > 1)
                {
                    table.Row("Длина реза на изделия", N(m.CutLength / 1000 * row.Total, "0.##"), "м");
                    table.Row("Врезки на изделия", (m.Pierces * row.Total).ToString(CultureInfo.CurrentCulture), "шт");
                }
                if (m.BendLength > 0)
                    table.Row("Линии гиба в DXF", N(m.BendLength / 1000, "0.###"), "м", PropertyKind.Muted,
                        "Прорези, пунктир или вырезы по линиям гиба — то, что попадёт в файл");
                if (m.OpenChains > 0)
                    table.Row("Незамкнутые контуры", m.OpenChains.ToString(CultureInfo.CurrentCulture), "шт", PropertyKind.Warning,
                        "Контур в DXF не замкнулся — проверьте развертку: площадь и масса могут быть неверны");
            }

            table.Section("Линии гиба");
            table.Row("Обработка", row.BendSummary);
            foreach (var item in row.BendLines)
            {
                bool own = isOwnLine?.Invoke(item.Line.Key) == true;
                table.Row(
                    string.Format(CultureInfo.CurrentCulture, "Линия {0} · {1}, {2:0.#} мм", item.Line.Index, item.Line.IsDown ? "вниз" : "вверх", item.Line.Length),
                    item.Layout.Description + (own ? " (своя)" : string.Empty));
            }

            if (row.Status.Length > 0)
            {
                table.Section("Выгрузка");
                table.Row("Статус", row.Status, null,
                    row.StatusKind == RowStatus.Error ? PropertyKind.Error : row.StatusKind == RowStatus.Warning ? PropertyKind.Warning : PropertyKind.Normal);
            }

            if (row.Part.CustomProperties.Count > 0)
            {
                table.Section("Свойства iProperties");
                foreach (var pair in row.Part.CustomProperties.OrderBy(p => p.Key, StringComparer.CurrentCultureIgnoreCase))
                    table.Row(pair.Key, pair.Value);
            }
        }
    }
}

using System.Collections.Generic;

namespace InvAuto.Core.SheetMetal
{
    /// <summary>Каталог колонок списка листовых деталей. Id сохраняются в настройках — не переименовывать.</summary>
    public static class PartColumns
    {
        public const string Flat = "Flat";
        public const string Thumbnail = "Thumbnail";
        public const string FileName = "FileName";
        public const string PartNumber = "PartNumber";
        public const string Description = "Description";
        public const string Material = "Material";
        public const string Thickness = "Thickness";
        public const string FlatSize = "FlatSize";
        public const string FlatState = "FlatState";
        public const string Quantity = "Quantity";
        public const string Total = "Total";
        public const string Status = "Status";
        public const string FilePath = "FilePath";
        public const string Bends = "Bends";
        public const string LibraryMaterial = "LibraryMaterial";
        public const string Area = "Area";
        public const string Mass = "Mass";
        public const string TotalMass = "TotalMass";
        public const string CutLength = "CutLength";
        public const string Pierces = "Pierces";
        public const string Check = "Check";

        /// <summary>Колонка флажка: в список колонок не входит, но по ней можно сортировать.</summary>
        public const string Checked = "Checked";

        /// <summary>Префикс колонок с пользовательскими iProperties: "prop:Заказ".</summary>
        public const string PropertyPrefix = "prop:";

        public static readonly IReadOnlyList<(string Id, string Header)> All = new[]
        {
            (FileName, "Имя файла"),
            (Check, "Проверка"),
            (Thickness, "Толщина, мм"),
            (FlatSize, "Габариты, мм"),
            (Quantity, "Количество"),
            (Flat, "Развертка"),
            (Thumbnail, "Миниатюра"),
            (Bends, "Линии гиба"),
            (PartNumber, "Обозначение"),
            (Description, "Наименование"),
            (Material, "Материал"),
            (FlatState, "Наличие развертки"),
            (Total, "Итого на изделия"),
            (Status, "Статус выгрузки"),
            (LibraryMaterial, "Материал (библиотека)"),
            (Area, "Площадь, м²"),
            (Mass, "Масса, кг"),
            (TotalMass, "Масса на изделия, кг"),
            (CutLength, "Длина реза, м"),
            (Pierces, "Врезки"),
            (FilePath, "Путь к файлу")
        };

        /// <summary>Колонки с картинками — для них строки делаются выше.</summary>
        public static bool IsImage(string id) => id == Flat || id == Thumbnail;
    }
}

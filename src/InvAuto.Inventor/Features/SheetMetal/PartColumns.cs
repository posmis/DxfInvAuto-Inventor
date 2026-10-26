using System.Collections.Generic;

namespace InvAuto.Features.SheetMetal
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

        /// <summary>Префикс колонок с пользовательскими iProperties: "prop:Заказ".</summary>
        public const string PropertyPrefix = "prop:";

        public static readonly IReadOnlyList<(string Id, string Header)> All = new[]
        {
            (FileName, "Имя файла"),
            (Thickness, "Толщина, мм"),
            (FlatSize, "Габариты, мм"),
            (Quantity, "Количество"),
            (Flat, "Развертка"),
            (Thumbnail, "Миниатюра"),
            (PartNumber, "Обозначение"),
            (Description, "Наименование"),
            (Material, "Материал"),
            (FlatState, "Наличие развертки"),
            (Total, "Итого на изделия"),
            (Status, "Статус выгрузки"),
            (FilePath, "Путь к файлу")
        };

        /// <summary>Колонки с картинками — для них строки делаются выше.</summary>
        public static bool IsImage(string id) => id == Flat || id == Thumbnail;
    }
}

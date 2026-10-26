using System.Collections.Generic;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>Настройки окна листовых деталей: колонки, размеры панелей. Сохраняются между запусками.</summary>
    public sealed class SheetMetalViewSettings
    {
        /// <summary>Видимые колонки списка деталей в порядке отображения. Пользовательские свойства — "prop:Имя".</summary>
        public List<string> Columns { get; set; } = new List<string>
        {
            PartColumns.Flat,
            PartColumns.Thumbnail,
            PartColumns.FileName,
            PartColumns.PartNumber,
            PartColumns.Description,
            PartColumns.Quantity,
            PartColumns.Material,
            PartColumns.Thickness,
            PartColumns.FlatSize,
            PartColumns.Status
        };

        public bool ShowDetails { get; set; } = true;
        public double TreeWidth { get; set; } = 420;
        public double DetailsWidth { get; set; } = 300;
        public double WindowWidth { get; set; } = 1400;
        public double WindowHeight { get; set; } = 800;
        public bool Maximized { get; set; }
    }
}

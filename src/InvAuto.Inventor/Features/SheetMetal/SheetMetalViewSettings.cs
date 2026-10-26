using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Features.SheetMetal
{
    /// <summary>Настройки окна листовых деталей: колонки, размеры панелей. Сохраняются между запусками.</summary>
    public sealed class SheetMetalViewSettings
    {
        /// <summary>Меняется, когда меняется набор колонок по умолчанию: старые сохранённые наборы сбрасываются.</summary>
        public const int CurrentColumnsVersion = 2;

        public static IReadOnlyList<string> DefaultColumns { get; } = new[]
        {
            PartColumns.FileName,
            PartColumns.Thickness,
            PartColumns.FlatSize,
            PartColumns.Quantity,
            PartColumns.Flat,
            PartColumns.Thumbnail
        };

        /// <summary>
        /// Видимые колонки списка деталей в порядке отображения. Пользовательские свойства — "prop:Имя".
        /// По умолчанию пустой: XmlSerializer при чтении дописывает элементы в существующий список,
        /// поэтому значения по умолчанию подставляет Normalize, а не инициализатор.
        /// </summary>
        public List<string> Columns { get; set; } = new List<string>();

        public int ColumnsVersion { get; set; }

        public bool ShowDetails { get; set; } = true;
        public double TreeWidth { get; set; } = 420;
        public double DetailsWidth { get; set; } = 300;
        public double WindowWidth { get; set; } = 1400;
        public double WindowHeight { get; set; } = 800;
        public bool Maximized { get; set; }

        /// <summary>Убирает повторы и подставляет колонки по умолчанию для новых и устаревших настроек.</summary>
        public void Normalize()
        {
            if (Columns == null || Columns.Count == 0 || ColumnsVersion < CurrentColumnsVersion)
                ResetColumns();
            else
                Columns = Columns.Distinct(StringComparer.Ordinal).ToList();
        }

        public void ResetColumns()
        {
            Columns = DefaultColumns.ToList();
            ColumnsVersion = CurrentColumnsVersion;
        }
    }
}

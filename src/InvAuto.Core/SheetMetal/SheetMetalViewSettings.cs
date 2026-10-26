using System;
using System.Collections.Generic;
using System.Linq;

namespace InvAuto.Core.SheetMetal
{
    /// <summary>Настройки окна листовых деталей: колонки, размеры панелей. Сохраняются между запусками.</summary>
    public sealed class SheetMetalViewSettings
    {
        /// <summary>Меняется, когда меняется набор колонок по умолчанию: старые сохранённые наборы сбрасываются.</summary>
        public const int CurrentColumnsVersion = 4;

        public static IReadOnlyList<string> DefaultColumns { get; } = new[]
        {
            PartColumns.FileName,
            PartColumns.Check,
            PartColumns.Thickness,
            PartColumns.FlatSize,
            PartColumns.Quantity,
            PartColumns.Flat,
            PartColumns.Thumbnail,
            PartColumns.Bends
        };

        /// <summary>
        /// Видимые колонки списка деталей в порядке отображения. Пользовательские свойства — "prop:Имя".
        /// По умолчанию пустой: XmlSerializer при чтении дописывает элементы в существующий список,
        /// поэтому значения по умолчанию подставляет Normalize, а не инициализатор.
        /// </summary>
        public List<string> Columns { get; set; } = new List<string>();

        public int ColumnsVersion { get; set; }

        /// <summary>Колонка сортировки списка деталей (Id из PartColumns, "prop:Имя" или "Checked"); пусто — порядок добавления.</summary>
        public string SortColumn { get; set; } = PartColumns.FileName;

        public bool SortDescending { get; set; }

        public bool ShowDetails { get; set; } = true;

        /// <summary>Свёрнутые группы таблицы свойств детали («Линии гиба», «Свойства iProperties»…).</summary>
        public List<string> CollapsedDetails { get; set; } = new List<string>();

        /// <summary>Открытая вкладка ленты окна (0 — «Главная»).</summary>
        public int RibbonTab { get; set; }
        public double TreeWidth { get; set; } = 420;
        public double DetailsWidth { get; set; } = 300;
        public double WindowWidth { get; set; } = 1600;
        public double WindowHeight { get; set; } = 860;

        /// <summary>Версия раскладки окна: при её смене размеры по умолчанию обновляются у сохранённых настроек.</summary>
        public int LayoutVersion { get; set; }

        /// <summary>С версии 2 лента шире: окно меньше 1600 px растягивается до нового размера по умолчанию.</summary>
        public const int CurrentLayoutVersion = 2;
        public bool Maximized { get; set; }

        /// <summary>Убирает повторы и подставляет колонки по умолчанию для новых и устаревших настроек.</summary>
        public void Normalize()
        {
            if (Columns == null || Columns.Count == 0 || ColumnsVersion < 3)
                ResetColumns();
            else
                Columns = Columns.Distinct(StringComparer.Ordinal).ToList();

            // Версия 4: колонка «Проверка» — добавляется после имени файла, остальной набор пользователя не трогаем.
            if (ColumnsVersion < 4)
            {
                if (!Columns.Contains(PartColumns.Check))
                {
                    int at = Columns.IndexOf(PartColumns.FileName);
                    Columns.Insert(at < 0 ? 0 : at + 1, PartColumns.Check);
                }
                ColumnsVersion = CurrentColumnsVersion;
            }

            if (LayoutVersion < CurrentLayoutVersion)
            {
                WindowWidth = Math.Max(WindowWidth, 1600);
                WindowHeight = Math.Max(WindowHeight, 860);
                LayoutVersion = CurrentLayoutVersion;
            }
        }

        public void ResetColumns()
        {
            Columns = DefaultColumns.ToList();
            ColumnsVersion = CurrentColumnsVersion;
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace InvAuto.Core.Materials
{
    /// <summary>Материал листа: наименование (по ГОСТ или другому стандарту), толщина и плотность.</summary>
    public sealed class Material
    {
        /// <summary>Постоянный идентификатор: на него ссылаются назначения деталям, имя можно менять.</summary>
        public string Id { get; set; } = Guid.NewGuid().ToString("N");

        public string Name { get; set; } = string.Empty;

        /// <summary>Толщина листа, мм.</summary>
        public double Thickness { get; set; }

        /// <summary>Плотность, кг/м³ (сталь — 7850).</summary>
        public double Density { get; set; }

        /// <summary>Материал по умолчанию для своей толщины: его получают детали, которым материал не назначен вручную.</summary>
        public bool IsDefault { get; set; }

        public Material Clone() => (Material)MemberwiseClone();

        public string Title => string.Format(CultureInfo.CurrentCulture, "{0} · {1:0.##} мм", Name, Thickness);

        public override string ToString() => Title;
    }

    /// <summary>
    /// Библиотека материалов. Хранится отдельно от настроек и кэша (см. MaterialLibraryStore) и никогда
    /// не очищается автоматически. Правила:
    /// — по умолчанию для толщины — не больше одного материала;
    /// — толщины сравниваются с точностью 0,01 мм.
    /// </summary>
    public sealed class MaterialLibrary
    {
        public const double SteelDensity = 7850;
        public const double ThicknessTolerance = 0.005;

        /// <summary>Толщины стартовой библиотеки, мм.</summary>
        public static readonly IReadOnlyList<double> StarterThicknesses = new[] { 0.5, 1.0, 1.5, 2.0, 3.0, 5.0 };

        /// <summary>Пустой по умолчанию: XmlSerializer дописывает прочитанное в существующий список.</summary>
        public List<Material> Materials { get; set; } = new List<Material>();

        /// <summary>Стартовая библиотека: сталь 7850 кг/м³ на каждую толщину, все — по умолчанию для своей толщины.</summary>
        public static MaterialLibrary CreateStarter() => new MaterialLibrary
        {
            Materials = StarterThicknesses
                .Select(t => new Material { Name = "Сталь", Thickness = t, Density = SteelDensity, IsDefault = true })
                .ToList()
        };

        public static bool SameThickness(double a, double b) => Math.Abs(a - b) < ThicknessTolerance;

        public Material Find(string id) =>
            string.IsNullOrEmpty(id) ? null : Materials.FirstOrDefault(m => m.Id == id);

        /// <summary>Материал по умолчанию для толщины: отмеченный, а если такой толщины ровно один материал — он.</summary>
        public Material DefaultFor(double thickness)
        {
            var same = Materials.Where(m => SameThickness(m.Thickness, thickness)).ToList();
            return same.FirstOrDefault(m => m.IsDefault) ?? (same.Count == 1 ? same[0] : null);
        }

        /// <summary>Делает материал единственным по умолчанию для его толщины.</summary>
        public void SetDefault(Material material)
        {
            foreach (var m in Materials.Where(m => SameThickness(m.Thickness, material.Thickness)))
                m.IsDefault = ReferenceEquals(m, material);
        }

        /// <summary>
        /// Приводит библиотеку в порядок после правки: по умолчанию для толщины — только первый отмеченный.
        /// </summary>
        public void Normalize()
        {
            foreach (var group in Materials.GroupBy(m => Math.Round(m.Thickness, 2)))
            {
                bool seen = false;
                foreach (var m in group.Where(m => m.IsDefault))
                {
                    if (seen)
                        m.IsDefault = false;
                    seen = true;
                }
            }
            foreach (var m in Materials.Where(m => string.IsNullOrEmpty(m.Id)))
                m.Id = Guid.NewGuid().ToString("N");
        }

        /// <summary>Ошибки, из-за которых библиотеку нельзя сохранить (пустое наименование, неверные числа).</summary>
        public IReadOnlyList<string> Validate()
        {
            var errors = new List<string>();
            for (int i = 0; i < Materials.Count; i++)
            {
                var m = Materials[i];
                string row = $"Строка {i + 1}";
                if (string.IsNullOrWhiteSpace(m.Name))
                    errors.Add($"{row}: не задано наименование");
                if (!(m.Thickness > 0 && m.Thickness <= 200))
                    errors.Add($"{row}: толщина должна быть от 0 до 200 мм");
                if (!(m.Density >= 100 && m.Density <= 25000))
                    errors.Add($"{row}: плотность должна быть от 100 до 25 000 кг/м³");
            }

            foreach (var dup in Materials.GroupBy(m => (m.Name.Trim().ToUpperInvariant(), Math.Round(m.Thickness, 2))).Where(g => g.Count() > 1))
                errors.Add($"Повторяется материал «{dup.First().Name.Trim()}» толщиной {dup.Key.Item2:0.##} мм");
            return errors;
        }

        /// <summary>
        /// Добавляет материалы из другой библиотеки (импорт). Совпадающие по наименованию и толщине обновляют
        /// плотность, новые добавляются. Ничего не удаляется. Возвращает (добавлено, обновлено).
        /// </summary>
        public (int Added, int Updated) Merge(MaterialLibrary other)
        {
            int added = 0, updated = 0;
            foreach (var incoming in other.Materials)
            {
                var existing = Materials.FirstOrDefault(m =>
                    string.Equals(m.Name.Trim(), incoming.Name.Trim(), StringComparison.OrdinalIgnoreCase) &&
                    SameThickness(m.Thickness, incoming.Thickness));
                if (existing != null)
                {
                    if (Math.Abs(existing.Density - incoming.Density) > 1e-9)
                    {
                        existing.Density = incoming.Density;
                        updated++;
                    }
                    continue;
                }

                var copy = incoming.Clone();
                if (Materials.Any(m => m.Id == copy.Id))
                    copy.Id = Guid.NewGuid().ToString("N");
                // Не перебиваем выбор «по умолчанию», уже сделанный в этой библиотеке.
                if (copy.IsDefault && Materials.Any(m => m.IsDefault && SameThickness(m.Thickness, copy.Thickness)))
                    copy.IsDefault = false;
                Materials.Add(copy);
                added++;
            }
            return (added, updated);
        }

        public MaterialLibrary Clone() => new MaterialLibrary { Materials = Materials.Select(m => m.Clone()).ToList() };
    }

    /// <summary>Материалы, назначенные деталям вручную: путь детали → Id материала.</summary>
    public sealed class MaterialAssignments
    {
        public const string Key = "SheetMetal.MaterialAssignments";

        public List<MaterialAssignment> Parts { get; set; } = new List<MaterialAssignment>();

        public string For(string partPath) =>
            Parts.FirstOrDefault(p => string.Equals(p.PartPath, partPath, StringComparison.OrdinalIgnoreCase))?.MaterialId;

        /// <summary>Назначает материал; null — вернуть материал по толщине.</summary>
        public void Set(string partPath, string materialId)
        {
            Parts.RemoveAll(p => string.Equals(p.PartPath, partPath, StringComparison.OrdinalIgnoreCase));
            if (!string.IsNullOrEmpty(materialId))
                Parts.Add(new MaterialAssignment { PartPath = partPath, MaterialId = materialId });
        }
    }

    public sealed class MaterialAssignment
    {
        public string PartPath { get; set; } = string.Empty;
        public string MaterialId { get; set; } = string.Empty;
    }

    /// <summary>Откуда у детали материал.</summary>
    public enum MaterialSource
    {
        /// <summary>В библиотеке нет материала для этой толщины.</summary>
        None,

        /// <summary>По умолчанию для толщины детали.</summary>
        ByThickness,

        /// <summary>Назначен вручную.</summary>
        Manual
    }

    public readonly struct ResolvedMaterial
    {
        public ResolvedMaterial(Material material, MaterialSource source, bool thicknessMismatch)
        {
            Material = material;
            Source = source;
            ThicknessMismatch = thicknessMismatch;
        }

        public Material Material { get; }
        public MaterialSource Source { get; }

        /// <summary>Назначенный вручную материал другой толщины, чем деталь.</summary>
        public bool ThicknessMismatch { get; }

        /// <summary>
        /// Материал детали: назначенный вручную (если он ещё есть в библиотеке), иначе — по умолчанию для толщины.
        /// </summary>
        public static ResolvedMaterial Resolve(MaterialLibrary library, MaterialAssignments assignments, string partPath, double thickness)
        {
            var manual = library.Find(assignments?.For(partPath));
            if (manual != null)
                return new ResolvedMaterial(manual, MaterialSource.Manual, !MaterialLibrary.SameThickness(manual.Thickness, thickness));
            var byThickness = library.DefaultFor(thickness);
            return byThickness != null
                ? new ResolvedMaterial(byThickness, MaterialSource.ByThickness, false)
                : new ResolvedMaterial(null, MaterialSource.None, false);
        }
    }
}

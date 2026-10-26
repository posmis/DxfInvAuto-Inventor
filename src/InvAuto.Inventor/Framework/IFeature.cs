using System;

namespace InvAuto.Framework
{
    /// <summary>В каких средах Inventor показывать кнопку функции.</summary>
    [Flags]
    public enum RibbonTargets
    {
        None = 0,
        ZeroDoc = 1,
        Part = 2,
        Assembly = 4,
        Drawing = 8,
        All = ZeroDoc | Part | Assembly | Drawing
    }

    /// <summary>
    /// Одна функция плагина = одна кнопка на вкладке InvAuto.
    /// Чтобы добавить функцию, достаточно создать класс, унаследованный от FeatureBase:
    /// RibbonBuilder найдёт его автоматически (см. README).
    /// </summary>
    public interface IFeature
    {
        /// <summary>Уникальный идентификатор, латиницей без пробелов. Менять нельзя: на нём держится настройка ленты пользователя.</summary>
        string Id { get; }

        string DisplayName { get; }
        string Description { get; }

        /// <summary>Панель на вкладке InvAuto, в которую попадёт кнопка.</summary>
        string Panel { get; }

        RibbonTargets Targets { get; }

        /// <summary>Порядок кнопки внутри панели.</summary>
        int Order { get; }

        void Execute(FeatureContext context);
    }

    /// <summary>Базовый класс с разумными значениями по умолчанию.</summary>
    public abstract class FeatureBase : IFeature
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }
        public virtual string Description => DisplayName;
        public virtual string Panel => "Общее";
        public virtual RibbonTargets Targets => RibbonTargets.Part | RibbonTargets.Assembly;
        public virtual int Order => 100;
        public abstract void Execute(FeatureContext context);
    }
}

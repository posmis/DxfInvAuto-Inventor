using System.Globalization;
using System.IO;
using System.Linq;
using InvAuto.Core.Logging;
using InvAuto.Core.Settings;
using InvAuto.Core.SheetMetal;
using InvAuto.Core.Sorting;
using InvAuto.Core.Structure;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>Список деталей, собранный пользователем, и сортировка как в проводнике.</summary>
    public class WorkListAndSortingTests
    {
        private static readonly NaturalComparer Natural = new NaturalComparer(new CultureInfo("ru-RU"));

        [Fact]
        public void WorkList_AddsInOrder_WithoutDuplicates()
        {
            var list = new WorkList();
            var first = list.Add(new[] { @"C:\A.ipt", @"C:\B.ipt" });
            var second = list.Add(new[] { @"c:\a.IPT", @"C:\C.ipt", @"C:\C.ipt", "", null });

            Assert.Equal(new[] { @"C:\A.ipt", @"C:\B.ipt" }, first.Added);
            Assert.Equal(new[] { @"C:\C.ipt" }, second.Added);
            Assert.Equal(1, second.AlreadyListed); // повтор внутри одного добавления не считается «уже в списке»
            Assert.Equal(new[] { @"C:\A.ipt", @"C:\B.ipt", @"C:\C.ipt" }, list.Items);
            Assert.True(list.Contains(@"C:\b.ipt"));
        }

        [Fact]
        public void WorkList_RemoveClearAndRetain()
        {
            var list = new WorkList();
            list.Add(new[] { "A", "B", "C", "D" });

            Assert.Equal(2, list.Remove(new[] { "b", "X", "D" }));
            Assert.Equal(new[] { "A", "C" }, list.Items);

            Assert.Equal(1, list.Retain(p => p == "C")); // после обновления состава детали A больше нет
            Assert.Equal(new[] { "C" }, list.Items);

            list.Clear();
            Assert.Equal(0, list.Count);
            Assert.Single(list.Add(new[] { "C" }).Added); // после очистки можно добавить снова
        }

        [Fact]
        public void Natural_OrdersNumbersByValue()
        {
            var names = new[] { "Деталь 10", "деталь 2", "Деталь 1", "Деталь 02a", "Ребро", "Деталь", "Лист 100", "Лист 9" };
            var sorted = names.OrderBy(n => n, Natural).ToArray();

            Assert.Equal(new[] { "Деталь", "Деталь 1", "деталь 2", "Деталь 02a", "Деталь 10", "Лист 9", "Лист 100", "Ребро" }, sorted);
        }

        [Fact]
        public void Natural_HandlesLongNumbersAndDesignations()
        {
            Assert.True(Natural.Compare("АБВГ.301318.012", "АБВГ.301318.0101") < 0);
            Assert.True(Natural.Compare("x99999999999999999999", "x100000000000000000000") < 0); // больше long
            Assert.True(Natural.Compare("A1", "a1") != 0);  // устойчивый порядок для равных по-проводниковски
            Assert.Equal(0, Natural.Compare("A1", "A1"));
            Assert.True(Natural.Compare(null, "a") < 0);
        }

        [Fact]
        public void SortValues_NumbersBoolsAndEmptyAtEnd()
        {
            Assert.True(SortValues.Compare(2.5, 10, false) < 0);
            Assert.True(SortValues.Compare(2.5, 10, true) > 0);
            Assert.True(SortValues.Compare(false, true, false) < 0);
            Assert.True(SortValues.Compare("Лист 9", "Лист 10", false) < 0);

            // Пустые — в конце при любом направлении, как в проводнике.
            Assert.True(SortValues.Compare("", "А", false) > 0);
            Assert.True(SortValues.Compare("", "А", true) > 0);
            Assert.True(SortValues.Compare(null, 1, true) > 0);
            Assert.Equal(0, SortValues.Compare(null, " ", false));
        }

        [Fact]
        public void SavedLists_ArePerAssembly_AndEmptyListIsForgotten()
        {
            var lists = new SavedPartLists();
            var t = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            lists.Set(@"C:\Сборка.iam", new[] { "A", "B", "a" }, t);
            lists.Set(@"C:\Другая.iam", new[] { "C" }, t);

            Assert.Equal(new[] { "A", "B" }, lists.Get(@"c:\сборка.IAM"));
            Assert.Equal(new[] { "C" }, lists.Get(@"C:\Другая.iam"));
            Assert.Empty(lists.Get(@"C:\Нет.iam"));
            Assert.Empty(lists.Get(null));

            lists.Set(@"C:\Сборка.iam", new[] { "D" }, t.AddMinutes(1)); // замена, а не вторая запись
            Assert.Equal(new[] { "D" }, lists.Get(@"C:\Сборка.iam"));
            Assert.Equal(2, lists.Lists.Count);

            lists.Set(@"C:\Сборка.iam", new string[0], t.AddMinutes(2));
            Assert.Single(lists.Lists);
            lists.Set("", new[] { "X" }, t); // несохранённая сборка — не запоминаем
            Assert.Single(lists.Lists);
        }

        [Fact]
        public void SavedLists_KeepNewest_AndSurviveXml()
        {
            var lists = new SavedPartLists();
            var t = new System.DateTime(2026, 1, 1, 0, 0, 0, System.DateTimeKind.Utc);
            for (int i = 0; i < SavedPartLists.MaxAssemblies + 5; i++)
                lists.Set($@"C:\{i}.iam", new[] { "P" + i }, t.AddMinutes(i));

            Assert.Equal(SavedPartLists.MaxAssemblies, lists.Lists.Count);
            Assert.Empty(lists.Get(@"C:\0.iam"));     // самые старые забыты
            Assert.NotEmpty(lists.Get($@"C:\{SavedPartLists.MaxAssemblies + 4}.iam"));

            string dir = Path.Combine(Path.GetTempPath(), "InvAutoLists_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new XmlSettingsStore(dir, new FileLogger(dir));
                store.Save(SavedPartLists.Key, lists);
                var loaded = store.Load<SavedPartLists>(SavedPartLists.Key);
                store.Save(SavedPartLists.Key, loaded);
                loaded = store.Load<SavedPartLists>(SavedPartLists.Key);
                Assert.Equal(SavedPartLists.MaxAssemblies, loaded.Lists.Count); // без задвоения при чтении
                Assert.Equal(new[] { "P10" }, loaded.Get(@"C:\10.iam"));

                Assert.False(store.Load<AppSettings>(AppSettings.Key).RememberPartLists); // по умолчанию выключено
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void ViewSettings_OldWindowSizeGrowsOnce_ForWiderRibbon()
        {
            var old = new SheetMetalViewSettings { WindowWidth = 1400, WindowHeight = 800, LayoutVersion = 0 };
            old.Normalize();
            Assert.Equal(1600, old.WindowWidth);
            Assert.Equal(860, old.WindowHeight);

            // Пользователь потом сам сделал окно уже — больше не трогаем.
            old.WindowWidth = 1450;
            old.Normalize();
            Assert.Equal(1450, old.WindowWidth);

            var big = new SheetMetalViewSettings { WindowWidth = 1900, LayoutVersion = 0 };
            big.Normalize();
            Assert.Equal(1900, big.WindowWidth);
        }

        [Fact]
        public void ViewSettings_SortSurvivesSave()
        {
            string dir = Path.Combine(Path.GetTempPath(), "InvAutoSort_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new XmlSettingsStore(dir, new FileLogger(dir));
                Assert.Equal(PartColumns.FileName, store.Load<SheetMetalViewSettings>("v").SortColumn);

                store.Save("v", new SheetMetalViewSettings { SortColumn = PartColumns.Thickness, SortDescending = true });
                var loaded = store.Load<SheetMetalViewSettings>("v");
                Assert.Equal(PartColumns.Thickness, loaded.SortColumn);
                Assert.True(loaded.SortDescending);

                store.Save("v", new SheetMetalViewSettings { SortColumn = "" });
                Assert.Equal("", store.Load<SheetMetalViewSettings>("v").SortColumn); // «порядок добавления» сохраняется
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }
    }
}

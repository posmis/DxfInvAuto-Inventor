using System.Collections.Generic;
using System.IO;
using System.Linq;
using InvAuto.Core.Logging;
using InvAuto.Core.Settings;
using InvAuto.Core.SheetMetal;
using InvAuto.Core.Structure;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class FlatTreeTests
    {
        private sealed class Item : IFlatTreeItem<Item>
        {
            private readonly List<Item> _children = new List<Item>();

            public Item(string name, int level)
            {
                Name = name;
                Level = level;
            }

            public string Name { get; }
            public int Level { get; }
            public bool IsExpanded { get; set; }
            public bool HasChildren => _children.Count > 0;
            public IReadOnlyList<Item> Children => _children;

            public Item Add(string name)
            {
                var child = new Item(name, Level + 1);
                _children.Add(child);
                return child;
            }
        }

        // Рама
        // ├ Стойка
        // │ ├ Пластина
        // │ └ Узел
        // │   └ Косынка
        // ├ Ригель
        // │ └ Полка
        // └ Болт
        private static (FlatTree<Item> Tree, Item Root, Item Post, Item Node, Item Beam) Build(int expandLevels)
        {
            var root = new Item("Рама", 0);
            var post = root.Add("Стойка");
            post.Add("Пластина");
            var node = post.Add("Узел");
            node.Add("Косынка");
            var beam = root.Add("Ригель");
            beam.Add("Полка");
            root.Add("Болт");
            var tree = new FlatTree<Item>();
            tree.Load(root, expandLevels);
            return (tree, root, post, node, beam);
        }

        private static string Visible(FlatTree<Item> tree) => string.Join(",", tree.Rows.Select(r => r.Name));

        [Fact]
        public void Load_ExpandsOnlyRequestedLevels()
        {
            Assert.Equal("Рама", Visible(Build(0).Tree));
            Assert.Equal("Рама,Стойка,Ригель,Болт", Visible(Build(1).Tree));
            Assert.Equal("Рама,Стойка,Пластина,Узел,Ригель,Полка,Болт", Visible(Build(2).Tree));
        }

        [Fact]
        public void Toggle_InsertsAndRemovesDescendants_InPlace()
        {
            var (tree, _, post, node, _) = Build(1);

            tree.Toggle(post);
            Assert.Equal("Рама,Стойка,Пластина,Узел,Ригель,Болт", Visible(tree));

            tree.Toggle(node);
            Assert.Equal("Рама,Стойка,Пластина,Узел,Косынка,Ригель,Болт", Visible(tree));

            tree.Toggle(post);
            Assert.Equal("Рама,Стойка,Ригель,Болт", Visible(tree));
            Assert.True(node.IsExpanded); // состояние вложенного узла запомнено

            tree.Toggle(post);
            Assert.Equal("Рама,Стойка,Пластина,Узел,Косынка,Ригель,Болт", Visible(tree));
        }

        [Fact]
        public void ExpandAll_ThenCollapseAll()
        {
            var (tree, root, _, node, _) = Build(1);

            tree.ExpandAll();
            Assert.Equal("Рама,Стойка,Пластина,Узел,Косынка,Ригель,Полка,Болт", Visible(tree));

            tree.CollapseAll();
            Assert.Equal("Рама,Стойка,Ригель,Болт", Visible(tree));
            Assert.True(root.IsExpanded);
            Assert.False(node.IsExpanded);
        }

        [Fact]
        public void ExpandingHiddenNode_DoesNotCorruptList()
        {
            var (tree, _, _, node, _) = Build(1);
            tree.Expand(node); // узел ещё не виден
            Assert.Equal("Рама,Стойка,Ригель,Болт", Visible(tree));
        }

        [Fact]
        public void LeafToggle_DoesNothing()
        {
            var (tree, root, _, _, _) = Build(1);
            var bolt = root.Children.Last();
            tree.Toggle(bolt);
            Assert.False(bolt.IsExpanded);
            Assert.Equal("Рама,Стойка,Ригель,Болт", Visible(tree));
        }
    }

    public class SheetMetalViewSettingsTests
    {
        [Fact]
        public void New_GetsDefaultColumns()
        {
            var settings = new SheetMetalViewSettings();
            settings.Normalize();
            Assert.Equal(SheetMetalViewSettings.DefaultColumns, settings.Columns);
        }

        [Fact]
        public void Duplicates_AreRemoved_OrderKept()
        {
            var settings = new SheetMetalViewSettings
            {
                ColumnsVersion = SheetMetalViewSettings.CurrentColumnsVersion,
                Columns = { PartColumns.FileName, PartColumns.Flat, PartColumns.FileName, "prop:Заказ", PartColumns.Flat }
            };
            settings.Normalize();
            Assert.Equal(new[] { PartColumns.FileName, PartColumns.Flat, "prop:Заказ" }, settings.Columns);
        }

        [Fact]
        public void OldVersion_IsResetToDefaults()
        {
            var settings = new SheetMetalViewSettings { ColumnsVersion = 1, Columns = { PartColumns.Status } };
            settings.Normalize();
            Assert.Equal(SheetMetalViewSettings.DefaultColumns, settings.Columns);
            Assert.Equal(SheetMetalViewSettings.CurrentColumnsVersion, settings.ColumnsVersion);
        }

        /// <summary>Регрессия: XmlSerializer дописывал сохранённые колонки к колонкам по умолчанию.</summary>
        [Fact]
        public void SaveLoad_ManyTimes_DoesNotDuplicateColumns()
        {
            string dir = Path.Combine(Path.GetTempPath(), "InvAutoView_" + System.Guid.NewGuid().ToString("N"));
            try
            {
                var store = new XmlSettingsStore(dir, new FileLogger(dir));
                var settings = new SheetMetalViewSettings();
                settings.Normalize();
                settings.Columns.Add("prop:Заказ");

                for (int i = 0; i < 5; i++)
                {
                    store.Save("view", settings);
                    settings = store.Load<SheetMetalViewSettings>("view");
                    settings.Normalize();
                }

                Assert.Equal(SheetMetalViewSettings.DefaultColumns.Concat(new[] { "prop:Заказ" }), settings.Columns);
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }

        [Fact]
        public void EveryDefaultColumn_IsInCatalog_WithHeader()
        {
            foreach (string id in SheetMetalViewSettings.DefaultColumns)
                Assert.Contains(PartColumns.All, c => c.Id == id && c.Header.Length > 0);
            Assert.Equal(PartColumns.All.Count, PartColumns.All.Select(c => c.Id).Distinct().Count());
            Assert.True(PartColumns.IsImage(PartColumns.Flat) && PartColumns.IsImage(PartColumns.Thumbnail));
            Assert.False(PartColumns.IsImage(PartColumns.FileName));
        }
    }
}

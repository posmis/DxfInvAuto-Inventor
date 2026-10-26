using System.Linq;
using InvAuto.Core.Export;
using InvAuto.Core.Structure;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class AssemblyNodeTests
    {
        private static AssemblyNode Sheet(AssemblyNode parent, string file, int qty)
        {
            var node = new AssemblyNode(file, NodeKind.SheetMetal, qty, parent)
            {
                SheetMetal = new SheetMetalPart { FullFileName = file, Name = file, Quantity = 1 }
            };
            parent.Children.Add(node);
            return node;
        }

        private static AssemblyNode Assembly(AssemblyNode parent, string file, int qty)
        {
            var node = new AssemblyNode(file, NodeKind.Assembly, qty, parent);
            parent?.Children.Add(node);
            return node;
        }

        [Fact]
        public void Quantities_MultiplyThroughSubassemblies_AndSumAcrossBranches()
        {
            // Рама: 2 x Стойка (в каждой 3 x Пластина), плюс 1 x Пластина и 4 x Косынка напрямую.
            var root = Assembly(null, "Рама.iam", 1);
            var post = Assembly(root, "Стойка.iam", 2);
            Sheet(post, "Пластина.ipt", 3);
            Sheet(root, "Пластина.ipt", 1);
            Sheet(root, "Косынка.ipt", 4);
            root.Children.Add(new AssemblyNode("Болт.ipt", NodeKind.Part, 8, root));

            var all = root.CollectSheetMetal();
            Assert.Equal(2, all.Count);
            Assert.Equal(7, all.Single(p => p.Name == "Пластина.ipt").Quantity);
            Assert.Equal(4, all.Single(p => p.Name == "Косынка.ipt").Quantity);

            // Выбрана только подсборка: количество всё равно на изделие (2 стойки x 3).
            var onlyPost = post.CollectSheetMetal();
            Assert.Equal(6, onlyPost.Single().Quantity);
        }

        [Fact]
        public void Collect_DoesNotChangeSharedPartData()
        {
            var root = Assembly(null, "A.iam", 1);
            var node = Sheet(root, "P.ipt", 5);
            root.CollectSheetMetal().Single().CustomProperties["x"] = "y";

            Assert.Equal(1, node.SheetMetal.Quantity);
            Assert.Empty(node.SheetMetal.CustomProperties);
        }
    }
}

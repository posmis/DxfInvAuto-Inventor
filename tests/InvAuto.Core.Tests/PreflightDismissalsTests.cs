using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using InvAuto.Core.Logging;
using InvAuto.Core.Preflight;
using InvAuto.Core.Settings;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>Пропуск замечаний проверки для детали и сброс при изменении детали.</summary>
    public class PreflightDismissalsTests
    {
        private const string Part = @"C:\Детали\Рамка.ipt";

        private static List<PreflightIssue> Issues() => new List<PreflightIssue>
        {
            new PreflightIssue(IssueSeverity.Warning, "MarkingCenterCut", "Центр детали вырезан — проверьте место маркировки"),
            new PreflightIssue(IssueSeverity.Warning, "SmallHole", "Отверстия < 2 мм: 1 шт."),
            new PreflightIssue(IssueSeverity.Error, "OpenContour", "Разрыв контура: 1 шт.")
        };

        [Fact]
        public void Dismissed_DoesNotCount_ErrorsCannotBeDismissed()
        {
            var d = new PreflightDismissals();
            d.Dismiss(Part, "v1", "MarkingCenterCut");
            d.Dismiss(Part, "v1", "OpenContour");
            var issues = d.Apply(Part, "v1", Issues());

            Assert.True(issues.Single(i => i.Code == "MarkingCenterCut").Dismissed);
            Assert.False(issues.Single(i => i.Code == "SmallHole").Dismissed);
            Assert.False(issues.Single(i => i.Code == "OpenContour").Dismissed); // ошибка — нельзя
            Assert.False(issues.Single(i => i.Code == "MarkingCenterCut").Counts);
            Assert.Contains("пропущено", issues.Single(i => i.Code == "MarkingCenterCut").ToString());
        }

        [Fact]
        public void Summary_IgnoresDismissedWarnings()
        {
            var d = new PreflightDismissals();
            var warnings = Issues().Where(i => i.Severity == IssueSeverity.Warning).ToList();
            var results = new Dictionary<string, IReadOnlyList<PreflightIssue>> { [Part] = warnings };
            Assert.Equal((0, 1), PreflightChecker.Summary(results));

            d.Dismiss(Part, "v1", "MarkingCenterCut");
            d.Dismiss(Part, "v1", "SmallHole");
            results[Part] = d.Apply(Part, "v1", warnings);
            Assert.Equal((0, 0), PreflightChecker.Summary(results));
        }

        [Fact]
        public void PartChanged_AllDismissalsReturn()
        {
            var d = new PreflightDismissals();
            d.Dismiss(Part, "v1", "MarkingCenterCut");
            Assert.All(d.Apply(Part, "v2", Issues()), i => Assert.False(i.Dismissed)); // другая версия — пропуски не действуют
            Assert.True(d.Refresh(Part, "v2"));
            Assert.Empty(d.Parts);
            Assert.False(d.Refresh(Part, "v2"));

            // Пропуск для новой версии заменяет старые.
            d.Dismiss(Part, "v1", "SmallHole");
            d.Dismiss(Part, "v3", "MarkingCenterCut");
            Assert.Equal(1, d.Count(Part));
        }

        [Fact]
        public void Restore_ReturnsIssue()
        {
            var d = new PreflightDismissals();
            d.Dismiss(Part, "v1", "MarkingCenterCut");
            d.Restore(Part.ToUpperInvariant(), "MarkingCenterCut");
            Assert.Empty(d.Parts);
            Assert.All(d.Apply(Part, "v1", Issues()), i => Assert.False(i.Dismissed));
        }

        [Fact]
        public void RoundTripThroughXml()
        {
            string dir = Path.Combine(Path.GetTempPath(), "invauto-dismiss-" + Guid.NewGuid().ToString("N"));
            try
            {
                var store = new XmlSettingsStore(dir, new FileLogger(Path.Combine(dir, "logs")));
                var d = new PreflightDismissals();
                d.Dismiss(Part, "638000000000000000", "MarkingCenterCut");
                store.Save(PreflightDismissals.Key, d);
                store.Save(PreflightDismissals.Key, store.Load<PreflightDismissals>(PreflightDismissals.Key));
                var loaded = store.Load<PreflightDismissals>(PreflightDismissals.Key);
                Assert.Equal(new[] { "MarkingCenterCut" }, loaded.Parts.Single().Codes);
                Assert.True(loaded.Apply(Part, "638000000000000000", Issues())[0].Dismissed);
            }
            finally
            {
                if (Directory.Exists(dir))
                    Directory.Delete(dir, true);
            }
        }
    }
}

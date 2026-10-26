using System;
using System.IO;
using System.Linq;
using InvAuto.Core.Logging;
using InvAuto.Core.Maintenance;
using InvAuto.Core.Settings;
using Xunit;

namespace InvAuto.Core.Tests
{
    /// <summary>Очистка кэша и журнала: удаляется только показанное, служебная папка проверяется, ссылки не обходятся.</summary>
    public class SafeCleanupTests : IDisposable
    {
        private const string Suffix = @"InvAuto\FlatCache";
        private readonly string _base = Path.Combine(Path.GetTempPath(), "InvAutoClean_" + Guid.NewGuid().ToString("N"));
        private readonly string _root;

        public SafeCleanupTests()
        {
            _root = Path.Combine(_base, "InvAuto", "FlatCache");
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            if (Directory.Exists(_base))
                Directory.Delete(_base, true);
        }

        private string Put(string relative, string text = "0\nEOF\n")
        {
            string path = Path.Combine(_root, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            File.WriteAllText(path, text);
            return path;
        }

        [Fact]
        public void ValidateRoot_RefusesForeignFolders()
        {
            Assert.Throws<InvalidOperationException>(() => SafeCleanup.ValidateRoot("", Suffix));
            Assert.Throws<InvalidOperationException>(() => SafeCleanup.ValidateRoot(Path.GetPathRoot(_root), Suffix));
            Assert.Throws<InvalidOperationException>(() => SafeCleanup.ValidateRoot(_base, Suffix));
            Assert.Throws<InvalidOperationException>(() => SafeCleanup.ValidateRoot(Path.Combine(_base, "InvAuto"), Suffix));
            Assert.Throws<InvalidOperationException>(() => SafeCleanup.ValidateRoot(Path.Combine(_base, "NotInvAuto", "FlatCache"), Suffix));
            Assert.Throws<InvalidOperationException>(() => SafeCleanup.ValidateRoot(Path.Combine(_root, ".."), Suffix)); // «..» раскрывается

            Assert.Equal(_root, SafeCleanup.ValidateRoot(_root + Path.DirectorySeparatorChar, Suffix));
            Assert.Equal(_root, SafeCleanup.ValidateRoot(_root, "InvAuto/FlatCache"));
        }

        [Fact]
        public void Plan_ListsOnlyAllowedTypes_WithReasonsForTheRest()
        {
            Put("a.dxf");
            Put(Path.Combine("sub", "b.DXF"));
            Put("notes.txt");

            var plan = SafeCleanup.Plan(_root, Suffix, "dxf");

            Assert.Equal(new[] { "a.dxf", Path.Combine("sub", "b.DXF") }, plan.Files.Select(f => f.RelativePath));
            Assert.Equal(12, plan.TotalBytes);
            Assert.Single(plan.Skipped);
            Assert.Contains("notes.txt", plan.Skipped[0]);
            Assert.Equal((2, 12L), SafeCleanup.Measure(_root, Suffix, ".dxf"));
        }

        [Fact]
        public void Plan_MissingFolder_IsEmpty_AndForeignFolder_MeasuresZero()
        {
            Directory.Delete(_root);
            Assert.Empty(SafeCleanup.Plan(_root, Suffix, "dxf").Files);
            Assert.Equal((0, 0L), SafeCleanup.Measure(_base, Suffix, "dxf"));
        }

        [Fact]
        public void Execute_DeletesPlannedFiles_KeepsRootAndForeignFiles_RemovesEmptySubfolders()
        {
            Put("a.dxf");
            Put(Path.Combine("sub", "deep", "b.dxf"));
            string keep = Put("keep.txt");

            var result = SafeCleanup.Execute(SafeCleanup.Plan(_root, Suffix, "dxf"));

            Assert.Equal(2, result.Deleted);
            Assert.Equal(12, result.FreedBytes);
            Assert.Empty(result.Failed);
            Assert.True(Directory.Exists(_root));
            Assert.True(File.Exists(keep));
            Assert.False(Directory.Exists(Path.Combine(_root, "sub")));
        }

        [Fact]
        public void Execute_SkipsFilesChangedAfterPlan_AndNewFiles()
        {
            string changed = Put("changed.dxf");
            Put("same.dxf");
            var plan = SafeCleanup.Plan(_root, Suffix, "dxf");

            File.WriteAllText(changed, "0\nSECTION\n0\nEOF\n");
            string added = Put("added.dxf");

            var result = SafeCleanup.Execute(plan);

            Assert.Equal(1, result.Deleted);
            Assert.True(File.Exists(changed));
            Assert.True(File.Exists(added)); // появился после показа списка — не в плане
            Assert.Contains(result.Failed, f => f.Path == "changed.dxf");
        }

        [Fact]
        public void Execute_AlreadyDeletedFile_IsNotAnError_AndReadOnlyIsDeleted()
        {
            string gone = Put("gone.dxf");
            string readOnly = Put("ro.dxf");
            File.SetAttributes(readOnly, FileAttributes.ReadOnly);
            var plan = SafeCleanup.Plan(_root, Suffix, "dxf");
            File.Delete(gone);

            var result = SafeCleanup.Execute(plan);

            Assert.Empty(result.Failed);
            Assert.Equal(1, result.Deleted);
            Assert.False(File.Exists(readOnly));
        }

        [Fact]
        public void Plan_DoesNotFollowFolderLinks()
        {
            string outside = Path.Combine(_base, "Важное");
            Directory.CreateDirectory(outside);
            string precious = Path.Combine(outside, "чертёж.dxf");
            File.WriteAllText(precious, "x");
            try
            {
                Directory.CreateSymbolicLink(Path.Combine(_root, "link"), outside);
            }
            catch (Exception)
            {
                return; // нет прав на ссылки (Windows без режима разработчика) — проверить нечем
            }

            var plan = SafeCleanup.Plan(_root, Suffix, "dxf");
            var result = SafeCleanup.Execute(plan);

            Assert.Empty(plan.Files);
            Assert.Contains(plan.Skipped, s => s.StartsWith("link"));
            Assert.Equal(0, result.Deleted);
            Assert.True(File.Exists(precious));
            Assert.True(Directory.Exists(Path.Combine(_root, "link")));
        }

        [Fact]
        public void Plan_RefusesLinkedRoot()
        {
            string real = Path.Combine(_base, "Real");
            Directory.CreateDirectory(real);
            string linked = Path.Combine(_base, "Other", "InvAuto", "FlatCache");
            Directory.CreateDirectory(Path.GetDirectoryName(linked));
            try
            {
                Directory.CreateSymbolicLink(linked, real);
            }
            catch (Exception)
            {
                return;
            }

            Assert.Throws<InvalidOperationException>(() => SafeCleanup.Plan(linked, Suffix, "dxf"));
        }

        [Fact]
        public void Except_MovesFileToSkipped()
        {
            Put("2026-10-25.log");
            string current = Put("2026-10-26.log");

            var plan = SafeCleanup.Plan(_root, Suffix, "log").Except(current, "текущий журнал");

            Assert.Single(plan.Files);
            Assert.Contains(plan.Skipped, s => s.Contains("2026-10-26.log") && s.Contains("текущий журнал"));
            Assert.Same(plan, plan.Except(Path.Combine(_root, "нет.log"), "—"));
        }

        [Theory]
        [InlineData(0, "0 Б")]
        [InlineData(1023, "1023 Б")]
        [InlineData(2048, "2 КБ")]
        [InlineData(5 * 1024 * 1024, "5 МБ")]
        public void FormatSize_UsesReadableUnits(long bytes, string expected)
        {
            Assert.Equal(expected, SafeCleanup.FormatSize(bytes).Replace(',', '.'));
        }

        [Fact]
        public void AppSettings_NormalizeClampsValues()
        {
            var s = new AppSettings { CacheMaxAgeDays = 0, LogMaxAgeDays = 100000 };
            s.Normalize();
            Assert.Equal(1, s.CacheMaxAgeDays);
            Assert.Equal(3650, s.LogMaxAgeDays);
            Assert.Equal(30, new AppSettings().CacheMaxAgeDays);
        }

        [Fact]
        public void SettingsReset_MovesFileToBackup_AndDefaultsComeBack()
        {
            var store = new XmlSettingsStore(_base, new FileLogger(Path.Combine(_base, "logs")));
            Assert.Null(store.Reset(AppSettings.Key));

            store.Save(AppSettings.Key, new AppSettings { CacheMaxAgeDays = 7 });
            Assert.True(store.Exists(AppSettings.Key));

            string backup = store.Reset(AppSettings.Key);
            string second;
            store.Save(AppSettings.Key, new AppSettings { CacheMaxAgeDays = 8 });
            second = store.Reset(AppSettings.Key); // в ту же секунду — имя не совпадает

            Assert.False(store.Exists(AppSettings.Key));
            Assert.Equal(30, store.Load<AppSettings>(AppSettings.Key).CacheMaxAgeDays);
            Assert.NotEqual(backup, second);
            Assert.Contains("<CacheMaxAgeDays>7<", File.ReadAllText(backup));
            Assert.Equal(Path.Combine(_base, "backup"), Path.GetDirectoryName(backup));
        }
    }
}

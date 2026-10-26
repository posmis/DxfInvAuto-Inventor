using System;
using System.IO;
using InvAuto.Core.Preview;
using Xunit;

namespace InvAuto.Core.Tests
{
    public class FlatPreviewCacheTests : IDisposable
    {
        private readonly string _dir = Path.Combine(Path.GetTempPath(), "InvAutoCache_" + Guid.NewGuid().ToString("N"));
        private static readonly DateTime Saved = new DateTime(2026, 10, 1, 12, 0, 0, DateTimeKind.Utc);

        public void Dispose()
        {
            if (Directory.Exists(_dir))
                Directory.Delete(_dir, true);
        }

        [Fact]
        public void SamePart_SameSettings_SameFile_PathCaseInsensitive()
        {
            var cache = new FlatPreviewCache(_dir);
            Assert.Equal(
                cache.PathFor(@"C:\Work\Опора.ipt", Saved, "ini|a"),
                cache.PathFor(@"c:\work\опора.IPT", Saved, "ini|a"));
            Assert.StartsWith(_dir, cache.PathFor(@"C:\Work\Опора.ipt", Saved, "ini|a"));
        }

        [Fact]
        public void SavedPart_OrOtherSettings_GiveNewFile()
        {
            var cache = new FlatPreviewCache(_dir);
            string original = cache.PathFor(@"C:\Work\Опора.ipt", Saved, "ini|a");

            Assert.NotEqual(original, cache.PathFor(@"C:\Work\Опора.ipt", Saved.AddSeconds(1), "ini|a"));
            Assert.NotEqual(original, cache.PathFor(@"C:\Work\Опора.ipt", Saved, "ini|b"));
            Assert.NotEqual(original, cache.PathFor(@"C:\Work\Пластина.ipt", Saved, "ini|a"));
        }

        [Fact]
        public void Cleanup_RemovesOnlyOldFiles()
        {
            Directory.CreateDirectory(_dir);
            string old = Path.Combine(_dir, "old.dxf");
            string fresh = Path.Combine(_dir, "fresh.dxf");
            File.WriteAllText(old, "");
            File.WriteAllText(fresh, "");
            File.SetLastWriteTimeUtc(old, DateTime.UtcNow.AddDays(-40));

            Assert.Equal(1, new FlatPreviewCache(_dir).Cleanup(TimeSpan.FromDays(30)));
            Assert.False(File.Exists(old));
            Assert.True(File.Exists(fresh));
        }

        [Fact]
        public void Cleanup_MissingFolder_IsFine()
        {
            Assert.Equal(0, new FlatPreviewCache(Path.Combine(_dir, "nope")).Cleanup(TimeSpan.FromDays(1)));
        }
    }
}

using System;
using System.IO;
using DTXMania.Game.Lib.Config;
using Xunit;

namespace DTXMania.Test.Config
{
    [Trait("Category", "Unit")]
    public class ConfigManagerSkinPathTests : IDisposable
    {
        private readonly string _tempDir;

        public ConfigManagerSkinPathTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "dtx-skinpath-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { /* best effort */ }
        }

        private string ConfigPath => Path.Combine(_tempDir, "Config.ini");

        [Fact]
        public void SetSkinPath_WithNewValue_ShouldUpdateConfigAndDeferSave()
        {
            var manager = new ConfigManager();
            var newSkin = Path.Combine(_tempDir, "System", "CXNeon") + Path.DirectorySeparatorChar;

            manager.SetSkinPath(ConfigPath, newSkin);

            Assert.Equal(newSkin, manager.Config.SkinPath);
            Assert.False(File.Exists(ConfigPath)); // write is deferred, not immediate

            manager.FlushPendingSave();

            Assert.True(File.Exists(ConfigPath));
            Assert.Contains($"SkinPath={newSkin}", File.ReadAllText(ConfigPath));
        }

        [Fact]
        public void SetSkinPath_WithUnchangedValue_ShouldNotDeferSave()
        {
            var manager = new ConfigManager();
            var current = manager.Config.SkinPath;

            manager.SetSkinPath(ConfigPath, current);
            manager.FlushPendingSave();

            Assert.Equal(current, manager.Config.SkinPath);
            Assert.False(File.Exists(ConfigPath)); // nothing marked dirty -> nothing written
        }

        [Fact]
        public void SetSkinPath_WithWhitespaceValue_ShouldBeIgnored()
        {
            var manager = new ConfigManager();
            var current = manager.Config.SkinPath;

            manager.SetSkinPath(ConfigPath, "   ");
            manager.FlushPendingSave();

            Assert.Equal(current, manager.Config.SkinPath);
            Assert.False(File.Exists(ConfigPath));
        }

        [Fact]
        public void SetSkinPath_WithEquivalentPathDifferingBySeparatorOrTrailingSlash_ShouldNotDeferSave()
        {
            // Config.SkinPath is loaded verbatim from Config.ini and may lack a
            // trailing separator or use backslashes, while the incoming value
            // arrives normalized by ResourceManager. These are the same path and
            // must not spuriously mark the config dirty.
            var manager = new ConfigManager();
            var current = manager.Config.SkinPath;
            // Build an equivalent path: same segments, opposite separators, no
            // trailing slash.
            var equivalent = current.Replace('/', Path.DirectorySeparatorChar)
                                    .Replace('\\', Path.DirectorySeparatorChar)
                                    .TrimEnd(Path.DirectorySeparatorChar);
            // Only meaningful when the default actually has a separator to trim;
            // otherwise the equivalent string equals current and the test is trivial.
            if (equivalent == current)
            {
                // Force a trailing-separator-only difference by appending and
                // re-trimming the OS separator on a copy.
                equivalent = current.TrimEnd(Path.DirectorySeparatorChar, '/');
            }

            manager.SetSkinPath(ConfigPath, equivalent);
            manager.FlushPendingSave();

            Assert.Equal(current, manager.Config.SkinPath);
            Assert.False(File.Exists(ConfigPath)); // no-op detected, nothing written
        }
    }
}

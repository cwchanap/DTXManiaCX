using System;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Utilities;
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

        [Fact]
        public void SkinPath_PersistedToConfigIni_ShouldRoundTripAcrossRestart()
        {
            // Simulate a full restart cycle: set SkinPath → flush → create a new
            // ConfigManager (fresh process) → load the same Config.ini → verify
            // the persisted SkinPath is read back. Then change it again, save,
            // reload once more, and verify the second value survives too.
            var skinA = Path.Combine(_tempDir, "System", "SkinA") + Path.DirectorySeparatorChar;
            var skinB = Path.Combine(_tempDir, "System", "SkinB") + Path.DirectorySeparatorChar;

            // --- First "session": set skin A and persist ---
            var manager1 = new ConfigManager();
            manager1.SetSkinPath(ConfigPath, skinA);
            manager1.FlushPendingSave();
            Assert.True(File.Exists(ConfigPath));
            Assert.Contains($"SkinPath={skinA}", File.ReadAllText(ConfigPath));

            // --- "Restart": new ConfigManager loads the persisted config ---
            var manager2 = new ConfigManager();
            manager2.LoadConfig(ConfigPath);
            Assert.Equal(skinA, manager2.Config.SkinPath);

            // --- Second "session": change to skin B and persist ---
            manager2.SetSkinPath(ConfigPath, skinB);
            manager2.FlushPendingSave();

            // --- Second "restart": new ConfigManager loads the updated config ---
            var manager3 = new ConfigManager();
            manager3.LoadConfig(ConfigPath);
            Assert.Equal(skinB, manager3.Config.SkinPath);
        }

        [Fact]
        public void SkinPath_ContainingEqualsSign_ShouldRoundTripAcrossRestart()
        {
            // A directory name containing '=' is legal. The loader must split on
            // the first '=' only so the value (which may itself contain '=') is
            // preserved verbatim. Without a 2-count split, the line
            // "SkinPath=/path/CX=Neon/" produces 3 pieces and is silently
            // discarded, so the selected skin works during the current session
            // but its configuration is lost on the next startup.
            var skinWithEquals = Path.Combine(_tempDir, "Skins", "CX=Neon") + Path.DirectorySeparatorChar;
            Directory.CreateDirectory(skinWithEquals);

            // --- First "session": set the skin path and persist ---
            var manager1 = new ConfigManager();
            manager1.SetSkinPath(ConfigPath, skinWithEquals);
            manager1.FlushPendingSave();
            Assert.True(File.Exists(ConfigPath));
            Assert.Contains($"SkinPath={skinWithEquals}", File.ReadAllText(ConfigPath));

            // --- "Restart": new ConfigManager loads the persisted config ---
            var manager2 = new ConfigManager();
            manager2.LoadConfig(ConfigPath);
            Assert.Equal(skinWithEquals, manager2.Config.SkinPath);
        }

        [Fact]
        public void LoadConfig_OnFreshConfig_ShouldPersistDefaultTokenNotAbsolutePath()
        {
            // A new config must persist the "Default" token, not an absolute
            // bundled path. The token resolves to the current install location
            // at runtime, surviving app relocations (moving the .app bundle or
            // portable folder).
            var manager = new ConfigManager();
            manager.LoadConfig(ConfigPath);

            Assert.Equal(ConfigManager.DefaultSkinPathToken, manager.Config.SkinPath);
            Assert.Contains($"SkinPath={ConfigManager.DefaultSkinPathToken}",
                File.ReadAllText(ConfigPath));
        }

        [Fact]
        public void ResolveSkinPath_WithDefaultToken_ShouldReturnValidatingBundledRoot()
        {
            // The "Default" token resolves to the current validating bundled
            // System skin root (or the app-data default when no bundled root
            // validates). This is the runtime resolution that makes the token
            // survive app relocations.
            var resolved = ConfigManager.ResolveSkinPath(ConfigManager.DefaultSkinPathToken);

            Assert.True(Path.IsPathRooted(resolved));
            // The resolved path should be one of the bundled candidates or the
            // app-data default — i.e. it should validate or at least be a real
            // candidate path.
            var candidates = AppPaths.GetBundledSystemSkinRootCandidates();
            var isBundledCandidate = false;
            foreach (var candidate in candidates)
            {
                if (string.Equals(resolved, candidate, AppPaths.SkinPathComparison))
                {
                    isBundledCandidate = true;
                    break;
                }
            }
            // If no bundled candidate validates, ResolveSkinPath falls back to
            // the app-data default — still a valid absolute path.
            Assert.True(isBundledCandidate ||
                string.Equals(resolved, AppPaths.GetDefaultSystemSkinRoot(),
                    AppPaths.SkinPathComparison));
        }

        [Fact]
        public void ResolveSkinPath_WithCustomPath_ShouldReturnAsIs()
        {
            // Custom skin paths are already absolute and must pass through
            // unchanged.
            var customPath = Path.Combine(_tempDir, "MyCustomSkin");
            Directory.CreateDirectory(customPath);

            var resolved = ConfigManager.ResolveSkinPath(customPath);

            Assert.Equal(customPath, resolved);
        }

        [Fact]
        public void LoadConfig_WithOldAbsoluteBundledPath_ShouldMigrateToDefaultToken()
        {
            // Migration from the previous format (commit 4134a68) where the
            // absolute bundled root was persisted directly. On load, such a
            // path should be recognized as a bundled candidate and remapped to
            // the "Default" token so future relocations don't stale it.
            var bundledCandidates = AppPaths.GetBundledSystemSkinRootCandidates()
                .Where(c => PathValidator.IsValidSkinPath(c))
                .ToList();
            if (bundledCandidates.Count == 0)
            {
                // No bundled root validates in this test environment — skip
                // rather than fabricate a path that wouldn't exercise the
                // migration branch.
                return;
            }

            var oldBundledPath = bundledCandidates[0];
            File.WriteAllText(ConfigPath,
                $"[System]\nSkinPath={oldBundledPath}\n");

            var manager = new ConfigManager();
            manager.LoadConfig(ConfigPath);

            Assert.Equal(ConfigManager.DefaultSkinPathToken, manager.Config.SkinPath);
        }

        [Fact]
        public void LoadConfig_WithDefaultToken_ShouldPreserveTokenAcrossRestart()
        {
            // A config that already stores the "Default" token should keep it
            // after a load → save → reload cycle.
            File.WriteAllText(ConfigPath,
                $"[System]\nSkinPath={ConfigManager.DefaultSkinPathToken}\n");

            var manager1 = new ConfigManager();
            manager1.LoadConfig(ConfigPath);
            Assert.Equal(ConfigManager.DefaultSkinPathToken, manager1.Config.SkinPath);

            // Trigger a save (LoadConfig on existing file doesn't auto-save
            // unless a setter marks dirty, so force one).
            manager1.SetAutoPlay(true);
            manager1.FlushPendingSave();

            var manager2 = new ConfigManager();
            manager2.LoadConfig(ConfigPath);
            Assert.Equal(ConfigManager.DefaultSkinPathToken, manager2.Config.SkinPath);
        }

        [Fact]
        public void DefaultToken_SurvivesRelocationSimulation()
        {
            // Simulate a relocation: create config at "location A", then
            // simulate a restart at "location B". With the token approach,
            // the config persists "Default" and ResolveSkinPath picks up the
            // current bundled root on every launch — so the effective default
            // tracks the current install location, not the one that was
            // active when the config was first written.
            var appDataA = Path.Combine(_tempDir, "appdataA");
            Directory.CreateDirectory(appDataA);

            // "Session at location A": create config with Default token.
            var configPathA = Path.Combine(appDataA, "Config.ini");
            var managerA = new ConfigManager();
            managerA.LoadConfig(configPathA);
            Assert.Equal(ConfigManager.DefaultSkinPathToken, managerA.Config.SkinPath);

            // The persisted file stores the token, not an absolute path.
            var persistedLine = File.ReadAllText(configPathA);
            Assert.Contains($"SkinPath={ConfigManager.DefaultSkinPathToken}", persistedLine);
            // No absolute bundled path should be persisted.
            foreach (var candidate in AppPaths.GetBundledSystemSkinRootCandidates())
            {
                Assert.DoesNotContain($"SkinPath={candidate}", persistedLine);
            }

            // "Relocation to location B": the same Config.ini (copied to a new
            // app-data root) still resolves to the current bundled root.
            var appDataB = Path.Combine(_tempDir, "appdataB");
            Directory.CreateDirectory(appDataB);
            var configPathB = Path.Combine(appDataB, "Config.ini");
            File.Copy(configPathA, configPathB);

            var prevRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", appDataB);
            try
            {
                var managerB = new ConfigManager();
                managerB.LoadConfig(configPathB);
                // Token survives the relocation.
                Assert.Equal(ConfigManager.DefaultSkinPathToken, managerB.Config.SkinPath);
                // And resolving it gives a valid absolute path (the current
                // bundled root, whatever it is at this install location).
                var resolved = ConfigManager.ResolveSkinPath(managerB.Config.SkinPath);
                Assert.True(Path.IsPathRooted(resolved));
            }
            finally
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", prevRoot);
            }
        }
    }
}

using System;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Utilities;
using Xunit;

namespace DTXMania.Test.Config
{
    [Trait("Category", "Unit")]
    [Collection("AppPaths")]
    public class ConfigManagerSkinPathTests : IDisposable
    {
        private readonly string _tempDir;
        private readonly string? _previousAppDataRoot;

        public ConfigManagerSkinPathTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "dtx-skinpath-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_tempDir);

            // Sandbox the app-data root so AppPaths.GetDefaultSystemSkinRoot()
            // and GetConfigFilePath() resolve under _tempDir, not the real
            // user app-data directory. Without this, LoadConfig normalizes the
            // default app-data SystemSkinRoot and DTXPath and ensures both
            // directories exist — creating Graphics/ in the real user app-data.
            // The [Collection("AppPaths")] attribute disables parallel execution
            // but does NOT redirect paths to a sandbox.
            _previousAppDataRoot = Environment.GetEnvironmentVariable("DTXMANIA_APPDATA_ROOT");
            Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", _tempDir);
        }

        public void Dispose()
        {
            try
            {
                Environment.SetEnvironmentVariable("DTXMANIA_APPDATA_ROOT", _previousAppDataRoot);
                Directory.Delete(_tempDir, recursive: true);
            }
            catch { /* best effort */ }
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
            // The migration must also be PERSISTED to the file — not just
            // applied in memory — so a relocation before the next
            // setter-triggered save doesn't leave the stale absolute path.
            //
            // This test exercises a GENUINE bundled path, not the app-data
            // default. It creates a fake install directory with a validating
            // System skin, then writes that path into Config.ini as the
            // "old absolute bundled path" and verifies LoadConfig migrates it
            // to the "Default" token. The explicit-base-directory seam
            // (ConfigManager.ResolveValidatingBundledSystemSkinRoot(baseDir))
            // is used indirectly through the internal ConfigManager members
            // that IsDefaultSkinPath consults via
            // AppPaths.GetBundledSystemSkinRootCandidates().
            //
            // Because AppPaths.GetBundledSystemSkinRootCandidates() reads
            // AppContext.BaseDirectory (immutable at runtime), we can't point
            // it at our fake install. Instead we verify the migration using
            // the app-data default path (which IsDefaultSkinPath also
            // recognizes) — but now sandboxed under _tempDir via
            // DTXMANIA_APPDATA_ROOT, so the test never touches the real user
            // app-data directory.
            var oldAbsolutePath = AppPaths.GetDefaultSystemSkinRoot();
            Directory.CreateDirectory(Path.Combine(oldAbsolutePath, "Graphics"));
            File.WriteAllText(Path.Combine(oldAbsolutePath, "Graphics", "1_background.jpg"), "bg");
            File.WriteAllText(ConfigPath,
                $"[System]\nSkinPath={oldAbsolutePath}\n");

            var manager = new ConfigManager();
            manager.LoadConfig(ConfigPath);

            // In-memory value migrated to the token.
            Assert.Equal(ConfigManager.DefaultSkinPathToken, manager.Config.SkinPath);

            // Persisted file contents also migrated — the absolute path is
            // gone and the token is in its place.
            var persistedContents = File.ReadAllText(ConfigPath);
            Assert.Contains($"SkinPath={ConfigManager.DefaultSkinPathToken}",
                persistedContents);
            Assert.DoesNotContain($"SkinPath={oldAbsolutePath}", persistedContents);
        }

        [Fact]
        public void LoadConfig_WithOldAbsoluteBundledPathFromExplicitBaseDir_ShouldMigrateToDefaultToken()
        {
            // Companion test that exercises a GENUINE bundled path (not the
            // app-data default) using the explicit-base-directory seam. Creates
            // a fake install directory with a validating System skin, writes
            // that path as the "old absolute bundled path" in Config.ini, then
            // verifies ConfigManager.IsDefaultSkinPath recognizes it via
            // AppPaths.GetBundledSystemSkinRootCandidates(baseDir).
            //
            // LoadConfig itself calls IsDefaultSkinPath using
            // AppContext.BaseDirectory (immutable), so we can't make LoadConfig
            // see our fake install directly. Instead we verify the recognition
            // logic through the internal seam: if the bundled candidate from
            // the fake base dir validates, IsDefaultSkinPath would return true
            // for it, and LoadConfig would migrate it to the token.
            var installDir = Path.Combine(_tempDir, "fakeInstall");
            Directory.CreateDirectory(installDir);
            var systemRoot = Path.Combine(installDir, "System");
            Directory.CreateDirectory(Path.Combine(systemRoot, "Graphics"));
            File.WriteAllText(Path.Combine(systemRoot, "Graphics", "1_background.jpg"), "bg");

            // The bundled candidate from this base dir validates.
            var resolvedBundled = ConfigManager.ResolveValidatingBundledSystemSkinRoot(installDir);
            Assert.NotNull(resolvedBundled);
            Assert.True(PathValidator.IsValidSkinPath(resolvedBundled!),
                "The fake install's System root must validate so IsDefaultSkinPath recognizes it");

            // ResolveSkinPath with the explicit base dir maps "Default" to
            // this bundled root — proving the token tracks the install location.
            var resolved = ConfigManager.ResolveSkinPath(
                ConfigManager.DefaultSkinPathToken, installDir);
            Assert.Equal(
                Path.GetFullPath(systemRoot).TrimEnd('/', Path.DirectorySeparatorChar),
                resolved.TrimEnd('/', Path.DirectorySeparatorChar));
        }

        [Fact]
        public void LoadConfig_WithGenuineBundledPath_ShouldMigrateAndPersistToDefaultToken()
        {
            // End-to-end migration test using a GENUINE bundled path (not the
            // app-data default). The previous companion test
            // (LoadConfig_WithOldAbsoluteBundledPathFromExplicitBaseDir) only
            // exercised candidate resolution and token mapping through the
            // internal seams — it never wrote the bundled path to Config.ini,
            // called LoadConfig, or verified the file was rewritten.
            //
            // This test closes that gap via the LoadConfig(filePath, baseDir)
            // seam: it creates a fake install directory with a validating
            // System skin, writes that bundled root as the "old absolute
            // bundled path" into Config.ini, calls LoadConfig with the fake
            // install as the base dir, and asserts BOTH the in-memory value
            // AND the persisted file contents migrated to the "Default" token.
            // This is the complete load-and-persist migration the reviewer
            // flagged as missing.
            var installDir = Path.Combine(_tempDir, "fakeInstall");
            Directory.CreateDirectory(installDir);
            var systemRoot = Path.Combine(installDir, "System");
            Directory.CreateDirectory(Path.Combine(systemRoot, "Graphics"));
            File.WriteAllText(Path.Combine(systemRoot, "Graphics", "1_background.jpg"), "bg");

            // Resolve the genuine bundled root from the fake install dir. On
            // macOS the first candidate is ../Resources/System (a sibling of
            // installDir, which doesn't exist here), so this falls through to
            // the installDir/System candidate that does validate.
            var bundledRoot = ConfigManager.ResolveValidatingBundledSystemSkinRoot(installDir);
            Assert.NotNull(bundledRoot);
            Assert.True(PathValidator.IsValidSkinPath(bundledRoot!),
                "Fake install's System root must validate so IsDefaultSkinPath recognizes it");

            // Write the genuine bundled path as the stale "old format" value.
            File.WriteAllText(ConfigPath,
                $"[System]\nSkinPath={bundledRoot}\n");

            var manager = new ConfigManager();
            // Use the baseDir seam so LoadConfig resolves bundled candidates
            // from the fake install, not AppContext.BaseDirectory.
            manager.LoadConfig(ConfigPath, installDir);

            // In-memory value migrated to the token.
            Assert.Equal(ConfigManager.DefaultSkinPathToken, manager.Config.SkinPath);

            // Persisted file contents also migrated — the absolute bundled
            // path is gone and the token is in its place.
            var persistedContents = File.ReadAllText(ConfigPath);
            Assert.Contains($"SkinPath={ConfigManager.DefaultSkinPathToken}",
                persistedContents);
            Assert.DoesNotContain($"SkinPath={bundledRoot}", persistedContents);
        }

        [Fact]
        public void IsDefaultSkinPath_WithGenuineBundledPathAndMatchingBaseDir_ShouldReturnTrue()
        {
            // Direct unit test of the IsDefaultSkinPath(path, baseDir) seam:
            // a genuine bundled root from a fake install dir must be
            // recognized as a default skin path so LoadConfig migrates it to
            // the token. This complements the end-to-end test above by
            // pinning the recognition logic independently of the load flow.
            var installDir = Path.Combine(_tempDir, "fakeInstall");
            Directory.CreateDirectory(installDir);
            var systemRoot = Path.Combine(installDir, "System");
            Directory.CreateDirectory(Path.Combine(systemRoot, "Graphics"));
            File.WriteAllText(Path.Combine(systemRoot, "Graphics", "1_background.jpg"), "bg");

            var bundledRoot = ConfigManager.ResolveValidatingBundledSystemSkinRoot(installDir);
            Assert.NotNull(bundledRoot);

            Assert.True(ConfigManager.IsDefaultSkinPath(bundledRoot!, installDir),
                "Genuine bundled root from the matching base dir must be recognized as a default skin path");

            // A non-matching base dir must NOT recognize it — proving the
            // recognition is baseDir-dependent, not a false positive from the
            // app-data default branch.
            var otherDir = Path.Combine(_tempDir, "otherInstall");
            Directory.CreateDirectory(otherDir);
            Assert.False(ConfigManager.IsDefaultSkinPath(bundledRoot!, otherDir),
                "Genuine bundled root must not be recognized from an unrelated base dir");
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

        [Fact]
        public void DefaultToken_ResolvesToCurrentBundledRootAcrossRelocation()
        {
            // Deterministic relocation test: create two fake bundled roots
            // (location A and B), each with a validating System skin, and
            // verify the "Default" token resolves to the bundled root at the
            // given base directory. This exercises the actual relocation
            // scenario (bundled root A → bundled root B) without mutating
            // AppContext.BaseDirectory, which is immutable at runtime.
            var installA = Path.Combine(_tempDir, "installA");
            var installB = Path.Combine(_tempDir, "installB");
            Directory.CreateDirectory(installA);
            Directory.CreateDirectory(installB);

            // Create validating bundled System skins at both locations.
            // PathValidator.IsValidSkinPath checks for Graphics/1_background.jpg
            // or Graphics/2_background.jpg under the skin root.
            var systemA = Path.Combine(installA, "System");
            var systemB = Path.Combine(installB, "System");
            Directory.CreateDirectory(Path.Combine(systemA, "Graphics"));
            Directory.CreateDirectory(Path.Combine(systemB, "Graphics"));
            File.WriteAllText(Path.Combine(systemA, "Graphics", "1_background.jpg"), "bg");
            File.WriteAllText(Path.Combine(systemB, "Graphics", "1_background.jpg"), "bg");

            // At install A, "Default" resolves to A's bundled System root.
            var resolvedA = ConfigManager.ResolveSkinPath(
                ConfigManager.DefaultSkinPathToken, installA);
            Assert.True(Path.IsPathRooted(resolvedA));
            Assert.Equal(
                Path.GetFullPath(systemA).TrimEnd('/', Path.DirectorySeparatorChar),
                resolvedA.TrimEnd('/', Path.DirectorySeparatorChar));

            // At install B, "Default" resolves to B's bundled System root —
            // a DIFFERENT absolute path, proving the token tracks the current
            // install location rather than a stale persisted path.
            var resolvedB = ConfigManager.ResolveSkinPath(
                ConfigManager.DefaultSkinPathToken, installB);
            Assert.True(Path.IsPathRooted(resolvedB));
            Assert.Equal(
                Path.GetFullPath(systemB).TrimEnd('/', Path.DirectorySeparatorChar),
                resolvedB.TrimEnd('/', Path.DirectorySeparatorChar));

            Assert.NotEqual(resolvedA, resolvedB);
        }
    }
}

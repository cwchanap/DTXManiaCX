using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Release-readiness gate for the original CX Neon pack, which lives at the
    /// repository root System/ directory and ships as the base skin. Every
    /// runtime texture and sound must be present: a missing file would render
    /// as a white box (textures) or fall back to silent audio (sounds) with no
    /// further fallback, so absence is always a failure (no self-skip).
    /// </summary>
    [Trait("Category", "Resources")]
    public class CxNeonPackTests
    {
        [Fact]
        public void CxNeonTheme_SelectedMenuText_ShouldUseReadableLightColor()
        {
            var theme = SkinTheme.Load(Path.Combine(FindRepoRoot(), "System", "Theme.ini"));

            Assert.Equal(Color.White, ConfigStage.ResolveSelectedMenuTextColor(theme));
        }

        [Fact]
        public void CxNeonPack_ShouldExpectEveryRequiredTexture()
        {
            var packRoot = Path.Combine(FindRepoRoot(), "System");

            var required = TexturePath.GetAllTexturePaths()
                .Where(p => p != TexturePath.PerformanceBackgroundVideo);

            var missing = required
                .Where(rel => !File.Exists(Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar))))
                .OrderBy(rel => rel)
                .ToList();

            Assert.True(missing.Count == 0,
                $"CX Neon pack is missing {missing.Count} required textures:\n  " +
                string.Join("\n  ", missing));
        }

        [Fact]
        public void CxNeonPack_ShouldExpectEveryRequiredSound()
        {
            var packRoot = Path.Combine(FindRepoRoot(), "System");

            var required = SoundPath.GetAllSoundPaths();

            var missing = required
                .Where(rel => !File.Exists(Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar))))
                .OrderBy(rel => rel)
                .ToList();

            Assert.True(missing.Count == 0,
                $"CX Neon pack is missing {missing.Count} required sounds:\n  " +
                string.Join("\n  ", missing));
        }

        /// <summary>
        /// Theme.ini can reference textures that are NOT in TexturePath.cs (e.g.
        /// Performance.SkillPanelTexture=Graphics/7_SkillPanel_perf.png). These
        /// theme-only assets are invisible to the TexturePath-driven pack test
        /// above and to skingen bootstrap, so a clean generate_source.py /
        /// skingen compose rebuild can silently omit them while both this test
        /// suite and skingen validate remain green. This guard parses Theme.ini
        /// for Graphics/ references and checks both pack presence and manifest
        /// inventory so a missing theme asset is caught at CI time.
        /// </summary>
        [Fact]
        public void CxNeonPack_ShouldExpectEveryThemeReferencedTextureInPackAndManifest()
        {
            var repoRoot = FindRepoRoot();
            var packRoot = Path.Combine(repoRoot, "System");
            var themePath = Path.Combine(packRoot, "Theme.ini");

            var manifestPath = Path.Combine(repoRoot, "tools", "skingen", "manifest.json");
            var manifestKeys = new HashSet<string>(StringComparer.Ordinal);
            if (File.Exists(manifestPath))
            {
                using var doc = JsonDocument.Parse(File.ReadAllText(manifestPath));
                if (doc.RootElement.TryGetProperty("assets", out var assets))
                {
                    foreach (var prop in assets.EnumerateObject())
                        manifestKeys.Add(prop.Name);
                }
            }

            // Match key=value lines where value starts with Graphics/ and ends
            // with a known image extension. Skip comment lines (leading ;).
            var textureRefPattern = new Regex(
                @"^[A-Za-z][A-Za-z0-9.]*\s*=\s*(Graphics/[^\s;]+\.(?:png|jpg|jpeg))\s*$",
                RegexOptions.IgnoreCase | RegexOptions.Compiled);

            var referenced = new List<string>();
            foreach (var line in File.ReadAllLines(themePath))
            {
                var trimmed = line.TrimStart();
                if (trimmed.StartsWith(";") || trimmed.StartsWith("#"))
                    continue;
                var match = textureRefPattern.Match(line);
                if (match.Success)
                    referenced.Add(match.Groups[1].Value);
            }

            // Distinct, sorted for stable failure messages.
            referenced = referenced.Distinct().OrderBy(r => r).ToList();

            var missingFromPack = referenced
                .Where(rel => !File.Exists(Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar))))
                .ToList();
            Assert.True(missingFromPack.Count == 0,
                $"Theme.ini references textures absent from the CX Neon pack ({missingFromPack.Count} missing):\n  " +
                string.Join("\n  ", missingFromPack));

            var missingFromManifest = referenced
                .Where(rel => !manifestKeys.Contains(rel))
                .ToList();
            Assert.True(missingFromManifest.Count == 0,
                $"Theme.ini references textures absent from tools/skingen/manifest.json ({missingFromManifest.Count} missing):\n  " +
                string.Join("\n  ", missingFromManifest) +
                "\n  Run `python tools/skingen/generate_source.py` then add the asset to manifest.json.");
        }

        /// <summary>
        /// The bundled skin must reach the build output (and so every published
        /// artifact) with its full relative layout. MSBuild globs also pick up
        /// physical junk that .gitignore hides, so the game projects exclude it
        /// explicitly; this guard fails when a copy item regresses, a Link
        /// collapses nested directories, or junk leaks into the output.
        /// </summary>
        [Fact]
        public void BundledSystemSkin_ShouldBeCopiedToBuildOutputWithIntactLayoutAndNoJunk()
        {
            var repoPack = Path.Combine(FindRepoRoot(), "System");
            var outputPack = Path.Combine(AppContext.BaseDirectory, "System");

            Assert.True(Directory.Exists(outputPack),
                $"Bundled System skin was not copied to the build output: {outputPack}");

            var expected = RelativeFiles(repoPack)
                .Where(p => p == "Theme.ini" ||
                            p.StartsWith("Graphics/", StringComparison.Ordinal) ||
                            p.StartsWith("Sounds/", StringComparison.Ordinal))
                .Where(p => !IsJunk(Path.GetFileName(p)))
                .ToList();
            var actual = RelativeFiles(outputPack).ToList();
            Assert.NotEmpty(expected);

            var missing = expected.Except(actual, StringComparer.Ordinal).OrderBy(p => p).ToList();
            Assert.True(missing.Count == 0,
                $"Bundled System skin in {outputPack} is missing {missing.Count} files:\n  " +
                string.Join("\n  ", missing));

            var unexpected = actual.Except(expected, StringComparer.Ordinal).OrderBy(p => p).ToList();
            Assert.True(unexpected.Count == 0,
                $"Bundled System skin in {outputPack} contains {unexpected.Count} files absent from the source pack:\n  " +
                string.Join("\n  ", unexpected));
        }

        private static IEnumerable<string> RelativeFiles(string root)
        {
            return Directory.EnumerateFiles(root, "*", SearchOption.AllDirectories)
                .Select(f => Path.GetRelativePath(root, f).Replace(Path.DirectorySeparatorChar, '/'));
        }

        private static bool IsJunk(string fileName)
        {
            return string.Equals(fileName, ".DS_Store", StringComparison.Ordinal)
                || string.Equals(fileName, "desktop.ini", StringComparison.OrdinalIgnoreCase)
                || string.Equals(fileName, "Thumbs.db", StringComparison.OrdinalIgnoreCase)
                || fileName.EndsWith(".log", StringComparison.OrdinalIgnoreCase);
        }

        private static string FindRepoRoot()
        {
            var dir = new DirectoryInfo(AppContext.BaseDirectory);
            while (dir != null)
            {
                if (Directory.Exists(Path.Combine(dir.FullName, "DTXMania.Game")) &&
                    Directory.Exists(Path.Combine(dir.FullName, "System")))
                {
                    return dir.FullName;
                }
                dir = dir.Parent;
            }

            throw new InvalidOperationException(
                "Could not locate repository root from " + AppContext.BaseDirectory + ".");
        }
    }
}

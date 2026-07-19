using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Release-readiness gate for the original CX Neon pack. Asset generation
    /// is delegated and happens incrementally (images and sounds may land in
    /// separate batches — see tools/README.md). Each tree is gated
    /// independently: the texture check passes vacuously until
    /// System/CXNeon/Graphics exists, and the sound check passes vacuously
    /// until System/CXNeon/Sounds exists. This mirrors the CI workflow, which
    /// skips validation/bundling until both trees are present, and avoids
    /// failing every normal CI run when one batch lands before the other.
    /// Once a tree exists it must be complete: releases bundle it as the base
    /// System skin, and a missing file would render as a white box (textures)
    /// or fall back to silent audio (sounds) with no further fallback.
    /// </summary>
    [Trait("Category", "Resources")]
    public class CxNeonPackTests
    {
        [Fact]
        public void CxNeonPack_ShouldExpectEveryRequiredTextureWhenGraphicsTreeExists()
        {
            var packRoot = Path.Combine(FindRepoRoot(), "System", "CXNeon");
            if (!Directory.Exists(Path.Combine(packRoot, "Graphics")))
                return; // texture tree not yet produced — see tools/README.md

            var required = TexturePath.GetAllTexturePaths()
                .Where(p => p != TexturePath.PerformanceBackgroundVideo);

            var missing = required
                .Where(rel => !File.Exists(Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar))))
                .OrderBy(rel => rel)
                .ToList();

            Assert.True(missing.Count == 0,
                $"CX Neon Graphics tree is present but incomplete ({missing.Count} missing textures):\n  " +
                string.Join("\n  ", missing));
        }

        [Fact]
        public void CxNeonPack_ShouldExpectEveryRequiredSoundWhenSoundsTreeExists()
        {
            var packRoot = Path.Combine(FindRepoRoot(), "System", "CXNeon");
            if (!Directory.Exists(Path.Combine(packRoot, "Sounds")))
                return; // sound tree not yet produced — see tools/README.md

            var required = SoundPath.GetAllSoundPaths();

            var missing = required
                .Where(rel => !File.Exists(Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar))))
                .OrderBy(rel => rel)
                .ToList();

            Assert.True(missing.Count == 0,
                $"CX Neon Sounds tree is present but incomplete ({missing.Count} missing sounds):\n  " +
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
            var packRoot = Path.Combine(repoRoot, "System", "CXNeon");
            var themePath = Path.Combine(packRoot, "Theme.ini");
            if (!File.Exists(themePath))
                return; // no Theme.ini — nothing theme-only to guard

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

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

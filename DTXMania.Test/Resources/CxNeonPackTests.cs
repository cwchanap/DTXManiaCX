using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Release-readiness gate for the original CX Neon pack. While the pack has
    /// not been produced yet (no System/CXNeon/Graphics directory), the test
    /// passes vacuously — asset generation is delegated and happens later.
    /// The moment the directory exists, the pack must be complete: releases
    /// bundle it as the base System skin, and a missing file would render as
    /// a white box with no further fallback.
    /// </summary>
    [Trait("Category", "Resources")]
    public class CxNeonPackTests
    {
        [Fact]
        public void CxNeonPack_ShouldExpectEveryRequiredTextureAndSound()
        {
            var packRoot = Path.Combine(FindRepoRoot(), "System", "CXNeon");
            if (!Directory.Exists(Path.Combine(packRoot, "Graphics")))
                return; // pack not yet produced — see tools/README.md

            var required = new List<string>(TexturePath.GetAllTexturePaths()
                .Where(p => p != TexturePath.PerformanceBackgroundVideo));
            required.AddRange(SoundPath.GetAllSoundPaths());

            var missing = required
                .Where(rel => !File.Exists(Path.Combine(packRoot, rel.Replace('/', Path.DirectorySeparatorChar))))
                .OrderBy(rel => rel)
                .ToList();

            Assert.True(missing.Count == 0,
                $"CX Neon pack is present but incomplete ({missing.Count} missing):\n  " +
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

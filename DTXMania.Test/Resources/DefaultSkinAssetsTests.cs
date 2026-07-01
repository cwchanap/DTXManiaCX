using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI.Layout;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Guards against regressions where the bundled default <c>System</c> skin is
    /// missing assets that the game loads at runtime. The hit-effect sprite sheet
    /// (<see cref="TexturePath.HitFx"/>) is bundled for skin compatibility; without
    /// this test, a future skin refactor could silently drop the asset and nobody
    /// would notice until a player reported it.
    /// </summary>
    [Trait("Category", "Resources")]
    public class DefaultSkinAssetsTests
    {
        private static readonly byte[] PngSignature =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        };

        [Fact]
        public void DefaultSkin_ShouldShipHitEffectSpriteSheet()
        {
            var repoRoot = FindRepoRoot();
            var hitFxPath = Path.Combine(repoRoot, "System", "Graphics", "hit_fx.png");

            Assert.True(File.Exists(hitFxPath),
                $"Bundled default skin must ship {TexturePath.HitFx}.");

            AssertPngSignature(hitFxPath, TexturePath.HitFx);
        }

        [Theory]
        [MemberData(nameof(BundledNxJudgementCollisionAssetPaths))]
        public void DefaultSkin_ShouldShipBundledNxJudgementCollisionAssets(string relativePath)
        {
            var repoRoot = FindRepoRoot();
            var assetPath = Path.Combine(repoRoot, "System", relativePath.Replace('/', Path.DirectorySeparatorChar));

            Assert.True(File.Exists(assetPath), $"Bundled default skin must ship {relativePath}.");
            AssertPngSignature(assetPath, relativePath);
        }

        public static IEnumerable<object[]> BundledNxJudgementCollisionAssetPaths()
        {
            yield return new object[] { TexturePath.JudgeStringsXg };
            yield return new object[] { TexturePath.ChipWave };

            for (var lane = 0; lane < PerformanceUILayout.LaneCount; lane++)
            {
                yield return new object[] { TexturePath.GetDrumChipFireLanePath(lane) };
                yield return new object[] { TexturePath.GetDrumChipStarLanePath(lane) };
            }
        }

        private static void AssertPngSignature(string filePath, string relativePath)
        {
            var header = File.ReadAllBytes(filePath);
            Assert.True(header.Length >= PngSignature.Length,
                $"Bundled {relativePath} is not a valid PNG (too short).");
            for (int i = 0; i < PngSignature.Length; i++)
            {
                Assert.Equal(PngSignature[i], header[i]);
            }
        }

        /// <summary>
        /// Walks upward from the test assembly's directory until both
        /// <c>DTXMania.Game</c> and <c>System</c> sibling directories exist, which
        /// uniquely identifies the repository root regardless of bin layout.
        /// </summary>
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
                "Could not locate repository root from " + AppContext.BaseDirectory +
                ". Ensure the test is executed from a build output inside the repo.");
        }
    }
}

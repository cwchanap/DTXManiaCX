using System;
using System.IO;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Performance;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// Guards against regressions where the bundled default <c>System</c> skin is
    /// missing assets that the game loads at runtime. The most load-bearing case is
    /// the hit-effect sprite sheet (<see cref="TexturePath.HitFx"/>): <see cref="EffectsManager"/>
    /// degrades gracefully when it is absent, but shipping it means players get the real
    /// hit-effect visual instead of the synthesized fallback, and the E2E gameplay smoke
    /// test exercises the real asset load path. Without this test, a future skin refactor
    /// could silently drop the asset and nobody would notice until a player reported it.
    /// </summary>
    [Trait("Category", "Resources")]
    public class DefaultSkinAssetsTests
    {
        private static readonly byte[] PngSignature =
        {
            0x89, 0x50, 0x4E, 0x47, 0x0D, 0x0A, 0x1A, 0x0A
        };

        [Fact]
        public void DefaultSkin_ShouldShipHitEffectSpriteSheet_ForRequiredEffectsManagerLoad()
        {
            var repoRoot = FindRepoRoot();
            var hitFxPath = Path.Combine(repoRoot, "System", "Graphics", "hit_fx.png");

            // The path constant mirrors EffectsManager's FrameWidth x FrameHeight (8x32),
            // so a load must yield TotalSprites >= 1. EffectsManager now degrades gracefully
            // if the asset is missing, but shipping it gives players the real visual and
            // keeps the E2E smoke test on the real load path.
            Assert.True(File.Exists(hitFxPath),
                $"Bundled default skin must ship {TexturePath.HitFx}. EffectsManager falls " +
                "back to a synthesized texture when it is missing, but the default skin " +
                "should provide the real hit-effect visual.");

            var header = File.ReadAllBytes(hitFxPath);
            Assert.True(header.Length >= PngSignature.Length,
                $"Bundled {TexturePath.HitFx} is not a valid PNG (too short).");
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

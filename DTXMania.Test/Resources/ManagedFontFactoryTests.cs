using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;
using Xunit;

namespace DTXMania.Test.Resources
{
    public class ManagedFontFactoryTests
    {
        [Fact]
        public void GetBestSizeSpriteFont_WhenBoldRequestedAtSize14_PrefersBoldAssetName()
        {
            // Use reflection because the method is private static and we want to
            // test asset-name selection without requiring a real ContentManager.
            var method = typeof(ManagedFont).GetMethod(
                "GetBestSpriteFontAssetName",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            var result = (string)method!.Invoke(null, new object[] { 14, FontStyle.Bold })!;
            Assert.Equal("NotoSerifJP-Bold", result);
        }

        [Fact]
        public void GetBestSizeSpriteFont_WhenRegularRequestedAtSize14_PrefersRegularAssetName()
        {
            var method = typeof(ManagedFont).GetMethod(
                "GetBestSpriteFontAssetName",
                BindingFlags.NonPublic | BindingFlags.Static);

            var result = (string)method!.Invoke(null, new object[] { 14, FontStyle.Regular })!;
            Assert.Equal("NotoSerifJP", result);
        }

        [Fact]
        public void GetBestSizeSpriteFont_WhenBoldRequestedAtSize24_FallsBackToRegular24()
        {
            // No -24-Bold asset exists; the factory should fall back to the closest
            // size in Regular rather than picking 14-Bold (which is the wrong size).
            var method = typeof(ManagedFont).GetMethod(
                "GetBestSpriteFontAssetName",
                BindingFlags.NonPublic | BindingFlags.Static);

            var result = (string)method!.Invoke(null, new object[] { 24, FontStyle.Bold })!;
            Assert.Equal("NotoSerifJP-24", result);
        }
    }
}

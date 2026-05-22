using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework.Graphics;
using System.Reflection;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public class ManagedFontFactoryTests
    {
        private static (string assetName, FontStyle resolvedStyle) InvokeGetBestSpriteFontAssetName(int size, FontStyle style)
        {
            var method = typeof(ManagedFont).GetMethod(
                "GetBestSpriteFontAssetName",
                BindingFlags.NonPublic | BindingFlags.Static);

            Assert.NotNull(method);

            var result = method!.Invoke(null, new object[] { size, style });
            // The method returns a ValueTuple<string, FontStyle> which boxes to ValueTuple<string, FontStyle>
            var tuple = ((string, FontStyle))result!;
            return tuple;
        }

        [Fact]
        public void GetBestSizeSpriteFont_WhenBoldRequestedAtSize14_PrefersBoldAssetName()
        {
            var (assetName, resolvedStyle) = InvokeGetBestSpriteFontAssetName(14, FontStyle.Bold);
            Assert.Equal("NotoSerifJP-Bold", assetName);
            Assert.Equal(FontStyle.Bold, resolvedStyle);
        }

        [Fact]
        public void GetBestSizeSpriteFont_WhenRegularRequestedAtSize14_PrefersRegularAssetName()
        {
            var (assetName, resolvedStyle) = InvokeGetBestSpriteFontAssetName(14, FontStyle.Regular);
            Assert.Equal("NotoSerifJP", assetName);
            Assert.Equal(FontStyle.Regular, resolvedStyle);
        }

        [Fact]
        public void GetBestSizeSpriteFont_WhenBoldRequestedAtSize24_FallsBackToRegular24()
        {
            // No -24-Bold asset exists; the factory should fall back to the closest
            // size in Regular rather than picking 14-Bold (which is the wrong size).
            var (assetName, resolvedStyle) = InvokeGetBestSpriteFontAssetName(24, FontStyle.Bold);
            Assert.Equal("NotoSerifJP-24", assetName);
            Assert.Equal(FontStyle.Regular, resolvedStyle); // Style reflects the actual resolved asset
        }

        [Fact]
        public void GetBestSizeSpriteFont_WhenBoldRequestedAtSize48_FallsBackToRegular48()
        {
            // No -48-Bold asset exists; should fall back to Regular-48
            var (assetName, resolvedStyle) = InvokeGetBestSpriteFontAssetName(48, FontStyle.Bold);
            Assert.Equal("NotoSerifJP-48", assetName);
            Assert.Equal(FontStyle.Regular, resolvedStyle);
        }

        [Fact]
        public void GetBestSizeSpriteFont_WhenBoldRequestedAtSize14_ResolvedStyleIsBold()
        {
            // Size 14 Bold exists, so resolved style should match requested
            var (_, resolvedStyle) = InvokeGetBestSpriteFontAssetName(14, FontStyle.Bold);
            Assert.Equal(FontStyle.Bold, resolvedStyle);
        }

        [Fact]
        public void GetBestSizeSpriteFont_WhenBoldRequestedAtSize24_ResolvedStyleIsRegular()
        {
            // Size 24 Bold doesn't exist, resolved style should be Regular
            var (_, resolvedStyle) = InvokeGetBestSpriteFontAssetName(24, FontStyle.Bold);
            Assert.Equal(FontStyle.Regular, resolvedStyle);
        }
    }
}

using DTXMania.Game.Lib.Resources;
using Xunit;

namespace DTXMania.Test.Resources
{
    /// <summary>
    /// SpriteFont asset selection: requested (family, size, style) snaps to the
    /// nearest baked asset. NotoSerifJP is the CJK-capable default family
    /// (14/24/48px, Bold only at 14); Orbitron is a Latin display family
    /// (14/18/24/32/40px, no Bold) used by skins for numeric and display text.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ManagedFontAssetMappingTests
    {
        [Theory]
        [InlineData("NotoSerifJP", 14, "NotoSerifJP")]
        [InlineData("NotoSerifJP", 16, "NotoSerifJP")]
        [InlineData("NotoSerifJP", 20, "NotoSerifJP-24")]
        [InlineData("NotoSerifJP", 48, "NotoSerifJP-48")]
        [InlineData("Arial", 14, "NotoSerifJP")]
        public void GetBestSpriteFontAssetName_DefaultFamily_ShouldSnapToNotoSerif(
            string fontPath, int size, string expectedAsset)
        {
            var (asset, style) = ManagedFont.GetBestSpriteFontAssetName(fontPath, size, FontStyle.Regular);

            Assert.Equal(expectedAsset, asset);
            Assert.Equal(FontStyle.Regular, style);
        }

        [Theory]
        [InlineData(14, "Orbitron-14")]
        [InlineData(16, "Orbitron-14")]
        [InlineData(18, "Orbitron-18")]
        [InlineData(24, "Orbitron-24")]
        [InlineData(30, "Orbitron-32")]
        [InlineData(32, "Orbitron-32")]
        [InlineData(40, "Orbitron-40")]
        [InlineData(50, "Orbitron-40")]
        public void GetBestSpriteFontAssetName_OrbitronFamily_ShouldSnapToOrbitron(
            int size, string expectedAsset)
        {
            var (asset, style) = ManagedFont.GetBestSpriteFontAssetName("Orbitron", size, FontStyle.Regular);

            Assert.Equal(expectedAsset, asset);
            Assert.Equal(FontStyle.Regular, style);
        }

        [Theory]
        [InlineData(12, "ShareTechMono-14")]
        [InlineData(14, "ShareTechMono-14")]
        // A tie snaps to the smaller asset, as it does for Orbitron.
        [InlineData(16, "ShareTechMono-14")]
        [InlineData(17, "ShareTechMono-18")]
        [InlineData(18, "ShareTechMono-18")]
        [InlineData(30, "ShareTechMono-18")]
        public void GetBestSpriteFontAssetName_ShareTechMonoFamily_ShouldSnapToShareTechMono(
            int size, string expectedAsset)
        {
            var (asset, style) = ManagedFont.GetBestSpriteFontAssetName("ShareTechMono", size, FontStyle.Regular);

            Assert.Equal(expectedAsset, asset);
            Assert.Equal(FontStyle.Regular, style);
        }

        [Fact]
        public void GetBestSpriteFontAssetName_ShareTechMonoFamily_ShouldIgnoreBoldRequest()
        {
            // The mono telemetry face ships Regular only.
            var (asset, style) = ManagedFont.GetBestSpriteFontAssetName("ShareTechMono", 14, FontStyle.Bold);

            Assert.Equal("ShareTechMono-14", asset);
            Assert.Equal(FontStyle.Regular, style);
        }

        [Fact]
        public void GetBestSpriteFontAssetName_ShareTechMonoFamily_ShouldBeCaseInsensitive()
        {
            var (asset, _) = ManagedFont.GetBestSpriteFontAssetName("sharetechmono", 14, FontStyle.Regular);

            Assert.Equal("ShareTechMono-14", asset);
        }

        [Fact]
        public void GetBestSpriteFontAssetName_OrbitronFamily_ShouldBeCaseInsensitive()
        {
            var (asset, _) = ManagedFont.GetBestSpriteFontAssetName("orbitron", 18, FontStyle.Regular);

            Assert.Equal("Orbitron-18", asset);
        }

        [Theory]
        [InlineData("Orbitron.ttf", 18, "Orbitron-18")]
        [InlineData("fonts/Orbitron.ttf", 24, "Orbitron-24")]
        [InlineData("ShareTechMono.ttf", 14, "ShareTechMono-14")]
        [InlineData("path/to/ShareTechMono.otf", 18, "ShareTechMono-18")]
        public void GetBestSpriteFontAssetName_PathFormRequest_ShouldResolveFamily(
            string fontPath, int size, string expectedAsset)
        {
            var (asset, style) = ManagedFont.GetBestSpriteFontAssetName(fontPath, size, FontStyle.Regular);

            Assert.Equal(expectedAsset, asset);
            Assert.Equal(FontStyle.Regular, style);
        }

        [Fact]
        public void GetBestSpriteFontAssetName_OrbitronBold_ShouldFallBackToRegular()
        {
            // Orbitron has no baked Bold variant; requests resolve to the
            // nearest Regular asset with the style downgraded.
            var (asset, style) = ManagedFont.GetBestSpriteFontAssetName("Orbitron", 18, FontStyle.Bold);

            Assert.Equal("Orbitron-18", asset);
            Assert.Equal(FontStyle.Regular, style);
        }

        [Fact]
        public void GetBestSpriteFontAssetName_NotoSerifBold14_ShouldKeepBoldVariant()
        {
            var (asset, style) = ManagedFont.GetBestSpriteFontAssetName("NotoSerifJP", 14, FontStyle.Bold);

            Assert.Equal("NotoSerifJP-Bold", asset);
            Assert.Equal(FontStyle.Bold, style);
        }

        [Fact]
        public void GetBestSpriteFontAssetName_NotoSerifBold24_ShouldPreferRightSizedRegular()
        {
            var (asset, style) = ManagedFont.GetBestSpriteFontAssetName("NotoSerifJP", 24, FontStyle.Bold);

            Assert.Equal("NotoSerifJP-24", asset);
            Assert.Equal(FontStyle.Regular, style);
        }
    }
}

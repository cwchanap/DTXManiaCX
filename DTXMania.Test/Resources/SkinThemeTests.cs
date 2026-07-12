using System;
using System.IO;
using DTXMania.Game.Lib.Resources;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Resources
{
    [Trait("Category", "Unit")]
    public class SkinThemeTests : IDisposable
    {
        private readonly string _tempDir;

        public SkinThemeTests()
        {
            _tempDir = Path.Combine(Path.GetTempPath(), "DTXManiaCX_Tests", Guid.NewGuid().ToString());
            Directory.CreateDirectory(_tempDir);
        }

        public void Dispose()
        {
            try { Directory.Delete(_tempDir, recursive: true); } catch { }
        }

        [Fact]
        public void GetColor_WithValidHexRgb_ShouldParse()
        {
            var theme = SkinTheme.Parse(new[] { "UI.Accent=#22D3EE" });
            Assert.Equal(new Color(0x22, 0xD3, 0xEE), theme.GetColor("UI.Accent", Color.White));
        }

        [Fact]
        public void GetColor_WithValidHexRgba_ShouldParseAlpha()
        {
            var theme = SkinTheme.Parse(new[] { "Overlay=#22D3EE80" });
            Assert.Equal(new Color(0x22, 0xD3, 0xEE, 0x80), theme.GetColor("Overlay", Color.White));
        }

        [Fact]
        public void GetColor_WithMalformedValue_ShouldReturnFallback()
        {
            var theme = SkinTheme.Parse(new[] { "UI.Accent=not-a-color" });
            Assert.Equal(Color.Magenta, theme.GetColor("UI.Accent", Color.Magenta));
        }

        [Fact]
        public void GetColor_WithMissingKey_ShouldReturnFallback()
        {
            Assert.Equal(Color.Red, SkinTheme.Empty.GetColor("Nope", Color.Red));
        }

        [Fact]
        public void GetInt_WithValidValue_ShouldParse()
        {
            var theme = SkinTheme.Parse(new[] { "Result.RankBadge.Offset=12" });
            Assert.Equal(12, theme.GetInt("Result.RankBadge.Offset", 0));
        }

        [Fact]
        public void GetInt_WithMalformedValue_ShouldReturnFallback()
        {
            var theme = SkinTheme.Parse(new[] { "K=12.7" });
            Assert.Equal(5, theme.GetInt("K", 5));
        }

        [Fact]
        public void GetFloat_WithValidValue_ShouldParseInvariantCulture()
        {
            var theme = SkinTheme.Parse(new[] { "Result.RankBadge.Scale=1.15" });
            Assert.Equal(1.15f, theme.GetFloat("Result.RankBadge.Scale", 1.0f), precision: 3);
        }

        [Fact]
        public void GetPoint_WithValidValue_ShouldParse()
        {
            var theme = SkinTheme.Parse(new[] { "SongSelect.StatusPanel.Position=580,130" });
            Assert.Equal(new Point(580, 130), theme.GetPoint("SongSelect.StatusPanel.Position", Point.Zero));
        }

        [Fact]
        public void GetPoint_WithMalformedValue_ShouldReturnFallback()
        {
            var theme = SkinTheme.Parse(new[] { "P=580" });
            Assert.Equal(new Point(1, 2), theme.GetPoint("P", new Point(1, 2)));
        }

        [Fact]
        public void Parse_ShouldIgnoreCommentsSectionsAndBlankLines()
        {
            var theme = SkinTheme.Parse(new[]
            {
                "; a comment",
                "",
                "[Palette]",
                "UI.Accent=#22D3EE",
                "[Layout]",
                "not a key value pair"
            });
            Assert.Equal(new Color(0x22, 0xD3, 0xEE), theme.GetColor("UI.Accent", Color.White));
        }

        [Fact]
        public void Parse_WithDuplicateKey_LaterValueShouldWin()
        {
            var theme = SkinTheme.Parse(new[] { "K=1", "K=2" });
            Assert.Equal(2, theme.GetInt("K", 0));
        }

        [Fact]
        public void Parse_KeysShouldBeCaseInsensitive()
        {
            var theme = SkinTheme.Parse(new[] { "ui.accent=#FF0000" });
            Assert.Equal(new Color(0xFF, 0x00, 0x00), theme.GetColor("UI.Accent", Color.White));
        }

        [Fact]
        public void Load_WithMissingFile_ShouldReturnEmpty()
        {
            var theme = SkinTheme.Load(Path.Combine(_tempDir, "does-not-exist.ini"));
            Assert.Same(SkinTheme.Empty, theme);
        }

        [Fact]
        public void Load_WithValidFile_ShouldParseValues()
        {
            var path = Path.Combine(_tempDir, "Theme.ini");
            File.WriteAllText(path, "[Palette]\nUI.Accent=#E879F9\n");
            var theme = SkinTheme.Load(path);
            Assert.Equal(new Color(0xE8, 0x79, 0xF9), theme.GetColor("UI.Accent", Color.White));
        }
    }
}

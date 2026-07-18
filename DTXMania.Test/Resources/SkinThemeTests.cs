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

        [Theory]
        [InlineData(null)]
        [InlineData("")]
        [InlineData("   ")]
        public void Load_WithNullOrWhitespacePath_ShouldReturnEmpty(string? path)
        {
            Assert.Same(SkinTheme.Empty, SkinTheme.Load(path!));
        }

        [Fact]
        public void Load_WithUnreadableFile_ShouldReturnEmpty()
        {
            // File.Exists returns true for a locked file, but File.ReadAllLines throws
            // because the stream is opened with FileShare.None. This exercises the catch
            // branch that returns Empty on IO failure.
            var path = Path.Combine(_tempDir, "Locked.ini");
            File.WriteAllText(path, "UI.Accent=#22D3EE\n");
            using var lockStream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.None);
            Assert.Same(SkinTheme.Empty, SkinTheme.Load(path));
        }

        [Theory]
        [InlineData("#FFF", 4)]   // too short
        [InlineData("#FFFFFFF", 8)] // wrong length (not 7 or 9)
        [InlineData("22D3EE", 6)]   // missing # prefix
        public void GetColor_WithInvalidFormat_ShouldReturnFallback(string raw, int _)
        {
            var theme = SkinTheme.Parse(new[] { $"UI.Accent={raw}" });
            Assert.Equal(Color.Magenta, theme.GetColor("UI.Accent", Color.Magenta));
        }

        [Fact]
        public void GetColor_WithHashButNonHexDigits_ShouldReturnFallback()
        {
            var theme = SkinTheme.Parse(new[] { "UI.Accent=#GGGGGG" });
            Assert.Equal(Color.Magenta, theme.GetColor("UI.Accent", Color.Magenta));
        }

        [Fact]
        public void GetInt_WithMissingKey_ShouldReturnFallback()
        {
            Assert.Equal(42, SkinTheme.Empty.GetInt("Nope", 42));
        }

        [Fact]
        public void GetFloat_WithMissingKey_ShouldReturnFallback()
        {
            Assert.Equal(2.5f, SkinTheme.Empty.GetFloat("Nope", 2.5f));
        }

        [Fact]
        public void GetFloat_WithMalformedValue_ShouldReturnFallback()
        {
            var theme = SkinTheme.Parse(new[] { "K=not-a-float" });
            Assert.Equal(1.0f, theme.GetFloat("K", 1.0f));
        }

        [Fact]
        public void GetPoint_WithMissingKey_ShouldReturnFallback()
        {
            Assert.Equal(new Point(3, 4), SkinTheme.Empty.GetPoint("Nope", new Point(3, 4)));
        }

        [Fact]
        public void Parse_WithLineStartingWithEquals_ShouldIgnore()
        {
            // separatorIndex == 0 (<= 0 guard) — a keyless "=value" line is skipped
            var theme = SkinTheme.Parse(new[] { "=value", "K=1" });
            Assert.Equal(1, theme.GetInt("K", 0));
        }

        [Fact]
        public void Parse_WithLineWithoutEquals_ShouldIgnore()
        {
            // separatorIndex == -1 (<= 0 guard) — a bare word with no '=' is skipped
            var theme = SkinTheme.Parse(new[] { "bareword", "K=1" });
            Assert.Equal(1, theme.GetInt("K", 0));
        }

        [Fact]
        public void GetString_WithMissingKey_ShouldReturnFallback()
        {
            Assert.Equal("fallback", SkinTheme.Empty.GetString("Result.ValueFontFamily", "fallback"));
        }

        [Fact]
        public void GetString_WithPresentKey_ShouldReturnValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.ValueFontFamily=Orbitron" });
            Assert.Equal("Orbitron", theme.GetString("Result.ValueFontFamily", string.Empty));
        }
    }
}

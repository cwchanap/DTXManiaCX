using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for SongTransitionStage focusing on pure logic methods
    /// that do not require graphics initialization.
    /// </summary>
    [Trait("Category", "Unit")]
    public class SongTransitionStageTests
    {
        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new SongTransitionStage(null));
        }

        [Fact]
        public void Constructor_WithValidGame_ShouldNotThrow()
        {
            var game = CreateUninitializedGame();
            var stage = new SongTransitionStage(game);
            Assert.NotNull(stage);
        }

        #endregion

        #region Type Property Tests

        [Fact]
        public void Type_ShouldReturnSongTransition()
        {
#pragma warning disable SYSLIB0050
            var stage = (SongTransitionStage)FormatterServices.GetUninitializedObject(typeof(SongTransitionStage));
#pragma warning restore SYSLIB0050
            Assert.Equal(StageType.SongTransition, stage.Type);
        }

        #endregion

        #region GetDifficultyName Tests (via reflection)

        [Theory]
        [InlineData(0, "Basic")]
        [InlineData(1, "Advanced")]
        [InlineData(2, "Extreme")]
        [InlineData(3, "Master")]
        [InlineData(4, "Ultimate")]
        public void GetDifficultyName_ValidDifficulty_ShouldReturnCorrectName(int difficulty, string expectedName)
        {
#pragma warning disable SYSLIB0050
            var stage = (SongTransitionStage)FormatterServices.GetUninitializedObject(typeof(SongTransitionStage));
#pragma warning restore SYSLIB0050

            var result = InvokePrivateMethod<string>(stage, "GetDifficultyName", difficulty);

            Assert.Equal(expectedName, result);
        }

        [Theory]
        [InlineData(-1)]
        [InlineData(5)]
        [InlineData(100)]
        [InlineData(int.MinValue)]
        public void GetDifficultyName_InvalidDifficulty_ShouldReturnUnknown(int difficulty)
        {
#pragma warning disable SYSLIB0050
            var stage = (SongTransitionStage)FormatterServices.GetUninitializedObject(typeof(SongTransitionStage));
#pragma warning restore SYSLIB0050

            var result = InvokePrivateMethod<string>(stage, "GetDifficultyName", difficulty);

            Assert.Equal("Unknown", result);
        }

        #endregion

        #region GetCurrentDifficultyLevel Tests (via reflection)

        [Fact]
        public void GetCurrentDifficultyLevel_WhenSelectedSongIsNull_ShouldReturnZero()
        {
#pragma warning disable SYSLIB0050
            var stage = (SongTransitionStage)FormatterServices.GetUninitializedObject(typeof(SongTransitionStage));
#pragma warning restore SYSLIB0050

            // _selectedSong is null by default (uninitialized object)
            var result = InvokePrivateMethod<float>(stage, "GetCurrentDifficultyLevel");

            Assert.Equal(0.0f, result);
        }

        #endregion

        #region Inheritance and Interface Tests

        [Fact]
        public void SongTransitionStage_ShouldInheritFromBaseStage()
        {
            Assert.True(typeof(BaseStage).IsAssignableFrom(typeof(SongTransitionStage)));
        }

        [Fact]
        public void SongTransitionStage_ShouldImplementIStage()
        {
            Assert.True(typeof(IStage).IsAssignableFrom(typeof(SongTransitionStage)));
        }

        [Fact]
        public void InitialPhase_ShouldBeInactive()
        {
            var game = CreateUninitializedGame();
            var stage = new SongTransitionStage(game);
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
        }

        #endregion

        #region Helper Methods

        private static BaseGame CreateUninitializedGame()
        {
#pragma warning disable SYSLIB0050
            return (BaseGame)FormatterServices.GetUninitializedObject(typeof(BaseGame));
#pragma warning restore SYSLIB0050
        }

        private static T? InvokePrivateMethod<T>(object target, string methodName, params object[] args)
        {
            var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.NotNull(method);
            var result = method!.Invoke(target, args);
            if (result is null) return default;
            return (T)result;
        }

        #endregion

        #region Theme Layout Tests

        [Fact]
        public void ResolveDifficultyPanelColor_WithEmptyTheme_ShouldKeepNxGray()
        {
            Assert.Equal(Microsoft.Xna.Framework.Color.Gray,
                SongTransitionStage.ResolveDifficultyPanelColor(DTXMania.Game.Lib.Resources.SkinTheme.Empty));
        }

        [Fact]
        public void ResolveDifficultyPanelColor_WithThemedColor_ShouldUseThemedValue()
        {
            var theme = DTXMania.Game.Lib.Resources.SkinTheme.Parse(
                new[] { "Transition.DifficultyPanel=#0F172AB4" });

            Assert.Equal(new Microsoft.Xna.Framework.Color(0x0F, 0x17, 0x2A, 0xB4),
                SongTransitionStage.ResolveDifficultyPanelColor(theme));
        }

        [Fact]
        public void ResolveTitleMaxWidth_WithEmptyTheme_ShouldBeUnlimited()
        {
            Assert.Equal(0, SongTransitionStage.ResolveTitleMaxWidth(
                DTXMania.Game.Lib.Resources.SkinTheme.Empty));
        }

        [Fact]
        public void ResolveTitleMaxWidth_WithThemedWidth_ShouldUseThemedValue()
        {
            // CX Neon caps the title short of the rotated jacket art at x~620.
            var theme = DTXMania.Game.Lib.Resources.SkinTheme.Parse(
                new[] { "Transition.TitleMaxWidth=430" });

            Assert.Equal(430, SongTransitionStage.ResolveTitleMaxWidth(theme));
        }

        [Fact]
        public void ResolveTitleFontFamily_WithEmptyTheme_ShouldBeEmpty()
        {
            Assert.Equal(string.Empty, SongTransitionStage.ResolveTitleFontFamily(
                DTXMania.Game.Lib.Resources.SkinTheme.Empty));
        }

        [Fact]
        public void ResolveTitleFontFamily_WithThemedFamily_ShouldUseThemedValue()
        {
            var theme = DTXMania.Game.Lib.Resources.SkinTheme.Parse(
                new[] { "Transition.TitleFontFamily=Orbitron" });

            Assert.Equal("Orbitron", SongTransitionStage.ResolveTitleFontFamily(theme));
        }

        [Fact]
        public void ResolveTitleFontSize_WithEmptyTheme_ShouldKeepNxSize()
        {
            Assert.Equal(DTXMania.Game.Lib.UI.Layout.SongTransitionUILayout.SongTitle.FontSize,
                SongTransitionStage.ResolveTitleFontSize(
                    DTXMania.Game.Lib.Resources.SkinTheme.Empty));
        }

        [Fact]
        public void ResolveArtistFontFamilyAndSize_WithThemedValues_ShouldUseThemedValues()
        {
            var theme = DTXMania.Game.Lib.Resources.SkinTheme.Parse(
                new[] { "Transition.ArtistFontFamily=Orbitron", "Transition.ArtistFontSize=24" });

            Assert.Equal("Orbitron", SongTransitionStage.ResolveArtistFontFamily(theme));
            Assert.Equal(24, SongTransitionStage.ResolveArtistFontSize(theme));
        }

        [Fact]
        public void ResolveArtistFontSize_WithEmptyTheme_ShouldKeepNxSize()
        {
            Assert.Equal(DTXMania.Game.Lib.UI.Layout.SongTransitionUILayout.Artist.FontSize,
                SongTransitionStage.ResolveArtistFontSize(
                    DTXMania.Game.Lib.Resources.SkinTheme.Empty));
        }

        [Fact]
        public void ResolveLevelFontFamilyAndSize_WithEmptyTheme_ShouldKeepNxSerif()
        {
            Assert.Equal("NotoSerifJP", SongTransitionStage.ResolveLevelFontFamily(
                DTXMania.Game.Lib.Resources.SkinTheme.Empty));
            Assert.Equal(24, SongTransitionStage.ResolveLevelFontSize(
                DTXMania.Game.Lib.Resources.SkinTheme.Empty));
        }

        [Fact]
        public void ResolveLevelFontFamilyAndSize_WithThemedValues_ShouldUseThemedValues()
        {
            var theme = DTXMania.Game.Lib.Resources.SkinTheme.Parse(
                new[] { "Transition.LevelFontFamily=Orbitron", "Transition.LevelFontSize=24" });

            Assert.Equal("Orbitron", SongTransitionStage.ResolveLevelFontFamily(theme));
            Assert.Equal(24, SongTransitionStage.ResolveLevelFontSize(theme));
        }

        [Fact]
        public void ResolveLevelFontFamily_WithEmptyThemedValue_ShouldFallBackToNotoSerif()
        {
            // A malformed `Transition.LevelFontFamily=` line yields an empty
            // string from SkinTheme.GetString; LoadFont rejects empty paths,
            // which would leave the level number undrawn. Treat empty as the
            // NX default instead.
            var theme = DTXMania.Game.Lib.Resources.SkinTheme.Parse(
                new[] { "Transition.LevelFontFamily=" });

            Assert.Equal("NotoSerifJP", SongTransitionStage.ResolveLevelFontFamily(theme));
        }

        #endregion

        #region Display Font Eligibility Tests

        [Theory]
        [InlineData("My Hope Is Gone", true)]
        [InlineData("AC/DC - T.N.T. (Live '77)", true)]
        [InlineData("蒼穹への翔歌", false)]
        [InlineData("NPC feat.ねんね", false)]
        public void IsAsciiDisplayable_ShouldDetectLatinOnlyText(string text, bool expected)
        {
            Assert.Equal(expected, SongTransitionStage.IsAsciiDisplayable(text));
        }

        #endregion

        #region Title Layout Tests

        // measure: 10px per character — keeps the expected widths easy to derive.
        private static float MeasureTenPerChar(string s) => s.Length * 10f;

        [Fact]
        public void ComputeTitleLayout_TitleFitting_ShouldReturnSingleLineFullScale()
        {
            var (lines, scale) = SongTransitionStage.ComputeTitleLayout(
                MeasureTenPerChar, "Short", 430);

            Assert.Equal(new[] { "Short" }, lines);
            Assert.Equal(1f, scale);
        }

        [Fact]
        public void ComputeTitleLayout_NoWidthLimit_ShouldReturnSingleLineFullScale()
        {
            var longTitle = new string('x', 200);

            var (lines, scale) = SongTransitionStage.ComputeTitleLayout(
                MeasureTenPerChar, longTitle, 0);

            Assert.Equal(new[] { longTitle }, lines);
            Assert.Equal(1f, scale);
        }

        [Fact]
        public void ComputeTitleLayout_SlightlyWide_ShouldShrinkSingleLine()
        {
            // 50 chars = 500px into 430px → scale 0.86, above the 0.75 single-line floor.
            var title = new string('a', 50);

            var (lines, scale) = SongTransitionStage.ComputeTitleLayout(
                MeasureTenPerChar, title, 430);

            Assert.Equal(new[] { title }, lines);
            Assert.Equal(0.86f, scale, 2);
        }

        [Fact]
        public void ComputeTitleLayout_TooWideForShrink_ShouldWrapToTwoFullScaleLines()
        {
            // 60 chars = 600px → single-line scale 0.716 < 0.75 floor → word wrap.
            var (lines, scale) = SongTransitionStage.ComputeTitleLayout(
                MeasureTenPerChar, "aaaaaaaaaaaaaaaaaaaaaaaaaaaaa bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", 430);

            Assert.Equal(2, lines.Length);
            Assert.Equal(1f, scale);
            Assert.Equal("aaaaaaaaaaaaaaaaaaaaaaaaaaaaa", lines[0]);
            Assert.Equal("bbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", lines[1]);
        }

        [Fact]
        public void ComputeTitleLayout_NoSpaces_ShouldWrapByCharacters()
        {
            // CJK-style titles have no spaces; the wrap must fall back to
            // character breaking and preserve every character.
            var title = new string('字', 60);

            var (lines, scale) = SongTransitionStage.ComputeTitleLayout(
                MeasureTenPerChar, title, 430);

            Assert.True(lines.Length >= 2);
            Assert.Equal(title, string.Concat(lines));
            Assert.All(lines, line => Assert.True(MeasureTenPerChar(line) * scale <= 430f));
        }

        [Fact]
        public void ComputeTitleLayout_ManyWords_ShouldShrinkSharedScaleToStayWithinTwoLines()
        {
            // Three 25-char words (250px each) wrap to three lines at full scale,
            // so the layout shrinks the shared scale until two lines suffice
            // instead of spilling to a third row.
            var title = "aaaaaaaaaaaaaaaaaaaaaaaaa bbbbbbbbbbbbbbbbbbbbbbbbb ccccccccccccccccccccccccc";

            var (lines, scale) = SongTransitionStage.ComputeTitleLayout(
                MeasureTenPerChar, title, 430);

            Assert.Equal(2, lines.Length);
            Assert.True(scale < 1f);
            Assert.True(scale >= 0.5f);
            Assert.All(lines, line => Assert.True(MeasureTenPerChar(line) * scale <= 430f + 0.01f));
        }

        [Fact]
        public void ComputeTitleLayout_AlwaysPreservesEveryCharacter()
        {
            var title = "The Quick Brown Fox Jumps Over The Lazy Dog And Keeps On Running Forever";

            var (lines, _) = SongTransitionStage.ComputeTitleLayout(
                MeasureTenPerChar, title, 430);

            Assert.Equal(title.Replace(" ", string.Empty),
                string.Concat(lines).Replace(" ", string.Empty));
        }

        [Fact]
        public void WrapToWidth_WithRepeatedSpaces_ShouldPreserveSeparatorsOnKeptLines()
        {
            // Regression guard: the previous implementation split on ' ' with
            // RemoveEmptyEntries and rejoined with a single space, collapsing
            // repeated spaces in the displayed title even though the helper's
            // contract is "Never drops characters". The wrap must preserve the
            // original whitespace runs between words on a kept line; only the
            // separator at a line break is consumed by the break itself.
            var lines = SongTransitionStage.WrapToWidth(
                MeasureTenPerChar, "foo  bar baz", 89);

            Assert.Equal(new[] { "foo  bar", "baz" }, lines);
        }

        [Fact]
        public void WrapToWidth_WithTrailingSpaces_ShouldDropLeadingWhitespaceOnWrappedLines()
        {
            // Leading whitespace on a wrapped line is invisible and would only
            // force an immediate re-wrap, so the wrap drops it. The separator
            // at a line break is consumed by the break itself.
            var lines = SongTransitionStage.WrapToWidth(
                MeasureTenPerChar, "foo   bar", 30);

            // "foo" (30) fits on line 1; "bar" (30) on line 2. The three-space
            // separator is consumed by the line break.
            Assert.Equal(new[] { "foo", "bar" }, lines);
        }

        #endregion
    }
}

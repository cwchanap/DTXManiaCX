using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage.Result;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// The result-screen song title/artist draw at NX-absolute Y positions that
    /// cross the CX Neon jacket panel's bottom border; skins may shift them via
    /// "Result.TitleY" / "Result.ArtistY". Themeless (NX) skins keep NX values.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ResultScreenRendererThemeLayoutTests
    {
        [Fact]
        public void ResolveTitlePosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(ResultUILayout.SongInfo.TitlePosition,
                ResultScreenRenderer.ResolveTitlePosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveTitlePosition_WithThemedY_ShouldShiftYOnly()
        {
            var theme = SkinTheme.Parse(new[] { "Result.TitleY=642" });

            var pos = ResultScreenRenderer.ResolveTitlePosition(theme);

            Assert.Equal(new Vector2(ResultUILayout.SongInfo.TitlePosition.X, 642f), pos);
        }

        [Fact]
        public void ResolveArtistPosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(ResultUILayout.SongInfo.ArtistPosition,
                ResultScreenRenderer.ResolveArtistPosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveArtistPosition_WithThemedY_ShouldShiftYOnly()
        {
            var theme = SkinTheme.Parse(new[] { "Result.ArtistY=674" });

            var pos = ResultScreenRenderer.ResolveArtistPosition(theme);

            Assert.Equal(new Vector2(ResultUILayout.SongInfo.ArtistPosition.X, 674f), pos);
        }

        [Fact]
        public void ResolveLevelPosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(ResultUILayout.SkillPanel.LevelPosition,
                ResultScreenRenderer.ResolveLevelPosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveLevelPosition_WithThemedXY_ShouldShiftBoth()
        {
            var theme = SkinTheme.Parse(new[] { "Result.LevelX=268", "Result.LevelY=516" });

            Assert.Equal(new Vector2(268f, 516f), ResultScreenRenderer.ResolveLevelPosition(theme));
        }

        [Fact]
        public void ResolvePlayingSkillPosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(ResultUILayout.SkillPanel.PlayingSkillPosition,
                ResultScreenRenderer.ResolvePlayingSkillPosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolvePlayingSkillPosition_WithThemedXY_ShouldShiftBoth()
        {
            var theme = SkinTheme.Parse(new[] { "Result.PlayingSkillX=268", "Result.PlayingSkillY=546" });

            Assert.Equal(new Vector2(268f, 546f), ResultScreenRenderer.ResolvePlayingSkillPosition(theme));
        }

        [Fact]
        public void ResolveFailedTextPosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(ResultUILayout.ResultPlate.FailedTextPosition,
                ResultScreenRenderer.ResolveFailedTextPosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveFailedTextPosition_WithThemedXY_ShouldShiftBoth()
        {
            var theme = SkinTheme.Parse(new[] { "Result.FailedTextX=330", "Result.FailedTextY=130" });

            Assert.Equal(new Vector2(330f, 130f), ResultScreenRenderer.ResolveFailedTextPosition(theme));
        }

        [Fact]
        public void ResolveScorePosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(ResultUILayout.Score.Position,
                ResultScreenRenderer.ResolveScorePosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveScorePosition_WithThemedXY_ShouldShiftBoth()
        {
            var theme = SkinTheme.Parse(new[] { "Result.ScoreX=268", "Result.ScoreY=263" });

            Assert.Equal(new Vector2(268f, 263f), ResultScreenRenderer.ResolveScorePosition(theme));
        }

        [Fact]
        public void ResolveRankBadgePosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(ResultUILayout.Rank.BadgePosition,
                ResultScreenRenderer.ResolveRankBadgePosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveRankBadgePosition_WithThemedXY_ShouldShiftBoth()
        {
            var theme = SkinTheme.Parse(new[] { "Result.RankBadgeX=980", "Result.RankBadgeY=25" });

            Assert.Equal(new Vector2(980f, 25f), ResultScreenRenderer.ResolveRankBadgePosition(theme));
        }

        [Fact]
        public void ResolvePlatePosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(ResultUILayout.ResultPlate.Position,
                ResultScreenRenderer.ResolvePlatePosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolvePlatePosition_WithThemedXY_ShouldShiftBoth()
        {
            var theme = SkinTheme.Parse(new[] { "Result.PlateX=340", "Result.PlateY=30" });

            Assert.Equal(new Vector2(340f, 30f), ResultScreenRenderer.ResolvePlatePosition(theme));
        }

        [Fact]
        public void ResolveLargeFontSize_WithEmptyTheme_ShouldKeepNxSize()
        {
            Assert.Equal(ResultUILayout.Fonts.Large,
                ResultScreenRenderer.ResolveLargeFontSize(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveLargeFontSize_WithThemedSize_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.FontLarge=24" });

            Assert.Equal(24, ResultScreenRenderer.ResolveLargeFontSize(theme));
        }

        [Fact]
        public void ResolveNormalFontSize_WithEmptyTheme_ShouldKeepNxSize()
        {
            Assert.Equal(ResultUILayout.Fonts.Normal,
                ResultScreenRenderer.ResolveNormalFontSize(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveNormalFontSize_WithThemedSize_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.FontNormal=18" });

            Assert.Equal(18, ResultScreenRenderer.ResolveNormalFontSize(theme));
        }

        [Fact]
        public void ResolveGameSkillPosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(ResultUILayout.SkillPanel.GameSkillPosition,
                ResultScreenRenderer.ResolveGameSkillPosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveGameSkillPosition_WithThemedXY_ShouldShiftBoth()
        {
            var theme = SkinTheme.Parse(new[] { "Result.GameSkillX=268", "Result.GameSkillY=628" });

            Assert.Equal(new Vector2(268f, 628f), ResultScreenRenderer.ResolveGameSkillPosition(theme));
        }

        [Fact]
        public void ResolveJacketPanelPosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(ResultUILayout.Jacket.PanelPosition,
                ResultScreenRenderer.ResolveJacketPanelPosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveJacketPanelPosition_WithThemedXY_ShouldShiftBoth()
        {
            var theme = SkinTheme.Parse(new[] { "Result.JacketPanelX=467", "Result.JacketPanelY=260" });

            Assert.Equal(new Vector2(467f, 260f), ResultScreenRenderer.ResolveJacketPanelPosition(theme));
        }

        [Fact]
        public void ResolveJacketPreviewPosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(
                new Vector2(
                    ResultUILayout.Jacket.PreviewDestination.X,
                    ResultUILayout.Jacket.PreviewDestination.Y),
                ResultScreenRenderer.ResolveJacketPreviewPosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveJacketPreviewPosition_WithThemedXY_ShouldShiftBoth()
        {
            var theme = SkinTheme.Parse(new[] { "Result.JacketPreviewX=519", "Result.JacketPreviewY=311" });

            Assert.Equal(new Vector2(519f, 311f), ResultScreenRenderer.ResolveJacketPreviewPosition(theme));
        }

        [Fact]
        public void ResolveNewRecordPosition_WithEmptyTheme_ShouldKeepNxPosition()
        {
            Assert.Equal(ResultUILayout.NewRecord.BadgePosition,
                ResultScreenRenderer.ResolveNewRecordPosition(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveNewRecordPosition_WithThemedXY_ShouldShiftBoth()
        {
            var theme = SkinTheme.Parse(new[] { "Result.NewRecordX=340", "Result.NewRecordY=642" });

            Assert.Equal(new Vector2(340f, 642f), ResultScreenRenderer.ResolveNewRecordPosition(theme));
        }

        [Fact]
        public void ResolveTitleCenterX_WithEmptyTheme_ShouldBeDisabled()
        {
            Assert.Equal(0, ResultScreenRenderer.ResolveTitleCenterX(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveTitleCenterX_WithThemedValue_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.TitleCenterX=640" });

            Assert.Equal(640, ResultScreenRenderer.ResolveTitleCenterX(theme));
        }

        [Fact]
        public void ResolveArtistCenterX_WithEmptyTheme_ShouldBeDisabled()
        {
            Assert.Equal(0, ResultScreenRenderer.ResolveArtistCenterX(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveArtistCenterX_WithThemedValue_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.ArtistCenterX=640" });

            Assert.Equal(640, ResultScreenRenderer.ResolveArtistCenterX(theme));
        }

        [Fact]
        public void ApplyCenterX_WithZeroCenter_ShouldKeepLeftAlignedX()
        {
            Assert.Equal(500f, ResultScreenRenderer.ApplyCenterX(500f, 0, 200f));
        }

        [Fact]
        public void ApplyCenterX_WithCenter_ShouldCenterTextOnAxis()
        {
            Assert.Equal(540f, ResultScreenRenderer.ApplyCenterX(500f, 640, 200f));
        }

        [Fact]
        public void ResolveNormalFontScale_WithEmptyTheme_ShouldBeUnscaled()
        {
            Assert.Equal(1.0f, ResultScreenRenderer.ResolveNormalFontScale(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveNormalFontScale_WithThemedPercent_ShouldScaleDown()
        {
            var theme = SkinTheme.Parse(new[] { "Result.FontNormalScale=75" });

            Assert.Equal(0.75f, ResultScreenRenderer.ResolveNormalFontScale(theme));
        }

        [Fact]
        public void ResolveScoreScale_WithEmptyTheme_ShouldBeUnscaled()
        {
            Assert.Equal(1.0f, ResultScreenRenderer.ResolveScoreScale(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveScoreScale_WithThemedPercent_ShouldScaleDown()
        {
            var theme = SkinTheme.Parse(new[] { "Result.ScoreScale=75" });

            Assert.Equal(0.75f, ResultScreenRenderer.ResolveScoreScale(theme));
        }

        [Fact]
        public void ResolveScoreColor_WithEmptyTheme_ShouldKeepNxWhite()
        {
            Assert.Equal(Color.White, ResultScreenRenderer.ResolveScoreColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveScoreColor_WithThemedColor_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.ScoreText=#22D3EE" });

            Assert.Equal(new Color(0x22, 0xD3, 0xEE), ResultScreenRenderer.ResolveScoreColor(theme));
        }

        [Fact]
        public void ResolveValueRightX_WithEmptyTheme_ShouldBeDisabled()
        {
            Assert.Equal(0, ResultScreenRenderer.ResolveValueRightX(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveValueRightX_WithThemedEdge_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.ValueRightX=421" });

            Assert.Equal(421, ResultScreenRenderer.ResolveValueRightX(theme));
        }

        [Fact]
        public void ResolveCountRightX_WithEmptyTheme_ShouldBeDisabled()
        {
            Assert.Equal(0, ResultScreenRenderer.ResolveCountRightX(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveCountRightX_WithThemedEdge_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.CountRightX=336" });

            Assert.Equal(336, ResultScreenRenderer.ResolveCountRightX(theme));
        }

        [Fact]
        public void ResolvePercentRightX_WithEmptyTheme_ShouldBeDisabled()
        {
            Assert.Equal(0, ResultScreenRenderer.ResolvePercentRightX(SkinTheme.Empty));
        }

        [Fact]
        public void ResolvePercentRightX_WithThemedEdge_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.PercentRightX=421" });

            Assert.Equal(421, ResultScreenRenderer.ResolvePercentRightX(theme));
        }

        [Fact]
        public void ApplyRightX_WithZeroEdge_ShouldKeepLeftAlignedX()
        {
            Assert.Equal(500f, ResultScreenRenderer.ApplyRightX(500f, 0, 60f));
        }

        [Fact]
        public void ApplyRightX_WithEdge_ShouldAnchorTextRightEdge()
        {
            Assert.Equal(361f, ResultScreenRenderer.ApplyRightX(500f, 421, 60f));
        }

        [Fact]
        public void ResolveCountColor_WithEmptyTheme_ShouldKeepNxWhite()
        {
            Assert.Equal(Color.White, ResultScreenRenderer.ResolveCountColor(SkinTheme.Empty, "Perfect"));
        }

        [Fact]
        public void ResolveCountColor_WithThemedJudgement_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.PerfectText=#FACC15" });

            Assert.Equal(new Color(0xFA, 0xCC, 0x15), ResultScreenRenderer.ResolveCountColor(theme, "Perfect"));
        }

        [Fact]
        public void ResolveCountColor_WithThemedComboKey_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.ComboText=#22D3EE" });

            Assert.Equal(new Color(0x22, 0xD3, 0xEE), ResultScreenRenderer.ResolveCountColor(theme, "Combo"));
        }

        [Fact]
        public void ResolvePercentColor_WithEmptyTheme_ShouldKeepNxWhite()
        {
            Assert.Equal(Color.White, ResultScreenRenderer.ResolvePercentColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolvePercentColor_WithThemedColor_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.PercentText=#94A3B8" });

            Assert.Equal(new Color(0x94, 0xA3, 0xB8), ResultScreenRenderer.ResolvePercentColor(theme));
        }

        [Fact]
        public void ResolveValueColor_WithEmptyTheme_ShouldKeepNxWhite()
        {
            Assert.Equal(Color.White, ResultScreenRenderer.ResolveValueColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveValueColor_WithThemedColor_ShouldUseThemedValue()
        {
            // Level / play / skill were hardcoded white, which outshines a themed
            // primary step and makes them louder than the score.
            var theme = SkinTheme.Parse(new[] { "Result.ValueText=#DBEBF0" });

            Assert.Equal(new Color(0xDB, 0xEB, 0xF0), ResultScreenRenderer.ResolveValueColor(theme));
        }

        [Fact]
        public void ResolveTitleColor_WithEmptyTheme_ShouldKeepNxWhite()
        {
            Assert.Equal(Color.White, ResultScreenRenderer.ResolveTitleColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveTitleColor_WithThemedColor_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.TitleText=#F1F5F9" });

            Assert.Equal(new Color(0xF1, 0xF5, 0xF9), ResultScreenRenderer.ResolveTitleColor(theme));
        }

        [Fact]
        public void ResolveArtistColor_WithEmptyTheme_ShouldKeepNxLightGray()
        {
            Assert.Equal(Color.LightGray, ResultScreenRenderer.ResolveArtistColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveArtistColor_WithThemedColor_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.ArtistText=#CBD5E1" });

            Assert.Equal(new Color(0xCB, 0xD5, 0xE1), ResultScreenRenderer.ResolveArtistColor(theme));
        }

        [Fact]
        public void ResolveValueFontFamily_WithEmptyTheme_ShouldBeDisabled()
        {
            Assert.Equal(string.Empty, ResultScreenRenderer.ResolveValueFontFamily(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveValueFontFamily_WithThemedFamily_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.FontValueFamily=Orbitron" });

            Assert.Equal("Orbitron", ResultScreenRenderer.ResolveValueFontFamily(theme));
        }

        [Fact]
        public void ResolveValueFontSize_WithEmptyTheme_ShouldDefaultTo18()
        {
            Assert.Equal(18, ResultScreenRenderer.ResolveValueFontSize(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveValueFontSize_WithThemedSize_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.FontValueSize=20" });

            Assert.Equal(20, ResultScreenRenderer.ResolveValueFontSize(theme));
        }

        [Fact]
        public void ResolveCountFontSize_WithEmptyTheme_ShouldDefaultTo14()
        {
            Assert.Equal(14, ResultScreenRenderer.ResolveCountFontSize(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveCountFontSize_WithThemedSize_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.FontValueSmallSize=13" });

            Assert.Equal(13, ResultScreenRenderer.ResolveCountFontSize(theme));
        }

        [Fact]
        public void ResolveCountYOffset_WithEmptyTheme_ShouldBeZero()
        {
            Assert.Equal(0, ResultScreenRenderer.ResolveCountYOffset(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveCountYOffset_WithThemedOffset_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.CountYOffset=3" });

            Assert.Equal(3, ResultScreenRenderer.ResolveCountYOffset(theme));
        }

        [Fact]
        public void ResolveLevelLabelRightX_WithEmptyTheme_ShouldBeHidden()
        {
            // 0 = the difficulty-tier name is not drawn (NX behavior).
            Assert.Equal(0, ResultScreenRenderer.ResolveLevelLabelRightX(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveLevelLabelRightX_WithThemedEdge_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.LevelLabelRightX=360" });

            Assert.Equal(360, ResultScreenRenderer.ResolveLevelLabelRightX(theme));
        }

        [Fact]
        public void ResolveLevelLabelColor_WithEmptyTheme_ShouldBeLightGray()
        {
            Assert.Equal(Color.LightGray,
                ResultScreenRenderer.ResolveLevelLabelColor(SkinTheme.Empty));
        }

        [Fact]
        public void ResolveLevelLabelColor_WithThemedColor_ShouldUseThemedValue()
        {
            var theme = SkinTheme.Parse(new[] { "Result.LevelLabelText=#94A3B8" });

            Assert.Equal(new Color(0x94, 0xA3, 0xB8),
                ResultScreenRenderer.ResolveLevelLabelColor(theme));
        }
    }
}

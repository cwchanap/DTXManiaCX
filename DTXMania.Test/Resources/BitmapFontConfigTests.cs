using DTXMania.Game.Lib.Resources;
using DTXMania.Test.TestData;
using Moq;

namespace DTXMania.Test.Resources;

public class BitmapFontConfigTests
{
    [Fact]
    public void CreateLevelNumberFontConfig_ShouldUseVariableWidthsForDigitsAndDot()
    {
        var config = BitmapFont.CreateLevelNumberFontConfig();

        Assert.Equal("0123456789.", config.DisplayableCharacters);
        Assert.Equal(new[] { 20, 20, 20, 20, 20, 20, 20, 20, 20, 20, 6 }, config.CharacterWidths);
        Assert.Equal(26, config.CharacterHeight);
        Assert.Equal(new[] { 100, 100, 100, 100, 100, 100, 100, 100, 100, 100, 30 }, config.SourceCharacterWidths);
        Assert.Equal(130, config.SourceCharacterHeight);
        Assert.Equal(new[] { TexturePath.LevelNumberFont }, config.TexturePaths);
        Assert.True(config.UseVariableWidths);
    }

    [Fact]
    public void CreateJudgementTextFontConfig_ShouldReuseConsoleLayoutWithAdjustedVisibility()
    {
        var consoleConfig = BitmapFont.CreateConsoleFontConfig();
        var judgementConfig = BitmapFont.CreateJudgementTextFontConfig();

        Assert.Equal(consoleConfig.DisplayableCharacters, judgementConfig.DisplayableCharacters);
        Assert.Equal(new[] { 12 }, judgementConfig.CharacterWidths);
        Assert.Equal(18, judgementConfig.CharacterHeight);
        Assert.Equal(consoleConfig.SourceCharacterWidths, judgementConfig.SourceCharacterWidths);
        Assert.Equal(consoleConfig.SourceCharacterHeight, judgementConfig.SourceCharacterHeight);
        Assert.Equal(consoleConfig.TexturePaths, judgementConfig.TexturePaths);
        Assert.Equal(consoleConfig.UseVariableWidths, judgementConfig.UseVariableWidths);
        Assert.Equal(consoleConfig.CharactersPerRow, judgementConfig.CharactersPerRow);
    }

    [Fact]
    public void GetCharacterWidth_WhenVariableWidthIndexIsOutOfRange_ReturnsFirstWidth()
    {
        using var font = CreateTestBitmapFont(BitmapFont.CreateLevelNumberFontConfig());

        var width = ReflectionHelpers.InvokePrivateMethod<int>(font, "GetCharacterWidth", 99);

        Assert.Equal(20, width);
    }

    [Fact]
    public void GetSourceCharacterWidth_WhenVariableWidthIndexIsOutOfRange_ReturnsFirstSourceWidth()
    {
        using var font = CreateTestBitmapFont(BitmapFont.CreateLevelNumberFontConfig());

        var width = ReflectionHelpers.InvokePrivateMethod<int>(font, "GetSourceCharacterWidth", 99);

        Assert.Equal(100, width);
    }

    private static BitmapFont CreateTestBitmapFont(BitmapFont.BitmapFontConfig config)
    {
        var resourceManager = new Mock<IResourceManager>().Object;
        return new BitmapFont(resourceManager, config, true);
    }
}

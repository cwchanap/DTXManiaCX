using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage;
using Xunit;
using LaneType = DTXMania.Game.Lib.UI.Layout.PerformanceUILayout.LaneType;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public sealed class DrumPadSystemKeysTests
{
    [Theory]
    [InlineData(LaneType.HT, InputCommandType.MoveUp)]
    [InlineData(LaneType.LT, InputCommandType.MoveDown)]
    [InlineData(LaneType.CY, InputCommandType.Activate)]
    [InlineData(LaneType.RD, InputCommandType.Activate)]
    [InlineData(LaneType.LC, InputCommandType.Back)]
    public void GetCommand_InMenus_MapsPadToSystemKey(LaneType lane, InputCommandType expected)
    {
        Assert.Equal(expected, DrumPadSystemKeys.GetCommand(StageType.SongSelect, (int)lane));
        Assert.Equal(expected, DrumPadSystemKeys.GetCommand(StageType.Config, (int)lane));
    }

    [Theory]
    [InlineData(LaneType.HH)]
    [InlineData(LaneType.LP)]
    [InlineData(LaneType.SN)]
    [InlineData(LaneType.DB)]
    [InlineData(LaneType.FT)]
    public void GetCommand_ForPadsWithoutSystemKey_ReturnsNull(LaneType lane)
    {
        Assert.Null(DrumPadSystemKeys.GetCommand(StageType.SongSelect, (int)lane));
    }

    [Theory]
    [InlineData(StageType.Performance)]
    [InlineData(StageType.DrumConfig)]
    public void GetCommand_DuringGameplayOrDrumBinding_IgnoresPads(StageType stage)
    {
        Assert.Null(DrumPadSystemKeys.GetCommand(stage, (int)LaneType.LC));
        Assert.Null(DrumPadSystemKeys.GetCommand(stage, (int)LaneType.CY));
    }

    [Fact]
    public void GetCommand_OnTitle_LeftCymbalDoesNotQuit()
    {
        Assert.Null(DrumPadSystemKeys.GetCommand(StageType.Title, (int)LaneType.LC));
        Assert.Equal(InputCommandType.Activate, DrumPadSystemKeys.GetCommand(StageType.Title, (int)LaneType.CY));
    }

    [Fact]
    public void GetCommand_WithNoStage_ReturnsNull()
    {
        Assert.Null(DrumPadSystemKeys.GetCommand(null, (int)LaneType.CY));
    }
}

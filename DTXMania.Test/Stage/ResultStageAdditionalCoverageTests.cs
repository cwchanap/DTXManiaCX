using System;
using System.Collections.Generic;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage;

[Trait("Category", "Unit")]
public class ResultStageAdditionalCoverageTests
{
    [Fact]
    public void HandleInput_WithNullInputManager_ShouldReturn()
    {
#pragma warning disable SYSLIB0050
        var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
        SetPrivateField(stage, "_inputManager", null);

        var exception = Record.Exception(() => InvokePrivateMethod(stage, "HandleInput"));

        Assert.Null(exception);
    }

    [Fact]
    public void ExecuteInputCommand_Activate_ShouldTransitionToSongSelect()
    {
#pragma warning disable SYSLIB0050
        var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
        var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
        SetPrivateField(stage, "_game", game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;

        var command = new DTXMania.Game.Lib.Input.InputCommand(
            DTXMania.Game.Lib.Input.InputCommandType.Activate, 0.0);

        InvokePrivateMethod(stage, "ExecuteInputCommand", command);

        stageManager.Verify(
            m => m.ChangeStage(
                StageType.SongSelect,
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Once);
    }

    [Fact]
    public void ExecuteInputCommand_Back_ShouldTransitionToSongSelect()
    {
#pragma warning disable SYSLIB0050
        var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
        var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
        SetPrivateField(stage, "_game", game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;

        var command = new DTXMania.Game.Lib.Input.InputCommand(
            DTXMania.Game.Lib.Input.InputCommandType.Back, 0.0);

        InvokePrivateMethod(stage, "ExecuteInputCommand", command);

        stageManager.Verify(
            m => m.ChangeStage(
                StageType.SongSelect,
                It.Is<IStageTransition>(t => t is DTXManiaFadeTransition),
                null),
            Times.Once);
    }

    [Fact]
    public void ExecuteInputCommand_WhenCannotTransition_ShouldNotTransition()
    {
#pragma warning disable SYSLIB0050
        var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
        var game = CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
        SetPrivateField(stage, "_game", game);
        var stageManager = new Mock<IStageManager>();
        stage.StageManager = stageManager.Object;

        var command = new DTXMania.Game.Lib.Input.InputCommand(
            DTXMania.Game.Lib.Input.InputCommandType.Activate, 0.0);

        InvokePrivateMethod(stage, "ExecuteInputCommand", command);

        stageManager.Verify(
            m => m.ChangeStage(
                It.IsAny<StageType>(),
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void CleanupComponents_ShouldDisposeWhitePixelAndReleaseFont()
    {
#pragma warning disable SYSLIB0050
        var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
        var whitePixel = (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
        GC.SuppressFinalize(whitePixel);
        var font = new Mock<IFont>();

        SetPrivateField(stage, "_whitePixel", whitePixel);
        SetPrivateField(stage, "_resultFont", font.Object);

        InvokePrivateMethod(stage, "CleanupComponents");

        font.Verify(f => f.RemoveReference(), Times.Once);
        Assert.Null(GetPrivateField<Texture2D>(stage, "_whitePixel"));
        Assert.Null(GetPrivateField<IFont>(stage, "_resultFont"));
    }

    [Fact]
    public void ExtractSharedData_WithNoSharedData_ShouldCreateDefaultSummary()
    {
#pragma warning disable SYSLIB0050
        var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
        SetPrivateField(stage, "_sharedData", null);

        InvokePrivateMethod(stage, "ExtractSharedData");

        var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.Equal(0, summary.Score);
        Assert.Equal(0, summary.MaxCombo);
        Assert.False(summary.ClearFlag);
        Assert.Equal(CompletionReason.Unknown, summary.CompletionReason);
    }

    [Fact]
    public void ExtractSharedData_WithNoSong_ShouldLeaveSongNull()
    {
#pragma warning disable SYSLIB0050
        var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
        SetPrivateField(stage, "_sharedData", new Dictionary<string, object>
        {
            ["performanceSummary"] = new PerformanceSummary { Score = 100 }
        });

        InvokePrivateMethod(stage, "ExtractSharedData");

        Assert.Null(GetPrivateField<object>(stage, "_selectedSong"));
        var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
        Assert.NotNull(summary);
        Assert.Equal(100, summary!.Score);
    }

    [Fact]
    public void OnActivate_WhenInputManagerIsNull_ShouldInitializeComponentsWithoutThrowing()
    {
#pragma warning disable SYSLIB0050
        var stage = (InspectableNullInputResultStage)FormatterServices.GetUninitializedObject(typeof(InspectableNullInputResultStage));
#pragma warning restore SYSLIB0050
        SetPrivateField(stage, "_inputManager", null);
        SetPrivateField(stage, "_sharedData", new Dictionary<string, object>
        {
            ["performanceSummary"] = new PerformanceSummary { Score = 123456 }
        });

        var exception = Record.Exception(() => InvokePrivateMethod(stage, "OnActivate"));

        Assert.Null(exception);
        Assert.True(stage.WhitePixelRequested);
        Assert.True(stage.ResultFontRequested);
    }

    private static SpriteBatch CreateFakeSpriteBatch(int width, int height)
    {
#pragma warning disable SYSLIB0050
        var spriteBatch = (SpriteBatch)FormatterServices.GetUninitializedObject(typeof(SpriteBatch));
        var graphicsDevice = (GraphicsDevice)FormatterServices.GetUninitializedObject(typeof(GraphicsDevice));
#pragma warning restore SYSLIB0050
        GC.SuppressFinalize(spriteBatch);
        GC.SuppressFinalize(graphicsDevice);
        SetPrivateField(spriteBatch, "graphicsDevice", graphicsDevice);
        SetPrivateField(graphicsDevice, "_viewport", new Viewport(0, 0, width, height));
        return spriteBatch;
    }

    private sealed class InspectableNullInputResultStage : ResultStage
    {
        public InspectableNullInputResultStage(BaseGame game) : base(game) { }

        public bool WhitePixelRequested { get; private set; }
        public bool ResultFontRequested { get; private set; }

        internal override Texture2D CreateWhitePixel()
        {
            WhitePixelRequested = true;
            return null!;
        }

        internal override IFont CreateResultFont()
        {
            ResultFontRequested = true;
            return null!;
        }
    }
}

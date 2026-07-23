using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.Stage.Result;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using static DTXMania.Test.TestData.ReflectionHelpers;

namespace DTXMania.Test.Stage.Result;

[Trait("Category", "Unit")]
public sealed class ResultStageScoreSaveTests
{
    [Fact]
    public void SaveFailure_ActivateRetriesSameSummary_ThenSuccessBecomesSaved()
    {
        var stage = CreateStage();
        var summary = SavableSummary();
        var chart = new SongChart { Id = 42 };
        var model = ResultScreenModel.Create(summary, null, 0, chart, null);
        stage.Enqueue(ScoreSaveResult.Failed("database busy"));
        stage.Enqueue(ScoreSaveResult.Saved(7));
        SetPrivateField(stage, "_performanceSummary", summary);
        SetPrivateField(stage, "_resultModel", model);

        InvokePrivateMethod(stage, "StartPerformanceSummarySave", chart);
        Assert.Equal(ResultSaveState.Saving, stage.ScoreSaveState);
        InvokePrivateMethod(stage, "ObservePerformanceSummarySave");

        Assert.Equal(ResultSaveState.Failed, stage.ScoreSaveState);
        Assert.Equal("database busy", stage.ScoreSaveError);
        Assert.Contains("BACK TO LEAVE WITHOUT SAVING", model.SaveGuidanceText);

        InvokePrivateMethod(
            stage,
            "ExecuteInputCommand",
            new InputCommand(InputCommandType.Activate, 0.0));
        Assert.Equal(ResultSaveState.Saving, stage.ScoreSaveState);
        Assert.Equal(2, stage.SaveCalls);
        Assert.All(stage.Summaries, saved => Assert.Same(summary, saved));

        InvokePrivateMethod(stage, "ObservePerformanceSummarySave");
        Assert.Equal(ResultSaveState.Saved, stage.ScoreSaveState);
        Assert.Null(stage.ScoreSaveError);
        Assert.Equal("SCORE SAVE: SAVED", model.SaveStatusText);
    }

    [Theory]
    [InlineData(InputCommandType.Activate)]
    [InlineData(InputCommandType.Back)]
    public void Saving_BlocksNormalExitAndDoesNotStartAnotherSave(
        InputCommandType commandType)
    {
        var stageManager = new Mock<IStageManager>();
        var pending = new TaskCompletionSource<ScoreSaveResult>(
            TaskCreationOptions.RunContinuationsAsynchronously);
        var stage = CreateStage(stageManager);
        var summary = SavableSummary();
        stage.Enqueue(pending.Task);
        SetPrivateField(stage, "_performanceSummary", summary);

        InvokePrivateMethod(
            stage,
            "StartPerformanceSummarySave",
            new SongChart { Id = 42 });
        InvokePrivateMethod(
            stage,
            "ExecuteInputCommand",
            new InputCommand(commandType, 0.0));

        Assert.Equal(ResultSaveState.Saving, stage.ScoreSaveState);
        Assert.Equal(1, stage.SaveCalls);
        stageManager.Verify(
            manager => manager.ChangeStage(
                It.IsAny<StageType>(),
                It.IsAny<IStageTransition>(),
                It.IsAny<Dictionary<string, object>>()),
            Times.Never);
    }

    [Fact]
    public void Failed_BackUsesExplicitLeaveWithoutSavingPath()
    {
        var stageManager = new Mock<IStageManager>();
        var stage = CreateStage(stageManager);
        InvokePrivateMethod(
            stage,
            "SetScoreSavePresentation",
            ResultSaveState.Failed,
            "disk full");

        InvokePrivateMethod(
            stage,
            "ExecuteInputCommand",
            new InputCommand(InputCommandType.Back, 0.0));

        stageManager.Verify(
            manager => manager.ChangeStage(
                StageType.SongSelect,
                It.IsAny<IStageTransition>(),
                null),
            Times.Once);
    }

    [Fact]
    public void AlreadySaved_IsTreatedAsSaved()
    {
        var stage = CreateStage();
        var summary = SavableSummary();
        stage.Enqueue(ScoreSaveResult.AlreadySaved(9));
        SetPrivateField(stage, "_performanceSummary", summary);

        InvokePrivateMethod(
            stage,
            "StartPerformanceSummarySave",
            new SongChart { Id = 42 });
        InvokePrivateMethod(stage, "ObservePerformanceSummarySave");

        Assert.Equal(ResultSaveState.Saved, stage.ScoreSaveState);
        Assert.Equal(1, stage.SaveCalls);
    }

    [Fact]
    public void ResolvePreviousScore_UsesFrozenSummarySpeedOnly()
    {
        var stage = CreateStage();
        var node = new SongListNode();
        node.SetScore(0, new SongScore
        {
            ChartId = 42,
            Instrument = EInstrumentPart.DRUMS,
            BestScore = 1_000_000
        });
        node.SetScoreVariant(0, 75, new SongScore
        {
            ChartId = 42,
            Instrument = EInstrumentPart.DRUMS,
            BestScore = 750_000
        });
        SetPrivateField(stage, "_selectedSong", node);
        SetPrivateField(stage, "_selectedDifficulty", 0);
        SetPrivateField(stage, "_performanceSummary", new PerformanceSummary
        {
            PlaySpeedPercent = 75
        });

        var previous = stage.ResolvePreviousScore(new SongChart { Id = 42 });

        Assert.NotNull(previous);
        Assert.Equal(75, previous.PlaySpeedPercent);
        Assert.Equal(750_000, previous.BestScore);
    }

    [Theory]
    [InlineData(EInstrumentPart.GUITAR)]
    [InlineData(EInstrumentPart.BASS)]
    public void StartPerformanceSummarySave_UsesSelectedSlotInstrument(
        EInstrumentPart instrument)
    {
        var stage = CreateStage();
        var summary = SavableSummary();
        var node = new SongListNode();
        node.SetScore(1, new SongScore
        {
            ChartId = 42,
            Instrument = instrument
        });
        stage.Enqueue(ScoreSaveResult.Saved(7));
        SetPrivateField(stage, "_selectedSong", node);
        SetPrivateField(stage, "_selectedDifficulty", 1);
        SetPrivateField(stage, "_performanceSummary", summary);

        InvokePrivateMethod(
            stage,
            "StartPerformanceSummarySave",
            new SongChart { Id = 42 });

        Assert.Equal(instrument, Assert.Single(stage.Instruments));
    }

    private static TestResultStage CreateStage(
        Mock<IStageManager>? stageManager = null)
    {
        var game = new Mock<IStageGame>();
        game.Setup(candidate => candidate.CanPerformStageTransition())
            .Returns(true);
        var stage = new TestResultStage(game.Object)
        {
            StageManager = stageManager?.Object
        };
        return stage;
    }

    private static PerformanceSummary SavableSummary()
    {
        return new PerformanceSummary
        {
            RunId = Guid.NewGuid(),
            PlaySpeedPercent = 75,
            PitchSemitones = 3,
            CompletionReason = CompletionReason.SongComplete,
            ClearFlag = true,
            Score = 750_000
        };
    }

    private sealed class TestResultStage : ResultStage
    {
        private readonly Queue<Task<ScoreSaveResult>> _results = new();

        public TestResultStage(IStageGame game)
            : base(game)
        {
        }

        public int SaveCalls { get; private set; }

        public List<PerformanceSummary> Summaries { get; } = new();

        public List<EInstrumentPart> Instruments { get; } = new();

        public void Enqueue(ScoreSaveResult result)
        {
            Enqueue(Task.FromResult(result));
        }

        public void Enqueue(Task<ScoreSaveResult> result)
        {
            _results.Enqueue(result);
        }

        protected override SpriteBatch CreateSpriteBatch(
            GraphicsDevice graphicsDevice)
        {
            return null!;
        }

        internal override Task<ScoreSaveResult> SavePerformanceSummaryAsync(
            int chartId,
            EInstrumentPart instrument,
            PerformanceSummary summary)
        {
            SaveCalls++;
            Instruments.Add(instrument);
            Summaries.Add(summary);
            return _results.Dequeue();
        }
    }
}
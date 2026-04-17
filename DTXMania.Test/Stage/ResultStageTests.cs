using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.Performance;
using DTXMania.Game.Lib.UI;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;

namespace DTXMania.Test.Stage
{
    /// <summary>
    /// Unit tests for ResultStage focusing on pure logic methods
    /// that do not require graphics initialization.
    /// </summary>
    [Trait("Category", "Unit")]
    public class ResultStageTests
    {
        private const string PerformanceSummaryKey = "performanceSummary";
        #region Constructor Tests

        [Fact]
        public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
        {
            Assert.Throws<ArgumentNullException>(() => new ResultStage(null));
        }

        #endregion

        #region Type Property Tests

        [Fact]
        public void Type_Property_ShouldExistAndReturnStageType()
        {
            var property = typeof(ResultStage).GetProperty(
                "Type",
                BindingFlags.Public | BindingFlags.Instance | BindingFlags.FlattenHierarchy);
            Assert.NotNull(property);
            Assert.Equal(typeof(StageType), property!.PropertyType);
        }

        [Fact]
        public void Type_Value_ShouldBeResult()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            Assert.Equal(StageType.Result, stage.Type);
        }

        #endregion

        #region ExtractSharedData Tests

        [Fact]
        public void ExtractSharedData_WithNullSharedData_ShouldCreateDefaultSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            SetPrivateField(stage, "_sharedData", null);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.Score);
            Assert.Equal(0, summary.MaxCombo);
            Assert.False(summary.ClearFlag);
            Assert.Equal(CompletionReason.Unknown, summary.CompletionReason);
        }

        [Fact]
        public void ExtractSharedData_WithMissingPerformanceSummaryKey_ShouldCreateDefaultSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            var sharedData = new Dictionary<string, object>
            {
                { "otherKey", "otherValue" }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.Score);
            Assert.False(summary.ClearFlag);
        }

        [Fact]
        public void ExtractSharedData_WithValidPerformanceSummary_ShouldUseProvidedSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            var expectedSummary = new PerformanceSummary
            {
                Score = 987654,
                MaxCombo = 250,
                ClearFlag = true,
                CompletionReason = CompletionReason.SongComplete
            };

            var sharedData = new Dictionary<string, object>
            {
                { PerformanceSummaryKey, expectedSummary }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(987654, summary!.Score);
            Assert.Equal(250, summary.MaxCombo);
            Assert.True(summary.ClearFlag);
            Assert.Equal(CompletionReason.SongComplete, summary.CompletionReason);
        }

        [Fact]
        public void ExtractSharedData_WithWrongTypeForSummaryKey_ShouldCreateDefaultSummary()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            // Put wrong type under the performanceSummary key
            var sharedData = new Dictionary<string, object>
            {
                { PerformanceSummaryKey, "not a PerformanceSummary" }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.Score);
            Assert.Equal(CompletionReason.Unknown, summary.CompletionReason);
        }

        [Fact]
        public void ExtractSharedData_DefaultSummary_ShouldHaveZeroJudgementCounts()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            SetPrivateField(stage, "_sharedData", null);
            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(0, summary!.JustCount);
            Assert.Equal(0, summary.GreatCount);
            Assert.Equal(0, summary.GoodCount);
            Assert.Equal(0, summary.PoorCount);
            Assert.Equal(0, summary.MissCount);
        }

        [Fact]
        public void ExtractSharedData_ValidSummary_PreservesJudgementCounts()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            var expectedSummary = new PerformanceSummary
            {
                Score = 500000,
                JustCount = 100,
                GreatCount = 50,
                GoodCount = 20,
                PoorCount = 5,
                MissCount = 10,
                MaxCombo = 80,
                ClearFlag = false
            };

            var sharedData = new Dictionary<string, object>
            {
                { PerformanceSummaryKey, expectedSummary }
            };
            SetPrivateField(stage, "_sharedData", sharedData);

            InvokePrivateMethod(stage, "ExtractSharedData");

            var summary = GetPrivateField<PerformanceSummary>(stage, "_performanceSummary");
            Assert.NotNull(summary);
            Assert.Equal(100, summary!.JustCount);
            Assert.Equal(50, summary.GreatCount);
            Assert.Equal(20, summary.GoodCount);
            Assert.Equal(5, summary.PoorCount);
            Assert.Equal(10, summary.MissCount);
        }

        #endregion

        #region Inheritance and Interface Tests

        [Fact]
        public void ResultStage_ShouldInheritFromBaseStage()
        {
            Assert.True(typeof(BaseStage).IsAssignableFrom(typeof(ResultStage)));
        }

        [Fact]
        public void ResultStage_ShouldImplementIStage()
        {
            Assert.True(typeof(IStage).IsAssignableFrom(typeof(ResultStage)));
        }

        [Fact]
        public void HandleInput_WhenInputManagerIsNull_ShouldReturnWithoutThrowing()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050

            SetPrivateField(stage, "_inputManager", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "HandleInput"));

            Assert.Null(exception);
        }

        [Fact]
        public void ExecuteInputCommand_WhenTransitionIsDebounced_ShouldNotChangeStage()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);

            SetPrivateField(stage, "_game", game);
            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.Back, 0.0));

            stageManager.Verify(
                manager => manager.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
            Assert.Equal(0.0, DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenCommandIsNotNavigation_ShouldIgnoreIt()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);

            SetPrivateField(stage, "_game", game);
            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.MoveDown, 0.0));

            stageManager.Verify(
                manager => manager.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
            Assert.Equal(0.0, DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenActivateAndTransitionAllowed_ShouldReturnToSongSelect()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);

            SetPrivateField(stage, "_game", game);
            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new DTXMania.Game.Lib.Input.InputCommand(DTXMania.Game.Lib.Input.InputCommandType.Activate, 0.0));

            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition),
                    null),
                Times.Once);
            Assert.Equal(2.0, DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_WhenBackAndTransitionAllowed_ShouldReturnToSongSelect()
        {
            var stage = CreateUninitializedResultStageWithStageManager();

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            VerifySongSelectTransition(stage);
        }

        [Fact]
        public void OnUpdate_WhenQueuedBackCommandExists_ShouldProcessInputAndReturnToSongSelect()
        {
            var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            var inputManager = new TrackingInputManager();

            inputManager.Enqueue(new InputCommand(InputCommandType.Back, 0.0));
            SetPrivateField(stage, "_game", game);
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_uiManager", new UIManager());
            SetPrivateField(stage, "_elapsedTime", 0.0);

            InvokePrivateMethod(stage, "OnUpdate", 0.25);

            Assert.True(inputManager.UpdateCalled);
            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition),
                    null),
                Times.Once);
            Assert.Equal(2.0, DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void OnUpdate_WhenUiManagerIsNull_ShouldStillProcessQueuedInput()
        {
            var stage = CreateUninitializedResultStageWithStageManager();
            var inputManager = new TrackingInputManager();
            inputManager.Enqueue(new InputCommand(InputCommandType.Back, 0.0));

            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_uiManager", null);
            SetPrivateField(stage, "_elapsedTime", 0.0);

            InvokePrivateMethod(stage, "OnUpdate", 0.25);

            Assert.True(inputManager.UpdateCalled);
            Assert.Equal(0.25, GetPrivateField<double>(stage, "_elapsedTime"));
            VerifySongSelectTransition(stage);
        }

        [Fact]
        public void HandleInput_WhenQueueIsEmpty_ShouldNotChangeStage()
        {
            var stage = CreateUninitializedResultStageWithStageManager();
            var inputManager = new TrackingInputManager();
            SetPrivateField(stage, "_inputManager", inputManager);

            InvokePrivateMethod(stage, "HandleInput");

            Assert.False(GetStageManagerMock(stage).Invocations.Any());
        }

        [Fact]
        public void ReturnToSongSelect_ShouldUseFadeTransitionWithNullSharedData()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ReturnToSongSelect");

            stageManager.Verify(
                manager => manager.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition),
                    null),
                Times.Once);
        }

        [Fact]
        public void DrawResultLine_WhenTextIsEmpty_ShouldNotAdvanceCurrentY()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var currentY = 120;
            object[] args = [string.Empty, 400, currentY, Color.White, 32];

            InvokePrivateMethod(stage, "DrawResultLine", args);

            Assert.Equal(120, Assert.IsType<int>(args[2]));
        }

        [Fact]
        public void CleanupComponents_ShouldDisposeTrackedResourcesAndClearFields()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
            var whitePixel = (TrackingTexture2D)FormatterServices.GetUninitializedObject(typeof(TrackingTexture2D));
            var font = (TrackingBitmapFont)FormatterServices.GetUninitializedObject(typeof(TrackingBitmapFont));
#pragma warning restore SYSLIB0050

            SetPrivateField(stage, "_whitePixel", whitePixel);
            SetPrivateField(stage, "_resultFont", font);

            InvokePrivateMethod(stage, "CleanupComponents");

            Assert.True(whitePixel.WasDisposed);
            Assert.True(font.WasDisposed);
            Assert.Null(GetPrivateField<Texture2D>(stage, "_whitePixel"));
            Assert.Null(GetPrivateField<BitmapFont>(stage, "_resultFont"));
        }

        [Fact]
        public void Dispose_WhenDisposing_ShouldReleaseSpriteBatchAndCleanupComponents()
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
            var spriteBatch = (TrackingSpriteBatch)FormatterServices.GetUninitializedObject(typeof(TrackingSpriteBatch));
            var whitePixel = (TrackingTexture2D)FormatterServices.GetUninitializedObject(typeof(TrackingTexture2D));
            var font = (TrackingBitmapFont)FormatterServices.GetUninitializedObject(typeof(TrackingBitmapFont));
#pragma warning restore SYSLIB0050

            SetPrivateField(stage, "_game", DTXMania.Test.TestData.ReflectionHelpers.CreateGame());
            SetPrivateField(stage, "_spriteBatch", spriteBatch);
            SetPrivateField(stage, "_uiManager", new UIManager());
            SetPrivateField(stage, "_whitePixel", whitePixel);
            SetPrivateField(stage, "_resultFont", font);
            SetPrivateField(stage, "_disposed", false);

            InvokeDispose(stage, true);

            Assert.True(spriteBatch.WasDisposed);
            Assert.True(whitePixel.WasDisposed);
            Assert.True(font.WasDisposed);
        }

        #endregion

        #region Helper Methods

        private static void InvokePrivateMethod(object target, string methodName, params object[] args)
        {
            var type = target.GetType();
            while (type != null)
            {
                var method = type.GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (method != null)
                {
                    method.Invoke(target, args);
                    return;
                }
                type = type.BaseType;
            }
            Assert.Fail($"Method '{methodName}' not found");
        }

        private static T? GetPrivateField<T>(object target, string fieldName)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                    return (T?)field.GetValue(target);
                type = type.BaseType;
            }
            Assert.Fail($"Field '{fieldName}' not found");
            return default;
        }

        private static void SetPrivateField(object target, string fieldName, object? value)
        {
            var type = target.GetType();
            while (type != null)
            {
                var field = type.GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
                if (field != null)
                {
                    field.SetValue(target, value);
                    return;
                }
                type = type.BaseType;
            }
            Assert.Fail($"Field '{fieldName}' not found");
        }

        private static void InvokeDispose(ResultStage stage, bool disposing)
        {
            var method = typeof(ResultStage).GetMethod(
                "Dispose",
                BindingFlags.Instance | BindingFlags.NonPublic,
                binder: null,
                types: new[] { typeof(bool) },
                modifiers: null);
            Assert.NotNull(method);
            method!.Invoke(stage, new object[] { disposing });
        }

        private static ResultStage CreateUninitializedResultStageWithStageManager(double totalGameTime = 2.0, double lastStageTransitionTime = 0.0)
        {
#pragma warning disable SYSLIB0050
            var stage = (ResultStage)FormatterServices.GetUninitializedObject(typeof(ResultStage));
#pragma warning restore SYSLIB0050
            var stageManager = new Mock<IStageManager>();
            var game = DTXMania.Test.TestData.ReflectionHelpers.CreateGame(totalGameTime: totalGameTime, lastStageTransitionTime: lastStageTransitionTime);

            SetPrivateField(stage, "_game", game);
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_inputManager", null);
            SetPrivateField(stage, "_uiManager", new UIManager());

            return stage;
        }

        private static Mock<IStageManager> GetStageManagerMock(ResultStage stage)
        {
            return Mock.Get(stage.StageManager!);
        }

        private static void VerifySongSelectTransition(ResultStage stage, double expectedTransitionTime = 2.0)
        {
            GetStageManagerMock(stage).Verify(
                manager => manager.ChangeStage(
                    StageType.SongSelect,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition),
                    null),
                Times.Once);
            Assert.Equal(expectedTransitionTime, DTXMania.Test.TestData.ReflectionHelpers.GetPrivateField<double>(GetPrivateField<BaseGame>(stage, "_game")!, "_lastStageTransitionTime"));
        }

        private sealed class TrackingInputManager : InputManager
        {
            public bool UpdateCalled { get; private set; }

            public void Enqueue(InputCommand command)
            {
                EnqueueCommand(command);
            }

            public override void Update(double deltaTime = 0)
            {
                UpdateCalled = true;
                base.Update(deltaTime);
            }
        }

        private sealed class TrackingSpriteBatch : SpriteBatch
        {
            public TrackingSpriteBatch() : base(null!)
            {
            }

            public bool WasDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
            }
        }

        private sealed class TrackingTexture2D : Texture2D
        {
            public TrackingTexture2D() : base(null!, 1, 1)
            {
            }

            public bool WasDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
            }
        }

        private sealed class TrackingBitmapFont : BitmapFont
        {
            public TrackingBitmapFont() : base((IResourceManager)null!, new BitmapFont.BitmapFontConfig(), allowNullGraphicsDevice: true)
            {
            }

            public bool WasDisposed { get; private set; }

            protected override void Dispose(bool disposing)
            {
                WasDisposed = true;
            }
        }

        #endregion
    }
}

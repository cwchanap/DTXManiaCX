using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using static DTXMania.Test.TestData.ReflectionHelpers;

using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.Stage
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongSelectionStageCoverageTests
    {
        [Fact]
        public void HandleInput_WithNonNullInputManager_ShouldDelegateToProcessInputCommands()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };
            var inputManager = new QueuedInputManager();

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_inputManager", inputManager);
            inputManager.Enqueue(new InputCommand(InputCommandType.MoveDown, 0.0));

            InvokePrivateMethod(stage, "HandleInput");

            Assert.Equal("B", display.SelectedSong!.Title);
        }

        [Fact]
        public void HandleInput_WhenInputManagerNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_inputManager", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "HandleInput"));

            Assert.Null(exception);
        }

        [Fact]
        public void ProcessInputCommands_WithQueuedCommands_ShouldExecuteAll()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B"), CreateScoreNode("C")]
            };
            var inputManager = new QueuedInputManager();

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_inputManager", inputManager);
            inputManager.Enqueue(new InputCommand(InputCommandType.MoveDown, 0.0));
            inputManager.Enqueue(new InputCommand(InputCommandType.MoveDown, 0.0));

            InvokePrivateMethod(stage, "ProcessInputCommands");

            Assert.Equal("C", display.SelectedSong!.Title);
            Assert.False(inputManager.HasPendingCommands);
        }

        [Fact]
        public void ProcessInputCommands_WhenInputManagerNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_inputManager", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "ProcessInputCommands"));

            Assert.Null(exception);
        }

        [Fact]
        public void ExecuteInputCommand_MoveUp_ShouldNavigatePrevious()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };
            var cursorSound = new Mock<ISound>();

            AttachCoreUi(stage, display: display);
            display.MoveNext();
            SetPrivateField(stage, "_elapsedTime", 5.0);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveUp, 0.0));

            Assert.Equal("A", display.SelectedSong!.Title);
            Assert.Equal(5.0, GetPrivateField<double>(stage, "_lastNavigationTime"));
            cursorSound.Verify(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume), Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_MoveDown_ShouldNavigateNext()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };
            var cursorSound = new Mock<ISound>();

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_elapsedTime", 3.0);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveDown, 0.0));

            Assert.Equal("B", display.SelectedSong!.Title);
            Assert.Equal(3.0, GetPrivateField<double>(stage, "_lastNavigationTime"));
            cursorSound.Verify(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume), Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_MoveUpInStatusPanel_ShouldNavigatePrevious()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };

            AttachCoreUi(stage, display: display);
            display.MoveNext();
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(stage, "_elapsedTime", 7.0);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveUp, 0.0));

            Assert.Equal("A", display.SelectedSong!.Title);
            Assert.Equal(7.0, GetPrivateField<double>(stage, "_lastNavigationTime"));
        }

        [Fact]
        public void ExecuteInputCommand_MoveDownInStatusPanel_ShouldNavigateNext()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(stage, "_elapsedTime", 4.0);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveDown, 0.0));

            Assert.Equal("B", display.SelectedSong!.Title);
            Assert.Equal(4.0, GetPrivateField<double>(stage, "_lastNavigationTime"));
        }

        [Fact]
        public void ExecuteInputCommand_MoveLeftInStatusPanel_ShouldCycleDifficultyBackward()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("Song", CreateScores(0, 2, 4));
            var display = new SongListDisplay { CurrentList = [song] };
            var statusPanel = new SongStatusPanel();
            var cursorSound = new Mock<ISound>();

            AttachCoreUi(stage, display: display, statusPanel: statusPanel);
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 4);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);
            display.CurrentDifficulty = 4;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveLeft, 0.0));

            Assert.Equal(2, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Equal(2, display.CurrentDifficulty);
            Assert.Equal(2, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            cursorSound.Verify(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume), Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_MoveRightInStatusPanel_ShouldCycleDifficultyForward()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("Song", CreateScores(0, 2));
            var display = new SongListDisplay { CurrentList = [song] };
            var statusPanel = new SongStatusPanel();
            var cursorSound = new Mock<ISound>();

            display.DifficultyChanged += (sender, e) => InvokePrivateMethod(stage, "OnDifficultyChanged", sender!, e);

            AttachCoreUi(stage, display: display, statusPanel: statusPanel);
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 0);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);
            display.CurrentDifficulty = 0;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveRight, 0.0));

            Assert.Equal(2, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Equal(2, display.CurrentDifficulty);
            Assert.Equal(2, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            cursorSound.Verify(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume), Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_MoveLeftOutsideStatusPanel_ShouldDoNothing()
        {
            var stage = CreateStage();
            var display = new SongListDisplay { CurrentList = [CreateScoreNode("A")] };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_isInStatusPanel", false);
            SetPrivateField(stage, "_currentDifficulty", 3);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveLeft, 0.0));

            Assert.Equal(3, GetPrivateField<int>(stage, "_currentDifficulty"));
        }

        [Fact]
        public void ExecuteInputCommand_MoveRightOutsideStatusPanel_ShouldDoNothing()
        {
            var stage = CreateStage();
            var display = new SongListDisplay { CurrentList = [CreateScoreNode("A")] };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_isInStatusPanel", false);
            SetPrivateField(stage, "_currentDifficulty", 1);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveRight, 0.0));

            Assert.Equal(1, GetPrivateField<int>(stage, "_currentDifficulty"));
        }

        [Fact]
        public void ExecuteInputCommand_Activate_ShouldDelegateToHandleActivateInput()
        {
            var stage = CreateStage();
            var statusPanel = new SongStatusPanel { Visible = false };
            var selectedSong = CreateScoreNode("Song");

            AttachCoreUi(stage, statusPanel: statusPanel);
            SetPrivateField(stage, "_selectedSong", selectedSong);
            SetPrivateField(stage, "_isInStatusPanel", false);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Activate, 0.0));

            Assert.True(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.True(statusPanel.Visible);
        }

        [Fact]
        public void ExecuteInputCommand_BackInStatusPanel_ShouldExitStatusPanelMode()
        {
            var stage = CreateStage();
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            stageManager.Verify(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()), Times.Never);
        }

        [Fact]
        public void ExecuteInputCommand_BackWithNavigationStack_ShouldNavigateBack()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var stack = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!;

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("Child Song") });
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Child");
            stack.Push(new SongListNode { Children = rootSongs, Title = "Root" });

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            Assert.Same(rootSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Empty(stack);
        }

        [Fact]
        public void ExecuteInputCommand_BackAtRoot_WhenTransitionAllowed_ShouldReturnToTitle()
        {
            var game = CreateGame(totalGameTime: 1.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(t => t is DTXManiaFadeTransition)),
                Times.Once);
            Assert.Equal(1.0, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_BackAtRoot_WhenTransitionDebounced_ShouldNotTransition()
        {
            var game = CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            stageManager.Verify(x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()), Times.Never);
            Assert.Equal(0.0, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_IncreaseScrollSpeed_ShouldCallConfigManager()
        {
            var stage = CreateStage();
            var configManager = new Mock<IConfigManager>();

            SetPrivateField(stage, "_configManager", configManager.Object);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.IncreaseScrollSpeed, 0.0));

            configManager.Verify(x => x.AdjustScrollSpeed(AppPaths.GetConfigFilePath(), 1), Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_DecreaseScrollSpeed_ShouldCallConfigManager()
        {
            var stage = CreateStage();
            var configManager = new Mock<IConfigManager>();

            SetPrivateField(stage, "_configManager", configManager.Object);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.DecreaseScrollSpeed, 0.0));

            configManager.Verify(x => x.AdjustScrollSpeed(AppPaths.GetConfigFilePath(), -1), Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_IncreaseScrollSpeed_WhenConfigManagerNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_configManager", null);

            var exception = Record.Exception(() =>
                InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.IncreaseScrollSpeed, 0.0)));

            Assert.Null(exception);
        }

        [Fact]
        public void ExecuteInputCommand_DecreaseScrollSpeed_WhenConfigManagerNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_configManager", null);

            var exception = Record.Exception(() =>
                InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.DecreaseScrollSpeed, 0.0)));

            Assert.Null(exception);
        }

        [Fact]
        public void HandleActivateInput_InStatusPanelWithScore_ShouldSelectSong()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var selectedSong = CreateScoreNode("Song", songId: 99);

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_selectedSong", selectedSong);
            SetPrivateField(stage, "_currentDifficulty", 1);
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "HandleActivateInput");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.Is<IStageTransition>(t => t is InstantTransition),
                    It.Is<Dictionary<string, object>>(d =>
                        ReferenceEquals(d["selectedSong"], selectedSong) &&
                        (int)d["selectedDifficulty"] == 1 &&
                        (int)d["songId"] == 99)),
                Times.Once);
        }

        [Fact]
        public void HandleActivateInput_InStatusPanelWithNonScoreNode_ShouldIgnore()
        {
            var stage = CreateStage();
            var stageManager = new Mock<IStageManager>();
            var folder = CreateBoxNode("Folder");

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_selectedSong", folder);
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.True(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void HandleActivateInput_InStatusPanelWithNullSelection_ShouldDoNothing()
        {
            var stage = CreateStage();
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_selectedSong", null);
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.True(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void HandleActivateInput_NotInStatusPanelWithScore_ShouldEnterStatusPanel()
        {
            var stage = CreateStage();
            var statusPanel = new SongStatusPanel { Visible = false };
            var selectedSong = CreateScoreNode("Song");

            AttachCoreUi(stage, statusPanel: statusPanel);
            SetPrivateField(stage, "_selectedSong", selectedSong);
            SetPrivateField(stage, "_isInStatusPanel", false);

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.True(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.True(statusPanel.Visible);
        }

        [Fact]
        public void HandleActivateInput_NotInStatusPanelWithBox_ShouldNavigateIntoBox()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var childSong = CreateScoreNode("Child");
            var box = CreateBoxNode("Folder", childSong);

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_selectedSong", box);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { box });

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.Same(box.Children, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal(NodeType.BackBox, display.CurrentList[0].Type);
            Assert.Same(childSong, display.CurrentList[1]);
        }

        [Fact]
        public void HandleActivateInput_NotInStatusPanelWithBackBox_ShouldNavigateBack()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var backBox = new SongListNode { Type = NodeType.BackBox, Title = ".. (Back)" };
            var stack = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!;

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_selectedSong", backBox);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("Child Song") });
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Child");
            stack.Push(new SongListNode { Children = rootSongs, Title = "Root" });

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.Same(rootSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
        }

        [Fact]
        public void HandleActivateInput_WhenSelectedSongIsNullAndNotInStatusPanel_ShouldDoNothing()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_selectedSong", null);
            SetPrivateField(stage, "_isInStatusPanel", false);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "HandleActivateInput"));

            Assert.Null(exception);
            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
        }

        [Fact]
        public void CycleDifficulty_Forward_ShouldCallSongListDisplay()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("Song", CreateScores(0, 2));
            var display = new SongListDisplay { CurrentList = [song] };
            var cursorSound = new Mock<ISound>();

            display.DifficultyChanged += (sender, e) => InvokePrivateMethod(stage, "OnDifficultyChanged", sender!, e);

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 0);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);
            display.CurrentDifficulty = 0;

            InvokePrivateMethod(stage, "CycleDifficulty", 1);

            Assert.Equal(2, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Equal(2, display.CurrentDifficulty);
            cursorSound.Verify(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume), Times.Once);
        }

        [Fact]
        public void CycleDifficulty_Backward_ShouldCycleToPreviousDifficulty()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("Song", CreateScores(0, 2, 4));
            var display = new SongListDisplay { CurrentList = [song] };
            var statusPanel = new SongStatusPanel();
            var cursorSound = new Mock<ISound>();

            AttachCoreUi(stage, display: display, statusPanel: statusPanel);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 4);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);
            display.CurrentDifficulty = 4;

            InvokePrivateMethod(stage, "CycleDifficulty", -1);

            Assert.Equal(2, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Equal(2, display.CurrentDifficulty);
            Assert.Equal(2, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            cursorSound.Verify(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume), Times.Once);
        }

        [Fact]
        public void CycleDifficulty_BackwardWithSingleDifficulty_ShouldNotChange()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("Song", CreateScores(2));
            var display = new SongListDisplay { CurrentList = [song] };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 2);
            display.CurrentDifficulty = 2;

            InvokePrivateMethod(stage, "CycleDifficulty", -1);

            Assert.Equal(2, GetPrivateField<int>(stage, "_currentDifficulty"));
        }

        [Fact]
        public void CycleDifficulty_BackwardWithNullSelectedSong_ShouldNotThrow()
        {
            var stage = CreateStage();
            var display = new SongListDisplay { CurrentList = [CreateScoreNode("A")] };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_selectedSong", null);
            SetPrivateField(stage, "_currentDifficulty", 0);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "CycleDifficulty", -1));

            Assert.Null(exception);
        }

        [Fact]
        public void CycleDifficulty_BackwardWhenCurrentDifficultyNotInAvailable_ShouldNotChange()
        {
            var stage = CreateStage();
            var scores = new SongScore[5];
            scores[0] = new SongScore { DifficultyLevel = 0, DifficultyLabel = "Basic" };
            scores[2] = new SongScore { DifficultyLevel = 2, DifficultyLabel = "Advanced" };
            var song = CreateScoreNode("Song", scores);
            var display = new SongListDisplay { CurrentList = [song] };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 1);
            display.CurrentDifficulty = 1;

            InvokePrivateMethod(stage, "CycleDifficulty", -1);

            Assert.Equal(1, GetPrivateField<int>(stage, "_currentDifficulty"));
        }

        [Fact]
        public void SelectSong_WhenTransitionAllowed_ShouldTransitionToSongTransition()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var song = CreateScoreNode("Song", CreateScores(0, 2), songId: 17);

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentDifficulty", 2);

            InvokePrivateMethod(stage, "SelectSong", song);

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.Is<IStageTransition>(t => t is InstantTransition),
                    It.Is<Dictionary<string, object>>(d =>
                        ReferenceEquals(d["selectedSong"], song) &&
                        (int)d["selectedDifficulty"] == 2 &&
                        (int)d["songId"] == 17)),
                Times.Once);
            Assert.Equal(2.0, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void SelectSong_WhenNodeIsNull_ShouldNotTransition()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "SelectSong", new object?[] { null });

            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void SelectSong_WhenNodeIsNotScoreType_ShouldNotTransition()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var box = CreateBoxNode("Folder");

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "SelectSong", box);

            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void SelectSong_WhenDebounceBlocksTransition_ShouldNotTransition()
        {
            var game = CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "SelectSong", CreateScoreNode("Song", songId: 12));

            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void SelectRandomSong_WhenSinglePlayableSongExists_ShouldSelectIt()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var song = CreateScoreNode("Only Song", songId: 45);

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>
            {
                CreateBoxNode("Folder"),
                song
            });

            InvokePrivateMethod(stage, "SelectRandomSong");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.IsAny<IStageTransition>(),
                    It.Is<Dictionary<string, object>>(d => ReferenceEquals(d["selectedSong"], song))),
                Times.Once);
        }

        [Fact]
        public void SelectRandomSong_WhenNoPlayableSongsExist_ShouldNotTransition()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>
            {
                CreateBoxNode("Folder"),
                new SongListNode { Type = NodeType.Random, Title = "Random" },
                new SongListNode { Type = NodeType.BackBox, Title = ".." }
            });

            InvokePrivateMethod(stage, "SelectRandomSong");

            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void SelectRandomSong_WhenCurrentSongListIsEmpty_ShouldNotTransition()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>());

            InvokePrivateMethod(stage, "SelectRandomSong");

            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>(), It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void SelectRandomSong_WhenCurrentSongListIsNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_currentSongList", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "SelectRandomSong"));

            Assert.Null(exception);
        }

        [Fact]
        public void OnScrollSpeedChanged_ShouldNotThrow()
        {
            var stage = CreateStage();
            var args = new ScrollSpeedChangedEventArgs(50, 75);

            var exception = Record.Exception(() =>
                InvokePrivateMethod(stage, "OnScrollSpeedChanged", stage, args));

            Assert.Null(exception);
        }

        [Fact]
        public void Deactivate_ShouldFlushPendingSave()
        {
            var configManager = new Mock<IConfigManager>();
            var stage = CreateStage();
            SetPrivateField(stage, "_configManager", configManager.Object);

            stage.Deactivate();

            configManager.Verify(x => x.FlushPendingSave(), Times.Once);
        }

        [Fact]
        public void Deactivate_WhenConfigManagerNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_configManager", null);

            var exception = Record.Exception(() => stage.Deactivate());

            Assert.Null(exception);
        }

        [Fact]
        public void NavigateIntoBox_ShouldPushStateAndPopulateChildren()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var childSong = CreateScoreNode("Child Song");
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var box = CreateBoxNode("Folder", childSong);

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", rootSongs);
            SetPrivateField(stage, "_currentBreadcrumb", "Root");

            InvokePrivateMethod(stage, "NavigateIntoBox", box);

            Assert.Equal(1, GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Count);
            var currentList = GetPrivateField<List<SongListNode>>(stage, "_currentSongList");
            Assert.NotNull(currentList);
            Assert.Single(currentList!);
            Assert.Equal("Root > Folder", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Equal(NodeType.BackBox, display.CurrentList[0].Type);
            Assert.Same(childSong, display.CurrentList[1]);
        }

        [Fact]
        public void NavigateIntoBox_WhenBreadcrumbIsEmpty_ShouldUseTitleWithoutSeparator()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var childSong = CreateScoreNode("Child");
            var box = CreateBoxNode("Folder", childSong);

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("Root") });
            SetPrivateField(stage, "_currentBreadcrumb", "");

            InvokePrivateMethod(stage, "NavigateIntoBox", box);

            Assert.Equal("Folder", GetPrivateField<string>(stage, "_currentBreadcrumb"));
        }

        [Fact]
        public void NavigateIntoBox_WhenChildrenAreEmpty_ShouldNotNavigate()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var box = CreateBoxNode("Empty");

            AttachCoreUi(stage, display: display);
            var originalList = new List<SongListNode> { CreateScoreNode("Root") };
            SetPrivateField(stage, "_currentSongList", originalList);
            SetPrivateField(stage, "_currentBreadcrumb", "Root");

            InvokePrivateMethod(stage, "NavigateIntoBox", box);

            Assert.Same(originalList, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
        }

        [Fact]
        public void NavigateBack_ShouldRestorePreviousState()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var stack = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!;

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("Child") });
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Child");
            stack.Push(new SongListNode { Children = rootSongs, Title = "Root" });

            InvokePrivateMethod(stage, "NavigateBack");

            Assert.Same(rootSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Empty(stack);
        }

        [Fact]
        public void NavigateBack_WhenStackEmpty_ShouldLeaveStateUnchanged()
        {
            var stage = CreateStage();
            var songs = new List<SongListNode> { CreateScoreNode("Root") };

            SetPrivateField(stage, "_currentSongList", songs);
            SetPrivateField(stage, "_currentBreadcrumb", "Root");

            InvokePrivateMethod(stage, "NavigateBack");

            Assert.Same(songs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
        }

        [Fact]
        public void HandleSongActivation_BackBox_ShouldNavigateBack()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root") };
            var stack = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!;

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("Child") });
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Child");
            stack.Push(new SongListNode { Children = rootSongs, Title = "Root" });

            InvokePrivateMethod(stage, "HandleSongActivation", new SongListNode { Type = NodeType.BackBox, Title = ".." });

            Assert.Same(rootSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
        }

        [Fact]
        public void HandleSongActivation_Box_ShouldNavigateIntoBox()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var child = CreateScoreNode("Child");
            var box = CreateBoxNode("Folder", child);

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { box });

            InvokePrivateMethod(stage, "HandleSongActivation", box);

            Assert.Same(box.Children, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
        }

        [Fact]
        public void HandleSongActivation_Score_ShouldSelectSong()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var song = CreateScoreNode("Song", songId: 21);

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "HandleSongActivation", song);

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.IsAny<IStageTransition>(),
                    It.Is<Dictionary<string, object>>(d => ReferenceEquals(d["selectedSong"], song))),
                Times.Once);
        }

        [Fact]
        public void HandleSongActivation_Random_ShouldSelectRandomSong()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var song = CreateScoreNode("Song", songId: 22);

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { song });

            InvokePrivateMethod(stage, "HandleSongActivation", new SongListNode { Type = NodeType.Random, Title = "Random" });

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.IsAny<IStageTransition>(),
                    It.Is<Dictionary<string, object>>(d => ReferenceEquals(d["selectedSong"], song))),
                Times.Once);
        }

        private static void AttachCoreUi(
            SongSelectionStage stage,
            SongListDisplay? display = null,
            SongStatusPanel? statusPanel = null,
            PreviewImagePanel? previewPanel = null,
            UILabel? breadcrumb = null)
        {
            SetPrivateField(stage, "_songListDisplay", display ?? new SongListDisplay());
            SetPrivateField(stage, "_statusPanel", statusPanel ?? new SongStatusPanel());
            SetPrivateField(stage, "_previewImagePanel", previewPanel ?? new PreviewImagePanel());
            SetPrivateField(stage, "_breadcrumbLabel", breadcrumb ?? new UILabel());
        }

        private static SongSelectionStage CreateStage(BaseGame? game = null)
        {
            return new SongSelectionStage(game ?? CreateGame());
        }

        private static SongListNode CreateScoreNode(
            string title,
            SongScore[]? scores = null,
            int? songId = null,
            SongChart? chart = null)
        {
            chart ??= new SongChart
            {
                FilePath = CreateWorkspacePath("charts", $"{Guid.NewGuid():N}.dtx"),
                HasDrumChart = true,
                DrumLevel = 35
            };

            var song = new SongEntity
            {
                Title = title,
                Artist = "Test Artist",
                Charts = [chart]
            };
            chart.Song = song;

            return new SongListNode
            {
                Type = NodeType.Score,
                Title = title,
                Scores = scores ?? CreateScores(0),
                DatabaseSong = song,
                DatabaseChart = chart,
                DatabaseSongId = songId
            };
        }

        private static SongListNode CreateBoxNode(string title, params SongListNode[] children)
        {
            return new SongListNode
            {
                Type = NodeType.Box,
                Title = title,
                Children = new List<SongListNode>(children)
            };
        }

        private static SongScore[] CreateScores(params int[] difficulties)
        {
            var scores = new SongScore[5];
            foreach (var difficulty in difficulties)
            {
                scores[difficulty] = new SongScore
                {
                    DifficultyLevel = difficulty,
                    DifficultyLabel = $"Level {difficulty}"
                };
            }

            return scores;
        }

        private static string CreateWorkspacePath(params string[] parts)
        {
            var path = Path.Combine(AppContext.BaseDirectory, "TestResults", "song-selection-stage-coverage");
            Directory.CreateDirectory(path);
            foreach (var part in parts)
            {
                path = Path.Combine(path, part);
            }

            return path;
        }

        private sealed class QueuedInputManager : InputManager
        {
            public void Enqueue(InputCommand command)
            {
                EnqueueCommand(command);
            }
        }

        /// <summary>
        /// InputManager subclass that supports both command enqueuing and simulated
        /// Backspace key press. Used to test the modal-open-frame race condition.
        /// </summary>
        private sealed class BackspaceInputManager : InputManager
        {
            private bool _backspacePressed;

            public void Enqueue(InputCommand command)
            {
                EnqueueCommand(command);
            }

            public BackspaceInputManager WithBackspace()
            {
                _backspacePressed = true;
                return this;
            }

            public override bool IsKeyPressed(int keyCode)
            {
                if (keyCode == (int)Microsoft.Xna.Framework.Input.Keys.Back && _backspacePressed)
                    return true;
                return base.IsKeyPressed(keyCode);
            }
        }

        [Fact]
        public void HandleInput_WhenModalJustOpened_ShouldNotProcessStageCommands()
        {
            // Simulate a frame where Backspace opens the modal AND a MoveDown command
            // is already queued. The modal should block the stage command on this frame.
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };
            var inputManager = new BackspaceInputManager();

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_inputManager", inputManager.WithBackspace());
            // Queue a navigation command that should NOT execute on this frame
            inputManager.Enqueue(new InputCommand(InputCommandType.MoveDown, 0.0));

            // Set up the search filter modal
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "HandleInput");

            // Modal should be open
            Assert.True(modal.IsOpen);
            // Selection should NOT have moved (MoveDown was blocked)
            Assert.Equal("A", display.SelectedSong!.Title);
        }
    }
}

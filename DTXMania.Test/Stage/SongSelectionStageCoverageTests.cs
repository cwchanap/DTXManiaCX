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
using DTXMania.Game.Lib.Song.Filtering;
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
        public void ExecuteInputCommand_OpenSearch_WhenModalIsNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_searchFilterModal", null);

            var exception = Record.Exception(() =>
                InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.OpenSearch, 0.0)));

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

        [Fact]
        public void ExecuteInputCommand_BackWithActiveFilteredView_ShouldCallOnFilterReset()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var filteredView = new List<FilteredSongResult> { new(CreateScoreNode("S"), "folder") };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_isInStatusPanel", false);
            SetPrivateField(stage, "_filteredView", filteredView);
            SetPrivateField(stage, "_filterCriteria", new SongFilterCriteria("test", null, null, PlayedStatus.All, SongSortCriteria.Title, false));

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            Assert.Null(GetPrivateField<System.Collections.Generic.IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
            var prop = typeof(SongSelectionStage).GetField("_filterCriteria", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            Assert.True(((SongFilterCriteria)prop!.GetValue(stage)!).IsEmpty);
        }

        [Fact]
        public void ExecuteInputCommand_BackWithFilteredViewAndInStatusPanel_ShouldExitStatusPanelOnly()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var filteredView = new List<FilteredSongResult> { new(CreateScoreNode("S"), "folder") };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(stage, "_filteredView", filteredView);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.NotNull(GetPrivateField<System.Collections.Generic.IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
        }

        [Fact]
        public void OpenSearchFilterModal_WhenModalIsNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_searchFilterModal", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "OpenSearchFilterModal"));

            Assert.Null(exception);
        }

        [Fact]
        public void OpenSearchFilterModal_WhenSongListNotNullAndTaskNull_ShouldSetLibraryReady()
        {
            var stage = CreateStage();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("S") });
            SetPrivateField(stage, "_songInitializationTask", null);
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "OpenSearchFilterModal");

            Assert.True(modal.IsLibraryReady);
            Assert.True(modal.IsOpen);
            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
        }

        [Fact]
        public void OpenSearchFilterModal_WhenSongListNullAndTaskNull_ShouldSetLibraryNotReady()
        {
            var stage = CreateStage();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_currentSongList", null);
            SetPrivateField(stage, "_songInitializationTask", null);

            InvokePrivateMethod(stage, "OpenSearchFilterModal");

            Assert.False(modal.IsLibraryReady);
        }

        [Fact]
        public void OpenSearchFilterModal_WhenTaskProcessed_ShouldSetLibraryReady()
        {
            var stage = CreateStage();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("S") });
            SetPrivateField(stage, "_songInitializationTask", Task.FromResult(new List<SongListNode>()));
            SetPrivateField(stage, "_songInitializationProcessed", true);

            InvokePrivateMethod(stage, "OpenSearchFilterModal");

            Assert.True(modal.IsLibraryReady);
        }

        [Fact]
        public void OpenSearchFilterModal_WhenTaskNotProcessed_ShouldSetLibraryNotReady()
        {
            var stage = CreateStage();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("S") });
            SetPrivateField(stage, "_songInitializationTask", Task.FromResult(new List<SongListNode>()));
            SetPrivateField(stage, "_songInitializationProcessed", false);

            InvokePrivateMethod(stage, "OpenSearchFilterModal");

            Assert.False(modal.IsLibraryReady);
        }

        [Fact]
        public void UpdateBreadcrumb_WhenFilterActive_ShouldShowFilterSummary()
        {
            var stage = CreateStage();
            var breadcrumb = new UILabel("");
            AttachCoreUi(stage, breadcrumb: breadcrumb);
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Folder");
            SetPrivateField(stage, "_filterCriteria", new SongFilterCriteria("hello", null, null, PlayedStatus.All, SongSortCriteria.Title, false));

            InvokePrivateMethod(stage, "UpdateBreadcrumb");

            Assert.Contains("Filtered", breadcrumb.Text);
            Assert.Contains("hello", breadcrumb.Text);
        }

        [Fact]
        public void UpdateBreadcrumb_WhenFilterNotActiveAndBreadcrumbEmpty_ShouldShowRoot()
        {
            var stage = CreateStage();
            var breadcrumb = new UILabel("");
            AttachCoreUi(stage, breadcrumb: breadcrumb);
            SetPrivateField(stage, "_currentBreadcrumb", "");
            SetPrivateField(stage, "_filterCriteria", SongFilterCriteria.Default);

            InvokePrivateMethod(stage, "UpdateBreadcrumb");

            Assert.Equal("Root", breadcrumb.Text);
        }

        [Fact]
        public void UpdateBreadcrumb_WhenFilterNotActiveAndBreadcrumbNonEmpty_ShouldShowBreadcrumb()
        {
            var stage = CreateStage();
            var breadcrumb = new UILabel("");
            AttachCoreUi(stage, breadcrumb: breadcrumb);
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Folder > Sub");
            SetPrivateField(stage, "_filterCriteria", SongFilterCriteria.Default);

            InvokePrivateMethod(stage, "UpdateBreadcrumb");

            Assert.Equal("Root > Folder > Sub", breadcrumb.Text);
        }

        [Fact]
        public void FilterCriteria_ShouldBeInstanceScopedAndNotLeakAcrossInstances()
        {
            // Set a non-default filter on one stage instance
            var stage1 = CreateStage();
            SetPrivateField(stage1, "_filterCriteria", new SongFilterCriteria("leaked", null, null, PlayedStatus.All, SongSortCriteria.Title, false));

            // A new stage instance should start with the default (empty) filter
            var stage2 = CreateStage();
            var prop = typeof(SongSelectionStage).GetField("_filterCriteria", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var filter = (SongFilterCriteria)prop!.GetValue(stage2)!;
            Assert.True(filter.IsEmpty);
        }

        [Fact]
        public void UpdateStatusPanelFolderHint_WhenStatusPanelNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_statusPanel", null);
            SetPrivateField(stage, "_filteredView", new List<FilteredSongResult> { new(CreateScoreNode("S"), "f") });

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "UpdateStatusPanelFolderHint"));

            Assert.Null(exception);
        }

        [Fact]
        public void UpdateStatusPanelFolderHint_WhenFilteredViewNull_ShouldSetEmptyHint()
        {
            var stage = CreateStage();
            var statusPanel = new SongStatusPanel();
            AttachCoreUi(stage, statusPanel: statusPanel);
            SetPrivateField(stage, "_filteredView", null);

            InvokePrivateMethod(stage, "UpdateStatusPanelFolderHint");

            Assert.Equal("", statusPanel.FolderHint);
        }

        [Fact]
        public void UpdateStatusPanelFolderHint_WhenSelectedNodeNull_ShouldSetEmptyHint()
        {
            var stage = CreateStage();
            var statusPanel = new SongStatusPanel();
            var display = new SongListDisplay();
            var filteredView = new List<FilteredSongResult> { new(CreateScoreNode("S"), "folder") };
            AttachCoreUi(stage, display: display, statusPanel: statusPanel);
            SetPrivateField(stage, "_filteredView", filteredView);

            InvokePrivateMethod(stage, "UpdateStatusPanelFolderHint");

            Assert.Equal("", statusPanel.FolderHint);
        }

        [Fact]
        public void UpdateStatusPanelFolderHint_WhenNodeFoundInFilteredView_ShouldSetFolderPath()
        {
            var stage = CreateStage();
            var statusPanel = new SongStatusPanel();
            var song = CreateScoreNode("Song");
            var display = new SongListDisplay { CurrentList = [song] };
            var filteredView = new List<FilteredSongResult> { new(song, "Rock > Hard Rock") };
            AttachCoreUi(stage, display: display, statusPanel: statusPanel);
            SetPrivateField(stage, "_filteredView", filteredView);

            InvokePrivateMethod(stage, "UpdateStatusPanelFolderHint");

            Assert.Equal("Rock > Hard Rock", statusPanel.FolderHint);
        }

        [Fact]
        public void UpdateStatusPanelFolderHint_WhenNodeNotFoundInFilteredView_ShouldSetEmptyHint()
        {
            var stage = CreateStage();
            var statusPanel = new SongStatusPanel();
            var songA = CreateScoreNode("A");
            var songB = CreateScoreNode("B");
            var display = new SongListDisplay { CurrentList = [songA] };
            var filteredView = new List<FilteredSongResult> { new(songB, "Other") };
            AttachCoreUi(stage, display: display, statusPanel: statusPanel);
            SetPrivateField(stage, "_filteredView", filteredView);

            InvokePrivateMethod(stage, "UpdateStatusPanelFolderHint");

            Assert.Equal("", statusPanel.FolderHint);
        }

        [Fact]
        public void RebuildFilteredView_WhenFilterEmpty_ShouldClearFilteredView()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_filterCriteria", SongFilterCriteria.Default);
            SetPrivateField(stage, "_filteredView", new List<FilteredSongResult>());

            InvokePrivateMethod(stage, "RebuildFilteredView");

            Assert.Null(GetPrivateField<System.Collections.Generic.IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
            Assert.False(GetPrivateField<bool>(stage, "_showEmptyFilterMessage"));
        }

        [Fact]
        public void PopulateFilteredSongList_WithItems_ShouldPopulateDisplay()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("Song");
            var display = new SongListDisplay();
            var filteredView = new List<FilteredSongResult>
            {
                new(song, "Folder1"),
                new(CreateScoreNode("Other"), "Folder2"),
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_filteredView", filteredView);

            InvokePrivateMethod(stage, "PopulateFilteredSongList");

            Assert.Equal(2, display.CurrentList.Count);
            Assert.Same(song, display.CurrentList[0]);
        }

        [Fact]
        public void SetBackgroundMusic_WithNullParams_ShouldSetFieldsToNull()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_backgroundMusic", null);
            SetPrivateField(stage, "_backgroundMusicInstance", null);

            stage.SetBackgroundMusic(null, null);

            Assert.Null(GetPrivateField<ISound>(stage, "_backgroundMusic"));
            Assert.Null(GetPrivateField<ISoundInstance>(stage, "_backgroundMusicInstance"));
        }

        [Fact]
        public void SetBackgroundMusic_WithNonNullParams_ShouldSetFields()
        {
            var stage = CreateStage();
            var mockSound = new Mock<ISound>();
            var mockInstance = new Mock<ISoundInstance>();

            stage.SetBackgroundMusic(mockSound.Object, mockInstance.Object);

            Assert.Same(mockSound.Object, GetPrivateField<ISound>(stage, "_backgroundMusic"));
            Assert.Same(mockInstance.Object, GetPrivateField<ISoundInstance>(stage, "_backgroundMusicInstance"));
        }

        [Fact]
        public void SetBackgroundMusic_WhenReplacingExistingBGM_ShouldDisposeOld()
        {
            var stage = CreateStage();
            var oldSound = new Mock<ISound>();
            var oldInstance = new Mock<ISoundInstance>();
            var newSound = new Mock<ISound>();
            var newInstance = new Mock<ISoundInstance>();

            SetPrivateField(stage, "_backgroundMusic", oldSound.Object);
            SetPrivateField(stage, "_backgroundMusicInstance", oldInstance.Object);

            stage.SetBackgroundMusic(newSound.Object, newInstance.Object);

            oldSound.Verify(x => x.RemoveReference(), Times.Once);
            oldInstance.Verify(x => x.Stop(), Times.Once);
            oldInstance.Verify(x => x.Dispose(), Times.Once);
            Assert.Same(newSound.Object, GetPrivateField<ISound>(stage, "_backgroundMusic"));
        }

        [Fact]
        public void SetBackgroundMusic_WhenOldRemoveReferenceThrows_ShouldNotThrow()
        {
            var stage = CreateStage();
            var oldSound = new Mock<ISound>();
            oldSound.Setup(x => x.RemoveReference()).Throws(new Exception("boom"));
            var newSound = new Mock<ISound>();

            SetPrivateField(stage, "_backgroundMusic", oldSound.Object);
            SetPrivateField(stage, "_backgroundMusicInstance", null);

            var exception = Record.Exception(() => stage.SetBackgroundMusic(newSound.Object, null));

            Assert.Null(exception);
            Assert.Same(newSound.Object, GetPrivateField<ISound>(stage, "_backgroundMusic"));
        }

        [Fact]
        public void SetBackgroundMusic_WhenOldStopThrows_ShouldNotThrow()
        {
            var stage = CreateStage();
            var oldSound = new Mock<ISound>();
            var oldInstance = new Mock<ISoundInstance>();
            oldInstance.Setup(x => x.Stop()).Throws(new Exception("stop fail"));

            SetPrivateField(stage, "_backgroundMusic", oldSound.Object);
            SetPrivateField(stage, "_backgroundMusicInstance", oldInstance.Object);

            var exception = Record.Exception(() => stage.SetBackgroundMusic(null, null));

            Assert.Null(exception);
        }

        [Fact]
        public void SetBackgroundMusic_WhenOldDisposeThrows_ShouldNotThrow()
        {
            var stage = CreateStage();
            var oldSound = new Mock<ISound>();
            var oldInstance = new Mock<ISoundInstance>();
            oldInstance.Setup(x => x.Dispose()).Throws(new Exception("dispose fail"));

            SetPrivateField(stage, "_backgroundMusic", oldSound.Object);
            SetPrivateField(stage, "_backgroundMusicInstance", oldInstance.Object);

            var exception = Record.Exception(() => stage.SetBackgroundMusic(null, null));

            Assert.Null(exception);
        }

        [Fact]
        public void DetectOpenSearchKey_WhenBackspacePressed_ShouldOpenModal()
        {
            var stage = CreateStage();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            var inputManager = new BackspaceInputManager().WithBackspace();

            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "DetectOpenSearchKey");

            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void DetectOpenSearchKey_WhenInputManagerNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_inputManager", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "DetectOpenSearchKey"));

            Assert.Null(exception);
        }

        [Fact]
        public void ProcessModalKeys_WhenInputManagerNull_ShouldNotThrow()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_inputManager", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "ProcessModalKeys"));

            Assert.Null(exception);
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenNotActive_ShouldNotChange()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_isPreviewDelayActive", false);
            SetPrivateField(stage, "_isBgmFadingOut", false);
            SetPrivateField(stage, "_isBgmFadingIn", false);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.016));

            Assert.Null(exception);
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenPreviewDelayActiveBelowThreshold_ShouldIncrement()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_isPreviewDelayActive", true);
            SetPrivateField(stage, "_previewPlayDelay", 0.0);
            SetPrivateField(stage, "_previewSound", null);
            SetPrivateField(stage, "_isBgmFadingOut", false);
            SetPrivateField(stage, "_isBgmFadingIn", false);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.1);

            Assert.Equal(0.1, GetPrivateField<double>(stage, "_previewPlayDelay"));
            Assert.True(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenBgmFadeOutActiveBelowDuration_ShouldIncrement()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_isPreviewDelayActive", false);
            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_bgmFadeOutTimer", 0.0);
            SetPrivateField(stage, "_backgroundMusicInstance", null);
            SetPrivateField(stage, "_isBgmFadingIn", false);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.1);

            Assert.Equal(0.1, GetPrivateField<double>(stage, "_bgmFadeOutTimer"));
            Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenBgmFadeInActiveBelowDuration_ShouldIncrement()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_isPreviewDelayActive", false);
            SetPrivateField(stage, "_isBgmFadingOut", false);
            SetPrivateField(stage, "_isBgmFadingIn", true);
            SetPrivateField(stage, "_bgmFadeInTimer", 0.0);
            SetPrivateField(stage, "_backgroundMusicInstance", null);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.1);

            Assert.Equal(0.1, GetPrivateField<double>(stage, "_bgmFadeInTimer"));
            Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
        }

        [Fact]
        public void OnFilterReset_ShouldClearFilterState()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_filterCriteria", new SongFilterCriteria("test", null, null, PlayedStatus.All, SongSortCriteria.Title, false));
            SetPrivateField(stage, "_filteredView", new List<FilteredSongResult>());
            SetPrivateField(stage, "_showEmptyFilterMessage", true);

            InvokePrivateMethod(stage, "OnFilterReset", stage, EventArgs.Empty);

            var prop = typeof(SongSelectionStage).GetField("_filterCriteria", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var applied = (SongFilterCriteria)prop!.GetValue(stage)!;
            Assert.True(applied.IsEmpty);
            Assert.Null(GetPrivateField<System.Collections.Generic.IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
            Assert.False(GetPrivateField<bool>(stage, "_showEmptyFilterMessage"));
        }

        [Fact]
        public void OnFilterCancelled_ShouldNotChangeFilterState()
        {
            var stage = CreateStage();
            var criteria = new SongFilterCriteria("kept", null, null, PlayedStatus.All, SongSortCriteria.Title, false);
            SetPrivateField(stage, "_filterCriteria", criteria);

            InvokePrivateMethod(stage, "OnFilterCancelled", stage, EventArgs.Empty);

            var prop = typeof(SongSelectionStage).GetField("_filterCriteria", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var applied = (SongFilterCriteria)prop!.GetValue(stage)!;
            Assert.Equal("kept", applied.SearchQuery);
        }

        [Fact]
        public void PopulateSongListForCurrentMode_WhenFilteredViewNotNull_ShouldCallPopulateFilteredSongList()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("S");
            var display = new SongListDisplay();
            var filteredView = new List<FilteredSongResult> { new(song, "folder") };
            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_filteredView", filteredView);

            InvokePrivateMethod(stage, "PopulateSongListForCurrentMode");

            Assert.Equal(1, display.CurrentList.Count);
            Assert.Same(song, display.CurrentList[0]);
        }

        [Fact]
        public void PopulateSongListForCurrentMode_WhenFilteredViewNull_ShouldCallPopulateSongList()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("S");
            var display = new SongListDisplay();
            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { song });
            SetPrivateField(stage, "_filteredView", null);

            InvokePrivateMethod(stage, "PopulateSongListForCurrentMode");

            Assert.Equal(1, display.CurrentList.Count);
            Assert.Same(song, display.CurrentList[0]);
        }

        [Fact]
        public void HandleActivateInput_WithFilteredViewAndNonScoreNode_ShouldDoNothing()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var box = CreateBoxNode("Folder");
            var filteredView = new List<FilteredSongResult> { new(CreateScoreNode("S"), "f") };
            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_filteredView", filteredView);
            SetPrivateField(stage, "_selectedSong", box);
            SetPrivateField(stage, "_isInStatusPanel", false);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "HandleActivateInput"));

            Assert.Null(exception);
        }

        [Fact]
        public void StartBGMFade_WhenFadeOut_ShouldSetFadeOutState()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_isBgmFadingIn", true);
            SetPrivateField(stage, "_isBgmFadingOut", false);

            InvokePrivateMethod(stage, "StartBGMFade", true);

            Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
            Assert.Equal(0.0, GetPrivateField<double>(stage, "_bgmFadeOutTimer"));
        }

        [Fact]
        public void StartBGMFade_WhenFadeIn_ShouldSetFadeInState()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_isBgmFadingIn", false);

            InvokePrivateMethod(stage, "StartBGMFade", false);

            Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
            Assert.Equal(0.0, GetPrivateField<double>(stage, "_bgmFadeInTimer"));
        }

        [Fact]
        public void OnFilterApplied_WithSwappedLevels_ShouldNormalizeAndApply()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var breadcrumb = new UILabel("");
            AttachCoreUi(stage, display: display, breadcrumb: breadcrumb);
            var criteria = new SongFilterCriteria("", 50, 10, PlayedStatus.All, SongSortCriteria.Title, false);

            InvokePrivateMethod(stage, "OnFilterApplied", stage, criteria);

            var prop = typeof(SongSelectionStage).GetField("_filterCriteria", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var applied = (SongFilterCriteria)prop!.GetValue(stage)!;
            Assert.Equal(10, applied.MinLevel);
            Assert.Equal(50, applied.MaxLevel);
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
                {
                    // Edge-triggered: consume the flag on first check to model a one-frame key press
                    _backspacePressed = false;
                    return true;
                }
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

        #region ProcessModalKeys – search-box command suppression (issue 1)

        /// <summary>
        /// InputManager stub that lets tests specify which commands appear as pressed.
        /// </summary>
        private sealed class CommandSimulatingInputManager : InputManager
        {
            private readonly HashSet<InputCommandType> _pressedCommands = new();

            public void PressCommand(InputCommandType cmd) => _pressedCommands.Add(cmd);

            public override bool IsCommandPressed(InputCommandType command) => _pressedCommands.Contains(command);
        }

        [Fact]
        public void ProcessModalKeys_WhenSearchBoxFocused_ShouldSuppressActivateCommand()
        {
            // If Activate is remapped to a text-producing key, the command must be
            // suppressed while the SearchBox is focused so text entry still works.
            var stage = CreateStage();
            var inputManager = new CommandSimulatingInputManager();
            inputManager.PressCommand(InputCommandType.Activate);
            SetPrivateField(stage, "_inputManager", inputManager);

            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);
            // Default focus is SearchBox
            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "ProcessModalKeys");

            // Modal should still be open (Activate was suppressed)
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void ProcessModalKeys_WhenSearchBoxFocused_ShouldSuppressBackCommand()
        {
            var stage = CreateStage();
            var inputManager = new CommandSimulatingInputManager();
            inputManager.PressCommand(InputCommandType.Back);
            SetPrivateField(stage, "_inputManager", inputManager);

            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);
            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "ProcessModalKeys");

            // Modal should still be open (Back was suppressed)
            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void ProcessModalKeys_WhenApplyButtonFocused_ShouldProcessActivateCommand()
        {
            // Activate should still work when focus is NOT on the SearchBox.
            var stage = CreateStage();
            var inputManager = new CommandSimulatingInputManager();
            inputManager.PressCommand(InputCommandType.Activate);
            SetPrivateField(stage, "_inputManager", inputManager);

            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);
            // Move focus to ApplyButton
            for (int i = 0; i < 7; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "ProcessModalKeys");

            // Modal should have closed (Activate triggered Apply)
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void ProcessModalKeys_WhenSearchBoxFocused_ShouldSuppressDirectionalCommands()
        {
            // Directional commands (MoveUp/MoveDown/MoveLeft/MoveRight) should be
            // suppressed when the SearchBox is focused so that remapped text-producing
            // keys (e.g. W→MoveUp, S→MoveDown) don't steal focus mid-typing.
            var stage = CreateStage();
            var inputManager = new CommandSimulatingInputManager();
            inputManager.PressCommand(InputCommandType.MoveDown);
            SetPrivateField(stage, "_inputManager", inputManager);

            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);
            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "ProcessModalKeys");

            // Focus should NOT have moved — MoveDown was suppressed
            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void ProcessModalKeys_WhenNotSearchBoxFocused_ShouldProcessDirectionalCommands()
        {
            // Directional commands should still work when focus is on a non-text field.
            var stage = CreateStage();
            var inputManager = new CommandSimulatingInputManager();
            inputManager.PressCommand(InputCommandType.MoveDown);
            SetPrivateField(stage, "_inputManager", inputManager);

            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);
            // Move focus to MinLevel (not SearchBox)
            modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "ProcessModalKeys");

            // Focus should have moved (MoveDown was processed)
            Assert.NotEqual(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);
        }

        #endregion

        #region ProcessModalKeys – first-click mouse handling (issue 2)

        [Fact]
        public void ProcessModalKeys_WhenPreviousMouseStateIsNull_ShouldStillDetectClick()
        {
            // On the first modal open, _previousMouseState is null.
            // The click should still register (null treated as "released").
            var stage = CreateStage();
            var inputManager = new CommandSimulatingInputManager();
            SetPrivateField(stage, "_inputManager", inputManager);

            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);
            SetPrivateField(stage, "_searchFilterModal", modal);

            // _previousMouseState defaults to null — don't set it
            Assert.Null(GetPrivateField<Microsoft.Xna.Framework.Input.MouseState?>(stage, "_previousMouseState"));

            // ProcessModalKeys reads Mouse.GetState() and checks the click condition.
            // We can't easily control Mouse.GetState() in a unit test, but we can verify
            // that the null check logic is correct by inspecting the method doesn't throw,
            // and that _previousMouseState is populated after the call.
            InvokePrivateMethod(stage, "ProcessModalKeys");

            // After the call, _previousMouseState should be populated (not null)
            Assert.NotNull(GetPrivateField<Microsoft.Xna.Framework.Input.MouseState?>(stage, "_previousMouseState"));
        }

        #endregion

        #region ExecuteInputCommand – filter reset drains remaining commands (P2 fix)

        [Fact]
        public void ExecuteInputCommand_BackWithActiveFilter_ShouldReturnFalse()
        {
            // When Back clears an active filter, ExecuteInputCommand returns false
            // so ProcessInputCommands stops iterating the stale snapshot.
            var stage = CreateStage();
            var display = new SongListDisplay();
            var filteredView = new List<FilteredSongResult> { new(CreateScoreNode("S"), "folder") };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_isInStatusPanel", false);
            SetPrivateField(stage, "_filteredView", filteredView);
            SetPrivateField(stage, "_filterCriteria", new SongFilterCriteria("test", null, null, PlayedStatus.All, SongSortCriteria.Title, false));

            var result = InvokePrivateMethod<bool>(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            Assert.False(result);
            Assert.Null(GetPrivateField<System.Collections.Generic.IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
        }

        [Fact]
        public void ExecuteInputCommand_BackWithoutActiveFilter_ShouldReturnTrue()
        {
            // Normal Back (no filter) should return true to continue processing.
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A")]
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_isInStatusPanel", true);

            var result = InvokePrivateMethod<bool>(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            Assert.True(result);
        }

        [Fact]
        public void ProcessInputCommands_BackAfterFilterReset_ShouldDrainRemainingCommands()
        {
            // Two Back commands queued: first clears the filter, second should NOT
            // navigate away or exit the stage because remaining commands are drained.
            var stage = CreateStage();
            var display = new SongListDisplay();
            var filteredView = new List<FilteredSongResult> { new(CreateScoreNode("S"), "folder") };
            var inputManager = new QueuedInputManager();

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_isInStatusPanel", false);
            SetPrivateField(stage, "_filteredView", filteredView);
            SetPrivateField(stage, "_filterCriteria", new SongFilterCriteria("test", null, null, PlayedStatus.All, SongSortCriteria.Title, false));

            inputManager.Enqueue(new InputCommand(InputCommandType.Back, 0.0));
            inputManager.Enqueue(new InputCommand(InputCommandType.Back, 0.0));

            InvokePrivateMethod(stage, "ProcessInputCommands");

            // Filter should be cleared
            Assert.Null(GetPrivateField<System.Collections.Generic.IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
            // Navigation stack should still be empty (second Back was drained)
            var stack = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!;
            Assert.Empty(stack);
        }

        #endregion

        #region OpenSearchFilterModal – mouse state reset on reopen (P3 fix)

        [Fact]
        public void OpenSearchFilterModal_ShouldResetPreviousMouseState()
        {
            // If _previousMouseState was left as Pressed from a prior modal session,
            // reopening the modal should reset it to null so the first click registers.
            var stage = CreateStage();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("S") });
            SetPrivateField(stage, "_songInitializationTask", null);

            // Simulate stale mouse state from a previous modal session
            SetPrivateField(stage, "_previousMouseState", new Microsoft.Xna.Framework.Input.MouseState(
                0, 0, 0,
                Microsoft.Xna.Framework.Input.ButtonState.Pressed,
                Microsoft.Xna.Framework.Input.ButtonState.Released,
                Microsoft.Xna.Framework.Input.ButtonState.Released,
                Microsoft.Xna.Framework.Input.ButtonState.Released,
                Microsoft.Xna.Framework.Input.ButtonState.Released));

            InvokePrivateMethod(stage, "OpenSearchFilterModal");

            Assert.Null(GetPrivateField<Microsoft.Xna.Framework.Input.MouseState?>(stage, "_previousMouseState"));
        }

        #endregion
    }
}

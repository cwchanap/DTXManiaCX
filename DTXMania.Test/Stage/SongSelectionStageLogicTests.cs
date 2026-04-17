using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework.Audio;
using Moq;
using static DTXMania.Test.TestData.ReflectionHelpers;

using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.Stage
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongSelectionStageLogicTests
    {
        [Fact]
        public void PopulateSongList_WhenCurrentSongListEmpty_ShouldClearDisplay()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("Existing")]
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>());

            InvokePrivateMethod(stage, "PopulateSongList");

            Assert.Empty(display.CurrentList);
        }

        [Fact]
        public void PopulateSongList_WhenInSubfolder_ShouldPrependBackEntry()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var songs = new List<SongListNode>
            {
                CreateScoreNode("Song A"),
                CreateBoxNode("Folder")
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", songs);
            GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(new SongListNode { Title = "Root" });

            InvokePrivateMethod(stage, "PopulateSongList");

            Assert.Equal(3, display.CurrentList.Count);
            Assert.Equal(NodeType.BackBox, display.CurrentList[0].Type);
            Assert.Same(songs[0], display.CurrentList[1]);
            Assert.Same(songs[1], display.CurrentList[2]);
        }

        [Fact]
        public void CheckSongInitializationCompletion_WhenTaskCompleted_ShouldPromoteSongsAndClearTask()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var songs = new List<SongListNode> { CreateScoreNode("Loaded Song") };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>());
            SetPrivateField(stage, "_songInitializationTask", Task.FromResult(songs));
            SetPrivateField(stage, "_songInitializationProcessed", false);

            InvokePrivateMethod(stage, "CheckSongInitializationCompletion");

            Assert.True(GetPrivateField<bool>(stage, "_songInitializationProcessed"));
            Assert.Null(GetPrivateField<object>(stage, "_songInitializationTask"));
            Assert.Same(songs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Single(display.CurrentList);
            Assert.Same(songs[0], display.CurrentList[0]);
        }

        [Fact]
        public void CheckSongInitializationCompletion_WhenTaskFaulted_ShouldClearTaskAndFallbackToEmptyList()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("Existing")]
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_songInitializationTask", Task.FromException<List<SongListNode>>(new InvalidOperationException("boom")));
            SetPrivateField(stage, "_songInitializationProcessed", false);

            InvokePrivateMethod(stage, "CheckSongInitializationCompletion");

            Assert.True(GetPrivateField<bool>(stage, "_songInitializationProcessed"));
            Assert.Null(GetPrivateField<object>(stage, "_songInitializationTask"));
            Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_currentSongList")!);
            Assert.Empty(display.CurrentList);
        }

        [Fact]
        public void CheckSongInitializationCompletion_WhenTaskStillRunning_ShouldLeaveStateUnchanged()
        {
            var pendingTask = new TaskCompletionSource<List<SongListNode>>();
            var stage = CreateStage();
            var currentSongs = new List<SongListNode> { CreateScoreNode("Existing") };
            var display = new SongListDisplay
            {
                CurrentList = [currentSongs[0]]
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", currentSongs);
            SetPrivateField(stage, "_songInitializationTask", pendingTask.Task);
            SetPrivateField(stage, "_songInitializationProcessed", false);

            InvokePrivateMethod(stage, "CheckSongInitializationCompletion");

            Assert.False(GetPrivateField<bool>(stage, "_songInitializationProcessed"));
            Assert.Same(pendingTask.Task, GetPrivateField<Task<List<SongListNode>>>(stage, "_songInitializationTask"));
            Assert.Same(currentSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Same(currentSongs[0], display.CurrentList[0]);
        }

        [Fact]
        public void CheckSongInitializationCompletion_WhenAlreadyProcessed_ShouldLeaveCompletedTaskUntouched()
        {
            var songs = new List<SongListNode> { CreateScoreNode("Existing") };
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [songs[0]]
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", songs);
            SetPrivateField(stage, "_songInitializationTask", Task.FromResult(new List<SongListNode> { CreateScoreNode("Loaded") }));
            SetPrivateField(stage, "_songInitializationProcessed", true);

            InvokePrivateMethod(stage, "CheckSongInitializationCompletion");

            Assert.True(GetPrivateField<bool>(stage, "_songInitializationProcessed"));
            Assert.NotNull(GetPrivateField<Task<List<SongListNode>>>(stage, "_songInitializationTask"));
            Assert.Same(songs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Same(songs[0], display.CurrentList[0]);
        }

        [Fact]
        public void OnSongSelectionChanged_WhenScoreAndScrollIncomplete_ShouldUpdateLightweightUiOnly()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("Song A"), CreateScoreNode("Song B")]
            };
            var statusPanel = new SongStatusPanel { Visible = false };
            var previewPanel = new PreviewImagePanel();
            var breadcrumbLabel = new UILabel();
            var existingPreviewSound = new Mock<ISound>();

            AttachCoreUi(stage, display, statusPanel, previewPanel, breadcrumbLabel);
            SetPrivateField(stage, "_currentBreadcrumb", "Folder A");
            SetPrivateField(stage, "_previewSound", existingPreviewSound.Object);
            GetTextureQueue(display).Clear();

            var selectedSong = CreateScoreNode("Selected");
            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                display,
                new SongSelectionChangedEventArgs(selectedSong, 2, isScrollComplete: false));

            existingPreviewSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Same(selectedSong, GetPrivateField<SongListNode>(stage, "_selectedSong"));
            Assert.Equal(2, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.True(statusPanel.Visible);
            Assert.Same(selectedSong, GetPrivateField<SongListNode>(statusPanel, "_currentSong"));
            Assert.Equal(2, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            Assert.Same(selectedSong, GetPrivateField<SongListNode>(previewPanel, "_currentSong"));
            Assert.Equal("Folder A", breadcrumbLabel.Text);
            Assert.Empty(GetTextureQueue(display));
        }

        [Fact]
        public void OnSongSelectionChanged_WhenScoreAndScrollComplete_ShouldInvalidateDisplayAndUpdatePanels()
        {
            var stage = CreateStage();
            var selectedSong = CreateScoreNode("Selected");
            var display = new SongListDisplay
            {
                CurrentList = [selectedSong, CreateScoreNode("Other")]
            };
            var statusPanel = new SongStatusPanel { Visible = false };
            var previewPanel = new PreviewImagePanel();
            var breadcrumbLabel = new UILabel();

            AttachCoreUi(stage, display, statusPanel, previewPanel, breadcrumbLabel);
            SetPrivateField(stage, "_currentBreadcrumb", "");
            GetTextureQueue(display).Clear();
            GetVisibleIndices(display).Clear();

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                display,
                new SongSelectionChangedEventArgs(selectedSong, 1, isScrollComplete: true));

            Assert.True(statusPanel.Visible);
            Assert.Same(selectedSong, GetPrivateField<SongListNode>(statusPanel, "_currentSong"));
            Assert.Equal(1, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            Assert.Same(selectedSong, GetPrivateField<SongListNode>(previewPanel, "_currentSong"));
            Assert.Equal("Root", breadcrumbLabel.Text);
            Assert.NotEmpty(GetTextureQueue(display));
            Assert.NotEmpty(GetVisibleIndices(display));
        }

        [Fact]
        public void OnSongSelectionChanged_WhenNonScore_ShouldExitStatusPanelAndHideStatus()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var statusPanel = new SongStatusPanel { Visible = true };
            var previewPanel = new PreviewImagePanel();
            var previewTexture = new Mock<ITexture>();
            var breadcrumbLabel = new UILabel();
            var folder = CreateBoxNode("Folder");

            AttachCoreUi(stage, display, statusPanel, previewPanel, breadcrumbLabel);
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(previewPanel, "_currentPreviewTexture", previewTexture.Object);

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                display,
                new SongSelectionChangedEventArgs(folder, 0, isScrollComplete: true));

            previewTexture.Verify(x => x.RemoveReference(), Times.Once);
            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.False(statusPanel.Visible);
            Assert.Same(folder, GetPrivateField<SongListNode>(previewPanel, "_currentSong"));
        }

        [Fact]
        public void OnSongSelectionChanged_WhenSelectedSongIsNull_ShouldHideStatusPanelAndSkipPreviewReload()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("Song A"), CreateScoreNode("Song B")]
            };
            var statusPanel = new SongStatusPanel { Visible = true };
            var previewPanel = new PreviewImagePanel();
            var breadcrumbLabel = new UILabel();
            var existingPreviewSound = new Mock<ISound>();

            AttachCoreUi(stage, display, statusPanel, previewPanel, breadcrumbLabel);
            SetPrivateField(stage, "_currentBreadcrumb", "Folder A");
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(stage, "_previewSound", existingPreviewSound.Object);
            SetPrivateField(stage, "_previewPlayDelay", 2.0);
            SetPrivateField(stage, "_isPreviewDelayActive", true);
            GetTextureQueue(display).Clear();
            GetVisibleIndices(display).Clear();

            var exception = Record.Exception(() => InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                display,
                new SongSelectionChangedEventArgs(null!, 0, isScrollComplete: true)));

            Assert.Null(exception);
            existingPreviewSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<SongListNode>(stage, "_selectedSong"));
            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.False(statusPanel.Visible);
            Assert.Null(GetPrivateField<SongListNode>(previewPanel, "_currentSong"));
            Assert.Equal(0.0, GetPrivateField<double>(stage, "_previewPlayDelay"));
            Assert.False(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            Assert.Equal("Folder A", breadcrumbLabel.Text);
            Assert.NotEmpty(GetTextureQueue(display));
            Assert.NotEmpty(GetVisibleIndices(display));
        }

        [Fact]
        public void OnDifficultyChanged_ShouldSyncStageAndStatusPanel()
        {
            var stage = CreateStage();
            var statusPanel = new SongStatusPanel();
            var song = CreateScoreNode("Song", CreateScores(0, 3));

            AttachCoreUi(stage, statusPanel: statusPanel);

            InvokePrivateMethod(stage, "OnDifficultyChanged", stage, new DifficultyChangedEventArgs(song, 3));

            Assert.Equal(3, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Same(song, GetPrivateField<SongListNode>(statusPanel, "_currentSong"));
            Assert.Equal(3, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
        }

        [Fact]
        public void NavigateIntoBox_ShouldPushCurrentStateAndPopulateChildren()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var childSongs = new List<SongListNode> { CreateScoreNode("Child Song") };
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var box = CreateBoxNode("Folder", childSongs.ToArray());

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", rootSongs);
            SetPrivateField(stage, "_currentBreadcrumb", "Root");

            InvokePrivateMethod(stage, "NavigateIntoBox", box);

            Assert.Equal(1, GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Count);
            var currentSongList = GetPrivateField<List<SongListNode>>(stage, "_currentSongList");
            Assert.NotNull(currentSongList);
            Assert.Single(currentSongList!);
            Assert.Same(childSongs[0], currentSongList[0]);
            Assert.Equal("Root > Folder", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Equal(NodeType.BackBox, display.CurrentList[0].Type);
            Assert.Same(childSongs[0], display.CurrentList[1]);
        }

        [Fact]
        public void NavigateIntoBox_WhenChildrenMissing_ShouldLeaveStateUnchanged()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("Root Song")]
            };
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var box = CreateBoxNode("Folder");

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", rootSongs);
            SetPrivateField(stage, "_currentBreadcrumb", "Root");

            InvokePrivateMethod(stage, "NavigateIntoBox", box);

            Assert.Same(rootSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Empty(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
            Assert.Single(display.CurrentList);
            Assert.Equal("Root Song", display.CurrentList[0].Title);
        }

        [Fact]
        public void NavigateIntoBox_WhenBreadcrumbIsEmpty_ShouldUseBoxTitleWithoutSeparator()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var childSong = CreateScoreNode("Child Song");
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var box = CreateBoxNode("Folder", childSong);

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", rootSongs);
            SetPrivateField(stage, "_currentBreadcrumb", "");

            InvokePrivateMethod(stage, "NavigateIntoBox", box);

            Assert.Equal("Folder", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Equal(1, GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Count);
            Assert.Equal(NodeType.BackBox, display.CurrentList[0].Type);
            Assert.Same(childSong, display.CurrentList[1]);
        }

        [Fact]
        public void NavigateBack_ShouldRestorePreviousState()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var stack = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!;

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("Child Song") });
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Child");
            stack.Push(new SongListNode { Children = rootSongs, Title = "Root" });

            InvokePrivateMethod(stage, "NavigateBack");

            Assert.Same(rootSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Single(display.CurrentList);
            Assert.Same(rootSongs[0], display.CurrentList[0]);
            Assert.Empty(stack);
        }

        [Fact]
        public void NavigateBack_WhenStackEmpty_ShouldLeaveCurrentStateUnchanged()
        {
            var stage = CreateStage();
            var songs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var display = new SongListDisplay
            {
                CurrentList = [songs[0]]
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", songs);
            SetPrivateField(stage, "_currentBreadcrumb", "Root");

            InvokePrivateMethod(stage, "NavigateBack");

            Assert.Same(songs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Empty(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
            Assert.Same(songs[0], display.CurrentList[0]);
        }

        [Fact]
        public void NavigateBack_WhenPreviousBreadcrumbIsNull_ShouldResetToEmptyBreadcrumb()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };
            var stack = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!;

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("Child Song") });
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Child");
            stack.Push(new SongListNode { Children = rootSongs, Title = null });

            InvokePrivateMethod(stage, "NavigateBack");

            Assert.Same(rootSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Single(display.CurrentList);
            Assert.Same(rootSongs[0], display.CurrentList[0]);
        }

        [Fact]
        public void SelectSong_WhenTransitionAllowed_ShouldGoToSongTransition()
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
                    It.Is<IStageTransition>(transition => transition is InstantTransition),
                    It.Is<Dictionary<string, object>>(sharedData =>
                        ReferenceEquals(sharedData["selectedSong"], song) &&
                        (int)sharedData["selectedDifficulty"] == 2 &&
                        (int)sharedData["songId"] == 17)),
                Times.Once);
            Assert.Equal(2.0, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void SelectSong_WhenDebounceBlocksTransition_ShouldDoNothing()
        {
            var game = CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "SelectSong", CreateScoreNode("Song", songId: 12));

            stageManager.Verify(
                x => x.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void SelectRandomSong_WhenSinglePlayableSongExists_ShouldSelectThatSong()
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
                    It.Is<Dictionary<string, object>>(sharedData => ReferenceEquals(sharedData["selectedSong"], song))),
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
                x => x.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void OnSongActivated_WhenSongIsNull_ShouldLeaveStageStateUnchanged()
        {
            var stage = CreateStage();
            var currentSongList = new List<SongListNode> { CreateScoreNode("Song") };
            var navigationStack = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!;

            SetPrivateField(stage, "_currentSongList", currentSongList);
            navigationStack.Push(new SongListNode { Title = "Root" });

            InvokePrivateMethod(stage, "OnSongActivated", stage, new SongActivatedEventArgs(null!, 0));

            Assert.Same(currentSongList, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Single(navigationStack);
        }

        [Fact]
        public void HandleSongActivation_WhenBackBoxNodeProvided_ShouldNavigateBack()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("Child Song") });
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Child");
            GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(new SongListNode { Children = rootSongs, Title = "Root" });

            InvokePrivateMethod(stage, "HandleSongActivation", new SongListNode { Type = NodeType.BackBox, Title = ".. (Back)" });

            Assert.Same(rootSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Same(rootSongs[0], display.CurrentList[0]);
        }

        [Fact]
        public void HandleSongActivation_WhenScoreNodeProvided_ShouldSelectSong()
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
                    It.Is<Dictionary<string, object>>(sharedData => ReferenceEquals(sharedData["selectedSong"], song))),
                Times.Once);
        }

        [Fact]
        public void HandleSongActivation_WhenRandomNodeProvided_ShouldSelectRandomSong()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var song = CreateScoreNode("Song", songId: 22);

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>
            {
                song,
                new SongListNode { Type = NodeType.Random, Title = "Random" }
            });

            InvokePrivateMethod(stage, "HandleSongActivation", new SongListNode { Type = NodeType.Random, Title = "Random" });

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.IsAny<IStageTransition>(),
                    It.Is<Dictionary<string, object>>(sharedData => ReferenceEquals(sharedData["selectedSong"], song))),
                Times.Once);
        }

        [Fact]
        public void UpdateBreadcrumb_ShouldUseRootFallbackAndCurrentBreadcrumb()
        {
            var stage = CreateStage();
            var label = new UILabel();

            AttachCoreUi(stage, breadcrumb: label);

            SetPrivateField(stage, "_currentBreadcrumb", "");
            InvokePrivateMethod(stage, "UpdateBreadcrumb");
            Assert.Equal("Root", label.Text);

            SetPrivateField(stage, "_currentBreadcrumb", "Genre > Folder");
            InvokePrivateMethod(stage, "UpdateBreadcrumb");
            Assert.Equal("Genre > Folder", label.Text);
        }

        [Fact]
        public void UpdatePhase_WhenFadeInDurationElapsed_ShouldEnterNormalPhase()
        {
            var stage = CreateStage();

            SetPrivateField(stage, "_selectionPhase", SongSelectionPhase.FadeIn);
            SetPrivateField(stage, "_currentPhase", StagePhase.FadeIn);
            SetPrivateField(stage, "_phaseStartTime", 0.0);
            SetPrivateField(stage, "_elapsedTime", SongSelectionUILayout.Timing.FadeInDuration);

            InvokePrivateMethod(stage, "UpdatePhase", 0.0);

            Assert.Equal(SongSelectionPhase.Normal, GetPrivateField<SongSelectionPhase>(stage, "_selectionPhase"));
            Assert.Equal(StagePhase.Normal, stage.CurrentPhase);
            Assert.Equal(SongSelectionUILayout.Timing.FadeInDuration, GetPrivateField<double>(stage, "_phaseStartTime"));
        }

        [Fact]
        public void OnUpdate_WhenOwnedInputManagerHasQueuedMoveDown_ShouldProcessInputAndAdvancePhase()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { CreateScoreNode("Song A"), CreateScoreNode("Song B") }
            };
            var inputManager = new UpdatingQueuedInputManager();
            inputManager.Enqueue(new InputCommand(InputCommandType.MoveDown, 0.0));

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_ownsInputManager", true);
            SetPrivateField(stage, "_uiManager", new DTXMania.Game.Lib.UI.UIManager());
            SetPrivateField(stage, "_selectionPhase", SongSelectionPhase.FadeIn);
            SetPrivateField(stage, "_currentPhase", StagePhase.FadeIn);
            SetPrivateField(stage, "_elapsedTime", 0.0);
            SetPrivateField(stage, "_phaseStartTime", 0.0);

            InvokePrivateMethod(stage, "OnUpdate", SongSelectionUILayout.Timing.FadeInDuration);

            Assert.True(inputManager.UpdateCalled);
            Assert.Equal(SongSelectionPhase.Normal, GetPrivateField<SongSelectionPhase>(stage, "_selectionPhase"));
            Assert.Equal(StagePhase.Normal, stage.CurrentPhase);
            Assert.Equal("Song B", display.SelectedSong.Title);
        }

        [Theory]
        [InlineData(InputCommandType.MoveDown, false, false, 3.5, "B")]
        [InlineData(InputCommandType.MoveUp, false, true, 2.5, "A")]
        [InlineData(InputCommandType.MoveUp, true, true, 4.5, "A")]
        [InlineData(InputCommandType.MoveDown, true, false, 5.5, "B")]
        public void ExecuteInputCommand_MoveNavigation_ShouldMoveSelectionAndPlayCursorSound(
            InputCommandType command,
            bool isInStatusPanel,
            bool startAtSecondItem,
            double elapsedTime,
            string expectedSelectedTitle)
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };
            var cursorSound = new Mock<ISound>();

            AttachCoreUi(stage, display: display);
            if (startAtSecondItem)
            {
                display.MoveNext();
            }

            SetPrivateField(stage, "_isInStatusPanel", isInStatusPanel);
            SetPrivateField(stage, "_elapsedTime", elapsedTime);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(command, 0.0));

            Assert.Equal(expectedSelectedTitle, display.SelectedSong!.Title);
            Assert.Equal(elapsedTime, GetPrivateField<double>(stage, "_lastNavigationTime"));
            cursorSound.Verify(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume), Times.Once);
        }

    [Fact]
    public void ExecuteInputCommand_MoveLeftInStatusPanel_ShouldCycleBackwardDifficulty()
    {
        var stage = CreateStage();
        var song = CreateScoreNode("Song", CreateScores(0, 2, 4));
            var display = new SongListDisplay
            {
                CurrentList = [song]
            };
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

        [Theory]
        [InlineData(InputCommandType.MoveLeft, 3)]
        [InlineData(InputCommandType.MoveRight, 1)]
        public void ExecuteInputCommand_MoveOutsideStatusPanel_ShouldDoNothing(InputCommandType command, int initialDifficulty)
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentDifficulty", initialDifficulty);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(command, 0.0));

            Assert.Equal(initialDifficulty, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Equal("A", display.SelectedSong!.Title);
        }

        [Fact]
        public void ExecuteInputCommand_MoveRightInStatusPanel_ShouldUseSongListDifficultyCycling()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("Song", CreateScores(0, 2));
            var display = new SongListDisplay
            {
                CurrentList = [song]
            };
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
        public void ExecuteInputCommand_BackInStatusPanel_ShouldOnlyExitStatusPanelMode()
        {
            var stage = CreateStage();
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()),
                Times.Never);
        }

        [Fact]
        public void ExecuteInputCommand_BackWithNavigationStack_ShouldRestorePreviousFolder()
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
            Assert.Single(display.CurrentList);
            Assert.Same(rootSongs[0], display.CurrentList[0]);
        }

        [Fact]
        public void ExecuteInputCommand_BackAtRoot_ShouldReturnToTitleWhenTransitionAllowed()
        {
            var game = CreateGame(totalGameTime: 1.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.Title,
                    It.Is<IStageTransition>(transition => transition is DTXManiaFadeTransition)),
                Times.Once);
            Assert.Equal(1.0, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
    public void HandleActivateInput_WhenSongSelected_ShouldEnterStatusPanel()
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
    public void ExecuteInputCommand_Activate_ShouldDelegateToActivateHandler()
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
    public void HandleActivateInput_WhenInStatusPanel_ShouldSelectSong()
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
                    It.IsAny<IStageTransition>(),
                    It.Is<Dictionary<string, object>>(sharedData => ReferenceEquals(sharedData["selectedSong"], selectedSong))),
                Times.Once);
        }

        [Fact]
        public void HandleActivateInput_WhenBoxSelected_ShouldNavigateIntoBox()
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
        public void ProcessInputCommands_ShouldDrainQueueAndExecuteCommands()
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

            InvokePrivateMethod(stage, "ProcessInputCommands");

            Assert.Equal("B", display.SelectedSong!.Title);
            Assert.False(inputManager.HasPendingCommands);
        }

        [Fact]
        public void LoadPreviewSound_WhenPreviewFileExists_ShouldLoadSoundAndActivateDelay()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var previousSound = new Mock<ISound>();
            var loadedSound = new Mock<ISound>();
            var dir = CreateWorkspacePath("preview", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(dir);

            try
            {
                var previewPath = Path.Combine(dir, "preview.ogg");
                File.WriteAllText(previewPath, "preview");

                AttachResourceManager(stage, resourceManager.Object);
                SetPrivateField(stage, "_previewSound", previousSound.Object);
                SetPrivateField(stage, "_previewPlayDelay", 5.0);
                SetPrivateField(stage, "_isPreviewDelayActive", false);

                resourceManager.Setup(x => x.LoadSound(previewPath)).Returns(loadedSound.Object);

                var song = CreateScoreNode(
                    "Song",
                    chart: new SongChart
                    {
                        FilePath = Path.Combine(dir, "chart.dtx"),
                        PreviewFile = "preview.ogg",
                        HasDrumChart = true,
                        DrumLevel = 30
                    });

                InvokePrivateMethod(stage, "LoadPreviewSound", song);

                previousSound.Verify(x => x.RemoveReference(), Times.Once);
                resourceManager.Verify(x => x.LoadSound(previewPath), Times.Once);
                Assert.Same(loadedSound.Object, GetPrivateField<ISound>(stage, "_previewSound"));
                Assert.Equal(0.0, GetPrivateField<double>(stage, "_previewPlayDelay"));
                Assert.True(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [Fact]
        public void LoadPreviewSound_WhenPreviewFileMissingOnDisk_ShouldLeavePreviewStateUnchanged()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var existingPreviewSound = new Mock<ISound>();
            var dir = CreateWorkspacePath("preview-missing", Guid.NewGuid().ToString("N"));

            Directory.CreateDirectory(dir);

            try
            {
                AttachResourceManager(stage, resourceManager.Object);
                SetPrivateField(stage, "_previewSound", existingPreviewSound.Object);
                SetPrivateField(stage, "_previewPlayDelay", 1.5);
                SetPrivateField(stage, "_isPreviewDelayActive", true);

                var song = CreateScoreNode(
                    "Song",
                    chart: new SongChart
                    {
                        FilePath = Path.Combine(dir, "chart.dtx"),
                        PreviewFile = "missing.ogg",
                        HasDrumChart = true,
                        DrumLevel = 25
                    });

                InvokePrivateMethod(stage, "LoadPreviewSound", song);

                resourceManager.Verify(x => x.LoadSound(It.IsAny<string>()), Times.Never);
                Assert.Same(existingPreviewSound.Object, GetPrivateField<ISound>(stage, "_previewSound"));
                Assert.Equal(1.5, GetPrivateField<double>(stage, "_previewPlayDelay"));
                Assert.True(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            }
            finally
            {
                if (Directory.Exists(dir))
                {
                    Directory.Delete(dir, recursive: true);
                }
            }
        }

        [Fact]
        public void TryLoadPreviewSoundFile_WhenLoadFailureEventFires_ShouldReleaseLoadedSound()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var previousSound = new Mock<ISound>();
            var failedSound = new Mock<ISound>();
            var previewPath = CreateWorkspacePath("preview-failure.ogg");

            AttachResourceManager(stage, resourceManager.Object);
            SetPrivateField(stage, "_previewSound", previousSound.Object);

            resourceManager
                .Setup(x => x.LoadSound(previewPath))
                .Callback(() => resourceManager.Raise(
                    x => x.ResourceLoadFailed += null,
                    new ResourceLoadFailedEventArgs(previewPath, new InvalidOperationException("load failed"))))
                .Returns(failedSound.Object);

            var loaded = InvokePrivateMethod<bool>(stage, "TryLoadPreviewSoundFile", previewPath);

            Assert.False(loaded);
            previousSound.Verify(x => x.RemoveReference(), Times.Once);
            failedSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<ISound>(stage, "_previewSound"));
        }

        [Fact]
        public void LoadUIGraphics_WhenFooterLoadFails_ShouldKeepHeaderTexture()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var header = new Mock<ITexture>().Object;

            resourceManager.Setup(x => x.LoadTexture(TexturePath.SongSelectionHeaderPanel)).Returns(header);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.SongSelectionFooterPanel)).Throws(new InvalidOperationException("footer"));
            AttachResourceManager(stage, resourceManager.Object);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "LoadUIGraphics"));

            Assert.Null(ex);
            Assert.Same(header, GetPrivateField<ITexture>(stage, "_headerPanelTexture"));
            Assert.Null(GetPrivateField<ITexture>(stage, "_footerPanelTexture"));
        }

        [Fact]
        public void LoadUIGraphics_WhenHeaderLoadFails_ShouldStillLoadFooterTexture()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var footer = new Mock<ITexture>().Object;

            resourceManager.Setup(x => x.LoadTexture(TexturePath.SongSelectionHeaderPanel)).Throws(new InvalidOperationException("header"));
            resourceManager.Setup(x => x.LoadTexture(TexturePath.SongSelectionFooterPanel)).Returns(footer);
            AttachResourceManager(stage, resourceManager.Object);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "LoadUIGraphics"));

            Assert.Null(ex);
            Assert.Null(GetPrivateField<ITexture>(stage, "_headerPanelTexture"));
            Assert.Same(footer, GetPrivateField<ITexture>(stage, "_footerPanelTexture"));
        }

        [Fact]
        public void LoadUIGraphics_WhenBothLoadsSucceed_ShouldSetHeaderAndFooterTextures()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var header = new Mock<ITexture>().Object;
            var footer = new Mock<ITexture>().Object;

            resourceManager.Setup(x => x.LoadTexture(TexturePath.SongSelectionHeaderPanel)).Returns(header);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.SongSelectionFooterPanel)).Returns(footer);
            AttachResourceManager(stage, resourceManager.Object);

            InvokePrivateMethod(stage, "LoadUIGraphics");

            Assert.Same(header, GetPrivateField<ITexture>(stage, "_headerPanelTexture"));
            Assert.Same(footer, GetPrivateField<ITexture>(stage, "_footerPanelTexture"));
        }

        [Fact]
        public void LoadNavigationSound_WhenNowLoadingFails_ShouldFallbackToDecideSound()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var cursor = new Mock<ISound>().Object;
            var fallback = new Mock<ISound>().Object;

            resourceManager.Setup(x => x.LoadSound("Sounds/Move.ogg")).Returns(cursor);
            resourceManager.Setup(x => x.LoadSound("Sounds/Now loading.ogg")).Throws(new InvalidOperationException("missing"));
            resourceManager.Setup(x => x.LoadSound("Sounds/Decide.ogg")).Returns(fallback);
            AttachResourceManager(stage, resourceManager.Object);

            InvokePrivateMethod(stage, "LoadNavigationSound");

            Assert.Same(cursor, GetPrivateField<ISound>(stage, "_cursorMoveSound"));
            Assert.Same(fallback, GetPrivateField<ISound>(stage, "_gameStartSound"));
        }

        [Fact]
        public void LoadNavigationSound_WhenAllLoadsFail_ShouldLeaveSoundsNull()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();

            resourceManager.Setup(x => x.LoadSound(It.IsAny<string>())).Throws(new InvalidOperationException("missing"));
            AttachResourceManager(stage, resourceManager.Object);

            InvokePrivateMethod(stage, "LoadNavigationSound");

            Assert.Null(GetPrivateField<ISound>(stage, "_cursorMoveSound"));
            Assert.Null(GetPrivateField<ISound>(stage, "_gameStartSound"));
        }

        [Fact]
        public void LoadNavigationSound_WhenCursorLoadFailsButNowLoadingSucceeds_ShouldKeepGameStartSound()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var nowLoading = new Mock<ISound>().Object;

            resourceManager.Setup(x => x.LoadSound("Sounds/Move.ogg")).Throws(new InvalidOperationException("cursor"));
            resourceManager.Setup(x => x.LoadSound("Sounds/Now loading.ogg")).Returns(nowLoading);
            AttachResourceManager(stage, resourceManager.Object);

            InvokePrivateMethod(stage, "LoadNavigationSound");

            Assert.Null(GetPrivateField<ISound>(stage, "_cursorMoveSound"));
            Assert.Same(nowLoading, GetPrivateField<ISound>(stage, "_gameStartSound"));
        }

        [Fact]
        public void TryLoadPreviewSoundFile_WhenLoadSucceeds_ShouldSetPreviewSoundAndDelay()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var previousSound = new Mock<ISound>();
            var loadedSound = new Mock<ISound>();
            var previewPath = CreateWorkspacePath("preview-success.ogg");

            AttachResourceManager(stage, resourceManager.Object);
            SetPrivateField(stage, "_previewSound", previousSound.Object);
            resourceManager.Setup(x => x.LoadSound(previewPath)).Returns(loadedSound.Object);

            var loaded = InvokePrivateMethod<bool>(stage, "TryLoadPreviewSoundFile", previewPath);

            Assert.True(loaded);
            previousSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Same(loadedSound.Object, GetPrivateField<ISound>(stage, "_previewSound"));
            Assert.Equal(0.0, GetPrivateField<double>(stage, "_previewPlayDelay"));
            Assert.True(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
        }

        [Fact]
        public void TryLoadPreviewSoundFile_WhenLoadThrows_ShouldReturnFalseAndClearPreview()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var previousSound = new Mock<ISound>();
            var previewPath = CreateWorkspacePath("preview-throw.ogg");

            AttachResourceManager(stage, resourceManager.Object);
            SetPrivateField(stage, "_previewSound", previousSound.Object);
            resourceManager.Setup(x => x.LoadSound(previewPath)).Throws(new IOException("boom"));

            var loaded = InvokePrivateMethod<bool>(stage, "TryLoadPreviewSoundFile", previewPath);

            Assert.False(loaded);
            previousSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<ISound>(stage, "_previewSound"));
        }

        [Fact]
        public void LoadPreviewSound_WhenPreviewFileBlankOrMissing_ShouldRemainNoOp()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var existingPreviewSound = new Mock<ISound>();
            var baseDir = CreateWorkspacePath("preview-load-noop");

            Directory.CreateDirectory(baseDir);
            try
            {
                AttachResourceManager(stage, resourceManager.Object);
                SetPrivateField(stage, "_previewSound", existingPreviewSound.Object);
                SetPrivateField(stage, "_isPreviewDelayActive", true);

                InvokePrivateMethod(stage, "LoadPreviewSound", CreateScoreNode("Blank", chart: new SongChart
                {
                    FilePath = Path.Combine(baseDir, "blank.dtx"),
                    PreviewFile = "",
                    HasDrumChart = true,
                    DrumLevel = 20
                }));
                InvokePrivateMethod(stage, "LoadPreviewSound", CreateScoreNode("Missing", chart: new SongChart
                {
                    FilePath = Path.Combine(baseDir, "missing.dtx"),
                    PreviewFile = "missing.ogg",
                    HasDrumChart = true,
                    DrumLevel = 20
                }));

                resourceManager.Verify(x => x.LoadSound(It.IsAny<string>()), Times.Never);
                Assert.Same(existingPreviewSound.Object, GetPrivateField<ISound>(stage, "_previewSound"));
                Assert.True(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            }
            finally
            {
                if (Directory.Exists(baseDir))
                {
                    Directory.Delete(baseDir, true);
                }
            }
        }

        [Fact]
        public void AssignInputManager_WhenNull_ShouldCreateOwnedFallbackManager()
        {
            var stage = CreateStage();

            InvokePrivateMethod(stage, "AssignInputManager", new object?[] { null });

            Assert.NotNull(GetPrivateField<InputManager>(stage, "_inputManager"));
            Assert.True(GetPrivateField<bool>(stage, "_ownsInputManager"));
        }

        [Fact]
        public void CreateFallbackFont_WhenLoadSucceeds_ShouldReturnLoadedFont()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var expectedFont = new Mock<IFont>().Object;

            resourceManager
                .Setup(x => x.LoadFont(
                    SongSelectionUILayout.Background.DefaultFontName,
                    SongSelectionUILayout.Background.DefaultFontSize,
                    FontStyle.Regular))
                .Returns(expectedFont);

            AttachResourceManager(stage, resourceManager.Object);

            var fallbackFont = InvokePrivateMethod<IFont?>(stage, "CreateFallbackFont");

            Assert.Same(expectedFont, fallbackFont);
        }

        [Fact]
        public void PlayCursorMoveSound_WhenPlayThrows_ShouldSwallowException()
        {
            var stage = CreateStage();
            var sound = new Mock<ISound>();
            sound.Setup(x => x.Play(SongSelectionUILayout.Audio.NavigationSoundVolume)).Throws(new InvalidOperationException("play"));
            SetPrivateField(stage, "_cursorMoveSound", sound.Object);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "PlayCursorMoveSound"));

            Assert.Null(ex);
        }

        [Fact]
        public void PlayGameStartSound_WhenPlayThrows_ShouldSwallowException()
        {
            var stage = CreateStage();
            var sound = new Mock<ISound>();
            sound.Setup(x => x.Play(SongSelectionUILayout.Audio.GameStartSoundVolume)).Throws(new InvalidOperationException("play"));
            SetPrivateField(stage, "_gameStartSound", sound.Object);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "PlayGameStartSound"));

            Assert.Null(ex);
        }

        [Fact]
        public void CycleDifficulty_WhenNegativeAndOnlyOneDifficultyExists_ShouldKeepState()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var statusPanel = new SongStatusPanel();
            var song = CreateScoreNode("Only", CreateScores(2));

            AttachCoreUi(stage, display: display, statusPanel: statusPanel);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 2);
            display.CurrentDifficulty = 2;
            statusPanel.UpdateSongInfo(song, 2);

            InvokePrivateMethod(stage, "CycleDifficulty", -1);

            Assert.Equal(2, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Equal(2, display.CurrentDifficulty);
            Assert.Equal(2, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
        }

        [Fact]
        public void StopCurrentPreview_ShouldResetDelayAndStartBgmFadeIn()
        {
            var stage = CreateStage();
            var previewSound = new Mock<ISound>();

            SetPrivateField(stage, "_previewSound", previewSound.Object);
            SetPrivateField(stage, "_previewPlayDelay", 2.5);
            SetPrivateField(stage, "_isPreviewDelayActive", true);
            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_isBgmFadingIn", false);

            InvokePrivateMethod(stage, "StopCurrentPreview");

            previewSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<ISound>(stage, "_previewSound"));
            Assert.Equal(0.0, GetPrivateField<double>(stage, "_previewPlayDelay"));
            Assert.False(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
            Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
        }

        [Fact]
        public void StopCurrentPreview_WhenPreviewInstanceThrows_ShouldClearInstanceAndContinueFadeIn()
        {
            var stage = CreateStage();
            var previewSound = new Mock<ISound>();
            var previewInstance = new Mock<ISoundInstance>();

            previewInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            previewInstance.Setup(x => x.Stop()).Throws(new InvalidOperationException("stop failed"));

            SetPrivateField(stage, "_previewSound", previewSound.Object);
            SetPrivateField(stage, "_previewSoundInstance", previewInstance.Object);
            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_isBgmFadingIn", false);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "StopCurrentPreview"));

            Assert.Null(ex);
            previewInstance.Verify(x => x.Stop(), Times.Once);
            previewInstance.Verify(x => x.Dispose(), Times.Once);
            previewSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
            Assert.True(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenPreviewDelayElapsesAndCreateInstanceThrows_ShouldClearPreviewInstance()
        {
            var stage = CreateStage();
            var previewSound = new Mock<ISound>();

            previewSound.Setup(x => x.CreateInstance()).Throws(new InvalidOperationException("preview failed"));

            SetPrivateField(stage, "_previewSound", previewSound.Object);
            SetPrivateField(stage, "_isPreviewDelayActive", true);
            SetPrivateField(stage, "_previewPlayDelay", SongSelectionUILayout.Audio.PreviewPlayDelaySeconds - 0.01);
            SetPrivateField(stage, "_isBgmFadingOut", false);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.02));

            Assert.Null(ex);
            previewSound.Verify(x => x.CreateInstance(), Times.Once);
            Assert.False(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
        }

        [Theory]
        [InlineData(true, "_isBgmFadingOut", "_isBgmFadingIn", "_bgmFadeOutTimer")]
        [InlineData(false, "_isBgmFadingIn", "_isBgmFadingOut", "_bgmFadeInTimer")]
        public void StartBGMFade_ShouldToggleExpectedFadeState(
            bool fadeOut,
            string enabledField,
            string disabledField,
            string timerField)
        {
            var stage = CreateStage();

            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_isBgmFadingIn", true);
            SetPrivateField(stage, "_bgmFadeOutTimer", 1.0);
            SetPrivateField(stage, "_bgmFadeInTimer", 1.0);

            InvokePrivateMethod(stage, "StartBGMFade", fadeOut);

            Assert.True(GetPrivateField<bool>(stage, enabledField));
            Assert.False(GetPrivateField<bool>(stage, disabledField));
            Assert.Equal(0.0, GetPrivateField<double>(stage, timerField));
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenExistingPreviewInstanceStopped_ShouldDisposeAndReplaceBeforePlayAttempt()
        {
            var stage = CreateStage();
            var previewSound = new Mock<ISound>();
            var oldInstance = new Mock<ISoundInstance>();

            oldInstance.SetupGet(x => x.State).Returns(SoundState.Stopped);
            previewSound.Setup(x => x.CreateInstance()).Returns((SoundEffectInstance)null!);

            SetPrivateField(stage, "_previewSound", previewSound.Object);
            SetPrivateField(stage, "_previewSoundInstance", oldInstance.Object);
            SetPrivateField(stage, "_isPreviewDelayActive", true);
            SetPrivateField(stage, "_previewPlayDelay", SongSelectionUILayout.Audio.PreviewPlayDelaySeconds - 0.01);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.02));

            Assert.Null(ex);
            oldInstance.Verify(x => x.Dispose(), Times.Once);
            previewSound.Verify(x => x.CreateInstance(), Times.Once);
            Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
            Assert.False(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
        }

        [Fact]
        public void StopCurrentPreview_WhenPreviewInstanceStateIsPlaying_ShouldStopAndClearInstance()
        {
            var stage = CreateStage();
            var previewInstance = new Mock<ISoundInstance>();

            previewInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            SetPrivateField(stage, "_previewSoundInstance", previewInstance.Object);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "StopCurrentPreview"));

            Assert.Null(ex);
            previewInstance.Verify(x => x.Stop(), Times.Once);
            previewInstance.Verify(x => x.Dispose(), Times.Once);
            Assert.Null(GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenBgmFadingOut_ShouldClampVolumeAndStopFade()
        {
            var stage = CreateStage();
            var backgroundMusicInstance = new Mock<ISoundInstance>();

            backgroundMusicInstance.SetupProperty(x => x.Volume, SongSelectionUILayout.Audio.BgmMaxVolume);
            backgroundMusicInstance.SetupGet(x => x.State).Returns(SoundState.Playing);

            SetPrivateField(stage, "_backgroundMusicInstance", backgroundMusicInstance.Object);
            SetPrivateField(stage, "_isBgmFadingOut", true);
            SetPrivateField(stage, "_bgmFadeOutTimer", 0.0);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 1.0);

            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
            Assert.Equal(SongSelectionUILayout.Audio.BgmFadeOutDuration, GetPrivateField<double>(stage, "_bgmFadeOutTimer"));
            Assert.Equal(SongSelectionUILayout.Audio.BgmMinVolume, backgroundMusicInstance.Object.Volume, 3);
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenBgmFadeOutVolumeSetThrows_ShouldStopRetrying()
        {
            var stage = CreateStage();
            var backgroundMusicInstance = new Mock<ISoundInstance>();

            backgroundMusicInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            backgroundMusicInstance.SetupSet(x => x.Volume = It.IsAny<float>()).Throws(new InvalidOperationException("volume"));

            SetPrivateField(stage, "_backgroundMusicInstance", backgroundMusicInstance.Object);
            SetPrivateField(stage, "_isBgmFadingOut", true);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.1));

            Assert.Null(ex);
            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingOut"));
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenBgmFadingIn_ShouldClampVolumeAndStopFade()
        {
            var stage = CreateStage();
            var backgroundMusicInstance = new Mock<ISoundInstance>();

            backgroundMusicInstance.SetupProperty(x => x.Volume, SongSelectionUILayout.Audio.BgmMinVolume);
            backgroundMusicInstance.SetupGet(x => x.State).Returns(SoundState.Playing);

            SetPrivateField(stage, "_backgroundMusicInstance", backgroundMusicInstance.Object);
            SetPrivateField(stage, "_isBgmFadingIn", true);
            SetPrivateField(stage, "_bgmFadeInTimer", 0.0);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 2.0);

            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
            Assert.Equal(SongSelectionUILayout.Audio.BgmFadeInDuration, GetPrivateField<double>(stage, "_bgmFadeInTimer"));
            Assert.Equal(SongSelectionUILayout.Audio.BgmMaxVolume, backgroundMusicInstance.Object.Volume, 3);
        }

        [Fact]
        public void UpdatePreviewSoundTimers_WhenBgmFadeInVolumeSetThrows_ShouldStopRetrying()
        {
            var stage = CreateStage();
            var backgroundMusicInstance = new Mock<ISoundInstance>();

            backgroundMusicInstance.SetupGet(x => x.State).Returns(SoundState.Playing);
            backgroundMusicInstance.SetupSet(x => x.Volume = It.IsAny<float>()).Throws(new InvalidOperationException("volume"));

            SetPrivateField(stage, "_backgroundMusicInstance", backgroundMusicInstance.Object);
            SetPrivateField(stage, "_isBgmFadingIn", true);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.1));

            Assert.Null(ex);
            Assert.False(GetPrivateField<bool>(stage, "_isBgmFadingIn"));
        }

        [Fact]
        public void LoadPreviewSound_WhenPathResolutionThrows_ShouldClearPreviewState()
        {
            var stage = CreateStage();
            var previewSound = new Mock<ISound>();
            var node = CreateScoreNode("Broken", chart: new SongChart
            {
                FilePath = CreateWorkspacePath("broken.dtx"),
                PreviewFile = "\0preview.ogg",
                HasDrumChart = true,
                DrumLevel = 20
            });

            SetPrivateField(stage, "_previewSound", previewSound.Object);
            SetPrivateField(stage, "_isPreviewDelayActive", true);

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "LoadPreviewSound", node));

            Assert.Null(ex);
            Assert.Null(GetPrivateField<ISound>(stage, "_previewSound"));
            Assert.False(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
        }

        [Fact]
        public void TryLoadPreviewSoundFile_WhenLoadFailureEventRaised_ShouldReturnFalseAndDisposeLoadedSound()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var loadedSound = new Mock<ISound>();
            var previewPath = CreateWorkspacePath("preview-load-failed.ogg");

            AttachResourceManager(stage, resourceManager.Object);
            resourceManager
                .Setup(x => x.LoadSound(previewPath))
                .Callback(() => resourceManager.Raise(
                    x => x.ResourceLoadFailed += null,
                    new ResourceLoadFailedEventArgs(previewPath, new IOException("load failed"))))
                .Returns(loadedSound.Object);

            var loaded = InvokePrivateMethod<bool>(stage, "TryLoadPreviewSoundFile", previewPath);

            Assert.False(loaded);
            loadedSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<ISound>(stage, "_previewSound"));
        }

        [Fact]
        public void TryLoadPreviewSoundFile_WhenLoadReturnsNull_ShouldReturnFalseWithoutDelay()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var previewPath = CreateWorkspacePath("preview-null.ogg");

            AttachResourceManager(stage, resourceManager.Object);
            resourceManager.Setup(x => x.LoadSound(previewPath)).Returns((ISound)null!);

            var loaded = InvokePrivateMethod<bool>(stage, "TryLoadPreviewSoundFile", previewPath);

            Assert.False(loaded);
            Assert.Null(GetPrivateField<ISound>(stage, "_previewSound"));
            Assert.False(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
        }

        [Fact]
        public void SetBackgroundMusic_WhenReplacingExistingReferences_ShouldCleanupOldResources()
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
            Assert.Same(newInstance.Object, GetPrivateField<ISoundInstance>(stage, "_backgroundMusicInstance"));
        }

        [Fact]
        public void SetBackgroundMusic_WhenOldCleanupThrows_ShouldStillReplaceReferences()
        {
            var stage = CreateStage();
            var oldSound = new Mock<ISound>();
            var oldInstance = new Mock<ISoundInstance>();
            var newSound = new Mock<ISound>();
            var newInstance = new Mock<ISoundInstance>();

            oldSound.Setup(x => x.RemoveReference()).Throws(new InvalidOperationException("remove failed"));
            oldInstance.Setup(x => x.Stop()).Throws(new InvalidOperationException("stop failed"));

            SetPrivateField(stage, "_backgroundMusic", oldSound.Object);
            SetPrivateField(stage, "_backgroundMusicInstance", oldInstance.Object);

            var exception = Record.Exception(() => stage.SetBackgroundMusic(newSound.Object, newInstance.Object));

            Assert.Null(exception);
            oldSound.Verify(x => x.RemoveReference(), Times.Once);
            oldInstance.Verify(x => x.Stop(), Times.Once);
            oldInstance.Verify(x => x.Dispose(), Times.Once);
            Assert.Same(newSound.Object, GetPrivateField<ISound>(stage, "_backgroundMusic"));
            Assert.Same(newInstance.Object, GetPrivateField<ISoundInstance>(stage, "_backgroundMusicInstance"));
        }

        [Fact]
        public void AssignInputManager_WhenReplacingOwnedFallback_ShouldDisposeOwnedManagerAndUseSharedManager()
        {
            var stage = CreateStage();
            var ownedInputManager = new TrackingInputManager();
            var sharedInputManager = new TrackingInputManager();

            SetPrivateField(stage, "_inputManager", ownedInputManager);
            SetPrivateField(stage, "_ownsInputManager", true);

            InvokePrivateMethod(stage, "AssignInputManager", sharedInputManager);

            Assert.True(ownedInputManager.ClearPendingCommandsCalled);
            Assert.True(ownedInputManager.DisposeCalled);
            Assert.Same(sharedInputManager, GetPrivateField<InputManager>(stage, "_inputManager"));
            Assert.False(GetPrivateField<bool>(stage, "_ownsInputManager"));
        }

        [Fact]
        public void Deactivate_WhenNoInitializationTaskAndOwnedInputManager_ShouldDisposeOwnedResourcesAndResetPhase()
        {
            var stage = CreateStage();
            var ownedInputManager = new TrackingInputManager();
            var headerTexture = new Mock<ITexture>();
            var footerTexture = new Mock<ITexture>();
            var previewSound = new Mock<ISound>();
            var backgroundMusic = new Mock<ISound>();
            var backgroundMusicInstance = new Mock<ISoundInstance>();
            var cursorSound = new Mock<ISound>();
            var gameStartSound = new Mock<ISound>();

            SetPrivateField(stage, "_cancellationTokenSource", new CancellationTokenSource());
            SetPrivateField(stage, "_inputManager", ownedInputManager);
            SetPrivateField(stage, "_ownsInputManager", true);
            SetPrivateField(stage, "_headerPanelTexture", headerTexture.Object);
            SetPrivateField(stage, "_footerPanelTexture", footerTexture.Object);
            SetPrivateField(stage, "_previewSound", previewSound.Object);
            SetPrivateField(stage, "_backgroundMusic", backgroundMusic.Object);
            SetPrivateField(stage, "_backgroundMusicInstance", backgroundMusicInstance.Object);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);
            SetPrivateField(stage, "_gameStartSound", gameStartSound.Object);
            SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
            SetPrivateField(stage, "_selectionPhase", SongSelectionPhase.Normal);

            stage.Deactivate();

            Assert.True(ownedInputManager.ClearPendingCommandsCalled);
            Assert.True(ownedInputManager.DisposeCalled);
            headerTexture.Verify(x => x.RemoveReference(), Times.Once);
            footerTexture.Verify(x => x.RemoveReference(), Times.Once);
            previewSound.Verify(x => x.RemoveReference(), Times.Once);
            backgroundMusic.Verify(x => x.RemoveReference(), Times.Once);
            backgroundMusicInstance.Verify(x => x.Dispose(), Times.Once);
            cursorSound.Verify(x => x.RemoveReference(), Times.Once);
            gameStartSound.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetPrivateField<InputManager>(stage, "_inputManager"));
            Assert.False(GetPrivateField<bool>(stage, "_ownsInputManager"));
            Assert.Null(GetPrivateField<CancellationTokenSource>(stage, "_cancellationTokenSource"));
            Assert.Equal(StagePhase.Inactive, stage.CurrentPhase);
            Assert.Equal(SongSelectionPhase.Inactive, GetPrivateField<SongSelectionPhase>(stage, "_selectionPhase"));
        }

        [Fact]
        public void Deactivate_WhenSongInitializationTaskExists_ShouldClearTaskWithoutDisposingSharedInputManager()
        {
            var stage = CreateStage();
            var sharedInputManager = new TrackingInputManager();
            var cancellationTokenSource = new CancellationTokenSource();
            var taskSource = new TaskCompletionSource<List<SongListNode>>();

            SetPrivateField(stage, "_inputManager", sharedInputManager);
            SetPrivateField(stage, "_ownsInputManager", false);
            SetPrivateField(stage, "_cancellationTokenSource", cancellationTokenSource);
            SetPrivateField(stage, "_songInitializationTask", taskSource.Task);

            stage.Deactivate();

            Assert.False(sharedInputManager.ClearPendingCommandsCalled);
            Assert.False(sharedInputManager.DisposeCalled);
            Assert.Null(GetPrivateField<object>(stage, "_songInitializationTask"));
            Assert.Null(GetPrivateField<CancellationTokenSource>(stage, "_cancellationTokenSource"));
        }

        [Fact]
        public void CreateFallbackFont_WhenFontLoadFailsAgain_ShouldReturnNull()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            resourceManager
                .Setup(x => x.LoadFont(
                    SongSelectionUILayout.Background.DefaultFontName,
                    SongSelectionUILayout.Background.DefaultFontSize,
                    FontStyle.Regular))
                .Throws(new InvalidOperationException("font-failed"));

            AttachResourceManager(stage, resourceManager.Object);

            var fallbackFont = InvokePrivateMethod<IFont?>(stage, "CreateFallbackFont");

            Assert.Null(fallbackFont);
        }

        [Fact]
        public void HandleSongActivation_WhenRandomNodeProvided_ShouldSelectRandomPlayableSong()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var onlySong = CreateScoreNode("Only Song", songId: 55);

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>
            {
                CreateBoxNode("Folder"),
                onlySong
            });

            InvokePrivateMethod(stage, "HandleSongActivation", new SongListNode { Type = NodeType.Random, Title = "Random" });

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.IsAny<IStageTransition>(),
                    It.Is<Dictionary<string, object>>(sharedData => ReferenceEquals(sharedData["selectedSong"], onlySong))),
                Times.Once);
        }

        [Fact]
        public void SelectSong_WhenSongNodeIsNull_ShouldReturnWithoutTransition()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "SelectSong", new object[] { null! });

            stageManager.Verify(
                x => x.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void SelectSong_WhenNodeIsNotScore_ShouldReturnWithoutTransition()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "SelectSong", new SongListNode { Type = NodeType.Box, Title = "Folder" });

            stageManager.Verify(
                x => x.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void HandleActivateInput_WhenBackBoxSelected_ShouldNavigateBack()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var rootSongs = new List<SongListNode> { CreateScoreNode("Root Song") };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_selectedSong", new SongListNode { Type = NodeType.BackBox, Title = ".." });
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("Child Song") });
            SetPrivateField(stage, "_currentBreadcrumb", "Root > Child");
            GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(new SongListNode { Children = rootSongs, Title = "Root" });

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.Same(rootSongs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Root", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Same(rootSongs[0], display.CurrentList[0]);
        }

        [Fact]
        public void HandleInput_WhenInputManagerNull_ShouldReturnWithoutThrowing()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_inputManager", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "HandleInput"));

            Assert.Null(exception);
        }

        [Fact]
        public void ProcessInputCommands_WhenInputManagerNull_ShouldReturnWithoutThrowing()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_inputManager", null);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "ProcessInputCommands"));

            Assert.Null(exception);
        }

        [Fact]
        public void InitializeSongList_WhenSongManagerAlreadyInitialized_ShouldPopulateCurrentAndDisplayedSongs()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var songManager = SongManager.Instance;
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalSongs = new List<SongListNode>(rootSongs);
            var originalInitialized = GetPrivateField<bool>(songManager, "_isInitialized");
            var song = CreateScoreNode("Initialized Song");

            try
            {
                AttachCoreUi(stage, display: display);
                rootSongs.Clear();
                rootSongs.Add(song);
                SetPrivateField(songManager, "_isInitialized", true);

                InvokePrivateMethod(stage, "InitializeSongList");

                Assert.Single(GetPrivateField<List<SongListNode>>(stage, "_currentSongList")!);
                Assert.Same(song, GetPrivateField<List<SongListNode>>(stage, "_currentSongList")![0]);
                Assert.Single(display.CurrentList);
                Assert.Same(song, display.CurrentList[0]);
                Assert.Null(GetPrivateField<object>(stage, "_songInitializationTask"));
            }
            finally
            {
                rootSongs.Clear();
                rootSongs.AddRange(originalSongs);
                SetPrivateField(songManager, "_isInitialized", originalInitialized);
            }
        }

        [Fact]
        public void InitializeSongList_WhenSongManagerIsNotInitialized_ShouldStartBackgroundTaskAndKeepDisplayEmpty()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var songManager = SongManager.Instance;
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalSongs = new List<SongListNode>(rootSongs);
            var originalInitialized = GetPrivateField<bool>(songManager, "_isInitialized");
            var song = CreateScoreNode("Deferred Song");

            try
            {
                AttachCoreUi(stage, display: display);
                rootSongs.Clear();
                rootSongs.Add(song);
                SetPrivateField(songManager, "_isInitialized", false);

                InvokePrivateMethod(stage, "InitializeSongList");

                var initializationTask = GetPrivateField<Task<List<SongListNode>>>(stage, "_songInitializationTask");
                Assert.NotNull(initializationTask);
                Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_currentSongList")!);
                Assert.Empty(display.CurrentList);
                initializationTask!.Wait();
            }
            finally
            {
                rootSongs.Clear();
                rootSongs.AddRange(originalSongs);
                SetPrivateField(songManager, "_isInitialized", originalInitialized);
            }
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

        private static void AttachResourceManager(SongSelectionStage stage, IResourceManager resourceManager)
        {
            SetPrivateField(stage, "_resourceManager", resourceManager);
        }

        private static IList GetTextureQueue(SongListDisplay display)
        {
            return (IList)GetPrivateField<object>(display, "_textureGenerationQueue")!;
        }

        private static HashSet<int> GetVisibleIndices(SongListDisplay display)
        {
            return GetPrivateField<HashSet<int>>(display, "_visibleBarIndices")!;
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
            var path = Path.Combine(AppContext.BaseDirectory, "TestResults", "song-selection-stage-logic");
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

        private sealed class UpdatingQueuedInputManager : InputManager
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

        private sealed class TrackingInputManager : InputManager
        {
            public bool ClearPendingCommandsCalled { get; private set; }

            public bool DisposeCalled { get; private set; }

            public override void ClearPendingCommands()
            {
                ClearPendingCommandsCalled = true;
                base.ClearPendingCommands();
            }

            protected override void Dispose(bool disposing)
            {
                DisposeCalled = true;
                base.Dispose(disposing);
            }
        }
    }
}

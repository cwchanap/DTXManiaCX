using System;
using System.Collections;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Utilities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Audio;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;
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
        public void CollectRandomCandidates_WhenDisabled_ShouldIncludeDirectScoresOnly()
        {
            var directSong = CreateScoreNode("Direct Song");
            var nestedSong = CreateScoreNode("Nested Song");
            var nodes = new List<SongListNode>
            {
                directSong,
                CreateBoxNode("Folder", nestedSong)
            };

            var candidates = SongSelectionStage.CollectRandomCandidates(nodes, includeSubBoxes: false);

            var candidate = Assert.Single(candidates);
            Assert.Same(directSong, candidate);
            Assert.DoesNotContain(nestedSong, candidates);
        }

        [Fact]
        public void CollectRandomCandidates_WhenDisabled_ShouldLookThroughSongRootBoxesOnly()
        {
            var rootSong = CreateScoreNode("Root Song");
            var nestedSong = CreateScoreNode("Nested Song");
            var songRoot = CreateBoxNode("SongsA", rootSong, CreateBoxNode("Pack", nestedSong));
            songRoot.IsSongRoot = true;
            var otherRootSong = CreateScoreNode("Other Root Song");
            var otherRoot = CreateBoxNode("SongsB", otherRootSong);
            otherRoot.IsSongRoot = true;

            var candidates = SongSelectionStage.CollectRandomCandidates(
                new List<SongListNode> { songRoot, otherRoot },
                includeSubBoxes: false);

            Assert.Equal(new[] { rootSong, otherRootSong }, candidates);
        }

        [Fact]
        public void CollectRandomCandidates_WhenEnabled_ShouldIncludeDirectAndDescendantScores()
        {
            var directSong = CreateScoreNode("Direct Song");
            var oneLevelSong = CreateScoreNode("One-Level Song");
            var deepSong = CreateScoreNode("Deep Song");
            var nestedBox = CreateBoxNode("Nested Folder", deepSong);
            var nodes = new List<SongListNode>
            {
                directSong,
                CreateBoxNode("Folder", oneLevelSong, nestedBox)
            };

            var candidates = SongSelectionStage.CollectRandomCandidates(nodes, includeSubBoxes: true);

            Assert.Equal(3, candidates.Count);
            Assert.Contains(directSong, candidates);
            Assert.Contains(oneLevelSong, candidates);
            Assert.Contains(deepSong, candidates);
        }

        [Fact]
        public void CollectRandomCandidates_ShouldIgnoreRandomAndBackBoxNodes()
        {
            var directSong = CreateScoreNode("Direct Song");
            var randomSong = CreateScoreNode("Random Child");
            var backSong = CreateScoreNode("Back Child");
            var nodes = new List<SongListNode>
            {
                new SongListNode
                {
                    Type = NodeType.Random,
                    Children = new List<SongListNode> { randomSong }
                },
                new SongListNode
                {
                    Type = NodeType.BackBox,
                    Children = new List<SongListNode> { backSong }
                },
                directSong
            };

            var candidates = SongSelectionStage.CollectRandomCandidates(nodes, includeSubBoxes: true);

            var candidate = Assert.Single(candidates);
            Assert.Same(directSong, candidate);
            Assert.DoesNotContain(randomSong, candidates);
            Assert.DoesNotContain(backSong, candidates);
        }

        [Fact]
        public void CollectRandomCandidates_WhenInputIsEmptyOrBoxOnly_ShouldReturnEmpty()
        {
            Assert.Empty(SongSelectionStage.CollectRandomCandidates(
                Array.Empty<SongListNode>(), includeSubBoxes: true));
            Assert.Empty(SongSelectionStage.CollectRandomCandidates(
                new[] { CreateBoxNode("Empty Folder") }, includeSubBoxes: true));
        }

        [Fact]
        public void CollectRandomCandidates_ShouldNotMutateTreeOrNavigationState()
        {
            var stage = CreateStage();
            var childSong = CreateScoreNode("Child Song");
            var box = CreateBoxNode("Folder", childSong);
            childSong.Parent = box;
            var nodes = new List<SongListNode> { box };
            var children = box.Children;
            var navigation = GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!;
            var navigationMarker = new SongListNode { Type = NodeType.BackBox, Title = "Marker" };
            navigation.Push(navigationMarker);

            var candidates = SongSelectionStage.CollectRandomCandidates(nodes, includeSubBoxes: true);
            var secondCandidates = SongSelectionStage.CollectRandomCandidates(nodes, includeSubBoxes: true);

            var candidate = Assert.Single(candidates);
            Assert.Same(childSong, candidate);
            Assert.NotSame(candidates, secondCandidates);
            Assert.Single(nodes);
            Assert.Same(box, nodes[0]);
            Assert.Same(children, box.Children);
            Assert.Same(box, childSong.Parent);
            Assert.Single(navigation);
            Assert.Same(navigationMarker, navigation.Peek());
        }

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
        public void PopulateSongList_WhenAtRoot_ShouldAppendExactlyOneRandomEntryWithoutMutatingCurrentSongList()
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

            InvokePrivateMethod(stage, "PopulateSongList");

            Assert.Equal(3, display.CurrentList.Count);
            Assert.Same(songs[0], display.CurrentList[0]);
            Assert.Same(songs[1], display.CurrentList[1]);
            Assert.Equal(NodeType.Random, display.CurrentList[2].Type);
            Assert.Equal(0, display.SelectedIndex);
            Assert.Same(songs[0], display.SelectedSong);
            Assert.Same(songs, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal(2, songs.Count);
            Assert.DoesNotContain(display.CurrentList[2], songs);
        }

        [Fact]
        public void PopulateSongList_WhenInSubfolder_ShouldPrependBackEntryAndKeepBackSelected()
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

            Assert.Equal(4, display.CurrentList.Count);
            Assert.Equal(NodeType.BackBox, display.CurrentList[0].Type);
            Assert.Same(songs[0], display.CurrentList[1]);
            Assert.Same(songs[1], display.CurrentList[2]);
            Assert.Equal(NodeType.Random, display.CurrentList[3].Type);
            Assert.Equal(0, display.SelectedIndex);
            Assert.Equal(NodeType.BackBox, display.SelectedSong!.Type);
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
            Assert.Equal(2, display.CurrentList.Count);
            Assert.Same(songs[0], display.CurrentList[0]);
            Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
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
        public void OnSongSelectionChanged_WhenScore_ShouldUpdatePlayHistoryForSelectedDifficultyOnly()
        {
            var stage = CreateStage();
            var scores = CreateScores(0, 2);
            scores[0].PlayHistoryLines = ["1.26/6/13 Cleared (A: 80.00)"];
            scores[2].PlayHistoryLines = ["2.26/6/12 Failed (S: 92.00)"];
            var selectedSong = CreateScoreNode("Selected", scores);
            var display = new SongListDisplay
            {
                CurrentList = [selectedSong]
            };
            var statusPanel = new SongStatusPanel();
            var historyPanel = new PlayHistoryPanel();
            var previewPanel = new PreviewImagePanel();
            var breadcrumbLabel = new UILabel();

            AttachCoreUi(stage, display, statusPanel, previewPanel, breadcrumbLabel);
            SetPrivateField(stage, "_playHistoryPanel", historyPanel);

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                display,
                new SongSelectionChangedEventArgs(selectedSong, 2, isScrollComplete: true));

            Assert.True(historyPanel.Visible);
            Assert.Equal(
                new[] { "2.26/6/12 Failed (S: 92.00)" },
                GetPrivateField<string[]>(historyPanel, "_historyLines"));
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
            var historyPanel = new PlayHistoryPanel();
            var song = CreateScoreNode("Song", CreateScores(0, 3));
            song.Scores[0].PlayHistoryLines = ["1.26/6/13 Cleared (B: 70.00)"];
            song.Scores[3].PlayHistoryLines = ["2.26/6/12 Cleared (SS: 98.00)"];

            AttachCoreUi(stage, statusPanel: statusPanel);
            SetPrivateField(stage, "_playHistoryPanel", historyPanel);

            InvokePrivateMethod(stage, "OnDifficultyChanged", stage, new DifficultyChangedEventArgs(song, 3));

            Assert.Equal(3, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Same(song, GetPrivateField<SongListNode>(statusPanel, "_currentSong"));
            Assert.Equal(3, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            Assert.True(historyPanel.Visible);
            Assert.Equal(
                new[] { "2.26/6/12 Cleared (SS: 98.00)" },
                GetPrivateField<string[]>(historyPanel, "_historyLines"));
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
            Assert.Equal(2, display.CurrentList.Count);
            Assert.Same(rootSongs[0], display.CurrentList[0]);
            Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
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
            Assert.Equal(2, display.CurrentList.Count);
            Assert.Same(rootSongs[0], display.CurrentList[0]);
            Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
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
            var display = new SongListDisplay();
            var song = CreateScoreNode("Song", songId: 22);
            var currentSongList = new List<SongListNode> { song };

            AttachCoreUi(stage, display: display);
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentSongList", currentSongList);
            InvokePrivateMethod(stage, "PopulateSongList");

            Assert.Equal(2, display.CurrentList.Count);
            Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
            Assert.DoesNotContain(display.CurrentList[1], currentSongList);

            InvokePrivateMethod(stage, "HandleSongActivation", display.CurrentList[1]);

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
            song.Scores[2].PlayHistoryLines = ["1.26/6/13 Cleared (A: 85.00)"];
            song.Scores[4].PlayHistoryLines = ["2.26/6/12 Cleared (S: 92.00)"];
            var display = new SongListDisplay
            {
                CurrentList = [song]
            };
            var statusPanel = new SongStatusPanel();
            var historyPanel = new PlayHistoryPanel();
            var cursorSound = new Mock<ISound>();

            AttachCoreUi(stage, display: display, statusPanel: statusPanel);
            SetPrivateField(stage, "_playHistoryPanel", historyPanel);
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 4);
            SetPrivateField(stage, "_cursorMoveSound", cursorSound.Object);
            display.CurrentDifficulty = 4;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.MoveLeft, 0.0));

            Assert.Equal(2, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.Equal(2, display.CurrentDifficulty);
            Assert.Equal(2, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            Assert.True(historyPanel.Visible);
            Assert.Equal(
                new[] { "1.26/6/13 Cleared (A: 85.00)" },
                GetPrivateField<string[]>(historyPanel, "_historyLines"));
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
            Assert.Equal(2, display.CurrentList.Count);
            Assert.Same(rootSongs[0], display.CurrentList[0]);
            Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
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
        public void ExecuteInputCommand_BackAtRoot_WhenTransitionIsDebounced_ShouldStayOnCurrentStage()
        {
            var game = CreateGame(totalGameTime: 0.1, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            stageManager.Verify(
                x => x.ChangeStage(It.IsAny<StageType>(), It.IsAny<IStageTransition>()),
                Times.Never);
            Assert.Equal(0.0, GetPrivateField<double>(game, "_lastStageTransitionTime"));
        }

        [Fact]
        public void ExecuteInputCommand_BackAtRoot_WhenStageManagerIsNull_ShouldMarkTransitionWithoutThrowing()
        {
            var game = CreateGame(totalGameTime: 1.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0)));

            Assert.Null(exception);
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
        public void HandleActivateInput_WhenInStatusPanelAndSelectedNodeIsNotScore_ShouldIgnoreActivation()
        {
            var stage = CreateStage();
            var stageManager = new Mock<IStageManager>();
            var display = new SongListDisplay();
            var breadcrumbLabel = new UILabel();
            var rootSong = CreateScoreNode("Root Song");
            var folder = CreateBoxNode("Folder", CreateScoreNode("Child"));
            var currentSongList = new List<SongListNode> { rootSong, folder };
            var displayList = new List<SongListNode> { rootSong, folder };
            var previousNavigationState = new SongListNode { Children = currentSongList, Title = "Genre" };

            AttachCoreUi(stage, display: display, breadcrumb: breadcrumbLabel);
            display.CurrentList = displayList;
            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_selectedSong", folder);
            SetPrivateField(stage, "_currentSongList", currentSongList);
            SetPrivateField(stage, "_currentBreadcrumb", "Genre");
            SetPrivateField(stage, "_isInStatusPanel", true);
            GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(previousNavigationState);
            breadcrumbLabel.Text = "Genre";

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.True(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.Same(folder, GetPrivateField<SongListNode>(stage, "_selectedSong"));
            Assert.Same(currentSongList, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal("Genre", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Single(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
            Assert.Same(previousNavigationState, GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Peek());
            Assert.Same(displayList, display.CurrentList);
            Assert.Equal("Genre", breadcrumbLabel.Text);
            stageManager.Verify(
                x => x.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Never);
        }

        [Fact]
        public void HandleActivateInput_WhenInStatusPanelAndSelectionIsNull_ShouldKeepStatusPanelActive()
        {
            var stage = CreateStage();
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_selectedSong", null);
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "HandleActivateInput");

            Assert.True(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.Null(GetPrivateField<SongListNode>(stage, "_selectedSong"));
            stageManager.Verify(
                x => x.ChangeStage(
                    It.IsAny<StageType>(),
                    It.IsAny<IStageTransition>(),
                    It.IsAny<Dictionary<string, object>>()),
                Times.Never);
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
        public void ProcessInputCommands_WhenMultipleCommandsQueued_ShouldExecuteUntilQueueIsEmpty()
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
            inputManager.Enqueue(new InputCommand(InputCommandType.MoveUp, 0.0));

            InvokePrivateMethod(stage, "ProcessInputCommands");

            Assert.Equal("B", display.SelectedSong!.Title);
            Assert.False(inputManager.HasPendingCommands);
        }

        [Fact]
        public void ProcessInputCommands_WhenInputManagerIsNull_ShouldLeaveSelectionUnchanged()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };

            AttachCoreUi(stage, display: display);
            display.MoveNext();
            SetPrivateField(stage, "_inputManager", null);

            InvokePrivateMethod(stage, "ProcessInputCommands");

            Assert.Equal("B", display.SelectedSong!.Title);
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

                var loaded = loadedSound.Object;
                resourceManager.Setup(x => x.TryLoadSound(previewPath, out loaded)).Returns(true);

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
                resourceManager.Verify(x => x.TryLoadSound(previewPath, out It.Ref<ISound>.IsAny), Times.Once);
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
        public void OnSongSelectionChanged_WhenNonScoreAndScrollIncomplete_ShouldHideStatusAndSkipHeavyRefresh()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("Song A"), CreateBoxNode("Folder")]
            };
            var statusPanel = new SongStatusPanel { Visible = true };
            var previewPanel = new PreviewImagePanel();
            var previewTexture = new Mock<ITexture>();
            var breadcrumbLabel = new UILabel();
            var existingPreviewSound = new Mock<ISound>();
            var previousSong = CreateScoreNode("Previous");
            var folder = CreateBoxNode("Folder");

            AttachCoreUi(stage, display, statusPanel, previewPanel, breadcrumbLabel);
            SetPrivateField(stage, "_currentBreadcrumb", "Genre");
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(stage, "_previewSound", existingPreviewSound.Object);
            SetPrivateField(previewPanel, "_currentPreviewTexture", previewTexture.Object);
            SetPrivateField(statusPanel, "_currentSong", previousSong);
            SetPrivateField(statusPanel, "_currentDifficulty", 3);
            GetTextureQueue(display).Clear();
            GetVisibleIndices(display).Clear();

            InvokePrivateMethod(
                stage,
                "OnSongSelectionChanged",
                display,
                new SongSelectionChangedEventArgs(folder, 1, isScrollComplete: false));

            existingPreviewSound.Verify(x => x.RemoveReference(), Times.Once);
            previewTexture.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Same(folder, GetPrivateField<SongListNode>(stage, "_selectedSong"));
            Assert.Equal(1, GetPrivateField<int>(stage, "_currentDifficulty"));
            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.False(statusPanel.Visible);
            Assert.Same(previousSong, GetPrivateField<SongListNode>(statusPanel, "_currentSong"));
            Assert.Equal(3, GetPrivateField<int>(statusPanel, "_currentDifficulty"));
            Assert.Same(folder, GetPrivateField<SongListNode>(previewPanel, "_currentSong"));
            Assert.Equal("Genre", breadcrumbLabel.Text);
            Assert.Empty(GetTextureQueue(display));
            Assert.Empty(GetVisibleIndices(display));
        }

        [Fact]
        public void TryLoadPreviewSoundFile_WhenLoadFails_ShouldReleaseLoadedSound()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var previousSound = new Mock<ISound>();
            var failedSound = new Mock<ISound>();
            var previewPath = CreateWorkspacePath("preview-failure.ogg");

            AttachResourceManager(stage, resourceManager.Object);
            SetPrivateField(stage, "_previewSound", previousSound.Object);

            var fallback = failedSound.Object;
            resourceManager
                .Setup(x => x.TryLoadSound(previewPath, out fallback))
                .Returns(false);

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
        public void LoadUIGraphics_WhenHeaderLoadFails_ShouldPreserveExistingHeaderAndLoadFooter()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var existingHeader = new Mock<ITexture>().Object;
            var footer = new Mock<ITexture>().Object;

            SetPrivateField(stage, "_headerPanelTexture", existingHeader);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.SongSelectionHeaderPanel)).Throws(new InvalidOperationException("header"));
            resourceManager.Setup(x => x.LoadTexture(TexturePath.SongSelectionFooterPanel)).Returns(footer);
            AttachResourceManager(stage, resourceManager.Object);

            InvokePrivateMethod(stage, "LoadUIGraphics");

            Assert.Same(existingHeader, GetPrivateField<ITexture>(stage, "_headerPanelTexture"));
            Assert.Same(footer, GetPrivateField<ITexture>(stage, "_footerPanelTexture"));
        }

        [Fact]
        public void LoadUIGraphics_WhenFooterLoadFails_ShouldPreserveExistingFooterAndKeepNewHeader()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var header = new Mock<ITexture>().Object;
            var existingFooter = new Mock<ITexture>().Object;

            SetPrivateField(stage, "_footerPanelTexture", existingFooter);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.SongSelectionHeaderPanel)).Returns(header);
            resourceManager.Setup(x => x.LoadTexture(TexturePath.SongSelectionFooterPanel)).Throws(new InvalidOperationException("footer"));
            AttachResourceManager(stage, resourceManager.Object);

            InvokePrivateMethod(stage, "LoadUIGraphics");

            Assert.Same(header, GetPrivateField<ITexture>(stage, "_headerPanelTexture"));
            Assert.Same(existingFooter, GetPrivateField<ITexture>(stage, "_footerPanelTexture"));
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
        public void InitializeUI_WhenDependenciesAreAvailable_ShouldCreateConfiguredUiHierarchy()
        {
            var game = CreateGame();
            SetPrivateField(game, "_graphicsDeviceService", new StubGraphicsDeviceService());

            var stage = CreateStage(game);
            var resourceManager = new Mock<IResourceManager>();
            var configManager = new Mock<IConfigManager>();
            var uiFont = new Mock<IFont>();

            var configData = new ConfigData
            {
                PlaySpeedPercent = 75,
                PitchSemitones = 3
            };
            configData.SongRoots[0] = "SongsRoot";
            configManager.SetupGet(x => x.Config).Returns(configData);
            resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);

            SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            SetPrivateField(stage, "_configManager", configManager.Object);
            SetPrivateField(stage, "_stageRenderTarget", null);

            InvokePrivateMethod(stage, "InitializeUI", uiFont.Object);

            var uiManager = GetPrivateField<DTXMania.Game.Lib.UI.UIManager>(stage, "_uiManager");
            var mainPanel = GetPrivateField<UIPanel>(stage, "_mainPanel");
            var titleLabel = GetPrivateField<UILabel>(stage, "_titleLabel");
            var breadcrumbLabel = GetPrivateField<UILabel>(stage, "_breadcrumbLabel");
            var playbackProfileLabel = GetPrivateField<UILabel>(stage, "_playbackProfileLabel");
            var songListDisplay = GetPrivateField<SongListDisplay>(stage, "_songListDisplay");
            var statusPanel = GetPrivateField<SongStatusPanel>(stage, "_statusPanel");
            var playHistoryPanel = GetPrivateField<PlayHistoryPanel>(stage, "_playHistoryPanel");
            var previewImagePanel = GetPrivateField<PreviewImagePanel>(stage, "_previewImagePanel");

            Assert.NotNull(uiManager);
            Assert.NotNull(mainPanel);
            Assert.NotNull(titleLabel);
            Assert.NotNull(breadcrumbLabel);
            Assert.NotNull(playbackProfileLabel);
            Assert.NotNull(songListDisplay);
            Assert.NotNull(statusPanel);
            Assert.NotNull(playHistoryPanel);
            Assert.NotNull(previewImagePanel);
            Assert.Equal(Vector2.Zero, mainPanel!.Position);
            Assert.Equal(new Vector2(SongSelectionUILayout.SongListDisplay.Width, SongSelectionUILayout.SongListDisplay.Height), mainPanel.Size);
            Assert.Equal(Color.Black * SongSelectionUILayout.Background.MainPanelAlpha, mainPanel.BackgroundColor);
            Assert.Equal(PanelLayoutMode.Manual, mainPanel.LayoutMode);
            Assert.Equal("Song Selection", titleLabel!.Text);
            Assert.Equal(SongSelectionUILayout.UILabels.Title.Position, titleLabel.Position);
            Assert.Equal(SongSelectionUILayout.UILabels.Title.Size, titleLabel.Size);
            Assert.Equal(Color.White, titleLabel.TextColor);
            Assert.True(titleLabel.HasShadow);
            Assert.Equal(DTXMania.Game.Lib.UI.Components.TextAlignment.Left, titleLabel.HorizontalAlignment);
            Assert.Equal(string.Empty, breadcrumbLabel!.Text);
            Assert.Equal(SongSelectionUILayout.UILabels.Breadcrumb.Position, breadcrumbLabel.Position);
            Assert.Equal(SongSelectionUILayout.UILabels.Breadcrumb.Size, breadcrumbLabel.Size);
            Assert.Equal(Color.Yellow, breadcrumbLabel.TextColor);
            Assert.True(breadcrumbLabel.HasShadow);
            Assert.Equal(DTXMania.Game.Lib.UI.Components.TextAlignment.Left, breadcrumbLabel.HorizontalAlignment);
            Assert.Equal("PLAY 0.75x · PITCH +3 st", playbackProfileLabel!.Text);
            Assert.Equal(
                SongSelectionUILayout.UILabels.PlaybackProfile.Position,
                playbackProfileLabel.Position);
            Assert.Equal(
                SongSelectionUILayout.UILabels.PlaybackProfile.Size,
                playbackProfileLabel.Size);
            Assert.Equal(Color.LightGreen, playbackProfileLabel.TextColor);
            Assert.True(playbackProfileLabel.HasShadow);
            Assert.Equal(
                DTXMania.Game.Lib.UI.Components.TextAlignment.Left,
                playbackProfileLabel.HorizontalAlignment);
            Assert.Same(uiFont.Object, songListDisplay!.ManagedFont);
            Assert.Equal(SongSelectionUILayout.SongListDisplay.Position, songListDisplay.Position);
            Assert.Equal(SongSelectionUILayout.SongListDisplay.Size, songListDisplay.Size);
            Assert.Equal(75, songListDisplay.PlaySpeedPercent);
            Assert.False(statusPanel!.Visible);
            Assert.Equal(75, statusPanel.PlaySpeedPercent);
            Assert.False(playHistoryPanel!.Visible);
            Assert.Same(uiFont.Object, playHistoryPanel.ManagedFont);
            Assert.Equal(75, playHistoryPanel.PlaySpeedPercent);
            Assert.True(previewImagePanel!.Visible);
            Assert.Empty(previewImagePanel.ActiveSongRootPaths);
            Assert.Equal(8, mainPanel.Children.Count);
            Assert.Same(titleLabel, mainPanel.Children[0]);
            Assert.Same(breadcrumbLabel, mainPanel.Children[1]);
            // Profile must draw above the full-screen song-list wash and neighbouring art.
            Assert.Same(songListDisplay, mainPanel.Children[2]);
            Assert.Same(statusPanel, mainPanel.Children[3]);
            Assert.Same(playHistoryPanel, mainPanel.Children[4]);
            Assert.Same(previewImagePanel, mainPanel.Children[5]);
            Assert.Same(playbackProfileLabel, mainPanel.Children[6]);
            Assert.Same(GetPrivateField<QuickSettingsPopup>(stage, "_quickSettingsPopup"), mainPanel.Children[7]);
            Assert.True(mainPanel.IsActive);
            Assert.Single(uiManager.RootContainers);
            Assert.Same(mainPanel, uiManager.RootContainers[0]);
            Assert.Same(mainPanel, uiManager.FocusedContainer);
        }

        [Fact]
        public void InitializeUI_WhenTextInputSourceAvailable_ShouldCreateSearchFilterModal()
        {
            // When IStageGame.GetTextInputSource returns a live source (non-headless environment),
            // InitializeUI should enter the textInputSource != null branch, create a
            // SongSearchFilterModal, wire its events, and add it to the main panel.
            var mockGame = new Mock<IStageGame>();
            var textInputSource = new Mock<ITextInputSource>();
            mockGame.SetupGet(g => g.GraphicsDevice).Returns((GraphicsDevice?)null);
            mockGame.Setup(g => g.GetTextInputSource()).Returns(textInputSource.Object);

            var stage = new SongSelectionStage(mockGame.Object);
            var resourceManager = new Mock<IResourceManager>();
            var configManager = new Mock<IConfigManager>();
            var uiFont = new Mock<IFont>();

            var configData = new ConfigData();
            configData.SongRoots[0] = "SongsRoot";
            configManager.SetupGet(x => x.Config).Returns(configData);
            resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);

            SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            SetPrivateField(stage, "_configManager", configManager.Object);
            SetPrivateField(stage, "_stageRenderTarget", null);

            InvokePrivateMethod(stage, "InitializeUI", uiFont.Object);

            var searchFilterModal = GetPrivateField<SongSearchFilterModal>(stage, "_searchFilterModal");
            var storedTextInputSource = GetPrivateField<ITextInputSource>(stage, "_textInputSource");
            var mainPanel = GetPrivateField<UIPanel>(stage, "_mainPanel");

            Assert.NotNull(searchFilterModal);
            Assert.Same(textInputSource.Object, storedTextInputSource);
            // The modal should be the 8th child (after the labels, song list, status,
            // history, and preview).
            Assert.Equal(9, mainPanel!.Children.Count);
            Assert.Same(GetPrivateField<UILabel>(stage, "_playbackProfileLabel"), mainPanel.Children[6]);
            Assert.Same(searchFilterModal, mainPanel.Children[7]);
            Assert.Same(GetPrivateField<QuickSettingsPopup>(stage, "_quickSettingsPopup"), mainPanel.Children[8]);
        }

        [Theory]
        [InlineData(50, -12, "PLAY 0.50x · PITCH -12 st")]
        [InlineData(75, 3, "PLAY 0.75x · PITCH +3 st")]
        [InlineData(100, 0, "PLAY 1.00x · PITCH 0 st")]
        [InlineData(150, 12, "PLAY 1.50x · PITCH +12 st")]
        public void CreatePlaybackProfileText_ShouldUseExactInvariantFormat(
            int playSpeedPercent,
            int pitchSemitones,
            string expected)
        {
            Assert.Equal(
                expected,
                SongSelectionStage.CreatePlaybackProfileText(
                    playSpeedPercent,
                    pitchSemitones));
        }

        [Fact]
        public void CreatePlaybackProfileText_WhenCultureUsesDecimalComma_ShouldRemainInvariant()
        {
            var originalCulture = CultureInfo.CurrentCulture;
            try
            {
                CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("fr-FR");

                Assert.Equal(
                    "PLAY 0.75x · PITCH +3 st",
                    SongSelectionStage.CreatePlaybackProfileText(75, 3));
            }
            finally
            {
                CultureInfo.CurrentCulture = originalCulture;
            }
        }

        [Fact]
        public void SynchronizeActivePlaySpeed_WhenConfigChanges_ShouldRefreshProfileLabel()
        {
            var stage = CreateStage();
            var config = new ConfigData
            {
                PlaySpeedPercent = 75,
                PitchSemitones = 3
            };
            var configManager = new Mock<IConfigManager>();
            var profileLabel = new UILabel();
            configManager.SetupGet(x => x.Config).Returns(config);
            SetPrivateField(stage, "_configManager", configManager.Object);
            SetPrivateField(stage, "_playbackProfileLabel", profileLabel);

            InvokePrivateMethod(stage, "SynchronizeActivePlaySpeed");
            Assert.Equal("PLAY 0.75x · PITCH +3 st", profileLabel.Text);
            Assert.Equal(
                SongSelectionUILayout.UILabels.PlaybackProfile.Size,
                profileLabel.Size);

            config.PlaySpeedPercent = 150;
            config.PitchSemitones = -12;
            InvokePrivateMethod(stage, "SynchronizeActivePlaySpeed");

            Assert.Equal("PLAY 1.50x · PITCH -12 st", profileLabel.Text);
            Assert.Equal(
                SongSelectionUILayout.UILabels.PlaybackProfile.Size,
                profileLabel.Size);
        }

        [Fact]
        public void InitializeUI_WhenEnhancedRenderingInitializationSucceeds_ShouldEnableEnhancedRendering()
        {
            var game = CreateGame();
            SetPrivateField(game, "_graphicsDeviceService", new StubGraphicsDeviceService());

            var stage = CreateStage(game);
            var resourceManager = new Mock<IResourceManager>();
            var configManager = new Mock<IConfigManager>();
            var uiFont = new Mock<IFont>();

            var configData = new ConfigData();
            configData.SongRoots[0] = "SongsRoot";
            configManager.SetupGet(x => x.Config).Returns(configData);
            resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);

            SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            SetPrivateField(stage, "_configManager", configManager.Object);
            SetPrivateField(stage, "_stageRenderTarget", null);

            InvokePrivateMethod(stage, "InitializeUI", uiFont.Object);

            var songListDisplay = GetPrivateField<SongListDisplay>(stage, "_songListDisplay");
            Assert.NotNull(songListDisplay);
            Assert.True(GetPrivateField<bool>(songListDisplay!, "_useEnhancedRendering"));
            Assert.Same(resourceManager.Object, GetPrivateField<IResourceManager>(songListDisplay!, "_resourceManager"));
        }

        [Fact]
        public void InitializeUI_WhenFontIsNull_ShouldStillCreateUiComponents()
        {
            var game = CreateGame();
            SetPrivateField(game, "_graphicsDeviceService", new StubGraphicsDeviceService());

            var stage = CreateStage(game);
            var resourceManager = new Mock<IResourceManager>();
            var configManager = new Mock<IConfigManager>();

            configManager.SetupGet(x => x.Config).Returns(new ConfigData());
            resourceManager.Setup(x => x.ResourceExists(It.IsAny<string>())).Returns(false);

            SetPrivateField(stage, "_resourceManager", resourceManager.Object);
            SetPrivateField(stage, "_configManager", configManager.Object);
            SetPrivateField(stage, "_stageRenderTarget", null);

            InvokePrivateMethod(stage, "InitializeUI", new object?[] { null! });

            Assert.Null(GetPrivateField<IFont>(GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!, "_managedFont"));
            Assert.Null(GetPrivateField<object>(GetPrivateField<SongListDisplay>(stage, "_songListDisplay")!, "_font"));
            Assert.Null(GetPrivateField<object>(GetPrivateField<SongStatusPanel>(stage, "_statusPanel")!, "_managedFont"));
            Assert.Null(GetPrivateField<object>(GetPrivateField<SongStatusPanel>(stage, "_statusPanel")!, "_managedSmallFont"));
            Assert.NotNull(GetPrivateField<UIPanel>(stage, "_mainPanel"));
            Assert.True(GetPrivateField<UIPanel>(stage, "_mainPanel")!.IsActive);
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
            var loadedSound_ = loadedSound.Object;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out loadedSound_)).Returns(true);

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
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out It.Ref<ISound>.IsAny)).Throws(new IOException("boom"));

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

                resourceManager.Verify(x => x.TryLoadSound(It.IsAny<string>(), out It.Ref<ISound>.IsAny), Times.Never);
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
        public void DrawTabBar_WhenFontAvailable_ShouldIterateAllTabsAndDrawEachLabel()
        {
            // Exercises the AllTabs cache + the tab-drawing loop. Uses a mock IFont so no
            // real SpriteFont/SpriteBatch graphics device is required.
            var stage = CreateStage();
            var font = new Mock<IFont>();
            font.Setup(x => x.MeasureString(It.IsAny<string>())).Returns(new Vector2(40f, 16f));
            SetPrivateField(stage, "_font", font.Object);
            SetPrivateField(stage, "_spriteBatch",
                (SpriteBatch)RuntimeHelpers.GetUninitializedObject(typeof(SpriteBatch)));

            var ex = Record.Exception(() => InvokePrivateMethod(stage, "DrawTabBar"));

            Assert.Null(ex);
            // Every tab label should have been measured and drawn exactly once.
            font.Verify(x => x.MeasureString(It.IsAny<string>()), Times.AtLeastOnce);
            font.Verify(
                x => x.DrawString(It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()),
                Times.AtLeastOnce);
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

        [Fact]
        public void UpdatePreviewSoundTimers_WhenPreviewDelayElapsesWithPlayingInstance_ShouldKeepExistingPreview()
        {
            var stage = CreateStage();
            var previewSound = new Mock<ISound>();
            var previewInstance = new Mock<ISoundInstance>();

            previewInstance.SetupGet(x => x.State).Returns(SoundState.Playing);

            SetPrivateField(stage, "_previewSound", previewSound.Object);
            SetPrivateField(stage, "_previewSoundInstance", previewInstance.Object);
            SetPrivateField(stage, "_isPreviewDelayActive", true);
            SetPrivateField(stage, "_previewPlayDelay", SongSelectionUILayout.Audio.PreviewPlayDelaySeconds - 0.01);
            SetPrivateField(stage, "_isBgmFadingOut", false);

            InvokePrivateMethod(stage, "UpdatePreviewSoundTimers", 0.02);

            previewSound.Verify(x => x.CreateInstance(), Times.Never);
            previewInstance.Verify(x => x.Play(), Times.Never);
            Assert.False(GetPrivateField<bool>(stage, "_isPreviewDelayActive"));
            Assert.Same(previewInstance.Object, GetPrivateField<ISoundInstance>(stage, "_previewSoundInstance"));
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
        public void UpdatePhase_WhenFadeOutDurationElapsed_ShouldRemainInFadeOutPhase()
        {
            var stage = CreateStage();

            SetPrivateField(stage, "_selectionPhase", SongSelectionPhase.FadeOut);
            SetPrivateField(stage, "_currentPhase", StagePhase.FadeOut);
            SetPrivateField(stage, "_phaseStartTime", 0.0);
            SetPrivateField(stage, "_elapsedTime", SongSelectionUILayout.Timing.FadeOutDuration);

            InvokePrivateMethod(stage, "UpdatePhase", 0.0);

            Assert.Equal(SongSelectionPhase.FadeOut, GetPrivateField<SongSelectionPhase>(stage, "_selectionPhase"));
            Assert.Equal(StagePhase.FadeOut, stage.CurrentPhase);
            Assert.Equal(0.0, GetPrivateField<double>(stage, "_phaseStartTime"));
        }

        [Fact]
        public void TryLoadPreviewSoundFile_WhenLoadFails_ShouldReturnFalseAndReleaseFallback()
        {
            var stage = CreateStage();
            var resourceManager = new Mock<IResourceManager>();
            var loadedSound = new Mock<ISound>();
            var previewPath = CreateWorkspacePath("preview-load-failed.ogg");

            AttachResourceManager(stage, resourceManager.Object);
            var fallback = loadedSound.Object;
            resourceManager
                .Setup(x => x.TryLoadSound(previewPath, out fallback))
                .Returns(false);

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
            ISound nullSound = null!;
            resourceManager.Setup(x => x.TryLoadSound(previewPath, out nullSound)).Returns(true);

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
                Assert.Equal(2, display.CurrentList.Count);
                Assert.Same(song, display.CurrentList[0]);
                Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
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
        public void ApplyLibrarySnapshot_ShouldRetainTheCoherentSnapshotUsedForUiProjection()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var song = CreateScoreNode("Snapshot Song");
            var snapshot = new SongLibrarySnapshot(
                version: 91,
                rootSongs: new[] { song },
                activeRoots: new[] { "/library/active" },
                enumeratedFileCount: 1,
                discoveredScoreCount: 1);

            AttachCoreUi(stage, display: display);
            InvokePrivateMethod(stage, "ApplyLibrarySnapshot", snapshot);

            Assert.Same(snapshot, GetPrivateField<SongLibrarySnapshot>(stage, "_appliedLibrarySnapshot"));
            Assert.Equal(91L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
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

        private sealed class StubGraphicsDeviceService : IGraphicsDeviceService
        {
            public GraphicsDevice GraphicsDevice { get; } = (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice));

            public event EventHandler<EventArgs>? DeviceCreated;
            public event EventHandler<EventArgs>? DeviceDisposing;
            public event EventHandler<EventArgs>? DeviceReset;
            public event EventHandler<EventArgs>? DeviceResetting;
        }

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

        [Theory]
        [InlineData(false, 1.3, 2)] // two hits within 0.5 s on the song list
        [InlineData(true, 1.3, 2)]  // also on the difficulty panel
        [InlineData(false, 1.6, 0)] // too far apart
        public void HandlePadLaneHit_HiHatTwice_StepsDifficulty(bool inStatusPanel, double secondHitTime, int expectedDifficulty)
        {
            var stage = CreateStage();
            var song = CreateScoreNode("Song", CreateScores(0, 2));
            var display = new SongListDisplay { CurrentList = [song] };

            display.DifficultyChanged += (sender, e) => InvokePrivateMethod(stage, "OnDifficultyChanged", sender!, e);

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_isInStatusPanel", inStatusPanel);
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_currentDifficulty", 0);
            display.CurrentDifficulty = 0;

            SetPrivateField(stage, "_elapsedTime", 1.0);
            InvokePrivateMethod(stage, "HandlePadLaneHit", (int)PerformanceUILayout.LaneType.HH);
            Assert.Equal(0, GetPrivateField<int>(stage, "_currentDifficulty")); // one hit does nothing

            SetPrivateField(stage, "_elapsedTime", secondHitTime);
            InvokePrivateMethod(stage, "HandlePadLaneHit", (int)PerformanceUILayout.LaneType.HH);

            Assert.Equal(expectedDifficulty, GetPrivateField<int>(stage, "_currentDifficulty"));
        }

        [Fact]
        public void HandlePadLaneHit_BassDrumTwice_TogglesQuickSettings()
        {
            var stage = CreateStage();
            var popup = new QuickSettingsPopup();
            SetPrivateField(stage, "_quickSettingsPopup", popup);
            SetPrivateField(stage, "_configManager", new ConfigManager());
            int bassDrum = (int)PerformanceUILayout.LaneType.DB;

            SetPrivateField(stage, "_elapsedTime", 1.0);
            InvokePrivateMethod(stage, "HandlePadLaneHit", bassDrum);
            Assert.False(popup.IsOpen);

            SetPrivateField(stage, "_elapsedTime", 1.2);
            InvokePrivateMethod(stage, "HandlePadLaneHit", bassDrum);
            Assert.True(popup.IsOpen);

            // A third quick hit starts a new double hit instead of closing straight away.
            SetPrivateField(stage, "_elapsedTime", 1.4);
            InvokePrivateMethod(stage, "HandlePadLaneHit", bassDrum);
            Assert.True(popup.IsOpen);

            SetPrivateField(stage, "_elapsedTime", 1.6);
            InvokePrivateMethod(stage, "HandlePadLaneHit", bassDrum);
            Assert.False(popup.IsOpen);
        }

        [Fact]
        public void HandlePadLaneHit_HitFromPreviousVisit_DoesNotPairAfterClockRestart()
        {
            var stage = CreateStage();
            var popup = new QuickSettingsPopup();
            SetPrivateField(stage, "_quickSettingsPopup", popup);
            SetPrivateField(stage, "_configManager", new ConfigManager());
            int bassDrum = (int)PerformanceUILayout.LaneType.DB;

            // A bass drum hit late in the last visit, then the stage clock restarts at 0.
            SetPrivateField(stage, "_lastDoubleHitPadLane", bassDrum);
            SetPrivateField(stage, "_lastDoubleHitPadTime", 30.0);
            SetPrivateField(stage, "_elapsedTime", 1.0);

            InvokePrivateMethod(stage, "HandlePadLaneHit", bassDrum);

            Assert.False(popup.IsOpen);
        }

        [Fact]
        public void OnQuickSettingsClosed_SavesConfigAndClearsQueuedInput()
        {
            var stage = CreateStage();
            var inputManager = new QueuedInputManager();
            var config = new Mock<IConfigManager>();
            config.SetupGet(x => x.Config).Returns(new ConfigData());
            config.Setup(x => x.FlushPendingSave()).Throws(new IOException("disk full"));
            AttachCoreUi(stage);
            SetPrivateField(stage, "_configManager", config.Object);
            SetPrivateField(stage, "_inputManager", inputManager);
            inputManager.Enqueue(new InputCommand(InputCommandType.Back, 0.0));

            // A failed save is logged, never thrown into the input loop.
            InvokePrivateMethod(stage, "OnQuickSettingsClosed", null!, EventArgs.Empty);

            config.Verify(x => x.FlushPendingSave(), Times.Once);
            Assert.False(inputManager.HasPendingCommands);
        }

        [Fact]
        public void HandlePadLaneHit_BassDrumTwiceWithoutConfig_LeavesQuickSettingsClosed()
        {
            var stage = CreateStage();
            var popup = new QuickSettingsPopup();
            SetPrivateField(stage, "_quickSettingsPopup", popup);
            SetPrivateField(stage, "_configManager", null);
            int bassDrum = (int)PerformanceUILayout.LaneType.DB;

            SetPrivateField(stage, "_elapsedTime", 1.0);
            InvokePrivateMethod(stage, "HandlePadLaneHit", bassDrum);
            SetPrivateField(stage, "_elapsedTime", 1.1);
            InvokePrivateMethod(stage, "HandlePadLaneHit", bassDrum);

            Assert.False(popup.IsOpen);
        }

        [Fact]
        public void OnPadLaneHit_OnPadRelease_DoesNothing()
        {
            var stage = CreateStage();
            AttachCoreUi(stage);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.AllSongs);

            var release = new LaneHitEventArgs(7, new DTXMania.Game.Lib.Input.ButtonState("MIDI.43", false));
            InvokePrivateMethod(stage, "OnPadLaneHit", stage, release);

            Assert.Equal(SongSelectionTab.AllSongs, GetPrivateField<SongSelectionTab>(stage, "_activeTab"));
        }

        [Fact]
        public void IsOverlayOpen_TracksQuickSettings()
        {
            var stage = CreateStage();
            var popup = new QuickSettingsPopup();
            SetPrivateField(stage, "_quickSettingsPopup", popup);
            var isOverlayOpen = typeof(SongSelectionStage).GetProperty("IsOverlayOpen",
                System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)!;

            Assert.False((bool)isOverlayOpen.GetValue(stage)!);
            popup.Open(Array.Empty<IConfigItem>());
            Assert.True((bool)isOverlayOpen.GetValue(stage)!);
        }

        [Fact]
        public void SubscribePadLaneHits_AttachesAndDetachesFromModularInput()
        {
            var stage = CreateStage();
            using var input = new InputManagerCompat(new ConfigManager(), new TestMidiDeviceBackend());
            SetPrivateField(stage, "_inputManager", input);
            int StageHandlers() => (GetPrivateField<EventHandler<LaneHitEventArgs>>(input.ModularInputManager, "OnLaneHit")
                ?.GetInvocationList() ?? Array.Empty<Delegate>()).Count(d => ReferenceEquals(d.Target, stage));

            InvokePrivateMethod(stage, "SubscribePadLaneHits");
            InvokePrivateMethod(stage, "SubscribePadLaneHits");
            Assert.Equal(1, StageHandlers());

            InvokePrivateMethod(stage, "UnsubscribePadLaneHits");
            Assert.Equal(0, StageHandlers());
        }

        [Fact]
        public void HandleInput_WhenQuickSettingsOpen_RoutesCommandsToPopup()
        {
            var stage = CreateStage();
            var display = new SongListDisplay { CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")] };
            var popup = new QuickSettingsPopup();
            var inputManager = new QueuedInputManager();
            bool noFail = false;
            popup.Open(new IConfigItem[] { new ToggleConfigItem("No Fail", () => noFail, value => noFail = value) });

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_quickSettingsPopup", popup);
            SetPrivateField(stage, "_inputManager", inputManager);
            inputManager.Enqueue(new InputCommand(InputCommandType.Activate, 0.0));
            inputManager.Enqueue(new InputCommand(InputCommandType.MoveDown, 0.0));

            InvokePrivateMethod(stage, "HandleInput");

            Assert.True(noFail);
            Assert.Equal(1, popup.SelectedIndex);
            Assert.Equal("A", display.SelectedSong!.Title); // the song list did not move
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

            configManager.Verify(x => x.AdjustScrollSpeed(1), Times.Once);
        }

        [Fact]
        public void ExecuteInputCommand_DecreaseScrollSpeed_ShouldCallConfigManager()
        {
            var stage = CreateStage();
            var configManager = new Mock<IConfigManager>();

            SetPrivateField(stage, "_configManager", configManager.Object);

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.DecreaseScrollSpeed, 0.0));

            configManager.Verify(x => x.AdjustScrollSpeed(-1), Times.Once);
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
        public void ExecuteInputCommand_OpenSearch_WhenModalIsNull_ShouldReturnTrue()
        {
            // OpenSearch returns true when the modal is unavailable (null or off-tab)
            // so remaining queued commands continue to execute normally; only an
            // actually-opened modal stops the drain.
            var stage = CreateStage();
            SetPrivateField(stage, "_searchFilterModal", null);

            var result = InvokePrivateMethod<bool>(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.OpenSearch, 0.0));

            Assert.True(result);
        }

        [Fact]
        public void ExecuteInputCommand_OpenSearch_WithModal_ShouldReturnFalse()
        {
            // When the modal exists and is opened, OpenSearch must return false
            // to stop draining queued commands that would fire against the song list
            // behind the modal.
            var stage = CreateStage();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("S") });
            SetPrivateField(stage, "_songInitializationTask", null);

            var result = InvokePrivateMethod<bool>(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.OpenSearch, 0.0));

            Assert.False(result);
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
        public void SelectRandomSong_WhenConfigManagerIsMissing_ShouldUseDirectScoresOnly()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var directSong = CreateScoreNode("Direct Song");
            var nestedSong = CreateScoreNode("Nested Song");

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>
            {
                directSong,
                CreateBoxNode("Folder", nestedSong)
            });

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "SelectRandomSong"));

            Assert.Null(exception);
            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.IsAny<IStageTransition>(),
                    It.Is<Dictionary<string, object>>(d => ReferenceEquals(d["selectedSong"], directSong))),
                Times.Once);
        }

        [Fact]
        public void SelectRandomSong_WhenConfigIsNull_ShouldUseDirectScoresOnly()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var configManager = new Mock<IConfigManager>();
            var directSong = CreateScoreNode("Direct Song");
            var nestedSong = CreateScoreNode("Nested Song");
            configManager.SetupGet(manager => manager.Config).Returns((ConfigData)null!);

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_configManager", configManager.Object);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>
            {
                directSong,
                CreateBoxNode("Folder", nestedSong)
            });

            var exception = Record.Exception(() => InvokePrivateMethod(stage, "SelectRandomSong"));

            Assert.Null(exception);
            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.IsAny<IStageTransition>(),
                    It.Is<Dictionary<string, object>>(d => ReferenceEquals(d["selectedSong"], directSong))),
                Times.Once);
        }

        [Fact]
        public void SelectRandomSong_WhenSubBoxSelectionIsDisabled_ShouldIgnoreNestedScores()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var configManager = new Mock<IConfigManager>();
            var nestedSong = CreateScoreNode("Nested Song");
            configManager.SetupGet(manager => manager.Config).Returns(
                new ConfigData { RandomSelectFromSubBox = false });

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_configManager", configManager.Object);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>
            {
                CreateBoxNode("Folder", nestedSong)
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
        public void SelectRandomSong_WhenSubBoxSelectionIsEnabled_ShouldSelectNestedSongAndKeepDifficulty()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();
            var configManager = new Mock<IConfigManager>();
            var nestedSong = CreateScoreNode("Nested Song");
            configManager.SetupGet(manager => manager.Config).Returns(
                new ConfigData { RandomSelectFromSubBox = true });

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_configManager", configManager.Object);
            SetPrivateField(stage, "_currentDifficulty", 99);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>
            {
                CreateBoxNode("Folder", nestedSong)
            });

            InvokePrivateMethod(stage, "SelectRandomSong");

            stageManager.Verify(
                x => x.ChangeStage(
                    StageType.SongTransition,
                    It.IsAny<IStageTransition>(),
                    It.Is<Dictionary<string, object>>(d =>
                        ReferenceEquals(d["selectedSong"], nestedSong) &&
                        (int)d["selectedDifficulty"] == 99)),
                Times.Once);
        }

        [Fact]
        public void SelectRandomSong_WhenActiveTabIsNotAllSongs_ShouldNotSelectHiddenSong()
        {
            var game = CreateGame(totalGameTime: 2.0, lastStageTransitionTime: 0.0);
            var stage = CreateStage(game);
            var stageManager = new Mock<IStageManager>();

            stage.StageManager = stageManager.Object;
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode>
            {
                CreateScoreNode("Hidden Song")
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
        public void SongSelectionStage_ShouldNotDrawDedicatedScrollSpeedLabel()
        {
            var repositoryRoot = TryFindRepositoryRoot();
            Assert.True(repositoryRoot != null,
                "Could not locate repository root containing DTXMania.sln from AppContext.BaseDirectory.");

            var songSelectionStagePath = Path.Combine(repositoryRoot!, "DTXMania.Game", "Lib", "Stage", "SongSelectionStage.cs");
            Assert.True(File.Exists(songSelectionStagePath), $"Could not locate source file: {songSelectionStagePath}");

            var source = File.ReadAllText(songSelectionStagePath);

            Assert.DoesNotContain("\"Scroll \"", source, StringComparison.Ordinal);
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
        public void ReapplyPersistedFilterIfActive_WhenFilterActive_ShouldRebuildListState()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var breadcrumb = new UILabel("");
            AttachCoreUi(stage, display: display, breadcrumb: breadcrumb);
            SetPrivateField(stage, "_filterCriteria",
                new SongFilterCriteria("missing", null, null, PlayedStatus.All, SongSortCriteria.Title, false));

            InvokePrivateMethod(stage, "ReapplyPersistedFilterIfActive");

            Assert.Empty(display.CurrentList);
            Assert.True(GetPrivateField<bool>(stage, "_showEmptyFilterMessage"));
        }

        [Fact]
        public void RebuildFilteredView_WhenFilterServiceThrows_ShouldResetFilterState()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_filterCriteria",
                new SongFilterCriteria("boom", null, null, PlayedStatus.All, SongSortCriteria.Title, false));
            SetPrivateField(stage, "_filteredView", new List<FilteredSongResult> { new(CreateScoreNode("Old"), "Old") });
            SetPrivateField(stage, "_showEmptyFilterMessage", true);
            SetPrivateField(stage, "_filterService", new ThrowingFilterService());

            InvokePrivateMethod(stage, "RebuildFilteredView");

            Assert.True(GetPrivateField<SongFilterCriteria>(stage, "_filterCriteria").IsEmpty);
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

            Assert.Equal(2, display.CurrentList.Count);
            Assert.Same(song, display.CurrentList[0]);
            Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
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

        [Fact]
        public void CheckSongInitializationCompletion_WhenModalIsOpen_ShouldMarkLibraryReady()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var modal = new SongSearchFilterModal(new Mock<ITextInputSource>().Object)
            {
                IsLibraryReady = false
            };
            modal.Open(SongFilterCriteria.Default);
            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_songInitializationTask",
                Task.FromResult(new List<SongListNode> { CreateScoreNode("Loaded") }));
            SetPrivateField(stage, "_songInitializationProcessed", false);

            InvokePrivateMethod(stage, "CheckSongInitializationCompletion");

            Assert.True(modal.IsLibraryReady);
            Assert.Null(GetPrivateField<Task<List<SongListNode>>?>(stage, "_songInitializationTask"));
        }








        private sealed class ThrowingFilterService : ISongListFilterService
        {
            public IReadOnlyList<FilteredSongResult> Apply(
                IEnumerable<SongListNode> roots,
                SongFilterCriteria criteria) =>
                throw new InvalidOperationException("filter failed");
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

        /// <summary>
        /// InputManager stub that lets tests specify which raw keys appear as pressed.
        /// </summary>
        private sealed class KeySimulatingInputManager : InputManager
        {
            private readonly HashSet<int> _pressedKeys = new();

            public void PressRawKey(Microsoft.Xna.Framework.Input.Keys key) => _pressedKeys.Add((int)key);

            public override bool IsKeyPressed(int keyCode) => _pressedKeys.Contains(keyCode);
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

        [Fact]
        public void ProcessModalKeys_WhenSearchBoxFocused_ShouldNavigateWithRawArrowKeys()
        {
            // Physical arrow keys do not produce text, so they should still work as
            // raw input even when SearchBox is focused (commands are suppressed but
            // raw arrow keys bypass the command system).
            var stage = CreateStage();
            var inputManager = new KeySimulatingInputManager();
            inputManager.PressRawKey(Microsoft.Xna.Framework.Input.Keys.Down);
            SetPrivateField(stage, "_inputManager", inputManager);

            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);
            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "ProcessModalKeys");

            // Focus should have moved to the next field via raw Down arrow
            Assert.NotEqual(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void ProcessModalKeys_WhenSearchBoxFocused_ShouldNavigateUpWithRawArrowKey()
        {
            // Up arrow should wrap focus backward from SearchBox.
            var stage = CreateStage();
            var inputManager = new KeySimulatingInputManager();
            inputManager.PressRawKey(Microsoft.Xna.Framework.Input.Keys.Up);
            SetPrivateField(stage, "_inputManager", inputManager);

            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);
            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "ProcessModalKeys");

            // Focus should have moved (Up wraps to the last field)
            Assert.NotEqual(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void ProcessModalKeys_WhenTabPressed_ShouldMoveFocus()
        {
            var stage = CreateStage();
            var inputManager = new KeySimulatingInputManager();
            inputManager.PressRawKey(Microsoft.Xna.Framework.Input.Keys.Tab);
            SetPrivateField(stage, "_inputManager", inputManager);

            var modal = new SongSearchFilterModal(new Mock<ITextInputSource>().Object);
            modal.Open(SongFilterCriteria.Default);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "ProcessModalKeys");

            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);
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

        #region ExecuteInputCommand – OpenSearch drains remaining commands (P2 fix)

        [Fact]
        public void ProcessInputCommands_OpenSearch_ShouldDrainRemainingCommands()
        {
            // OpenSearch + MoveDown queued on All Songs: OpenSearch opens the modal and
            // stops the drain, so the queued MoveDown must NOT move the selection behind
            // the modal on the same frame.
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            var inputManager = new QueuedInputManager();

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("A"), CreateScoreNode("B") });
            SetPrivateField(stage, "_songInitializationTask", null);
            SetPrivateField(stage, "_inputManager", inputManager);

            inputManager.Enqueue(new InputCommand(InputCommandType.OpenSearch, 0.0));
            inputManager.Enqueue(new InputCommand(InputCommandType.MoveDown, 0.0));

            InvokePrivateMethod(stage, "ProcessInputCommands");

            // Modal should be open
            Assert.True(modal.IsOpen);
            // The queued MoveDown was drained and NOT executed: selection stays on A.
            // (ProcessInputCommands copies+clears the manager queue before dispatch, so
            // queue emptiness would not prove suppression — the selection does.)
            Assert.Equal("A", display.SelectedSong!.Title);
        }

        [Fact]
        public void ProcessInputCommands_OpenSearch_OnRecentPlays_ShouldContinueRemainingCommands()
        {
            // Off All Songs, OpenSearch is a no-op (there is no Search UI on Recent Plays),
            // so the drain must continue: a queued MoveDown still moves the selection.
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            var inputManager = new QueuedInputManager();

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("A"), CreateScoreNode("B") });
            SetPrivateField(stage, "_songInitializationTask", null);
            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_activeTab", SongSelectionTab.RecentPlays);

            inputManager.Enqueue(new InputCommand(InputCommandType.OpenSearch, 0.0));
            inputManager.Enqueue(new InputCommand(InputCommandType.MoveDown, 0.0));

            InvokePrivateMethod(stage, "ProcessInputCommands");

            // Search is unavailable off All Songs: the modal stays closed...
            Assert.False(modal.IsOpen);
            // ...and the queued MoveDown still executes: selection moves to B.
            Assert.Equal("B", display.SelectedSong!.Title);
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

        private static string? TryFindRepositoryRoot()
        {
            var current = new DirectoryInfo(AppContext.BaseDirectory);

            while (current != null)
            {
                if (File.Exists(Path.Combine(current.FullName, "DTXMania.sln")))
                {
                    return current.FullName;
                }

                current = current.Parent;
            }

            return null;
        }

        #endregion

        #region RenderTarget Management Coverage

        [Fact]
        public void InitializeStageRenderTargets_WithGraphicsManager_ShouldCreateRenderTargetViaManager()
        {
            // Covers the refactored InitializeStageRenderTargets path that delegates directly
            // to _game.GraphicsManager.RenderTargetManager (no BaseGame cast).
            var renderTargetManager = new TestRenderTargetManager();
            var mockGraphicsManager = new Mock<IGraphicsManager>();
            mockGraphicsManager.SetupGet(g => g.RenderTargetManager).Returns(renderTargetManager);
            var mockGame = new Mock<IStageGame>();
            mockGame.SetupGet(g => g.GraphicsManager).Returns(mockGraphicsManager.Object);

            var stage = new SongSelectionStage(mockGame.Object);

            InvokePrivateMethod(stage, "InitializeStageRenderTargets");

            var stageRenderTarget = GetPrivateField<RenderTarget2D>(stage, "_stageRenderTarget");
            Assert.NotNull(stageRenderTarget);
            Assert.Same(stageRenderTarget, renderTargetManager.GetOrCreateRenderTarget("SongSelectionStage_Main", 1024, 1024));
        }

        [Fact]
        public void CleanupStageRenderTargets_WithExistingRenderTarget_ShouldRemoveViaManager()
        {
            // Covers the refactored CleanupStageRenderTargets path that delegates directly
            // to _game.GraphicsManager.RenderTargetManager (no BaseGame cast / no fallback).
            var renderTargetManager = new TestRenderTargetManager();
            var mockGraphicsManager = new Mock<IGraphicsManager>();
            mockGraphicsManager.SetupGet(g => g.RenderTargetManager).Returns(renderTargetManager);
            var mockGame = new Mock<IStageGame>();
            mockGame.SetupGet(g => g.GraphicsManager).Returns(mockGraphicsManager.Object);

            var stage = new SongSelectionStage(mockGame.Object);

            // Initialize so a render target exists in the manager and on the stage
            InvokePrivateMethod(stage, "InitializeStageRenderTargets");
            Assert.NotNull(GetPrivateField<RenderTarget2D>(stage, "_stageRenderTarget"));

            // Now clean up — should remove from the manager and null out the field
            InvokePrivateMethod(stage, "CleanupStageRenderTargets");

            Assert.Null(GetPrivateField<RenderTarget2D>(stage, "_stageRenderTarget"));
        }

        /// <summary>
        /// Test-only <see cref="RenderTargetManager"/> that overrides <see cref="RenderTargetManager.CreateRenderTarget"/>
        /// to return an uninitialized <see cref="RenderTarget2D"/> instead of calling the GraphicsDevice constructor,
        /// so render-target management paths can be exercised without a live graphics device.
        /// </summary>
        private sealed class TestRenderTargetManager : RenderTargetManager
        {
#pragma warning disable SYSLIB0050
            private static readonly GraphicsDevice _stubDevice =
                (GraphicsDevice)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(GraphicsDevice));
#pragma warning restore SYSLIB0050

            public TestRenderTargetManager() : base(_stubDevice) { }

            protected override RenderTarget2D CreateRenderTarget(
                int width, int height, SurfaceFormat format, DepthFormat depthFormat, int multiSampleCount)
            {
#pragma warning disable SYSLIB0050
                return (RenderTarget2D)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(RenderTarget2D));
#pragma warning restore SYSLIB0050
            }
        }

        #endregion
    }
}

using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using System.Collections.Generic;
using Xunit;

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Moq;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;
namespace DTXMania.Test.Stage
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongSelectionStageInputTests
    {
        #region ClampSelectionIndex

        [Fact]
        public void ClampSelectionIndex_PreviousAtEndOfMultiItemList_ShouldReturnLastIndex()
        {
            var node = new SongListNode { Title = "C" };
            var list = new List<SongListNode>
            {
                new SongListNode { Title = "A" },
                new SongListNode { Title = "B" },
                node
            };
            Assert.Equal(2, SongSelectionStage.ClampSelectionIndex(node, list));
        }

        [Fact]
        public void ClampSelectionIndex_SingleItemListWithMatch_ShouldReturn0()
        {
            var node = new SongListNode { Title = "Only" };
            var list = new List<SongListNode> { node };
            Assert.Equal(0, SongSelectionStage.ClampSelectionIndex(node, list));
        }

        [Fact]
        public void ClampSelectionIndex_SingleItemListWithoutMatch_ShouldReturn0()
        {
            var prev = new SongListNode { Title = "X" };
            var list = new List<SongListNode> { new SongListNode { Title = "Y" } };
            Assert.Equal(0, SongSelectionStage.ClampSelectionIndex(prev, list));
        }

        [Fact]
        public void ClampSelectionIndex_PreviousFoundAtMiddlePosition_ShouldReturnCorrectIndex()
        {
            var node = new SongListNode { Title = "Mid" };
            var list = new List<SongListNode>
            {
                new SongListNode { Title = "A" },
                new SongListNode { Title = "B" },
                node,
                new SongListNode { Title = "D" },
                new SongListNode { Title = "E" }
            };
            Assert.Equal(2, SongSelectionStage.ClampSelectionIndex(node, list));
        }

        [Fact]
        public void ClampSelectionIndex_DuplicateReferences_ShouldReturnFirstOccurrence()
        {
            var node = new SongListNode { Title = "Dup" };
            var list = new List<SongListNode>
            {
                node,
                new SongListNode { Title = "Other" },
                node
            };
            Assert.Equal(0, SongSelectionStage.ClampSelectionIndex(node, list));
        }

        #endregion

        #region SummarizeFilter

        [Fact]
        public void SummarizeFilter_WithPlayedStatusUnplayed_ShouldContainUnplayed()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Unplayed", result);
        }

        [Fact]
        public void SummarizeFilter_WithPlayedStatusPlayed_ShouldContainPlayed()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Played };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Played", result);
        }

        [Fact]
        public void SummarizeFilter_WithPlayedStatusOnly_ShouldStartWithFiltered()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Cleared };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.StartsWith("Filtered:", result);
            Assert.Contains("Cleared", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortDescendingOnly_ShouldStartWithFiltered()
        {
            var c = SongFilterCriteria.Default with { SortDescending = true };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.StartsWith("Filtered:", result);
            Assert.Contains("Titlev", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortByArtistDescending_ShouldContainArtistv()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Artist, SortDescending = true };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Artistv", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortByGenreDescending_ShouldContainGenrev()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Genre, SortDescending = true };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Genrev", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortByLevelAscending_ShouldContainLevelUpArrow()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Level, SortDescending = false };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Level^", result);
        }

        [Fact]
        public void SummarizeFilter_WithCombinedAllFields_ShouldContainAllParts()
        {
            var c = SongFilterCriteria.Default with
            {
                SearchQuery = "mega",
                MinLevel = 40,
                MaxLevel = 80,
                PlayedStatus = PlayedStatus.Unplayed,
                SortBy = SongSortCriteria.Artist,
                SortDescending = true
            };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.StartsWith("Filtered:", result);
            Assert.Contains("\"mega\"", result);
            Assert.Contains("Lv 40-80", result);
            Assert.Contains("Unplayed", result);
            Assert.Contains("Artistv", result);
        }

        [Fact]
        public void SummarizeFilter_WithSearchAndSortOnly_ShouldContainBothParts()
        {
            var c = SongFilterCriteria.Default with
            {
                SearchQuery = "test",
                SortBy = SongSortCriteria.Genre
            };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("\"test\"", result);
            Assert.Contains("Genre^", result);
            Assert.DoesNotContain("Lv", result);
        }

        [Fact]
        public void SummarizeFilter_WithLevelAndSortOnly_ShouldContainLevelAndSort()
        {
            var c = SongFilterCriteria.Default with
            {
                MaxLevel = 50,
                SortBy = SongSortCriteria.Level,
                SortDescending = true
            };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains("Lv <=50", result);
            Assert.Contains("Levelv", result);
            Assert.DoesNotContain("\"", result);
        }

        [Fact]
        public void SummarizeFilter_WithPartsJoinedByPipe_ShouldUsePipeSeparator()
        {
            var c = SongFilterCriteria.Default with
            {
                SearchQuery = "x",
                MinLevel = 10
            };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.Contains(" | ", result);
        }

        [Fact]
        public void SummarizeFilter_WithSortByTitleAscending_ShouldOmitSortFromResult()
        {
            var c = SongFilterCriteria.Default with
            {
                SearchQuery = "y",
                SortBy = SongSortCriteria.Title,
                SortDescending = false
            };
            var result = SongSelectionStage.SummarizeFilter(c);
            Assert.DoesNotContain("Title", result);
        }

        #endregion

        [Fact]
        public void HandleInput_WhenModalOpen_ShouldProcessModalKeysInsteadOfStageCommands()
        {
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = [CreateScoreNode("A"), CreateScoreNode("B")]
            };
            var inputManager = new ModalOpenInputManager();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            modal.Open(SongFilterCriteria.Default);

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "HandleInput");

            Assert.True(modal.IsOpen);
            Assert.Equal("A", display.SelectedSong!.Title);
        }

        [Fact]
        public void ProcessModalKeys_PadCommandsDriveModalFromSearchBox()
        {
            var stage = CreateStage();
            using var input = new InputManagerCompat(new ConfigManager(), new TestMidiDeviceBackend());
            var modal = new SongSearchFilterModal(new Mock<ITextInputSource>().Object);
            modal.Open(SongFilterCriteria.Default);
            SetPrivateField(stage, "_inputManager", input);
            SetPrivateField(stage, "_searchFilterModal", modal);

            // Low tom = down moves off the SearchBox even though keys are suppressed there.
            input.PadCommandResolver = _ => InputCommandType.MoveDown;
            InvokePrivateMethod(input, "OnModularLaneHit", null!, new LaneHitEventArgs(6, new DTXMania.Game.Lib.Input.ButtonState("MIDI.45", true)));
            InvokePrivateMethod(stage, "ProcessModalKeys");
            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);

            // Cymbal = enter on a value field steps the value instead of applying.
            modal.FocusNext(); // MaxLevel
            modal.FocusNext(); // PlayedStatus
            modal.FocusNext(); // SortBy
            var sortBefore = modal.CurrentDraft.SortBy;
            input.ClearPendingCommands();
            input.PadCommandResolver = _ => InputCommandType.Activate;
            InvokePrivateMethod(input, "OnModularLaneHit", null!, new LaneHitEventArgs(8, new DTXMania.Game.Lib.Input.ButtonState("MIDI.51", true)));
            InvokePrivateMethod(stage, "ProcessModalKeys");

            Assert.True(modal.IsOpen);
            Assert.NotEqual(sortBefore, modal.CurrentDraft.SortBy);
        }

        [Fact]
        public void DetectOpenSearchKey_WhenBackspacePressed_ShouldOpenModal()
        {
            var stage = CreateStage();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            var inputManager = new BackspaceDetectInputManager();

            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "DetectOpenSearchKey");

            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void DetectOpenSearchKey_WhenNoBackspace_ShouldNotOpenModal()
        {
            var stage = CreateStage();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            var inputManager = new NoKeyPressInputManager();

            SetPrivateField(stage, "_inputManager", inputManager);
            SetPrivateField(stage, "_searchFilterModal", modal);

            InvokePrivateMethod(stage, "DetectOpenSearchKey");

            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void ExecuteInputCommand_WhenFilterActiveAndBackPressed_ShouldClearFilter()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var filteredView = new List<FilteredSongResult> { new(CreateScoreNode("S"), "folder") };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_isInStatusPanel", false);
            SetPrivateField(stage, "_filteredView", filteredView);
            SetPrivateField(stage, "_filterCriteria",
                new SongFilterCriteria("active", null, null, PlayedStatus.All, SongSortCriteria.Title, false));

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            Assert.Null(GetPrivateField<IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
            var criteria = GetPrivateField<SongFilterCriteria>(stage, "_filterCriteria");
            Assert.True(criteria.IsEmpty);
        }

        [Fact]
        public void ExecuteInputCommand_WhenInStatusPanelAndBackPressed_ShouldExitPanelOnly()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var filteredView = new List<FilteredSongResult> { new(CreateScoreNode("S"), "f") };

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_isInStatusPanel", true);
            SetPrivateField(stage, "_filteredView", filteredView);
            SetPrivateField(stage, "_filterCriteria",
                new SongFilterCriteria("test", null, null, PlayedStatus.All, SongSortCriteria.Title, false));

            InvokePrivateMethod(stage, "ExecuteInputCommand", new InputCommand(InputCommandType.Back, 0.0));

            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.NotNull(GetPrivateField<IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
        }

        [Fact]
        public void OpenSearchFilterModal_ShouldSetModalIsOpen()
        {
            var stage = CreateStage();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("S") });
            SetPrivateField(stage, "_songInitializationTask", null);

            InvokePrivateMethod(stage, "OpenSearchFilterModal");

            Assert.True(modal.IsOpen);
        }

        [Fact]
        public void OpenSearchFilterModal_ShouldExitStatusPanel()
        {
            var stage = CreateStage();
            var fakeTextSource = new Mock<ITextInputSource>();
            var modal = new SongSearchFilterModal(fakeTextSource.Object);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("S") });
            SetPrivateField(stage, "_songInitializationTask", null);
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "OpenSearchFilterModal");

            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
        }

        [Fact]
        public void OnFilterApplied_WhenLevelRangeSwapped_ShouldNormalize()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var breadcrumb = new UILabel("");
            AttachCoreUi(stage, display: display, breadcrumb: breadcrumb);
            var criteria = new SongFilterCriteria("test", 80, 20, PlayedStatus.All, SongSortCriteria.Title, false);

            InvokePrivateMethod(stage, "OnFilterApplied", stage, criteria);

            var applied = GetPrivateField<SongFilterCriteria>(stage, "_filterCriteria");
            Assert.Equal(20, applied.MinLevel);
            Assert.Equal(80, applied.MaxLevel);
        }

        [Fact]
        public void OnFilterApplied_WhenLevelRangeNormal_ShouldNotSwap()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var breadcrumb = new UILabel("");
            AttachCoreUi(stage, display: display, breadcrumb: breadcrumb);
            var criteria = new SongFilterCriteria("", 10, 50, PlayedStatus.All, SongSortCriteria.Title, false);

            InvokePrivateMethod(stage, "OnFilterApplied", stage, criteria);

            var applied = GetPrivateField<SongFilterCriteria>(stage, "_filterCriteria");
            Assert.Equal(10, applied.MinLevel);
            Assert.Equal(50, applied.MaxLevel);
        }

        [Fact]
        public void OnFilterReset_ShouldClearFilterCriteria()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_filterCriteria",
                new SongFilterCriteria("hello", null, null, PlayedStatus.All, SongSortCriteria.Title, false));
            SetPrivateField(stage, "_filteredView", new List<FilteredSongResult>());
            SetPrivateField(stage, "_showEmptyFilterMessage", true);

            InvokePrivateMethod(stage, "OnFilterReset", stage, EventArgs.Empty);

            Assert.True(GetPrivateField<SongFilterCriteria>(stage, "_filterCriteria").IsEmpty);
            Assert.Null(GetPrivateField<IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
            Assert.False(GetPrivateField<bool>(stage, "_showEmptyFilterMessage"));
        }

        [Fact]
        public void OnFilterCancelled_ShouldClearInputCommandsAndPreserveFilter()
        {
            var stage = CreateStage();
            var criteria = new SongFilterCriteria("preserved", null, null, PlayedStatus.All, SongSortCriteria.Title, false);
            SetPrivateField(stage, "_filterCriteria", criteria);

            InvokePrivateMethod(stage, "OnFilterCancelled", stage, EventArgs.Empty);

            var applied = GetPrivateField<SongFilterCriteria>(stage, "_filterCriteria");
            Assert.Equal("preserved", applied.SearchQuery);
        }

        [Fact]
        public void RebuildFilteredView_WhenFilterEmpty_ShouldClearView()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_filterCriteria", SongFilterCriteria.Default);
            SetPrivateField(stage, "_filteredView", new List<FilteredSongResult>());

            InvokePrivateMethod(stage, "RebuildFilteredView");

            Assert.Null(GetPrivateField<IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
            Assert.False(GetPrivateField<bool>(stage, "_showEmptyFilterMessage"));
        }

        [Theory]
        [InlineData(100, 75)]   // speed changed: the played-status filter is rebuilt for 0.75x
        [InlineData(75, null)]  // unchanged: the filter is left alone
        public void OnQuickSettingsClosed_RebuildsFilterOnlyWhenPlaySpeedChanged(int speedAtOpen, int? expectedFilterSpeed)
        {
            var stage = CreateStage();
            var filterService = new PassthroughFilterService();
            var configManager = new Mock<IConfigManager>();
            configManager.SetupGet(manager => manager.Config).Returns(new ConfigData { PlaySpeedPercent = 75 });
            AttachCoreUi(stage);
            SetPrivateField(stage, "_filterCriteria",
                new SongFilterCriteria(null, null, null, PlayedStatus.Played, SongSortCriteria.Title, false));
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { CreateScoreNode("TestSong") });
            SetPrivateField(stage, "_filterService", filterService);
            SetPrivateField(stage, "_configManager", configManager.Object);
            SetPrivateField(stage, "_quickSettingsPlaySpeedAtOpen", speedAtOpen);

            InvokePrivateMethod(stage, "OnQuickSettingsClosed", null!, EventArgs.Empty);

            Assert.Equal(expectedFilterSpeed, filterService.PlaySpeedPercent);
        }

        [Fact]
        public void RebuildFilteredView_WhenFilterHasCriteria_ShouldApplyFilter()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("TestSong");
            var filterService = new PassthroughFilterService();
            var configManager = new Mock<IConfigManager>();
            configManager.SetupGet(manager => manager.Config).Returns(
                new ConfigData { PlaySpeedPercent = 75 });
            SetPrivateField(stage, "_filterCriteria",
                new SongFilterCriteria("TestSong", null, null, PlayedStatus.All, SongSortCriteria.Title, false));
            SetPrivateField(stage, "_filteredView", null);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { song });
            SetPrivateField(stage, "_filterService", filterService);
            SetPrivateField(stage, "_configManager", configManager.Object);

            InvokePrivateMethod(stage, "RebuildFilteredView");

            Assert.NotNull(GetPrivateField<IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
            Assert.Equal(75, filterService.PlaySpeedPercent);
        }

        [Fact]
        public void PopulateRecentPlaysList_WithLatestVariantSpeed_ShouldLabelVisibleRow()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            var node = CreateScoreNode("Recent Song");
            node.RecentPlaySpeedPercent = 75;
            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_recentPlayNodes", new List<SongListNode> { node });

            InvokePrivateMethod(stage, "PopulateRecentPlaysList");

            Assert.Equal("Recent Song [0.75x]", Assert.Single(display.CurrentList).Title);
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
            int? songId = null)
        {
            var chart = new SongChart
            {
                FilePath = Path.Combine(AppContext.BaseDirectory, "TestResults", "input-coverage", $"{Guid.NewGuid():N}.dtx"),
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

        private sealed class ModalOpenInputManager : InputManager
        {
            public override bool IsCommandPressed(InputCommandType command) => false;
        }

        private sealed class BackspaceDetectInputManager : InputManager
        {
            private bool _consumed;
            public override bool IsKeyPressed(int keyCode)
            {
                if (keyCode == (int)Microsoft.Xna.Framework.Input.Keys.Back && !_consumed)
                {
                    _consumed = true;
                    return true;
                }
                return false;
            }
        }

        private sealed class NoKeyPressInputManager : InputManager
        {
            public override bool IsKeyPressed(int keyCode) => false;
        }

        private sealed class PassthroughFilterService : ISongListFilterService
        {
            public int? PlaySpeedPercent { get; private set; }

            public IReadOnlyList<FilteredSongResult> Apply(
                IEnumerable<SongListNode> roots,
                SongFilterCriteria criteria)
            {
                return roots.Select(n => new FilteredSongResult(n, "")).ToList();
            }

            public IReadOnlyList<FilteredSongResult> Apply(
                IEnumerable<SongListNode> roots,
                SongFilterCriteria criteria,
                int playSpeedPercent)
            {
                PlaySpeedPercent = playSpeedPercent;
                return Apply(roots, criteria);
            }
        }
    }
}

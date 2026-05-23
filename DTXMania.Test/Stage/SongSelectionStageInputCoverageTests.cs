using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.Serialization;
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
using Microsoft.Xna.Framework;
using Moq;
using static DTXMania.Test.TestData.ReflectionHelpers;

using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
using SongScore = DTXMania.Game.Lib.Song.Entities.SongScore;

namespace DTXMania.Test.Stage
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongSelectionStageInputCoverageTests
    {
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

        [Fact]
        public void RebuildFilteredView_WhenFilterHasCriteria_ShouldApplyFilter()
        {
            var stage = CreateStage();
            var song = CreateScoreNode("TestSong");
            SetPrivateField(stage, "_filterCriteria",
                new SongFilterCriteria("TestSong", null, null, PlayedStatus.All, SongSortCriteria.Title, false));
            SetPrivateField(stage, "_filteredView", null);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { song });
            SetPrivateField(stage, "_filterService", new PassthroughFilterService());

            InvokePrivateMethod(stage, "RebuildFilteredView");

            Assert.NotNull(GetPrivateField<IReadOnlyList<FilteredSongResult>?>(stage, "_filteredView"));
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
            public IReadOnlyList<FilteredSongResult> Apply(
                IEnumerable<SongListNode> roots,
                SongFilterCriteria criteria)
            {
                return roots.Select(n => new FilteredSongResult(n, "")).ToList();
            }
        }
    }
}

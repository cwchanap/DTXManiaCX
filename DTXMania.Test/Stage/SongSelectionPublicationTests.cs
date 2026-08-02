using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using Moq;
using Xunit;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage
{
    [Collection("SongManager")]
    [Trait("Category", "Unit")]
    public class SongSelectionPublicationTests
    {
        [Fact]
        public void ReconcileLibrarySnapshot_WhenBoxAndChartRemain_ShouldRestoreNavigationSelectionAndPreviewRoots()
        {
            var rootPath = "/library/one";
            var boxPath = "/library/one/BOX";
            var oldSong = Score("Old", 42, "/library/one/BOX/old.dtx");
            var oldBox = Box("BOX", boxPath, oldSong);
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode>
                {
                    new() { Type = NodeType.BackBox, Title = ".." },
                    oldSong
                }
            };
            var preview = new PreviewImagePanel();
            AttachCoreUi(stage, display: display, previewPanel: preview);
            display.SelectedIndex = 1;
            SetPrivateField(stage, "_currentSongList", oldBox.Children);
            SetPrivateField(stage, "_currentBreadcrumb", "BOX");
            SetPrivateField(stage, "_selectedSong", oldSong);
            GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(
                new SongListNode { Children = new List<SongListNode> { oldBox }, Title = "" });

            var retainedSong = Score("Retained", 42, "/library/one/BOX/retained.dtx");
            var retainedBox = Box("BOX", boxPath, retainedSong);
            var snapshot = Snapshot(11, new[] { retainedBox }, new[] { rootPath });

            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", snapshot);

            Assert.Same(retainedBox.Children, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Single(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
            Assert.Equal("BOX", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Equal(NodeType.BackBox, display.CurrentList[0].Type);
            Assert.Same(retainedSong, display.SelectedSong);
            Assert.Equal(new[] { rootPath }, preview.ActiveSongRootPaths);
        }

        [Fact]
        public void InitializeSongList_WhenReentrySnapshotRemovesCurrentBox_ShouldClearStaleNavigation()
        {
            var songManager = SongManager.Instance;
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var originalInitialized = GetPrivateField<bool>(songManager, "_isInitialized");
            var oldSong = Score("Old", 41, "/library/old/BOX/old.dtx");
            var oldBox = Box("BOX", "/library/old/BOX", oldSong);
            var replacement = Score("Replacement", 42, "/library/new/replacement.dtx");
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode>
                {
                    new() { Type = NodeType.BackBox, Title = ".." },
                    oldSong
                }
            };

            try
            {
                AttachCoreUi(stage, display: display);
                SetPrivateField(stage, "_currentSongList", oldBox.Children);
                SetPrivateField(stage, "_currentBreadcrumb", "BOX");
                SetPrivateField(stage, "_selectedSong", oldSong);
                GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(
                    new SongListNode { Children = new List<SongListNode> { oldBox }, Title = "" });
                rootSongs.Clear();
                rootSongs.Add(replacement);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/new" });
                SetPrivateField(songManager, "_publicationVersion", 61L);
                SetPrivateField(songManager, "_isInitialized", true);

                InvokePrivateMethod(stage, "InitializeSongList");

                Assert.Empty(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
                Assert.Equal("", GetPrivateField<string>(stage, "_currentBreadcrumb"));
                Assert.Equal(new[] { replacement }, display.CurrentList);
            }
            finally
            {
                rootSongs.Clear();
                rootSongs.AddRange(originalRoots);
                SetPrivateField(songManager, "_currentSearchPaths", originalActiveRoots);
                SetPrivateField(songManager, "_publicationVersion", originalVersion);
                SetPrivateField(songManager, "_isInitialized", originalInitialized);
            }
        }

        [Fact]
        public void ReconcileLibrarySnapshot_WhenSelectedBoxIsRetainedByDirectoryPath_ShouldSelectTheNewBox()
        {
            var firstOldBox = Box("First", "/library/one/FIRST");
            var selectedOldBox = Box("Selected", "/library/one/SELECTED");
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { firstOldBox, selectedOldBox }
            };
            AttachCoreUi(stage, display: display);
            display.SelectedIndex = 1;
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { firstOldBox, selectedOldBox });
            SetPrivateField(stage, "_selectedSong", selectedOldBox);

            var firstReplacementBox = Box("First replacement", "/library/one/FIRST");
            var selectedReplacementBox = Box("Selected replacement", "/library/one/SELECTED");
            var snapshot = Snapshot(
                111,
                new[] { firstReplacementBox, selectedReplacementBox },
                new[] { "/library/one" });

            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", snapshot);

            Assert.Same(selectedReplacementBox, display.SelectedSong);
        }

        [Fact]
        public void ReconcileLibrarySnapshot_WhenCurrentBoxIsRemoved_ShouldResetNavigationAtRootAndClearOldPresentation()
        {
            var oldSong = Score("Old", 42, "/library/one/BOX/old.dtx");
            var oldBox = Box("BOX", "/library/one/BOX", oldSong);
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode>
                {
                    new() { Type = NodeType.BackBox, Title = ".." },
                    oldSong
                }
            };
            var preview = new PreviewImagePanel();
            var oldTexture = new Mock<ITexture>();
            AttachCoreUi(stage, display: display, previewPanel: preview);
            SetPrivateField(preview, "_currentSong", oldSong);
            SetPrivateField(preview, "_currentPreviewTexture", oldTexture.Object);
            SetPrivateField(stage, "_currentSongList", oldBox.Children);
            SetPrivateField(stage, "_selectedSong", oldSong);
            GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(
                new SongListNode { Children = new List<SongListNode> { oldBox }, Title = "" });

            var replacement = Score("Replacement", 77, "/library/one/replacement.dtx");
            var snapshot = Snapshot(12, new[] { replacement }, new[] { "/library/one" });

            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", snapshot);

            Assert.Empty(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
            Assert.Single(GetPrivateField<List<SongListNode>>(stage, "_currentSongList")!);
            Assert.Same(replacement, display.CurrentList.Single());
            Assert.Null(GetPrivateField<SongListNode>(preview, "_currentSong"));
            oldTexture.Verify(texture => texture.RemoveReference(), Times.Once);
        }

        [Fact]
        public void ReconcileLibrarySnapshot_WhenSelectedChartIsRetainedByDatabaseIdentity_ShouldSelectNewNode()
        {
            var oldSong = Score("Old title", 42, "/library/one/old.dtx");
            var stage = CreateStage();
            var display = new SongListDisplay { CurrentList = new List<SongListNode> { oldSong } };
            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { oldSong });
            SetPrivateField(stage, "_selectedSong", oldSong);

            var replacement = Score("Renamed", 42, "/library/one/renamed.dtx");
            var snapshot = Snapshot(13, new[] { replacement }, new[] { "/library/one" });

            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", snapshot);

            Assert.Same(replacement, display.SelectedSong);
        }

        [Fact]
        public void ReconcileLibrarySnapshot_WhenSelectedChartIsRemoved_ShouldResetSelectionAndClearPreview()
        {
            var oldSong = Score("Old", 42, "/library/one/old.dtx");
            var stage = CreateStage();
            var display = new SongListDisplay { CurrentList = new List<SongListNode> { oldSong } };
            var preview = new PreviewImagePanel();
            var oldTexture = new Mock<ITexture>();
            AttachCoreUi(stage, display: display, previewPanel: preview);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { oldSong });
            SetPrivateField(stage, "_selectedSong", oldSong);
            SetPrivateField(preview, "_currentSong", oldSong);
            SetPrivateField(preview, "_currentPreviewTexture", oldTexture.Object);

            var replacement = Score("Replacement", 77, "/library/one/replacement.dtx");
            var snapshot = Snapshot(14, new[] { replacement }, new[] { "/library/one" });

            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", snapshot);

            Assert.Same(replacement, display.SelectedSong);
            Assert.Null(GetPrivateField<SongListNode>(preview, "_currentSong"));
            oldTexture.Verify(texture => texture.RemoveReference(), Times.Once);
        }

        [Fact]
        public void PopulateBookmarksList_WhenCachedRowsAreOutsideActiveRoots_ShouldHideThemWithoutDiscardingCache()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display: display);
            var active = Score("Active", 1, "/library/active/active.dtx");
            var inactive = Score("Inactive", 2, "/library/inactive/inactive.dtx");
            var cached = new List<SongListNode> { active, inactive };
            InvokePrivateMethod(stage, "ApplyLibrarySnapshot",
                Snapshot(15, new[] { active }, new[] { "/library/active" }));
            SetPrivateField(stage, "_bookmarkNodes", cached);

            InvokePrivateMethod(stage, "PopulateBookmarksList");

            Assert.Equal(new[] { active }, display.CurrentList);
            Assert.Equal(2, cached.Count);
        }

        [Fact]
        public void PopulateBookmarksList_WhenRootIsReadded_ShouldMakeTheRetainedCachedRowVisibleAgain()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display: display);
            var active = Score("Active", 1, "/library/active/active.dtx");
            var readded = Score("Readded", 2, "/library/readded/readded.dtx");
            var cached = new List<SongListNode> { active, readded };
            SetPrivateField(stage, "_bookmarkNodes", cached);

            InvokePrivateMethod(stage, "ApplyLibrarySnapshot",
                Snapshot(151, new[] { active }, new[] { "/library/active" }));
            InvokePrivateMethod(stage, "PopulateBookmarksList");
            Assert.Equal(new[] { active }, display.CurrentList);

            InvokePrivateMethod(stage, "ApplyLibrarySnapshot",
                Snapshot(152, new[] { active, readded }, new[] { "/library/active", "/library/readded" }));
            InvokePrivateMethod(stage, "PopulateBookmarksList");

            Assert.Equal(new[] { active, readded }, display.CurrentList);
            Assert.Equal(2, cached.Count);
        }

        [Fact]
        public void PopulateRecentPlaysList_WhenCachedRowsAreOutsideActiveRoots_ShouldHideThemWithoutDiscardingCache()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display: display);
            var active = Score("Active", 1, "/library/active/active.dtx");
            var inactive = Score("Inactive", 2, "/library/inactive/inactive.dtx");
            var cached = new List<SongListNode> { active, inactive };
            InvokePrivateMethod(stage, "ApplyLibrarySnapshot",
                Snapshot(16, new[] { active }, new[] { "/library/active" }));
            SetPrivateField(stage, "_recentPlayNodes", cached);

            InvokePrivateMethod(stage, "PopulateRecentPlaysList");

            Assert.Equal(new[] { active }, display.CurrentList);
            Assert.Equal(2, cached.Count);
        }

        [Fact]
        public void OnSongLibraryPublished_WhenVersionsArriveOutOfOrder_ShouldCaptureOnlyTheHighestPendingVersion()
        {
            var stage = CreateStage();
            var display = new SongListDisplay { CurrentList = new List<SongListNode> { Score("Existing", 1, "/library/existing.dtx") } };
            AttachCoreUi(stage, display: display);
            var before = display.CurrentList;
            SetPrivateField(stage, "_libraryPublicationActive", 1);
            SetPrivateField(stage, "_pendingLibraryPublicationVersion", 0L);

            InvokePrivateMethod(stage, "OnSongLibraryPublished", null!, new SongLibraryPublishedEventArgs(Snapshot(20, Array.Empty<SongListNode>(), Array.Empty<string>())));
            InvokePrivateMethod(stage, "OnSongLibraryPublished", null!, new SongLibraryPublishedEventArgs(Snapshot(22, Array.Empty<SongListNode>(), Array.Empty<string>())));
            InvokePrivateMethod(stage, "OnSongLibraryPublished", null!, new SongLibraryPublishedEventArgs(Snapshot(21, Array.Empty<SongListNode>(), Array.Empty<string>())));

            Assert.Equal(22L, GetPrivateField<long>(stage, "_pendingLibraryPublicationVersion"));
            Assert.Same(before, display.CurrentList);
        }

        [Fact]
        public void SongLibraryPublished_WhenPriorActivationHandlerRuns_ShouldIgnoreIt()
        {
            var songManager = SongManager.Instance;
            var originalHandler = GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                songManager,
                "SongLibraryPublished");
            var stage = CreateStage();

            try
            {
                SetPrivateField(songManager, "SongLibraryPublished", null);
                SetPrivateField(stage, "_activationVersion", 41);
                InvokePrivateMethod(stage, "SubscribeLibraryPublications");
                var priorActivationHandler = GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                    songManager,
                    "SongLibraryPublished");
                Assert.NotNull(priorActivationHandler);

                SetPrivateField(stage, "_activationVersion", 42);
                InvokePrivateMethod(stage, "SubscribeLibraryPublications");
                SetPrivateField(stage, "_pendingLibraryPublicationVersion", 0L);

                priorActivationHandler!(songManager,
                    new SongLibraryPublishedEventArgs(
                        Snapshot(73, Array.Empty<SongListNode>(), Array.Empty<string>())));

                Assert.Equal(0L, GetPrivateField<long>(stage, "_pendingLibraryPublicationVersion"));
            }
            finally
            {
                InvokePrivateMethod(stage, "UnsubscribeLibraryPublications");
                SetPrivateField(songManager, "SongLibraryPublished", originalHandler);
            }
        }

        [Fact]
        public void ApplyPendingLibraryPublication_ShouldFetchAndApplyTheCurrentManagerSnapshotOnTheUpdateThread()
        {
            var songManager = SongManager.Instance;
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var newestNode = Score("Newest", 88, "/library/newest/newest.dtx");
            var stage = CreateStage();
            var display = new SongListDisplay();

            try
            {
                AttachCoreUi(stage, display: display);
                rootSongs.Clear();
                rootSongs.Add(newestNode);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/newest" });
                SetPrivateField(songManager, "_publicationVersion", 31L);
                SetPrivateField(stage, "_libraryPublicationActive", 1);
                SetPrivateField(stage, "_pendingLibraryPublicationVersion", 31L);

                InvokePrivateMethod(stage, "ApplyPendingLibraryPublication");

                Assert.Equal(31L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
                Assert.Same(newestNode, display.CurrentList.Single());
                Assert.Equal(new[] { "/library/newest" },
                    GetPrivateField<SongLibrarySnapshot>(stage, "_appliedLibrarySnapshot")!.ActiveRoots);
            }
            finally
            {
                rootSongs.Clear();
                rootSongs.AddRange(originalRoots);
                SetPrivateField(songManager, "_currentSearchPaths", originalActiveRoots);
                SetPrivateField(songManager, "_publicationVersion", originalVersion);
            }
        }

        [Fact]
        public void CompleteTabListRefreshConsumption_WhenAsyncCacheCompletesDuringReconciliation_ShouldKeepRefreshPending()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_tabListNeedsRefresh", true);
            SetPrivateField(stage, "_asyncTabListRefreshVersion", 4);

            var consumedVersion = InvokePrivateMethod<int>(stage, "BeginTabListRefreshConsumption");
            InvokePrivateMethod(stage, "RequestAsyncTabListRefresh");
            InvokePrivateMethod(stage, "CompleteTabListRefreshConsumption", consumedVersion);

            Assert.True(GetPrivateField<bool>(stage, "_tabListNeedsRefresh"));
        }

        [Fact]
        public void PopulateBookmarksList_WhenCachedChartIsRemovedFromPublishedSnapshot_ShouldHideButRetainTheCacheRow()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display: display);
            var retained = Score("Retained", 1, "/library/active/retained.dtx");
            var removed = Score("Removed", 2, "/library/active/removed.dtx");
            var cached = new List<SongListNode> { retained, removed };
            InvokePrivateMethod(stage, "ApplyLibrarySnapshot",
                Snapshot(32, new[] { retained }, new[] { "/library/active" }));
            SetPrivateField(stage, "_bookmarkNodes", cached);

            InvokePrivateMethod(stage, "PopulateBookmarksList");

            Assert.Equal(new[] { retained }, display.CurrentList);
            Assert.Equal(2, cached.Count);
        }

        [Fact]
        public void CheckSongInitializationCompletion_WhenNewerPublicationIsAlreadyApplied_ShouldNotResetRestoredSelection()
        {
            var stage = CreateStage();
            var first = Score("First", 1, "/library/new/first.dtx");
            var selected = Score("Selected", 2, "/library/new/selected.dtx");
            var oldNode = Score("Old", 3, "/library/old/old.dtx");
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { first, selected }
            };
            AttachCoreUi(stage, display: display);
            display.SelectedIndex = 1;

            var appliedSnapshot = Snapshot(51, new[] { first, selected }, new[] { "/library/new" });
            var staleSnapshot = Snapshot(50, new[] { oldNode }, new[] { "/library/old" });
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { first, selected });
            SetPrivateField(stage, "_selectedSong", selected);
            SetPrivateField(stage, "_appliedLibrarySnapshot", appliedSnapshot);
            SetPrivateField(stage, "_appliedLibraryPublicationVersion", 51L);
            SetPrivateField(stage, "_backgroundLibrarySnapshot", staleSnapshot);
            SetPrivateField(stage, "_songInitializationTask", Task.FromResult(new List<SongListNode> { oldNode }));
            SetPrivateField(stage, "_songInitializationProcessed", false);

            InvokePrivateMethod(stage, "CheckSongInitializationCompletion");

            Assert.Same(selected, display.SelectedSong);
            Assert.Same(appliedSnapshot, GetPrivateField<SongLibrarySnapshot>(stage, "_appliedLibrarySnapshot"));
        }

        [Fact]
        public void CheckSongInitializationCompletion_WhenNewerSnapshotArrives_ShouldReconcileNestedSelection()
        {
            var oldSong = Score("Old", 1, "/library/shared/BOX/old.dtx");
            var oldBox = Box("BOX", "/library/shared/BOX", oldSong);
            var replacementSong = Score("Replacement", 1, "/library/shared/BOX/replacement.dtx");
            var replacementBox = Box("BOX", "/library/shared/BOX", replacementSong);
            var stage = CreateStage();
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode>
                {
                    new() { Type = NodeType.BackBox, Title = ".." },
                    oldSong
                }
            };
            AttachCoreUi(stage, display: display);
            display.SelectedIndex = 1;
            SetPrivateField(stage, "_currentSongList", oldBox.Children);
            SetPrivateField(stage, "_currentBreadcrumb", "BOX");
            SetPrivateField(stage, "_selectedSong", oldSong);
            GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(
                new SongListNode { Children = new List<SongListNode> { oldBox }, Title = "" });

            var appliedSnapshot = Snapshot(81, new[] { oldBox }, new[] { "/library/shared" });
            var newerSnapshot = Snapshot(82, new[] { replacementBox }, new[] { "/library/shared" });
            SetPrivateField(stage, "_appliedLibrarySnapshot", appliedSnapshot);
            SetPrivateField(stage, "_appliedLibraryPublicationVersion", 81L);
            SetPrivateField(stage, "_backgroundLibrarySnapshot", newerSnapshot);
            SetPrivateField(stage, "_songInitializationTask",
                Task.FromResult(new List<SongListNode> { replacementBox }));
            SetPrivateField(stage, "_songInitializationProcessed", false);

            InvokePrivateMethod(stage, "CheckSongInitializationCompletion");

            Assert.Same(replacementBox.Children,
                GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Single(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
            Assert.Equal("BOX", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Same(replacementSong, display.SelectedSong);
            Assert.Equal(82L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
        }

        [Fact]
        public void OnSongLibraryPublished_AfterDeactivation_ShouldIgnoreTheNotification()
        {
            var stage = CreateStage();
            SetPrivateField(stage, "_libraryPublicationActive", 0);
            SetPrivateField(stage, "_pendingLibraryPublicationVersion", 0L);

            InvokePrivateMethod(stage, "OnSongLibraryPublished", null!, new SongLibraryPublishedEventArgs(Snapshot(23, Array.Empty<SongListNode>(), Array.Empty<string>())));

            Assert.Equal(0L, GetPrivateField<long>(stage, "_pendingLibraryPublicationVersion"));
        }

        [Fact]
        public void ResolveLibraryEmptyState_ShouldDistinguishNoRootsFromRootsWithoutSupportedCharts()
        {
            var stage = CreateStage();
            var noRoots = Snapshot(24, Array.Empty<SongListNode>(), Array.Empty<string>());
            var noSupportedCharts = Snapshot(25, Array.Empty<SongListNode>(), new[] { "/library/active" });

            var noRootsResult = InvokePrivateMethod<object>(stage, "ResolveLibraryEmptyState", noRoots);
            var noChartsResult = InvokePrivateMethod<object>(stage, "ResolveLibraryEmptyState", noSupportedCharts);

            Assert.Equal("NoActiveRoots", noRootsResult!.ToString());
            Assert.Equal("NoSupportedCharts", noChartsResult!.ToString());
        }

        [Fact]
        public async Task ReconcileLibrarySnapshot_WhenOldSnapshotIsProjectedDuringNewPublication_ShouldLeaveUiOnNewestVersion()
        {
            var oldNode = Score("Old", 42, "/library/old/old.dtx");
            var oldSnapshot = Snapshot(26, new[] { oldNode }, new[] { "/library/old" });
            var newestNode = Score("Newest", 99, "/library/new/new.dtx");
            var newestSnapshot = Snapshot(27, new[] { newestNode }, new[] { "/library/new" });
            var stage = CreateStage();
            var display = new SongListDisplay { CurrentList = new List<SongListNode> { oldNode } };
            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { oldNode });
            SetPrivateField(stage, "_selectedSong", oldNode);

            var oldSnapshotReader = Task.Run(() =>
            {
                for (var i = 0; i < 100; i++)
                {
                    var projected = InvokePrivateMethod<List<SongListNode>>(
                        stage,
                        "FilterNodesForActiveRoots",
                        oldSnapshot.RootSongs,
                        oldSnapshot.ActiveRoots);
                    BookmarkStateReconciler.Apply(oldSnapshot.RootSongs, 42, isBookmarked: true);
                    Assert.Equal(new[] { oldNode }, projected);
                }
            });

            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", newestSnapshot);
            await oldSnapshotReader;

            Assert.Equal(27L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
            Assert.Equal(new[] { newestNode }, display.CurrentList);
            Assert.DoesNotContain(oldNode, display.CurrentList);
        }

        private static SongListNode Box(string title, string directoryPath, params SongListNode[] children)
        {
            var box = new SongListNode
            {
                Type = NodeType.Box,
                Title = title,
                DirectoryPath = directoryPath,
                Children = children.ToList()
            };
            foreach (var child in box.Children)
                child.Parent = box;
            return box;
        }

        private static SongListNode Score(string title, int songId, string chartPath)
        {
            var chart = new SongChart { FilePath = chartPath };
            var song = new SongEntity { Id = songId, Title = title, Charts = new List<SongChart> { chart } };
            chart.Song = song;
            chart.SongId = songId;
            return new SongListNode
            {
                Type = NodeType.Score,
                Title = title,
                DatabaseSongId = songId,
                DatabaseSong = song,
                DatabaseChart = chart
            };
        }

        private static SongLibrarySnapshot Snapshot(
            long version,
            IReadOnlyList<SongListNode> roots,
            IReadOnlyList<string> activeRoots) =>
            new(version, roots, activeRoots, EnumeratedFileCount: roots.Count, DiscoveredScoreCount: roots.Count);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using Moq;
using Xunit;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;
namespace DTXMania.Test.Stage
{
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
            var stage = CreateStage();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var originalInitialized = GetPrivateField<bool>(songManager, "_isInitialized");
            var oldSong = Score("Old", 41, "/library/old/BOX/old.dtx");
            var oldBox = Box("BOX", "/library/old/BOX", oldSong);
            var replacement = Score("Replacement", 42, "/library/new/replacement.dtx");
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
                Assert.Equal(2, display.CurrentList.Count);
                Assert.Same(replacement, display.CurrentList[0]);
                Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
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
            Assert.Equal(2, display.CurrentList.Count);
            Assert.Same(replacement, display.CurrentList[0]);
            Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
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

        // Raises the library-published event through the stage's REGISTERED handler —
        // the delegate bound to the current activation's reporter — exactly as the
        // publisher thread would.
        private static void PublishThroughRegisteredHandler(
            SongSelectionStage stage, SongLibraryPublishedEventArgs e) =>
            GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                stage, "_libraryPublicationHandler")!.Invoke(null!, e);

        // Read-only probe of the scope's pending terminal queue, so tests can prove no
        // stale publication was ever enqueued (same pattern as StageBackgroundWorkScopeTests).
        private static int PendingQueueCount(StageBackgroundWorkScope scope)
        {
            var queue = typeof(StageBackgroundWorkScope)
                .GetField("_queue", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(scope) as System.Collections.ICollection;
            return queue?.Count ?? 0;
        }

        [Fact]
        public void SongLibraryPublished_WhenVersionsArriveOutOfOrder_ShouldApplyOnlyTheNewestOnDrain()
        {
            var stage = CreateStage();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var newestNode = Score("Newest", 88, "/library/newest/newest.dtx");
            var display = new SongListDisplay { CurrentList = new List<SongListNode> { Score("Existing", 1, "/library/existing.dtx") } };

            try
            {
                AttachCoreUi(stage, display: display);
                rootSongs.Clear();
                rootSongs.Add(newestNode);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/newest" });
                SetPrivateField(songManager, "_publicationVersion", 31L);
                stage.InstallTestBackgroundScope();
                InvokePrivateMethod(stage, "SubscribeLibraryPublications");

                var before = display.CurrentList;

                // Out-of-order publications coalesce into one pending Latest signal on the
                // stage scope; nothing applies from the publisher thread.
                PublishThroughRegisteredHandler(stage, new SongLibraryPublishedEventArgs(Snapshot(20, Array.Empty<SongListNode>(), Array.Empty<string>())));
                PublishThroughRegisteredHandler(stage, new SongLibraryPublishedEventArgs(Snapshot(22, Array.Empty<SongListNode>(), Array.Empty<string>())));
                PublishThroughRegisteredHandler(stage, new SongLibraryPublishedEventArgs(Snapshot(21, Array.Empty<SongListNode>(), Array.Empty<string>())));

                Assert.Same(before, display.CurrentList);
                Assert.Equal(0L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));

                // One drain applies the newest signal: the update thread fetches the
                // newest coherent manager snapshot (version 31), not any stale signal.
                stage.BackgroundWork!.Drain();

                Assert.Equal(31L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
                Assert.Equal(2, display.CurrentList.Count);
                Assert.Same(newestNode, display.CurrentList[0]);
            }
            finally
            {
                InvokePrivateMethod(stage, "UnsubscribeLibraryPublications");
                rootSongs.Clear();
                rootSongs.AddRange(originalRoots);
                SetPrivateField(songManager, "_currentSearchPaths", originalActiveRoots);
                SetPrivateField(songManager, "_publicationVersion", originalVersion);
            }
        }

        [Fact]
        public void SongLibraryPublished_WhenScopeRetires_ShouldDropStaleSignalsAndRearmForNewScope()
        {
            var stage = CreateStage();
            var songManager = LibraryOf(stage);
            var originalHandler = GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                songManager,
                "SongLibraryPublished");

            try
            {
                AttachCoreUi(stage);
                SetPrivateField(songManager, "SongLibraryPublished", null);
                stage.InstallTestBackgroundScope();
                InvokePrivateMethod(stage, "SubscribeLibraryPublications");
                // The publisher may retain the handler it captured for activation A.
                var handlerA = GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                    stage, "_libraryPublicationHandler")!;

                // While active, a coalesced signal applies the snapshot on drain.
                PublishThroughRegisteredHandler(stage, new SongLibraryPublishedEventArgs(Snapshot(73, Array.Empty<SongListNode>(), Array.Empty<string>())));
                stage.BackgroundWork!.Drain();
                Assert.Equal(73L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));

                // Retire the scope (as Deactivate does before teardown) and model a
                // publisher thread that already captured the old handler: its signal
                // drops into the retired scope instead of reaching torn-down UI state.
                SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
                stage.Deactivate();
                handlerA.Invoke(null!, new SongLibraryPublishedEventArgs(Snapshot(74, Array.Empty<SongListNode>(), Array.Empty<string>())));
                Assert.Equal(73L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));

                // A new activation installs a fresh scope and re-arms the subscription:
                // only the new scope receives and applies new signals.
                stage.InstallTestBackgroundScope();
                InvokePrivateMethod(stage, "SubscribeLibraryPublications");
                PublishThroughRegisteredHandler(stage, new SongLibraryPublishedEventArgs(Snapshot(75, Array.Empty<SongListNode>(), Array.Empty<string>())));
                stage.BackgroundWork!.Drain();
                Assert.Equal(75L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));

                // Regression: the retained A-handler fires AFTER re-arming. It must reach
                // only A's retired reporter and drop — never enqueue a stale apply into
                // the new activation's scope.
                handlerA.Invoke(null!, new SongLibraryPublishedEventArgs(Snapshot(74, Array.Empty<SongListNode>(), Array.Empty<string>())));
                Assert.Equal(75L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
                Assert.Equal(0, PendingQueueCount(stage.BackgroundWork!));
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
            var stage = CreateStage();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var newestNode = Score("Newest", 88, "/library/newest/newest.dtx");
            var display = new SongListDisplay();

            try
            {
                AttachCoreUi(stage, display: display);
                rootSongs.Clear();
                rootSongs.Add(newestNode);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/newest" });
                SetPrivateField(songManager, "_publicationVersion", 31L);

                InvokePrivateMethod(stage, "ApplyPendingLibraryPublication", 31L);

                Assert.Equal(31L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
                Assert.Equal(2, display.CurrentList.Count);
                Assert.Same(newestNode, display.CurrentList[0]);
                Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
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
        public void ApplyPendingLibraryPublication_WhenAppliedVersionIsCurrent_ShouldNotReapply()
        {
            var stage = CreateStage();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var oldNode = Score("Old", 3, "/library/old/old.dtx");
            var display = new SongListDisplay { CurrentList = new List<SongListNode> { oldNode } };
            var appliedSnapshot = Snapshot(50, new[] { oldNode }, new[] { "/library/old" });

            try
            {
                AttachCoreUi(stage, display: display);
                rootSongs.Clear();
                rootSongs.Add(oldNode);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/old" });
                SetPrivateField(songManager, "_publicationVersion", 50L);
                SetPrivateField(stage, "_appliedLibrarySnapshot", appliedSnapshot);
                SetPrivateField(stage, "_appliedLibraryPublicationVersion", 50L);
                var displayListBefore = display.CurrentList;

                // A stale (already-applied) signal must not reapply an older tree.
                InvokePrivateMethod(stage, "ApplyPendingLibraryPublication", 50L);

                Assert.Same(appliedSnapshot, GetPrivateField<SongLibrarySnapshot>(stage, "_appliedLibrarySnapshot"));
                Assert.Equal(50L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
                Assert.Same(displayListBefore, display.CurrentList);
                Assert.Single(display.CurrentList);
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
        public void ApplyInitializedLibrarySnapshot_WhenNewerPublicationIsAlreadyApplied_ShouldNotResetRestoredSelection()
        {
            var stage = CreateStage();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var first = Score("First", 1, "/library/new/first.dtx");
            var selected = Score("Selected", 2, "/library/new/selected.dtx");
            var oldNode = Score("Old", 3, "/library/old/old.dtx");
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { first, selected }
            };

            try
            {
                AttachCoreUi(stage: stage, display: display);
                rootSongs.Clear();
                rootSongs.Add(oldNode);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/old" });
                SetPrivateField(songManager, "_publicationVersion", 50L);
                display.SelectedIndex = 1;

                var appliedSnapshot = Snapshot(51, new[] { first, selected }, new[] { "/library/new" });
                SetPrivateField(stage, "_currentSongList", new List<SongListNode> { first, selected });
                SetPrivateField(stage, "_selectedSong", selected);
                SetPrivateField(stage, "_appliedLibrarySnapshot", appliedSnapshot);
                SetPrivateField(stage, "_appliedLibraryPublicationVersion", 51L);

                // The initializer completion fetches on the update thread: the manager's
                // stale snapshot (v50) must not overwrite the newer publication (v51) that
                // already restored the user's navigation and selection.
                InvokePrivateMethod(stage, "ApplyInitializedLibrarySnapshot");

                Assert.Same(selected, display.SelectedSong);
                Assert.Same(appliedSnapshot, GetPrivateField<SongLibrarySnapshot>(stage, "_appliedLibrarySnapshot"));
                Assert.True(GetPrivateField<bool>(stage, "_libraryInitializationCompleted"));
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
        public void ApplyInitializedLibrarySnapshot_WhenNewerSnapshotArrives_ShouldReconcileNestedSelection()
        {
            var stage = CreateStage();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var oldSong = Score("Old", 1, "/library/shared/BOX/old.dtx");
            var oldBox = Box("BOX", "/library/shared/BOX", oldSong);
            var replacementSong = Score("Replacement", 1, "/library/shared/BOX/replacement.dtx");
            var replacementBox = Box("BOX", "/library/shared/BOX", replacementSong);
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
                rootSongs.Clear();
                rootSongs.Add(replacementBox);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/shared" });
                SetPrivateField(songManager, "_publicationVersion", 82L);
                display.SelectedIndex = 1;
                SetPrivateField(stage, "_currentSongList", oldBox.Children);
                SetPrivateField(stage, "_currentBreadcrumb", "BOX");
                SetPrivateField(stage, "_selectedSong", oldSong);
                GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(
                    new SongListNode { Children = new List<SongListNode> { oldBox }, Title = "" });

                var appliedSnapshot = Snapshot(81, new[] { oldBox }, new[] { "/library/shared" });
                SetPrivateField(stage, "_appliedLibrarySnapshot", appliedSnapshot);
                SetPrivateField(stage, "_appliedLibraryPublicationVersion", 81L);

                InvokePrivateMethod(stage, "ApplyInitializedLibrarySnapshot");

                // The newer snapshot (v82) reconciles the nested selection against the
                // replacement box from the same coherent tree.
                Assert.Same(replacementBox.Children,
                    GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
                Assert.Single(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
                Assert.Equal("BOX", GetPrivateField<string>(stage, "_currentBreadcrumb"));
                Assert.Same(replacementSong, display.SelectedSong);
                Assert.Equal(82L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
                Assert.True(GetPrivateField<bool>(stage, "_libraryInitializationCompleted"));
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
        public void ApplyInitializedLibrarySnapshot_ShouldFetchTheNewestSnapshotOnTheUpdateThread()
        {
            var stage = CreateStage();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var newNode = Score("New", 92, "/library/new/new.dtx");
            var display = new SongListDisplay();

            try
            {
                AttachCoreUi(stage, display: display);
                rootSongs.Clear();
                rootSongs.Add(newNode);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/new" });
                SetPrivateField(songManager, "_publicationVersion", 92L);

                // The initializer completion never carries a copied snapshot: it fetches
                // the newest coherent manager snapshot on the update thread, so an old
                // initializer read can never overwrite a newer library.
                InvokePrivateMethod(stage, "ApplyInitializedLibrarySnapshot");

                Assert.Equal(92L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
                Assert.Equal(2, display.CurrentList.Count);
                Assert.Same(newNode, display.CurrentList[0]);
                Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
                Assert.True(GetPrivateField<bool>(stage, "_libraryInitializationCompleted"));
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
        public void ApplyLibraryInitializationFailure_ShouldFallBackToEmptyListAndMarkCompleted()
        {
            var stage = CreateStage();
            var display = new SongListDisplay { CurrentList = new List<SongListNode> { Score("Existing", 1, "/library/existing.dtx") } };
            var modal = new SongSearchFilterModal(new Mock<ITextInputSource>().Object) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);

            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_searchFilterModal", modal);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { Score("Existing", 1, "/library/existing.dtx") });

            // The initializer's failure fallback (fetch/apply faulted on the update thread):
            // with no snapshot applied yet it shows an empty placeholder list and marks the
            // initialization completed so an open modal stops waiting.
            InvokePrivateMethod(stage, "ApplyLibraryInitializationFailure");

            Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_currentSongList")!);
            Assert.True(GetPrivateField<bool>(stage, "_libraryInitializationCompleted"));
            Assert.True(modal.IsLibraryReady);
        }

        [Fact]
        public void Deactivate_WhenInitializerQueuedBeforeRetire_ShouldDropTheResult()
        {
            var stage = CreateStage();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var originalInitialized = GetPrivateField<bool>(songManager, "_isInitialized");
            var deferredNode = Score("Deferred", 311, "/library/deferred/deferred.dtx");
            var display = new SongListDisplay();

            try
            {
                AttachCoreUi(stage, display: display);
                rootSongs.Clear();
                rootSongs.Add(deferredNode);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/deferred" });
                SetPrivateField(songManager, "_publicationVersion", 311L);
                SetPrivateField(songManager, "_isInitialized", false);
                stage.InstallTestBackgroundScope();

                // The async initializer queues its apply into the activation scope.
                InvokePrivateMethod(stage, "InitializeSongList");
                Assert.Empty(display.CurrentList); // placeholder list while pending

                // Deactivate retires the scope BEFORE tearing down UI state: the queued
                // initializer apply drops and can never reach the disposed stage state.
                SetPrivateField(stage, "_currentPhase", StagePhase.Normal);
                stage.Deactivate();

                Assert.Null(GetPrivateField<SongLibrarySnapshot>(stage, "_appliedLibrarySnapshot"));
                Assert.Equal(0L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
                Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_currentSongList")!);
                Assert.Empty(display.CurrentList);
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
        public void SubscribeLibraryPublications_WhenNoActiveScope_ShouldNotSubscribe()
        {
            var stage = CreateStage();

            // No activation scope: nothing could apply a publication, so the stage must
            // not register a handler at all.
            InvokePrivateMethod(stage, "SubscribeLibraryPublications");

            Assert.Null(GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                stage, "_libraryPublicationHandler"));
            Assert.Equal(0L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
        }

        [Fact]
        public void SongLibraryPublished_WhenReporterIsBoundToARetiredScope_ShouldDropTheSignal()
        {
            var stage = CreateStage();
            AttachCoreUi(stage);
            var scope = stage.InstallTestBackgroundScope();
            InvokePrivateMethod(stage, "SubscribeLibraryPublications");
            var handler = GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                stage, "_libraryPublicationHandler")!;

            try
            {
                scope.Retire();

                // A publisher thread that already captured the old handler can still call
                // it after retirement: the reporter is bound to the retired scope, so the
                // signal drops directly instead of reaching torn-down UI state.
                handler.Invoke(null!, new SongLibraryPublishedEventArgs(Snapshot(45, Array.Empty<SongListNode>(), Array.Empty<string>())));

                Assert.Equal(0L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
                Assert.Null(GetPrivateField<SongLibrarySnapshot>(stage, "_appliedLibrarySnapshot"));
            }
            finally
            {
                InvokePrivateMethod(stage, "UnsubscribeLibraryPublications");
            }
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
            Assert.Equal(2, display.CurrentList.Count);
            Assert.Same(newestNode, display.CurrentList[0]);
            Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
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
            new(version, roots, activeRoots, enumeratedFileCount: roots.Count, discoveredScoreCount: roots.Count);

        [Fact]
        public void FilterNodesForActiveRoots_WhenNodesAreNull_ShouldReturnEmptyList()
        {
            var stage = CreateStage();

            var result = InvokePrivateMethod<List<SongListNode>>(
                stage, "FilterNodesForActiveRoots", null, new[] { "/songs" });

            Assert.Empty(result);
        }

        [Fact]
        public void FilterNodesForActiveRoots_WhenActiveRootsAreNull_ShouldReturnAllNodes()
        {
            var stage = CreateStage();
            var song = Score("Test", 1, "/songs/test.dtx");

            var result = InvokePrivateMethod<List<SongListNode>>(
                stage, "FilterNodesForActiveRoots", new[] { song }, null);

            Assert.Single(result);
            Assert.Same(song, result[0]);
        }

        [Fact]
        public void FilterNodesForActiveRoots_WhenActiveRootsAreEmpty_ShouldReturnEmptyList()
        {
            var stage = CreateStage();
            var song = Score("Test", 1, "/songs/test.dtx");

            var result = InvokePrivateMethod<List<SongListNode>>(
                stage, "FilterNodesForActiveRoots", new[] { song }, Array.Empty<string>());

            Assert.Empty(result);
        }

        [Fact]
        public void FilterNodesForActiveRoots_WhenRootsDoNotNormalize_ShouldReturnEmptyList()
        {
            var stage = CreateStage();
            var song = Score("Test", 1, "/songs/test.dtx");

            // A NUL character makes SongPathIdentity.TryNormalize fail, so no roots
            // normalize and the filtered list is empty.
            var result = InvokePrivateMethod<List<SongListNode>>(
                stage, "FilterNodesForActiveRoots", new[] { song }, new[] { "/bad\0root" });

            Assert.Empty(result);
        }

        [Fact]
        public void FilterNodesForActiveRoots_WhenSongIsUnderActiveRoot_ShouldIncludeIt()
        {
            var stage = CreateStage();
            var song = Score("Test", 1, "/library/active/test.dtx");

            var result = InvokePrivateMethod<List<SongListNode>>(
                stage, "FilterNodesForActiveRoots", new[] { song }, new[] { "/library/active" });

            Assert.Single(result);
            Assert.Same(song, result[0]);
        }

        [Fact]
        public void FilterNodesForActiveRoots_WhenSongIsOutsideActiveRoot_ShouldExcludeIt()
        {
            var stage = CreateStage();
            var active = Score("Active", 1, "/library/active/active.dtx");
            var inactive = Score("Inactive", 2, "/library/inactive/inactive.dtx");

            var result = InvokePrivateMethod<List<SongListNode>>(
                stage, "FilterNodesForActiveRoots", new[] { active, inactive }, new[] { "/library/active" });

            Assert.Single(result);
            Assert.Same(active, result[0]);
        }

        [Fact]
        public void FilterNodesForAppliedLibrary_WhenNoSnapshotIsApplied_ShouldReturnRootFilteredNodes()
        {
            var stage = CreateStage();
            var song = Score("Test", 1, "/songs/test.dtx");

            // Without an applied snapshot, GetAppliedActiveRoots returns null, so
            // FilterNodesForActiveRoots returns all nodes.
            var result = InvokePrivateMethod<List<SongListNode>>(
                stage, "FilterNodesForAppliedLibrary", new object[] { new[] { song } });

            Assert.Single(result);
            Assert.Same(song, result[0]);
        }

        [Fact]
        public void FilterNodesForAppliedLibrary_WhenSnapshotHasNoScoreIdentities_ShouldReturnEmptyList()
        {
            var stage = CreateStage();
            var song = Score("Test", 1, "/library/active/test.dtx");
            // Apply a snapshot with only boxes (no score nodes) so the identity set is empty.
            var box = Box("Empty", "/library/active/Empty");
            InvokePrivateMethod(stage, "ApplyLibrarySnapshot",
                Snapshot(30, new[] { box }, new[] { "/library/active" }));

            var result = InvokePrivateMethod<List<SongListNode>>(
                stage, "FilterNodesForAppliedLibrary", new object[] { new[] { song } });

            Assert.Empty(result);
        }

        [Fact]
        public void GetAppliedActiveRoots_WhenNoSnapshotIsApplied_ShouldReturnNull()
        {
            var stage = CreateStage();

            var roots = InvokePrivateMethod<IReadOnlyList<string>?>(
                stage, "GetAppliedActiveRoots");

            Assert.Null(roots);
        }

        [Fact]
        public void GetAppliedActiveRoots_WhenSnapshotIsApplied_ShouldReturnSnapshotRoots()
        {
            var stage = CreateStage();
            InvokePrivateMethod(stage, "ApplyLibrarySnapshot",
                Snapshot(31, Array.Empty<SongListNode>(), new[] { "/library/one", "/library/two" }));

            var roots = InvokePrivateMethod<IReadOnlyList<string>?>(
                stage, "GetAppliedActiveRoots");

            Assert.Equal(new[] { "/library/one", "/library/two" }, roots);
        }

        [Fact]
        public void ClearRemovedSelectionPresentation_ShouldClearSelectionAndPreview()
        {
            var stage = CreateStage();
            var preview = new PreviewImagePanel();
            var statusPanel = new SongStatusPanel { Visible = true };
            AttachCoreUi(stage, previewPanel: preview, statusPanel: statusPanel);
            var song = Score("Selected", 1, "/songs/selected.dtx");
            SetPrivateField(stage, "_selectedSong", song);
            SetPrivateField(stage, "_isInStatusPanel", true);

            InvokePrivateMethod(stage, "ClearRemovedSelectionPresentation");

            Assert.Null(GetPrivateField<SongListNode>(stage, "_selectedSong"));
            Assert.False(GetPrivateField<bool>(stage, "_isInStatusPanel"));
            Assert.False(statusPanel.Visible);
        }

        [Fact]
        public void GetPublishedScoreIdentities_WhenTreeContainsScores_ShouldCollectSongAndChartIdentities()
        {
            var song = Score("Test", 7, "/library/active/test.dtx");
            var box = Box("Group", "/library/active/Group", song);

            var identities = InvokeStatic<HashSet<string>>(
                "GetPublishedScoreIdentities", new object[] { new[] { box } });

            Assert.Contains("song:7", identities);
            Assert.Contains($"chart:{NormalizePath("/library/active/test.dtx")}", identities);
        }

        [Fact]
        public void GetScoreIdentityCandidates_WhenNodeIsNotAScore_ShouldYieldNoIdentities()
        {
            var box = Box("Folder", "/library/active/Folder");

            var candidates = InvokeStatic<IEnumerable<string>>(
                "GetScoreIdentityCandidates", new object[] { box });

            Assert.Empty(candidates);
        }

        [Fact]
        public void GetScoreIdentityCandidates_WhenScoreHasNoDatabaseId_ShouldUseChartPathIdentity()
        {
            var chart = new SongChart { FilePath = "/library/active/anon.dtx" };
            var song = new SongEntity { Id = 0, Title = "Anon", Charts = new List<SongChart> { chart } };
            chart.Song = song;
            var node = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Anon",
                DatabaseSong = song,
                DatabaseChart = chart,
            };

            var candidates = InvokeStatic<IEnumerable<string>>(
                "GetScoreIdentityCandidates", new object[] { node }).ToList();

            // The chart path is yielded from both DatabaseChart and the song's Charts
            // collection, producing two identical chart: identities.
            Assert.Equal(2, candidates.Count);
            Assert.All(candidates, c => Assert.StartsWith("chart:", c));
        }

        [Fact]
        public void ApplyLibrarySnapshotCore_WhenSnapshotIsNull_ShouldThrowArgumentNullException()
        {
            var stage = CreateStage();

            var thrown = Assert.Throws<TargetInvocationException>(() =>
                InvokePrivateMethod(stage, "ApplyLibrarySnapshotCore", new object?[] { null, true }));
            Assert.IsType<ArgumentNullException>(thrown.InnerException);
        }

        [Fact]
        public void ReconcileLibrarySnapshot_WhenSnapshotIsNull_ShouldThrowArgumentNullException()
        {
            var stage = CreateStage();

            var thrown = Assert.Throws<TargetInvocationException>(() =>
                InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", new object?[] { null }));
            Assert.IsType<ArgumentNullException>(thrown.InnerException);
        }

        [Fact]
        public void ResolveLibraryEmptyState_WhenRootsContainScores_ShouldReturnHasSongs()
        {
            var stage = CreateStage();
            var song = Score("Test", 1, "/library/active/test.dtx");
            var snapshot = Snapshot(50, new[] { song }, new[] { "/library/active" });

            var result = InvokePrivateMethod<object>(stage, "ResolveLibraryEmptyState", snapshot);

            Assert.Equal("HasSongs", result!.ToString());
        }

        #region Helpers

        private static string NormalizePath(string path)
        {
            return SongPathIdentity.TryNormalize(path, out var normalized) ? normalized : path;
        }

        private static T InvokeStatic<T>(string name, params object[] args)
        {
            var method = typeof(SongSelectionStage).GetMethod(
                name, BindingFlags.Static | BindingFlags.NonPublic);
            Assert.NotNull(method);
            return (T)method!.Invoke(null, args)!;
        }

        #endregion
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
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
            SetPrivateField(stage, "_activationVersion", 0);
            SetPrivateField(stage, "_activeSongInitializationGeneration", 1L);
            EnqueueSongInitializationResult(stage, 0, 1, staleSnapshot, new[] { oldNode });
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
            SetPrivateField(stage, "_activationVersion", 0);
            SetPrivateField(stage, "_activeSongInitializationGeneration", 1L);
            EnqueueSongInitializationResult(stage, 0, 1, newerSnapshot, new[] { replacementBox });
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
        public async Task CheckSongInitializationCompletion_WhenOldActivationCompletesAfterNewCompletion_ShouldUseNewSnapshot()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display: display);
            var oldNode = Score("Old", 91, "/library/old/old.dtx");
            var newNode = Score("New", 92, "/library/new/new.dtx");
            var oldSnapshot = Snapshot(91, new[] { oldNode }, new[] { "/library/old" });
            var newSnapshot = Snapshot(92, new[] { newNode }, new[] { "/library/new" });
            var oldWorkerPassedTokenCheck = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseOldWorker = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var newWorker = new TaskCompletionSource<List<SongListNode>>(
                TaskCreationOptions.RunContinuationsAsynchronously);

            SetPrivateField(stage, "_activationVersion", 71);
            var completions = GetPrivateField<
                System.Collections.Concurrent.ConcurrentQueue<SongSelectionStage.SongInitializationResult>>(
                stage,
                "_pendingSongInitializationResults")!;
            var oldWorker = Task.Run(async () =>
            {
                // Model the prior initializer after it has passed its activation check but
                // before it publishes its immutable completion record.
                oldWorkerPassedTokenCheck.TrySetResult(true);
                await releaseOldWorker.Task;
                completions.Enqueue(new SongSelectionStage.SongInitializationResult(
                    71,
                    1,
                    oldSnapshot,
                    new[] { oldNode }));
            });
            await oldWorkerPassedTokenCheck.Task.WaitAsync(TimeSpan.FromSeconds(3));

            // A later activation completes and installs its own task/snapshot first.
            SetPrivateField(stage, "_activationVersion", 72);
            SetPrivateField(stage, "_activeSongInitializationGeneration", 2L);
            EnqueueSongInitializationResult(stage, 72, 2, newSnapshot, new[] { newNode });
            SetPrivateField(stage, "_songInitializationTask", newWorker.Task);
            SetPrivateField(stage, "_songInitializationProcessed", false);
            newWorker.SetResult(new List<SongListNode> { newNode });

            // The old worker is released last. Its record must be discarded instead of being
            // paired with the newer activation's completed task.
            releaseOldWorker.SetResult(true);
            await oldWorker.WaitAsync(TimeSpan.FromSeconds(3));

            InvokePrivateMethod(stage, "CheckSongInitializationCompletion");

            Assert.Equal(92L, GetPrivateField<long>(stage, "_appliedLibraryPublicationVersion"));
            Assert.Equal(new[] { newNode }, display.CurrentList);
            Assert.Empty(completions);
        }

        [Fact]
        public async Task Deactivate_WhenCancelledInitializerCompletesLate_ShouldDiscardOnlyItsInactiveGeneration()
        {
            var stage = CreateStage();
            var currentNode = Score("Current", 311, "/library/current/current.dtx");
            var staleNode = Score("Stale", 310, "/library/stale/stale.dtx");
            var staleSnapshot = Snapshot(310, new[] { staleNode }, new[] { "/library/stale" });
            var display = new SongListDisplay
            {
                CurrentList = new List<SongListNode> { currentNode }
            };
            AttachCoreUi(stage, display: display);
            var completions = GetPrivateField<
                System.Collections.Concurrent.ConcurrentQueue<SongSelectionStage.SongInitializationResult>>(
                stage,
                "_pendingSongInitializationResults")!;
            var workerStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseWorker = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = Task.Run(async () =>
            {
                workerStarted.TrySetResult(true);
                await releaseWorker.Task;
                completions.Enqueue(new SongSelectionStage.SongInitializationResult(
                    310,
                    17,
                    staleSnapshot,
                    new[] { staleNode }));
                return new List<SongListNode> { staleNode };
            });

            SetPrivateField(stage, "_activationVersion", 310);
            SetPrivateField(stage, "_activeSongInitializationGeneration", 17L);
            SetPrivateField(stage, "_songInitializationTask", worker);
            SetPrivateField(stage, "_cancellationTokenSource", new CancellationTokenSource());
            await workerStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            stage.Deactivate();
            releaseWorker.TrySetResult(true);
            await worker.WaitAsync(TimeSpan.FromSeconds(3));
            await WaitForSongInitializationQueueAsync(completions, results => results.Length == 0);

            Assert.Empty(completions);
            Assert.Equal(new[] { currentNode }, display.CurrentList);
            Assert.Null(GetPrivateField<SongLibrarySnapshot>(stage, "_appliedLibrarySnapshot"));
        }

        [Fact]
        public async Task Deactivate_WhenCancelledInitializerCompletesLate_ShouldPreserveNewerGenerationRecords()
        {
            var stage = CreateStage();
            var staleNode = Score("Stale", 320, "/library/stale/stale.dtx");
            var newerNode = Score("Newer", 321, "/library/newer/newer.dtx");
            var staleSnapshot = Snapshot(320, new[] { staleNode }, new[] { "/library/stale" });
            var newerSnapshot = Snapshot(321, new[] { newerNode }, new[] { "/library/newer" });
            var completions = GetPrivateField<
                System.Collections.Concurrent.ConcurrentQueue<SongSelectionStage.SongInitializationResult>>(
                stage,
                "_pendingSongInitializationResults")!;
            var workerStarted = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var releaseWorker = new TaskCompletionSource<bool>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var worker = Task.Run(async () =>
            {
                workerStarted.TrySetResult(true);
                await releaseWorker.Task;
                completions.Enqueue(new SongSelectionStage.SongInitializationResult(
                    320,
                    23,
                    staleSnapshot,
                    new[] { staleNode }));
                return new List<SongListNode> { staleNode };
            });

            SetPrivateField(stage, "_activationVersion", 320);
            SetPrivateField(stage, "_activeSongInitializationGeneration", 23L);
            SetPrivateField(stage, "_songInitializationTask", worker);
            SetPrivateField(stage, "_cancellationTokenSource", new CancellationTokenSource());
            await workerStarted.Task.WaitAsync(TimeSpan.FromSeconds(3));

            stage.Deactivate();
            completions.Enqueue(new SongSelectionStage.SongInitializationResult(
                321,
                24,
                newerSnapshot,
                new[] { newerNode }));
            releaseWorker.TrySetResult(true);
            await worker.WaitAsync(TimeSpan.FromSeconds(3));
            await WaitForSongInitializationQueueAsync(
                completions,
                results => results.Length == 1 &&
                           results[0].ActivationVersion == 321 &&
                           results[0].Generation == 24);

            var remaining = Assert.Single(completions);
            Assert.Equal(321, remaining.ActivationVersion);
            Assert.Equal(24L, remaining.Generation);
            Assert.Same(newerSnapshot, remaining.Snapshot);
            Assert.Equal(new[] { newerNode }, remaining.SongList);
        }

        [Fact]
        public void CheckSongInitializationCompletion_WhenPriorActivationCompletesAfterSynchronousReactivation_ShouldDiscardQueuedResult()
        {
            var stage = CreateStage();
            var display = new SongListDisplay();
            AttachCoreUi(stage, display: display);
            var currentNode = Score("Current", 101, "/library/current/current.dtx");
            var staleNode = Score("Stale", 100, "/library/stale/stale.dtx");
            var currentSnapshot = Snapshot(101, new[] { currentNode }, new[] { "/library/current" });
            var staleSnapshot = Snapshot(100, new[] { staleNode }, new[] { "/library/stale" });
            var completions = GetPrivateField<
                System.Collections.Concurrent.ConcurrentQueue<SongSelectionStage.SongInitializationResult>>(
                stage,
                "_pendingSongInitializationResults")!;

            // Model a late worker from activation 100 arriving after activation 101 took the
            // already-initialized synchronous path, so there is no current initializer task.
            SetPrivateField(stage, "_activationVersion", 101);
            SetPrivateField(stage, "_activeSongInitializationGeneration", 0L);
            SetPrivateField(stage, "_songInitializationTask", null!);
            SetPrivateField(stage, "_songInitializationProcessed", false);
            SetPrivateField(stage, "_appliedLibrarySnapshot", currentSnapshot);
            SetPrivateField(stage, "_appliedLibraryPublicationVersion", 101L);
            SetPrivateField(stage, "_currentSongList", new List<SongListNode> { currentNode });
            display.CurrentList = new List<SongListNode> { currentNode };
            completions.Enqueue(new SongSelectionStage.SongInitializationResult(
                100,
                17,
                staleSnapshot,
                new[] { staleNode }));

            InvokePrivateMethod(stage, "CheckSongInitializationCompletion");

            Assert.Empty(completions);
            Assert.Same(currentSnapshot, GetPrivateField<SongLibrarySnapshot>(
                stage,
                "_appliedLibrarySnapshot"));
            Assert.Equal(new[] { currentNode }, display.CurrentList);
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

        private static void EnqueueSongInitializationResult(
            SongSelectionStage stage,
            int activationVersion,
            long generation,
            SongLibrarySnapshot? snapshot,
            IReadOnlyList<SongListNode> songList)
        {
            var completions = GetPrivateField<
                System.Collections.Concurrent.ConcurrentQueue<SongSelectionStage.SongInitializationResult>>(
                stage,
                "_pendingSongInitializationResults")!;
            completions.Enqueue(new SongSelectionStage.SongInitializationResult(
                activationVersion,
                generation,
                snapshot,
                songList));
        }

        private static async Task WaitForSongInitializationQueueAsync(
            System.Collections.Concurrent.ConcurrentQueue<SongSelectionStage.SongInitializationResult> completions,
            Func<SongSelectionStage.SongInitializationResult[], bool> condition)
        {
            for (var attempt = 0; attempt < 600; attempt++)
            {
                if (condition(completions.ToArray()))
                    return;

                await Task.Delay(10);
            }

            Assert.True(condition(completions.ToArray()),
                "The expected SongSelection initialization completion queue state was not observed.");
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
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.SongSelection;
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
        public void ApplyPendingLibraryPublication_WhenSignalIsOlderThanTheManagerSnapshot_ShouldApplyTheNewerManagerSnapshot()
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

                // A stale publication signal (v20) still reconciles: the update thread
                // fetches the newest coherent manager snapshot (v31), never any stale
                // signal payload, and acknowledges the newer of signal and fetch.
                InvokePrivateMethod(stage, "ApplyPendingLibraryPublication", 20L);

                Assert.Equal(2, display.CurrentList.Count);
                Assert.Same(newestNode, display.CurrentList[0]);

                // A second, still-stale signal must not reapply over the applied v31.
                var displayListAfter = display.CurrentList;
                InvokePrivateMethod(stage, "ApplyPendingLibraryPublication", 22L);
                Assert.Same(displayListAfter, display.CurrentList);
                Assert.Equal(2, display.CurrentList.Count);
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
            var preview = new PreviewImagePanel();

            try
            {
                AttachCoreUi(stage, display: display, previewPanel: preview);
                rootSongs.Clear();
                rootSongs.Add(newestNode);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/newest" });
                SetPrivateField(songManager, "_publicationVersion", 31L);

                InvokePrivateMethod(stage, "ApplyPendingLibraryPublication", 31L);

                Assert.Equal(2, display.CurrentList.Count);
                Assert.Same(newestNode, display.CurrentList[0]);
                Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
                Assert.Equal(new[] { "/library/newest" }, preview.ActiveSongRootPaths);
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
                // Reconcile installs v50 as the applied snapshot and acknowledges it.
                InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", appliedSnapshot);
                var displayListBefore = display.CurrentList;

                // A stale (already-applied) signal must not reapply an older tree.
                InvokePrivateMethod(stage, "ApplyPendingLibraryPublication", 50L);

                Assert.Same(displayListBefore, display.CurrentList);
                Assert.Equal(2, display.CurrentList.Count);
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

                // The v51 publication already reconciled (restoring navigation and
                // selection) while the manager state above stays stale (v50) for the
                // initializer.
                var appliedSnapshot = Snapshot(51, new[] { first, selected }, new[] { "/library/new" });
                InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", appliedSnapshot);

                // The initializer completion fetches on the update thread: the manager's
                // stale snapshot (v50) must not overwrite the newer publication (v51) that
                // already restored the user's navigation and selection.
                InvokePrivateMethod(stage, "ApplyInitializedLibrarySnapshot");

                Assert.Same(selected, display.SelectedSong);
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

                // v81 is reconciled as the already-applied publication; the nav stack and
                // selection above are restored against it by the same path a live
                // publication takes.
                var appliedSnapshot = Snapshot(81, new[] { oldBox }, new[] { "/library/shared" });
                InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", appliedSnapshot);

                InvokePrivateMethod(stage, "ApplyInitializedLibrarySnapshot");

                // The newer snapshot (v82) reconciles the nested selection against the
                // replacement box from the same coherent tree.
                Assert.Same(replacementBox.Children,
                    GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
                Assert.Single(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
                Assert.Equal("BOX", GetPrivateField<string>(stage, "_currentBreadcrumb"));
                Assert.Same(replacementSong, display.SelectedSong);
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
        public void InitializeSongList_WhenInitializerProjectionFaultsWithAPersistentThrower_ShouldStillUnblockReadinessOnAnEmptyPlaceholder()
        {
            // HPA-802 round-3 concern follow-up: with no snapshot ever applied, can a
            // subscriber that throws on EVERY selection event abort the tail of
            // ApplyLibraryInitializationFailure (the _libraryInitializationCompleted
            // write and the open modal's readiness) after the initializer's snapshot
            // application faults mid-projection? The spec requires initialization
            // failure to unblock readiness and leave an EMPTY placeholder even then.
            var (stage, intake) = CreateStageWithIntake();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var initializerSong = Score("Initializer", 51, "/library/init/song.dtx");
            var display = new SongListDisplay();
            var preview = new PreviewImagePanel();
            var statusPanel = new SongStatusPanel();
            var modal = new SongSearchFilterModal(new Mock<ITextInputSource>().Object) { IsLibraryReady = false };
            var scope = stage.InstallTestBackgroundScope();
            var throwerInvocations = 0;

            try
            {
                AttachCoreUi(stage, display: display, statusPanel: statusPanel, previewPanel: preview);
                modal.Open(SongFilterCriteria.Default);
                SetPrivateField(stage, "_searchFilterModal", modal);

                // Production subscriber order: the stage's real selection handler runs
                // first, then a persistently faulting subscriber that throws on EVERY
                // selection notification, including the row-zero event and the
                // placeholder's empty-list event.
                display.SelectionChanged += (sender, e) =>
                    InvokePrivateMethod(stage, "OnSongSelectionChanged", sender, e);
                display.SelectionChanged += (_, _) =>
                {
                    throwerInvocations++;
                    throw new InvalidOperationException("selection callback failed");
                };

                // No prior snapshot: the library stays uninitialized (the activation head
                // must take the pending-library placeholder branch) while its roots already
                // hold the initializer's result, fetched live when the terminal runs.
                rootSongs.Clear();
                rootSongs.Add(initializerSong);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/init" });
                SetPrivateField(songManager, "_publicationVersion", 51L);

                // Activation head: projects the empty pending-library placeholder (silent
                // on a fresh display) and posts the initializer terminal onto the scope.
                InvokePrivateMethod(stage, "InitializeSongList");
                intake.Subscribe(scope, signal =>
                    InvokePrivateMethod(stage, "ApplyPendingLibraryPublication", signal));

                // The initializer scan completes: the real publication event queues the
                // version signal exactly as the publisher thread would.
                PublishLibrarySnapshot(songManager, 51, "/library/init", initializerSong);

                scope.Drain();

                // The initializer's reconcile faulted on a selection event (the thrower
                // fired), the rollback restored the placeholder, and the failure fallback
                // still completed its tail: empty placeholder, readiness unblocked.
                Assert.True(throwerInvocations > 0);
                Assert.Null(intake.AppliedSnapshot);
                Assert.Equal(0L, intake.CommittedVersion);
                Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_currentSongList")!);
                Assert.Empty(display.CurrentList);
                Assert.Null(display.SelectedSong);
                Assert.Null(GetPrivateField<SongListNode>(stage, "_selectedSong"));
                Assert.Empty(preview.ActiveSongRootPaths);
                Assert.False(statusPanel.Visible);
                Assert.True(GetPrivateField<bool>(stage, "_libraryInitializationCompleted"));
                Assert.True(modal.IsLibraryReady);
                Assert.True(modal.IsOpen);

                scope.Retire();
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
                    var projected = SongSelectionLibraryIntake.FilterNodesForActiveRoots(
                        oldSnapshot.RootSongs,
                        oldSnapshot.ActiveRoots);
                    BookmarkStateReconciler.Apply(oldSnapshot.RootSongs, 42, isBookmarked: true);
                    Assert.Equal(new[] { oldNode }, projected);
                }
            });

            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", newestSnapshot);
            await oldSnapshotReader;

            Assert.Equal(2, display.CurrentList.Count);
            Assert.Same(newestNode, display.CurrentList[0]);
            Assert.Equal(NodeType.Random, display.CurrentList[1].Type);
            Assert.DoesNotContain(oldNode, display.CurrentList);
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
        public void ReconcileLibrarySnapshot_WhenSnapshotIsNull_ShouldThrowArgumentNullException()
        {
            var stage = CreateStage();

            var thrown = Assert.Throws<TargetInvocationException>(() =>
                InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", new object?[] { null }));
            Assert.IsType<ArgumentNullException>(thrown.InnerException);
        }

        [Fact]
        public void ReconcileLibrarySnapshot_WhenProjectionFailsAfterInstall_WithPriorSnapshot_ShouldRestoreThePriorSnapshotAndProjection()
        {
            var (stage, intake) = CreateStageWithIntake();
            var priorNode = Score("Prior", 1, "/library/prior/prior.dtx");
            var newNode = Score("New", 2, "/library/new/new.dtx");
            var thirdNode = Score("Third", 3, "/library/third/third.dtx");
            var priorSnapshot = Snapshot(11, new[] { priorNode }, new[] { "/library/prior" });
            var failedSnapshot = Snapshot(12, new[] { newNode }, new[] { "/library/new" });
            var laterSnapshot = Snapshot(13, new[] { thirdNode }, new[] { "/library/third" });
            var display = new SongListDisplay();
            AttachCoreUi(stage, display: display);

            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", priorSnapshot);
            Assert.Equal(2, display.CurrentList.Count);

            // A selection callback raised during the failed snapshot's list refresh
            // (the real SongListDisplay.SelectionChanged event) faults the
            // reconciliation after Install already replaced the applied snapshot.
            display.SelectionChanged += (_, e) =>
            {
                if (e.SelectedSong == newNode)
                    throw new InvalidOperationException("selection callback failed");
            };

            Assert.Throws<TargetInvocationException>(() =>
                InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", failedSnapshot));

            // The failed snapshot must not remain installed: the prior valid snapshot
            // and its projection survive, and nothing newer was acknowledged. (The
            // Random sentinel row is re-created per projection; the snapshot-derived
            // rows are compared by reference.)
            Assert.Same(priorSnapshot, intake.AppliedSnapshot);
            Assert.Equal(11L, intake.CommittedVersion);
            Assert.Equal(2, display.CurrentList.Count);
            Assert.Same(priorNode, display.CurrentList[0]);
            Assert.Same(priorNode, display.SelectedSong);
            Assert.Equal(new[] { priorNode },
                GetPrivateField<List<SongListNode>>(stage, "_currentSongList")!);

            // Failure readiness: the next publication still reconciles normally.
            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", laterSnapshot);

            Assert.Same(laterSnapshot, intake.AppliedSnapshot);
            Assert.Equal(13L, intake.CommittedVersion);
            Assert.Equal(new[] { thirdNode, display.CurrentList[^1] }, display.CurrentList);
        }

        [Fact]
        public void ApplyInitializedLibrarySnapshot_WhenReconciliationFailsWithoutPriorSnapshot_ShouldClearThePlaceholder()
        {
            var (stage, intake) = CreateStageWithIntake();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var firstNode = Score("First", 5, "/library/first/first.dtx");
            var display = new SongListDisplay();

            try
            {
                AttachCoreUi(stage, display: display);
                rootSongs.Clear();
                rootSongs.Add(firstNode);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/first" });
                SetPrivateField(songManager, "_publicationVersion", 5L);

                // A selection callback raised while the first snapshot's list is
                // projected faults the initializer reconciliation after Install.
                display.SelectionChanged += (_, e) =>
                {
                    if (e.SelectedSong == firstNode)
                        throw new InvalidOperationException("selection callback failed");
                };

                // The initializer catches the reconciliation failure internally and
                // must fall back to the pending placeholder: the first failed snapshot
                // is not treated as valid.
                InvokePrivateMethod(stage, "ApplyInitializedLibrarySnapshot");

                Assert.Null(intake.AppliedSnapshot);
                Assert.Equal(0L, intake.CommittedVersion);
                Assert.Empty(display.CurrentList);
                Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_currentSongList")!);
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
        public void ApplyPendingLibraryPublication_WhenSignalIsNewerThanTheFetchedSnapshot_ShouldNotRedeliverTheSameSignal()
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

                // First delivery of a signal newer than the fetched snapshot (41 > 31):
                // the fetch reconciles the v31 snapshot and the handler acknowledges
                // the signal itself, not just the fetched version.
                InvokePrivateMethod(stage, "ApplyPendingLibraryPublication", 41L);
                var projectionAfterFirstDelivery = display.CurrentList;
                Assert.Equal(2, projectionAfterFirstDelivery.Count);

                // The same signal delivered again must not rebuild the display: the
                // committed marker tracks max(signal, fetched) = 41.
                InvokePrivateMethod(stage, "ApplyPendingLibraryPublication", 41L);

                Assert.Same(projectionAfterFirstDelivery, display.CurrentList);
                Assert.Equal(2, display.CurrentList.Count);
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
        public void ReconcileLibrarySnapshot_WhenFailedSnapshotRemovesTheOpenFolder_ShouldRestoreThePreAttemptFolderAndSelection()
        {
            var (stage, intake) = CreateStageWithIntake();
            var priorSong = Score("Prior", 1, "/library/prior/BOX/prior.dtx");
            var priorBox = Box("BOX", "/library/prior/BOX", priorSong);
            var priorSnapshot = Snapshot(11, new[] { priorBox }, new[] { "/library/prior" });
            var newNode = Score("New", 2, "/library/new/new.dtx");
            var failedSnapshot = Snapshot(12, new[] { newNode }, new[] { "/library/new" });
            var laterSong = Score("Later", 3, "/library/later/later.dtx");
            var laterSnapshot = Snapshot(13, new[] { laterSong }, new[] { "/library/later" });
            var display = new SongListDisplay();
            var preview = new PreviewImagePanel();
            AttachCoreUi(stage, display: display, previewPanel: preview);

            // Browse into the prior folder, then reconcile the prior snapshot so the
            // stage is legitimately showing it (committed 11) with the folder open.
            SetPrivateField(stage, "_currentSongList", priorBox.Children);
            SetPrivateField(stage, "_currentBreadcrumb", "BOX");
            SetPrivateField(stage, "_selectedSong", priorSong);
            GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(
                new SongListNode { Children = new List<SongListNode> { priorBox }, Title = "" });
            display.CurrentList = new List<SongListNode>
            {
                new() { Type = NodeType.BackBox, Title = ".." },
                priorSong
            };
            display.SelectedIndex = 1;
            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", priorSnapshot);
            Assert.Same(priorSnapshot, intake.AppliedSnapshot);

            // The failed snapshot removes the open folder entirely; the selection
            // callback raised while the replacement row is projected faults the
            // reconciliation after Install replaced the applied snapshot.
            display.SelectionChanged += (_, e) =>
            {
                if (e.SelectedSong == newNode)
                    throw new InvalidOperationException("selection callback failed");
            };

            Assert.Throws<TargetInvocationException>(() =>
                InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", failedSnapshot));

            // The rollback must resume from the PRE-ATTEMPT browse state: the open
            // folder, breadcrumb and the selected song inside it all survive instead
            // of a root restart against the post-failure navigation.
            Assert.Same(priorSnapshot, intake.AppliedSnapshot);
            Assert.Equal(11L, intake.CommittedVersion);
            Assert.Single(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
            Assert.Equal("BOX", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Same(priorBox.Children, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Same(priorSong, GetPrivateField<SongListNode>(stage, "_selectedSong"));
            Assert.Equal(new[] { "/library/prior" }, preview.ActiveSongRootPaths);
            Assert.Equal(3, display.CurrentList.Count);
            Assert.Same(priorSong, display.SelectedSong);
            Assert.DoesNotContain(newNode, display.CurrentList);

            // The stage remains usable: the next publication reconciles normally.
            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", laterSnapshot);

            Assert.Same(laterSnapshot, intake.AppliedSnapshot);
            Assert.Equal(13L, intake.CommittedVersion);
            Assert.Equal(2, display.CurrentList.Count);
            Assert.Same(laterSong, display.CurrentList[0]);
        }

        [Fact]
        public void ReconcileLibrarySnapshot_WhenTheRestorationProjectionFailsToo_ShouldKeepTheRestoredBrowseStateAndRethrowTheOriginalFault()
        {
            var (stage, intake) = CreateStageWithIntake();
            var priorSong = Score("Prior", 1, "/library/prior/BOX/prior.dtx");
            var priorBox = Box("BOX", "/library/prior/BOX", priorSong);
            var priorSnapshot = Snapshot(11, new[] { priorBox }, new[] { "/library/prior" });
            var newNode = Score("New", 2, "/library/new/new.dtx");
            var failedSnapshot = Snapshot(12, new[] { newNode }, new[] { "/library/new" });
            var display = new SongListDisplay();
            AttachCoreUi(stage, display: display);
            SetPrivateField(stage, "_currentSongList", priorBox.Children);
            SetPrivateField(stage, "_currentBreadcrumb", "BOX");
            SetPrivateField(stage, "_selectedSong", priorSong);
            GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(
                new SongListNode { Children = new List<SongListNode> { priorBox }, Title = "" });
            display.CurrentList = new List<SongListNode>
            {
                new() { Type = NodeType.BackBox, Title = ".." },
                priorSong
            };
            display.SelectedIndex = 1;
            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", priorSnapshot);

            // The callback faults BOTH projections: the failed snapshot's replacement
            // row and the restoration projection of the prior snapshot's own row.
            var attemptFault = new InvalidOperationException("failed-attempt fault");
            var restorationFault = new InvalidOperationException("restoration fault");
            display.SelectionChanged += (_, e) =>
            {
                if (e.SelectedSong == newNode)
                    throw attemptFault;
                if (e.SelectedSong == priorSong)
                    throw restorationFault;
            };

            var thrown = Assert.Throws<TargetInvocationException>(() =>
                InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", failedSnapshot));

            // The original fault is never masked, even though the restore also faulted.
            Assert.Same(attemptFault, thrown.InnerException);
            Assert.Same(priorSnapshot, intake.AppliedSnapshot);
            Assert.Equal(11L, intake.CommittedVersion);

            // Browse state is restored by the pure reconciliation steps even while the
            // display's selection callbacks keep throwing: folder, breadcrumb and the
            // selected song inside it match the pre-attempt state. (UI polish that runs
            // after the faulting projection, e.g. label text, cannot complete; the next
            // successful reconciliation refreshes it.)
            Assert.Single(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
            Assert.Equal("BOX", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Same(priorBox.Children, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Same(priorSong, GetPrivateField<SongListNode>(stage, "_selectedSong"));
            Assert.Null(GetPrivateField<System.Collections.Generic.IReadOnlyList<FilteredSongResult>>(stage, "_filteredView"));
        }

        [Fact]
        public void ReconcileLibrarySnapshot_WhenTheFirstSnapshotFails_ShouldClearFilterViewAndPreviewRootsSoStaleRowsCannotReappear()
        {
            var (stage, intake) = CreateStageWithIntake();
            var newNode = Score("New", 2, "/library/new/new.dtx");
            var failedSnapshot = Snapshot(12, new[] { newNode }, new[] { "/library/new" });
            var display = new SongListDisplay();
            var preview = new PreviewImagePanel();
            AttachCoreUi(stage, display: display, previewPanel: preview);

            // A persisted filter is active while no snapshot has ever been applied:
            // the failed attempt rebuilds the filtered view from the failed snapshot
            // before its projection faults.
            SetPrivateField(stage, "_filterCriteria",
                new SongFilterCriteria("New", null, null, PlayedStatus.All, SongSortCriteria.Title, false));
            display.SelectionChanged += (_, e) =>
            {
                if (e.SelectedSong == newNode)
                    throw new InvalidOperationException("selection callback failed");
            };

            Assert.Throws<TargetInvocationException>(() =>
                InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", failedSnapshot));

            // Nothing was ever applied, so the placeholder must be a real empty state:
            // the failed snapshot's filtered rows and preview root paths are cleared.
            Assert.Null(intake.AppliedSnapshot);
            Assert.Equal(0L, intake.CommittedVersion);
            Assert.Null(GetPrivateField<System.Collections.Generic.IReadOnlyList<FilteredSongResult>>(stage, "_filteredView"));
            Assert.False(GetPrivateField<bool>(stage, "_showEmptyFilterMessage"));
            Assert.Empty(preview.ActiveSongRootPaths);
            Assert.Empty(display.CurrentList);
            Assert.Empty(GetPrivateField<List<SongListNode>>(stage, "_currentSongList")!);

            // The stale-row route from the review: switch away and back to All Songs,
            // then the deferred repopulation runs — no failed row may reappear.
            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.RecentPlays);
            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");
            InvokePrivateMethod(stage, "SwitchToTab", SongSelectionTab.AllSongs);
            InvokePrivateMethod(stage, "RefreshSongListForActiveTab");

            Assert.DoesNotContain(newNode, display.CurrentList);
            Assert.Empty(display.CurrentList);
        }

        [Fact]
        public void ApplyPendingLibraryPublication_WhenAProjectionFailsBetweenDeliveries_ShouldRetryThroughTheRealScopeDrain()
        {
            var (stage, intake) = CreateStageWithIntake();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var priorSong = Score("Prior", 1, "/library/prior/prior.dtx");
            var newNode = Score("New", 2, "/library/new/new.dtx");
            var recoveredSong = Score("Recovered", 3, "/library/recovered/recovered.dtx");
            var display = new SongListDisplay();
            var faults = new List<Exception>();

            try
            {
                AttachCoreUi(stage, display: display);
                var scope = new StageBackgroundWorkScope(faults.Add);
                rootSongs.Clear();
                rootSongs.Add(priorSong);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/prior" });
                SetPrivateField(songManager, "_publicationVersion", 11L);

                // The stage's real publication handler bound to a real activation
                // scope: the library event only reports a version, the drain applies it.
                intake.Subscribe(scope, signal =>
                    InvokePrivateMethod(stage, "ApplyPendingLibraryPublication", signal));
                PublishLibrarySnapshot(songManager, 11, "/library/prior", priorSong);
                scope.Drain();

                Assert.Equal(2, display.CurrentList.Count);
                var appliedPrior = intake.AppliedSnapshot;
                Assert.Equal(11L, intake.CommittedVersion);

                // A projection fault on the next delivered publication: the drain
                // contains the fault (it reaches the scope's fault handler, never the
                // caller), nothing newer is acknowledged and the prior snapshot and
                // projection survive intact.
                display.SelectionChanged += (_, e) =>
                {
                    if (e.SelectedSong == newNode)
                        throw new InvalidOperationException("selection callback failed");
                };
                rootSongs.Clear();
                rootSongs.Add(newNode);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/new" });
                SetPrivateField(songManager, "_publicationVersion", 12L);
                PublishLibrarySnapshot(songManager, 12, "/library/new", newNode);
                scope.Drain();

                // The scope contains the fault. The reflection-based subscription wraps
                // it in a TargetInvocationException — a test-harness artifact; unwrap
                // it to prove the ORIGINAL reconciliation fault is what was reported.
                var reported = Assert.Single(faults);
                var fault = reported is TargetInvocationException wrapped
                    ? wrapped.InnerException
                    : reported;
                Assert.IsType<InvalidOperationException>(fault);
                Assert.Same(appliedPrior, intake.AppliedSnapshot);
                Assert.Equal(11L, intake.CommittedVersion);
                Assert.Same(priorSong, display.SelectedSong);

                // Recovery arrives as the next real delivery through the same scope.
                rootSongs.Clear();
                rootSongs.Add(recoveredSong);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/recovered" });
                SetPrivateField(songManager, "_publicationVersion", 13L);
                PublishLibrarySnapshot(songManager, 13, "/library/recovered", recoveredSong);
                scope.Drain();

                Assert.Equal(13L, intake.CommittedVersion);
                Assert.Equal(2, display.CurrentList.Count);
                Assert.Same(recoveredSong, display.CurrentList[0]);
                Assert.DoesNotContain(newNode, display.CurrentList);

                scope.Retire();
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
        public void ReconcileLibrarySnapshot_WhenARollbackSelectionNotificationFaults_ShouldStillRestoreTheFinalRowAndPresentation()
        {
            var (stage, intake) = CreateStageWithIntake();
            var priorSong = Score("Prior", 1, "/library/prior/BOX/prior.dtx");
            var priorBox = Box("BOX", "/library/prior/BOX", priorSong);
            var priorSnapshot = Snapshot(11, new[] { priorBox }, new[] { "/library/prior" });
            var newNode = Score("New", 2, "/library/new/new.dtx");
            var failedSnapshot = Snapshot(12, new[] { newNode }, new[] { "/library/new" });
            var display = new SongListDisplay();
            var preview = new PreviewImagePanel();
            var statusPanel = new SongStatusPanel();
            AttachCoreUi(stage, display: display, statusPanel: statusPanel, previewPanel: preview);
            SetPrivateField(stage, "_currentSongList", priorBox.Children);
            SetPrivateField(stage, "_currentBreadcrumb", "BOX");
            SetPrivateField(stage, "_selectedSong", priorSong);
            GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!.Push(
                new SongListNode { Children = new List<SongListNode> { priorBox }, Title = "" });
            display.CurrentList = new List<SongListNode>
            {
                new() { Type = NodeType.BackBox, Title = ".." },
                priorSong
            };
            display.SelectedIndex = 1;
            InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", priorSnapshot);

            // Production subscriber order: the stage's real selection handler runs
            // first (it overwrites _selectedSong with every notified row and drives
            // the preview/status presentation), then a persistently faulting
            // subscriber that throws on EVERY notification — including the row-zero
            // ('..') event the list refresh raises BEFORE the rollback reaches the
            // restored row.
            display.SelectionChanged += (sender, e) =>
                InvokePrivateMethod(stage, "OnSongSelectionChanged", sender, e);
            display.SelectionChanged += (_, _) =>
                throw new InvalidOperationException("selection callback failed");

            Assert.Throws<TargetInvocationException>(() =>
                InvokePrivateMethod(stage, "ReconcileLibrarySnapshot", failedSnapshot));

            // The rollback must still land on the final row and restore its presentation:
            // the display and stage selection sit on the prior song and the preview and
            // status panel present it, even though every selection notification faulted.
            Assert.Same(priorSnapshot, intake.AppliedSnapshot);
            Assert.Equal(11L, intake.CommittedVersion);
            Assert.Equal(1, display.SelectedIndex);
            Assert.Same(priorSong, display.SelectedSong);
            Assert.Same(priorSong, GetPrivateField<SongListNode>(stage, "_selectedSong"));
            Assert.Same(priorSong, GetPrivateField<SongListNode>(preview, "_currentSong"));
            Assert.True(statusPanel.Visible);
            Assert.Single(GetPrivateField<Stack<SongListNode>>(stage, "_navigationStack")!);
            Assert.Equal("BOX", GetPrivateField<string>(stage, "_currentBreadcrumb"));
            Assert.Same(priorBox.Children, GetPrivateField<List<SongListNode>>(stage, "_currentSongList"));
            Assert.Equal(new[] { "/library/prior" }, preview.ActiveSongRootPaths);
            Assert.DoesNotContain(newNode, display.CurrentList);
        }

        [Fact]
        public void ApplyPendingLibraryPublication_WhenTheSameVersionIsRedeliveredAfterAFailure_ShouldReconcileThroughTheRealScopeDrain()
        {
            var (stage, intake) = CreateStageWithIntake();
            var songManager = LibraryOf(stage);
            var rootSongs = GetPrivateField<List<SongListNode>>(songManager, "_rootSongs")!;
            var originalRoots = rootSongs.ToList();
            var originalActiveRoots = GetPrivateField<string[]>(songManager, "_currentSearchPaths")!;
            var originalVersion = GetPrivateField<long>(songManager, "_publicationVersion");
            var priorSong = Score("Prior", 1, "/library/prior/prior.dtx");
            var newNode = Score("New", 2, "/library/new/new.dtx");
            var display = new SongListDisplay();
            var faults = new List<Exception>();

            try
            {
                AttachCoreUi(stage, display: display);
                var scope = new StageBackgroundWorkScope(faults.Add);
                rootSongs.Clear();
                rootSongs.Add(priorSong);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/prior" });
                SetPrivateField(songManager, "_publicationVersion", 11L);

                intake.Subscribe(scope, signal =>
                    InvokePrivateMethod(stage, "ApplyPendingLibraryPublication", signal));
                PublishLibrarySnapshot(songManager, 11, "/library/prior", priorSong);
                scope.Drain();

                Assert.Equal(11L, intake.CommittedVersion);

                var failSelection = true;
                display.SelectionChanged += (_, e) =>
                {
                    if (failSelection && e.SelectedSong == newNode)
                        throw new InvalidOperationException("selection callback failed");
                };
                rootSongs.Clear();
                rootSongs.Add(newNode);
                SetPrivateField(songManager, "_currentSearchPaths", new[] { "/library/new" });
                SetPrivateField(songManager, "_publicationVersion", 12L);
                PublishLibrarySnapshot(songManager, 12, "/library/new", newNode);
                scope.Drain();

                Assert.Single(faults);
                Assert.Equal(11L, intake.CommittedVersion);
                Assert.Same(priorSong, display.SelectedSong);

                // The SAME version is re-delivered once the transient callback fault is
                // gone: the drain must neither drop it (a failed reconcile is never
                // acknowledged) nor acknowledge it without reconciling it first.
                failSelection = false;
                PublishLibrarySnapshot(songManager, 12, "/library/new", newNode);
                scope.Drain();

                Assert.Single(faults);
                Assert.Equal(12L, intake.CommittedVersion);
                Assert.Same(newNode, display.SelectedSong);
                Assert.Equal(2, display.CurrentList.Count);
                Assert.Same(newNode, display.CurrentList[0]);

                scope.Retire();
            }
            finally
            {
                rootSongs.Clear();
                rootSongs.AddRange(originalRoots);
                SetPrivateField(songManager, "_currentSearchPaths", originalActiveRoots);
                SetPrivateField(songManager, "_publicationVersion", originalVersion);
            }
        }

        private static void PublishLibrarySnapshot(
            SongLibrary library,
            long version,
            string activeRoot,
            SongListNode node)
        {
            // Raises the library's real publication event, exactly as the publisher
            // thread would (the intake's subscribed handler only reports the version).
            var handler = GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                library, "SongLibraryPublished");
            Assert.NotNull(handler);
            handler!.Invoke(null, new SongLibraryPublishedEventArgs(
                Snapshot(version, new[] { node }, new[] { activeRoot })));
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

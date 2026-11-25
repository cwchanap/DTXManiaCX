#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.Stage.SongSelection;
using Xunit;
using static DTXMania.Test.Stage.SongSelectionStageTestFactory;
using static DTXMania.Test.TestData.ReflectionHelpers;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.Stage
{
    // HPA-802 task 2: the passive library intake is exercised with constructor-built
    // objects — a real game-owned SongLibrary over a never-initialized temp database,
    // a real StageBackgroundWorkScope drained/retired by the test, and plain snapshots.
    // No stage internals and no background-queue reflection.
    [Trait("Category", "Unit")]
    public class SongSelectionLibraryIntakeTests
    {
        #region Subscription wiring

        [Fact]
        public void Subscribe_WhenScopeIsNull_ShouldNotSubscribeToTheLibrary()
        {
            var library = CreateLibrary();
            var probe = new List<long>();
            library.SongLibraryPublished += (_, e) => probe.Add(e.Snapshot.Version);
            var intake = new SongSelectionLibraryIntake(() => library);
            var reported = new List<long>();

            intake.Subscribe(null, reported.Add);

            Publish(library, Snapshot(7, Array.Empty<SongListNode>(), Array.Empty<string>()));
            Assert.Single(probe);              // the event fired on the library
            Assert.Empty(reported);            // but the intake never subscribed
        }

        [Fact]
        public void Subscribe_ShouldReportPublicationVersionsThroughTheScopeReporter()
        {
            var library = CreateLibrary();
            var intake = new SongSelectionLibraryIntake(() => library);
            var reported = new List<long>();
            var scope = new StageBackgroundWorkScope();

            intake.Subscribe(scope, reported.Add);
            Publish(library, Snapshot(7, Array.Empty<SongListNode>(), Array.Empty<string>()));
            Assert.Empty(reported);            // publisher thread: nothing applied yet

            scope.Drain();
            Assert.Equal(new[] { 7L }, reported);
        }

        [Fact]
        public void Publish_WhenSeveralVersionsArriveBeforeOneDrain_ShouldReportOnlyTheMostRecentOnDrain()
        {
            var library = CreateLibrary();
            var intake = new SongSelectionLibraryIntake(() => library);
            var reported = new List<long>();
            var scope = new StageBackgroundWorkScope();

            intake.Subscribe(scope, reported.Add);
            Publish(library, Snapshot(20, Array.Empty<SongListNode>(), Array.Empty<string>()));
            Publish(library, Snapshot(22, Array.Empty<SongListNode>(), Array.Empty<string>()));
            Publish(library, Snapshot(21, Array.Empty<SongListNode>(), Array.Empty<string>()));

            // Multiple publications coalesce into one pending Latest signal on the
            // scope; a single drain reports only the most recent version. The stage
            // handler then fetches the coherent manager snapshot, which can be newer
            // still, and acknowledges the newer of the two (covered stage-side).
            scope.Drain();
            Assert.Equal(new[] { 21L }, reported);
        }

        [Fact]
        public void Publish_WhenScopeRetires_ShouldDropTheSignal()
        {
            var library = CreateLibrary();
            var intake = new SongSelectionLibraryIntake(() => library);
            var reported = new List<long>();
            var scope = new StageBackgroundWorkScope();
            intake.Subscribe(scope, reported.Add);

            scope.Retire();
            Publish(library, Snapshot(45, Array.Empty<SongListNode>(), Array.Empty<string>()));

            scope.Drain();
            Assert.Empty(reported);
        }

        [Fact]
        public void Subscribe_AfterRetire_ShouldRearmAgainstTheActualLibraryAndDropTheRetainedHandlerSignal()
        {
            var library = CreateLibrary();
            var intake = new SongSelectionLibraryIntake(() => library);
            var reportedByA = new List<long>();
            var reportedByB = new List<long>();
            var scopeA = new StageBackgroundWorkScope();
            intake.Subscribe(scopeA, reportedByA.Add);

            // A publisher thread may retain the handler captured for activation A.
            var handlerA = Assert.IsType<EventHandler<SongLibraryPublishedEventArgs>>(
                GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(library, "SongLibraryPublished"));

            scopeA.Retire();
            handlerA.Invoke(null, new SongLibraryPublishedEventArgs(Snapshot(74, Array.Empty<SongListNode>(), Array.Empty<string>())));
            scopeA.Drain();
            Assert.Empty(reportedByA);         // the retired reporter drops the signal

            // A new activation re-arms against the same library with a fresh scope.
            var scopeB = new StageBackgroundWorkScope();
            intake.Subscribe(scopeB, reportedByB.Add);
            Publish(library, Snapshot(75, Array.Empty<SongListNode>(), Array.Empty<string>()));
            scopeB.Drain();
            Assert.Equal(new[] { 75L }, reportedByB);

            // Regression: the retained A-handler fires AFTER re-arming. It must reach
            // only A's retired reporter and drop — never enqueue into the new scope.
            handlerA.Invoke(null, new SongLibraryPublishedEventArgs(Snapshot(74, Array.Empty<SongListNode>(), Array.Empty<string>())));
            scopeB.Drain();
            Assert.Equal(new[] { 75L }, reportedByB);
        }

        [Fact]
        public void Unsubscribe_ShouldDetachTheHandlerFromTheActuallySubscribedLibraryInstance()
        {
            var libraryA = CreateLibrary();
            var libraryB = CreateLibrary();
            SongLibrary current = libraryA;
            var intake = new SongSelectionLibraryIntake(() => current);
            var reported = new List<long>();
            var scope = new StageBackgroundWorkScope();
            intake.Subscribe(scope, reported.Add);

            // The getter now hands out a different library; Unsubscribe must still detach
            // from A — the instance actually subscribed — never from a fresh lookup
            // (unsubscribing B instead would leave A's handler registered).
            current = libraryB;
            intake.Unsubscribe();

            Assert.Null(GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                libraryA, "SongLibraryPublished"));
            Assert.Empty(reported);
        }

        [Fact]
        public void Unsubscribe_WhenCalledRepeatedlyOrBeforeSubscribe_ShouldBeIdempotent()
        {
            var library = CreateLibrary();
            var intake = new SongSelectionLibraryIntake(() => library);

            intake.Unsubscribe();
            intake.Subscribe(new StageBackgroundWorkScope(), _ => { });
            intake.Unsubscribe();
            intake.Unsubscribe();

            // No handler must remain registered on the library.
            Assert.Null(GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                library, "SongLibraryPublished"));
        }

        #endregion

        #region Snapshot fetch and version acknowledgment

        [Fact]
        public void TryGetNewer_WhenNothingIsInstalled_ShouldAcceptTheFirstSnapshotEvenAtVersionZero()
        {
            var library = CreateLibrary();
            var intake = new SongSelectionLibraryIntake(() => library);

            // A freshly constructed library publishes version zero; the initializer poll
            // (signal 0) must accept it so first initialization cannot stall.
            var snapshot = intake.TryGetNewer(0);

            Assert.NotNull(snapshot);
            Assert.Equal(0L, snapshot!.Version);
            Assert.Equal(0L, intake.CommittedVersion);   // fetch does not acknowledge
        }

        [Fact]
        public void TryGetNewer_WhenSignalOrSnapshotExceedsCommittedVersion_ShouldReturnTheSnapshot()
        {
            var library = CreateLibrary();
            SetManagerState(library, publicationVersion: 12, activeRoots: new[] { "/library/new" });
            var intake = new SongSelectionLibraryIntake(() => library);
            intake.Install(Snapshot(10, Array.Empty<SongListNode>(), Array.Empty<string>()));
            intake.Commit(10);

            Assert.NotNull(intake.TryGetNewer(11));   // snapshot version 12 wins
        }

        [Fact]
        public void TryGetNewer_WhenSignalAndSnapshotAreNotNewer_ShouldReturnNull()
        {
            var library = CreateLibrary();
            SetManagerState(library, publicationVersion: 50, activeRoots: new[] { "/library/old" });
            var intake = new SongSelectionLibraryIntake(() => library);
            intake.Install(Snapshot(50, Array.Empty<SongListNode>(), Array.Empty<string>()));
            intake.Commit(50);

            Assert.Null(intake.TryGetNewer(31));      // stale (already-applied) signal
            Assert.Null(intake.TryGetNewer(50));      // current signal, no reapply
        }

        [Fact]
        public void TryGetNewer_WhenOnlyTheSignalExceedsCommittedVersion_ShouldStillReturnTheOlderFetchedSnapshot()
        {
            var library = CreateLibrary();
            SetManagerState(library, publicationVersion: 31, activeRoots: new[] { "/library/new" });
            var intake = new SongSelectionLibraryIntake(() => library);
            intake.Install(Snapshot(30, Array.Empty<SongListNode>(), Array.Empty<string>()));
            intake.Commit(30);

            // Signal 33 exceeds the committed 30, so reconciliation proceeds against the
            // fetched (older, v31) snapshot; the acknowledgment below raises the marker.
            var snapshot = intake.TryGetNewer(33);
            Assert.NotNull(snapshot);
            Assert.Equal(31L, snapshot!.Version);
            Assert.Equal(30L, intake.CommittedVersion);   // still not acknowledged at fetch
        }

        [Fact]
        public void TryGetNewer_WhenInitializerPollFindsOnlyAnOlderSnapshot_ShouldReturnNull()
        {
            var library = CreateLibrary();
            SetManagerState(library, publicationVersion: 50, activeRoots: new[] { "/library/old" });
            var intake = new SongSelectionLibraryIntake(() => library);
            intake.Install(Snapshot(51, Array.Empty<SongListNode>(), Array.Empty<string>()));
            intake.Commit(51);

            // A newer publication was already applied; the stale initializer read (v50)
            // must not overwrite the restored navigation/selection.
            Assert.Null(intake.TryGetNewer(0));
        }

        [Fact]
        public void TryGetNewer_WhenInitializerPollFindsANewerSnapshot_ShouldReturnIt()
        {
            var library = CreateLibrary();
            SetManagerState(library, publicationVersion: 82, activeRoots: new[] { "/library/shared" });
            var intake = new SongSelectionLibraryIntake(() => library);
            intake.Install(Snapshot(81, Array.Empty<SongListNode>(), Array.Empty<string>()));
            intake.Commit(81);

            var snapshot = intake.TryGetNewer(0);
            Assert.NotNull(snapshot);
            Assert.Equal(82L, snapshot!.Version);
        }

        [Fact]
        public void Commit_ShouldAdvanceOnlyMonotonically()
        {
            var intake = new SongSelectionLibraryIntake(CreateLibrary);

            intake.Commit(31);
            intake.Commit(20);                        // late signal can never lower the marker

            Assert.Equal(31L, intake.CommittedVersion);
            intake.Commit(33);
            Assert.Equal(33L, intake.CommittedVersion);
        }

        [Fact]
        public void Install_ShouldRetainTheCoherentSnapshotUsedForUiProjection()
        {
            var intake = new SongSelectionLibraryIntake(CreateLibrary);
            var song = Score("Snapshot Song", 91, "/library/active/song.dtx");
            var snapshot = Snapshot(91, new[] { song }, new[] { "/library/active" });

            intake.Install(snapshot);
            intake.Commit(snapshot.Version);

            Assert.Same(snapshot, intake.AppliedSnapshot);
            Assert.Equal(91L, intake.CommittedVersion);
            Assert.Equal(new[] { "/library/active" }, intake.AppliedActiveRoots);
            Assert.Equal(SongSelectionLibraryEmptyState.HasSongs, intake.EmptyState);
        }

        [Fact]
        public void Install_WhenSnapshotIsNull_ShouldThrowArgumentNullException()
        {
            var intake = new SongSelectionLibraryIntake(CreateLibrary);

            Assert.Throws<ArgumentNullException>(() => intake.Install(null!));
        }

        [Fact]
        public void Rollback_WhenAPriorSnapshotWasApplied_ShouldReinstallItsDerivedState()
        {
            var intake = new SongSelectionLibraryIntake(CreateLibrary);
            var prior = Snapshot(11, Array.Empty<SongListNode>(), Array.Empty<string>());
            var failedSong = Score("Failed", 12, "/library/new/failed.dtx");
            var failed = Snapshot(12, new[] { failedSong }, new[] { "/library/new" });

            intake.Install(prior);
            intake.Commit(prior.Version);
            intake.Install(failed); // the failed reconciliation had already replaced the applied snapshot

            intake.Rollback(prior);

            Assert.Same(prior, intake.AppliedSnapshot);
            Assert.Empty(intake.AppliedActiveRoots!);
            Assert.Equal(SongSelectionLibraryEmptyState.NoActiveRoots, intake.EmptyState);
            Assert.Equal(11L, intake.CommittedVersion);
        }

        [Fact]
        public void Rollback_WhenNoSnapshotWasAppliedBefore_ShouldClearTheFailedInstallButKeepTheCommittedVersion()
        {
            var intake = new SongSelectionLibraryIntake(CreateLibrary);
            var song = Score("Failed", 12, "/library/active/failed.dtx");
            intake.Install(Snapshot(12, new[] { song }, new[] { "/library/active" }));
            intake.Commit(9); // an earlier acknowledged version — rollback never moves the marker

            intake.Rollback(null);

            Assert.Null(intake.AppliedSnapshot);
            Assert.Null(intake.AppliedActiveRoots);
            Assert.Equal(SongSelectionLibraryEmptyState.HasSongs, intake.EmptyState);
            Assert.Equal(9L, intake.CommittedVersion);

            // The permissive no-snapshot projection seam stays available after clearing.
            Assert.Equal(new[] { song }, intake.Filter(new[] { song }));
        }

        #endregion

        #region Accessors and reset

        [Fact]
        public void AppliedActiveRoots_WhenNoSnapshotIsInstalled_ShouldBeNull()
        {
            var intake = new SongSelectionLibraryIntake(CreateLibrary);

            Assert.Null(intake.AppliedActiveRoots);
        }

        [Fact]
        public void AppliedActiveRoots_WhenSnapshotIsInstalled_ShouldReturnTheSnapshotRoots()
        {
            var intake = new SongSelectionLibraryIntake(CreateLibrary);
            intake.Install(Snapshot(31, Array.Empty<SongListNode>(), new[] { "/library/one", "/library/two" }));

            Assert.Equal(new[] { "/library/one", "/library/two" }, intake.AppliedActiveRoots);
        }

        [Fact]
        public void Reset_ShouldClearTheAppliedSnapshotAndVersionBackToActivationDefaults()
        {
            var library = CreateLibrary();
            var intake = new SongSelectionLibraryIntake(() => library);
            var reported = new List<long>();
            var scope = new StageBackgroundWorkScope();
            intake.Subscribe(scope, reported.Add);
            intake.Install(Snapshot(50, new[] { Score("S", 1, "/library/s.dtx") }, new[] { "/library" }));
            intake.Commit(50);

            intake.Reset();

            Assert.Null(intake.AppliedSnapshot);
            Assert.Equal(0L, intake.CommittedVersion);
            Assert.Equal(SongSelectionLibraryEmptyState.HasSongs, intake.EmptyState);
            Assert.Null(GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                library, "SongLibraryPublished"));   // also unsubscribes
        }

        #endregion

        #region Eligibility and empty classification (plain snapshots)

        [Fact]
        public void FilterNodesForActiveRoots_WhenNodesAreNull_ShouldReturnEmptyList()
        {
            var result = SongSelectionLibraryIntake.FilterNodesForActiveRoots(null, new[] { "/songs" });

            Assert.Empty(result);
        }

        [Fact]
        public void FilterNodesForActiveRoots_WhenActiveRootsAreNull_ShouldReturnAllNodes()
        {
            var song = Score("Test", 1, "/songs/test.dtx");

            var result = SongSelectionLibraryIntake.FilterNodesForActiveRoots(new[] { song }, null);

            Assert.Single(result);
            Assert.Same(song, result[0]);
        }

        [Fact]
        public void FilterNodesForActiveRoots_WhenActiveRootsAreEmpty_ShouldReturnEmptyList()
        {
            var song = Score("Test", 1, "/songs/test.dtx");

            var result = SongSelectionLibraryIntake.FilterNodesForActiveRoots(
                new[] { song }, Array.Empty<string>());

            Assert.Empty(result);
        }

        [Fact]
        public void FilterNodesForActiveRoots_WhenRootsDoNotNormalize_ShouldReturnEmptyList()
        {
            var song = Score("Test", 1, "/songs/test.dtx");

            // A NUL character makes SongPathIdentity.TryNormalize fail, so no roots
            // normalize and the filtered list is empty.
            var result = SongSelectionLibraryIntake.FilterNodesForActiveRoots(
                new[] { song }, new[] { "/bad\0root" });

            Assert.Empty(result);
        }

        [Fact]
        public void FilterNodesForActiveRoots_WhenSongIsUnderActiveRoot_ShouldIncludeIt()
        {
            var song = Score("Test", 1, "/library/active/test.dtx");

            var result = SongSelectionLibraryIntake.FilterNodesForActiveRoots(
                new[] { song }, new[] { "/library/active" });

            Assert.Single(result);
            Assert.Same(song, result[0]);
        }

        [Fact]
        public void FilterNodesForActiveRoots_WhenSongIsOutsideActiveRoot_ShouldExcludeIt()
        {
            var active = Score("Active", 1, "/library/active/active.dtx");
            var inactive = Score("Inactive", 2, "/library/inactive/inactive.dtx");

            var result = SongSelectionLibraryIntake.FilterNodesForActiveRoots(
                new[] { active, inactive }, new[] { "/library/active" });

            Assert.Single(result);
            Assert.Same(active, result[0]);
        }

        [Fact]
        public void Filter_WhenNoSnapshotIsInstalled_ShouldReturnRootFilteredNodes()
        {
            var intake = new SongSelectionLibraryIntake(CreateLibrary);
            var song = Score("Test", 1, "/songs/test.dtx");

            // Without an applied snapshot, active roots are null, so the permissive
            // pre-projection seam returns all nodes.
            var result = intake.Filter(new[] { song });

            Assert.Single(result);
            Assert.Same(song, result[0]);
        }

        [Fact]
        public void Filter_WhenSnapshotHasNoScoreIdentities_ShouldReturnEmptyList()
        {
            var intake = new SongSelectionLibraryIntake(CreateLibrary);
            var song = Score("Test", 1, "/library/active/test.dtx");
            // A snapshot with only boxes (no score nodes) has an empty identity set.
            intake.Install(Snapshot(30, new[] { Box("Empty", "/library/active/Empty") }, new[] { "/library/active" }));

            var result = intake.Filter(new[] { song });

            Assert.Empty(result);
        }

        [Fact]
        public void Filter_WhenChartLeavesAndReentersThePublishedLibrary_ShouldTrackEligibilityWithoutOrderDependency()
        {
            var intake = new SongSelectionLibraryIntake(CreateLibrary);
            var retained = Score("Retained", 1, "/library/active/retained.dtx");
            var removed = Score("Removed", 2, "/library/active/removed.dtx");

            intake.Install(Snapshot(32, new[] { retained }, new[] { "/library/active" }));
            Assert.Equal(new[] { retained }, intake.Filter(new[] { retained, removed }));

            // The root (and its chart) is re-published: the row becomes eligible again.
            intake.Install(Snapshot(33, new[] { retained, removed }, new[] { "/library/active" }));
            Assert.Equal(new[] { retained, removed }, intake.Filter(new[] { retained, removed }));
        }

        [Fact]
        public void GetPublishedScoreIdentities_WhenTreeContainsScores_ShouldCollectSongAndChartIdentities()
        {
            var song = Score("Test", 7, "/library/active/test.dtx");
            var box = Box("Group", "/library/active/Group", song);

            var identities = SongSelectionLibraryIntake.GetPublishedScoreIdentities(new[] { box });

            Assert.Contains("song:7", identities);
            Assert.Contains($"chart:{NormalizePath("/library/active/test.dtx")}", identities);
        }

        [Fact]
        public void GetScoreIdentityCandidates_WhenNodeIsNotAScore_ShouldYieldNoIdentities()
        {
            var box = Box("Folder", "/library/active/Folder");

            Assert.Empty(SongSelectionLibraryIntake.GetScoreIdentityCandidates(box));
        }

        [Fact]
        public void GetScoreIdentityCandidates_WhenScoreWithDatabaseId_ShouldYieldSongAndChartIdentities()
        {
            var node = Score("Song", 42, "/library/song.dtx");

            var candidates = SongSelectionLibraryIntake.GetScoreIdentityCandidates(node).ToList();

            Assert.Contains("song:42", candidates);
            Assert.Contains($"chart:{NormalizePath("/library/song.dtx")}", candidates);
        }

        [Fact]
        public void GetScoreIdentityCandidates_WhenScoreWithoutDatabaseId_ShouldYieldOnlyChartIdentity()
        {
            var chart = new SongChart { FilePath = "/library/song.dtx" };
            var node = new SongListNode { Type = NodeType.Score, DatabaseChart = chart };

            var candidates = SongSelectionLibraryIntake.GetScoreIdentityCandidates(node).ToList();

            Assert.Single(candidates);
            Assert.Equal($"chart:{NormalizePath("/library/song.dtx")}", candidates[0]);
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

            var candidates = SongSelectionLibraryIntake.GetScoreIdentityCandidates(node).ToList();

            // The chart path is yielded from both DatabaseChart and the song's Charts
            // collection, producing two identical chart: identities.
            Assert.Equal(2, candidates.Count);
            Assert.All(candidates, c => Assert.StartsWith("chart:", c));
        }

        [Fact]
        public void HasPublishedScoreIdentity_WhenIdentityIsPublished_ShouldReturnTrue()
        {
            var node = Score("Song", 42, "/library/song.dtx");
            var identities = new HashSet<string>(StringComparer.Ordinal) { "song:42" };

            Assert.True(SongSelectionLibraryIntake.HasPublishedScoreIdentity(node, identities));
        }

        [Fact]
        public void HasPublishedScoreIdentity_WhenIdentityIsNotPublished_ShouldReturnFalse()
        {
            var node = Score("Song", 42, "/library/song.dtx");
            var identities = new HashSet<string>(StringComparer.Ordinal) { "song:99" };

            Assert.False(SongSelectionLibraryIntake.HasPublishedScoreIdentity(node, identities));
        }

        [Fact]
        public void GetNodeChartPaths_WhenNodeHasDatabaseChart_ShouldReturnChartPath()
        {
            var chart = new SongChart { FilePath = "/library/song.dtx" };
            var node = new SongListNode { DatabaseChart = chart };

            var paths = SongSelectionLibraryIntake.GetNodeChartPaths(node).ToList();

            Assert.Single(paths);
            Assert.Equal("/library/song.dtx", paths[0]);
        }

        [Fact]
        public void GetNodeChartPaths_WhenNodeHasSongWithMultipleCharts_ShouldReturnAllChartPaths()
        {
            var song = new SongEntity
            {
                Charts = new List<SongChart>
                {
                    new() { FilePath = "/library/song1.dtx" },
                    new() { FilePath = "/library/song2.dtx" },
                },
            };
            var node = new SongListNode { DatabaseSong = song };

            var paths = SongSelectionLibraryIntake.GetNodeChartPaths(node).ToList();

            Assert.Equal(2, paths.Count);
        }

        [Fact]
        public void GetNodeChartPaths_WhenNodeHasOnlyDirectoryPath_ShouldReturnDirectoryPath()
        {
            var node = new SongListNode { DirectoryPath = "/library/box" };

            var paths = SongSelectionLibraryIntake.GetNodeChartPaths(node).ToList();

            Assert.Single(paths);
            Assert.Equal("/library/box", paths[0]);
        }

        [Fact]
        public void NodeIsUnderAnyActiveRoot_WhenChartUnderRoot_ShouldReturnTrue()
        {
            var node = Score("Song", 1, "/library/songs/song.dtx");

            Assert.True(SongSelectionLibraryIntake.NodeIsUnderAnyActiveRoot(node, new[] { "/library/songs" }));
        }

        [Fact]
        public void NodeIsUnderAnyActiveRoot_WhenChartOutsideRoots_ShouldReturnFalse()
        {
            var node = Score("Song", 1, "/other/song.dtx");

            Assert.False(SongSelectionLibraryIntake.NodeIsUnderAnyActiveRoot(node, new[] { "/library/songs" }));
        }

        [Fact]
        public void NodeIsUnderAnyActiveRoot_WhenChartPathCannotBeNormalized_ShouldSkipThatPath()
        {
            var chart = new SongChart { FilePath = "/bad\0path" };
            var node = new SongListNode
            {
                Type = NodeType.Score,
                DatabaseChart = chart,
                DirectoryPath = "/library/songs/fallback",
            };

            Assert.True(SongSelectionLibraryIntake.NodeIsUnderAnyActiveRoot(node, new[] { "/library/songs" }));
        }

        [Fact]
        public void ContainsPublishedScore_WhenTreeHasScore_ShouldReturnTrue()
        {
            var song = Score("Song", 1, "/song.dtx");
            var box = Box("BOX", "/library/BOX", song);

            Assert.True(SongSelectionLibraryIntake.ContainsPublishedScore(new[] { box }));
        }

        [Fact]
        public void ContainsPublishedScore_WhenTreeOnlyHasEmptyBoxes_ShouldReturnFalse()
        {
            var box = Box("BOX", "/library/BOX");

            Assert.False(SongSelectionLibraryIntake.ContainsPublishedScore(new[] { box }));
        }

        [Fact]
        public void ContainsPublishedScore_WhenNodesAreEmpty_ShouldReturnFalse()
        {
            Assert.False(SongSelectionLibraryIntake.ContainsPublishedScore(
                Array.Empty<SongListNode>()));
        }

        [Fact]
        public void ResolveLibraryEmptyState_ShouldDistinguishNoRootsFromRootsWithoutSupportedCharts()
        {
            var noRoots = Snapshot(24, Array.Empty<SongListNode>(), Array.Empty<string>());
            var noSupportedCharts = Snapshot(25, Array.Empty<SongListNode>(), new[] { "/library/active" });

            Assert.Equal(SongSelectionLibraryEmptyState.NoActiveRoots,
                SongSelectionLibraryIntake.ResolveLibraryEmptyState(noRoots));
            Assert.Equal(SongSelectionLibraryEmptyState.NoSupportedCharts,
                SongSelectionLibraryIntake.ResolveLibraryEmptyState(noSupportedCharts));
        }

        [Fact]
        public void ResolveLibraryEmptyState_WhenRootsContainScores_ShouldReturnHasSongs()
        {
            var song = Score("Test", 1, "/library/active/test.dtx");
            var snapshot = Snapshot(50, new[] { song }, new[] { "/library/active" });

            Assert.Equal(SongSelectionLibraryEmptyState.HasSongs,
                SongSelectionLibraryIntake.ResolveLibraryEmptyState(snapshot));
        }

        [Fact]
        public void Install_ShouldClassifyEmptyStatesFromTheSnapshot()
        {
            var intake = new SongSelectionLibraryIntake(CreateLibrary);

            intake.Install(Snapshot(1, Array.Empty<SongListNode>(), Array.Empty<string>()));
            Assert.Equal(SongSelectionLibraryEmptyState.NoActiveRoots, intake.EmptyState);

            intake.Install(Snapshot(2, Array.Empty<SongListNode>(), new[] { "/library/active" }));
            Assert.Equal(SongSelectionLibraryEmptyState.NoSupportedCharts, intake.EmptyState);

            intake.Install(Snapshot(3, new[] { Score("S", 1, "/library/active/s.dtx") }, new[] { "/library/active" }));
            Assert.Equal(SongSelectionLibraryEmptyState.HasSongs, intake.EmptyState);
        }

        #endregion

        #region Helpers

        private static SongLibrary CreateLibrary()
        {
            var dbPath = Path.Combine(
                Path.GetTempPath(), "DTXManiaCX_Tests", "SongSelectionLibraryIntakeTests",
                $"{Guid.NewGuid():N}.db");
            return new SongLibrary(new SongDatabase(dbPath));
        }

        private static void SetManagerState(SongLibrary library, long publicationVersion, string[] activeRoots)
        {
            SetPrivateField(library, "_publicationVersion", publicationVersion);
            SetPrivateField(library, "_currentSearchPaths", activeRoots);
        }

        // Raises the library's real publication event, exactly as the publisher thread would.
        private static void Publish(SongLibrary library, SongLibrarySnapshot snapshot)
        {
            var handler = GetPrivateField<EventHandler<SongLibraryPublishedEventArgs>>(
                library, "SongLibraryPublished");
            Assert.NotNull(handler);   // a subscriber must be registered for this to be meaningful
            handler!.Invoke(null, new SongLibraryPublishedEventArgs(snapshot));
        }

        private static string NormalizePath(string path) =>
            SongPathIdentity.TryNormalize(path, out var normalized) ? normalized : path;

        private static SongLibrarySnapshot Snapshot(
            long version,
            IReadOnlyList<SongListNode> roots,
            IReadOnlyList<string> activeRoots) =>
            new(version, roots, activeRoots, enumeratedFileCount: roots.Count, discoveredScoreCount: roots.Count);

        #endregion
    }
}

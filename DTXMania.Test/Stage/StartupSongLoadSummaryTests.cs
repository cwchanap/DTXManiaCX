using System;
using DTXMania.Game.Lib.Stage;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class StartupSongLoadSummaryTests
    {
        [Fact]
        public void Format_ShouldProduceOneInvariantMachineReadableLine()
        {
            var summary = new StartupSongLoadSummary(
                StartupSongLoadPath.Enumeration,
                StartupSongLoadOutcome.Success,
                TimeSpan.FromMilliseconds(1250),
                TimeSpan.FromMilliseconds(100),
                TimeSpan.FromMilliseconds(700),
                TimeSpan.FromMilliseconds(300),
                TimeSpan.FromMilliseconds(25),
                TimeSpan.FromMilliseconds(75),
                discoveredCharts: 100,
                parsedCharts: 98,
                logicalGroups: 27,
                added: 98,
                updated: 0,
                preserved: 0,
                skipped: 2,
                conflicts: 0,
                stale: 0,
                error: null);

            Assert.Equal(
                "HPA192_STARTUP path=enumeration outcome=success total_ms=1250 db_init_ms=100 " +
                "discovery_parse_ms=700 persistence_ms=300 cleanup_ms=25 hierarchy_ms=75 " +
                "discovered=100 parsed=98 groups=27 added=98 updated=0 preserved=0 " +
                "skipped=2 conflicts=0 stale=0 error=none",
                summary.Format());
        }

        [Fact]
        public void Format_ShouldSanitizeFailureTextOntoOneLine()
        {
            var summary = StartupSongLoadSummary.Failed(
                StartupSongLoadPath.Enumeration,
                TimeSpan.FromMilliseconds(20),
                "SQLite write\nfailed");

            var text = summary.Format();

            Assert.DoesNotContain('\n', text);
            Assert.Contains("outcome=failure", text);
            Assert.Contains("error=SQLite_write_failed", text);
        }
    }
}

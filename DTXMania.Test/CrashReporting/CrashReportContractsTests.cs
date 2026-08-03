using DTXMania.Game.Lib.Diagnostics.CrashReporting;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashReportContractsTests
{
    [Fact]
    public void EmptyInbox_ShouldReturnNoReportsAndRejectMutationsWithoutThrowing()
    {
        var inbox = EmptyCrashReportInbox.Instance;

        Assert.Empty(inbox.GetReports());
        Assert.False(inbox.TryAcknowledge("missing", out var acknowledgeError));
        Assert.Equal("report_not_found", acknowledgeError);
        Assert.False(inbox.TryDelete("missing", out var deleteError));
        Assert.Equal("report_not_found", deleteError);
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Diagnostics.CrashReporting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace DTXMania.Test.CrashReporting;

[Trait("Category", "Unit")]
public sealed class CrashReportContractsTests
{
    [Fact]
    public void EmptyCrashReportInbox_GetReports_ShouldReturnEmptyList()
    {
        Assert.Empty(EmptyCrashReportInbox.Instance.GetReports());
    }

    [Fact]
    public void EmptyCrashReportInbox_OpenGitHubIssue_ShouldSucceed()
    {
        Assert.True(EmptyCrashReportInbox.Instance.OpenGitHubIssue("any-id").Succeeded);
    }

    [Fact]
    public void EmptyCrashReportInbox_OpenReportFolder_ShouldSucceed()
    {
        Assert.True(EmptyCrashReportInbox.Instance.OpenReportFolder("any-id").Succeeded);
    }

    [Fact]
    public void EmptyCrashReportInbox_Dismiss_ShouldSucceed()
    {
        Assert.True(EmptyCrashReportInbox.Instance.Dismiss("any-id").Succeeded);
    }

    [Fact]
    public void EmptyCrashReportInbox_Delete_ShouldSucceed()
    {
        Assert.True(EmptyCrashReportInbox.Instance.Delete("any-id").Succeeded);
    }

    [Fact]
    public void EmptyCrashReportInbox_Instance_ShouldBeSingleton()
    {
        Assert.Same(EmptyCrashReportInbox.Instance, EmptyCrashReportInbox.Instance);
    }

    [Fact]
    public void EmptyCrashBreadcrumbSink_Record_ShouldNotThrow()
    {
        var exception = Record.Exception(() =>
            EmptyCrashBreadcrumbSink.Instance.Record("event"));

        Assert.Null(exception);
    }

    [Fact]
    public void EmptyCrashBreadcrumbSink_RecordWithProperties_ShouldNotThrow()
    {
        var exception = Record.Exception(() =>
            EmptyCrashBreadcrumbSink.Instance.Record(
                "event",
                new Dictionary<string, object?> { ["key"] = "value" }));

        Assert.Null(exception);
    }

    [Fact]
    public void EmptyCrashBreadcrumbSink_Instance_ShouldBeSingleton()
    {
        Assert.Same(EmptyCrashBreadcrumbSink.Instance, EmptyCrashBreadcrumbSink.Instance);
    }

    [Fact]
    public void EmptyCrashContextSink_SetSnapshot_ShouldNotThrow()
    {
        var exception = Record.Exception(() =>
            EmptyCrashContextSink.Instance.SetSnapshot(
                new CrashContextSnapshot(
                    CrashContextKind.Process,
                    CrashContextStatus.Available,
                    new Dictionary<string, object?>())));

        Assert.Null(exception);
    }

    [Fact]
    public void EmptyCrashContextSink_Instance_ShouldBeSingleton()
    {
        Assert.Same(EmptyCrashContextSink.Instance, EmptyCrashContextSink.Instance);
    }

    [Fact]
    public void EmptyCrashSensitiveDataSink_RegisterPath_ShouldNotThrow()
    {
        var exception = Record.Exception(() =>
            EmptyCrashSensitiveDataSink.Instance.RegisterPath("/some/path"));

        Assert.Null(exception);
    }

    [Fact]
    public void EmptyCrashSensitiveDataSink_RegisterPathWithNull_ShouldNotThrow()
    {
        var exception = Record.Exception(() =>
            EmptyCrashSensitiveDataSink.Instance.RegisterPath(null));

        Assert.Null(exception);
    }

    [Fact]
    public void EmptyCrashSensitiveDataSink_RegisterSecret_ShouldNotThrow()
    {
        var exception = Record.Exception(() =>
            EmptyCrashSensitiveDataSink.Instance.RegisterSecret("secret"));

        Assert.Null(exception);
    }

    [Fact]
    public void EmptyCrashSensitiveDataSink_RegisterSecretWithNull_ShouldNotThrow()
    {
        var exception = Record.Exception(() =>
            EmptyCrashSensitiveDataSink.Instance.RegisterSecret(null));

        Assert.Null(exception);
    }

    [Fact]
    public void EmptyCrashSensitiveDataSink_Instance_ShouldBeSingleton()
    {
        Assert.Same(EmptyCrashSensitiveDataSink.Instance, EmptyCrashSensitiveDataSink.Instance);
    }

    [Fact]
    public void IGameCrashDiagnostics_DefaultCrashReportInbox_ShouldBeEmptyFacade()
    {
        // The default interface implementation returns the null-object inbox facade.
        IGameCrashDiagnostics diagnostics = new TestDiagnostics();

        Assert.Same(EmptyCrashReportInbox.Instance, diagnostics.CrashReportInbox);
    }

    private sealed class TestDiagnostics : IGameCrashDiagnostics
    {
        public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;
        public ICrashBreadcrumbSink Breadcrumbs => EmptyCrashBreadcrumbSink.Instance;
        public ICrashContextSink Contexts => EmptyCrashContextSink.Instance;
        public ICrashSensitiveDataSink SensitiveData => EmptyCrashSensitiveDataSink.Instance;
    }
}

#nullable enable

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage.Config;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Moq;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public sealed class SongFolderPanelTests
{
    [Fact]
    public void Activate_ShouldCopyConfiguredRootsIntoAnIsolatedDraft()
    {
        using var root = TemporaryDirectory.Create();
        var configuredRoots = new List<string> { root.Path };
        var panel = CreatePanel(configuredRoots);

        panel.Activate();
        configuredRoots[0] = "changed-after-activation";

        Assert.Equal(new[] { root.Path }, panel.DraftRoots);
        Assert.DoesNotContain(typeof(SongFolderPanel).GetFields(
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic),
            field => typeof(IConfigManager).IsAssignableFrom(field.FieldType));
    }

    [Fact]
    public void AddFolder_WhenPickerSelectsValidFolder_ShouldAppendItToDraft()
    {
        using var first = TemporaryDirectory.Create();
        using var second = TemporaryDirectory.Create();
        var picker = new QueuedFolderPicker(FolderPickerResult.Selected(second.Path));
        var panel = CreatePanel(new[] { first.Path }, picker);
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        DrainPicker(panel);

        Assert.Equal(new[] { first.Path, second.Path }, panel.DraftRoots);
        Assert.Equal(first.Path, picker.InitialDirectories.Single());
        Assert.True(panel.IsActive);
    }

    [Fact]
    public void Remove_WhenOnlyOneDraftRootRemains_ShouldKeepTheRequiredRoot()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { root.Path });
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 1);

        Assert.Equal(new[] { root.Path }, panel.DraftRoots);
        Assert.True(panel.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(panel.StatusMessage));
    }

    [Fact]
    public void MoveActions_ShouldReorderTheSelectedDraftRoot()
    {
        using var first = TemporaryDirectory.Create();
        using var second = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { first.Path, second.Path });
        panel.Activate();

        Press(panel, Keys.Down); // select the second root
        ActivateActionFromCurrent(panel, pressesToAction: 3); // Move Up
        Assert.Equal(new[] { second.Path, first.Path }, panel.DraftRoots);

        Press(panel, Keys.Down); // Move Down
        Press(panel, Keys.Enter);
        Assert.Equal(new[] { first.Path, second.Path }, panel.DraftRoots);
    }

    [Fact]
    public void MoveSelectedRoot_WhenAvailabilityWarningsChangeOrder_ShouldRefreshCachedStatusAndSeverity()
    {
        var firstMissingPath = Path.Combine(Path.GetTempPath(),
            $"dtxmania-panel-first-missing-{Guid.NewGuid():N}");
        var secondMissingPath = Path.Combine(Path.GetTempPath(),
            $"dtxmania-panel-second-missing-{Guid.NewGuid():N}");
        var panel = CreatePanel(new[] { firstMissingPath, secondMissingPath });
        panel.Activate();

        Assert.Contains(firstMissingPath, panel.StatusMessage);

        Press(panel, Keys.Down); // select the second root
        ActivateActionFromCurrent(panel, pressesToAction: 3); // Move Up

        Assert.Equal(new[] { secondMissingPath, firstMissingPath }, panel.DraftRoots);
        var statusMessage = Assert.IsType<string>(panel.StatusMessage);
        Assert.Contains(secondMissingPath, statusMessage);

        var font = new Mock<IFont>();
        font.Setup(value => value.MeasureString(It.IsAny<string>())).Returns(Vector2.One);
        var spriteBatch = CreateUninitializedSpriteBatch();

        try
        {
            panel.Draw(spriteBatch, font.Object, boldFont: null, whitePixel: null,
                virtualWidth: 1280, virtualHeight: 720);

            font.Verify(value => value.DrawString(
                    spriteBatch,
                    statusMessage,
                    It.IsAny<Vector2>(),
                    It.Is<Color>(color => color == new Color(255, 196, 96))),
                Times.Once);
        }
        finally
        {
            GC.SuppressFinalize(spriteBatch);
        }
    }

    [Fact]
    public void CancelAction_ShouldDiscardDraftWithoutApplying()
    {
        using var first = TemporaryDirectory.Create();
        using var second = TemporaryDirectory.Create();
        var picker = new QueuedFolderPicker(FolderPickerResult.Selected(second.Path));
        var applyCalls = 0;
        var panel = CreatePanel(
            new[] { first.Path },
            picker,
            _ =>
            {
                applyCalls++;
                return ApplyResult(SongFolderApplyStatus.Updated, new[] { first.Path, second.Path });
            });
        var closed = false;
        panel.Closed += (_, _) => closed = true;
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        DrainPicker(panel);
        ActivateActionFromCurrent(panel, pressesToAction: 6);

        Assert.True(closed);
        Assert.False(panel.IsActive);
        Assert.Equal(0, applyCalls);

        panel.Activate();
        Assert.Equal(new[] { first.Path }, panel.DraftRoots);
    }

    [Fact]
    public void BackKey_ShouldDiscardDraftWithoutApplying()
    {
        using var root = TemporaryDirectory.Create();
        var applyCalls = 0;
        var panel = CreatePanel(
            new[] { root.Path },
            apply: _ =>
            {
                applyCalls++;
                return ApplyResult(SongFolderApplyStatus.Updated, new[] { root.Path });
            });
        panel.Activate();

        Press(panel, Keys.Escape);

        Assert.False(panel.IsActive);
        Assert.Equal(0, applyCalls);
    }

    [Fact]
    public void AddFolder_WhenSelectedFolderDuplicatesDraft_ShouldKeepPanelOpenWithStructuralError()
    {
        using var root = TemporaryDirectory.Create();
        var picker = new QueuedFolderPicker(FolderPickerResult.Selected(root.Path));
        var panel = CreatePanel(new[] { root.Path }, picker);
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        DrainPicker(panel);

        Assert.Equal(new[] { root.Path }, panel.DraftRoots);
        Assert.True(panel.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(panel.StatusMessage));
    }

    [Fact]
    public void AddFolder_WhenSelectedFolderOverlapsDraft_ShouldKeepPanelOpenWithStructuralError()
    {
        using var parent = TemporaryDirectory.Create();
        var childPath = Path.Combine(parent.Path, "child");
        Directory.CreateDirectory(childPath);
        var picker = new QueuedFolderPicker(FolderPickerResult.Selected(childPath));
        var panel = CreatePanel(new[] { parent.Path }, picker);
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        DrainPicker(panel);

        Assert.Equal(new[] { parent.Path }, panel.DraftRoots);
        Assert.True(panel.IsActive);
        Assert.False(string.IsNullOrWhiteSpace(panel.StatusMessage));
    }

    [Fact]
    public void Apply_WhenOnlyAvailabilityWarningsExist_ShouldDelegateAndClose()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"dtxmania-missing-{Guid.NewGuid():N}");
        var capturedRoots = Array.Empty<string>();
        var panel = CreatePanel(
            new[] { missingPath },
            apply: roots =>
            {
                capturedRoots = roots.ToArray();
                return ApplyResult(SongFolderApplyStatus.Updated, roots);
            });
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 4);

        Assert.Equal(new[] { Path.GetFullPath(missingPath) }, capturedRoots);
        Assert.False(panel.IsActive);
    }

    [Fact]
    public void Draw_WhenAvailabilityChangesAfterActivation_ShouldUseCachedWarningAcrossRepeatedFrames()
    {
        var missingPath = Path.Combine(Path.GetTempPath(), $"dtxmania-panel-cache-{Guid.NewGuid():N}");
        var panel = CreatePanel(new[] { missingPath });
        panel.Activate();
        var statusMessage = Assert.IsType<string>(panel.StatusMessage);
        var font = new Mock<IFont>();
        font.Setup(value => value.MeasureString(It.IsAny<string>())).Returns(Vector2.One);
        var spriteBatch = CreateUninitializedSpriteBatch();

        try
        {
            // The panel already captured the missing-root warning. If Draw validates again,
            // this newly created directory changes that warning into a non-warning frame.
            Directory.CreateDirectory(missingPath);

            panel.Draw(spriteBatch, font.Object, boldFont: null, whitePixel: null,
                virtualWidth: 1280, virtualHeight: 720);
            panel.Draw(spriteBatch, font.Object, boldFont: null, whitePixel: null,
                virtualWidth: 1280, virtualHeight: 720);

            font.Verify(value => value.DrawString(
                    spriteBatch,
                    statusMessage,
                    It.IsAny<Vector2>(),
                    It.Is<Color>(color => color == new Color(255, 196, 96))),
                Times.Exactly(2));
        }
        finally
        {
            GC.SuppressFinalize(spriteBatch);
            if (Directory.Exists(missingPath))
                Directory.Delete(missingPath, recursive: true);
        }
    }

    [Fact]
    public void AddFolder_WhenPickerIsCancelled_ShouldLeaveDraftAndPanelUnchanged()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { root.Path }, new QueuedFolderPicker(FolderPickerResult.Cancelled()));
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        DrainPicker(panel);

        Assert.Equal(new[] { root.Path }, panel.DraftRoots);
        Assert.True(panel.IsActive);
        Assert.True(string.IsNullOrWhiteSpace(panel.StatusMessage));
    }

    [Theory]
    [InlineData(FolderPickerStatus.Unavailable)]
    [InlineData(FolderPickerStatus.Failed)]
    public void AddFolder_WhenPickerCannotProvideAFolder_ShouldKeepPanelOpen(FolderPickerStatus status)
    {
        using var root = TemporaryDirectory.Create();
        var picker = new QueuedFolderPicker(new FolderPickerResult(status, message: "picker failure"));
        var panel = CreatePanel(new[] { root.Path }, picker);
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        DrainPicker(panel);

        Assert.Equal(new[] { root.Path }, panel.DraftRoots);
        Assert.True(panel.IsActive);
        Assert.Contains("picker failure", panel.StatusMessage);
    }

    [Fact]
    public async Task AddFolder_WhenAnOldPickerCompletesAfterReactivation_ShouldIgnoreItsResult()
    {
        using var root = TemporaryDirectory.Create();
        using var staleSelection = TemporaryDirectory.Create();
        var picker = new DelayedFolderPicker();
        var panel = CreatePanel(new[] { root.Path }, picker);
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        panel.Deactivate();
        panel.Activate();

        picker.Completion.TrySetResult(FolderPickerResult.Selected(staleSelection.Path));
        await WaitForPendingPickerCompletionAsync(panel);
        DrainPicker(panel);

        Assert.Equal(new[] { root.Path }, panel.DraftRoots);
        Assert.True(panel.IsActive);
    }

    [Fact]
    public void Apply_WhenUpdated_ShouldRaiseSavedBeforeClosed()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(
            new[] { root.Path },
            apply: roots => ApplyResult(SongFolderApplyStatus.Updated, roots));
        var events = new List<string>();
        panel.Saved += (_, _) => events.Add("Saved");
        panel.Closed += (_, _) => events.Add("Closed");
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 4);

        Assert.Equal(new[] { "Saved", "Closed" }, events);
        Assert.False(panel.IsActive);
    }

    [Fact]
    public void Apply_WhenUpdated_ShouldUseTheSavedRootsWhenReopened()
    {
        using var first = TemporaryDirectory.Create();
        using var second = TemporaryDirectory.Create();
        var picker = new QueuedFolderPicker(FolderPickerResult.Selected(second.Path));
        var panel = CreatePanel(
            new[] { first.Path },
            picker,
            roots => ApplyResult(SongFolderApplyStatus.Updated, roots));
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        DrainPicker(panel);
        ActivateActionFromCurrent(panel, pressesToAction: 5);

        panel.Activate();

        Assert.Equal(new[] { first.Path, second.Path }, panel.DraftRoots);
    }

    [Fact]
    public void NavigateLongDraft_ShouldScrollAndKeepActionsReachable()
    {
        var roots = Enumerable.Range(0, 7)
            .Select(_ => TemporaryDirectory.Create())
            .ToArray();
        try
        {
            var applyCalls = 0;
            var panel = CreatePanel(
                roots.Select(root => root.Path).ToArray(),
                apply: draft =>
                {
                    applyCalls++;
                    return new SongFolderApplyResult(
                        SongFolderApplyStatus.Busy,
                        draft,
                        new[] { new SongRootDiagnostic(string.Empty, "busy", IsWarning: false) });
                });
            panel.Activate();

            Assert.Equal(0, panel.FirstVisibleRowIndex);
            Assert.True(panel.TotalRowCount > panel.VisibleRowCapacity);

            for (var index = 0; index < roots.Length + 4; index++)
                Press(panel, Keys.Down);

            Assert.Equal(roots.Length + 4, panel.SelectedRowIndex); // Apply
            Assert.True(panel.FirstVisibleRowIndex > 0);
            Assert.InRange(panel.SelectedRowIndex,
                panel.FirstVisibleRowIndex,
                panel.FirstVisibleRowIndex + panel.VisibleRowCapacity - 1);

            Press(panel, Keys.Enter);
            Assert.Equal(1, applyCalls);
            Assert.True(panel.IsActive);

            Press(panel, Keys.Down);
            Assert.Equal(roots.Length + 5, panel.SelectedRowIndex); // Cancel
            Assert.InRange(panel.SelectedRowIndex,
                panel.FirstVisibleRowIndex,
                panel.FirstVisibleRowIndex + panel.VisibleRowCapacity - 1);

            Press(panel, Keys.Enter);
            Assert.False(panel.IsActive);
        }
        finally
        {
            foreach (var root in roots)
                root.Dispose();
        }
    }

    [Fact]
    public void Apply_WhenUnchanged_ShouldCloseSilently()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(
            new[] { root.Path },
            apply: roots => ApplyResult(SongFolderApplyStatus.Unchanged, roots));
        var saved = false;
        var closed = false;
        panel.Saved += (_, _) => saved = true;
        panel.Closed += (_, _) => closed = true;
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 4);

        Assert.False(saved);
        Assert.True(closed);
        Assert.False(panel.IsActive);
    }

    [Fact]
    public void Apply_WhenConfigOwnedDelegateReportsFailure_ShouldKeepPanelOpen()
    {
        using var root = TemporaryDirectory.Create();
        // SongFolderApplyStatus is internal, so it cannot be exposed on a public
        // Theory parameter. The loop preserves per-status diagnostic clarity via
        // explicit assertion messages instead.
        var failureStatuses = new[]
        {
            SongFolderApplyStatus.Busy,
            SongFolderApplyStatus.ValidationFailed,
            SongFolderApplyStatus.PersistenceFailed,
        };
        foreach (var status in failureStatuses)
        {
            var panel = CreatePanel(
                new[] { root.Path },
                apply: roots => new SongFolderApplyResult(
                    status,
                    roots,
                    new[] { new SongRootDiagnostic(root.Path, "apply failed", IsWarning: false) }));
            panel.Activate();

            ActivateAction(panel, rootCount: 1, actionOffset: 4);

            Assert.True(panel.IsActive,
                $"Status {status} should keep the panel open.");
            Assert.True(panel.StatusMessage.Contains("apply failed", StringComparison.Ordinal),
                $"Status {status} should surface the diagnostic message.");
        }
    }

    [Fact]
    public void Apply_WhenStarted_ShouldCommitRootsRaiseSavedBeforeClosedAndClosePanel()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(
            new[] { root.Path },
            apply: roots => ApplyResult(SongFolderApplyStatus.Started, roots));
        var events = new List<string>();
        panel.Saved += (_, _) => events.Add("Saved");
        panel.Closed += (_, _) => events.Add("Closed");
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 4);

        Assert.Equal(SongFolderApplyStatus.Started, panel.LastApplyStatus);
        Assert.Equal(new[] { "Saved", "Closed" }, events);
        Assert.False(panel.IsActive);
    }

    [Fact]
    public async Task AddFolder_WhenPickerReturnsNull_ShouldReportFailureWithoutCrashing()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { root.Path }, new NullFolderPicker());
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        await WaitForPendingPickerCompletionAsync(panel);
        DrainPicker(panel);

        Assert.True(panel.IsActive);
        Assert.Contains("returned no result", panel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AddFolder_WhenPickerThrowsUnexpectedException_ShouldReportFailure()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { root.Path }, new ThrowingFolderPicker());
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        await WaitForPendingPickerCompletionAsync(panel);
        DrainPicker(panel);

        Assert.True(panel.IsActive);
        Assert.Contains("picker blew up", panel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void AddFolder_WhenPickerReportsAnUnknownStatus_ShouldReportUnknownResult()
    {
        using var root = TemporaryDirectory.Create();
        var unknown = new FolderPickerResult((FolderPickerStatus)999);
        var panel = CreatePanel(new[] { root.Path }, new QueuedFolderPicker(unknown));
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        DrainPicker(panel);

        Assert.True(panel.IsActive);
        Assert.Contains("unknown result", panel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_WhenConfigDelegateThrows_ShouldKeepPanelOpenWithPersistenceFailureStatus()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(
            new[] { root.Path },
            apply: _ => throw new InvalidOperationException("apply blew up"));
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 4);

        Assert.True(panel.IsActive);
        Assert.Equal(SongFolderApplyStatus.PersistenceFailed, panel.LastApplyStatus);
        Assert.Contains("apply blew up", panel.StatusMessage, StringComparison.Ordinal);
    }

    [Fact]
    public void Apply_WhenConfigDelegateReportsUnknownStatus_ShouldKeepPanelOpenWithGenericMessage()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(
            new[] { root.Path },
            apply: roots => new SongFolderApplyResult(
                (SongFolderApplyStatus)999,
                roots,
                Array.Empty<SongRootDiagnostic>()));
        panel.Activate();

        ActivateAction(panel, rootCount: 1, actionOffset: 4);

        Assert.True(panel.IsActive);
        Assert.Contains("Could not save song folders.", panel.StatusMessage, StringComparison.Ordinal);
    }

    private static SongFolderPanel CreatePanel(
        IReadOnlyList<string> configuredRoots,
        IFolderPickerService? picker = null,
        Func<IReadOnlyList<string>, SongFolderApplyResult>? apply = null)
    {
        return new SongFolderPanel(
            configuredRoots,
            picker ?? new QueuedFolderPicker(),
            new SongRootPolicy(SongRootPolicy.CreateComparer(ignoreCase: false)),
            apply ?? (roots => ApplyResult(SongFolderApplyStatus.Updated, roots)));
    }

    private static SongFolderApplyResult ApplyResult(
        SongFolderApplyStatus status,
        IReadOnlyList<string> roots)
    {
        return new SongFolderApplyResult(status, roots, Array.Empty<SongRootDiagnostic>());
    }

    private static void ActivateAction(SongFolderPanel panel, int rootCount, int actionOffset)
    {
        for (var index = 0; index < rootCount + actionOffset; index++)
            Press(panel, Keys.Down);

        Press(panel, Keys.Enter);
    }

    private static void ActivateActionFromCurrent(SongFolderPanel panel, int pressesToAction)
    {
        for (var index = 0; index < pressesToAction; index++)
            Press(panel, Keys.Down);

        Press(panel, Keys.Enter);
    }

    private static void DrainPicker(SongFolderPanel panel) =>
        panel.Update(0, new KeyboardState(), new KeyboardState());

    private static int PendingPickerCompletionCount(SongFolderPanel panel)
    {
        var queue = typeof(SongFolderPanel)
            .GetField("_pickerCompletions", BindingFlags.NonPublic | BindingFlags.Instance)
            ?.GetValue(panel) as System.Collections.ICollection;
        return queue?.Count ?? 0;
    }

    private static async Task WaitForPendingPickerCompletionAsync(
        SongFolderPanel panel,
        int timeoutMs = 2000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (PendingPickerCompletionCount(panel) == 0)
        {
            if (DateTime.UtcNow >= deadline)
                throw new TimeoutException(
                    "A picker completion was not enqueued within the timeout.");
            await Task.Yield();
        }
    }

    private static void Press(SongFolderPanel panel, Keys key) =>
        panel.Update(0, new KeyboardState(key), new KeyboardState());

    private static SpriteBatch CreateUninitializedSpriteBatch()
    {
#pragma warning disable SYSLIB0050
        return (SpriteBatch)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(SpriteBatch));
#pragma warning restore SYSLIB0050
    }

    private sealed class QueuedFolderPicker : IFolderPickerService
    {
        private readonly Queue<FolderPickerResult> _results;

        public QueuedFolderPicker(params FolderPickerResult[] results)
        {
            _results = new Queue<FolderPickerResult>(results);
        }

        public List<string?> InitialDirectories { get; } = new();

        public Task<FolderPickerResult> PickFolderAsync(
            string? initialDirectory,
            CancellationToken cancellationToken)
        {
            InitialDirectories.Add(initialDirectory);
            return Task.FromResult(_results.Count > 0
                ? _results.Dequeue()
                : FolderPickerResult.Cancelled());
        }
    }

    private sealed class DelayedFolderPicker : IFolderPickerService
    {
        public TaskCompletionSource<FolderPickerResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<FolderPickerResult> PickFolderAsync(
            string? initialDirectory,
            CancellationToken cancellationToken) => Completion.Task;
    }

    private sealed class NullFolderPicker : IFolderPickerService
    {
        public Task<FolderPickerResult> PickFolderAsync(
            string? initialDirectory,
            CancellationToken cancellationToken) =>
            Task.FromResult<FolderPickerResult>(null!);
    }

    private sealed class ThrowingFolderPicker : IFolderPickerService
    {
        public Task<FolderPickerResult> PickFolderAsync(
            string? initialDirectory,
            CancellationToken cancellationToken) =>
            Task.FromException<FolderPickerResult>(new InvalidOperationException("picker blew up"));
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path)
        {
            Path = path;
        }

        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dtxmania-song-folder-panel-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }
}

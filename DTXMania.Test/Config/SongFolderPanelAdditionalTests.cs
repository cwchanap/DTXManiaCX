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
using Xunit;

namespace DTXMania.Test.Config;

[Trait("Category", "Unit")]
public sealed class SongFolderPanelAdditionalTests
{
    [Fact]
    public void Constructor_WhenConfiguredRootsIsNull_ShouldThrowArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => CreatePanel(null!));
    }

    [Fact]
    public void Constructor_WhenFolderPickerIsNull_ShouldThrowArgumentNullException()
    {
        using var root = TemporaryDirectory.Create();
        Assert.Throws<ArgumentNullException>(() => new SongFolderPanel(
            new[] { root.Path },
            folderPicker: null!,
            new SongRootPolicy(SongRootPolicy.CreateComparer(false)),
            _ => ApplyResult(SongFolderApplyStatus.Updated, _)));
    }

    [Fact]
    public void Constructor_WhenRootPolicyIsNull_ShouldThrowArgumentNullException()
    {
        using var root = TemporaryDirectory.Create();
        Assert.Throws<ArgumentNullException>(() => new SongFolderPanel(
            new[] { root.Path },
            new Mock<IFolderPickerService>().Object,
            rootPolicy: null!,
            _ => ApplyResult(SongFolderApplyStatus.Updated, _)));
    }

    [Fact]
    public void Constructor_WhenApplyDelegateIsNull_ShouldThrowArgumentNullException()
    {
        using var root = TemporaryDirectory.Create();
        Assert.Throws<ArgumentNullException>(() => new SongFolderPanel(
            new[] { root.Path },
            new Mock<IFolderPickerService>().Object,
            new SongRootPolicy(SongRootPolicy.CreateComparer(false)),
            apply: null!));
    }

    [Fact]
    public void Constructor_WhenConfiguredRootsIsEmpty_ShouldThrowArgumentException()
    {
        var exception = Assert.Throws<ArgumentException>(() =>
            CreatePanel(Array.Empty<string>()));
        Assert.Contains("At least one configured song folder", exception.Message);
    }

    [Fact]
    public void Deactivate_WhenPanelIsNotActive_ShouldBeANoOp()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { root.Path });

        panel.Deactivate();

        Assert.False(panel.IsActive);
    }

    [Fact]
    public void Update_WhenNotActive_ShouldOnlyDrainPickerCompletions()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { root.Path });

        // Pressing keys while inactive must not navigate or activate rows.
        panel.Update(0, new KeyboardState(Keys.Down), new KeyboardState());

        Assert.False(panel.IsActive);
        Assert.Equal(0, panel.SelectedRowIndex);
    }

    [Fact]
    public void Draw_WhenNotActive_ShouldBeANoOp()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { root.Path });
        var font = new Mock<IFont>();
        font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(Vector2.One);
        var spriteBatch = CreateUninitializedSpriteBatch();
        try
        {
            panel.Draw(spriteBatch, font.Object, boldFont: null, whitePixel: null,
                virtualWidth: 1280, virtualHeight: 720);

            font.Verify(f => f.DrawString(
                It.IsAny<SpriteBatch>(),
                It.IsAny<string>(),
                It.IsAny<Vector2>(),
                It.IsAny<Color>()), Times.Never);
        }
        finally
        {
            GC.SuppressFinalize(spriteBatch);
        }
    }

    [Fact]
    public void Draw_WhenSpriteBatchIsNull_ShouldBeANoOpEvenWhenActive()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { root.Path });
        panel.Activate();

        panel.Draw(spriteBatch: null, font: null, boldFont: null, whitePixel: null,
            virtualWidth: 1280, virtualHeight: 720);

        Assert.True(panel.IsActive);
    }

    [Fact]
    public void BackKey_WhenBackKeyIsPressed_ShouldClosePanel()
    {
        using var root = TemporaryDirectory.Create();
        var applyCalls = 0;
        var panel = CreatePanel(
            new[] { root.Path },
            apply: _ =>
            {
                applyCalls++;
                return ApplyResult(SongFolderApplyStatus.Updated, _);
            });
        panel.Activate();

        Press(panel, Keys.Back);

        Assert.False(panel.IsActive);
        Assert.Equal(0, applyCalls);
    }

    [Fact]
    public void ActivateSelectedRow_WhenARootRowIsSelected_ShouldUpdateSelectedRootIndexOnly()
    {
        using var first = TemporaryDirectory.Create();
        using var second = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { first.Path, second.Path });
        panel.Activate();

        // Select the second root row (index 1) and press Enter. Activating a root
        // row only records the selected root index; it must not close the panel.
        Press(panel, Keys.Down);
        Press(panel, Keys.Enter);

        Assert.True(panel.IsActive);
        Assert.Equal(1, panel.SelectedRowIndex);
    }

    [Fact]
    public void MoveSelectedRoot_WhenMovingFirstRootUp_ShouldRemainInPlace()
    {
        using var first = TemporaryDirectory.Create();
        using var second = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { first.Path, second.Path });
        panel.Activate();

        // Select the first root (index 0) and trigger Move Up (action offset 2).
        ActivateActionFromCurrent(panel, pressesToAction: 2);

        Assert.Equal(new[] { first.Path, second.Path }, panel.DraftRoots);
        Assert.True(panel.IsActive);
    }

    [Fact]
    public void MoveSelectedRoot_WhenMovingLastRootDown_ShouldRemainInPlace()
    {
        using var first = TemporaryDirectory.Create();
        using var second = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { first.Path, second.Path });
        panel.Activate();

        // Select the second root (index 1), then navigate to Move Down (action
        // index 3 -> row index 2 + 3 = 5, so 4 Down presses from index 1).
        Press(panel, Keys.Down);
        ActivateActionFromCurrent(panel, pressesToAction: 4);

        Assert.Equal(new[] { first.Path, second.Path }, panel.DraftRoots);
        Assert.True(panel.IsActive);
    }

    [Fact]
    public void RemoveSelectedRoot_WhenSelectedRootIndexExceedsBounds_ShouldClampToRemoveLast()
    {
        using var first = TemporaryDirectory.Create();
        using var second = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { first.Path, second.Path });
        panel.Activate();

        // Force the selected root index beyond the draft count via reflection so
        // RemoveSelectedRoot exercises its Math.Clamp guard.
        SetPrivateField(panel, "_selectedRootIndex", 99);
        // Select the Remove action row (action offset 1 from root count 2 -> 3 downs).
        ActivateAction(panel, rootCount: 2, actionOffset: 1);

        Assert.Equal(new[] { first.Path }, panel.DraftRoots);
        Assert.True(panel.IsActive);
    }

    [Fact]
    public void BeginFolderPicker_WhenAlreadyPending_ShouldIgnoreSecondRequest()
    {
        using var root = TemporaryDirectory.Create();
        var picker = new DelayedFolderPicker();
        var panel = CreatePanel(new[] { root.Path }, picker);
        panel.Activate();

        // Start the first picker.
        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        var firstCancellation = GetPrivateField<CancellationTokenSource>(panel, "_pickerCancellation");
        Assert.NotNull(firstCancellation);

        // A second activation of Add Folder while the first is pending must not
        // replace the active cancellation or start a new picker.
        ActivateActionFromCurrent(panel, pressesToAction: 0);
        var secondCancellation = GetPrivateField<CancellationTokenSource>(panel, "_pickerCancellation");

        Assert.Same(firstCancellation, secondCancellation);
        Assert.Equal(1, picker.CallCount);

        // Clean up the pending picker.
        picker.Completion.TrySetResult(FolderPickerResult.Cancelled());
    }

    [Fact]
    public void Draw_WhenViewportScrollsDown_ShouldRenderMoreAboveIndicator()
    {
        var roots = Enumerable.Range(0, 7)
            .Select(_ => TemporaryDirectory.Create())
            .ToArray();
        try
        {
            var panel = CreatePanel(roots.Select(r => r.Path).ToArray());
            panel.Activate();
            // Scroll past the viewport capacity (9 rows) so the first visible row
            // advances and the "More above" indicator is rendered.
            for (var i = 0; i < panel.VisibleRowCapacity; i++)
                Press(panel, Keys.Down);
            Assert.True(panel.FirstVisibleRowIndex > 0);

            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(Vector2.One);
            var spriteBatch = CreateUninitializedSpriteBatch();
            try
            {
                panel.Draw(spriteBatch, font.Object, boldFont: null, whitePixel: null,
                    virtualWidth: 1280, virtualHeight: 720);

                font.Verify(f => f.DrawString(
                    It.IsAny<SpriteBatch>(),
                    It.Is<string>(s => s.Contains("More above", StringComparison.Ordinal)),
                    It.IsAny<Vector2>(),
                    It.IsAny<Color>()), Times.Once);
            }
            finally
            {
                GC.SuppressFinalize(spriteBatch);
            }
        }
        finally
        {
            foreach (var r in roots)
                r.Dispose();
        }
    }

    [Fact]
    public void Draw_WhenViewportCannotShowAllRows_ShouldRenderMoreBelowIndicator()
    {
        var roots = Enumerable.Range(0, 7)
            .Select(_ => TemporaryDirectory.Create())
            .ToArray();
        try
        {
            var panel = CreatePanel(roots.Select(r => r.Path).ToArray());
            panel.Activate();
            // Stay at the top so rows below the viewport exist.
            Assert.Equal(0, panel.FirstVisibleRowIndex);
            Assert.True(panel.TotalRowCount > panel.VisibleRowCapacity);

            var font = new Mock<IFont>();
            font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(Vector2.One);
            var spriteBatch = CreateUninitializedSpriteBatch();
            try
            {
                panel.Draw(spriteBatch, font.Object, boldFont: null, whitePixel: null,
                    virtualWidth: 1280, virtualHeight: 720);

                font.Verify(f => f.DrawString(
                    It.IsAny<SpriteBatch>(),
                    It.Is<string>(s => s.Contains("More below", StringComparison.Ordinal)),
                    It.IsAny<Vector2>(),
                    It.IsAny<Color>()), Times.Once);
            }
            finally
            {
                GC.SuppressFinalize(spriteBatch);
            }
        }
        finally
        {
            foreach (var r in roots)
                r.Dispose();
        }
    }

    [Fact]
    public void Draw_WhenStatusIsAnError_ShouldUseErrorColor()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(
            new[] { root.Path },
            apply: _ => throw new InvalidOperationException("apply blew up"));
        panel.Activate();
        // Trigger an apply that throws -> PersistenceFailed with an error (not warning).
        ActivateAction(panel, rootCount: 1, actionOffset: 4);
        Assert.True(panel.IsActive);
        Assert.Contains("apply blew up", panel.StatusMessage, StringComparison.Ordinal);

        var font = new Mock<IFont>();
        font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(Vector2.One);
        var spriteBatch = CreateUninitializedSpriteBatch();
        try
        {
            panel.Draw(spriteBatch, font.Object, boldFont: null, whitePixel: null,
                virtualWidth: 1280, virtualHeight: 720);

            font.Verify(f => f.DrawString(
                It.IsAny<SpriteBatch>(),
                It.Is<string>(s => s.Contains("apply blew up", StringComparison.Ordinal)),
                It.IsAny<Vector2>(),
                It.Is<Color>(c => c == new Color(255, 96, 96))), Times.Once);
        }
        finally
        {
            GC.SuppressFinalize(spriteBatch);
        }
    }

    [Fact]
    public void Draw_WhenFolderPickerIsPending_ShouldRenderWaitingInstruction()
    {
        using var root = TemporaryDirectory.Create();
        var picker = new DelayedFolderPicker();
        var panel = CreatePanel(new[] { root.Path }, picker);
        panel.Activate();
        ActivateAction(panel, rootCount: 1, actionOffset: 0);
        Assert.NotNull(GetPrivateField<CancellationTokenSource>(panel, "_pickerCancellation"));

        var font = new Mock<IFont>();
        font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(Vector2.One);
        var spriteBatch = CreateUninitializedSpriteBatch();
        try
        {
            panel.Draw(spriteBatch, font.Object, boldFont: null, whitePixel: null,
                virtualWidth: 1280, virtualHeight: 720);

            font.Verify(f => f.DrawString(
                It.IsAny<SpriteBatch>(),
                It.Is<string>(s => s.Contains("Waiting for folder picker", StringComparison.Ordinal)),
                It.IsAny<Vector2>(),
                It.IsAny<Color>()), Times.Once);
        }
        finally
        {
            GC.SuppressFinalize(spriteBatch);
            picker.Completion.TrySetResult(FolderPickerResult.Cancelled());
        }
    }

    [Fact]
    public void Draw_WithNullFont_ShouldNotThrow()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { root.Path });
        panel.Activate();
        var spriteBatch = CreateUninitializedSpriteBatch();
        try
        {
            // A null font must not crash the draw path.
            panel.Draw(spriteBatch, font: null, boldFont: null, whitePixel: null,
                virtualWidth: 1280, virtualHeight: 720);
        }
        finally
        {
            GC.SuppressFinalize(spriteBatch);
        }
        Assert.True(panel.IsActive);
    }

    [Fact]
    public void Draw_WithBoldFont_ShouldUseBoldFontForSelectedRow()
    {
        using var root = TemporaryDirectory.Create();
        var panel = CreatePanel(new[] { root.Path });
        panel.Activate();
        var font = new Mock<IFont>();
        font.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(Vector2.One);
        var boldFont = new Mock<IFont>();
        boldFont.Setup(f => f.MeasureString(It.IsAny<string>())).Returns(Vector2.One);
        var spriteBatch = CreateUninitializedSpriteBatch();
        try
        {
            panel.Draw(spriteBatch, font.Object, boldFont.Object, whitePixel: null,
                virtualWidth: 1280, virtualHeight: 720);

            // The selected row (index 0, a root) should use the bold font.
            boldFont.Verify(f => f.DrawString(
                It.IsAny<SpriteBatch>(),
                It.Is<string>(s => s.Contains("1.", StringComparison.Ordinal)),
                It.IsAny<Vector2>(),
                It.IsAny<Color>()), Times.Once);
        }
        finally
        {
            GC.SuppressFinalize(spriteBatch);
        }
    }

    #region Helpers

    private static SongFolderPanel CreatePanel(
        IReadOnlyList<string> configuredRoots,
        IFolderPickerService? picker = null,
        Func<IReadOnlyList<string>, SongFolderApplyResult>? apply = null)
    {
        return new SongFolderPanel(
            configuredRoots,
            picker ?? new Mock<IFolderPickerService>().Object,
            new SongRootPolicy(SongRootPolicy.CreateComparer(false)),
            apply ?? (roots => ApplyResult(SongFolderApplyStatus.Updated, roots)));
    }

    private static SongFolderApplyResult ApplyResult(
        SongFolderApplyStatus status,
        IReadOnlyList<string> roots) =>
        new(status, roots, Array.Empty<SongRootDiagnostic>());

    private static void ActivateAction(SongFolderPanel panel, int rootCount, int actionOffset)
    {
        for (var i = 0; i < rootCount + actionOffset; i++)
            Press(panel, Keys.Down);
        Press(panel, Keys.Enter);
    }

    private static void ActivateActionFromCurrent(SongFolderPanel panel, int pressesToAction)
    {
        for (var i = 0; i < pressesToAction; i++)
            Press(panel, Keys.Down);
        Press(panel, Keys.Enter);
    }

    private static void Press(SongFolderPanel panel, Keys key) =>
        panel.Update(0, new KeyboardState(key), new KeyboardState());

    private static void SetPrivateField(object target, string name, object? value) =>
        typeof(SongFolderPanel)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)
            ?.SetValue(target, value);

    private static T GetPrivateField<T>(object target, string name) =>
        (T)typeof(SongFolderPanel)
            .GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(target)!;

    private static SpriteBatch CreateUninitializedSpriteBatch()
    {
#pragma warning disable SYSLIB0050
        return (SpriteBatch)System.Runtime.Serialization.FormatterServices
            .GetUninitializedObject(typeof(SpriteBatch));
#pragma warning restore SYSLIB0050
    }

    private sealed class DelayedFolderPicker : IFolderPickerService
    {
        public TaskCompletionSource<FolderPickerResult> Completion { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);
        public int CallCount { get; private set; }

        public Task<FolderPickerResult> PickFolderAsync(
            string? initialDirectory,
            CancellationToken cancellationToken)
        {
            CallCount++;
            return Completion.Task;
        }
    }

    private sealed class TemporaryDirectory : IDisposable
    {
        private TemporaryDirectory(string path) => Path = path;
        public string Path { get; }

        public static TemporaryDirectory Create()
        {
            var path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"dtxmania-panel-add-{Guid.NewGuid():N}");
            Directory.CreateDirectory(path);
            return new TemporaryDirectory(path);
        }

        public void Dispose()
        {
            if (Directory.Exists(Path))
                Directory.Delete(Path, recursive: true);
        }
    }

    #endregion
}

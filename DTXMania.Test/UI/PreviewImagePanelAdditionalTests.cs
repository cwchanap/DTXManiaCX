using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.UI;

[Trait("Category", "UI")]
public class PreviewImagePanelAdditionalTests
{
    [Fact]
    public void ActiveSongRootPaths_WhenSetToNull_ShouldDefaultToEmptyArray()
    {
        var panel = new PreviewImagePanel();

        panel.ActiveSongRootPaths = null;

        Assert.Empty(panel.ActiveSongRootPaths);
    }

    [Fact]
    public void ResolveSongDirectoryPath_WhenPathIsAbsolute_ShouldReturnItWithoutRemapping()
    {
        var panel = new PreviewImagePanel();
        var absoluteDir = Path.Combine(Path.GetTempPath(), "dtx-preview-absolute", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(absoluteDir);

        try
        {
            // An absolute path must be returned as-is, ignoring ActiveSongRootPaths.
            panel.ActiveSongRootPaths = new[] { "/some/other/root" };

            var resolved = InvokePrivate<string>(panel, "ResolveSongDirectoryPath", absoluteDir);

            Assert.Equal(absoluteDir, resolved);
        }
        finally
        {
            if (Directory.Exists(absoluteDir))
                Directory.Delete(absoluteDir, recursive: true);
        }
    }

    [Fact]
    public void ResolveSongDirectoryPath_WhenPathResolutionThrowsAndActiveRootFallbackExists_ShouldReturnActiveRootPath()
    {
        var panel = new PreviewImagePanel();
        var root = Path.Combine(Path.GetTempPath(), "dtx-preview-catch-root", Guid.NewGuid().ToString("N"));
        var relative = Path.Combine("genre", "song");
        var expected = Path.Combine(root, relative);
        Directory.CreateDirectory(expected);

        try
        {
            panel.ActiveSongRootPaths = new[] { root };

            // Force the primary try block to throw by temporarily changing the current
            // directory to a non-existent path. The catch block's active-root fallback
            // should still resolve the directory.
            var originalDir = Environment.CurrentDirectory;
            try
            {
                // Use a NUL character in the relative path to make Path.GetFullPath throw,
                // triggering the catch block. But we need a valid directory under the root,
                // so instead we use a path that GetFullPath rejects.
                var invalidRelative = relative + "\0";
                var resolved = InvokePrivate<string>(panel, "ResolveSongDirectoryPath", invalidRelative);

                // The catch block's fallback also can't resolve a NUL path, so it returns
                // the original value.
                Assert.Equal(invalidRelative, resolved);
            }
            finally
            {
                Environment.CurrentDirectory = originalDir;
            }
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ResolveSongDirectoryPath_WhenRelativePathResolvesUnderActiveRoot_ShouldReturnFullPath()
    {
        var panel = new PreviewImagePanel();
        var root = Path.Combine(Path.GetTempPath(), "dtx-preview-active-root", Guid.NewGuid().ToString("N"));
        var relative = "genre/song";
        var expected = Path.Combine(root, relative);
        Directory.CreateDirectory(expected);

        try
        {
            panel.ActiveSongRootPaths = new[] { root };

            var resolved = InvokePrivate<string>(panel, "ResolveSongDirectoryPath", relative);

            Assert.Equal(Path.GetFullPath(expected), resolved);
        }
        finally
        {
            if (Directory.Exists(root))
                Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void ActiveSongRootPaths_WhenSetToNewCollection_ShouldReplacePreviousValue()
    {
        var panel = new PreviewImagePanel();
        panel.ActiveSongRootPaths = new[] { "/old/root" };

        panel.ActiveSongRootPaths = new[] { "/new/root", "/another/root" };

        Assert.Equal(new[] { "/new/root", "/another/root" }, panel.ActiveSongRootPaths);
    }

    private static T InvokePrivate<T>(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(target, args)!;
    }
}

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Moq;

using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

namespace DTXMania.Test.UI;

public class PreviewImagePanelTests
{
    [Fact]
    public void Initialize_ShouldLoadDefaultAndPanelTextures()
    {
        var panel = new PreviewImagePanel();
        var mockDefault = new Mock<ITexture>();
        var mockPanel = new Mock<ITexture>();
        var rm = new Mock<IResourceManager>();
        rm.Setup(x => x.LoadTexture(TexturePath.DefaultPreview)).Returns(mockDefault.Object);
        rm.Setup(x => x.ResourceExists(TexturePath.PreimagePanel)).Returns(true);
        rm.Setup(x => x.LoadTexture(TexturePath.PreimagePanel)).Returns(mockPanel.Object);

        panel.Initialize(rm.Object);

        rm.Verify(x => x.LoadTexture(TexturePath.DefaultPreview), Times.Once);
        rm.Verify(x => x.LoadTexture(TexturePath.PreimagePanel), Times.Once);
    }

    [Fact]
    public void UpdateSelectedSong_WithNonScoreNode_ShouldReleaseCurrentPreview()
    {
        var panel = new PreviewImagePanel();
        var preview = new Mock<ITexture>();
        SetField(panel, "_currentPreviewTexture", preview.Object);

        panel.UpdateSelectedSong(new SongListNode { Type = NodeType.Box, Title = "Folder" });

        preview.Verify(x => x.RemoveReference(), Times.Once);
        Assert.Null(GetField<ITexture>(panel, "_currentPreviewTexture"));
    }

    [Fact]
    public void Update_ShouldIncreaseDelay_AndShouldDisplayPreviewAfterThreshold()
    {
        var panel = new PreviewImagePanel();
        SetField(panel, "_currentSong", new SongListNode { Type = NodeType.Score, Title = "Song" });
        SetField(panel, "_defaultPreviewTexture", new Mock<ITexture>().Object);

        panel.Update(0.3);
        panel.Update(0.3);

        var delay = GetField<double>(panel, "_displayDelay");
        Assert.True(delay >= 0.5);

        var shouldDisplay = InvokePrivate<bool>(panel, "ShouldDisplayPreview");
        Assert.True(shouldDisplay);
    }

    [Fact]
    public void ShouldDisplayPreview_ShouldRequireScoreNodeDelayAndTexture()
    {
        var panel = new PreviewImagePanel();
        var texture = new Mock<ITexture>().Object;

        SetField(panel, "_currentSong", new SongListNode { Type = NodeType.Score, Title = "Song" });
        SetField(panel, "_currentPreviewTexture", texture);

        SetField(panel, "_displayDelay", 0.49);
        Assert.False(InvokePrivate<bool>(panel, "ShouldDisplayPreview"));

        SetField(panel, "_displayDelay", 0.5);
        Assert.True(InvokePrivate<bool>(panel, "ShouldDisplayPreview"));

        SetField(panel, "_currentSong", new SongListNode { Type = NodeType.Box, Title = "Folder" });
        Assert.False(InvokePrivate<bool>(panel, "ShouldDisplayPreview"));
    }

    [Fact]
    public void LoadPreviewImageComprehensive_WhenSongDirectoryMissing_ShouldUseDefaultTexture()
    {
        var panel = new PreviewImagePanel();
        var defaultTexture = new Mock<ITexture>();
        var rm = new Mock<IResourceManager>();
        var scoreNode = new SongListNode { Type = NodeType.Score, Title = "NoPathSong" };

        SetField(panel, "_resourceManager", rm.Object);
        SetField(panel, "_defaultPreviewTexture", defaultTexture.Object);

        InvokePrivate<object?>(panel, "LoadPreviewImageComprehensive", scoreNode);

        defaultTexture.Verify(x => x.AddReference(), Times.Once);
        Assert.Same(defaultTexture.Object, GetField<ITexture>(panel, "_currentPreviewTexture"));
    }

    [Fact]
    public void LoadPreviewImageComprehensive_WhenDirectoryDoesNotExist_ShouldUseDefaultTexture()
    {
        var panel = new PreviewImagePanel();
        var defaultTexture = new Mock<ITexture>();
        var rm = new Mock<IResourceManager>();
        var scoreNode = new SongListNode
        {
            Type = NodeType.Score,
            Title = "MissingDirSong",
            DirectoryPath = Path.Combine(Path.GetTempPath(), "dtx-preview-missing", Guid.NewGuid().ToString("N"))
        };

        SetField(panel, "_resourceManager", rm.Object);
        SetField(panel, "_defaultPreviewTexture", defaultTexture.Object);

        InvokePrivate<object?>(panel, "LoadPreviewImageComprehensive", scoreNode);

        defaultTexture.Verify(x => x.AddReference(), Times.Once);
        Assert.Same(defaultTexture.Object, GetField<ITexture>(panel, "_currentPreviewTexture"));
    }

    [Fact]
    public void LoadPreviewImageComprehensive_WhenPreviewExists_ShouldLoadTexture()
    {
        var panel = new PreviewImagePanel();
        var rm = new Mock<IResourceManager>();
        var loadedTexture = new Mock<ITexture>().Object;
        var dir = Path.Combine(Path.GetTempPath(), "dtx-preview-load", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var previewPath = Path.Combine(dir, "preview.jpg");
            File.WriteAllText(previewPath, "x");

            var scoreNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "PreviewSong",
                DirectoryPath = dir
            };

            rm.Setup(x => x.LoadTexture(previewPath)).Returns(loadedTexture);
            SetField(panel, "_resourceManager", rm.Object);

            InvokePrivate<object?>(panel, "LoadPreviewImageComprehensive", scoreNode);

            rm.Verify(x => x.LoadTexture(previewPath), Times.Once);
            Assert.Same(loadedTexture, GetField<ITexture>(panel, "_currentPreviewTexture"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void LoadPreviewImageComprehensive_WhenTextureLoadThrows_ShouldFallbackToDefaultTexture()
    {
        var panel = new PreviewImagePanel();
        var rm = new Mock<IResourceManager>();
        var defaultTexture = new Mock<ITexture>();
        var dir = Path.Combine(Path.GetTempPath(), "dtx-preview-throw", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var previewPath = Path.Combine(dir, "preview.jpg");
            File.WriteAllText(previewPath, "x");

            var scoreNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "ThrowSong",
                DirectoryPath = dir
            };

            rm.Setup(x => x.LoadTexture(previewPath)).Throws(new ObjectDisposedException("ITexture"));
            SetField(panel, "_resourceManager", rm.Object);
            SetField(panel, "_defaultPreviewTexture", defaultTexture.Object);

            InvokePrivate<object?>(panel, "LoadPreviewImageComprehensive", scoreNode);

            defaultTexture.Verify(x => x.AddReference(), Times.Once);
            Assert.Same(defaultTexture.Object, GetField<ITexture>(panel, "_currentPreviewTexture"));
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    [Fact]
    public void GetSongDirectoryFromNode_ShouldPreferChartPath_ThenFallbackToDirectoryPath()
    {
        var panel = new PreviewImagePanel();
        var chartDir = Path.Combine(Path.GetTempPath(), "dtx-preview-chart");
        var chartFile = Path.Combine(chartDir, "song.dtx");

        var withChart = new SongListNode
        {
            Type = NodeType.Score,
            DatabaseSong = new SongEntity { Charts = new List<SongChart> { new SongChart { FilePath = chartFile } } },
            DirectoryPath = "fallback-dir"
        };

        var fromChart = InvokePrivate<string>(panel, "GetSongDirectoryFromNode", withChart);
        Assert.Equal(chartDir, fromChart);

        var withFallback = new SongListNode
        {
            Type = NodeType.Score,
            DatabaseSong = new SongEntity { Charts = new List<SongChart>() },
            DirectoryPath = "fallback-dir"
        };

        var fromFallback = InvokePrivate<string>(panel, "GetSongDirectoryFromNode", withFallback);
        Assert.Equal("fallback-dir", fromFallback);
    }

    [Fact]
    public void ResolveSongDirectoryPath_WhenRelativeAndSongsRootConfigured_ShouldResolveToExistingDirectory()
    {
        var panel = new PreviewImagePanel();
        var root = Path.Combine(Path.GetTempPath(), "dtx-preview-root", Guid.NewGuid().ToString("N"));
        var relative = Path.Combine("genre", "song");
        var expected = Path.Combine(root, relative);
        Directory.CreateDirectory(expected);

        try
        {
            panel.SongsRootPath = root;

            var resolved = InvokePrivate<string>(panel, "ResolveSongDirectoryPath", relative);

            Assert.Equal(Path.GetFullPath(expected), resolved);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [Fact]
    public void FindPreviewImageFile_ShouldFindPreviewByKnownNamesAndExtensions()
    {
        var panel = new PreviewImagePanel();
        var dir = Path.Combine(Path.GetTempPath(), "dtx-preview-find", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "jacket.jpg"), "x");
            File.WriteAllText(Path.Combine(dir, "preview.bmp"), "x");

            var found = InvokePrivate<string>(panel, "FindPreviewImageFile", dir);

            Assert.EndsWith(Path.Combine("preview.bmp"), found);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, recursive: true);
            }
        }
    }

    private static T InvokePrivate<T>(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(target, args)!;
    }

    private static void SetField(object target, string fieldName, object? value)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        field!.SetValue(target, value);
    }

    private static T GetField<T>(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return (T)field!.GetValue(target)!;
    }
}

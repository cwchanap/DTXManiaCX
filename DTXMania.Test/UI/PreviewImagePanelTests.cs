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
    public void GetPanelBounds_WhenFrameTextureLoaded_ShouldUseTextureMetrics()
    {
        var panel = new PreviewImagePanel();
        var frameTexture = new Mock<ITexture>();
        frameTexture.SetupGet(x => x.Width).Returns(308);
        frameTexture.SetupGet(x => x.Height).Returns(308);

        SetField(panel, "_preimagePanelTexture", frameTexture.Object);
        InvokePrivateVoid(panel, "UpdatePositionAndSize");

        var panelBounds = InvokePrivate<Rectangle>(panel, "GetPanelBounds");
        var contentBounds = InvokePrivate<Rectangle>(panel, "GetContentBounds", panelBounds);

        Assert.Equal(new Rectangle(250, 34, 308, 308), panelBounds);
        Assert.Equal(new Rectangle(258, 42, 292, 292), contentBounds);
    }

    [Fact]
    public void HasStatusPanel_WhenDisabled_ShouldUseFallbackPositionAndMetrics()
    {
        var panel = new PreviewImagePanel
        {
            HasStatusPanel = false
        };

        var panelBounds = InvokePrivate<Rectangle>(panel, "GetPanelBounds");
        var contentBounds = InvokePrivate<Rectangle>(panel, "GetContentBounds", panelBounds);

        Assert.Equal(new Vector2(18, 88), panel.Position);
        Assert.Equal(new Vector2(368, 368), panel.Size);
        Assert.Equal(new Rectangle(18, 88, 368, 368), panelBounds);
        Assert.Equal(new Rectangle(55, 112, 294, 294), contentBounds);
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
    public void LoadPreviewImageComprehensive_WhenPreviewMissingAndDefaultTextureUnavailable_ShouldClearCurrentPreview()
    {
        var panel = new PreviewImagePanel();
        var resourceManager = new Mock<IResourceManager>();
        var existingPreview = new Mock<ITexture>();
        var dir = Path.Combine(Path.GetTempPath(), "dtx-preview-empty", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var scoreNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "NoPreviewSong",
                DirectoryPath = dir
            };

            SetField(panel, "_resourceManager", resourceManager.Object);
            SetField(panel, "_currentPreviewTexture", existingPreview.Object);

            InvokePrivate<object?>(panel, "LoadPreviewImageComprehensive", scoreNode);

            existingPreview.Verify(x => x.RemoveReference(), Times.Once);
            Assert.Null(GetFieldValue(panel, "_currentPreviewTexture"));
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
    public void LoadPreviewImageComprehensive_WhenNodeIsNotScore_ShouldReleaseCurrentPreviewAndSkipLoading()
    {
        var panel = new PreviewImagePanel();
        var resourceManager = new Mock<IResourceManager>();
        var existingPreview = new Mock<ITexture>();

        SetField(panel, "_resourceManager", resourceManager.Object);
        SetField(panel, "_currentPreviewTexture", existingPreview.Object);

        InvokePrivate<object?>(panel, "LoadPreviewImageComprehensive", new SongListNode { Type = NodeType.Box, Title = "Folder" });

        existingPreview.Verify(x => x.RemoveReference(), Times.Once);
        Assert.Null(GetFieldValue(panel, "_currentPreviewTexture"));
        resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void LoadPreviewImageComprehensive_WhenPreviewMissingAndDefaultTextureExists_ShouldAssignDefaultTexture()
    {
        var panel = new PreviewImagePanel();
        var resourceManager = new Mock<IResourceManager>();
        var defaultTexture = new Mock<ITexture>();
        var dir = Path.Combine(Path.GetTempPath(), "dtx-preview-default", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var scoreNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "DefaultPreviewSong",
                DirectoryPath = dir
            };

            SetField(panel, "_resourceManager", resourceManager.Object);
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
    public void LoadPreviewImageComprehensive_WhenUnexpectedLoadFailureOccurs_ShouldFallbackToDefaultTexture()
    {
        var panel = new PreviewImagePanel();
        var resourceManager = new Mock<IResourceManager>();
        var defaultTexture = new Mock<ITexture>();
        var dir = Path.Combine(Path.GetTempPath(), "dtx-preview-invalid-op", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var previewPath = Path.Combine(dir, "preview.jpg");
            File.WriteAllText(previewPath, "x");

            var scoreNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "FallbackSong",
                DirectoryPath = dir
            };

            resourceManager.Setup(x => x.LoadTexture(previewPath)).Throws(new InvalidOperationException("broken preview"));
            SetField(panel, "_resourceManager", resourceManager.Object);
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
    public void LoadPreviewImageComprehensive_WhenUnexpectedLoadFailureOccursWithoutDefault_ShouldClearPreview()
    {
        var panel = new PreviewImagePanel();
        var resourceManager = new Mock<IResourceManager>();
        var dir = Path.Combine(Path.GetTempPath(), "dtx-preview-invalid-op-null", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var previewPath = Path.Combine(dir, "preview.jpg");
            File.WriteAllText(previewPath, "x");

            var scoreNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "NoDefaultFallbackSong",
                DirectoryPath = dir
            };

            resourceManager.Setup(x => x.LoadTexture(previewPath)).Throws(new InvalidOperationException("broken preview"));
            SetField(panel, "_resourceManager", resourceManager.Object);

            InvokePrivate<object?>(panel, "LoadPreviewImageComprehensive", scoreNode);

            Assert.Null(GetFieldValue(panel, "_currentPreviewTexture"));
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
    public void UpdateSelectedSong_WhenSongUnchanged_ShouldNotResetDelayOrReloadPreview()
    {
        var panel = new PreviewImagePanel();
        var resourceManager = new Mock<IResourceManager>();
        var song = new SongListNode
        {
            Type = NodeType.Score,
            Title = "Song",
            DatabaseSong = new SongEntity { Charts = new List<SongChart>() }
        };

        SetField(panel, "_resourceManager", resourceManager.Object);
        SetField(panel, "_currentSong", song);
        SetField(panel, "_displayDelay", 0.25);

        panel.UpdateSelectedSong(song);

        Assert.Equal(0.25, GetField<double>(panel, "_displayDelay"), 3);
        resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void Initialize_WhenDefaultAndPanelTexturesUnavailable_ShouldLeaveTextureReferencesNull()
    {
        var panel = new PreviewImagePanel();
        var resourceManager = new Mock<IResourceManager>();

        resourceManager.Setup(x => x.LoadTexture(TexturePath.DefaultPreview)).Throws(new FileNotFoundException("missing default"));
        resourceManager.Setup(x => x.ResourceExists(TexturePath.PreimagePanel)).Returns(false);

        panel.Initialize(resourceManager.Object);

        Assert.Null(GetFieldValue(panel, "_defaultPreviewTexture"));
        Assert.Null(GetFieldValue(panel, "_preimagePanelTexture"));
        Assert.Equal(new Vector2(250, 34), panel.Position);
        Assert.Equal(new Vector2(292, 292), panel.Size);
    }

    [Fact]
    public void AssignDefaultPreviewTexture_WhenDefaultTextureMissing_ShouldClearCurrentPreview()
    {
        var panel = new PreviewImagePanel();
        var existingPreview = new Mock<ITexture>();

        SetField(panel, "_currentPreviewTexture", existingPreview.Object);
        SetField(panel, "_defaultPreviewTexture", null);

        InvokePrivateVoid(panel, "AssignDefaultPreviewTexture");

        Assert.Null(GetFieldValue(panel, "_currentPreviewTexture"));
        // Production expects callers to release the old preview before AssignDefaultPreviewTexture() runs.
        existingPreview.Verify(x => x.RemoveReference(), Times.Never);
    }

    [Fact]
    public void DrawPreviewContent_WhenDefaultTextureIsDisposed_ShouldClearDefaultReference()
    {
        var panel = new PreviewImagePanel();
        var disposedTexture = new Mock<ITexture>();
        disposedTexture
            .Setup(x => x.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()))
            .Throws(new ObjectDisposedException("preview"));

        SetField(panel, "_currentSong", new SongListNode { Type = NodeType.Score, Title = "Song" });
        SetField(panel, "_displayDelay", 0.5);
        SetField(panel, "_defaultPreviewTexture", disposedTexture.Object);

        InvokePrivateVoid(panel, "DrawPreviewContent", null!, new Rectangle(250, 34, 308, 308));

        Assert.Null(GetFieldValue(panel, "_defaultPreviewTexture"));
        Assert.False(InvokePrivate<bool>(panel, "ShouldDisplayPreview"));
    }

    [Fact]
    public void DrawPreviewContent_WhenCurrentPreviewIsDisposed_ShouldClearOnlyCurrentPreviewReference()
    {
        var panel = new PreviewImagePanel();
        var disposedPreview = new Mock<ITexture>();
        var defaultTexture = new Mock<ITexture>();

        disposedPreview
            .Setup(x => x.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()))
            .Throws(new ObjectDisposedException("preview"));

        SetField(panel, "_currentSong", new SongListNode { Type = NodeType.Score, Title = "Song" });
        SetField(panel, "_displayDelay", 0.5);
        SetField(panel, "_currentPreviewTexture", disposedPreview.Object);
        SetField(panel, "_defaultPreviewTexture", defaultTexture.Object);

        InvokePrivateVoid(panel, "DrawPreviewContent", null!, new Rectangle(250, 34, 308, 308));

        Assert.Null(GetFieldValue(panel, "_currentPreviewTexture"));
        Assert.Same(defaultTexture.Object, GetField<ITexture>(panel, "_defaultPreviewTexture"));
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
    public void ResolveSongDirectoryPath_WhenAllFallbacksReceiveInvalidPath_ShouldReturnOriginalValue()
    {
        var panel = new PreviewImagePanel();
        var invalidPath = "invalid\0preview";

        panel.SongsRootPath = Path.GetTempPath();

        var resolved = InvokePrivate<string>(panel, "ResolveSongDirectoryPath", invalidPath);

        Assert.Equal(invalidPath, resolved);
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

    [Fact]
    public void FindPreviewImageFile_WhenNoKnownPreviewExists_ShouldReturnNull()
    {
        var panel = new PreviewImagePanel();
        var dir = Path.Combine(Path.GetTempPath(), "dtx-preview-none", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "notes.txt"), "x");

            var found = InvokePrivate<string?>(panel, "FindPreviewImageFile", dir);

            Assert.Null(found);
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
    public void DrawBackground_WhenFrameTextureIsLoaded_ShouldUseNaturalTextureBounds()
    {
        var panel = new PreviewImagePanel();
        var frameTexture = new Mock<ITexture>();
        Rectangle? drawnBounds = null;

        frameTexture.SetupGet(x => x.Width).Returns(308);
        frameTexture.SetupGet(x => x.Height).Returns(308);
        frameTexture
            .Setup(x => x.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()))
            .Callback<SpriteBatch, Rectangle, Rectangle?, Color, float, Vector2, SpriteEffects, float>(
                (_, destination, _, _, _, _, _, _) => drawnBounds = destination);

        SetField(panel, "_preimagePanelTexture", frameTexture.Object);

        InvokePrivateVoid(panel, "DrawBackground", null!, new Rectangle(10, 20, 400, 400));

        Assert.Equal(new Rectangle(10, 20, 308, 308), drawnBounds);
    }

    [Fact]
    public void DrawPreviewContent_WhenPreviewDrawThrowsUnexpectedException_ShouldKeepTextureReference()
    {
        var panel = new PreviewImagePanel();
        var previewTexture = new Mock<ITexture>();

        previewTexture
            .Setup(x => x.Draw(
                It.IsAny<SpriteBatch>(),
                It.IsAny<Rectangle>(),
                It.IsAny<Rectangle?>(),
                It.IsAny<Color>(),
                It.IsAny<float>(),
                It.IsAny<Vector2>(),
                It.IsAny<SpriteEffects>(),
                It.IsAny<float>()))
            .Throws(new InvalidOperationException("boom"));

        SetField(panel, "_currentSong", new SongListNode { Type = NodeType.Score, Title = "Song" });
        SetField(panel, "_displayDelay", 0.5);
        SetField(panel, "_currentPreviewTexture", previewTexture.Object);

        var exception = Record.Exception(() => InvokePrivateVoid(panel, "DrawPreviewContent", null!, new Rectangle(250, 34, 308, 308)));

        Assert.Null(exception);
        Assert.Same(previewTexture.Object, GetField<ITexture>(panel, "_currentPreviewTexture"));
    }

    [Fact]
    public void Dispose_ShouldReleaseCurrentDefaultAndFrameTextures()
    {
        var panel = new PreviewImagePanel();
        var currentPreview = new Mock<ITexture>();
        var defaultPreview = new Mock<ITexture>();
        var frameTexture = new Mock<ITexture>();

        SetField(panel, "_currentPreviewTexture", currentPreview.Object);
        SetField(panel, "_defaultPreviewTexture", defaultPreview.Object);
        SetField(panel, "_preimagePanelTexture", frameTexture.Object);

        panel.Dispose();

        currentPreview.Verify(x => x.RemoveReference(), Times.Once);
        defaultPreview.Verify(x => x.RemoveReference(), Times.Once);
        frameTexture.Verify(x => x.RemoveReference(), Times.Once);
        Assert.Null(GetFieldValue(panel, "_currentPreviewTexture"));
        Assert.Null(GetFieldValue(panel, "_defaultPreviewTexture"));
        Assert.Null(GetFieldValue(panel, "_preimagePanelTexture"));
    }

    [Fact]
    public void LoadDefaultPreviewTexture_WhenResourceManagerIsNull_ShouldLeaveDefaultPreviewNull()
    {
        var panel = new PreviewImagePanel();
        SetField(panel, "_resourceManager", null);

        InvokePrivateVoid(panel, "LoadDefaultPreviewTexture");

        Assert.Null(GetFieldValue(panel, "_defaultPreviewTexture"));
    }

    [Fact]
    public void LoadDefaultPreviewTexture_WhenLoadTextureThrows_ShouldLeaveDefaultPreviewNull()
    {
        var panel = new PreviewImagePanel();
        var rm = new Mock<IResourceManager>();
        rm.Setup(x => x.LoadTexture(TexturePath.DefaultPreview))
            .Throws(new FileNotFoundException("default preview missing"));

        SetField(panel, "_resourceManager", rm.Object);

        InvokePrivateVoid(panel, "LoadDefaultPreviewTexture");

        Assert.Null(GetFieldValue(panel, "_defaultPreviewTexture"));
    }

    [Fact]
    public void LoadPreimagePanelTexture_WhenResourceExistsReturnsFalse_ShouldLeavePanelTextureNull()
    {
        var panel = new PreviewImagePanel();
        var rm = new Mock<IResourceManager>();
        rm.Setup(x => x.ResourceExists(TexturePath.PreimagePanel)).Returns(false);

        SetField(panel, "_resourceManager", rm.Object);

        InvokePrivateVoid(panel, "LoadPreimagePanelTexture");

        Assert.Null(GetFieldValue(panel, "_preimagePanelTexture"));
        rm.Verify(x => x.LoadTexture(TexturePath.PreimagePanel), Times.Never);
    }

    [Fact]
    public void LoadPreimagePanelTexture_WhenLoadTextureThrows_ShouldLeavePanelTextureNull()
    {
        var panel = new PreviewImagePanel();
        var rm = new Mock<IResourceManager>();
        rm.Setup(x => x.ResourceExists(TexturePath.PreimagePanel)).Returns(true);
        rm.Setup(x => x.LoadTexture(TexturePath.PreimagePanel))
            .Throws(new InvalidOperationException("panel texture corrupted"));

        SetField(panel, "_resourceManager", rm.Object);

        InvokePrivateVoid(panel, "LoadPreimagePanelTexture");

        Assert.Null(GetFieldValue(panel, "_preimagePanelTexture"));
    }

    [Fact]
    public void AssignDefaultPreviewTexture_WhenDefaultPreviewPresent_ShouldAddReferenceAndAssign()
    {
        var panel = new PreviewImagePanel();
        var defaultTexture = new Mock<ITexture>();

        SetField(panel, "_defaultPreviewTexture", defaultTexture.Object);

        InvokePrivateVoid(panel, "AssignDefaultPreviewTexture");

        defaultTexture.Verify(x => x.AddReference(), Times.Once);
        Assert.Same(defaultTexture.Object, GetField<ITexture>(panel, "_currentPreviewTexture"));
    }

    [Fact]
    public void GetSongDirectoryFromNode_WhenDatabaseSongHasChartPath_ShouldReturnChartDirectory()
    {
        var panel = new PreviewImagePanel();
        var chartFile = Path.Combine("DTXFiles", "genre", "song", "chart.dtx");
        var node = new SongListNode
        {
            Type = NodeType.Score,
            DatabaseSong = new SongEntity
            {
                Charts = new List<SongChart> { new SongChart { FilePath = chartFile } }
            },
            DirectoryPath = "should-not-use-this"
        };

        var result = InvokePrivate<string>(panel, "GetSongDirectoryFromNode", node);

        Assert.Equal(Path.GetDirectoryName(chartFile), result);
    }

    [Fact]
    public void GetSongDirectoryFromNode_WhenChartPathMissing_ShouldFallbackToDirectoryPath()
    {
        var panel = new PreviewImagePanel();
        var node = new SongListNode
        {
            Type = NodeType.Score,
            DatabaseSong = new SongEntity { Charts = new List<SongChart>() },
            DirectoryPath = "fallback-directory"
        };

        var result = InvokePrivate<string>(panel, "GetSongDirectoryFromNode", node);

        Assert.Equal("fallback-directory", result);
    }

    [Fact]
    public void ResolveSongDirectoryPath_WhenRelativePathExistsUnderSongsRootPath_ShouldResolveToThatPath()
    {
        var panel = new PreviewImagePanel();
        var root = Path.Combine(Path.GetTempPath(), "dtx-resolve-root", Guid.NewGuid().ToString("N"));
        var relative = Path.Combine("category", "songname");
        var expected = Path.Combine(root, relative);
        Directory.CreateDirectory(expected);

        try
        {
            panel.SongsRootPath = root;

            var result = InvokePrivate<string>(panel, "ResolveSongDirectoryPath", relative);

            Assert.Equal(Path.GetFullPath(expected), result);
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
    public void FindPreviewImageFile_ShouldSearchInOrderPreviewJacketBanner()
    {
        var panel = new PreviewImagePanel();
        var dir = Path.Combine(Path.GetTempPath(), "dtx-search-order", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "jacket.png"), "jacket");
            File.WriteAllText(Path.Combine(dir, "banner.jpg"), "banner");

            var found = InvokePrivate<string>(panel, "FindPreviewImageFile", dir);

            Assert.EndsWith(Path.Combine(dir, "jacket.png"), found);
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
    public void FindPreviewImageFile_ShouldPrioritizePreviewOverJacket()
    {
        var panel = new PreviewImagePanel();
        var dir = Path.Combine(Path.GetTempPath(), "dtx-priority", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            File.WriteAllText(Path.Combine(dir, "preview.png"), "preview");
            File.WriteAllText(Path.Combine(dir, "jacket.jpg"), "jacket");

            var found = InvokePrivate<string>(panel, "FindPreviewImageFile", dir);

            Assert.EndsWith(Path.Combine(dir, "preview.png"), found);
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
    public void ClearDisposedTextureReference_WhenMatchesCurrentPreview_ShouldClearCurrentPreviewOnly()
    {
        var panel = new PreviewImagePanel();
        var currentTexture = new Mock<ITexture>();
        var defaultTexture = new Mock<ITexture>();

        SetField(panel, "_currentPreviewTexture", currentTexture.Object);
        SetField(panel, "_defaultPreviewTexture", defaultTexture.Object);

        InvokePrivateVoid(panel, "ClearDisposedTextureReference", currentTexture.Object);

        Assert.Null(GetFieldValue(panel, "_currentPreviewTexture"));
        Assert.Same(defaultTexture.Object, GetField<ITexture>(panel, "_defaultPreviewTexture"));
    }

    [Fact]
    public void ClearDisposedTextureReference_WhenMatchesDefaultPreview_ShouldClearDefaultPreviewOnly()
    {
        var panel = new PreviewImagePanel();
        var currentTexture = new Mock<ITexture>();
        var defaultTexture = new Mock<ITexture>();

        SetField(panel, "_currentPreviewTexture", currentTexture.Object);
        SetField(panel, "_defaultPreviewTexture", defaultTexture.Object);

        InvokePrivateVoid(panel, "ClearDisposedTextureReference", defaultTexture.Object);

        Assert.Same(currentTexture.Object, GetField<ITexture>(panel, "_currentPreviewTexture"));
        Assert.Null(GetFieldValue(panel, "_defaultPreviewTexture"));
    }

    [Fact]
    public void OnDraw_WhenNotVisible_ShouldReturnEarly()
    {
        var panel = new PreviewImagePanel { Visible = false };
        var frameTexture = new Mock<ITexture>();

        SetField(panel, "_preimagePanelTexture", frameTexture.Object);

        InvokePrivateVoid(panel, "OnDraw", null!, 0.016);

        frameTexture.Verify(x => x.Draw(
            It.IsAny<SpriteBatch>(),
            It.IsAny<Rectangle>(),
            It.IsAny<Rectangle?>(),
            It.IsAny<Color>(),
            It.IsAny<float>(),
            It.IsAny<Vector2>(),
            It.IsAny<SpriteEffects>(),
            It.IsAny<float>()), Times.Never);
    }

    [Fact]
    public void DrawPlaceholder_WhenWhitePixelIsNull_ShouldReturnEarly()
    {
        var panel = new PreviewImagePanel();
        SetField(panel, "_whitePixel", null);

        var exception = Record.Exception(() =>
            InvokePrivateVoid(panel, "DrawPlaceholder", null!, new Rectangle(0, 0, 100, 100)));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawBackground_WhenPanelTextureIsNullAndWhitePixelIsNull_ShouldReturnEarly()
    {
        var panel = new PreviewImagePanel();
        SetField(panel, "_preimagePanelTexture", null);
        SetField(panel, "_whitePixel", null);

        var exception = Record.Exception(() =>
            InvokePrivateVoid(panel, "DrawBackground", null!, new Rectangle(0, 0, 100, 100)));

        Assert.Null(exception);
    }

    [Fact]
    public void WhitePixel_ShouldGetAndSetValue()
    {
        var panel = new PreviewImagePanel();

        panel.WhitePixel = null;
        Assert.Null(panel.WhitePixel);

        // We can't easily create a real Texture2D in a unit test,
        // so we just verify the property getter/setter logic works with null
    }

    [Fact]
    public void SongsRootPath_ShouldGetAndSetValue()
    {
        var panel = new PreviewImagePanel();
        var rootPath = "/test/songs";

        panel.SongsRootPath = rootPath;

        Assert.Equal(rootPath, panel.SongsRootPath);
    }

    private static T InvokePrivate<T>(object target, string methodName, params object[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        return (T)method!.Invoke(target, args)!;
    }

    private static void InvokePrivateVoid(object target, string methodName, params object?[] args)
    {
        var method = target.GetType().GetMethod(methodName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);
        method!.Invoke(target, args);
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

    private static object? GetFieldValue(object target, string fieldName)
    {
        var field = target.GetType().GetField(fieldName, BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return field!.GetValue(target);
    }
}

using System;
using System.IO;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.Utilities;
using Microsoft.Xna.Framework;
using Moq;

namespace DTXMania.Test.UI;

public class SongBarRendererLogicTests
{
    [Fact]
    public void SetFastScrollMode_ShouldToggleProperty()
    {
        var renderer = CreateUninitializedRenderer();

        renderer.SetFastScrollMode(true);
        Assert.True(renderer.IsFastScrollMode);

        renderer.SetFastScrollMode(false);
        Assert.False(renderer.IsFastScrollMode);
    }

    [Theory]
    [InlineData(NodeType.BackBox, ".. (Back)")]
    [InlineData(NodeType.Box, "[Folder]")]
    [InlineData(NodeType.Random, "*** RANDOM SELECT ***")]
    [InlineData(NodeType.Score, "Song")]
    public void GetDisplayText_ShouldReturnExpectedText(NodeType type, string expected)
    {
        var renderer = CreateUninitializedRenderer();
        var node = new SongListNode { Type = type, Title = type == NodeType.Box ? "Folder" : "Song" };

        var text = InvokePrivate<string>(renderer, "GetDisplayText", node);

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(NodeType.Box, 0, 255, 255)]
    [InlineData(NodeType.BackBox, 255, 165, 0)]
    [InlineData(NodeType.Random, 255, 0, 255)]
    [InlineData(NodeType.Score, 255, 255, 255)]
    public void GetNodeTypeColor_ShouldReturnExpectedColor(NodeType type, byte r, byte g, byte b)
    {
        var renderer = CreateUninitializedRenderer();
        var node = new SongListNode { Type = type, Title = "X" };

        var color = InvokePrivate<Color>(renderer, "GetNodeTypeColor", node);

        Assert.Equal(r, color.R);
        Assert.Equal(g, color.G);
        Assert.Equal(b, color.B);
    }

    [Fact]
    public void GetClearStatus_ShouldMapScoreState()
    {
        var renderer = CreateUninitializedRenderer();
        var node = new SongListNode
        {
            Type = NodeType.Score,
            Scores =
            [
                new SongScore { FullCombo = true, BestRank = 90, PlayCount = 3 },
                new SongScore { FullCombo = false, BestRank = 85, PlayCount = 3 },
                new SongScore { FullCombo = false, BestRank = 10, PlayCount = 1 },
                null,
                null,
            ]
        };

        Assert.Equal(ClearStatus.FullCombo, InvokePrivate<ClearStatus>(renderer, "GetClearStatus", node, 0));
        Assert.Equal(ClearStatus.Clear, InvokePrivate<ClearStatus>(renderer, "GetClearStatus", node, 1));
        Assert.Equal(ClearStatus.Failed, InvokePrivate<ClearStatus>(renderer, "GetClearStatus", node, 2));
        Assert.Equal(ClearStatus.NotPlayed, InvokePrivate<ClearStatus>(renderer, "GetClearStatus", node, 3));
    }

    [Theory]
    [InlineData(NodeType.Score, BarType.Score)]
    [InlineData(NodeType.Box, BarType.Box)]
    [InlineData(NodeType.BackBox, BarType.Other)]
    [InlineData(NodeType.Random, BarType.Other)]
    public void GetBarType_ShouldMapNodeType(NodeType type, BarType expected)
    {
        var renderer = CreateUninitializedRenderer();
        var node = new SongListNode { Type = type };

        var barType = InvokePrivate<BarType>(renderer, "GetBarType", node);

        Assert.Equal(expected, barType);
    }

    [Fact]
    public void GetClearLampCacheKey_ShouldIncludeDifficultyAndStatus()
    {
        var renderer = CreateUninitializedRenderer();
        var node = new SongListNode
        {
            Type = NodeType.Score,
            Scores = [new SongScore { FullCombo = true }]
        };

        var key = InvokePrivate<string>(renderer, "GetClearLampCacheKey", node, 0);

        Assert.Contains("_0_", key);
        Assert.Contains("FullCombo", key);
    }

    [Fact]
    public void LoadPreviewImage_WhenFastScrollMode_ShouldReturnNull()
    {
        var renderer = CreateUninitializedRenderer();
        var resourceManager = new Mock<IResourceManager>();
        SetField(renderer, "_resourceManager", resourceManager.Object);
        SetField(renderer, "_isFastScrollMode", true);

        var node = new SongListNode
        {
            DatabaseChart = new SongChart { FilePath = "/tmp/song.dtx", PreviewImage = "preview.png" }
        };

        var texture = InvokePrivate<ITexture?>(renderer, "LoadPreviewImage", node);

        Assert.Null(texture);
        resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public void LoadPreviewImage_WhenPreviewFileDoesNotExist_ShouldReturnNull()
    {
        var renderer = CreateUninitializedRenderer();
        var resourceManager = new Mock<IResourceManager>();
        SetField(renderer, "_resourceManager", resourceManager.Object);
        SetField(renderer, "_isFastScrollMode", false);

        var dir = Path.Combine(Path.GetTempPath(), "dtx-songbar-missing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var node = new SongListNode
            {
                DatabaseChart = new SongChart { FilePath = Path.Combine(dir, "song.dtx"), PreviewImage = "preview.png" }
            };

            var texture = InvokePrivate<ITexture?>(renderer, "LoadPreviewImage", node);

            Assert.Null(texture);
            resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenPreviewFileMissing_ShouldReturnNullWithoutLoading()
    {
        var renderer = CreateUninitializedRenderer();
        var resourceManager = new Mock<IResourceManager>();
        SetField(renderer, "_resourceManager", resourceManager.Object);
        SetField(renderer, "_previewImageCache", new CacheManager<string, ITexture>());
        SetField(renderer, "_isFastScrollMode", false);

        var dir = Path.Combine(Path.GetTempPath(), "dtx-songbar-public-missing", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var node = new SongListNode
            {
                DatabaseChart = new SongChart { FilePath = Path.Combine(dir, "song.dtx"), PreviewImage = "preview.png" }
            };

            var result = renderer.GeneratePreviewImageTexture(node);

            Assert.Null(result);
            resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void UpdateBarInfo_WhenStateUnchangedForScore_ShouldNotReplaceClearLamp()
    {
        var renderer = CreateUninitializedRenderer();
        var clearLamp = new Mock<ITexture>();
        var barInfo = new SongBarInfo
        {
            SongNode = new SongListNode { Type = NodeType.Score, Title = "Song" },
            DifficultyLevel = 2,
            IsSelected = true,
            ClearLamp = clearLamp.Object
        };

        renderer.UpdateBarInfo(barInfo, 2, true);

        Assert.Same(clearLamp.Object, barInfo.ClearLamp);
        clearLamp.Verify(x => x.RemoveReference(), Times.Never);
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenPreviewIsMissingOrFastScroll_ShouldReturnNull()
    {
        var renderer = CreateUninitializedRenderer();

        var noPreviewNode = new SongListNode
        {
            DatabaseChart = new SongChart { FilePath = "/tmp/song.dtx", PreviewImage = null }
        };

        var noPreviewResult = renderer.GeneratePreviewImageTexture(noPreviewNode);
        Assert.Null(noPreviewResult);

        SetField(renderer, "_isFastScrollMode", true);
        var fastScrollNode = new SongListNode
        {
            DatabaseChart = new SongChart { FilePath = "/tmp/song.dtx", PreviewImage = "preview.png" }
        };

        var fastScrollResult = renderer.GeneratePreviewImageTexture(fastScrollNode);
        Assert.Null(fastScrollResult);
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenCached_ShouldReturnCachedTexture()
    {
        var renderer = CreateUninitializedRenderer();
        var cache = new CacheManager<string, ITexture>();
        var cachedTexture = new Mock<ITexture>().Object;
        cache.Add("preview.png", cachedTexture);

        SetField(renderer, "_previewImageCache", cache);
        SetField(renderer, "_isFastScrollMode", false);

        var node = new SongListNode
        {
            DatabaseChart = new SongChart { FilePath = "/tmp/song.dtx", PreviewImage = "preview.png" }
        };

        var result = renderer.GeneratePreviewImageTexture(node);

        Assert.Same(cachedTexture, result);
    }

    [Fact]
    public void SetFont_ShouldClearTitleTextureCache()
    {
        var renderer = CreateUninitializedRenderer();
        var cache = new CacheManager<string, ITexture>();
        var cachedTexture = new Mock<ITexture>().Object;
        cache.Add("title-key", cachedTexture);
        SetField(renderer, "_titleTextureCache", cache);

        renderer.SetFont(null);

        Assert.False(cache.TryGet("title-key", out _));
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenPreviewFileIsTooLarge_ShouldReturnNullWithoutLoading()
    {
        var renderer = CreateUninitializedRenderer();
        var resourceManager = new Mock<IResourceManager>();
        SetField(renderer, "_resourceManager", resourceManager.Object);
        SetField(renderer, "_previewImageCache", new CacheManager<string, ITexture>());
        SetField(renderer, "_isFastScrollMode", false);

        var dir = Path.Combine(Path.GetTempPath(), "dtx-songbar-large", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var previewPath = Path.Combine(dir, "preview-large.png");
            File.WriteAllBytes(previewPath, new byte[501 * 1024]);

            var node = new SongListNode
            {
                DatabaseChart = new SongChart { FilePath = Path.Combine(dir, "song.dtx"), PreviewImage = "preview-large.png" }
            };

            var result = renderer.GeneratePreviewImageTexture(node);

            Assert.Null(result);
            resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void LoadPreviewImage_WhenResourceManagerThrows_ShouldReturnNull()
    {
        var renderer = CreateUninitializedRenderer();
        var resourceManager = new Mock<IResourceManager>();
        SetField(renderer, "_resourceManager", resourceManager.Object);
        SetField(renderer, "_isFastScrollMode", false);

        var dir = Path.Combine(Path.GetTempPath(), "dtx-songbar-throw", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);

        try
        {
            var previewPath = Path.Combine(dir, "preview.png");
            File.WriteAllText(previewPath, "x");

            var node = new SongListNode
            {
                DatabaseChart = new SongChart { FilePath = Path.Combine(dir, "song.dtx"), PreviewImage = "preview.png" }
            };

            resourceManager.Setup(x => x.LoadTexture(previewPath)).Throws(new ObjectDisposedException("texture"));

            var result = InvokePrivate<ITexture?>(renderer, "LoadPreviewImage", node);

            Assert.Null(result);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    [Fact]
    public void GenerateBarInfoWithPriority_ShouldHandleNullAndFastScrollBranches()
    {
        var renderer = CreateUninitializedRenderer();
        SetField(renderer, "_titleTextureCache", new CacheManager<string, ITexture>());
        SetField(renderer, "_previewImageCache", new CacheManager<string, ITexture>());
        SetField(renderer, "_clearLampCache", new CacheManager<string, ITexture>());
        SetField(renderer, "_font", null);
        SetField(renderer, "_titleRenderTarget", null);

        Assert.Null(renderer.GenerateBarInfoWithPriority(null, 0, false));

        var fastScrollNode = new SongListNode
        {
            Type = NodeType.Box,
            Title = "Folder",
            DatabaseChart = new SongChart { FilePath = "/tmp/song.dtx", PreviewImage = "preview.png" }
        };

        SetField(renderer, "_isFastScrollMode", true);
        var skippedPreview = renderer.GenerateBarInfoWithPriority(fastScrollNode, 1, false);
        Assert.NotNull(skippedPreview);
        Assert.Null(skippedPreview!.PreviewImage);

        var cachedTexture = new Mock<ITexture>().Object;
        var previewCache = new CacheManager<string, ITexture>();
        previewCache.Add("preview.png", cachedTexture);
        SetField(renderer, "_previewImageCache", previewCache);
        SetField(renderer, "_isFastScrollMode", false);

        var withPreview = renderer.GenerateBarInfoWithPriority(fastScrollNode, 1, false);
        Assert.NotNull(withPreview);
        Assert.Same(cachedTexture, withPreview!.PreviewImage);
    }

    [Fact]
    public void GenerateBarInfo_AndUpdateBarInfo_ShouldHandleBasicPaths()
    {
        var renderer = CreateUninitializedRenderer();
        SetField(renderer, "_titleTextureCache", new CacheManager<string, ITexture>());
        SetField(renderer, "_previewImageCache", new CacheManager<string, ITexture>());
        SetField(renderer, "_clearLampCache", new CacheManager<string, ITexture>());
        SetField(renderer, "_font", null);
        SetField(renderer, "_titleRenderTarget", null);

        Assert.Null(renderer.GenerateBarInfo(null, 0, false));

        var node = new SongListNode { Type = NodeType.Box, Title = "Folder" };
        var barInfo = renderer.GenerateBarInfo(node, 2, false);

        Assert.NotNull(barInfo);
        Assert.Equal(node, barInfo.SongNode);
        Assert.Equal(BarType.Box, barInfo.BarType);
        Assert.Equal(2, barInfo.DifficultyLevel);
        Assert.False(barInfo.IsSelected);

        renderer.UpdateBarInfo(null, 1, true); // should be no-op

        renderer.UpdateBarInfo(barInfo, 2, true);
        Assert.True(barInfo.IsSelected);
        Assert.Equal(Color.Yellow, barInfo.TextColor);
    }

    [Fact]
    public void GenerateClearLampTexture_WhenNodeIsNotScore_ShouldReturnNull()
    {
        var renderer = CreateUninitializedRenderer();

        var result = renderer.GenerateClearLampTexture(new SongListNode { Type = NodeType.Box }, 0);

        Assert.Null(result);
    }

    [Fact]
    public void LoadPreviewImage_WhenPreviewFileExists_ShouldLoadTexture()
    {
        var renderer = CreateUninitializedRenderer();
        var resourceManager = new Mock<IResourceManager>();
        var expectedTexture = new Mock<ITexture>().Object;
        SetField(renderer, "_resourceManager", resourceManager.Object);
        SetField(renderer, "_isFastScrollMode", false);

        var dir = Path.Combine(Path.GetTempPath(), "dtx-songbar-load", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        try
        {
            var previewPath = Path.Combine(dir, "preview.png");
            File.WriteAllText(previewPath, "x");

            var node = new SongListNode
            {
                DatabaseChart = new SongChart { FilePath = Path.Combine(dir, "song.dtx"), PreviewImage = "preview.png" }
            };

            resourceManager.Setup(x => x.LoadTexture(previewPath)).Returns(expectedTexture);

            var texture = InvokePrivate<ITexture?>(renderer, "LoadPreviewImage", node);

            Assert.Same(expectedTexture, texture);
            resourceManager.Verify(x => x.LoadTexture(previewPath), Times.Once);
        }
        finally
        {
            if (Directory.Exists(dir))
            {
                Directory.Delete(dir, true);
            }
        }
    }

    private static SongBarRenderer CreateUninitializedRenderer()
    {
#pragma warning disable SYSLIB0050
        return (SongBarRenderer)FormatterServices.GetUninitializedObject(typeof(SongBarRenderer));
#pragma warning restore SYSLIB0050
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
}

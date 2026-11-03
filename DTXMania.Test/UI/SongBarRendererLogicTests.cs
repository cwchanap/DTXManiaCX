using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;
using DTXMania.Game.Lib.Utilities;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test.UI;

[Trait("Category", "Unit")]
public class SongBarRendererLogicTests
{
    [Fact]
    public void GenerateTitleTexture_WhenSongNodeIsNull_ShouldReturnNull()
    {
        var renderer = CreateRenderer();

        var texture = renderer.GenerateTitleTexture(null!);

        Assert.Null(texture);
    }

    [Fact]
    public void GenerateTitleTexture_WhenCachedTextureExists_ShouldAddReferenceAndReturnCachedTexture()
    {
        var renderer = CreateRenderer();
        var songNode = CreateScoreNode();
        var cachedTexture = CreateTextureMock();
        GetTitleCache(renderer).Add(InvokePrivate<string>(renderer, "GetTitleCacheKey", songNode), cachedTexture.Object);

        var texture = renderer.GenerateTitleTexture(songNode);

        Assert.Same(cachedTexture.Object, texture);
        cachedTexture.Verify(x => x.AddReference(), Times.Once);
    }

    [Fact]
    public void GenerateTitleTexture_WhenCacheMissAndTitleTextureCannotBeCreated_ShouldReturnNull()
    {
        var renderer = CreateRenderer();

        var texture = renderer.GenerateTitleTexture(CreateScoreNode());

        Assert.Null(texture);
        Assert.Equal(0, GetTitleCache(renderer).GetStats().ItemCount);
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenFastScrollModeEnabled_ShouldReturnNull()
    {
        var renderer = CreateRenderer();
        var songNode = CreateScoreNode(previewImage: "preview.png");
        renderer.SetFastScrollMode(true);

        var texture = renderer.GeneratePreviewImageTexture(songNode);

        Assert.Null(texture);
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenCachedPreviewExists_ShouldAddReferenceAndReturnCachedTexture()
    {
        var renderer = CreateRenderer();
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-preview");
        try
        {
            var previewPath = Path.Combine(tempDir.FullName, "preview.png");
            File.WriteAllText(previewPath, "preview");
            var songNode = CreateScoreNode(filePath: Path.Combine(tempDir.FullName, "chart.dtx"), previewImage: "preview.png");
            var cachedTexture = CreateTextureMock();
            GetPreviewCache(renderer).Add(Path.GetFullPath(previewPath), cachedTexture.Object);

            var texture = renderer.GeneratePreviewImageTexture(songNode);

            Assert.Same(cachedTexture.Object, texture);
            cachedTexture.Verify(x => x.AddReference(), Times.Once);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenPreviewIsMissing_ShouldReturnNull()
    {
        var renderer = CreateRenderer();

        var texture = renderer.GeneratePreviewImageTexture(CreateScoreNode(previewImage: null));

        Assert.Null(texture);
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenPreviewFileMissing_ShouldReturnNullWithoutLoading()
    {
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-missing");
        try
        {
            var songNode = CreateScoreNode(filePath: Path.Combine(tempDir.FullName, "chart.dtx"), previewImage: "preview.png");

            var texture = renderer.GeneratePreviewImageTexture(songNode);

            Assert.Null(texture);
            resourceManager.Verify(x => x.TryLoadTexture(It.IsAny<string>(), out It.Ref<ITexture>.IsAny), Times.Never);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenPreviewFileIsTooLarge_ShouldReturnNullWithoutLoading()
    {
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-large");
        try
        {
            var previewPath = Path.Combine(tempDir.FullName, "preview-large.png");
            File.WriteAllBytes(previewPath, new byte[8 * 1024 * 1024 + 1]);
            var songNode = CreateScoreNode(filePath: Path.Combine(tempDir.FullName, "chart.dtx"), previewImage: "preview-large.png");

            var texture = renderer.GeneratePreviewImageTexture(songNode);

            Assert.Null(texture);
            resourceManager.Verify(x => x.TryLoadTexture(It.IsAny<string>(), out It.Ref<ITexture>.IsAny), Times.Never);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenPreviewExceedsLegacy500KbCap_ShouldLoad()
    {
        // Real jackets routinely exceed 500KB; the guard only rejects
        // pathological files (>8 MiB), so a 1 MiB jacket must load.
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-bigjacket");
        try
        {
            var previewPath = Path.Combine(tempDir.FullName, "jacket.png");
            File.WriteAllBytes(previewPath, new byte[1024 * 1024]);
            var songNode = CreateScoreNode(filePath: Path.Combine(tempDir.FullName, "chart.dtx"), previewImage: "jacket.png");
            var loaded = CreateTextureMock();
            var loadedTexture = loaded.Object;
            resourceManager.Setup(x => x.TryLoadTexture(previewPath, out loadedTexture)).Returns(true);

            var texture = renderer.GeneratePreviewImageTexture(songNode);

            Assert.Same(loaded.Object, texture);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenLoadFails_ShouldNegativeCacheAndDisposeFallback()
    {
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-corrupt");
        try
        {
            var previewPath = Path.Combine(tempDir.FullName, "jacket.png");
            File.WriteAllBytes(previewPath, new byte[16]);
            var songNode = CreateScoreNode(filePath: Path.Combine(tempDir.FullName, "chart.dtx"), previewImage: "jacket.png");
            var fallback = CreateTextureMock();
            var fallbackTexture = fallback.Object;
            resourceManager.Setup(x => x.TryLoadTexture(previewPath, out fallbackTexture)).Returns(false);

            Assert.Null(renderer.GeneratePreviewImageTexture(songNode));
            // A second generation must not re-read or re-decode the file.
            Assert.Null(renderer.GeneratePreviewImageTexture(songNode));

            resourceManager.Verify(x => x.TryLoadTexture(previewPath, out It.Ref<ITexture>.IsAny), Times.Once);
            fallback.Verify(x => x.Dispose(), Times.Once);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenPreviewFileAppearsAfterMiss_ShouldRetryAndLoad()
    {
        // A missing jacket is not a load failure, so the path must not be
        // negative-cached: once the file exists the next attempt loads it.
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-appearing");
        try
        {
            var previewPath = Path.Combine(tempDir.FullName, "jacket.png");
            var songNode = CreateScoreNode(filePath: Path.Combine(tempDir.FullName, "chart.dtx"), previewImage: "jacket.png");

            Assert.Null(renderer.GeneratePreviewImageTexture(songNode));

            File.WriteAllText(previewPath, "preview");
            var loaded = CreateTextureMock();
            var loadedTexture = loaded.Object;
            resourceManager.Setup(x => x.TryLoadTexture(previewPath, out loadedTexture)).Returns(true);

            Assert.Same(loaded.Object, renderer.GeneratePreviewImageTexture(songNode));
            resourceManager.Verify(x => x.TryLoadTexture(previewPath, out It.Ref<ITexture>.IsAny), Times.Once);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenOversizedPreviewIsReplaced_ShouldRetryAndLoad()
    {
        // The size guard rejects the file without negative-caching the path,
        // so a jacket swapped in later is picked up on the next generation.
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-shrunk");
        try
        {
            var previewPath = Path.Combine(tempDir.FullName, "jacket.png");
            File.WriteAllBytes(previewPath, new byte[8 * 1024 * 1024 + 1]);
            var songNode = CreateScoreNode(filePath: Path.Combine(tempDir.FullName, "chart.dtx"), previewImage: "jacket.png");

            Assert.Null(renderer.GeneratePreviewImageTexture(songNode));

            File.WriteAllText(previewPath, "preview");
            var loaded = CreateTextureMock();
            var loadedTexture = loaded.Object;
            resourceManager.Setup(x => x.TryLoadTexture(previewPath, out loadedTexture)).Returns(true);

            Assert.Same(loaded.Object, renderer.GeneratePreviewImageTexture(songNode));
            resourceManager.Verify(x => x.TryLoadTexture(previewPath, out It.Ref<ITexture>.IsAny), Times.Once);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void ClearCache_ShouldClearFailedPreviewPaths()
    {
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-clearfail");
        try
        {
            var previewPath = Path.Combine(tempDir.FullName, "jacket.png");
            File.WriteAllBytes(previewPath, new byte[16]);
            var songNode = CreateScoreNode(filePath: Path.Combine(tempDir.FullName, "chart.dtx"), previewImage: "jacket.png");
            var fallback = CreateTextureMock();
            var fallbackTexture = fallback.Object;
            resourceManager.Setup(x => x.TryLoadTexture(previewPath, out fallbackTexture)).Returns(false);

            Assert.Null(renderer.GeneratePreviewImageTexture(songNode));
            renderer.ClearCache();
            Assert.Null(renderer.GeneratePreviewImageTexture(songNode));

            // The failure marker was cleared, so the load is retried once.
            resourceManager.Verify(x => x.TryLoadTexture(previewPath, out It.Ref<ITexture>.IsAny), Times.Exactly(2));
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void GeneratePreviewImageTexture_WhenDifferentSongsShareFilename_ShouldNotCollideCache()
    {
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        var rootDir = Directory.CreateTempSubdirectory("dtxmania-songbar-cache-key");
        try
        {
            var songDir1 = Directory.CreateDirectory(Path.Combine(rootDir.FullName, "song1"));
            var songDir2 = Directory.CreateDirectory(Path.Combine(rootDir.FullName, "song2"));
            var previewPath1 = Path.Combine(songDir1.FullName, "preview.png");
            var previewPath2 = Path.Combine(songDir2.FullName, "preview.png");
            File.WriteAllText(previewPath1, "1");
            File.WriteAllText(previewPath2, "2");

            var texture1 = CreateTextureMock();
            var texture2 = CreateTextureMock();
            var loaded1 = texture1.Object;
            var loaded2 = texture2.Object;
            resourceManager.Setup(x => x.TryLoadTexture(previewPath1, out loaded1)).Returns(true);
            resourceManager.Setup(x => x.TryLoadTexture(previewPath2, out loaded2)).Returns(true);

            var first = renderer.GeneratePreviewImageTexture(CreateScoreNode(filePath: Path.Combine(songDir1.FullName, "chart.dtx"), previewImage: "preview.png"));
            var second = renderer.GeneratePreviewImageTexture(CreateScoreNode(filePath: Path.Combine(songDir2.FullName, "chart.dtx"), previewImage: "preview.png"));

            Assert.Same(texture1.Object, first);
            Assert.Same(texture2.Object, second);
            resourceManager.Verify(x => x.TryLoadTexture(previewPath1, out It.Ref<ITexture>.IsAny), Times.Once);
            resourceManager.Verify(x => x.TryLoadTexture(previewPath2, out It.Ref<ITexture>.IsAny), Times.Once);
        }
        finally
        {
            rootDir.Delete(true);
        }
    }

    [Fact]
    public void LoadPreviewImage_WhenFastScrollModeEnabled_ShouldReturnNullWithoutLoading()
    {
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        renderer.SetFastScrollMode(true);

        var texture = InvokePrivate<ITexture?>(renderer, "LoadPreviewImage", CreateScoreNode(filePath: "/songs/chart.dtx", previewImage: "preview.png"));

        Assert.Null(texture);
        resourceManager.Verify(x => x.TryLoadTexture(It.IsAny<string>(), out It.Ref<ITexture>.IsAny), Times.Never);
    }

    [Fact]
    public void LoadPreviewImage_WhenPreviewFileDoesNotExist_ShouldReturnNull()
    {
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-preview-missing");
        try
        {
            var texture = InvokePrivate<ITexture?>(renderer, "LoadPreviewImage", CreateScoreNode(filePath: Path.Combine(tempDir.FullName, "chart.dtx"), previewImage: "preview.png"));

            Assert.Null(texture);
            resourceManager.Verify(x => x.TryLoadTexture(It.IsAny<string>(), out It.Ref<ITexture>.IsAny), Times.Never);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void LoadPreviewImage_WhenPreviewFileExists_ShouldLoadTexture()
    {
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        var expectedTexture = CreateTextureMock();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-load");
        try
        {
            var previewPath = Path.Combine(tempDir.FullName, "preview.png");
            File.WriteAllText(previewPath, "x");
            var loaded = expectedTexture.Object;
            resourceManager.Setup(x => x.TryLoadTexture(previewPath, out loaded)).Returns(true);

            var texture = InvokePrivate<ITexture?>(renderer, "LoadPreviewImage", CreateScoreNode(filePath: Path.Combine(tempDir.FullName, "chart.dtx"), previewImage: "preview.png"));

            Assert.Same(expectedTexture.Object, texture);
            resourceManager.Verify(x => x.TryLoadTexture(previewPath, out It.Ref<ITexture>.IsAny), Times.Once);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void LoadPreviewImage_WhenResourceManagerThrows_ShouldReturnNull()
    {
        var renderer = CreateRenderer();
        var resourceManager = new Mock<IResourceManager>();
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", resourceManager.Object);
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-throw");
        try
        {
            var previewPath = Path.Combine(tempDir.FullName, "preview.png");
            File.WriteAllText(previewPath, "x");
            resourceManager.Setup(x => x.TryLoadTexture(previewPath, out It.Ref<ITexture>.IsAny)).Throws(new System.ObjectDisposedException("texture"));

            var texture = InvokePrivate<ITexture?>(renderer, "LoadPreviewImage", CreateScoreNode(filePath: Path.Combine(tempDir.FullName, "chart.dtx"), previewImage: "preview.png"));

            Assert.Null(texture);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void GenerateBarInfo_WhenSongNodeIsNull_ShouldReturnNull()
    {
        var renderer = CreateRenderer();

        var barInfo = renderer.GenerateBarInfo(null!, 0);

        Assert.Null(barInfo);
    }

    [Fact]
    public void GenerateBarInfo_WithBoxNode_ShouldPopulateBasicFields()
    {
        var renderer = CreateRenderer();
        var songNode = new SongListNode { Type = NodeType.Box, Title = "Folder" };

        var barInfo = renderer.GenerateBarInfo(songNode, 2);

        Assert.NotNull(barInfo);
        Assert.Same(songNode, barInfo!.SongNode);
        Assert.Equal(BarType.Box, barInfo.BarType);
        Assert.Equal("[Folder]", barInfo.TitleString);
        Assert.Equal(Color.Cyan, barInfo.TextColor);
        Assert.Equal(2, barInfo.DifficultyLevel);
        Assert.False(barInfo.IsSelected);
        Assert.Null(barInfo.PreviewImage);
        Assert.Null(barInfo.ClearLamp);
    }

    [Fact]
    public void UpdateBarInfo_WhenBarInfoIsNull_ShouldReturnWithoutThrowing()
    {
        var renderer = CreateRenderer();

        var exception = Record.Exception(() => renderer.UpdateBarInfo(null!, 1, isSelected: true));

        Assert.Null(exception);
    }

    [Fact]
    public void UpdateBarInfo_WhenDifficultyChangesForScoreNode_ShouldReleaseOldLampAndLoadNewLamp()
    {
        var renderer = CreateRenderer();
        var songNode = CreateScoreNode();
        var oldLamp = CreateTextureMock();
        var newLamp = CreateTextureMock();
        GetClearLampCache(renderer).Add(
            InvokePrivate<string>(
                renderer,
                "GetClearLampCacheKey",
                songNode,
                1,
                PlaySpeedRange.Default),
            newLamp.Object);
        var barInfo = new SongBarInfo
        {
            SongNode = songNode,
            DifficultyLevel = 0,
            PlaySpeedPercent = PlaySpeedRange.Default,
            IsSelected = false,
            ClearLamp = oldLamp.Object
        };

        renderer.UpdateBarInfo(barInfo, 1, isSelected: true);

        oldLamp.Verify(x => x.RemoveReference(), Times.Once);
        Assert.Same(newLamp.Object, barInfo.ClearLamp);
        Assert.Equal(Color.Yellow, barInfo.TextColor);
        newLamp.Verify(x => x.AddReference(), Times.Once);
    }

    [Fact]
    public void UpdateBarInfo_WhenStateUnchangedForScoreNode_ShouldNotReplaceClearLamp()
    {
        var renderer = CreateRenderer();
        var clearLamp = CreateTextureMock();
        var barInfo = new SongBarInfo
        {
            SongNode = CreateScoreNode(),
            DifficultyLevel = 2,
            PlaySpeedPercent = PlaySpeedRange.Default,
            IsSelected = true,
            ClearLamp = clearLamp.Object
        };

        renderer.UpdateBarInfo(barInfo, 2, isSelected: true);

        Assert.Same(clearLamp.Object, barInfo.ClearLamp);
        clearLamp.Verify(x => x.RemoveReference(), Times.Never);
        Assert.Equal(Color.Yellow, barInfo.TextColor);
    }

    [Fact]
    public void GenerateClearLampTexture_WhenSongNodeIsNotScore_ShouldReturnNull()
    {
        var renderer = CreateRenderer();
        var songNode = new SongListNode { Type = NodeType.Box, Title = "Folder" };

        var texture = renderer.GenerateClearLampTexture(songNode, 0);

        Assert.Null(texture);
    }

    [Fact]
    public void GenerateClearLampTexture_WhenCachedTextureExists_ShouldAddReferenceAndReturnCachedTexture()
    {
        var renderer = CreateRenderer();
        var songNode = CreateScoreNode();
        var cachedTexture = CreateTextureMock();
        GetClearLampCache(renderer).Add(
            InvokePrivate<string>(
                renderer,
                "GetClearLampCacheKey",
                songNode,
                0,
                PlaySpeedRange.Default),
            cachedTexture.Object);

        var texture = renderer.GenerateClearLampTexture(songNode, 0);

        Assert.Same(cachedTexture.Object, texture);
        cachedTexture.Verify(x => x.AddReference(), Times.Once);
    }

    [Fact]
    public void GenerateClearLampTexture_WhenGraphicsGeneratorMissing_ShouldReturnNull()
    {
        var renderer = CreateRenderer();
        ReflectionHelpers.SetPrivateField(renderer, "_graphicsGenerator", null);

        var texture = renderer.GenerateClearLampTexture(CreateScoreNode(), 0);

        Assert.Null(texture);
    }

    [Fact]
    public void ClearCache_ShouldDisposeAndEmptyAllCaches()
    {
        var renderer = CreateRenderer();
        var titleTexture = CreateTextureMock();
        var previewTexture = CreateTextureMock();
        var clearLampTexture = CreateTextureMock();
        GetTitleCache(renderer).Add("title", titleTexture.Object);
        GetPreviewCache(renderer).Add("preview", previewTexture.Object);
        GetClearLampCache(renderer).Add("lamp", clearLampTexture.Object);
        ReflectionHelpers.GetPrivateField<Queue<string>>(renderer, "_clearLampCacheOrder")!
            .Enqueue("lamp");

        renderer.ClearCache();

        titleTexture.Verify(x => x.Dispose(), Times.Once);
        previewTexture.Verify(x => x.Dispose(), Times.Once);
        // Lamp textures are owned by the graphics generator, which re-hands
        // them across cache keys — clearing must release the key's reference
        // without disposing the shared texture out from under the generator.
        clearLampTexture.Verify(x => x.RemoveReference(), Times.Once);
        clearLampTexture.Verify(x => x.Dispose(), Times.Never);
        Assert.Equal(0, GetTitleCache(renderer).GetStats().ItemCount);
        Assert.Equal(0, GetPreviewCache(renderer).GetStats().ItemCount);
        Assert.Equal(0, GetClearLampCache(renderer).GetStats().ItemCount);
    }

    [Fact]
    public void EvictGeneratedTextureOverflow_WhenRendererOwned_ShouldDeferDisposalToFlush()
    {
        var renderer = CreateRenderer();
        var rm = new Mock<IResourceManager>(MockBehavior.Strict);
        rm.Setup(x => x.CollectUnusedResources());
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", rm.Object);
        var cache = new CacheManager<string, ITexture>();
        var order = new Queue<string>();
        var held = CreateTextureMock();
        held.SetupGet(x => x.ReferenceCount).Returns(1);
        var free = CreateTextureMock();
        var spare = CreateTextureMock();
        // Stale slot left behind by an already-removed entry must be skipped.
        order.Enqueue("removed");
        cache.Add("held", held.Object);
        order.Enqueue("held");
        cache.Add("free", free.Object);
        order.Enqueue("free");
        cache.Add("spare", spare.Object);
        order.Enqueue("spare");

        InvokePrivate<object?>(renderer, "EvictGeneratedTextureOverflow", cache, order, 2, false);

        Assert.Equal(2, cache.Count);
        Assert.True(cache.TryGet("held", out _));
        Assert.False(cache.TryGet("free", out _));
        Assert.True(cache.TryGet("spare", out _));
        // Renderer-owned textures are not disposed during the draw pass —
        // the deferred SpriteBatch may still hold a queued draw for them.
        held.Verify(x => x.Dispose(), Times.Never);
        free.Verify(x => x.Dispose(), Times.Never);
        spare.Verify(x => x.Dispose(), Times.Never);

        renderer.FlushDeferredReclamation();

        held.Verify(x => x.Dispose(), Times.Never);
        free.Verify(x => x.Dispose(), Times.Once);
        spare.Verify(x => x.Dispose(), Times.Never);
    }

    [Fact]
    public void EvictGeneratedTextureOverflow_WhenGeneratorOwned_ShouldReleaseWithoutDisposing()
    {
        var renderer = CreateRenderer();
        var cache = new CacheManager<string, ITexture>();
        var order = new Queue<string>();
        var lamp = CreateTextureMock();
        cache.Add("lamp0", lamp.Object);
        order.Enqueue("lamp0");
        cache.Add("lamp1", CreateTextureMock().Object);
        order.Enqueue("lamp1");
        cache.Add("lamp2", CreateTextureMock().Object);
        order.Enqueue("lamp2");

        InvokePrivate<object?>(renderer, "EvictGeneratedTextureOverflow", cache, order, 2, true);

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("lamp0", out _));
        // The generator keeps and re-hands shared lamp textures — eviction
        // only drops the key's counted reference, never disposes them.
        lamp.Verify(x => x.RemoveReference(), Times.Once);
        lamp.Verify(x => x.Dispose(), Times.Never);
    }

    [Fact]
    public void EvictGeneratedTextureOverflow_WhenGeneratorOwnedTextureSharedAndReferenced_ShouldStillEvictKeys()
    {
        var renderer = CreateRenderer();
        var cache = new CacheManager<string, ITexture>();
        var order = new Queue<string>();
        // Lamp textures are shared per (difficulty, status): every key maps
        // to one instance, and a live bar keeps its global ReferenceCount
        // above zero. That count cannot stand in for the key's own entry —
        // re-queueing on it would retain every key sharing the lamp.
        var sharedLamp = CreateTextureMock();
        sharedLamp.SetupGet(x => x.ReferenceCount).Returns(3);
        cache.Add("lamp0", sharedLamp.Object);
        order.Enqueue("lamp0");
        cache.Add("lamp1", sharedLamp.Object);
        order.Enqueue("lamp1");
        cache.Add("lamp2", sharedLamp.Object);
        order.Enqueue("lamp2");

        InvokePrivate<object?>(renderer, "EvictGeneratedTextureOverflow", cache, order, 2, true);

        Assert.Equal(2, cache.Count);
        Assert.False(cache.TryGet("lamp0", out _));
        Assert.True(cache.TryGet("lamp1", out _));
        Assert.True(cache.TryGet("lamp2", out _));
        sharedLamp.Verify(x => x.RemoveReference(), Times.Once);
        sharedLamp.Verify(x => x.Dispose(), Times.Never);
    }

    [Fact]
    public void EvictPreviewImageOverflow_ShouldDropCacheReferenceAndDeferReclaim()
    {
        var renderer = CreateRenderer();
        var rm = new Mock<IResourceManager>(MockBehavior.Strict);
        rm.Setup(x => x.CollectUnusedResources());
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", rm.Object);
        var cache = GetPreviewCache(renderer);
        var order = ReflectionHelpers.GetPrivateField<Queue<string>>(renderer, "_previewImageCacheOrder")!;
        const int cacheCap = SongSelectionUILayout.SongBars.VisibleItems * 4;
        var evicted = CreateTextureMock();
        cache.Add("evicted.png", evicted.Object);
        order.Enqueue("evicted.png");
        for (var i = 1; i <= cacheCap; i++)
        {
            cache.Add($"cached{i}.png", CreateTextureMock().Object);
            order.Enqueue($"cached{i}.png");
        }

        InvokePrivate<object?>(renderer, "EvictPreviewImageOverflow");

        Assert.Equal(cacheCap, cache.Count);
        Assert.False(cache.TryGet("evicted.png", out _));
        // Eviction releases the counted reference — it must not dispose a
        // texture a bar could still be drawing, and GPU reclamation is
        // deferred until the deferred SpriteBatch has flushed.
        evicted.Verify(x => x.RemoveReference(), Times.Once);
        evicted.Verify(x => x.Dispose(), Times.Never);
        rm.Verify(x => x.CollectUnusedResources(), Times.Never);

        renderer.FlushDeferredReclamation();

        rm.Verify(x => x.CollectUnusedResources(), Times.Once);
    }

    [Fact]
    public void GenerateBarInfoWithPriority_WhenSongNodeIsNull_ShouldReturnNull()
    {
        var renderer = CreateRenderer();

        var barInfo = renderer.GenerateBarInfoWithPriority(null!, 0, isSelected: false);

        Assert.Null(barInfo);
    }

    [Fact]
    public void GenerateBarInfoWithPriority_WhenFastScrollAndNotSelected_ShouldSkipPreviewImage()
    {
        var renderer = CreateRenderer();
        renderer.SetFastScrollMode(true);
        var songNode = CreateScoreNode(previewImage: "preview.png");
        var titleTexture = CreateTextureMock();
        var clearLampTexture = CreateTextureMock();
        GetTitleCache(renderer).Add(InvokePrivate<string>(renderer, "GetTitleCacheKey", songNode), titleTexture.Object);
        GetClearLampCache(renderer).Add(
            InvokePrivate<string>(
                renderer,
                "GetClearLampCacheKey",
                songNode,
                0,
                PlaySpeedRange.Default),
            clearLampTexture.Object);

        var barInfo = renderer.GenerateBarInfoWithPriority(songNode, 0, isSelected: false);

        Assert.NotNull(barInfo);
        Assert.Same(titleTexture.Object, barInfo.TitleTexture);
        Assert.Same(clearLampTexture.Object, barInfo.ClearLamp);
        Assert.Null(barInfo.PreviewImage);
    }

    [Fact]
    public void GenerateBarInfoWithPriority_WhenPreviewIsAvailable_ShouldPopulatePreviewImage()
    {
        var renderer = CreateRenderer();
        var tempDir = Directory.CreateTempSubdirectory("dtxmania-songbar-priority-preview");
        try
        {
            var previewPath = Path.Combine(tempDir.FullName, "preview.png");
            File.WriteAllText(previewPath, "preview");
            var songNode = new SongListNode
            {
                Type = NodeType.Box,
                Title = "Folder",
                DatabaseChart = new SongChart { FilePath = Path.Combine(tempDir.FullName, "chart.dtx"), PreviewImage = "preview.png" }
            };
            var cachedPreview = CreateTextureMock();
            GetPreviewCache(renderer).Add(Path.GetFullPath(previewPath), cachedPreview.Object);

            var barInfo = renderer.GenerateBarInfoWithPriority(songNode, 1, isSelected: false);

            Assert.NotNull(barInfo);
            Assert.Same(cachedPreview.Object, barInfo!.PreviewImage);
        }
        finally
        {
            tempDir.Delete(true);
        }
    }

    [Fact]
    public void SetFont_ShouldStoreFontAndClearTitleCache()
    {
        var renderer = CreateRenderer();
        var cachedTexture = CreateTextureMock();
        GetTitleCache(renderer).Add("title", cachedTexture.Object);
        var font = (SpriteFont)RuntimeHelpers.GetUninitializedObject(typeof(SpriteFont));

        renderer.SetFont(font);

        Assert.Same(font, ReflectionHelpers.GetPrivateField<SpriteFont>(renderer, "_font"));
        cachedTexture.Verify(x => x.Dispose(), Times.Once);
        Assert.Equal(0, GetTitleCache(renderer).GetStats().ItemCount);
    }

    [Fact]
    public void SetFastScrollMode_ShouldUpdateProperty()
    {
        var renderer = CreateRenderer();

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
    public void GetDisplayText_ShouldReturnExpectedLabel(NodeType nodeType, string expected)
    {
        var renderer = CreateRenderer();
        var songNode = new SongListNode { Type = nodeType, Title = nodeType == NodeType.Box ? "Folder" : "Song" };

        var text = InvokePrivate<string>(renderer, "GetDisplayText", songNode);

        Assert.Equal(expected, text);
    }

    [Theory]
    [InlineData(NodeType.Box, 0, 255, 255, 255)]
    [InlineData(NodeType.BackBox, 255, 165, 0, 255)]
    [InlineData(NodeType.Random, 255, 0, 255, 255)]
    [InlineData(NodeType.Score, 255, 255, 255, 255)]
    public void GetNodeTypeColor_ShouldReturnExpectedColor(NodeType nodeType, byte r, byte g, byte b, byte a)
    {
        var renderer = CreateRenderer();
        var songNode = new SongListNode { Type = nodeType };

        var color = InvokePrivate<Color>(renderer, "GetNodeTypeColor", songNode);

        Assert.Equal(new Color(r, g, b, a), color);
    }

    [Fact]
    public void GetClearStatus_ShouldMapScoreHistoryToExpectedStatus()
    {
        var renderer = CreateRenderer();
        var songNode = CreateScoreNode(scores:
        [
            new SongScore { PlayCount = 0, BestRank = 99, FullCombo = false },
            new SongScore { PlayCount = 3, BestRank = 92, FullCombo = true },
            new SongScore { PlayCount = 2, BestRank = 95, FullCombo = false },
            new SongScore { PlayCount = 1, BestRank = 50, FullCombo = false }
        ]);

        Assert.Equal(
            ClearStatus.NotPlayed,
            InvokeStaticPrivate<ClearStatus>(
                "ResolveClearStatus",
                songNode,
                0,
                PlaySpeedRange.Default));
        Assert.Equal(
            ClearStatus.FullCombo,
            InvokeStaticPrivate<ClearStatus>(
                "ResolveClearStatus",
                songNode,
                1,
                PlaySpeedRange.Default));
        Assert.Equal(
            ClearStatus.Clear,
            InvokeStaticPrivate<ClearStatus>(
                "ResolveClearStatus",
                songNode,
                2,
                PlaySpeedRange.Default));
        Assert.Equal(
            ClearStatus.Failed,
            InvokeStaticPrivate<ClearStatus>(
                "ResolveClearStatus",
                songNode,
                3,
                PlaySpeedRange.Default));
    }

    [Fact]
    public void GetClearLampCacheKey_ShouldIncludeDifficultyAndStatus()
    {
        var renderer = CreateRenderer();
        var songNode = CreateScoreNode(scores: [new SongScore { PlayCount = 1, FullCombo = true }]);

        var cacheKey = InvokePrivate<string>(
            renderer,
            "GetClearLampCacheKey",
            songNode,
            0,
            PlaySpeedRange.Default);

        Assert.Contains("_0_", cacheKey);
        Assert.Contains(nameof(ClearStatus.FullCombo), cacheKey);
    }

    [Theory]
    [InlineData(NodeType.Score, BarType.Score)]
    [InlineData(NodeType.Box, BarType.Box)]
    [InlineData(NodeType.BackBox, BarType.Other)]
    [InlineData(NodeType.Random, BarType.Other)]
    public void GetBarType_ShouldMapNodeTypeToExpectedBarType(NodeType nodeType, BarType expected)
    {
        var renderer = CreateRenderer();
        var songNode = new SongListNode { Type = nodeType };

        var barType = InvokePrivate<BarType>(renderer, "GetBarType", songNode);

        Assert.Equal(expected, barType);
    }

    [Fact]
    public void GetResolvedPreviewImagePath_WhenChartPathOrPreviewMissing_ShouldReturnNull()
    {
        Assert.Null(InvokeStaticPrivate<string>("GetResolvedPreviewImagePath", new SongListNode
        {
            DatabaseChart = new SongChart { FilePath = null, PreviewImage = "preview.png" }
        }));

        Assert.Null(InvokeStaticPrivate<string>("GetResolvedPreviewImagePath", new SongListNode
        {
            DatabaseChart = new SongChart { FilePath = "/songs/chart.dtx", PreviewImage = null }
        }));
    }

    [Fact]
    public void SongBarInfo_Dispose_ShouldRemoveReferencesAndNullTextures()
    {
        var titleTexture = CreateTextureMock();
        var previewTexture = CreateTextureMock();
        var clearLampTexture = CreateTextureMock();
        var barInfo = new SongBarInfo
        {
            TitleTexture = titleTexture.Object,
            PreviewImage = previewTexture.Object,
            ClearLamp = clearLampTexture.Object
        };

        barInfo.Dispose();

        titleTexture.Verify(x => x.RemoveReference(), Times.Once);
        previewTexture.Verify(x => x.RemoveReference(), Times.Once);
        clearLampTexture.Verify(x => x.RemoveReference(), Times.Once);
        Assert.Null(barInfo.TitleTexture);
        Assert.Null(barInfo.PreviewImage);
        Assert.Null(barInfo.ClearLamp);
    }

    [Fact]
    public void Dispose_WhenNotOwningSharedRenderTarget_ShouldClearCachesAndNullManagedFields()
    {
        var renderer = CreateRenderer();
        var cachedTexture = CreateTextureMock();
        GetTitleCache(renderer).Add("title", cachedTexture.Object);

        renderer.Dispose();

        cachedTexture.Verify(x => x.Dispose(), Times.Once);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(renderer, "_disposed"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(renderer, "_titleRenderTarget"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(renderer, "_clearLampRenderTarget"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(renderer, "_spriteBatch"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(renderer, "_whitePixel"));
        Assert.Null(ReflectionHelpers.GetPrivateField<object>(renderer, "_graphicsGenerator"));
    }

    private static SongBarRenderer CreateRenderer()
    {
        var renderer = (SongBarRenderer)RuntimeHelpers.GetUninitializedObject(typeof(SongBarRenderer));
        ReflectionHelpers.SetPrivateField(renderer, "_graphicsDevice", (GraphicsDevice)RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice)));
        ReflectionHelpers.SetPrivateField(renderer, "_resourceManager", new Mock<IResourceManager>().Object);
        ReflectionHelpers.SetPrivateField(renderer, "_font", null);
        ReflectionHelpers.SetPrivateField(renderer, "_whitePixel", null);
        ReflectionHelpers.SetPrivateField(renderer, "_titleTextureCache", new CacheManager<string, ITexture>());
        ReflectionHelpers.SetPrivateField(renderer, "_previewImageCache", new CacheManager<string, ITexture>());
        ReflectionHelpers.SetPrivateField(renderer, "_failedPreviewPaths", new HashSet<string>(AppPaths.SkinPathComparer));
        ReflectionHelpers.SetPrivateField(renderer, "_clearLampCache", new CacheManager<string, ITexture>());
        ReflectionHelpers.SetPrivateField(renderer, "_titleTextureCacheOrder", new Queue<string>());
        ReflectionHelpers.SetPrivateField(renderer, "_previewImageCacheOrder", new Queue<string>());
        ReflectionHelpers.SetPrivateField(renderer, "_clearLampCacheOrder", new Queue<string>());
        ReflectionHelpers.SetPrivateField(renderer, "_pendingDispose", new Queue<ITexture>());
        ReflectionHelpers.SetPrivateField(renderer, "_reclamationPending", false);
        ReflectionHelpers.SetPrivateField(renderer, "_titleRenderTarget", null);
        ReflectionHelpers.SetPrivateField(renderer, "_clearLampRenderTarget", null);
        ReflectionHelpers.SetPrivateField(renderer, "_ownsSharedRenderTarget", false);
        ReflectionHelpers.SetPrivateField(renderer, "_spriteBatch", null);
        ReflectionHelpers.SetPrivateField(renderer, "_graphicsGenerator", null);
        ReflectionHelpers.SetPrivateField(renderer, "_isFastScrollMode", false);
        ReflectionHelpers.SetPrivateField(renderer, "_playSpeedPercent", PlaySpeedRange.Default);
        ReflectionHelpers.SetPrivateField(renderer, "_disposed", false);
        return renderer;
    }

    private static SongListNode CreateScoreNode(
        string? filePath = "/songs/chart.dtx",
        string? previewImage = null,
        SongScore[]? scores = null)
    {
        var song = new DTXMania.Game.Lib.Song.Entities.Song { Title = "Song" };
        var chart = new SongChart
        {
            FilePath = filePath,
            PreviewImage = previewImage,
            Song = song
        };
        song.Charts = [chart];

        return new SongListNode
        {
            Type = NodeType.Score,
            Title = "Song",
            DatabaseSong = song,
            DatabaseChart = chart,
            Scores = scores ?? [new SongScore { PlayCount = 1, BestRank = 99, FullCombo = false }]
        };
    }

    private static CacheManager<string, ITexture> GetTitleCache(SongBarRenderer renderer)
    {
        return ReflectionHelpers.GetPrivateField<CacheManager<string, ITexture>>(renderer, "_titleTextureCache")!;
    }

    private static CacheManager<string, ITexture> GetPreviewCache(SongBarRenderer renderer)
    {
        return ReflectionHelpers.GetPrivateField<CacheManager<string, ITexture>>(renderer, "_previewImageCache")!;
    }

    private static CacheManager<string, ITexture> GetClearLampCache(SongBarRenderer renderer)
    {
        return ReflectionHelpers.GetPrivateField<CacheManager<string, ITexture>>(renderer, "_clearLampCache")!;
    }

    private static Mock<ITexture> CreateTextureMock()
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.SourcePath).Returns("cached");
        return texture;
    }

    private static T InvokePrivate<T>(object target, string methodName, params object?[] args)
    {
        var method = FindMatchingMethod(target.GetType(), methodName, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic, args);
        Assert.NotNull(method);
        return (T)method!.Invoke(target, args)!;
    }

    private static T InvokeStaticPrivate<T>(string methodName, params object?[] args)
    {
        var method = FindMatchingMethod(typeof(SongBarRenderer), methodName, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic, args);
        Assert.NotNull(method);
        return (T)method!.Invoke(null, args)!;
    }

    private static System.Reflection.MethodInfo? FindMatchingMethod(Type targetType, string methodName, System.Reflection.BindingFlags bindingFlags, params object?[] args)
    {
        foreach (var method in targetType.GetMethods(bindingFlags))
        {
            if (!string.Equals(method.Name, methodName, StringComparison.Ordinal))
            {
                continue;
            }

            var parameters = method.GetParameters();
            if (parameters.Length != args.Length)
            {
                continue;
            }

            var isMatch = true;
            for (var i = 0; i < parameters.Length; i++)
            {
                var argument = args[i];
                var parameterType = parameters[i].ParameterType;

                if (argument == null)
                {
                    if (parameterType.IsValueType && Nullable.GetUnderlyingType(parameterType) == null)
                    {
                        isMatch = false;
                        break;
                    }

                    continue;
                }

                if (!parameterType.IsInstanceOfType(argument))
                {
                    isMatch = false;
                    break;
                }
            }

            if (isMatch)
            {
                return method;
            }
        }

        return null;
    }
}

using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Moq;
using Xunit;

namespace DTXMania.Test.UI;

[Trait("Category", "UI")]
public class SongListDisplayLogicTests
{
    [Theory]
    [InlineData(NodeType.BackBox, ".. (Back)")]
    [InlineData(NodeType.Box, "[Folder]")]
    [InlineData(NodeType.Random, "*** RANDOM SELECT ***")]
    [InlineData(NodeType.Score, "Song")]
    public void GetDisplayText_ShouldMapNodeTypes(NodeType nodeType, string expected)
    {
        var display = new SongListDisplay();
        var node = new SongListNode { Type = nodeType, Title = nodeType == NodeType.Box ? "Folder" : "Song" };

        var text = InvokePrivate<string>(display, "GetDisplayText", node);

        Assert.Equal(expected, text);
    }

    [Fact]
    public void TruncateTextToWidth_ShouldReturnOriginalOrTruncated()
    {
        var display = new SongListDisplay();
        var font = new Mock<IFont>();
        font.Setup(x => x.MeasureString(It.IsAny<string>())).Returns((string s) => new Vector2(s.Length * 10, 10));

        var original = InvokePrivate<string>(display, "TruncateTextToWidth", "ABC", 100f, font.Object);
        var truncated = InvokePrivate<string>(display, "TruncateTextToWidth", "ABCDEFGHIJ", 40f, font.Object);

        Assert.Equal("ABC", original);
        Assert.EndsWith("...", truncated);
        Assert.True(font.Object.MeasureString(truncated).X <= 40f);
    }

    [Fact]
    public void WrapTextToWidth_WhenFontIsNull_ShouldReturnSingleOriginalLine()
    {
        var display = new SongListDisplay();

        var lines = InvokePrivate<string[]>(display, "WrapTextToWidth", "hello world", 100f, null, 1f);

        Assert.Single(lines);
        Assert.Equal("hello world", lines[0]);
    }

    [Fact]
    public void GetScrollAcceleration_ShouldRespectThresholdsFastModeAndMultiplier()
    {
        var display = new SongListDisplay();

        Assert.Equal(15, InvokePrivate<int>(display, "GetScrollAcceleration", 100));
        Assert.Equal(25, InvokePrivate<int>(display, "GetScrollAcceleration", 300));
        Assert.Equal(40, InvokePrivate<int>(display, "GetScrollAcceleration", 800));
        Assert.Equal(60, InvokePrivate<int>(display, "GetScrollAcceleration", 1200));

        SetField(display, "_consecutiveInputCount", 3);
        SetField(display, "_lastInputTime", DateTime.UtcNow);
        Assert.Equal(30, InvokePrivate<int>(display, "GetScrollAcceleration", 100));

        display.ScrollSpeedMultiplier = 2.0f;
        Assert.Equal(60, InvokePrivate<int>(display, "GetScrollAcceleration", 100));
    }

    [Fact]
    public void RapidInputHelpers_ShouldToggleFastScrollMode()
    {
        var display = new SongListDisplay();

        // stale input should not be fast mode
        SetField(display, "_consecutiveInputCount", 5);
        SetField(display, "_lastInputTime", DateTime.UtcNow.AddSeconds(-2));
        Assert.False(InvokePrivate<bool>(display, "IsFastScrollMode"));

        // rapid input should be fast mode
        SetField(display, "_consecutiveInputCount", 3);
        SetField(display, "_lastInputTime", DateTime.UtcNow);
        Assert.True(InvokePrivate<bool>(display, "IsFastScrollMode"));

        // TrackRapidInput should increment within the window and reset when outside window
        SetField(display, "_lastInputTime", DateTime.UtcNow);
        SetField(display, "_consecutiveInputCount", 1);
        InvokePrivate<object?>(display, "TrackRapidInput");
        Assert.Equal(2, GetField<int>(display, "_consecutiveInputCount"));

        SetField(display, "_lastInputTime", DateTime.UtcNow.AddSeconds(-2));
        InvokePrivate<object?>(display, "TrackRapidInput");
        Assert.Equal(1, GetField<int>(display, "_consecutiveInputCount"));
    }

    [Fact]
    public void UpdateScrollTarget_AndUpdateScrollAnimation_ShouldMoveTowardsTarget()
    {
        var display = new SongListDisplay();

        SetField(display, "_selectedIndex", 7);
        InvokePrivate<object?>(display, "UpdateScrollTarget");
        Assert.Equal(700, GetField<int>(display, "_targetScrollCounter"));

        SetField(display, "_currentScrollCounter", 0);
        SetField(display, "_targetScrollCounter", 300);
        InvokePrivate<object?>(display, "UpdateScrollAnimation", 1.0 / 60.0);
        var forward = GetField<int>(display, "_currentScrollCounter");
        Assert.InRange(forward, 1, 300);

        SetField(display, "_currentScrollCounter", 0);
        SetField(display, "_targetScrollCounter", -300);
        InvokePrivate<object?>(display, "UpdateScrollAnimation", 1.0 / 60.0);
        var backward = GetField<int>(display, "_currentScrollCounter");
        Assert.InRange(backward, -300, -1);
    }

    [Fact]
    public void IsScrolling_ShouldBeFalseWhenBothCountersAreZero_AndTrueOtherwise()
    {
        var display = new SongListDisplay();

        SetField(display, "_targetScrollCounter", 0);
        SetField(display, "_currentScrollCounter", 0);
        Assert.False(display.IsScrolling);

        SetField(display, "_targetScrollCounter", 1);
        Assert.True(display.IsScrolling);

        SetField(display, "_targetScrollCounter", 0);
        SetField(display, "_currentScrollCounter", -1);
        Assert.True(display.IsScrolling);
    }

    [Fact]
    public void SelectedIndex_WhenCurrentListIsNull_ShouldReturnWithoutChangingState()
    {
        var display = new SongListDisplay();

        SetField(display, "_currentList", null);
        SetField(display, "_selectedIndex", 5);

        display.SelectedIndex = 12;

        Assert.Equal(5, GetField<int>(display, "_selectedIndex"));
    }

    [Fact]
    public void SelectedIndex_WhenCurrentListIsEmpty_ShouldReturnWithoutChangingState()
    {
        var display = new SongListDisplay();

        SetField(display, "_currentList", new List<SongListNode>());
        SetField(display, "_selectedIndex", 3);

        display.SelectedIndex = 9;

        Assert.Equal(3, GetField<int>(display, "_selectedIndex"));
    }

    [Fact]
    public void UpdateSelection_ShouldWrapNegativeIndexAndRaiseSelectionChanged()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(3)
        };

        SongSelectionChangedEventArgs? args = null;
        display.SelectionChanged += (_, e) => args = e;

        SetField(display, "_selectedIndex", -1);
        InvokePrivate<object?>(display, "UpdateSelection");

        Assert.NotNull(args);
        Assert.Equal(display.CurrentList[2], display.SelectedSong);
        Assert.Equal(display.SelectedSong, args!.SelectedSong);
    }

    [Fact]
    public void UpdateSelection_WhenListIsEmpty_ShouldSetSelectedSongNullAndNotRaiseEvent()
    {
        var display = new SongListDisplay
        {
            CurrentList = new List<SongListNode>()
        };

        var fired = false;
        display.SelectionChanged += (_, _) => fired = true;

        InvokePrivate<object?>(display, "UpdateSelection");

        Assert.Null(display.SelectedSong);
        Assert.False(fired);
    }

    [Fact]
    public void PreGenerateAdjacentSongTextures_ShouldQueueAroundSelectedIndex_AndSkipWhenNegative()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(20)
        };

        var queue = GetTextureGenerationQueue(display);
        queue.Clear();

        SetField(display, "_selectedIndex", 10);
        SetField(display, "_currentDifficulty", 1);
        InvokePrivate<object?>(display, "PreGenerateAdjacentSongTextures");
        Assert.Equal(10, queue.Count);

        queue.Clear();
        SetField(display, "_selectedIndex", -1);
        InvokePrivate<object?>(display, "PreGenerateAdjacentSongTextures");
        Assert.Empty(queue);
    }

    [Fact]
    public void PreGenerateAdjacentSongTextures_ShouldSkipAlreadyCachedAdjacentSong()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(20)
        };

        var queue = GetTextureGenerationQueue(display);
        queue.Clear();

        var barInfoCache = (IDictionary)GetField<object>(display, "_barInfoCache");
        barInfoCache.Clear();

        SetField(display, "_selectedIndex", 10);
        SetField(display, "_currentDifficulty", 1);

        var cachedSong = display.CurrentList[9]; // offset -1 song around selection
        barInfoCache[$"{cachedSong.GetHashCode()}_1"] = new SongBarInfo();

        InvokePrivate<object?>(display, "PreGenerateAdjacentSongTextures");

        Assert.Equal(9, queue.Count);
    }

    [Fact]
    public void QueueTextureGenerationForNewBars_ShouldTrackVisibleIndicesAndQueueRequests()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(3)
        };

        SetField(display, "_currentScrollCounter", 0);
        SetField(display, "_currentDifficulty", 0);

        var queue = GetTextureGenerationQueue(display);
        queue.Clear();

        var barInfoCache = (IDictionary)GetField<object>(display, "_barInfoCache");
        barInfoCache.Clear();

        InvokePrivate<object?>(display, "QueueTextureGenerationForNewBars");

        var visible = GetField<HashSet<int>>(display, "_visibleBarIndices");
        Assert.Equal(3, visible.Count);
        Assert.True(queue.Count > 0);
    }

    [Fact]
    public void QueueTextureGenerationForNewBars_WhenAllVisibleCached_ShouldNotQueueRequests()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(3)
        };

        SetField(display, "_currentScrollCounter", 0);
        SetField(display, "_currentDifficulty", 0);

        var queue = GetTextureGenerationQueue(display);
        queue.Clear();

        var barInfoCache = (IDictionary)GetField<object>(display, "_barInfoCache");
        barInfoCache.Clear();
        foreach (var song in display.CurrentList)
        {
            barInfoCache[$"{song.GetHashCode()}_0"] = new SongBarInfo();
        }

        InvokePrivate<object?>(display, "QueueTextureGenerationForNewBars");

        Assert.Empty(queue);
        var visible = GetField<HashSet<int>>(display, "_visibleBarIndices");
        Assert.Equal(3, visible.Count);
    }

    [Fact]
    public void QueueTextureGenerationForNewBars_WhenScrollWrapsNegative_ShouldQueueWrappedSelectedRequestFirst()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(3)
        };

        SetField(display, "_currentScrollCounter", -100);
        SetField(display, "_currentDifficulty", 2);

        var queue = GetTextureGenerationQueue(display);
        queue.Clear();

        var barInfoCache = (IDictionary)GetField<object>(display, "_barInfoCache");
        barInfoCache.Clear();

        InvokePrivate<object?>(display, "QueueTextureGenerationForNewBars");

        Assert.NotEmpty(queue);

        var firstRequest = queue[0];
        Assert.Equal(display.CurrentList[2], firstRequest.SongNode);
        Assert.Equal(2, firstRequest.SongIndex);
        Assert.Equal(2, firstRequest.Difficulty);
        Assert.True(firstRequest.IsSelected);
        Assert.Equal(100, firstRequest.Priority);

        var queuedIndices = queue
            .Select(item => item.SongIndex)
            .Distinct()
            .OrderBy(index => index)
            .ToArray();
        Assert.Equal(new[] { 0, 1, 2 }, queuedIndices);
    }

    [Fact]
    public void QueueTextureGenerationForNewBars_WhenOnlyDifferentDifficultyCached_ShouldQueueCurrentDifficultyRequests()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(3)
        };

        SetField(display, "_currentScrollCounter", 0);
        SetField(display, "_currentDifficulty", 1);

        var queue = GetTextureGenerationQueue(display);
        queue.Clear();

        var barInfoCache = (IDictionary)GetField<object>(display, "_barInfoCache");
        barInfoCache.Clear();
        foreach (var song in display.CurrentList)
        {
            barInfoCache[$"{song.GetHashCode()}_0"] = new SongBarInfo();
        }

        InvokePrivate<object?>(display, "QueueTextureGenerationForNewBars");

        var visibleItems = (int)typeof(SongListDisplay)
            .GetField("VISIBLE_ITEMS", BindingFlags.NonPublic | BindingFlags.Static)!
            .GetRawConstantValue()!;
        Assert.Equal(visibleItems, queue.Count);

        var queuedBarIndices = queue
            .Select(item => item.BarIndex)
            .OrderBy(index => index)
            .ToArray();
        Assert.Equal(Enumerable.Range(0, visibleItems).ToArray(), queuedBarIndices);

        Assert.All(queue, item =>
        {
            Assert.Equal(1, item.Difficulty);
        });
    }

    [Fact]
    public void InsertTextureRequestSorted_ShouldKeepDescendingPriorityOrder()
    {
        var display = new SongListDisplay();
        var queue = GetTextureGenerationQueue(display);
        queue.Clear();

        static TextureGenerationRequest CreateRequest(int priority) => new()
        {
            SongNode = new SongListNode { Type = NodeType.Score, Title = "S" + priority },
            SongIndex = priority,
            BarIndex = 0,
            Difficulty = 0,
            IsSelected = false,
            Priority = priority
        };

        InvokePrivate<object?>(display, "InsertTextureRequestSorted", CreateRequest(50));
        InvokePrivate<object?>(display, "InsertTextureRequestSorted", CreateRequest(100));
        InvokePrivate<object?>(display, "InsertTextureRequestSorted", CreateRequest(75));

        Assert.Equal(new[] { 100, 75, 50 }, queue.Select(item => item.Priority).ToArray());
    }

    [Fact]
    public void GetSkinBarTexture_ShouldUseFallbackTexturesPerBarType()
    {
        var display = new SongListDisplay();
        var score = new Mock<ITexture>().Object;
        var selected = new Mock<ITexture>().Object;
        var box = new Mock<ITexture>().Object;

        SetField(display, "_skinBarTexturesLoaded", true);
        SetField(display, "_barScoreTexture", score);
        SetField(display, "_barScoreSelectedTexture", selected);
        SetField(display, "_barBoxTexture", box);
        SetField(display, "_barBoxSelectedTexture", null);
        SetField(display, "_barOtherTexture", null);
        SetField(display, "_barOtherSelectedTexture", null);

        Assert.Same(selected, InvokePrivate<ITexture?>(display, "GetSkinBarTexture", BarType.Score, true));
        Assert.Same(score, InvokePrivate<ITexture?>(display, "GetSkinBarTexture", BarType.Score, false));
        Assert.Same(selected, InvokePrivate<ITexture?>(display, "GetSkinBarTexture", BarType.Box, true));
        Assert.Same(box, InvokePrivate<ITexture?>(display, "GetSkinBarTexture", BarType.Box, false));
        Assert.Same(selected, InvokePrivate<ITexture?>(display, "GetSkinBarTexture", BarType.Other, true));
        Assert.Same(score, InvokePrivate<ITexture?>(display, "GetSkinBarTexture", BarType.Other, false));
    }

    [Fact]
    public void UpdatePendingTextures_WhenRendererIsNull_ShouldNotThrow()
    {
        var display = new SongListDisplay();

        var ex = Record.Exception(() => InvokePrivate<object?>(display, "UpdatePendingTextures"));

        Assert.Null(ex);
    }

    [Fact]
    public void PropertySetters_ShouldUpdateStateWithoutRenderer()
    {
        var display = new SongListDisplay();

        display.Font = null;
        Assert.Null(display.Font);

        display.WhitePixel = null;
        Assert.Null(display.WhitePixel);

        var managedFont = new Mock<IFont>();
        managedFont.SetupGet(x => x.SpriteFont).Returns((Microsoft.Xna.Framework.Graphics.SpriteFont?)null);

        display.ManagedFont = managedFont.Object;
        Assert.Same(managedFont.Object, display.ManagedFont);
        Assert.Null(display.Font);

        display.ManagedFont = null;
        Assert.Null(display.ManagedFont);
        Assert.Null(display.Font);
    }

    [Fact]
    public void FontSetter_WhenManagedFontExists_ShouldNotClearManagedFont()
    {
        var display = new SongListDisplay();
        var managedFont = new Mock<IFont>();
        managedFont.SetupGet(x => x.SpriteFont).Returns((Microsoft.Xna.Framework.Graphics.SpriteFont?)null);
        display.ManagedFont = managedFont.Object;

        display.Font = null;

        Assert.Same(managedFont.Object, display.ManagedFont);
        Assert.Null(display.Font);
    }

    [Fact]
    public void FontSetter_WhenBarRendererIsPresentButUninitialized_ShouldThrow()
    {
        var display = new SongListDisplay();
#pragma warning disable SYSLIB0050
        var fakeRenderer = (SongBarRenderer)FormatterServices.GetUninitializedObject(typeof(SongBarRenderer));
#pragma warning restore SYSLIB0050
        SetField(display, "_barRenderer", fakeRenderer);

        var ex = Record.Exception(() => display.Font = null);

        Assert.IsType<NullReferenceException>(ex);
        Assert.Null(display.Font);
    }

    [Fact]
    public void ManagedFontSetter_WhenBarRendererIsPresentButUninitialized_ShouldThrow()
    {
        var display = new SongListDisplay();
#pragma warning disable SYSLIB0050
        var fakeRenderer = (SongBarRenderer)FormatterServices.GetUninitializedObject(typeof(SongBarRenderer));
#pragma warning restore SYSLIB0050
        SetField(display, "_barRenderer", fakeRenderer);
        var managedFont = new Mock<IFont>();
        managedFont.SetupGet(x => x.SpriteFont).Returns((Microsoft.Xna.Framework.Graphics.SpriteFont?)null);

        var ex = Record.Exception(() => display.ManagedFont = managedFont.Object);

        Assert.IsType<NullReferenceException>(ex);
        Assert.Same(managedFont.Object, display.ManagedFont);
        Assert.Null(display.Font);
    }

    [Fact]
    public void SetResourceManager_WhenCommentBarMissing_ShouldLoadCommentBarTexture()
    {
        var display = new SongListDisplay();
        var resourceManager = new Mock<IResourceManager>();
        var commentTexture = new Mock<ITexture>().Object;

        resourceManager.Setup(x => x.LoadTexture(TexturePath.CommentBar)).Returns(commentTexture);

        display.SetResourceManager(resourceManager.Object);

        Assert.Same(commentTexture, GetField<ITexture?>(display, "_commentBarTexture"));
        resourceManager.Verify(x => x.LoadTexture(TexturePath.CommentBar), Times.Once);
    }

    [Fact]
    public void SetResourceManager_WhenCommentBarAlreadyLoaded_ShouldNotReload()
    {
        var display = new SongListDisplay();
        var existing = new Mock<ITexture>().Object;
        var resourceManager = new Mock<IResourceManager>();

        SetField(display, "_commentBarTexture", existing);

        display.SetResourceManager(resourceManager.Object);

        resourceManager.Verify(x => x.LoadTexture(It.IsAny<string>()), Times.Never);
        Assert.Same(existing, GetField<ITexture?>(display, "_commentBarTexture"));
    }

    [Fact]
    public void SetResourceManager_WhenCommentBarLoadThrows_ShouldKeepTextureNull()
    {
        var display = new SongListDisplay();
        var resourceManager = new Mock<IResourceManager>();
        resourceManager.Setup(x => x.LoadTexture(TexturePath.CommentBar)).Throws(new Exception("load-failed"));

        var ex = Record.Exception(() => display.SetResourceManager(resourceManager.Object));

        Assert.Null(ex);
        Assert.Null(GetField<ITexture?>(display, "_commentBarTexture"));
    }

    [Fact]
    public void SetEnhancedRendering_ShouldToggleInternalFlag()
    {
        var display = new SongListDisplay();

        display.SetEnhancedRendering(false);
        Assert.False(GetField<bool>(display, "_useEnhancedRendering"));

        display.SetEnhancedRendering(true);
        Assert.True(GetField<bool>(display, "_useEnhancedRendering"));
    }

    [Fact]
    public void InitializeEnhancedRendering_WhenRenderTargetIsNull_ShouldDisableBarRenderer()
    {
        var display = new SongListDisplay();

        var ex = Record.Exception(() => display.InitializeEnhancedRendering(null, null, null));

        Assert.Null(ex);
        Assert.Null(GetField<SongBarRenderer?>(display, "_barRenderer"));
    }

    [Fact]
    public void InvalidateVisuals_ShouldClearVisibleIndices_AndQueueVisibleRequests()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(3)
        };

        var visible = GetField<HashSet<int>>(display, "_visibleBarIndices");
        visible.Add(999);

        var queue = GetTextureGenerationQueue(display);
        queue.Clear();

        display.InvalidateVisuals();

        Assert.DoesNotContain(999, visible);
        Assert.Equal(3, visible.Count);
        Assert.True(queue.Count > 0);
    }

    [Fact]
    public void RefreshDisplay_ShouldClearCachesAndDisposeBarInfoTextures()
    {
        var display = new SongListDisplay();

        var titleCache = (IDictionary)GetField<object>(display, "_titleBarCache");
        titleCache[1] = null;

        var previewCache = (IDictionary)GetField<object>(display, "_previewImageCache");
        previewCache[1] = null;

        var songBarCache = (IDictionary)GetField<object>(display, "_songBarCache");
        songBarCache[1] = null;

        var title = new Mock<ITexture>();
        var preview = new Mock<ITexture>();
        var lamp = new Mock<ITexture>();
        var barInfoCache = (IDictionary)GetField<object>(display, "_barInfoCache");
        barInfoCache["k"] = new SongBarInfo
        {
            TitleTexture = title.Object,
            PreviewImage = preview.Object,
            ClearLamp = lamp.Object
        };

        display.RefreshDisplay();

        Assert.Empty(titleCache);
        Assert.Empty(previewCache);
        Assert.Empty(songBarCache);
        Assert.Empty(barInfoCache);

        title.Verify(x => x.RemoveReference(), Times.Once);
        preview.Verify(x => x.RemoveReference(), Times.Once);
        lamp.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void LoadSkinBarTextures_WhenAllTexturesLoad_ShouldAssignTexturesAndEnableFlag()
    {
        var display = new SongListDisplay();
        var rm = new Mock<IResourceManager>();

        var score = new Mock<ITexture>().Object;
        var scoreSel = new Mock<ITexture>().Object;
        var box = new Mock<ITexture>().Object;
        var boxSel = new Mock<ITexture>().Object;
        var other = new Mock<ITexture>().Object;
        var otherSel = new Mock<ITexture>().Object;

        rm.Setup(x => x.ResourceExists(TexturePath.BarScore)).Returns(true);
        rm.Setup(x => x.ResourceExists(TexturePath.BarScoreSelected)).Returns(true);
        rm.Setup(x => x.ResourceExists(TexturePath.BarBox)).Returns(true);
        rm.Setup(x => x.ResourceExists(TexturePath.BarBoxSelected)).Returns(true);
        rm.Setup(x => x.ResourceExists(TexturePath.BarOther)).Returns(true);
        rm.Setup(x => x.ResourceExists(TexturePath.BarOtherSelected)).Returns(true);

        rm.Setup(x => x.LoadTexture(TexturePath.BarScore)).Returns(score);
        rm.Setup(x => x.LoadTexture(TexturePath.BarScoreSelected)).Returns(scoreSel);
        rm.Setup(x => x.LoadTexture(TexturePath.BarBox)).Returns(box);
        rm.Setup(x => x.LoadTexture(TexturePath.BarBoxSelected)).Returns(boxSel);
        rm.Setup(x => x.LoadTexture(TexturePath.BarOther)).Returns(other);
        rm.Setup(x => x.LoadTexture(TexturePath.BarOtherSelected)).Returns(otherSel);

        SetField(display, "_resourceManager", rm.Object);

        InvokePrivate<object?>(display, "LoadSkinBarTextures");

        Assert.True(GetField<bool>(display, "_skinBarTexturesLoaded"));
        Assert.Same(score, GetField<ITexture?>(display, "_barScoreTexture"));
        Assert.Same(scoreSel, GetField<ITexture?>(display, "_barScoreSelectedTexture"));
        Assert.Same(box, GetField<ITexture?>(display, "_barBoxTexture"));
        Assert.Same(boxSel, GetField<ITexture?>(display, "_barBoxSelectedTexture"));
        Assert.Same(other, GetField<ITexture?>(display, "_barOtherTexture"));
        Assert.Same(otherSel, GetField<ITexture?>(display, "_barOtherSelectedTexture"));
    }

    [Fact]
    public void LoadSkinBarTextures_WhenResourceManagerMissing_ShouldNotEnableSkinFlag()
    {
        var display = new SongListDisplay();

        SetField(display, "_resourceManager", null);
        InvokePrivate<object?>(display, "LoadSkinBarTextures");

        Assert.False(GetField<bool>(display, "_skinBarTexturesLoaded"));
    }

    [Fact]
    public void GetSkinBarTexture_WhenUnknownBarType_ShouldFallbackToScoreTextures()
    {
        var display = new SongListDisplay();
        var score = new Mock<ITexture>().Object;
        var scoreSel = new Mock<ITexture>().Object;

        SetField(display, "_skinBarTexturesLoaded", true);
        SetField(display, "_barScoreTexture", score);
        SetField(display, "_barScoreSelectedTexture", scoreSel);

        var selected = InvokePrivate<ITexture?>(display, "GetSkinBarTexture", (BarType)(-1), true);
        var unselected = InvokePrivate<ITexture?>(display, "GetSkinBarTexture", (BarType)(-1), false);

        Assert.Same(scoreSel, selected);
        Assert.Same(score, unselected);
    }

    [Fact]
    public void GetOrCreateBarInfo_WhenNodeIsNullOrRendererMissing_ShouldReturnNull()
    {
        var display = new SongListDisplay();

        var nullNode = InvokePrivate<SongBarInfo?>(display, "GetOrCreateBarInfo", null, 0, false);
        var nonNullNode = InvokePrivate<SongBarInfo?>(display, "GetOrCreateBarInfo", new SongListNode { Type = NodeType.Score, Title = "A" }, 0, false);

        Assert.Null(nullNode);
        Assert.Null(nonNullNode);
    }

    [Fact]
    public void SetBarInfoCacheEntry_WhenReplacingExistingEntry_ShouldDisposePreviousBarInfo()
    {
        var display = new SongListDisplay();

        var oldTitle = new Mock<ITexture>();
        var oldPreview = new Mock<ITexture>();
        var oldLamp = new Mock<ITexture>();
        var oldBarInfo = new SongBarInfo
        {
            TitleTexture = oldTitle.Object,
            PreviewImage = oldPreview.Object,
            ClearLamp = oldLamp.Object
        };

        var newBarInfo = new SongBarInfo();

        InvokePrivate<object?>(display, "SetBarInfoCacheEntry", "cache-key", oldBarInfo);
        InvokePrivate<object?>(display, "SetBarInfoCacheEntry", "cache-key", newBarInfo);

        oldTitle.Verify(x => x.RemoveReference(), Times.Once);
        oldPreview.Verify(x => x.RemoveReference(), Times.Once);
        oldLamp.Verify(x => x.RemoveReference(), Times.Once);

        var barInfoCache = (IDictionary)GetField<object>(display, "_barInfoCache");
        Assert.Same(newBarInfo, barInfoCache["cache-key"]);
    }

    [Fact]
    public void LoadSkinBarTextures_WhenCoreTextureMissing_ShouldDisableSkinFlag()
    {
        var display = new SongListDisplay();
        var rm = new Mock<IResourceManager>();

        rm.Setup(x => x.LoadTexture(TexturePath.BarScore)).Throws(new Exception("missing-score"));
        rm.Setup(x => x.LoadTexture(TexturePath.BarScoreSelected)).Returns(new Mock<ITexture>().Object);
        rm.Setup(x => x.LoadTexture(TexturePath.BarBox)).Returns(new Mock<ITexture>().Object);
        rm.Setup(x => x.LoadTexture(TexturePath.BarBoxSelected)).Returns(new Mock<ITexture>().Object);
        rm.Setup(x => x.LoadTexture(TexturePath.BarOther)).Returns(new Mock<ITexture>().Object);
        rm.Setup(x => x.LoadTexture(TexturePath.BarOtherSelected)).Returns(new Mock<ITexture>().Object);

        SetField(display, "_resourceManager", rm.Object);
        InvokePrivate<object?>(display, "LoadSkinBarTextures");

        Assert.False(GetField<bool>(display, "_skinBarTexturesLoaded"));
        Assert.Null(GetField<ITexture?>(display, "_barScoreTexture"));
    }

    [Fact]
    public void CalculateBarTextureBounds_WhenSelected_ShouldApplyYOffset()
    {
        var display = new SongListDisplay();
        var itemBounds = new Rectangle(665, 269, 510, 48);
        const int selectedTextureWidth = 640;
        const int selectedTextureHeight = 96;

        var textureBounds = InvokePrivate<Rectangle>(display, "CalculateBarTextureBounds", itemBounds, true, selectedTextureWidth, selectedTextureHeight);

        // NX selected skin renders at natural 640x96, shifted upward by 30px.
        Assert.Equal(itemBounds.Y + SongSelectionUILayout.SongBars.SelectedBarTextureYOffset, textureBounds.Y);
        Assert.Equal(itemBounds.X, textureBounds.X);
        Assert.Equal(selectedTextureWidth, textureBounds.Width);
        Assert.Equal(selectedTextureHeight, textureBounds.Height);
    }

    [Fact]
    public void CalculateBarTextureBounds_WhenNotSelected_ShouldUseOriginalY()
    {
        var display = new SongListDisplay();
        var itemBounds = new Rectangle(673, 100, 510, 48);

        var textureBounds = InvokePrivate<Rectangle>(display, "CalculateBarTextureBounds", itemBounds, false, -1, -1);

        Assert.Equal(itemBounds.Y, textureBounds.Y);
        Assert.Equal(itemBounds.X, textureBounds.X);
        Assert.Equal(itemBounds.Height, textureBounds.Height);
    }

    [Fact]
    public void CalculateArtistNamePosition_ShouldUseAbsoluteNXCoordinates()
    {
        var display = new SongListDisplay();
        float textWidth = 150f;

        var pos = InvokePrivate<Vector2>(display, "CalculateArtistNamePosition", textWidth);

        // NX: x = 1260 - 25 - textWidth = 1235 - textWidth
        Assert.Equal(SongSelectionUILayout.SongBars.ArtistNameAbsoluteRightEdge - textWidth, pos.X);
        Assert.Equal(SongSelectionUILayout.SongBars.ArtistNameAbsoluteY, (int)pos.Y);
    }

    [Fact]
    public void CalculateArtistNamePosition_WhenTextWouldOverflow_ShouldClampToZero()
    {
        var display = new SongListDisplay();
        float textWidth = 1400f;

        var pos = InvokePrivate<Vector2>(display, "CalculateArtistNamePosition", textWidth);

        Assert.Equal(0f, pos.X);
    }

    [Fact]
    public void MoveNext_ShouldAdvanceSelectionAndRaiseIncompleteSelectionEvent()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(3)
        };
        SongSelectionChangedEventArgs? args = null;

        display.SelectionChanged += (_, e) => args = e;
        SetField(display, "_selectedIndex", 0);
        SetField(display, "_targetScrollCounter", 0);
        SetField(display, "_currentScrollCounter", 0);

        display.MoveNext();

        Assert.Equal(1, GetField<int>(display, "_selectedIndex"));
        Assert.Equal(100, GetField<int>(display, "_targetScrollCounter"));
        Assert.Same(display.CurrentList[1], display.SelectedSong);
        Assert.NotNull(args);
        Assert.Same(display.CurrentList[1], args!.SelectedSong);
        Assert.False(args.IsScrollComplete);
    }

    [Fact]
    public void MovePrevious_WhenAtFirstSong_ShouldSelectLastSongAndRaiseSelectionEvent()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(3)
        };
        SongSelectionChangedEventArgs? args = null;

        display.SelectionChanged += (_, e) => args = e;
        SetField(display, "_selectedIndex", 0);
        SetField(display, "_targetScrollCounter", 0);
        SetField(display, "_currentScrollCounter", 0);

        display.MovePrevious();

        Assert.Equal(-1, GetField<int>(display, "_selectedIndex"));
        Assert.Equal(-100, GetField<int>(display, "_targetScrollCounter"));
        Assert.Same(display.CurrentList[2], display.SelectedSong);
        Assert.NotNull(args);
        Assert.Same(display.CurrentList[2], args!.SelectedSong);
    }

    [Fact]
    public void SelectedIndex_WhenValueUnchanged_ShouldNotRaiseSelectionChangedOrUpdateTarget()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(3)
        };
        var fired = false;

        display.SelectionChanged += (_, _) => fired = true;
        SetField(display, "_selectedIndex", 1);
        SetField(display, "_targetScrollCounter", 150);

        display.SelectedIndex = 1;

        Assert.False(fired);
        Assert.Equal(1, GetField<int>(display, "_selectedIndex"));
        Assert.Equal(150, GetField<int>(display, "_targetScrollCounter"));
    }

    [Fact]
    public void CycleDifficulty_WhenOnlyCurrentDifficultyExists_ShouldKeepDifficultyAndRaiseEvent()
    {
        var display = new SongListDisplay
        {
            CurrentList = new List<SongListNode>
            {
                new SongListNode
                {
                    Type = NodeType.Score,
                    Title = "Song",
                    Scores = new SongScore[5]
                }
            }
        };
        DifficultyChangedEventArgs? args = null;

        display.CurrentList[0].Scores![2] = new SongScore { DifficultyLevel = 2, DifficultyLabel = "Hard" };
        display.DifficultyChanged += (_, e) => args = e;
        display.CurrentDifficulty = 2;

        display.CycleDifficulty();

        Assert.Equal(2, display.CurrentDifficulty);
        Assert.NotNull(args);
        Assert.Same(display.SelectedSong, args!.Song);
        Assert.Equal(2, args.NewDifficulty);
    }

    [Fact]
    public void ActivateSelected_ShouldRaiseSongActivatedWithCurrentDifficulty()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(2)
        };
        SongActivatedEventArgs? args = null;

        display.SongActivated += (_, e) => args = e;
        display.CurrentDifficulty = 4;
        display.ActivateSelected();

        Assert.NotNull(args);
        Assert.Same(display.SelectedSong, args!.Song);
        Assert.Equal(4, args.Difficulty);
    }

    private static List<SongListNode> CreateSongs(int count)
    {
        var songs = new List<SongListNode>();
        for (int i = 0; i < count; i++)
        {
            songs.Add(new SongListNode
            {
                Type = NodeType.Score,
                Title = $"Song {i}",
                Scores = new SongScore[5]
            });
        }

        return songs;
    }

    private static List<TextureGenerationRequest> GetTextureGenerationQueue(SongListDisplay display)
    {
        return GetField<List<TextureGenerationRequest>>(display, "_textureGenerationQueue");
    }

    private static T InvokePrivate<T>(object target, string methodName, params object[] args)
    {
        MethodInfo? method = null;
        foreach (var candidate in target.GetType().GetMethods(BindingFlags.Instance | BindingFlags.NonPublic))
        {
            if (candidate.Name != methodName)
                continue;

            var parameters = candidate.GetParameters();
            if (parameters.Length != args.Length)
                continue;

            bool isMatch = true;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (args[i] == null)
                    continue;

                if (!parameters[i].ParameterType.IsInstanceOfType(args[i]))
                {
                    isMatch = false;
                    break;
                }
            }

            if (isMatch)
            {
                method = candidate;
                break;
            }
        }

        Assert.NotNull(method);
        var result = method!.Invoke(target, args);
        // For reference types, handle null results safely
        // For value types, the cast will succeed or throw with a clearer error
        if (result == null)
        {
            // If T is a reference type, return default (null)
            // If T is a value type, this will throw but that's expected behavior
            return default!;
        }
        return (T)result;
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

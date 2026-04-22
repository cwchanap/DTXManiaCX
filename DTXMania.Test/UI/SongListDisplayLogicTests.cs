using System;
using System.Collections;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;
using SongEntity = DTXMania.Game.Lib.Song.Entities.Song;

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
    public void GetDisplayText_WhenScoreTitleMissing_ShouldFallbackToUnknownSong()
    {
        var display = new SongListDisplay();

        var text = InvokePrivate<string>(display, "GetDisplayText", new SongListNode { Type = NodeType.Score, Title = null });

        Assert.Equal("Unknown", text);
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
    public void TruncateTextToWidth_WhenSpriteFontTextIsTooLong_ShouldReturnFittingEllipsis()
    {
        var display = new SongListDisplay();
        var font = CreateSpriteFont(
            [(' ', 4), ('.', 4), ('A', 10), ('B', 10), ('C', 10), ('D', 10), ('E', 10), ('F', 10), ('G', 10), ('H', 10)]);

        var truncated = InvokePrivate<string>(display, "TruncateTextToWidth", "ABCDEFGH", 44f, font);

        Assert.EndsWith("...", truncated);
        Assert.True(font.MeasureString(truncated).X <= 44f);
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
    public void WrapTextToWidth_WhenSpriteFontTextExceedsWidth_ShouldWrapIntoMultipleLines()
    {
        var display = new SongListDisplay();
        var font = CreateSpriteFont([(' ', 4), ('A', 10), ('B', 10), ('C', 10)]);

        var lines = InvokePrivate<string[]>(display, "WrapTextToWidth", "AA BB CC", 30f, font, 1f);

        Assert.Equal(["AA", "BB", "CC"], lines);
        Assert.All(lines, line => Assert.True(font.MeasureString(line).X <= 30f));
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
    public void UpdateScrollAnimation_WhenCrossingTwoBars_ShouldQueueTextureGenerationForVisibleBars()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(12)
        };
        var queue = GetTextureGenerationQueue(display);
        queue.Clear();

        SetField(display, "_selectedIndex", 5);
        SetField(display, "_currentDifficulty", 1);
        SetField(display, "_currentScrollCounter", 0);
        SetField(display, "_targetScrollCounter", 300);

        InvokePrivate<object?>(display, "UpdateScrollAnimation", 1.0);

        Assert.Equal(300, GetField<int>(display, "_currentScrollCounter"));
        Assert.NotEmpty(queue);
    }

    [Fact]
    public void CycleDifficulty_WhenCurrentSlotIsMissing_ShouldWrapToNextAvailableScore()
    {
        var display = new SongListDisplay
        {
            CurrentList =
            [
                new SongListNode
                {
                    Type = NodeType.Score,
                    Title = "Song",
                    Scores =
                    [
                        null,
                        new SongScore { DifficultyLevel = 1 },
                        null,
                        null,
                        new SongScore { DifficultyLevel = 4 }
                    ]
                }
            ]
        };

        DifficultyChangedEventArgs? args = null;
        display.DifficultyChanged += (_, e) => args = e;
        display.CurrentDifficulty = 0;

        display.CycleDifficulty();
        Assert.Equal(1, display.CurrentDifficulty);
        Assert.NotNull(args);
        Assert.Equal(1, args!.NewDifficulty);

        display.CycleDifficulty();
        Assert.Equal(4, display.CurrentDifficulty);
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
    public void QueueTextureGenerationForNewBars_WhenCurrentListIsNull_ShouldDoNothing()
    {
        var display = new SongListDisplay();
        SetField(display, "_currentList", null);

        InvokePrivate<object?>(display, "QueueTextureGenerationForNewBars");

        Assert.Empty(GetTextureGenerationQueue(display));
    }

    [Fact]
    public void QueueTextureGenerationForNewBars_WhenCurrentListIsEmpty_ShouldDoNothing()
    {
        var display = new SongListDisplay();
        SetField(display, "_currentList", new List<SongListNode>());

        InvokePrivate<object?>(display, "QueueTextureGenerationForNewBars");

        Assert.Empty(GetTextureGenerationQueue(display));
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
    public void QueueTextureGenerationForNewBars_WhenOnlyOneSongExists_ShouldQueueSingleSelectedRequest()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(1)
        };

        SetField(display, "_currentScrollCounter", 0);
        SetField(display, "_currentDifficulty", 2);

        var queue = GetTextureGenerationQueue(display);
        queue.Clear();

        var barInfoCache = (IDictionary)GetField<object>(display, "_barInfoCache");
        barInfoCache.Clear();

        InvokePrivate<object?>(display, "QueueTextureGenerationForNewBars");

        var request = Assert.Single(queue);
        Assert.Equal(display.CurrentList[0], request.SongNode);
        Assert.Equal(0, request.SongIndex);
        Assert.Equal(2, request.Difficulty);
        Assert.True(request.IsSelected);
        Assert.Equal(100, request.Priority);

        var visible = GetField<HashSet<int>>(display, "_visibleBarIndices");
        Assert.Equal(new[] { 0 }, visible.OrderBy(index => index));
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

        // Deduplication: each unique song is enqueued at most once, so queue size equals unique song count
        Assert.Equal(display.CurrentList.Count, queue.Count);

        var queuedSongIndices = queue
            .Select(item => item.SongIndex)
            .OrderBy(index => index)
            .ToArray();
        Assert.Equal(new[] { 0, 1, 2 }, queuedSongIndices);

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
    public void GetOrCreateBarInfo_WhenCachedEntryExists_ShouldReuseAndUpdateCachedInfo()
    {
        var display = new SongListDisplay();
        var node = new SongListNode { Type = NodeType.Box, Title = "Folder" };
        var fakeRenderer = (SongBarRenderer)RuntimeHelpers.GetUninitializedObject(typeof(SongBarRenderer));
        var cachedInfo = new SongBarInfo
        {
            SongNode = node,
            DifficultyLevel = 0,
            IsSelected = false,
            TextColor = Color.White
        };

        SetField(display, "_barRenderer", fakeRenderer);
        GetField<Dictionary<string, SongBarInfo>>(display, "_barInfoCache")[$"{node.GetHashCode()}_2"] = cachedInfo;

        var result = InvokePrivate<SongBarInfo?>(display, "GetOrCreateBarInfo", node, 2, true);

        Assert.Same(cachedInfo, result);
        Assert.Equal(2, cachedInfo.DifficultyLevel);
        Assert.True(cachedInfo.IsSelected);
        Assert.Equal(Color.Yellow, cachedInfo.TextColor);
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

    [Theory]
    [InlineData(false, 8, 2)]
    [InlineData(true, 12, 2)]
    public void UpdatePendingTextures_ShouldRespectPerFrameProcessingLimit(bool isScrolling, int queuedCount, int expectedRemaining)
    {
        var display = new SongListDisplay();
        var fakeRenderer = (SongBarRenderer)RuntimeHelpers.GetUninitializedObject(typeof(SongBarRenderer));
        var queue = GetTextureGenerationQueue(display);

        SetField(display, "_barRenderer", fakeRenderer);
        SetField(display, "_currentScrollCounter", isScrolling ? 0 : 0);
        SetField(display, "_targetScrollCounter", isScrolling ? 100 : 0);
        queue.Clear();

        for (int i = 0; i < queuedCount; i++)
        {
            queue.Add(new TextureGenerationRequest
            {
                SongNode = null!,
                SongIndex = i,
                Difficulty = 0,
                IsSelected = false,
                Priority = queuedCount - i
            });
        }

        InvokePrivate<object?>(display, "UpdatePendingTextures");

        Assert.Equal(expectedRemaining, queue.Count);
        Assert.Empty(GetField<Dictionary<string, SongBarInfo>>(display, "_barInfoCache"));
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
        var fakeRenderer = (SongBarRenderer)RuntimeHelpers.GetUninitializedObject(typeof(SongBarRenderer));
        SetField(display, "_barRenderer", fakeRenderer);

        var ex = Record.Exception(() => display.Font = null);

        Assert.IsType<NullReferenceException>(ex);
        Assert.Null(display.Font);
    }

    [Fact]
    public void ManagedFontSetter_WhenBarRendererIsPresentButUninitialized_ShouldThrow()
    {
        var display = new SongListDisplay();
        var fakeRenderer = (SongBarRenderer)RuntimeHelpers.GetUninitializedObject(typeof(SongBarRenderer));
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
    public void Update_WhenActive_ShouldAnimateScrollAndTolerateMissingRenderer()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(3)
        };

        SetField(display, "_currentScrollCounter", 0);
        SetField(display, "_targetScrollCounter", 200);
        display.Activate();

        display.Update(1.0 / 60.0);

        Assert.InRange(GetField<int>(display, "_currentScrollCounter"), 1, 200);
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
    public void SetBarInfoCacheEntry_WhenReplacingWithSameReference_ShouldKeepExistingBarInfo()
    {
        var display = new SongListDisplay();
        var title = new Mock<ITexture>();
        var preview = new Mock<ITexture>();
        var lamp = new Mock<ITexture>();
        var barInfo = new SongBarInfo
        {
            TitleTexture = title.Object,
            PreviewImage = preview.Object,
            ClearLamp = lamp.Object
        };

        InvokePrivate<object?>(display, "SetBarInfoCacheEntry", "cache-key", barInfo);
        InvokePrivate<object?>(display, "SetBarInfoCacheEntry", "cache-key", barInfo);

        title.Verify(x => x.RemoveReference(), Times.Never);
        preview.Verify(x => x.RemoveReference(), Times.Never);
        lamp.Verify(x => x.RemoveReference(), Times.Never);

        var barInfoCache = (IDictionary)GetField<object>(display, "_barInfoCache");
        Assert.Same(barInfo, barInfoCache["cache-key"]);
    }

    [Fact]
    public void LoadSkinBarTextures_WhenCoreTextureResourceMissing_ShouldDisableSkinFlagAndSkipLoad()
    {
        var display = new SongListDisplay();
        var rm = new Mock<IResourceManager>();

        rm.Setup(x => x.ResourceExists(TexturePath.BarScore)).Returns(false);
        rm.Setup(x => x.ResourceExists(TexturePath.BarScoreSelected)).Returns(true);
        rm.Setup(x => x.ResourceExists(TexturePath.BarBox)).Returns(true);
        rm.Setup(x => x.ResourceExists(TexturePath.BarBoxSelected)).Returns(true);
        rm.Setup(x => x.ResourceExists(TexturePath.BarOther)).Returns(true);
        rm.Setup(x => x.ResourceExists(TexturePath.BarOtherSelected)).Returns(true);

        rm.Setup(x => x.LoadTexture(TexturePath.BarScoreSelected)).Returns(new Mock<ITexture>().Object);
        rm.Setup(x => x.LoadTexture(TexturePath.BarBox)).Returns(new Mock<ITexture>().Object);
        rm.Setup(x => x.LoadTexture(TexturePath.BarBoxSelected)).Returns(new Mock<ITexture>().Object);
        rm.Setup(x => x.LoadTexture(TexturePath.BarOther)).Returns(new Mock<ITexture>().Object);
        rm.Setup(x => x.LoadTexture(TexturePath.BarOtherSelected)).Returns(new Mock<ITexture>().Object);

        SetField(display, "_resourceManager", rm.Object);
        InvokePrivate<object?>(display, "LoadSkinBarTextures");

        rm.Verify(x => x.LoadTexture(TexturePath.BarScore), Times.Never);
        Assert.False(GetField<bool>(display, "_skinBarTexturesLoaded"));
        Assert.Null(GetField<ITexture?>(display, "_barScoreTexture"));
        Assert.NotNull(GetField<ITexture?>(display, "_barScoreSelectedTexture"));
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
    public void TruncateTextToWidth_WhenTextIsNull_ShouldReturnNull()
    {
        var display = new SongListDisplay();
        var font = new Mock<IFont>();

        var result = InvokePrivate<string>(display, "TruncateTextToWidth", null, 100f, font.Object);

        Assert.Null(result);
    }

    [Fact]
    public void TruncateTextToWidth_WhenTextIsEmpty_ShouldReturnEmpty()
    {
        var display = new SongListDisplay();
        var font = new Mock<IFont>();

        var result = InvokePrivate<string>(display, "TruncateTextToWidth", "", 100f, font.Object);

        Assert.Equal("", result);
    }

    [Fact]
    public void TruncateTextToWidth_WhenFontIsNull_ShouldReturnOriginalText()
    {
        var display = new SongListDisplay();

        var result = InvokePrivate<string>(display, "TruncateTextToWidth", "test", 100f, (IFont)null);

        Assert.Equal("test", result);
    }

    [Fact]
    public void TruncateTextToWidth_WhenTextFitsExactly_ShouldReturnOriginal()
    {
        var display = new SongListDisplay();
        var font = new Mock<IFont>();
        font.Setup(x => x.MeasureString("exact")).Returns(new Vector2(100f, 10f));

        var result = InvokePrivate<string>(display, "TruncateTextToWidth", "exact", 100f, font.Object);

        Assert.Equal("exact", result);
    }

    [Fact]
    public void TruncateTextToWidth_WhenVeryLongText_ShouldTruncateWithEllipsis()
    {
        var display = new SongListDisplay();
        var font = new Mock<IFont>();
        font.Setup(x => x.MeasureString(It.IsAny<string>())).Returns((string s) => new Vector2(s.Length * 10, 10));

        var longText = new string('A', 100);
        var result = InvokePrivate<string>(display, "TruncateTextToWidth", longText, 50f, font.Object);

        Assert.EndsWith("...", result);
        Assert.True(result.Length < longText.Length);
        Assert.True(font.Object.MeasureString(result).X <= 50f);
    }

    [Fact]
    public void WrapTextToWidth_WhenTextIsNull_ShouldReturnSingleEmptyLine()
    {
        var display = new SongListDisplay();

        var lines = InvokePrivate<string[]>(display, "WrapTextToWidth", null, 100f, (SpriteFont)null, 1f);

        Assert.Single(lines);
        Assert.Equal("", lines[0]);
    }

    [Fact]
    public void WrapTextToWidth_WhenTextIsEmpty_ShouldReturnSingleEmptyLine()
    {
        var display = new SongListDisplay();

        var lines = InvokePrivate<string[]>(display, "WrapTextToWidth", "", 100f, (SpriteFont)null, 1f);

        Assert.Single(lines);
        Assert.Equal("", lines[0]);
    }

    [Fact]
    public void WrapTextWithMeasurement_WhenTextFitsOnOneLine_ShouldReturnSingleLine()
    {
        var display = new SongListDisplay();
        System.Func<string, float> measureText = text => text.Length * 10f;

        var lines = InvokePrivate<string[]>(display, "WrapTextWithMeasurement", "hello world", 200f, measureText);

        Assert.Single(lines);
        Assert.Equal("hello world", lines[0]);
    }

    [Fact]
    public void WrapTextWithMeasurement_WhenTextExceedsWidth_ShouldWrapAtWordBoundaries()
    {
        var display = new SongListDisplay();
        System.Func<string, float> measureText = text => text.Length * 10f;

        var lines = InvokePrivate<string[]>(display, "WrapTextWithMeasurement", "hello world foo", 100f, measureText);

        Assert.Equal(2, lines.Length);
        Assert.Equal("hello", lines[0]);
        Assert.Equal("world foo", lines[1]);
    }

    [Fact]
    public void WrapTextWithMeasurement_WhenMultipleWordsExceedWidth_ShouldWrapMultipleTimes()
    {
        var display = new SongListDisplay();
        System.Func<string, float> measureText = text => text.Length * 10f;

        var lines = InvokePrivate<string[]>(display, "WrapTextWithMeasurement", "one two three four five", 80f, measureText);

        Assert.Equal(4, lines.Length);
        Assert.Equal("one two", lines[0]);
        Assert.Equal("three", lines[1]);
        Assert.Equal("four", lines[2]);
        Assert.Equal("five", lines[3]);
    }

    [Fact]
    public void WrapTextWithMeasurement_WhenSingleWordExceedsWidth_ShouldAddWordAnyway()
    {
        var display = new SongListDisplay();
        System.Func<string, float> measureText = text => text.Length * 10f;

        var lines = InvokePrivate<string[]>(display, "WrapTextWithMeasurement", "superlongword", 50f, measureText);

        Assert.Single(lines);
        Assert.Equal("superlongword", lines[0]);
    }

    [Fact]
    public void WrapTextWithMeasurement_WhenSingleWordExceedsWidthInMiddle_ShouldPlaceOnNewLine()
    {
        var display = new SongListDisplay();
        System.Func<string, float> measureText = text => text.Length * 10f;

        var lines = InvokePrivate<string[]>(display, "WrapTextWithMeasurement", "short superlongword end", 100f, measureText);

        Assert.Equal(3, lines.Length);
        Assert.Equal("short", lines[0]);
        Assert.Equal("superlongword", lines[1]);
        Assert.Equal("end", lines[2]);
    }

    [Fact]
    public void WrapTextWithMeasurement_WhenExactlyFitsWidth_ShouldNotWrap()
    {
        var display = new SongListDisplay();
        System.Func<string, float> measureText = text => text.Length * 10f;

        var lines = InvokePrivate<string[]>(display, "WrapTextWithMeasurement", "hello", 50f, measureText);

        Assert.Single(lines);
        Assert.Equal("hello", lines[0]);
    }

    [Fact]
    public void CalculateBarTextureBounds_WhenTextureSizeProvided_ShouldUseProvidedSize()
    {
        var display = new SongListDisplay();
        var itemBounds = new Rectangle(100, 200, 500, 50);

        var result = InvokePrivate<Rectangle>(display, "CalculateBarTextureBounds", itemBounds, false, 640, 96);

        Assert.Equal(640, result.Width);
        Assert.Equal(96, result.Height);
        Assert.Equal(itemBounds.X, result.X);
        Assert.Equal(itemBounds.Y, result.Y);
    }

    [Fact]
    public void CalculateBarTextureBounds_WhenTextureSizeNegative_ShouldUseItemBoundsSize()
    {
        var display = new SongListDisplay();
        var itemBounds = new Rectangle(100, 200, 500, 50);

        var result = InvokePrivate<Rectangle>(display, "CalculateBarTextureBounds", itemBounds, false, -1, -1);

        Assert.Equal(itemBounds.Width, result.Width);
        Assert.Equal(itemBounds.Height, result.Height);
    }

    [Fact]
    public void CalculateBarTextureBounds_WhenWidthOnlyProvided_ShouldUseItemBoundsHeight()
    {
        var display = new SongListDisplay();
        var itemBounds = new Rectangle(100, 200, 500, 50);

        var result = InvokePrivate<Rectangle>(display, "CalculateBarTextureBounds", itemBounds, false, 640, -1);

        Assert.Equal(640, result.Width);
        Assert.Equal(itemBounds.Height, result.Height);
    }

    [Fact]
    public void CalculateBarTextureBounds_WhenHeightOnlyProvided_ShouldUseItemBoundsWidth()
    {
        var display = new SongListDisplay();
        var itemBounds = new Rectangle(100, 200, 500, 50);

        var result = InvokePrivate<Rectangle>(display, "CalculateBarTextureBounds", itemBounds, false, -1, 96);

        Assert.Equal(itemBounds.Width, result.Width);
        Assert.Equal(96, result.Height);
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
    public void CycleDifficulty_WhenSelectedSongHasNoScores_ShouldLeaveDifficultyUnchanged()
    {
        var display = new SongListDisplay
        {
            CurrentList =
            [
                new SongListNode
                {
                    Type = NodeType.Score,
                    Title = "Song",
                    Scores = null
                }
            ]
        };
        var fired = false;

        display.DifficultyChanged += (_, _) => fired = true;
        display.CurrentDifficulty = 3;

        display.CycleDifficulty();

        Assert.Equal(3, display.CurrentDifficulty);
        Assert.False(fired);
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

    [Fact]
    public void ActivateSelected_WhenSelectedSongIsNull_ShouldNotRaiseSongActivated()
    {
        var display = new SongListDisplay();
        var fired = false;

        display.SongActivated += (_, _) => fired = true;
        display.ActivateSelected();

        Assert.False(fired);
    }

    [Fact]
    public void Draw_WhenUsingManagedFontAndSkinTextures_ShouldRenderBasicSongList()
    {
        var managedFont = CreateManagedFont();
        var selectedBarTexture = CreateTexture(width: 640, height: 96);
        var scoreBarTexture = CreateTexture(width: 620, height: 48);
        var commentBarTexture = CreateTexture(width: 620, height: 60);
        var scrollbarTexture = CreateTexture(width: 12, height: 373);
        var display = new SongListDisplay
        {
            ManagedFont = managedFont.Object,
            WhitePixel = null,
            CurrentList = CreateSongsForDraw()
        };

        SetField(display, "_useEnhancedRendering", false);
        SetField(display, "_skinBarTexturesLoaded", true);
        SetField(display, "_barScoreSelectedTexture", selectedBarTexture.Object);
        SetField(display, "_barScoreTexture", scoreBarTexture.Object);
        SetField(display, "_commentBarTexture", commentBarTexture.Object);
        SetField(display, "_scrollbarTexture", scrollbarTexture.Object);
        display.Activate();

        display.Draw(null!, 0);

        selectedBarTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()), Times.AtLeastOnce);
        scoreBarTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()), Times.AtLeastOnce);
        commentBarTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
        scrollbarTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
        managedFont.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()), Times.AtLeast(14));
    }

    [Fact]
    public void Draw_WhenInvisible_ShouldReturnWithoutThrowing()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(2),
            Visible = false
        };

        var exception = Record.Exception(() => display.Draw(null!, 0));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_WhenCurrentListIsNull_ShouldReturnWithoutThrowing()
    {
        var display = new SongListDisplay();

        var exception = Record.Exception(() => display.Draw(null!, 0));

        Assert.Null(exception);
    }

    [Fact]
    public void Draw_WhenCurrentListEmptyAndManagedFontPresent_ShouldRenderEmptyMessage()
    {
        var managedFont = CreateManagedFont();
        var display = new SongListDisplay
        {
            ManagedFont = managedFont.Object,
            WhitePixel = null,
            CurrentList = new List<SongListNode>()
        };
        display.Activate();

        display.Draw(null!, 0);

        managedFont.Verify(x => x.MeasureString("No songs found"), Times.Once);
        managedFont.Verify(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), "No songs found", It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Once);
    }

    [Fact]
    public void DrawBarInfoWithPerspective_WhenTexturesExist_ShouldDrawBarPartsAndArtist()
    {
        var managedFont = CreateManagedFont();
        var selectedBarTexture = CreateTexture(width: 640, height: 96);
        var titleTexture = CreateTexture(width: 240, height: 32);
        var previewTexture = CreateTexture(width: 64, height: 64);
        var clearLampTexture = CreateTexture(width: 7, height: 41);
        var display = new SongListDisplay
        {
            ManagedFont = managedFont.Object
        };
        var barInfo = new SongBarInfo
        {
            SongNode = CreateSongsForDraw()[0],
            BarType = BarType.Score,
            TitleTexture = titleTexture.Object,
            PreviewImage = previewTexture.Object,
            ClearLamp = clearLampTexture.Object,
            TitleString = "Song 0"
        };

        SetField(display, "_skinBarTexturesLoaded", true);
        SetField(display, "_barScoreSelectedTexture", selectedBarTexture.Object);

        InvokePrivate<object?>(display, "DrawBarInfoWithPerspective", null!, barInfo, new Rectangle(665, 269, 510, 48), true, true, 1f, 1f);

        selectedBarTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()), Times.Once);
        clearLampTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()), Times.Once);
        previewTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()), Times.Once);
        titleTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<float>(), It.IsAny<Vector2>()), Times.Once);
    }

    [Fact]
    public void DrawCommentBar_WhenSelectedSongIsScore_ShouldDrawBackgroundTexture()
    {
        var display = new SongListDisplay();
        var commentBarTexture = CreateTexture(width: 620, height: 60);

        display.CurrentList = CreateSongsForDraw();
        SetField(display, "_commentBarTexture", commentBarTexture.Object);

        InvokePrivate<object?>(display, "DrawCommentBar", (SpriteBatch)null!);

        commentBarTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
    }

    [Fact]
    public void DrawCommentBar_WhenSelectedSongMissingOrNonScore_ShouldReturnWithoutThrowing()
    {
        var display = new SongListDisplay();

        var noSongException = Record.Exception(() => InvokePrivate<object?>(display, "DrawCommentBar", (SpriteBatch)null!));
        Assert.Null(noSongException);

        display.CurrentList = [new SongListNode { Type = NodeType.Box, Title = "Folder" }];

        var nonScoreException = Record.Exception(() => InvokePrivate<object?>(display, "DrawCommentBar", (SpriteBatch)null!));
        Assert.Null(nonScoreException);
    }

    [Fact]
    public void DrawCommentBar_WhenTextureMissingAndResourceManagerAvailable_ShouldLazyLoadAndDraw()
    {
        var display = new SongListDisplay
        {
            CurrentList =
            [
                new SongListNode
                {
                    Type = NodeType.Score,
                    Title = "Song 0",
                    DatabaseSong = new SongEntity
                    {
                        Title = "Song 0",
                        Artist = "Artist 0",
                        Comment = "A comment"
                    },
                    Scores = new SongScore[5]
                }
            ]
        };
        var resourceManager = new Mock<IResourceManager>();
        var commentBarTexture = CreateTexture(width: 620, height: 60);

        resourceManager.Setup(x => x.LoadTexture(TexturePath.CommentBar)).Returns(commentBarTexture.Object);
        SetField(display, "_resourceManager", resourceManager.Object);
        SetField(display, "_commentBarTexture", null);
        SetField(display, "_font", null);

        InvokePrivate<object?>(display, "DrawCommentBar", (SpriteBatch)null!);

        resourceManager.Verify(x => x.LoadTexture(TexturePath.CommentBar), Times.Once);
        commentBarTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
        Assert.Same(commentBarTexture.Object, GetField<ITexture>(display, "_commentBarTexture"));
    }

    [Fact]
    public void DrawCommentBar_WhenTextureAndResourceManagerAreMissing_ShouldSkipFallbackSafely()
    {
        var display = new SongListDisplay
        {
            CurrentList =
            [
                new SongListNode
                {
                    Type = NodeType.Score,
                    Title = "Song 0",
                    DatabaseSong = new SongEntity
                    {
                        Title = "Song 0",
                        Artist = "Artist 0",
                        Comment = null
                    },
                    Scores = new SongScore[5]
                }
            ]
        };
        SetField(display, "_commentBarTexture", null);
        SetField(display, "_resourceManager", null);
        SetField(display, "_whitePixel", null);

        var exception = Record.Exception(() => InvokePrivate<object?>(display, "DrawCommentBar", (SpriteBatch)null!));

        Assert.Null(exception);
        Assert.Null(GetField<ITexture?>(display, "_commentBarTexture"));
    }

    [Fact]
    public void DrawScrollbar_WhenTextureAvailable_ShouldDrawTrack()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongsForDraw()
        };
        var scrollbarTexture = CreateTexture(width: 12, height: 373);

        SetField(display, "_scrollbarTexture", scrollbarTexture.Object);
        SetField(display, "_selectedIndex", 1);
        SetField(display, "_whitePixel", null);

        InvokePrivate<object?>(display, "DrawScrollbar", (SpriteBatch)null!);

        scrollbarTexture.Verify(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
    }

    [Fact]
    public void DrawScrollbar_WhenListHasSingleItem_ShouldReturnWithoutThrowing()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(1)
        };

        var exception = Record.Exception(() => InvokePrivate<object?>(display, "DrawScrollbar", (SpriteBatch)null!));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawItemCounter_WhenListEmptyOrFontMissing_ShouldReturnWithoutThrowing()
    {
        var emptyDisplay = new SongListDisplay
        {
            CurrentList = new List<SongListNode>()
        };

        var emptyException = Record.Exception(() => InvokePrivate<object?>(emptyDisplay, "DrawItemCounter", (SpriteBatch)null!));
        Assert.Null(emptyException);

        var noFontDisplay = new SongListDisplay
        {
            CurrentList = CreateSongs(2),
            Font = null
        };

        var noFontException = Record.Exception(() => InvokePrivate<object?>(noFontDisplay, "DrawItemCounter", (SpriteBatch)null!));
        Assert.Null(noFontException);
    }

    [Fact]
    public void DrawSongItems_WhenListEmptyAndNoFonts_ShouldReturnWithoutThrowing()
    {
        var display = new SongListDisplay
        {
            CurrentList = new List<SongListNode>(),
            Font = null,
            ManagedFont = null
        };

        var exception = Record.Exception(() => InvokePrivate<object?>(display, "DrawSongItems", (SpriteBatch)null!, new Rectangle(0, 0, 640, 480)));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawSongItemWithPerspective_WhenNoFontsTexturesOrFallbackPixel_ShouldReturnWithoutThrowing()
    {
        var display = new SongListDisplay
        {
            Font = null,
            ManagedFont = null,
            WhitePixel = null
        };
        var node = new SongListNode { Type = NodeType.Box, Title = "Folder" };

        var exception = Record.Exception(() => InvokePrivate<object?>(
            display,
            "DrawSongItemWithPerspective",
            (SpriteBatch)null!,
            node,
            new Rectangle(100, 200, 510, 48),
            false,
            false,
            0,
            1f,
            1f));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawSongItemWithPerspective_WhenCachedEnhancedBarExists_ShouldUseEnhancedPath()
    {
        var display = new SongListDisplay
        {
            Font = CreateSpriteFontStub()
        };
        var node = new SongListNode { Type = NodeType.Box, Title = "Folder" };
        var titleTexture = CreateTexture(width: 240, height: 32);
        var fakeRenderer = (SongBarRenderer)RuntimeHelpers.GetUninitializedObject(typeof(SongBarRenderer));

        SetField(display, "_useEnhancedRendering", true);
        SetField(display, "_barRenderer", fakeRenderer);
        GetField<Dictionary<string, SongBarInfo>>(display, "_barInfoCache")[$"{node.GetHashCode()}_0"] = new SongBarInfo
        {
            SongNode = node,
            BarType = BarType.Box,
            TitleTexture = titleTexture.Object,
            TitleString = "Folder",
            DifficultyLevel = 0
        };

        InvokePrivate<object?>(
            display,
            "DrawSongItemWithPerspective",
            (SpriteBatch)null!,
            node,
            new Rectangle(100, 200, 510, 48),
            false,
            false,
            0,
            1f,
            1f);

        titleTexture.Verify(x => x.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<float>(), It.IsAny<Vector2>()), Times.Once);
    }

    [Fact]
    public void DrawBarInfoWithPerspective_WhenOptionalAssetsAreMissing_ShouldReturnWithoutThrowing()
    {
        var display = new SongListDisplay
        {
            Font = null
        };
        var barInfo = new SongBarInfo
        {
            SongNode = new SongListNode
            {
                Type = NodeType.Score,
                Title = "Song 0",
                DatabaseSong = new SongEntity
                {
                    Title = "Song 0",
                    Artist = null
                }
            },
            BarType = BarType.Score,
            TitleTexture = null,
            PreviewImage = null,
            ClearLamp = null,
            TitleString = "Song 0"
        };

        var exception = Record.Exception(() => InvokePrivate<object?>(
            display,
            "DrawBarInfoWithPerspective",
            (SpriteBatch)null!,
            barInfo,
            new Rectangle(665, 269, 510, 48),
            false,
            false,
            1f,
            1f));

        Assert.Null(exception);
    }

    [Fact]
    public void LoadCommentBarTexture_WhenResourceManagerMissing_ShouldLeaveTextureUnset()
    {
        var display = new SongListDisplay();

        SetField(display, "_resourceManager", null);
        InvokePrivate<object?>(display, "LoadCommentBarTexture");

        Assert.Null(GetField<ITexture?>(display, "_commentBarTexture"));
    }

    [Fact]
    public void LoadCommentBarTexture_WhenLoadSucceeds_ShouldAssignTexture()
    {
        var display = new SongListDisplay();
        var resourceManager = new Mock<IResourceManager>();
        var commentBarTexture = new Mock<ITexture>().Object;
        resourceManager.Setup(x => x.LoadTexture(TexturePath.CommentBar)).Returns(commentBarTexture);

        SetField(display, "_resourceManager", resourceManager.Object);
        InvokePrivate<object?>(display, "LoadCommentBarTexture");

        Assert.Same(commentBarTexture, GetField<ITexture?>(display, "_commentBarTexture"));
    }

    [Fact]
    public void GetSkinBarTexture_WhenSkinTexturesDisabled_ShouldReturnNull()
    {
        var display = new SongListDisplay();

        SetField(display, "_skinBarTexturesLoaded", false);

        Assert.Null(InvokePrivate<ITexture?>(display, "GetSkinBarTexture", BarType.Score, true));
    }

    [Fact]
    public void UpdateScrollAnimation_WhenTargetAlreadyReached_ShouldLeaveQueueAndPositionUntouched()
    {
        var display = new SongListDisplay();
        var queue = GetTextureGenerationQueue(display);
        queue.Clear();

        SetField(display, "_currentScrollCounter", 200);
        SetField(display, "_targetScrollCounter", 200);

        InvokePrivate<object?>(display, "UpdateScrollAnimation", 1.0 / 60.0);

        Assert.Equal(200, GetField<int>(display, "_currentScrollCounter"));
        Assert.Empty(queue);
    }

    [Fact]
    public void DrawScrollbar_WhenTrackAndIndicatorResourcesMissing_ShouldReturnWithoutThrowing()
    {
        var display = new SongListDisplay
        {
            CurrentList = CreateSongs(3),
            WhitePixel = null
        };

        SetField(display, "_scrollbarTexture", null);
        SetField(display, "_selectedIndex", 1);

        var exception = Record.Exception(() => InvokePrivate<object?>(display, "DrawScrollbar", (SpriteBatch)null!));

        Assert.Null(exception);
    }

    [Fact]
    public void DrawCommentBarCommentText_WhenTextIsEmptyOrFontMissing_ShouldReturnWithoutThrowing()
    {
        var display = new SongListDisplay
        {
            Font = null
        };

        var emptyException = Record.Exception(() => InvokePrivate<object?>(display, "DrawCommentBarCommentText", (SpriteBatch)null!, string.Empty));
        var noFontException = Record.Exception(() => InvokePrivate<object?>(display, "DrawCommentBarCommentText", (SpriteBatch)null!, "comment"));

        Assert.Null(emptyException);
        Assert.Null(noFontException);
    }

    [Fact]
    public void DrawArtistNameWithManagedFont_WhenArtistMissingOrManagedFontMissing_ShouldReturnWithoutThrowing()
    {
        var display = new SongListDisplay
        {
            ManagedFont = null
        };
        var itemBounds = new Rectangle(665, 269, 510, 48);

        var missingArtistException = Record.Exception(() => InvokePrivate<object?>(
            display,
            "DrawArtistNameWithManagedFont",
            (SpriteBatch)null!,
            string.Empty,
            itemBounds,
            Vector2.One,
            1f));

        var missingFontException = Record.Exception(() => InvokePrivate<object?>(
            display,
            "DrawArtistNameWithManagedFont",
            (SpriteBatch)null!,
            "Artist",
            itemBounds,
            Vector2.One,
            1f));

        Assert.Null(missingArtistException);
        Assert.Null(missingFontException);
    }

    [Fact]
    public void DrawArtistNameWithManagedFont_WhenArtistExists_ShouldMeasureTruncateAndDraw()
    {
        var display = new SongListDisplay();
        var managedFont = CreateManagedFont();
        var itemBounds = new Rectangle(665, 269, 510, 48);
        string? drawnText = null;

        managedFont.Setup(x => x.MeasureString(It.IsAny<string>())).Returns((string text) => new Vector2(text.Length * 24, 16));
        managedFont
            .Setup(x => x.DrawString(It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()))
            .Callback<SpriteBatch, string, Vector2, Color>((_, text, _, _) => drawnText = text);
        display.ManagedFont = managedFont.Object;

        InvokePrivate<object?>(
            display,
            "DrawArtistNameWithManagedFont",
            (SpriteBatch)null!,
            "Artist Name That Needs Truncation Artist Name That Needs Truncation Again",
            itemBounds,
            Vector2.One,
            1f);

        managedFont.Verify(x => x.MeasureString(It.IsAny<string>()), Times.AtLeastOnce);
        managedFont.Verify(x => x.DrawString(It.IsAny<SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()), Times.Once);
        Assert.NotNull(drawnText);
        Assert.EndsWith("...", drawnText);
    }

    [Fact]
    public void Dispose_ShouldReleaseManagedTexturesAndClearCaches()
    {
        var display = new SongListDisplay();
        var scrollbar = new Mock<ITexture>();
        var score = new Mock<ITexture>();
        var scoreSelected = new Mock<ITexture>();
        var box = new Mock<ITexture>();
        var boxSelected = new Mock<ITexture>();
        var other = new Mock<ITexture>();
        var otherSelected = new Mock<ITexture>();
        var comment = new Mock<ITexture>();
        var title = new Mock<ITexture>();
        var preview = new Mock<ITexture>();
        var lamp = new Mock<ITexture>();
        var cachedTitleTexture = (TrackingTexture2D)RuntimeHelpers.GetUninitializedObject(typeof(TrackingTexture2D));
        var cachedPreviewTexture = (TrackingTexture2D)RuntimeHelpers.GetUninitializedObject(typeof(TrackingTexture2D));

        SetField(display, "_scrollbarTexture", scrollbar.Object);
        SetField(display, "_barScoreTexture", score.Object);
        SetField(display, "_barScoreSelectedTexture", scoreSelected.Object);
        SetField(display, "_barBoxTexture", box.Object);
        SetField(display, "_barBoxSelectedTexture", boxSelected.Object);
        SetField(display, "_barOtherTexture", other.Object);
        SetField(display, "_barOtherSelectedTexture", otherSelected.Object);
        SetField(display, "_commentBarTexture", comment.Object);
        SetField(display, "_skinBarTexturesLoaded", true);

        var songBarCache = (IDictionary)GetField<object>(display, "_songBarCache");
        songBarCache[1] = new SongBar();

        var barInfoCache = (IDictionary)GetField<object>(display, "_barInfoCache");
        barInfoCache["info"] = new SongBarInfo
        {
            TitleTexture = title.Object,
            PreviewImage = preview.Object,
            ClearLamp = lamp.Object
        };

        var titleBarCache = (IDictionary)GetField<object>(display, "_titleBarCache");
        titleBarCache[1] = cachedTitleTexture;

        var previewImageCache = (IDictionary)GetField<object>(display, "_previewImageCache");
        previewImageCache[1] = cachedPreviewTexture;

        display.Dispose();

        scrollbar.Verify(x => x.RemoveReference(), Times.Once);
        score.Verify(x => x.RemoveReference(), Times.Once);
        scoreSelected.Verify(x => x.RemoveReference(), Times.Once);
        box.Verify(x => x.RemoveReference(), Times.Once);
        boxSelected.Verify(x => x.RemoveReference(), Times.Once);
        other.Verify(x => x.RemoveReference(), Times.Once);
        otherSelected.Verify(x => x.RemoveReference(), Times.Once);
        comment.Verify(x => x.RemoveReference(), Times.Once);
        title.Verify(x => x.RemoveReference(), Times.Once);
        preview.Verify(x => x.RemoveReference(), Times.Once);
        lamp.Verify(x => x.RemoveReference(), Times.Once);
        Assert.True(cachedTitleTexture.WasDisposed);
        Assert.True(cachedPreviewTexture.WasDisposed);
        Assert.Empty((IDictionary)GetField<object>(display, "_titleBarCache"));
        Assert.Empty((IDictionary)GetField<object>(display, "_previewImageCache"));
        Assert.Empty((IDictionary)GetField<object>(display, "_songBarCache"));
        Assert.Empty((IDictionary)GetField<object>(display, "_barInfoCache"));
        Assert.False(GetField<bool>(display, "_skinBarTexturesLoaded"));
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

    private static List<SongListNode> CreateSongsForDraw()
    {
        return
        [
            new SongListNode
            {
                Type = NodeType.Score,
                Title = "Song 0",
                DatabaseSong = new SongEntity
                {
                    Title = "Song 0",
                    Artist = "Artist 0",
                    Comment = ""
                },
                Scores = new SongScore[5]
            },
            new SongListNode
            {
                Type = NodeType.Score,
                Title = "Song 1",
                DatabaseSong = new SongEntity
                {
                    Title = "Song 1",
                    Artist = "Artist 1",
                    Comment = ""
                },
                Scores = new SongScore[5]
            }
        ];
    }

    private static Mock<IFont> CreateManagedFont()
    {
        var font = new Mock<IFont>();
        font.SetupGet(x => x.SpriteFont).Returns((Microsoft.Xna.Framework.Graphics.SpriteFont?)null);
        font.Setup(x => x.MeasureString(It.IsAny<string>())).Returns((string text) => new Vector2(text.Length * 8, 16));
        font.Setup(x => x.DrawString(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<string>(), It.IsAny<Vector2>(), It.IsAny<Color>()));
        return font;
    }

    private static Mock<ITexture> CreateTexture(int width = 64, int height = 64)
    {
        var texture = new Mock<ITexture>();
        texture.SetupGet(x => x.Width).Returns(width);
        texture.SetupGet(x => x.Height).Returns(height);
        texture.Setup(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>()));
        texture.Setup(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>(), It.IsAny<Rectangle?>()));
        texture.Setup(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Rectangle>(), It.IsAny<Rectangle?>(), It.IsAny<Color>(), It.IsAny<float>(), It.IsAny<Vector2>(), It.IsAny<SpriteEffects>(), It.IsAny<float>()));
        texture.Setup(x => x.Draw(It.IsAny<Microsoft.Xna.Framework.Graphics.SpriteBatch>(), It.IsAny<Vector2>(), It.IsAny<Vector2>(), It.IsAny<float>(), It.IsAny<Vector2>()));
        return texture;
    }

    private static SpriteFont CreateSpriteFont((char character, int width)[] glyphs, int lineSpacing = 16, char? defaultCharacter = null)
    {
        var texture = (Texture2D)RuntimeHelpers.GetUninitializedObject(typeof(Texture2D));
        var glyphBounds = new List<Rectangle>();
        var cropping = new List<Rectangle>();
        var characters = new List<char>();
        var kerning = new List<Vector3>();
        var x = 0;

        foreach (var (character, width) in glyphs.OrderBy(glyph => glyph.character))
        {
            glyphBounds.Add(new Rectangle(x, 0, width, lineSpacing));
            cropping.Add(new Rectangle(0, 0, width, lineSpacing));
            characters.Add(character);
            kerning.Add(new Vector3(0, width, 0));
            x += width;
        }

        return (SpriteFont)Activator.CreateInstance(
            typeof(SpriteFont),
            BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic,
            binder: null,
            args: [texture, glyphBounds, cropping, characters, lineSpacing, 0f, kerning, defaultCharacter],
            culture: null)!;
    }

    private static SpriteFont CreateSpriteFontStub()
    {
        return (SpriteFont)RuntimeHelpers.GetUninitializedObject(typeof(SpriteFont));
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

    private sealed class TrackingTexture2D : Texture2D
    {
        public TrackingTexture2D() : base(null!, 1, 1)
        {
        }

        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
        }
    }
}

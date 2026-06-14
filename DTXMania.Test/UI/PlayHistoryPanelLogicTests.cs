using System.Linq;
using System.Reflection;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;
using Xunit;

namespace DTXMania.Test.UI;

[Trait("Category", "UI")]
public class PlayHistoryPanelLogicTests
{
    [Fact]
    public void UpdateSongInfo_WithHistoryForSelectedDifficulty_ShouldShowRows()
    {
        var panel = new PlayHistoryPanel();
        var node = new SongListNode { Type = NodeType.Score };
        node.Scores[0] = new SongScore { PlayHistoryLines = ["1.26/6/13 Cleared (S: 90.00)"] };
        node.Scores[1] = new SongScore { PlayHistoryLines = ["2.26/6/12 Failed (B: 70.00)"] };

        panel.UpdateSongInfo(node, 1);

        Assert.True(panel.Visible);
        // NX row text is rendered verbatim; a "Failed" run with a grade is not rewritten.
        Assert.Equal(new[] { "2.26/6/12 Failed (B: 70.00)" }, GetRows(panel));
    }

    [Fact]
    public void UpdateSongInfo_WithFailedHistoryThatHasScore_ShouldPreserveFailedText()
    {
        var panel = new PlayHistoryPanel();
        var node = new SongListNode { Type = NodeType.Score };
        node.Scores[1] = new SongScore { PlayHistoryLines = ["9.26/5/28 Failed (B: 70.10)"] };

        panel.UpdateSongInfo(node, 1);

        Assert.Equal(new[] { "9.26/5/28 Failed (B: 70.10)" }, GetRows(panel));
    }

    [Fact]
    public void UpdateSongInfo_WithBareFailedHistory_ShouldKeepFailed()
    {
        var panel = new PlayHistoryPanel();
        var node = new SongListNode { Type = NodeType.Score };
        node.Scores[1] = new SongScore { PlayHistoryLines = ["9.26/5/28 Failed"] };

        panel.UpdateSongInfo(node, 1);

        Assert.Equal(new[] { "9.26/5/28 Failed" }, GetRows(panel));
    }

    [Fact]
    public void DefaultTextScale_ShouldBeSmallerThanSharedUiFont()
    {
        var panel = new PlayHistoryPanel();

        var property = typeof(PlayHistoryPanel).GetProperty("TextScale");

        Assert.NotNull(property);
        var scale = Assert.IsType<float>(property!.GetValue(panel));
        Assert.InRange(scale, 0.1f, 0.99f);
    }

    [Fact]
    public void UpdateSongInfo_WithMoreThanFiveHistoryRows_ShouldKeepFirstFive()
    {
        var panel = new PlayHistoryPanel();
        var node = new SongListNode { Type = NodeType.Score };
        node.Scores[0] = new SongScore
        {
            PlayHistoryLines =
            [
                "1.26/6/13 Cleared (S: 90.00)",
                "2.26/6/12 Cleared (A: 88.00)",
                "3.26/6/11 Failed (B: 75.00)",
                "4.26/6/10 Cleared (C: 65.00)",
                "5.26/6/9 Failed (D: 55.00)",
                "6.26/6/8 Failed (E: 45.00)"
            ]
        };

        panel.UpdateSongInfo(node, 0);

        Assert.Equal(5, GetRows(panel).Length);
        Assert.DoesNotContain("6.26/6/8 Failed (E: 45.00)", GetRows(panel));
    }

    [Fact]
    public void UpdateSongInfo_WithNoHistoryForSelectedScore_ShouldShowEmptyBadge()
    {
        var panel = new PlayHistoryPanel();
        var node = new SongListNode { Type = NodeType.Score };
        node.Scores[0] = new SongScore();

        panel.UpdateSongInfo(node, 0);

        Assert.True(panel.Visible);
        Assert.Empty(GetRows(panel));
    }

    [Fact]
    public void UpdateSongInfo_WithFolder_ShouldHide()
    {
        var panel = new PlayHistoryPanel { Visible = true };

        panel.UpdateSongInfo(new SongListNode { Type = NodeType.Box }, 0);

        Assert.False(panel.Visible);
        Assert.Empty(GetRows(panel));
    }

    [Fact]
    public void UpdateSongInfo_WithNull_ShouldClearAndHide()
    {
        // Null is a supported, contract-level input (the selection pipeline signals
        // "nothing selected" with null). It must clear rows and hide, not throw.
        var panel = new PlayHistoryPanel();
        var node = new SongListNode { Type = NodeType.Score };
        node.Scores[0] = new SongScore { PlayHistoryLines = ["1.26/6/13 Cleared (S: 90.00)"] };
        panel.UpdateSongInfo(node, 0);
        Assert.True(panel.Visible);

        panel.UpdateSongInfo(null, 0);

        Assert.False(panel.Visible);
        Assert.Empty(GetRows(panel));
    }

    [Fact]
    public void ManagedFontSetter_AloneDerivesBackingFont()
    {
        // After removing the redundant raw Font property, ManagedFont is the single
        // source of truth: assigning it must populate the backing _font used at
        // draw time, with no separate Font setter required.
        var panel = new PlayHistoryPanel();

        Assert.Null(GetBackingFont(panel));

        var font = new Mock<IFont>();
        font.SetupGet(f => f.SpriteFont).Returns(null as SpriteFont);
        panel.ManagedFont = font.Object;

        // Assigning ManagedFont wires the backing field through to the derived
        // SpriteFont (null here, but the assignment path is exercised). Clearing
        // it returns the backing field to null.
        Assert.NotNull(typeof(PlayHistoryPanel)
            .GetField("_managedFont", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(panel));
        Assert.Same(font.Object, panel.ManagedFont);

        panel.ManagedFont = null;
        Assert.Null(panel.ManagedFont);
        Assert.Null(GetBackingFont(panel));
    }

    private static object? GetBackingFont(PlayHistoryPanel panel)
    {
        return typeof(PlayHistoryPanel)
            .GetField("_font", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(panel);
    }

    [Fact]
    public void Initialize_WhenTextureLoadFails_ShouldNotThrow()
    {
        var panel = new PlayHistoryPanel();
        var rm = new Mock<IResourceManager>();
        rm.Setup(r => r.LoadTexture(TexturePath.PlayHistoryPanel)).Throws(new System.Exception("missing"));

        var ex = Record.Exception(() => panel.Initialize(rm.Object));

        Assert.Null(ex);
    }

    [Fact]
    public void Dispose_AfterInitialize_ShouldReleasePanelTexture()
    {
        var panel = new PlayHistoryPanel();
        var rm = new Mock<IResourceManager>();
        var texture = new Mock<ITexture>();
        rm.Setup(r => r.LoadTexture(TexturePath.PlayHistoryPanel)).Returns(texture.Object);

        panel.Initialize(rm.Object);
        panel.Dispose();

        texture.Verify(t => t.RemoveReference(), Times.Once);
    }

    [Fact]
    public void Draw_WhenPanelTextureDisposed_ShouldReleasePanelTextureReference()
    {
        var panel = new PlayHistoryPanel();
        var rm = new Mock<IResourceManager>();
        var texture = new Mock<ITexture>();
        var node = new SongListNode { Type = NodeType.Score };
        node.Scores[0] = new SongScore { PlayHistoryLines = ["1.26/6/13 Cleared (S: 90.00)"] };

        texture
            .Setup(t => t.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Vector2>()))
            .Throws(new System.ObjectDisposedException("texture"));
        rm.Setup(r => r.LoadTexture(TexturePath.PlayHistoryPanel)).Returns(texture.Object);

        panel.Initialize(rm.Object);
        panel.UpdateSongInfo(node, 0);
        panel.Activate();
        panel.Draw(null!, 0.0);

        texture.Verify(t => t.RemoveReference(), Times.Once);
        Assert.Null(typeof(PlayHistoryPanel)
            .GetField("_panelTexture", BindingFlags.Instance | BindingFlags.NonPublic)!
            .GetValue(panel));
    }

    [Fact]
    public void Draw_WithNoHistoryForSelectedScore_ShouldDrawBadgeTexture()
    {
        var panel = new PlayHistoryPanel();
        var rm = new Mock<IResourceManager>();
        var texture = new Mock<ITexture>();
        var node = new SongListNode { Type = NodeType.Score };
        node.Scores[0] = new SongScore();

        rm.Setup(r => r.LoadTexture(TexturePath.PlayHistoryPanel)).Returns(texture.Object);

        panel.Initialize(rm.Object);
        panel.UpdateSongInfo(node, 0);
        panel.Activate();
        panel.Draw(null!, 0.0);

        texture.Verify(t => t.Draw(It.IsAny<SpriteBatch>(), It.IsAny<Vector2>()), Times.Once);
    }

    private static string[] GetRows(PlayHistoryPanel panel)
    {
        var field = typeof(PlayHistoryPanel).GetField("_historyLines", BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(field);
        return ((string[])field!.GetValue(panel)!).ToArray();
    }
}

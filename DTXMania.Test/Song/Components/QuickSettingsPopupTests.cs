using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Song.Components;
using Moq;
using Xunit;
using PopupLayout = DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.QuickSettingsPopup;

namespace DTXMania.Test.Song.Components;

[Trait("Category", "Unit")]
public sealed class QuickSettingsPopupTests
{
    private bool _toggle;
    private int _number = 5;

    private QuickSettingsPopup OpenPopup()
    {
        var popup = new QuickSettingsPopup();
        popup.Open(new IConfigItem[]
        {
            new ToggleConfigItem("Toggle", () => _toggle, value => _toggle = value),
            new IntegerConfigItem("Number", () => _number, value => _number = value, minValue: 0, maxValue: 10)
        });
        return popup;
    }

    [Fact]
    public void MoveUpAndDown_WrapAcrossItemsAndReturnRow()
    {
        var popup = OpenPopup();

        popup.HandleCommand(InputCommandType.MoveUp);
        Assert.Equal(2, popup.SelectedIndex); // Return row

        popup.HandleCommand(InputCommandType.MoveDown);
        Assert.Equal(0, popup.SelectedIndex);
    }

    [Fact]
    public void Activate_OnToggle_ChangesValue()
    {
        var popup = OpenPopup();

        popup.HandleCommand(InputCommandType.Activate);

        Assert.True(_toggle);
        Assert.True(popup.IsOpen);
    }

    [Fact]
    public void Activate_OnNumber_EditsWithUpAndDown()
    {
        var popup = OpenPopup();
        popup.HandleCommand(InputCommandType.MoveDown);

        popup.HandleCommand(InputCommandType.Activate);
        Assert.True(popup.IsEditingValue);

        popup.HandleCommand(InputCommandType.MoveUp);
        popup.HandleCommand(InputCommandType.MoveUp);
        popup.HandleCommand(InputCommandType.MoveDown);
        Assert.Equal(6, _number);
        Assert.Equal(1, popup.SelectedIndex);

        popup.HandleCommand(InputCommandType.Activate);
        Assert.False(popup.IsEditingValue);
    }

    [Fact]
    public void Activate_OnReturn_ClosesAndRaisesClosed()
    {
        var popup = OpenPopup();
        int closed = 0;
        popup.Closed += (_, _) => closed++;
        popup.HandleCommand(InputCommandType.MoveUp);

        popup.HandleCommand(InputCommandType.Activate);

        Assert.False(popup.IsOpen);
        Assert.Equal(1, closed);
    }

    [Fact]
    public void Back_Closes()
    {
        var popup = OpenPopup();

        popup.HandleCommand(InputCommandType.Back);

        Assert.False(popup.IsOpen);
    }

    [Theory]
    [InlineData(new int[0], "OFF")]
    [InlineData(new[] { 5 }, "Auto BD")]
    [InlineData(new[] { 2 }, "Auto LP")]
    [InlineData(new[] { 2, 5 }, "2 Pedal Auto")]
    [InlineData(new[] { 0, 1, 2, 3, 4, 5, 6, 7, 8, 9 }, "All Auto")]
    [InlineData(new[] { 1, 3 }, QuickSettingsPopup.CustomAutoMode)]
    public void GetAutoMode_MatchesNxPresets(int[] lanes, string expected)
    {
        Assert.Equal(expected, QuickSettingsPopup.GetAutoMode(new HashSet<int>(lanes)));
    }

    [Fact]
    public void AutoModeItem_SetsPresetLanes()
    {
        var config = new ConfigManager();
        var autoMode = QuickSettingsPopup.CreateItems(config, () => true)
            .First(item => item.Name == "Auto Mode");

        for (int i = 0; i < 10 && QuickSettingsPopup.GetAutoMode(config.Config.AutoPlayLanes) != "2 Pedal Auto"; i++)
            autoMode.NextValue();

        Assert.True(config.Config.AutoPlayLanes.SetEquals(new[] { 2, 5 }));
        Assert.Equal("Auto Mode: 2 Pedal Auto", autoMode.GetDisplayText());
    }

    [Fact]
    public void PlaySpeedItem_WithoutFfmpeg_KeepsCurrentSpeed()
    {
        var config = new ConfigManager();
        int before = config.Config.PlaySpeedPercent;
        var playSpeed = QuickSettingsPopup.CreateItems(config, () => false)
            .First(item => item.Name == "Play Speed");

        playSpeed.NextValue();

        Assert.Equal(before, config.Config.PlaySpeedPercent);
    }

    [Fact]
    public void MoveLeftAndRight_StepTheSelectedValue()
    {
        var popup = OpenPopup();
        popup.HandleCommand(InputCommandType.MoveDown); // Number

        popup.HandleCommand(InputCommandType.MoveRight);
        popup.HandleCommand(InputCommandType.MoveRight);
        popup.HandleCommand(InputCommandType.MoveLeft);

        Assert.Equal(6, _number);
        Assert.Equal(2, popup.Items.Count);
    }

    [Fact]
    public void ClosedPopup_IgnoresCommandsAndRepeatCloses()
    {
        var popup = new QuickSettingsPopup();
        int closed = 0;
        popup.Closed += (_, _) => closed++;

        popup.HandleCommand(InputCommandType.Activate);
        popup.Close();

        Assert.False(popup.IsOpen);
        Assert.Equal(0, closed);
        Assert.False(popup.HandleInput(null!));
        Assert.Throws<System.ArgumentNullException>(() => popup.Open(null!));
    }

    [Fact]
    public void GetValueText_StripsTheNamePrefix()
    {
        var item = new ToggleConfigItem("No Fail", () => true, _ => { });
        var custom = new Mock<IConfigItem>();
        custom.SetupGet(x => x.Name).Returns("Other");
        custom.Setup(x => x.GetDisplayText()).Returns("no prefix");

        Assert.Equal("ON", QuickSettingsPopup.GetValueText(item));
        Assert.Equal("no prefix", QuickSettingsPopup.GetValueText(custom.Object));
    }

    [Fact]
    public void Initialize_LoadsSkinArtAndReleasesItOnDispose()
    {
        var background = new Mock<ITexture>();
        var resources = new Mock<IResourceManager>();
        resources.Setup(x => x.LoadTexture(TexturePath.QuickSettingsBackground)).Returns(background.Object);
        resources.Setup(x => x.LoadTexture(TexturePath.QuickSettingsCursor)).Throws(new System.IO.FileNotFoundException("missing"));
        var popup = new QuickSettingsPopup();

        popup.Initialize(null);
        popup.Initialize(resources.Object);
        popup.Dispose();

        background.Verify(x => x.RemoveReference(), Times.Once);
    }

    [Fact]
    public void CreateItems_EveryItemReadsAndWritesConfig()
    {
        var config = new ConfigManager();
        var items = QuickSettingsPopup.CreateItems(config, () => true);

        foreach (var item in items)
        {
            string before = item.GetDisplayText();
            item.NextValue();
            Assert.StartsWith(item.Name + ": ", item.GetDisplayText());
            Assert.NotEqual(before, item.GetDisplayText());
        }
    }

    [Fact]
    public void Layout_FitsRowsCursorAndFallbackPanelInsideThePopup()
    {
        Assert.True(PopupLayout.CursorX + PopupLayout.CursorWidth <= PopupLayout.Width);
        Assert.True(PopupLayout.FirstRowY + 8 * PopupLayout.RowHeight <= PopupLayout.HintY);
        Assert.True(PopupLayout.Bounds.Contains(PopupLayout.PanelFallbackBounds));
        Assert.NotEqual(PopupLayout.PanelFallbackColor, PopupLayout.CursorFallbackColor);
    }
}

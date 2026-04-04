using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using Moq;

namespace DTXMania.Test.UI;

[Trait("Category", "Unit")]
public class UIListInputTests
{
    [Theory]
    [InlineData(0, Keys.Up, 2)]
    [InlineData(2, Keys.Down, 0)]
    [InlineData(3, Keys.Home, 0)]
    [InlineData(0, Keys.End, 3)]
    public void HandleInput_WhenNavigationKeysArePressed_UpdatesSelection(int initialIndex, Keys keyPressed, int expectedIndex)
    {
        var itemCount = keyPressed is Keys.Up or Keys.Down ? 3 : 4;
        var list = CreateActiveList(itemCount);
        list.SelectedIndex = initialIndex;

        var handled = list.HandleInput(CreateInputState(keyPressed: keyPressed).Object);

        Assert.True(handled);
        Assert.Equal(expectedIndex, list.SelectedIndex);
    }

    [Fact]
    public void HandleInput_WhenEnterPressedWithoutSelection_ReturnsFalse()
    {
        var list = CreateActiveList(2);
        list.SelectedIndex = -1;

        var handled = list.HandleInput(CreateInputState(keyPressed: Keys.Enter).Object);

        Assert.False(handled);
        Assert.Equal(-1, list.SelectedIndex);
    }

    [Fact]
    public void HandleInput_WhenInputStateIsNull_ReturnsFalseAndSelectionIsUnchanged()
    {
        var list = CreateActiveList(3);
        list.SelectedIndex = 1;

        var handled = list.HandleInput(null!);

        Assert.False(handled);
        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void HandleInput_WhenMouseIsOutsideBounds_ClearsHoveredIndex()
    {
        var list = CreateActiveList(3);
        ReflectionHelpers.SetPrivateField(list, "_hoveredIndex", 1);

        var handled = ReflectionHelpers.InvokePrivateMethod<bool>(
            list,
            "HandleMouseInput",
            CreateInputState(mousePosition: new Vector2(-10, -10)).Object);

        Assert.False(handled);
        Assert.Equal(-1, ReflectionHelpers.GetPrivateField<int>(list, "_hoveredIndex"));
    }

    [Fact]
    public void HandleMouseInput_WhenInputStateIsNull_ReturnsFalseAndHoverIsUnchanged()
    {
        var list = CreateActiveList(3);
        ReflectionHelpers.SetPrivateField(list, "_hoveredIndex", 1);

        var handled = ReflectionHelpers.InvokePrivateMethod<bool>(list, "HandleMouseInput", new object[] { null! });

        Assert.False(handled);
        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(list, "_hoveredIndex"));
    }

    [Fact]
    public void HandleMouseInput_WhenMousePositionIsInvalid_ReturnsFalseAndStateIsUnchanged()
    {
        var list = CreateActiveList(3);
        list.SelectedIndex = 2;
        ReflectionHelpers.SetPrivateField(list, "_hoveredIndex", 1);

        var handled = ReflectionHelpers.InvokePrivateMethod<bool>(
            list,
            "HandleMouseInput",
            CreateInputState(mousePosition: new Vector2(float.NaN, float.NaN)).Object);

        Assert.False(handled);
        Assert.Equal(2, list.SelectedIndex);
        Assert.Equal(1, ReflectionHelpers.GetPrivateField<int>(list, "_hoveredIndex"));
    }

    [Fact]
    public void HandleInput_WhenLeftClickHitsHoveredItem_SelectsItem()
    {
        var list = CreateActiveList(3);

        var handled = ReflectionHelpers.InvokePrivateMethod<bool>(
            list,
            "HandleMouseInput",
            CreateInputState(mousePosition: new Vector2(10, 35), leftClick: true).Object);

        Assert.True(handled);
        Assert.Equal(1, list.SelectedIndex);
    }

    [Fact]
    public void HandleInput_WhenScrollWheelMoves_UpdatesScrollOffset()
    {
        var list = CreateActiveList(5, visibleItemCount: 3);
        list.ScrollOffset = 1;

        var handled = ReflectionHelpers.InvokePrivateMethod<bool>(
            list,
            "HandleMouseInput",
            CreateInputState(mousePosition: new Vector2(10, 10), scrollWheelDelta: 1).Object);

        Assert.True(handled);
        Assert.Equal(0, list.ScrollOffset);
    }

    private static UIList CreateActiveList(int itemCount, int visibleItemCount = 5)
    {
        var list = new UIList
        {
            Position = Vector2.Zero,
            Size = new Vector2(200, visibleItemCount * 30),
            VisibleItemCount = visibleItemCount
        };

        for (int i = 0; i < itemCount; i++)
        {
            list.AddItem($"Item {i}");
        }

        list.Activate();
        return list;
    }

    private static Mock<IInputState> CreateInputState(
        Keys? keyPressed = null,
        Vector2? mousePosition = null,
        bool leftClick = false,
        int scrollWheelDelta = 0)
    {
        var mockInput = new Mock<IInputState>();
        mockInput.Setup(i => i.MousePosition).Returns(mousePosition ?? new Vector2(-1000, -1000));
        mockInput.Setup(i => i.IsMouseButtonPressed(It.IsAny<MouseButton>()))
            .Returns<MouseButton>(button => button == MouseButton.Left && leftClick);
        mockInput.Setup(i => i.ScrollWheelDelta).Returns(scrollWheelDelta);
        mockInput.Setup(i => i.IsKeyPressed(It.IsAny<Keys>()))
            .Returns<Keys>(key => keyPressed.HasValue && key == keyPressed.Value);
        return mockInput;
    }
}

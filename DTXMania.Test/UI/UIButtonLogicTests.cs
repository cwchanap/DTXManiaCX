using System;
using System.Runtime.Serialization;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Moq;

namespace DTXMania.Test.UI;

[Trait("Category", "Unit")]
public class UIButtonLogicTests
{
    [Fact]
    public void ImageComponent_SetterShouldAssignButtonAsParent()
    {
        var button = CreateActiveButton();
        var image = new UIImage();

        button.ImageComponent = image;

        Assert.Same(image, button.ImageComponent);
        Assert.Same(button, image.Parent);
    }

    [Fact]
    public void PropertySetters_ShouldFallbackToSafeDefaults()
    {
        var button = CreateActiveButton();
        var idle = new ButtonStateAppearance();
        var hover = new ButtonStateAppearance();
        var pressed = new ButtonStateAppearance();
        var disabled = new ButtonStateAppearance();

        button.Text = "Test";
        button.Text = null!;
        button.IdleAppearance = idle;
        button.HoverAppearance = hover;
        button.PressedAppearance = pressed;
        button.DisabledAppearance = disabled;

        button.IdleAppearance = null!;
        button.HoverAppearance = null!;
        button.PressedAppearance = null!;
        button.DisabledAppearance = null!;

        Assert.Equal(string.Empty, button.Text);
        Assert.NotNull(button.IdleAppearance);
        Assert.NotNull(button.HoverAppearance);
        Assert.NotNull(button.PressedAppearance);
        Assert.NotNull(button.DisabledAppearance);
        Assert.NotSame(idle, button.IdleAppearance);
        Assert.NotSame(hover, button.HoverAppearance);
        Assert.NotSame(pressed, button.PressedAppearance);
        Assert.NotSame(disabled, button.DisabledAppearance);
    }

    [Fact]
    public void Click_WhenDisabled_ShouldNotRaiseEvent()
    {
        var button = CreateActiveButton();
        var clickCount = 0;
        button.Enabled = false;
        button.ButtonClicked += (_, _) => clickCount++;

        button.Click();

        Assert.Equal(0, clickCount);
    }

    [Fact]
    public void InputHandling_ShouldTrackHoverPressAndReleaseOverButton()
    {
        var button = CreateActiveButton();
        button.Position = Vector2.Zero;
        button.Size = new Vector2(100, 40);
        var clickCount = 0;
        button.ButtonClicked += (_, _) => clickCount++;

        var hoverHandled = HandleInput(button, CreateInputState(new Vector2(10, 10)).Object);
        button.Update(0);

        Assert.True(hoverHandled);
        Assert.True(button.IsHovered);
        Assert.False(button.IsPressed);
        Assert.Equal(ButtonState.Hover, button.CurrentState);
        Assert.Equal(0, clickCount);

        var pressHandled = HandleInput(button, CreateInputState(new Vector2(10, 10), isMouseDown: true).Object);
        button.Update(0);

        Assert.True(pressHandled);
        Assert.True(button.IsHovered);
        Assert.True(button.IsPressed);
        Assert.Equal(ButtonState.Pressed, button.CurrentState);
        Assert.Equal(0, clickCount);

        var releaseOutsideHandled = HandleInput(button, CreateInputState(new Vector2(150, 80), isMouseReleased: true).Object);
        button.Update(0);

        Assert.True(releaseOutsideHandled);
        Assert.False(button.IsHovered);
        Assert.False(button.IsPressed);
        Assert.Equal(ButtonState.Idle, button.CurrentState);
        Assert.Equal(0, clickCount);

        HandleInput(button, CreateInputState(new Vector2(10, 10), isMouseDown: true).Object);
        button.Update(0);

        var releaseHandled = HandleInput(button, CreateInputState(new Vector2(10, 10), isMouseReleased: true).Object);
        button.Update(0);

        Assert.True(releaseHandled);
        Assert.True(button.IsHovered);
        Assert.False(button.IsPressed);
        Assert.Equal(ButtonState.Hover, button.CurrentState);
        Assert.Equal(1, clickCount);
    }

    [Fact]
    public void SetBackgroundTexture_ShouldUpdateRequestedAppearance()
    {
        var button = CreateActiveButton();
        var idleTexture = CreateTextureStub();
        var pressedTexture = CreateTextureStub();
        var pressedSource = new Rectangle(1, 2, 3, 4);

        button.SetBackgroundTexture(ButtonState.Idle, idleTexture);
        button.SetBackgroundTexture(ButtonState.Pressed, pressedTexture, pressedSource);

        Assert.Same(idleTexture, button.IdleAppearance.BackgroundTexture);
        Assert.Null(button.IdleAppearance.BackgroundSourceRectangle);
        Assert.Same(pressedTexture, button.PressedAppearance.BackgroundTexture);
        Assert.Equal(pressedSource, button.PressedAppearance.BackgroundSourceRectangle);
        Assert.Null(button.HoverAppearance.BackgroundTexture);
        Assert.Null(button.DisabledAppearance.BackgroundTexture);
    }

    [Fact]
    public void SetBackgroundTexture_ShouldUpdateHoverAndDisabledAppearances()
    {
        var button = CreateActiveButton();
        var hoverTexture = CreateTextureStub();
        var disabledTexture = CreateTextureStub();
        var hoverSource = new Rectangle(5, 6, 7, 8);

        button.SetBackgroundTexture(ButtonState.Hover, hoverTexture, hoverSource);
        button.SetBackgroundTexture(ButtonState.Disabled, disabledTexture);

        Assert.Same(hoverTexture, button.HoverAppearance.BackgroundTexture);
        Assert.Equal(hoverSource, button.HoverAppearance.BackgroundSourceRectangle);
        Assert.Same(disabledTexture, button.DisabledAppearance.BackgroundTexture);
        Assert.Null(button.DisabledAppearance.BackgroundSourceRectangle);
    }

    [Fact]
    public void Click_WhenEnabled_ShouldRaiseEvent()
    {
        var button = CreateActiveButton();
        var clickCount = 0;
        button.ButtonClicked += (_, _) => clickCount++;

        button.Click();

        Assert.Equal(1, clickCount);
    }

    [Fact]
    public void OnUpdate_WhenDisabled_ShouldSetDisabledState()
    {
        var button = CreateActiveButton();
        button.Enabled = false;

        button.Update(0);

        Assert.Equal(ButtonState.Disabled, button.CurrentState);
    }

    [Fact]
    public void OnDraw_WhenInvisible_ShouldReturnWithoutThrowing()
    {
        var button = CreateActiveButton();
        button.Visible = false;

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(button, "OnDraw", null!, 0d));

        Assert.Null(exception);
    }

    [Fact]
    public void OnDraw_WhenVisibleWithoutFontOrBackgroundTexture_ShouldUseSafeNonDrawingPaths()
    {
        var button = CreateActiveButton();
        button.Position = new Vector2(10, 20);
        button.Size = new Vector2(100, 40);

        var exception = Record.Exception(() => ReflectionHelpers.InvokePrivateMethod(button, "OnDraw", null!, 0d));

        Assert.Null(exception);
    }

    [Fact]
    public void OnDraw_WhenImageComponentPresent_ShouldSyncBoundsAndInvokeImageDraw()
    {
        var button = CreateActiveButton();
        var image = new TrackingUIImage();
        button.Position = new Vector2(15, 25);
        button.Size = new Vector2(120, 50);
        button.ImageComponent = image;

        ReflectionHelpers.InvokePrivateMethod(button, "OnDraw", null!, 0d);

        Assert.True(image.DrawCalled);
        Assert.Equal(new Vector2(15, 25), image.Position);
        Assert.Equal(new Vector2(120, 50), image.Size);
    }

    [Theory]
    [InlineData(ButtonState.Idle, "IdleAppearance")]
    [InlineData(ButtonState.Hover, "HoverAppearance")]
    [InlineData(ButtonState.Pressed, "PressedAppearance")]
    [InlineData(ButtonState.Disabled, "DisabledAppearance")]
    public void GetCurrentAppearance_ShouldReturnAppearanceForCurrentState(ButtonState state, string propertyName)
    {
        var button = CreateActiveButton();
        ReflectionHelpers.SetPrivateField(button, "_currentState", state);

        var appearance = ReflectionHelpers.InvokePrivateMethod<ButtonStateAppearance>(button, "GetCurrentAppearance");
        var expectedAppearance = typeof(UIButton).GetProperty(propertyName)!.GetValue(button);

        Assert.Same(expectedAppearance, appearance);
    }

    [Fact]
    public void OnHandleInput_WhenDisabled_ShouldReturnFalse()
    {
        var button = CreateActiveButton();
        button.Enabled = false;

        var handled = ReflectionHelpers.InvokePrivateMethod<bool>(button, "OnHandleInput", CreateInputState(new Vector2(10, 10)).Object);

        Assert.False(handled);
    }

    private static UIButton CreateActiveButton()
    {
        var resourceManager = new Mock<IResourceManager>();
        var font = new Mock<IFont>();
        font.SetupGet(x => x.SpriteFont).Returns((SpriteFont?)null);
        resourceManager.Setup(x => x.LoadFont("DefaultFont", 20)).Returns(font.Object);

        var button = new UIButton(resourceManager.Object, "Button");
        button.Activate();
        return button;
    }

    private static Mock<IInputState> CreateInputState(Vector2 mousePosition, bool isMouseDown = false, bool isMouseReleased = false)
    {
        var inputState = new Mock<IInputState>();
        inputState.SetupGet(x => x.MousePosition).Returns(mousePosition);
        inputState.Setup(x => x.IsMouseButtonDown(MouseButton.Left)).Returns(isMouseDown);
        inputState.Setup(x => x.IsMouseButtonReleased(MouseButton.Left)).Returns(isMouseReleased);
        return inputState;
    }

    private static bool HandleInput(UIButton button, IInputState inputState) =>
        ReflectionHelpers.InvokePrivateMethod<bool>(button, "OnHandleInput", inputState);

    private static Texture2D CreateTextureStub()
    {
#pragma warning disable SYSLIB0050
        return (Texture2D)FormatterServices.GetUninitializedObject(typeof(Texture2D));
#pragma warning restore SYSLIB0050
    }

    private sealed class TrackingUIImage : UIImage
    {
        public bool DrawCalled { get; private set; }

        public override void Draw(SpriteBatch spriteBatch, double deltaTime)
        {
            DrawCalled = true;
        }
    }
}

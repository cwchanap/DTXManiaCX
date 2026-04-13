using System;
using System.Collections.Generic;
using System.Reflection;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Test.TestData;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;

namespace DTXMania.Test.Graphics;

[Trait("Category", "Unit")]
public class GraphicsManagerLogicTests
{
    [Fact]
    public void Constructor_WithNullGame_ShouldThrowArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new GraphicsManager(null!, ReflectionHelpers.CreateUninitialized<GraphicsDeviceManager>()));

        Assert.Equal("game", exception.ParamName);
    }

    [Fact]
    public void Constructor_WithNullDeviceManager_ShouldThrowArgumentNullException()
    {
        var exception = Assert.Throws<ArgumentNullException>(() => new GraphicsManager(ReflectionHelpers.CreateGame(), null!));

        Assert.Equal("deviceManager", exception.ParamName);
    }

    [Fact]
    public void Settings_ShouldReturnCloneOfCurrentSettings()
    {
        var manager = CreateManager(new GraphicsSettings
        {
            Width = 1280,
            Height = 720,
            IsFullscreen = false,
            VSync = true
        });

        var snapshot = manager.Settings;
        snapshot.Width = 1920;

        Assert.Equal(1280, manager.Settings.Width);
        Assert.NotSame(snapshot, manager.Settings);
    }

    [Fact]
    public void ApplySettings_WithNullSettings_ShouldThrowArgumentNullException()
    {
        var manager = CreateManager();

        var exception = Assert.Throws<ArgumentNullException>(() => manager.ApplySettings(null!));

        Assert.Equal("settings", exception.ParamName);
    }

    [Fact]
    public void ApplySettings_WithInvalidSettings_ShouldReturnFalse()
    {
        var manager = CreateManager();

        var result = manager.ApplySettings(new GraphicsSettings { Width = 0, Height = 720 });

        Assert.False(result);
    }

    [Fact]
    public void ApplySettings_WhenSettingsMatchCurrent_ShouldReturnTrue()
    {
        var current = new GraphicsSettings { Width = 1280, Height = 720, IsFullscreen = false, VSync = true };
        var manager = CreateManager(current);

        var result = manager.ApplySettings(current.Clone());

        Assert.True(result);
    }

    [Fact]
    public void ChangeResolution_WhenDimensionsAreInvalid_ShouldReturnFalse()
    {
        var manager = CreateManager();

        Assert.False(manager.ChangeResolution(-1, 720));
        Assert.False(manager.ChangeResolution(1280, 5000));
    }

    [Fact]
    public void SetFullscreen_WhenRequestedStateMatchesCurrent_ShouldReturnTrue()
    {
        var manager = CreateManager(new GraphicsSettings
        {
            Width = 1280,
            Height = 720,
            IsFullscreen = false,
            VSync = true
        });

        var result = manager.SetFullscreen(false);

        Assert.True(result);
    }

    [Fact]
    public void SetVSync_WhenRequestedStateMatchesCurrent_ShouldReturnTrue()
    {
        var manager = CreateManager(new GraphicsSettings
        {
            Width = 1280,
            Height = 720,
            IsFullscreen = false,
            VSync = true
        });

        var result = manager.SetVSync(true);

        Assert.True(result);
    }

    [Fact]
    public void IsResolutionSupported_WithInvalidDimensions_ShouldReturnFalse()
    {
        var manager = CreateManager();

        Assert.False(manager.IsResolutionSupported(0, 720));
        Assert.False(manager.IsResolutionSupported(1280, -1));
        Assert.False(manager.IsResolutionSupported(8000, 720));
    }

    [Fact]
    public void OnDeviceLost_ShouldRaiseDeviceLostEvent()
    {
        var manager = CreateManager();
        var raised = false;
        manager.DeviceLost += (_, _) => raised = true;

        ReflectionHelpers.InvokePrivateMethod(manager, "OnDeviceLost", null!, EventArgs.Empty);

        Assert.True(raised);
    }

    [Fact]
    public void OnDeviceReset_ShouldRaiseDeviceResetEvent()
    {
        var manager = CreateManager(renderTargetManager: CreateEmptyRenderTargetManager());
        var raised = false;
        manager.DeviceReset += (_, _) => raised = true;

        ReflectionHelpers.InvokePrivateMethod(manager, "OnDeviceReset", null!, EventArgs.Empty);

        Assert.True(raised);
    }

    [Fact]
    public void Dispose_ShouldDisposeRenderTargetManagerAndMarkDisposed()
    {
        var renderTargetManager = CreateEmptyRenderTargetManager();
        var manager = CreateManager(renderTargetManager: renderTargetManager);

        manager.Dispose();

        Assert.True(ReflectionHelpers.GetPrivateField<bool>(manager, "_disposed"));
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(renderTargetManager, "_disposed"));
    }

    [Fact]
    public void ResetDevice_WhenDeviceManagerIsUnavailable_ShouldReturnFalse()
    {
        var manager = CreateManager();

        Assert.False(manager.ResetDevice());
    }

    private static GraphicsManager CreateManager(GraphicsSettings? currentSettings = null, RenderTargetManager? renderTargetManager = null)
    {
        var manager = ReflectionHelpers.CreateUninitialized<GraphicsManager>();
        ReflectionHelpers.SetPrivateField(manager, "_game", null);
        ReflectionHelpers.SetPrivateField(manager, "_deviceManager", ReflectionHelpers.CreateUninitialized<GraphicsDeviceManager>());
        ReflectionHelpers.SetPrivateField(manager, "_logger", NullLogger<GraphicsManager>.Instance);
        ReflectionHelpers.SetPrivateField(manager, "_currentSettings", currentSettings ?? new GraphicsSettings());
        ReflectionHelpers.SetPrivateField(manager, "_renderTargetManager", renderTargetManager);
        ReflectionHelpers.SetPrivateField(manager, "_disposed", false);
        return manager;
    }

    private static RenderTargetManager CreateEmptyRenderTargetManager()
    {
        var manager = ReflectionHelpers.CreateUninitialized<RenderTargetManager>();
        ReflectionHelpers.SetPrivateField(manager, "_graphicsDevice", ReflectionHelpers.CreateUninitialized<GraphicsDevice>());
        ReflectionHelpers.SetPrivateField(manager, "_renderTargets", CreateRenderTargetDictionary());
        ReflectionHelpers.SetPrivateField(manager, "_disposed", false);
        return manager;
    }

    private static object CreateRenderTargetDictionary()
    {
        var infoType = typeof(RenderTargetManager).GetNestedType("RenderTargetInfo", BindingFlags.NonPublic)!;
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), infoType);
        return Activator.CreateInstance(dictionaryType)!;
    }

}

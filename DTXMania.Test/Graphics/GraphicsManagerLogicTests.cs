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
    public void ApplySettings_WhenDeviceChangesSucceed_ShouldUpdateCurrentSettingsAndRaiseEvent()
    {
        var original = new GraphicsSettings { Width = 1280, Height = 720, IsFullscreen = false, VSync = true };
        var requested = new GraphicsSettings { Width = 1920, Height = 1080, IsFullscreen = true, VSync = false };
        var manager = CreateTestableManager(original);
        GraphicsSettingsChangedEventArgs? args = null;
        manager.SettingsChanged += (_, eventArgs) => args = eventArgs;

        var result = manager.ApplySettings(requested);

        Assert.True(result);
        Assert.Equal([(1920, 1080, true, false)], manager.AppliedSettings);
        Assert.NotNull(args);
        Assert.Equal(1280, args!.OldSettings.Width);
        Assert.Equal(1920, args.NewSettings.Width);
        Assert.True(manager.Settings.IsFullscreen);
        Assert.False(manager.Settings.VSync);
    }

    [Fact]
    public void ApplySettings_WhenApplyDeviceChangesThrowsInvalidOperation_ShouldReturnFalseAndRestorePreviousSettings()
    {
        var original = new GraphicsSettings { Width = 1280, Height = 720, IsFullscreen = false, VSync = true };
        var requested = new GraphicsSettings { Width = 1600, Height = 900, IsFullscreen = true, VSync = false };
        var manager = CreateTestableManager(original);
        manager.ApplyDeviceChangeExceptions.Enqueue(new InvalidOperationException("device not ready"));

        var result = manager.ApplySettings(requested);

        Assert.False(result);
        Assert.Equal(
            [(1600, 900, true, false), (1280, 720, false, true)],
            manager.AppliedSettings);
        Assert.Equal(2, manager.ApplyDeviceChangesCallCount);
        Assert.Equal(1280, manager.Settings.Width);
        Assert.Equal(720, manager.Settings.Height);
        Assert.False(manager.Settings.IsFullscreen);
        Assert.True(manager.Settings.VSync);
    }

    [Fact]
    public void ApplySettings_WhenRevertAlsoThrows_ShouldReturnFalseAndKeepOriginalSettingsSnapshot()
    {
        var original = new GraphicsSettings { Width = 1280, Height = 720, IsFullscreen = false, VSync = true };
        var requested = new GraphicsSettings { Width = 1920, Height = 1080, IsFullscreen = true, VSync = false };
        var manager = CreateTestableManager(original);
        manager.ApplyDeviceChangeExceptions.Enqueue(new InvalidOperationException("apply failed"));
        manager.ApplyDeviceChangeExceptions.Enqueue(new Exception("revert failed"));

        var result = manager.ApplySettings(requested);

        Assert.False(result);
        Assert.Equal(
            [(1920, 1080, true, false), (1280, 720, false, true)],
            manager.AppliedSettings);
        Assert.Equal(2, manager.ApplyDeviceChangesCallCount);
        Assert.Equal(1280, manager.Settings.Width);
        Assert.Equal(720, manager.Settings.Height);
        Assert.False(manager.Settings.IsFullscreen);
        Assert.True(manager.Settings.VSync);
    }

    [Fact]
    public void ApplySettings_WhenApplyDeviceChangesThrowsArgumentException_ShouldReturnFalseAndRestorePreviousSettings()
    {
        var original = new GraphicsSettings { Width = 1280, Height = 720, IsFullscreen = false, VSync = true };
        var requested = new GraphicsSettings { Width = 1440, Height = 900, IsFullscreen = true, VSync = false };
        var manager = CreateTestableManager(original);
        manager.ApplyDeviceChangeExceptions.Enqueue(new ArgumentException("invalid dimensions"));

        var result = manager.ApplySettings(requested);

        Assert.False(result);
        Assert.Equal(
            [(1440, 900, true, false), (1280, 720, false, true)],
            manager.AppliedSettings);
        Assert.Equal(2, manager.ApplyDeviceChangesCallCount);
        Assert.Equal(1280, manager.Settings.Width);
        Assert.Equal(720, manager.Settings.Height);
        Assert.False(manager.Settings.IsFullscreen);
        Assert.True(manager.Settings.VSync);
    }

    [Fact]
    public void ChangeResolution_WhenDimensionsAreInvalid_ShouldReturnFalse()
    {
        var manager = CreateManager();

        Assert.False(manager.ChangeResolution(-1, 720));
        Assert.False(manager.ChangeResolution(1280, 5000));
    }

    [Fact]
    public void ChangeResolution_WhenDimensionsAreValid_ShouldApplyUpdatedResolution()
    {
        var manager = CreateTestableManager(new GraphicsSettings
        {
            Width = 1280,
            Height = 720,
            IsFullscreen = false,
            VSync = true
        });

        var result = manager.ChangeResolution(1600, 900);

        Assert.True(result);
        Assert.Equal([(1600, 900, false, true)], manager.AppliedSettings);
        Assert.Equal(1600, manager.Settings.Width);
        Assert.Equal(900, manager.Settings.Height);
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
    public void ToggleFullscreen_ShouldInvertFullscreenFlagAndApplySettings()
    {
        var manager = CreateTestableManager(new GraphicsSettings
        {
            Width = 1280,
            Height = 720,
            IsFullscreen = false,
            VSync = true
        });

        var result = manager.ToggleFullscreen();

        Assert.True(result);
        Assert.Equal([(1280, 720, true, true)], manager.AppliedSettings);
        Assert.True(manager.Settings.IsFullscreen);
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

    [Fact]
    public void ResetDevice_WhenApplyDeviceChangesSucceeds_ShouldReturnTrue()
    {
        var manager = CreateTestableManager();

        var result = manager.ResetDevice();

        Assert.True(result);
        Assert.Equal(1, manager.ApplyDeviceChangesCallCount);
    }

    [Fact]
    public void PropertyGetters_WhenManagerUsesUninitializedDeviceManager_ShouldExposeConfiguredState()
    {
        var renderTargetManager = CreateEmptyRenderTargetManager();
        var manager = CreateManager(
            currentSettings: new GraphicsSettings { Width = 1280, Height = 720, IsFullscreen = false, VSync = true },
            renderTargetManager: renderTargetManager);

        Assert.Null(manager.GraphicsDevice);
        Assert.False(manager.IsDeviceAvailable);
        Assert.Same(renderTargetManager, manager.RenderTargetManager);
    }

    [Fact]
    public void Initialize_WhenGraphicsDeviceIsUnavailable_ShouldLeaveRenderTargetManagerUnset()
    {
        var manager = CreateManager();

        manager.Initialize();

        Assert.Null(manager.RenderTargetManager);
    }

    [Fact]
    public void GetAvailableDisplayModes_WhenDeviceIsUnavailable_ShouldReturnEmptyArray()
    {
        var manager = CreateManager();

        var modes = manager.GetAvailableDisplayModes();

        Assert.Empty(modes);
    }

    [Fact]
    public void IsResolutionSupported_WhenDeviceIsUnavailableAndResolutionIsValid_ShouldReturnTrue()
    {
        var manager = CreateManager(new GraphicsSettings { Width = 1280, Height = 720, IsFullscreen = true, VSync = true });

        Assert.True(manager.IsResolutionSupported(1280, 720));
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

    private static TestableGraphicsManager CreateTestableManager(GraphicsSettings? currentSettings = null, RenderTargetManager? renderTargetManager = null)
    {
        var manager = ReflectionHelpers.CreateUninitialized<TestableGraphicsManager>();
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

    private sealed class TestableGraphicsManager : GraphicsManager
    {
        private List<(int width, int height, bool fullscreen, bool vsync)>? _appliedSettings;
        private Queue<Exception?>? _applyDeviceChangeExceptions;

        private TestableGraphicsManager()
            : base(ReflectionHelpers.CreateGame(), ReflectionHelpers.CreateUninitialized<GraphicsDeviceManager>())
        {
        }

        public List<(int width, int height, bool fullscreen, bool vsync)> AppliedSettings => _appliedSettings ??= [];
        public Queue<Exception?> ApplyDeviceChangeExceptions => _applyDeviceChangeExceptions ??= new();
        public int ApplyDeviceChangesCallCount { get; private set; }

        protected override void SetDeviceManagerSettings(GraphicsSettings settings)
        {
            AppliedSettings.Add((settings.Width, settings.Height, settings.IsFullscreen, settings.VSync));
        }

        protected override void ApplyDeviceChanges()
        {
            ApplyDeviceChangesCallCount++;

            if (ApplyDeviceChangeExceptions.Count > 0)
            {
                var exception = ApplyDeviceChangeExceptions.Dequeue();
                if (exception != null)
                {
                    throw exception;
                }
            }
        }
    }

}

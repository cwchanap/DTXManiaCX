using DTXMania.Game.Lib.Graphics;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace DTXMania.Test.Graphics;

public class RenderTargetManagerTests
{
    [Fact]
    public void RenderTargetManager_Constructor_WithNullGraphicsDevice_ShouldThrowArgumentNullException()
    {
        // Arrange, Act & Assert
        Assert.Throws<ArgumentNullException>(() => new RenderTargetManager(null!));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void GetOrCreateRenderTarget_WithNullOrEmptyName_ShouldThrowArgumentException(string? name)
    {
        var manager = CreateManagerWithTargets();

        Assert.Throws<ArgumentException>(() => manager.GetOrCreateRenderTarget(name!, 1280, 720));
    }

    [Fact]
    public void GetRenderTarget_WhenTargetDoesNotExist_ShouldReturnNull()
    {
        var manager = CreateManagerWithTargets();

        Assert.Null(manager.GetRenderTarget("missing"));
    }

    [Fact]
    public void GetRenderTarget_WhenTargetExistsAndIsActive_ShouldReturnTarget()
    {
        var target = CreateTrackingRenderTarget();
        var manager = CreateManagerWithTargets(("main", target, 1280, 720, SurfaceFormat.Color, DepthFormat.None, 0));

        Assert.Same(target, manager.GetRenderTarget("main"));
    }

    [Fact]
    public void GetOrCreateRenderTarget_WhenMatchingTargetAlreadyExists_ShouldReturnExistingTarget()
    {
        var target = CreateTrackingRenderTarget();
        var manager = CreateManagerWithTargets(("scene", target, 640, 480, SurfaceFormat.Color, DepthFormat.Depth24, 2));

        var result = manager.GetOrCreateRenderTarget("scene", 640, 480, SurfaceFormat.Color, DepthFormat.Depth24, 2);

        Assert.Same(target, result);
    }

    [Fact]
    public void RemoveRenderTarget_WhenTargetExists_ShouldDisposeAndRemoveTarget()
    {
        var target = CreateTrackingRenderTarget();
        var manager = CreateManagerWithTargets(("overlay", target, 320, 240, SurfaceFormat.Color, DepthFormat.None, 0));

        manager.RemoveRenderTarget("overlay");

        Assert.True(target.WasDisposed);
        Assert.Equal(0, manager.Count);
        Assert.Null(manager.GetRenderTarget("overlay"));
    }

    [Fact]
    public void RemoveRenderTarget_WhenTargetDoesNotExist_ShouldReturnWithoutThrowing()
    {
        var manager = CreateManagerWithTargets();

        var exception = Record.Exception(() => manager.RemoveRenderTarget("missing"));

        Assert.Null(exception);
    }

    [Fact]
    public void Count_ShouldReflectManagedRenderTargets()
    {
        var manager = CreateManagerWithTargets(
            ("primary", CreateTrackingRenderTarget(), 1280, 720, SurfaceFormat.Color, DepthFormat.None, 0),
            ("secondary", CreateTrackingRenderTarget(), 640, 360, SurfaceFormat.Color, DepthFormat.Depth24, 0));

        Assert.Equal(2, manager.Count);
    }

    [Fact]
    public void Dispose_ShouldDisposeManagedTargetsAndClearDictionary()
    {
        var targetA = CreateTrackingRenderTarget();
        var targetB = CreateTrackingRenderTarget();
        var manager = CreateManagerWithTargets(
            ("a", targetA, 800, 600, SurfaceFormat.Color, DepthFormat.None, 0),
            ("b", targetB, 400, 300, SurfaceFormat.Color, DepthFormat.None, 0));

        manager.Dispose();

        Assert.True(targetA.WasDisposed);
        Assert.True(targetB.WasDisposed);
        Assert.Equal(0, manager.Count);
        Assert.True(ReflectionHelpers.GetPrivateField<bool>(manager, "_disposed"));
    }

    private static RenderTargetManager CreateManagerWithTargets(params (string Name, RenderTarget2D Target, int Width, int Height, SurfaceFormat Format, DepthFormat DepthFormat, int MultiSampleCount)[] entries)
    {
        var manager = (RenderTargetManager)RuntimeHelpers.GetUninitializedObject(typeof(RenderTargetManager));
        ReflectionHelpers.SetPrivateField(manager, "_graphicsDevice", RuntimeHelpers.GetUninitializedObject(typeof(GraphicsDevice)));
        ReflectionHelpers.SetPrivateField(manager, "_renderTargets", CreateRenderTargetDictionary(entries));
        ReflectionHelpers.SetPrivateField(manager, "_disposed", false);
        return manager;
    }

    private static object CreateRenderTargetDictionary(params (string Name, RenderTarget2D Target, int Width, int Height, SurfaceFormat Format, DepthFormat DepthFormat, int MultiSampleCount)[] entries)
    {
        var infoType = typeof(RenderTargetManager).GetNestedType("RenderTargetInfo", BindingFlags.NonPublic)!;
        var dictionaryType = typeof(Dictionary<,>).MakeGenericType(typeof(string), infoType);
        var dictionary = Activator.CreateInstance(dictionaryType)!;
        var addMethod = dictionaryType.GetMethod("Add")!;

        foreach (var entry in entries)
        {
            var info = Activator.CreateInstance(infoType, nonPublic: true)!;
            infoType.GetProperty("RenderTarget")!.SetValue(info, entry.Target);
            infoType.GetProperty("Width")!.SetValue(info, entry.Width);
            infoType.GetProperty("Height")!.SetValue(info, entry.Height);
            infoType.GetProperty("Format")!.SetValue(info, entry.Format);
            infoType.GetProperty("DepthFormat")!.SetValue(info, entry.DepthFormat);
            infoType.GetProperty("MultiSampleCount")!.SetValue(info, entry.MultiSampleCount);
            addMethod.Invoke(dictionary, new object[] { entry.Name, info });
        }

        return dictionary;
    }

    private static TrackingRenderTarget2D CreateTrackingRenderTarget()
    {
        return (TrackingRenderTarget2D)RuntimeHelpers.GetUninitializedObject(typeof(TrackingRenderTarget2D));
    }

    private sealed class TrackingRenderTarget2D : RenderTarget2D
    {
        public TrackingRenderTarget2D() : base(null!, 1, 1)
        {
        }

        public bool WasDisposed { get; private set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
        }
    }
}

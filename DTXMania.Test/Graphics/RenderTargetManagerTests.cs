using DTXMania.Game.Lib.Graphics;
using DTXMania.Test.TestData;
using Microsoft.Xna.Framework.Graphics;
using System;
using System.Collections.Generic;
using System.Reflection;

namespace DTXMania.Test.Graphics;

[Trait("Category", "Unit")]
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
        Assert.Equal(0, manager.CreateRenderTargetCallCount);
    }

    [Fact]
    public void GetOrCreateRenderTarget_WhenTargetDoesNotExist_ShouldCreateAndStoreTarget()
    {
        var created = CreateTrackingRenderTarget();
        var manager = CreateManagerWithTargets((w, h, f, d, msaa) =>
        {
            Assert.Equal(1024, w);
            Assert.Equal(576, h);
            Assert.Equal(SurfaceFormat.Color, f);
            Assert.Equal(DepthFormat.Depth24, d);
            Assert.Equal(0, msaa);
            return created;
        });

        var result = manager.GetOrCreateRenderTarget("new-target", 1024, 576, SurfaceFormat.Color, DepthFormat.Depth24, 0);

        Assert.Same(created, result);
        Assert.Same(created, manager.GetRenderTarget("new-target"));
        Assert.Equal(1, manager.CreateRenderTargetCallCount);
    }

    [Theory]
    [InlineData(800, 480, SurfaceFormat.Color, DepthFormat.Depth24)]
    [InlineData(640, 480, SurfaceFormat.Bgr565, DepthFormat.Depth24)]
    [InlineData(640, 480, SurfaceFormat.Color, DepthFormat.Depth16)]
    public void GetOrCreateRenderTarget_WhenExistingTargetParametersDiffer_ShouldDisposeAndReplaceTarget(
        int width,
        int height,
        SurfaceFormat format,
        DepthFormat depthFormat)
    {
        var existing = CreateTrackingRenderTarget();
        var replacement = CreateTrackingRenderTarget();
        var manager = CreateManagerWithTargets(
            (w, h, f, d, msaa) =>
            {
                Assert.Equal(width, w);
                Assert.Equal(height, h);
                Assert.Equal(format, f);
                Assert.Equal(depthFormat, d);
                Assert.Equal(2, msaa);
                return replacement;
            },
            ("scene", existing, 640, 480, SurfaceFormat.Color, DepthFormat.Depth24, 2));

        var result = manager.GetOrCreateRenderTarget("scene", width, height, format, depthFormat, 2);

        Assert.Same(replacement, result);
        Assert.True(existing.WasDisposed);
        Assert.Same(replacement, manager.GetRenderTarget("scene"));
        Assert.Equal(1, manager.CreateRenderTargetCallCount);
    }

    [Fact]
    public void GetOrCreateRenderTarget_WhenExistingTargetIsDisposed_ShouldCreateReplacement()
    {
        var existing = CreateTrackingRenderTarget();
        existing.SimulatedIsDisposed = true;
        var replacement = CreateTrackingRenderTarget();
        var manager = CreateManagerWithTargets(
            (w, h, f, d, msaa) => replacement,
            ("scene", existing, 640, 480, SurfaceFormat.Color, DepthFormat.Depth24, 2));

        var result = manager.GetOrCreateRenderTarget("scene", 640, 480, SurfaceFormat.Color, DepthFormat.Depth24, 2);

        Assert.Same(replacement, result);
        Assert.True(existing.WasDisposed);
        Assert.Same(replacement, manager.GetRenderTarget("scene"));
        Assert.Equal(1, manager.CreateRenderTargetCallCount);
    }

    [Fact]
    public void RecreateAllRenderTargets_ShouldDisposeAndReplaceTargetsUsingFactory()
    {
        var existing = CreateTrackingRenderTarget();
        var replacement = CreateTrackingRenderTarget();
        var manager = CreateManagerWithTargets(
            (w, h, f, d, msaa) =>
            {
                Assert.Equal(320, w);
                Assert.Equal(240, h);
                Assert.Equal(SurfaceFormat.Color, f);
                Assert.Equal(DepthFormat.Depth16, d);
                Assert.Equal(4, msaa);
                return replacement;
            },
            ("overlay", existing, 320, 240, SurfaceFormat.Color, DepthFormat.Depth16, 4));

        manager.RecreateAllRenderTargets();

        Assert.True(existing.WasDisposed);
        Assert.Same(replacement, manager.GetRenderTarget("overlay"));
        Assert.Equal(1, manager.CreateRenderTargetCallCount);
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

    private static TestRenderTargetManager CreateManagerWithTargets(
        params (string Name, RenderTarget2D Target, int Width, int Height, SurfaceFormat Format, DepthFormat DepthFormat, int MultiSampleCount)[] entries)
    {
        return CreateManagerWithTargets(null, entries);
    }

    private static TestRenderTargetManager CreateManagerWithTargets(
        Func<int, int, SurfaceFormat, DepthFormat, int, RenderTarget2D>? factory,
        params (string Name, RenderTarget2D Target, int Width, int Height, SurfaceFormat Format, DepthFormat DepthFormat, int MultiSampleCount)[] entries)
    {
        var manager = new TestRenderTargetManager(factory);
        var dictionary = ReflectionHelpers.GetPrivateField<object>(manager, "_renderTargets")!;
        var dictionaryType = dictionary.GetType();
        var addMethod = dictionaryType.GetMethod("Add")!;

        foreach (var entry in entries)
        {
            var info = CreateRenderTargetInfo(entry.Target, entry.Width, entry.Height, entry.Format, entry.DepthFormat, entry.MultiSampleCount);
            addMethod.Invoke(dictionary, new object[] { entry.Name, info });
        }

        return manager;
    }

    private static object CreateRenderTargetInfo(
        RenderTarget2D target,
        int width,
        int height,
        SurfaceFormat format,
        DepthFormat depthFormat,
        int multiSampleCount)
    {
        var infoType = typeof(RenderTargetManager).GetNestedType("RenderTargetInfo", BindingFlags.NonPublic)!;
        var info = Activator.CreateInstance(infoType, nonPublic: true)!;
        infoType.GetProperty("RenderTarget")!.SetValue(info, target);
        infoType.GetProperty("Width")!.SetValue(info, width);
        infoType.GetProperty("Height")!.SetValue(info, height);
        infoType.GetProperty("Format")!.SetValue(info, format);
        infoType.GetProperty("DepthFormat")!.SetValue(info, depthFormat);
        infoType.GetProperty("MultiSampleCount")!.SetValue(info, multiSampleCount);
        return info;
    }

    private static TrackingRenderTarget2D CreateTrackingRenderTarget()
    {
        return ReflectionHelpers.CreateUninitialized<TrackingRenderTarget2D>();
    }

    private sealed class TestRenderTargetManager : RenderTargetManager
    {
        private readonly Func<int, int, SurfaceFormat, DepthFormat, int, RenderTarget2D> _factory;

        public TestRenderTargetManager(Func<int, int, SurfaceFormat, DepthFormat, int, RenderTarget2D>? factory)
            : base(ReflectionHelpers.CreateUninitialized<GraphicsDevice>())
        {
            _factory = factory ?? ((_, _, _, _, _) => CreateTrackingRenderTarget());
        }

        public int CreateRenderTargetCallCount { get; private set; }

        protected override RenderTarget2D CreateRenderTarget(
            int width,
            int height,
            SurfaceFormat format,
            DepthFormat depthFormat,
            int multiSampleCount)
        {
            CreateRenderTargetCallCount++;
            return _factory(width, height, format, depthFormat, multiSampleCount);
        }

        protected override bool IsRenderTargetDisposed(RenderTarget2D renderTarget)
        {
            return renderTarget is TrackingRenderTarget2D tracking && tracking.SimulatedIsDisposed;
        }
    }

    private sealed class TrackingRenderTarget2D : RenderTarget2D
    {
        public TrackingRenderTarget2D() : base(null!, 1, 1)
        {
        }

        public bool WasDisposed { get; private set; }
        public bool SimulatedIsDisposed { get; set; }

        protected override void Dispose(bool disposing)
        {
            WasDisposed = true;
            SimulatedIsDisposed = true;
        }
    }
}

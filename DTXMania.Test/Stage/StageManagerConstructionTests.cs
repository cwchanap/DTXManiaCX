using System;
using System.Collections.Concurrent;
using DTXMania.Game;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Graphics;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Resources;
using DTXMania.Game.Lib.Stage;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using DTXMania.Test.TestData;
using Moq;

namespace DTXMania.Test.Stage
{
    [Trait("Category", "Unit")]
    public class StageManagerConstructionTests
    {
        [Theory]
        [InlineData(StageType.Config)]
        [InlineData(StageType.SongSelect)]
        [InlineData(StageType.SongTransition)]
        [InlineData(StageType.Performance)]
        [InlineData(StageType.Result)]
        [InlineData(StageType.DrumConfig)]
        public void GetOrCreateStage_ShouldConstructRealStage_ForEachNonStartupTitleType(StageType stageType)
        {
            // Without a pre-registered stage, StageManager.GetOrCreateStage must lazily construct
            // the concrete stage for every stage type. This exercises each construction branch.
            var game = new ConfiguredStageGame(new Mock<IResourceManager>().Object);
            var manager = new StageManager(game);

            var stage = (IStage)ReflectionHelpers.InvokePrivateMethod(manager, "GetOrCreateStage", stageType)!;

            Assert.NotNull(stage);
            Assert.Equal(stageType, stage.Type);
            manager.Dispose();
        }

        [Fact]
        public void GetOrCreateStage_WhenCriticalPathHostGetterThrows_ShouldSwallowAndStillConstructTitleStage()
        {
            // ResolveCriticalPathTrace wraps the host lookup in try/catch; a throwing host getter
            // degrades to a null trace so the Title stage still constructs.
            var game = new ThrowingHostStageGame(new Mock<IResourceManager>().Object);
            var manager = new StageManager(game);

            var stage = (IStage)ReflectionHelpers.InvokePrivateMethod(manager, "GetOrCreateStage", StageType.Title)!;

            Assert.NotNull(stage);
            Assert.Equal(StageType.Title, stage.Type);
            manager.Dispose();
        }

        private sealed class ConfiguredStageGame : IStageGame
        {
            public ConfiguredStageGame(IResourceManager resourceManager)
            {
                ResourceManager = resourceManager;
                ConfigManager = new ConfigManager();
            }

            public GraphicsDevice GraphicsDevice => null!;
            public IStageManager StageManager => null!;
            public IConfigManager ConfigManager { get; }
            public InputManagerCompat InputManager => null!;
            public IGraphicsManager GraphicsManager => null!;
            public IResourceManager ResourceManager { get; }
            public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;
            public bool CanPerformStageTransition() => false;
            public void MarkStageTransition() { }
            public Point? MapMouseToVirtual(Point windowPoint) => null;
            public ITextInputSource? GetTextInputSource() => null;
            public void RequestExit() { }
        }

        private sealed class ThrowingHostStageGame : IStageGame, IStartupCriticalPathHost
        {
            public ThrowingHostStageGame(IResourceManager resourceManager)
            {
                ResourceManager = resourceManager;
                ConfigManager = new ConfigManager();
            }

            StartupCriticalPathTrace? IStartupCriticalPathHost.StartupCriticalPathTrace =>
                throw new InvalidOperationException("trace resolution failed");

            public GraphicsDevice GraphicsDevice => null!;
            public IStageManager StageManager => null!;
            public IConfigManager ConfigManager { get; }
            public InputManagerCompat InputManager => null!;
            public IGraphicsManager GraphicsManager => null!;
            public IResourceManager ResourceManager { get; }
            public ILoggerFactory LoggerFactory => NullLoggerFactory.Instance;
            public bool CanPerformStageTransition() => false;
            public void MarkStageTransition() { }
            public Point? MapMouseToVirtual(Point windowPoint) => null;
            public ITextInputSource? GetTextInputSource() => null;
            public void RequestExit() { }
        }
    }
}

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

namespace DTXMania.Test.TestData
{
    /// <summary>
    /// Shared test fixtures for critical-path tracing tests, extracted from
    /// BaseStageTests, TitleStageLogicTests, and StartupStageLogicTests to
    /// eliminate duplication of identical helper types.
    /// </summary>
    internal static class CriticalPathTestFixtures
    {
        /// <summary>
        /// Creates a <see cref="StartupCriticalPathTrace"/> configured with a
        /// <see cref="TestFailureHook"/> that throws, exercising the defensive
        /// catch blocks in the Try* helpers without corrupting internal state.
        /// The hook fires before any timestamp is read, so the clock
        /// implementations' increment semantics are irrelevant.
        /// </summary>
        internal static StartupCriticalPathTrace CreateCorruptedTrace(
            IMonotonicClock clock,
            IUtcMicrosecondClock wallClock)
        {
            var trace = StartupCriticalPathTrace.Start(
                clock,
                wallClock,
                entryTimestamp: 0,
                entryUnixMicroseconds: 1_000_000,
                exitAfterPublication: false);
            trace.TestFailureHook = () =>
                throw new InvalidOperationException("test failure");
            return trace;
        }
    }

    /// <summary>
    /// UTC microsecond clock that always returns 2_000_000.
    /// </summary>
    internal sealed class FixedUtcMicrosecondClock : IUtcMicrosecondClock
    {
        public long GetUnixMicroseconds() => 2_000_000;
    }

    /// <summary>
    /// <see cref="IStageGame"/> + <see cref="IStartupCriticalPathHost"/> that
    /// exposes a given <see cref="StartupCriticalPathTrace"/> via the host
    /// interface. Used by stage tests that need to verify critical-path trace
    /// resolution without constructing a full game.
    /// </summary>
    internal sealed class CriticalPathHostStageGame
        : IStageGame, IStartupCriticalPathHost
    {
        private readonly StartupCriticalPathTrace _trace;

        public CriticalPathHostStageGame(
            StartupCriticalPathTrace trace,
            IResourceManager resourceManager)
        {
            _trace = trace;
            ResourceManager = resourceManager;
            ConfigManager = new ConfigManager();
        }

        StartupCriticalPathTrace? IStartupCriticalPathHost.StartupCriticalPathTrace =>
            _trace;

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

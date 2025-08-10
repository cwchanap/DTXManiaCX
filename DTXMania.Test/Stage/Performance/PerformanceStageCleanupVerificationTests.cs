using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Xunit;
using DTX.Stage;
using DTXMania.Game;
using DTXMania.Test.Helpers;
using DTX.Song;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Input;

namespace DTXMania.Test.Stage.Performance
{
    /// <summary>
    /// Verification tests for PerformanceStage state reset functionality.
    /// Uses debug output analysis to verify proper cleanup and state reset
    /// without requiring complex mocking.
    /// </summary>
    public class PerformanceStageCleanupVerificationTests
    {
        [Fact]
        public void PerformanceStage_LogsShowStateReset_AfterDeactivation()
        {
            // This test verifies that the CleanupComponents method properly logs
            // the state reset by checking for the expected log messages
            var capturedOutput = new StringWriter();
            var originalWriter = Debug.Listeners.Cast<DebugListener>().FirstOrDefault();

            try
            {
                // Capture debug output
                Debug.Listeners.Add(new TextWriterTraceListener(capturedOutput));

                // Create a test scenario that would typically cause state persistence issues
                using (var graphicsService = new TestGraphicsDeviceService())
                {
                    var game = new MockGameSimple(graphicsService.GraphicsDevice);
                    var performanceStage = new PerformanceStage(game);

                    // Test data
                    var sharedData = new Dictionary<string, object>
                    {
                        { "selectedSong", CreateMinimalSongListNode() },
                        { "selectedDifficulty", 0 },
                        { "songId", 1 },
                        { "parsedChart", CreateMinimalParsedChart() }
                    };

                    // First activation
                    performanceStage.Activate(sharedData);
                    
                    // Simulate some state changes by modifying private fields directly
                    SimulateGameplayStateChanges(performanceStage);
                    
                    // Deactivate to trigger cleanup - this should reset all state
                    performanceStage.Deactivate();
                    
                    // Verify cleanup logs show proper state reset
                    var debugOutput = capturedOutput.ToString();
                    
                    // Check that cleanup method was called and logged state transitions
                    Assert.Contains("CleanupComponents] START", debugOutput);
                    Assert.Contains("CleanupComponents] END", debugOutput);
                    
                    // Verify that final state shows proper reset values
                    Assert.Contains("_isLoading=True", debugOutput.Split("CleanupComponents] END")[1]);
                    Assert.Contains("_isReady=False", debugOutput.Split("CleanupComponents] END")[1]);
                    Assert.Contains("_stageCompleted=False", debugOutput.Split("CleanupComponents] END")[1]);
                    Assert.Contains("_inputPaused=False", debugOutput.Split("CleanupComponents] END")[1]);
                    Assert.Contains("_totalTime=0", debugOutput.Split("CleanupComponents] END")[1]);
                    Assert.Contains("_readyCountdown=1", debugOutput.Split("CleanupComponents] END")[1]);
                }
            }
            finally
            {
                // Restore original debug listener
                Debug.Listeners.Clear();
                if (originalWriter != null)
                {
                    Debug.Listeners.Add(originalWriter);
                }
            }
        }

        [Fact] 
        public void BackgroundRenderer_StateIsReset_AfterDispose()
        {
            // This test verifies that BackgroundRenderer resets its state variables
            // when disposed, which is important for proper reactivation
            using (var graphicsService = new TestGraphicsDeviceService())
            {
                var resourceManager = new MockResourceManager(graphicsService.GraphicsDevice);
                var backgroundRenderer = new DTX.Stage.Performance.BackgroundRenderer(resourceManager);
                
                // Simulate loading state changes by using reflection
                var isLoadingField = typeof(DTX.Stage.Performance.BackgroundRenderer)
                    .GetField("_isLoading", BindingFlags.NonPublic | BindingFlags.Instance);
                var loadingFailedField = typeof(DTX.Stage.Performance.BackgroundRenderer)
                    .GetField("_loadingFailed", BindingFlags.NonPublic | BindingFlags.Instance);
                
                // Set non-initial values
                isLoadingField?.SetValue(backgroundRenderer, true);
                loadingFailedField?.SetValue(backgroundRenderer, true);
                
                // Dispose should reset state
                backgroundRenderer.Dispose();
                
                // Verify state is reset to initial values
                var isLoadingAfterDispose = (bool)(isLoadingField?.GetValue(backgroundRenderer) ?? true);
                var loadingFailedAfterDispose = (bool)(loadingFailedField?.GetValue(backgroundRenderer) ?? true);
                
                Assert.False(isLoadingAfterDispose, "isLoading should be reset to false after dispose");
                Assert.False(loadingFailedAfterDispose, "loadingFailed should be reset to false after dispose");
            }
        }

        private SongListNode CreateMinimalSongListNode()
        {
            return new SongListNode();
        }

        private ParsedChart CreateMinimalParsedChart()
        {
            return new ParsedChart
            {
                Title = "Test Chart",
                Artist = "Test Artist",
                Bpm = 120.0,
                DurationMs = 60000,
                Notes = new List<Note>(),
                BGMEvents = new List<BGMEvent>(),
                BackgroundAudioPath = "test.wav"
            };
        }

        private void SimulateGameplayStateChanges(PerformanceStage stage)
        {
            // Use reflection to set internal state to simulate gameplay
            try
            {
                var stageType = typeof(PerformanceStage);
                var flags = BindingFlags.NonPublic | BindingFlags.Instance;
                
                stageType.GetField("_isLoading", flags)?.SetValue(stage, false);
                stageType.GetField("_isReady", flags)?.SetValue(stage, true);
                stageType.GetField("_stageCompleted", flags)?.SetValue(stage, true);
                stageType.GetField("_inputPaused", flags)?.SetValue(stage, true);
                stageType.GetField("_totalTime", flags)?.SetValue(stage, 45.5);
                stageType.GetField("_readyCountdown", flags)?.SetValue(stage, 0.2);
            }
            catch (Exception)
            {
                // If reflection fails, that's ok - the test can still verify
                // that cleanup logging works properly
            }
        }
    }

    /// <summary>
    /// Minimal mock game for testing without complex dependencies
    /// </summary>
    public class MockGameSimple : BaseGame
    {
        private readonly GraphicsDevice _graphicsDevice;

        public MockGameSimple(GraphicsDevice graphicsDevice) : base()
        {
            _graphicsDevice = graphicsDevice;
            InputManager = new MockInputManagerSimple();
        }

        public new GraphicsDevice GraphicsDevice => _graphicsDevice;

        protected override void LoadContent() { }
        protected override void Update(GameTime gameTime) { }
        protected override void Draw(GameTime gameTime) { }
    }

    /// <summary>
    /// Minimal mock input manager
    /// </summary>
    public class MockInputManagerSimple : InputManagerCompat
    {
        public override bool IsBackActionTriggered() => false;
        public override void Update(double deltaTime) { }
    }
}

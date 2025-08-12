using System;
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Config;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.Xna.Framework.Input;

namespace DTXMania.Test.Helpers
{
    /// <summary>
    /// Mock InputManager for testing that provides complete control over input simulation.
    /// Inherits from InputManager to maintain compatibility while allowing state injection.
    /// </summary>
    public class MockInputManager : InputManager
    {
        private readonly Dictionary<Keys, bool> _currentKeyStates = new();
        private readonly Dictionary<Keys, bool> _previousKeyStates = new();
        private readonly Dictionary<InputCommandType, bool> _commandStates = new();

        public event EventHandler<LaneHitEventArgs>? OnLaneHit;

        public MockInputManager() : base()
        {
        }

        /// <summary>
        /// Sets the current state of a key
        /// </summary>
        public void SetKeyState(Keys key, bool isPressed)
        {
            _previousKeyStates[key] = _currentKeyStates.TryGetValue(key, out var prevState) && prevState;
            _currentKeyStates[key] = isPressed;
        }

        /// <summary>
        /// Simulates a key press (transition from not pressed to pressed)
        /// </summary>
        public void SetKeyPressed(Keys key, bool isPressed)
        {
            if (isPressed)
            {
                _previousKeyStates[key] = false; // Ensure it was not pressed before
                _currentKeyStates[key] = true;   // Now it is pressed
            }
            else
            {
                _previousKeyStates[key] = _currentKeyStates.TryGetValue(key, out var prevState) && prevState;
                _currentKeyStates[key] = false;
            }
        }

        /// <summary>
        /// Updates key states to simulate frame-to-frame progression
        /// </summary>
        public void UpdateKeyStates()
        {
            // Move current states to previous states
            _previousKeyStates.Clear();
            foreach (var kvp in _currentKeyStates)
            {
                _previousKeyStates[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Simulates a complete key press cycle (press -> hold -> release)
        /// </summary>
        public void SimulateKeyPressSequence(Keys key)
        {
            // Frame 1: Key press
            SetKeyPressed(key, true);
            
            // Frame 2: Key held (previous = true, current = true)
            UpdateKeyStates();
            
            // Frame 3: Key release
            SetKeyPressed(key, false);
        }

        /// <summary>
        /// Override to simulate back action using consolidated method
        /// </summary>
        public new bool IsBackActionTriggered()
        {
            return IsCommandPressed(InputCommandType.Back) || IsKeyPressed((int)Keys.Escape);
        }

        /// <summary>
        /// Sets the current state of a command
        /// </summary>
        public void SetCommandPressed(InputCommandType command, bool isPressed)
        {
            _commandStates[command] = isPressed;
        }

        /// <summary>
        /// Checks if a command was pressed
        /// </summary>
        public new bool IsCommandPressed(InputCommandType command)
        {
            return _commandStates.TryGetValue(command, out var isPressed) && isPressed;
        }

        /// <summary>
        /// Override to use injected state instead of actual keyboard
        /// </summary>
        public new bool IsKeyPressed(int keyCode)
        {
            var key = (Keys)keyCode;
            var currentPressed = _currentKeyStates.TryGetValue(key, out var isPressed) && isPressed;
            var prevPressed = _previousKeyStates.TryGetValue(key, out var wasPrevPressed) && wasPrevPressed;
            return currentPressed && !prevPressed;
        }

        /// <summary>
        /// Override to use injected state instead of actual keyboard
        /// </summary>
        public new bool IsKeyDown(int keyCode)
        {
            var key = (Keys)keyCode;
            return _currentKeyStates.TryGetValue(key, out var isPressed) && isPressed;
        }

        /// <summary>
        /// Override to use injected state instead of actual keyboard
        /// </summary>
        public new bool IsKeyReleased(int keyCode)
        {
            var key = (Keys)keyCode;
            var currentPressed = _currentKeyStates.TryGetValue(key, out var isPressed) && isPressed;
            var prevPressed = _previousKeyStates.TryGetValue(key, out var wasPrevPressed) && wasPrevPressed;
            return !currentPressed && prevPressed;
        }
    }

    /// <summary>
    /// Mock InputManagerCompat for testing that provides complete control over input simulation.
    /// Inherits from InputManagerCompat while allowing state injection for testing purposes.
    /// </summary>
    public class MockInputManagerCompat : InputManagerCompat
    {
        private readonly Dictionary<Keys, bool> _currentKeyStates = new();
        private readonly Dictionary<Keys, bool> _previousKeyStates = new();

        public event EventHandler<LaneHitEventArgs>? OnLaneHit;
        
        public MockInputManagerCompat() : base(new ConfigManager())
        {
        }

        /// <summary>
        /// Sets the current state of a key
        /// </summary>
        public void SetKeyState(Keys key, bool isPressed)
        {
            _previousKeyStates[key] = _currentKeyStates.TryGetValue(key, out var prevState) && prevState;
            _currentKeyStates[key] = isPressed;
        }

        /// <summary>
        /// Simulates a key press (transition from not pressed to pressed)
        /// </summary>
        public void SetKeyPressed(Keys key, bool isPressed)
        {
            if (isPressed)
            {
                _previousKeyStates[key] = false;
                _currentKeyStates[key] = true;
            }
            else
            {
                _previousKeyStates[key] = _currentKeyStates.TryGetValue(key, out var prevState) && prevState;
                _currentKeyStates[key] = false;
            }
        }

        /// <summary>
        /// Updates key states to simulate frame-to-frame progression
        /// </summary>
        public void UpdateKeyStates()
        {
            _previousKeyStates.Clear();
            foreach (var kvp in _currentKeyStates)
            {
                _previousKeyStates[kvp.Key] = kvp.Value;
            }
        }

        /// <summary>
        /// Simulates triggering a lane hit event for testing by using the key mapping
        /// </summary>
        public void TriggerLaneHitFromKey(Keys key)
        {
            // Get the lane for this key using the ModularInputManager's KeyBindings
            int lane = ModularInputManager.KeyBindings.GetLane(key.ToString());
            if (lane >= 0)
            {
                var buttonState = new DTXMania.Game.Lib.Input.ButtonState($"Key.{key}", true, 1.0f);
                var hitArgs = new LaneHitEventArgs(lane, buttonState);
                // Fire the event through the real ModularInputManager
                ModularInputManager.GetType()
                    .GetMethod("OnInputRouterLaneHit", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)?
                    .Invoke(ModularInputManager, new object[] { this, hitArgs });
            }
        }

        /// <summary>
        /// Simulates triggering a lane hit event for testing
        /// </summary>
        public void TriggerLaneHit(int lane)
        {
            var buttonState = new DTXMania.Game.Lib.Input.ButtonState($"MockButton{lane}", true, 1.0f);
            var hitArgs = new LaneHitEventArgs(lane, buttonState);
            
            // Use reflection to trigger the OnLaneHit event on ModularInputManager
            var eventField = ModularInputManager.GetType().GetField("OnLaneHit");
            var eventDelegate = eventField?.GetValue(ModularInputManager) as EventHandler<LaneHitEventArgs>;
            eventDelegate?.Invoke(this, hitArgs);
        }

        public override bool IsKeyDown(int keyCode)
        {
            var key = (Keys)keyCode;
            return _currentKeyStates.TryGetValue(key, out var isPressed) && isPressed;
        }

        public override bool IsKeyPressed(int keyCode)
        {
            var key = (Keys)keyCode;
            var currentPressed = _currentKeyStates.TryGetValue(key, out var isPressed) && isPressed;
            var prevPressed = _previousKeyStates.TryGetValue(key, out var wasPrevPressed) && wasPrevPressed;
            return currentPressed && !prevPressed;
        }

        public override bool IsKeyReleased(int keyCode)
        {
            var key = (Keys)keyCode;
            var currentPressed = _currentKeyStates.TryGetValue(key, out var isPressed) && isPressed;
            var prevPressed = _previousKeyStates.TryGetValue(key, out var wasPrevPressed) && wasPrevPressed;
            return !currentPressed && prevPressed;
        }
    }

    /// <summary>
    /// Mock ChartManager that creates synthetic charts with configurable notes for testing.
    /// Provides complete control over note timing, lanes, and chart structure.
    /// </summary>
    public class MockChartManager : ChartManager
    {
        private readonly List<Note> _syntheticNotes;
        private readonly ParsedChart _mockChart;

        public MockChartManager() : this(CreateDefaultSyntheticChart())
        {
        }

        public MockChartManager(ParsedChart chart) : base(chart)
        {
            _mockChart = chart;
            _syntheticNotes = new List<Note>();
        }

        /// <summary>
        /// Creates a default synthetic chart for basic testing
        /// </summary>
        public static ParsedChart CreateDefaultSyntheticChart()
        {
            var chart = new ParsedChart("synthetic-test.dtx")
            {
                Bpm = 120.0 // Standard BPM for predictable timing
            };

            // Add a basic pattern of notes across multiple lanes
            // At 120 BPM: 96 ticks = 500ms, so each tick ≈ 5.208ms
            var notes = new List<(int lane, int tick, double timeMs)>
            {
                (0, 96, 1000.0),   // Lane 0 (A key) at 1000ms
                (1, 144, 1500.0),  // Lane 1 (S key) at 1500ms  
                (2, 192, 2000.0),  // Lane 2 (D key) at 2000ms
                (3, 240, 2500.0),  // Lane 3 (F key) at 2500ms
                (4, 288, 3000.0),  // Lane 4 (Space) at 3000ms
                (5, 336, 3500.0),  // Lane 5 (J key) at 3500ms
                (6, 384, 4000.0),  // Lane 6 (K key) at 4000ms
                (7, 432, 4500.0),  // Lane 7 (L key) at 4500ms
                (8, 480, 5000.0),  // Lane 8 (; key) at 5000ms
            };

            for (int i = 0; i < notes.Count; i++)
            {
                var (lane, tick, timeMs) = notes[i];
                var channel = 0x11 + lane; // DTX channels 0x11-0x19
                chart.AddNote(new Note(i, 0, tick, channel, "01"));
            }

            chart.FinalizeChart();
            return chart;
        }

        /// <summary>
        /// Creates a chart with notes designed for testing specific scenarios
        /// </summary>
        public static MockChartManager CreateScenarioChart(TestScenario scenario)
        {
            var chart = new ParsedChart($"scenario-{scenario}.dtx") { Bpm = 120.0 };

            switch (scenario)
            {
                case TestScenario.HitWindowTesting:
                    // Single note for precise timing tests
                    chart.AddNote(new Note(0, 0, 96, 0x11, "01")); // 1000ms
                    break;

                case TestScenario.ComboBuilding:
                    // Sequence of notes for combo testing
                    for (int i = 0; i < 10; i++)
                    {
                        chart.AddNote(new Note(i, 0, 96 + i * 10, 0x11, "01")); // Every ~52ms
                    }
                    break;

                case TestScenario.ScoreAccumulation:
                    // Many notes for score testing
                    for (int i = 0; i < 100; i++)
                    {
                        var lane = i % 9;
                        var channel = 0x11 + lane;
                        chart.AddNote(new Note(i, 0, 96 + i * 5, channel, "01")); // Every ~26ms
                    }
                    break;

                case TestScenario.LifeGaugeStress:
                    // Pattern designed to test gauge management
                    for (int i = 0; i < 50; i++)
                    {
                        chart.AddNote(new Note(i, 0, 96 + i * 20, 0x11, "01")); // Every ~104ms
                    }
                    break;

                case TestScenario.MultiLaneTiming:
                    // Notes across all lanes at the same time
                    for (int lane = 0; lane < 9; lane++)
                    {
                        var channel = 0x11 + lane;
                        chart.AddNote(new Note(lane, 0, 96, channel, "01")); // All at 1000ms
                    }
                    break;

                case TestScenario.TimingWindowEdges:
                    // Notes specifically for boundary testing
                    var timings = new[] { 96, 97, 98, 99, 100, 101, 102, 103, 104 }; // Around 1000ms
                    for (int i = 0; i < timings.Length; i++)
                    {
                        chart.AddNote(new Note(i, 0, timings[i], 0x11, "01"));
                    }
                    break;
            }

            chart.FinalizeChart();
            return new MockChartManager(chart);
        }

        /// <summary>
        /// Adds a synthetic note at a specific time and lane for testing
        /// </summary>
        public void AddSyntheticNote(int lane, double timeMs, int noteId = -1)
        {
            if (noteId == -1)
                noteId = _syntheticNotes.Count;

            // Convert time to ticks (approximate)
            // At 120 BPM: 96 ticks = 500ms
            int tick = (int)((timeMs / 500.0) * 96);
            var channel = 0x11 + lane;

            var note = new Note(noteId, 0, tick, channel, "01");
            _syntheticNotes.Add(note);

            // Add to the actual chart (this is tricky with the existing implementation)
            // For now, we'll track them separately and override the AllNotes property if needed
        }

        /// <summary>
        /// Creates a simple chart with a single note for focused testing
        /// </summary>
        public static MockChartManager CreateSingleNoteChart(int lane = 0, double timeMs = 1000.0)
        {
            var chart = new ParsedChart("single-note.dtx") { Bpm = 120.0 };
            
            int tick = (int)((timeMs / 500.0) * 96);
            var channel = 0x11 + lane;
            chart.AddNote(new Note(0, 0, tick, channel, "01"));
            
            chart.FinalizeChart();
            return new MockChartManager(chart);
        }

        /// <summary>
        /// Creates a chart with multiple notes in the same lane for timing tests
        /// </summary>
        public static MockChartManager CreateMultipleNotesChart(int lane = 0, params double[] timesMs)
        {
            var chart = new ParsedChart("multiple-notes.dtx") { Bpm = 120.0 };
            
            for (int i = 0; i < timesMs.Length; i++)
            {
                int tick = (int)((timesMs[i] / 500.0) * 96);
                var channel = 0x11 + lane;
                chart.AddNote(new Note(i, 0, tick, channel, "01"));
            }
            
            chart.FinalizeChart();
            return new MockChartManager(chart);
        }

        /// <summary>
        /// Simulates input events for testing manager interactions
        /// </summary>
        public class MockInputEvent
        {
            public Keys Key { get; set; }
            public double TimeMs { get; set; }
            public InputEventType Type { get; set; }
        }

        public enum InputEventType
        {
            Press,
            Hold,
            Release
        }

        /// <summary>
        /// Creates a sequence of mock input events for testing
        /// </summary>
        public static List<MockInputEvent> CreateInputSequence(params (Keys key, double timeMs, InputEventType type)[] events)
        {
            return events.Select(e => new MockInputEvent
            {
                Key = e.key,
                TimeMs = e.timeMs,
                Type = e.type
            }).ToList();
        }
    }

    /// <summary>
    /// Test scenarios for creating specific chart configurations
    /// </summary>
    public enum TestScenario
    {
        HitWindowTesting,
        ComboBuilding,
        ScoreAccumulation,
        LifeGaugeStress,
        MultiLaneTiming,
        TimingWindowEdges
    }

    /// <summary>
    /// Utility class for creating common test data patterns
    /// </summary>
    public static class TestDataPatterns
    {
        /// <summary>
        /// Creates a perfect play sequence (all Just hits)
        /// </summary>
        public static List<JudgementEvent> CreatePerfectPlay(int noteCount)
        {
            var events = new List<JudgementEvent>();
            for (int i = 0; i < noteCount; i++)
            {
                events.Add(new JudgementEvent(i, i % 9, 0.0, JudgementType.Just));
            }
            return events;
        }

        /// <summary>
        /// Creates a mixed play sequence with various judgement types
        /// </summary>
        public static List<JudgementEvent> CreateMixedPlay(int noteCount)
        {
            var events = new List<JudgementEvent>();
            var judgements = new[] { JudgementType.Just, JudgementType.Great, JudgementType.Good, JudgementType.Poor, JudgementType.Miss };
            
            for (int i = 0; i < noteCount; i++)
            {
                var judgement = judgements[i % judgements.Length];
                var delta = judgement switch
                {
                    JudgementType.Just => 0.0,
                    JudgementType.Great => 30.0,
                    JudgementType.Good => 70.0,
                    JudgementType.Poor => 120.0,
                    JudgementType.Miss => 200.0,
                    _ => 0.0
                };
                
                events.Add(new JudgementEvent(i, i % 9, delta, judgement));
            }
            return events;
        }

        /// <summary>
        /// Creates a failing play sequence (mostly Poor and Miss)
        /// </summary>
        public static List<JudgementEvent> CreateFailingPlay(int noteCount)
        {
            var events = new List<JudgementEvent>();
            var badJudgements = new[] { JudgementType.Poor, JudgementType.Miss };
            
            for (int i = 0; i < noteCount; i++)
            {
                var judgement = badJudgements[i % badJudgements.Length];
                var delta = judgement == JudgementType.Poor ? 120.0 : 200.0;
                events.Add(new JudgementEvent(i, i % 9, delta, judgement));
            }
            return events;
        }

        /// <summary>
        /// Creates timing boundary test data
        /// </summary>
        public static List<(double deltaMs, JudgementType expectedJudgement)> CreateTimingBoundaryTests()
        {
            return new List<(double, JudgementType)>
            {
                (0.0, JudgementType.Just),      // Perfect
                (25.0, JudgementType.Just),     // Just boundary
                (25.1, JudgementType.Great),    // Just over Just
                (50.0, JudgementType.Great),    // Great boundary
                (50.1, JudgementType.Good),     // Just over Great
                (100.0, JudgementType.Good),    // Good boundary
                (100.1, JudgementType.Poor),    // Just over Good
                (150.0, JudgementType.Poor),    // Poor boundary
                (150.1, JudgementType.Miss),    // Just over Poor
                (200.0, JudgementType.Miss),    // Miss threshold
                (-25.0, JudgementType.Just),    // Early Just
                (-50.0, JudgementType.Great),   // Early Great
                (-100.0, JudgementType.Good),   // Early Good
                (-150.0, JudgementType.Poor),   // Early Poor
                (-200.0, JudgementType.Miss),   // Early Miss
            };
        }
    }
}

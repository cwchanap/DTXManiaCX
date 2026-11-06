using System;
using System.Collections.Generic;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.DrumConfig;
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework.Input;
using Xunit;
using ButtonState = DTXMania.Game.Lib.Input.ButtonState;

namespace DTXMania.Test.Stage.DrumConfig
{
    [Trait("Category", "Unit")]
    public class DrumCaptureSessionTests
    {
        private static readonly Dictionary<Keys, InputCommandType> SystemMap = new()
        {
            [Keys.Enter] = InputCommandType.Activate,
            [Keys.Escape] = InputCommandType.Back,
            [Keys.Up] = InputCommandType.MoveUp,
            [Keys.PageUp] = InputCommandType.IncreaseScrollSpeed,
        };

        private static DrumCaptureSession Listening(int lane = 3)
        {
            var session = new DrumCaptureSession(() => SystemMap);
            session.Start(lane);
            return session;
        }

        [Fact]
        public void Constructor_WithNullProvider_Throws()
        {
            Assert.Throws<ArgumentNullException>(() => new DrumCaptureSession(null!));
        }

        [Fact]
        public void StartAndStop_TrackListeningAndLane()
        {
            var session = Listening(7);
            Assert.True(session.IsListening);
            Assert.Equal(7, session.Lane);

            session.Stop();

            Assert.False(session.IsListening);
            Assert.Equal(-1, session.Lane);
        }

        [Theory]
        [InlineData("Key.Q")]
        [InlineData("Key.PageUp")] // non-required system key: the stage evicts it on capture
        [InlineData("MIDI.38")]
        [InlineData("Pad.A")]
        public void TryCapture_UnreservedInput_IsCapturedAndKeepsListening(string id)
        {
            var session = Listening();

            Assert.Equal(DrumCaptureOutcome.Captured, session.TryCapture(new ButtonState(id, true)));
            Assert.True(session.IsListening);
        }

        [Theory]
        [InlineData("Key.Enter", "Activate")]
        [InlineData("Key.Escape", "Back")]
        [InlineData("Key.Up", "MoveUp")]
        public void TryCapture_RequiredSystemKey_IsRejectedWithMessage(string id, string command)
        {
            var session = Listening();

            Assert.Equal(DrumCaptureOutcome.Rejected, session.TryCapture(new ButtonState(id, true)));
            Assert.Contains(command, session.ConflictMessage);
            Assert.True(session.IsListening);
        }

        [Fact]
        public void TryCapture_WhenNotListeningOrBlank_IsIgnored()
        {
            var idle = new DrumCaptureSession(() => SystemMap);
            Assert.Equal(DrumCaptureOutcome.Ignored, idle.TryCapture(new ButtonState("Key.Q", true)));

            var session = Listening();
            Assert.Equal(DrumCaptureOutcome.Ignored, session.TryCapture(null));
            Assert.Equal(DrumCaptureOutcome.Ignored, session.TryCapture(new ButtonState(" ", true)));
        }

        [Fact]
        public void ConflictMessage_ExpiresAfterNoticeTime()
        {
            var session = Listening();
            session.TryCapture(new ButtonState("Key.Enter", true));

            session.Tick(DrumConfigUILayout.NoticeSeconds - 0.1);
            Assert.NotNull(session.ConflictMessage);

            session.Tick(0.2);
            Assert.Null(session.ConflictMessage);
        }

        [Fact]
        public void ConflictMessage_ClearsOnTheNextCapture()
        {
            var session = Listening();
            session.TryCapture(new ButtonState("Key.Enter", true));

            Assert.Equal(DrumCaptureOutcome.Captured, session.TryCapture(new ButtonState("MIDI.42", true)));
            Assert.Null(session.ConflictMessage);
        }
    }
}

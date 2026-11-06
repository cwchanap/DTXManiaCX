#nullable enable

using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework.Input;
using DTXMania.Game.Lib.Input;
using DTXMania.Game.Lib.Stage.KeyAssign;
using DTXMania.Game.Lib.UI.Layout;

namespace DTXMania.Game.Lib.Stage.DrumConfig
{
    public enum DrumCaptureOutcome { Ignored, Captured, Rejected }

    /// <summary>
    /// Inline capture for one drum lane: while listening, decides whether the next input can be
    /// bound to <see cref="Lane"/>. Intent-only: it owns no bindings and mutates nothing. A
    /// Captured outcome is applied by the stage (ConfigManager.SetKeyBindings plus the permanent
    /// system-key eviction, Decision 3). Required system keys (navigation, Enter, Esc) are
    /// Rejected and leave a <see cref="ConflictMessage"/> up for a short while; listening goes on.
    /// </summary>
    public sealed class DrumCaptureSession
    {
        private readonly Func<IReadOnlyDictionary<Keys, InputCommandType>> _systemMappingProvider;
        private double _conflictTimer;

        public DrumCaptureSession(Func<IReadOnlyDictionary<Keys, InputCommandType>> systemMappingProvider)
        {
            _systemMappingProvider = systemMappingProvider ?? throw new ArgumentNullException(nameof(systemMappingProvider));
        }

        public bool IsListening { get; private set; }
        public int Lane { get; private set; } = -1;
        public string? ConflictMessage { get; private set; }

        public void Start(int lane)
        {
            IsListening = true;
            Lane = lane;
            ClearConflict();
        }

        public void Stop()
        {
            IsListening = false;
            Lane = -1;
            ClearConflict();
        }

        /// <summary>Counts down the reserved-key message.</summary>
        public void Tick(double deltaTime)
        {
            if (ConflictMessage == null)
                return;
            _conflictTimer -= deltaTime;
            if (_conflictTimer <= 0)
                ClearConflict();
        }

        public DrumCaptureOutcome TryCapture(DTXMania.Game.Lib.Input.ButtonState? button)
        {
            if (!IsListening || button == null || string.IsNullOrWhiteSpace(button.Id))
                return DrumCaptureOutcome.Ignored;

            if (KeyBindings.IsKeyboardButtonId(button.Id)
                && Enum.TryParse(button.Id.Substring(4), out Keys key))
            {
                var required = KeyConflictChecker.GetRequiredSystemConflict(_systemMappingProvider(), key);
                if (required != null)
                {
                    ConflictMessage = $"{KeyBindings.FormatButtonId(button.Id)} is reserved for {required}. Pick another key.";
                    _conflictTimer = DrumConfigUILayout.NoticeSeconds;
                    return DrumCaptureOutcome.Rejected;
                }
            }

            ClearConflict();
            return DrumCaptureOutcome.Captured;
        }

        private void ClearConflict()
        {
            ConflictMessage = null;
            _conflictTimer = 0;
        }
    }
}

#nullable enable

using DTXMania.Game.Lib.Input;
using LaneType = DTXMania.Game.Lib.UI.Layout.PerformanceUILayout.LaneType;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Default drum-pad system keys (menu navigation), following DTXManiaNX.
    /// A hit on a real pad (MIDI or gamepad, never a keyboard drum key) becomes the same
    /// command as the matching keyboard system key. Gameplay and the drum-binding screen
    /// ignore pads, so there a hit stays a hit and system keys are keyboard only.
    /// Song select adds its own pad actions in <see cref="SongSelectionStage"/>, where "x2"
    /// means two hits within 500 ms: bass drum x2 opens or closes Quick Settings, hi-hat x2
    /// steps the difficulty, floor tom switches tabs and floor tom x2 opens Search &amp; Filter.
    /// </summary>
    public static class DrumPadSystemKeys
    {
        public static InputCommandType? GetCommand(StageType? stage, int lane)
        {
            if (stage is null or StageType.Performance or StageType.DrumConfig)
                return null;

            return (LaneType)lane switch
            {
                LaneType.HT => InputCommandType.MoveUp,
                LaneType.LT => InputCommandType.MoveDown,
                LaneType.CY or LaneType.RD => InputCommandType.Activate,
                // Back on Title quits the game, so a stray cymbal hit must not reach it (NX ignores LC there too).
                LaneType.LC when stage != StageType.Title => InputCommandType.Back,
                _ => null
            };
        }
    }
}

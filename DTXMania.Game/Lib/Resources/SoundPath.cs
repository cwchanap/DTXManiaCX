namespace DTXMania.Game.Lib.Resources
{
    /// <summary>
    /// Centralized constants for all system sound file paths used in DTXManiaCX,
    /// mirroring <see cref="TexturePath"/> for audio assets. These names double as
    /// the CX Neon sound-pack inventory (see tools/sfxgen/).
    /// </summary>
    public static class SoundPath
    {
        public const string CursorMove = "Sounds/Move.ogg";
        public const string Decide = "Sounds/Decide.ogg";
        public const string GameStart = "Sounds/Game start.ogg";
        public const string NowLoading = "Sounds/Now loading.ogg";
        public const string StageClear = "Sounds/Stage Clear.ogg";
        public const string FullCombo = "Sounds/Full Combo.ogg";
        public const string Excellent = "Sounds/Excellent.ogg";
        public const string NewRecord = "Sounds/New Record.ogg";

        /// <summary>All system sound paths; the CX Neon pack must ship each one.</summary>
        public static string[] GetAllSoundPaths()
        {
            return new[]
            {
                CursorMove, Decide, GameStart, NowLoading,
                StageClear, FullCombo, Excellent, NewRecord
            };
        }
    }
}

namespace DTXMania.Game.Lib.Song
{
    /// <summary>
    /// Identifies one speed-scoped score slot within a song-list node.
    /// </summary>
    public readonly record struct ScoreVariantKey(
        int DifficultyIndex,
        int PlaySpeedPercent);
}
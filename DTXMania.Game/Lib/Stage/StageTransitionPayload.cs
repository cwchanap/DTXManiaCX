#nullable enable
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Stage.Performance;

namespace DTXMania.Game.Lib.Stage
{
    /// <summary>
    /// Typed payload carried across a stage transition.
    /// Replaces the former Dictionary&lt;string, object&gt; shared-data hand-off.
    /// </summary>
    public abstract record StageTransitionPayload;

    /// <summary>
    /// Request from SongSelection (via SongTransition) to play a song at a difficulty.
    /// Carries the optional parsed chart prepared during the transition.
    /// </summary>
    public sealed record SongPlayRequest(
        SongListNode Song,
        int Difficulty,
        ParsedChart? Chart = null) : StageTransitionPayload;

    /// <summary>
    /// Outcome of a performance run handed to the Result stage.
    /// </summary>
    public sealed record PerformanceResult(
        PerformanceSummary Summary,
        SongListNode? Song,
        int Difficulty) : StageTransitionPayload;
}

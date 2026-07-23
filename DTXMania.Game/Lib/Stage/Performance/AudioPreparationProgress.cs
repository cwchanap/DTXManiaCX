using System;

namespace DTXMania.Game.Lib.Stage.Performance
{
    public sealed record AudioPreparationProgress(
        int CompletedCount,
        int TotalCount,
        string CurrentRole,
        int CacheHitCount,
        TimeSpan Elapsed,
        long DecodedByteEstimate);
}
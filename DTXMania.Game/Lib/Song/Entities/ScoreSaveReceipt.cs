#nullable enable

using System;
using System.ComponentModel.DataAnnotations;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// Durable idempotency receipt for one completed gameplay run.
    /// Chart identity is stored directly and deliberately has no chart navigation.
    /// </summary>
    public sealed class ScoreSaveReceipt
    {
        [Key]
        public Guid RunId { get; set; }

        public int ChartId { get; set; }

        public EInstrumentPart Instrument { get; set; }

        public int PlaySpeedPercent { get; set; } = 100;

        public int? SongScoreId { get; set; }

        public SongScore? SongScore { get; set; }

        public DateTime SavedAtUtc { get; set; }
    }
}
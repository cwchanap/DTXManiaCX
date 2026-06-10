#nullable enable
namespace DTXMania.Game.Lib.Song
{
    /// <summary>Aggregate outcome of a bulk NX score import.</summary>
    public sealed class NxImportResult
    {
        public int Scanned { get; set; }
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }

        /// <summary>True when the import could not run because no database service was available.</summary>
        public bool DbUnavailable { get; set; }
    }

    /// <summary>Progress snapshot reported during a bulk NX score import.</summary>
    public sealed class NxImportProgress
    {
        public int Scanned { get; set; }
        public int Imported { get; set; }
        public int Skipped { get; set; }
        public int Errors { get; set; }
        public string CurrentFile { get; set; } = "";
    }
}

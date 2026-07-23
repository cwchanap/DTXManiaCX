#nullable enable

namespace DTXMania.Game.Lib.Song.Entities
{
    public enum ScoreSaveStatus
    {
        Saved,
        AlreadySaved,
        Failed
    }

    /// <summary>
    /// Observable outcome of one atomic gameplay-score save attempt.
    /// </summary>
    public sealed class ScoreSaveResult
    {
        private ScoreSaveResult(
            ScoreSaveStatus status,
            int? songScoreId,
            string? errorMessage)
        {
            Status = status;
            SongScoreId = songScoreId;
            ErrorMessage = errorMessage;
        }

        public ScoreSaveStatus Status { get; }

        public int? SongScoreId { get; }

        public string? ErrorMessage { get; }

        public bool IsSuccess =>
            Status == ScoreSaveStatus.Saved
            || Status == ScoreSaveStatus.AlreadySaved;

        public static ScoreSaveResult Saved(int songScoreId) =>
            new(ScoreSaveStatus.Saved, songScoreId, null);

        public static ScoreSaveResult AlreadySaved(int? songScoreId) =>
            new(ScoreSaveStatus.AlreadySaved, songScoreId, null);

        public static ScoreSaveResult Failed(string errorMessage) =>
            new(ScoreSaveStatus.Failed, null, errorMessage);
    }
}
namespace DTXMania.Game.Lib.Song.Filtering
{
    public readonly record struct FilteredSongResult(
        SongListNode Node,
        string FolderPath);
}

using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Entities;

namespace DTXMania.Test.TestData
{
    /// <summary>
    /// Reflection access to the SongManager's private database-service field.
    /// Replaces the removed test-only SongManager.DatabaseService accessor
    /// (HPA-801 Decision 0) for test classes that cannot carry an instance
    /// helper property.
    /// </summary>
    internal static class SongManagerTestDb
    {
        public static SongDatabaseService? Of(SongManager manager) =>
            ReflectionHelpers.GetPrivateField<SongDatabaseService?>(manager, "_databaseService");
    }
}

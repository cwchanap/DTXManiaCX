using System.IO;
using Microsoft.Data.Sqlite;

namespace DTXMania.Test.Config
{
    /// <summary>
    /// Deterministic store-save failure for config tests: replaces the config
    /// root directory with a blocker file so <c>SqliteConfigStore.Save</c>'s
    /// directory creation throws. <see cref="Repair"/> restores write access
    /// (clearing pooled SQLite handles that still point at the deleted inode
    /// first), and <see cref="Dispose"/> repairs idempotently so cleanup runs
    /// even when an assertion or the save under test fails.
    /// </summary>
    internal sealed class ConfigStoreFailureScope : IDisposable
    {
        private readonly string _root;
        private bool _blocked;

        public ConfigStoreFailureScope(string root)
        {
            _root = root;
            Directory.Delete(root, recursive: true);
            File.WriteAllText(root, "blocker");
            _blocked = true;
        }

        /// <summary>Remove the blocker so saves against the same path succeed again.</summary>
        public void Repair()
        {
            if (!_blocked)
                return;
            SqliteConnection.ClearAllPools();
            File.Delete(_root);
            Directory.CreateDirectory(_root);
            _blocked = false;
        }

        public void Dispose() => Repair();
    }
}

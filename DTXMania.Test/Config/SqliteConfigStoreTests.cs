using System;
using System.Collections.Generic;
using System.IO;
using DTXMania.Game.Lib.Config;
using Microsoft.Data.Sqlite;
using Xunit;

namespace DTXMania.Test.Config
{
    /// <summary>
    /// Real-SQLite tests for the v1 configuration store. Each test owns a
    /// unique temp directory; <see cref="SqliteConnection.ClearAllPools"/>
    /// runs before cleanup so pooled native handles do not keep files open
    /// (required for recursive directory deletion on Windows CI).
    /// </summary>
    [Trait("Category", "Unit")]
    public sealed class SqliteConfigStoreTests : IDisposable
    {
        private readonly string _root;

        public SqliteConfigStoreTests()
        {
            _root = Path.Combine(
                Path.GetTempPath(),
                "dtx-sqlite-config-store-tests",
                Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_root);
        }

        public void Dispose()
        {
            SqliteConnection.ClearAllPools();
            if (Directory.Exists(_root))
            {
                Directory.Delete(_root, recursive: true);
            }
        }

        private string DbPath => Path.Combine(_root, "config.db");

        [Fact]
        public void SaveAndLoad_OnNewDatabase_ShouldRoundTripAllEntries()
        {
            var store = new SqliteConfigStore(DbPath);
            var entries = new Dictionary<string, string>
            {
                ["ScrollSpeed"] = "250",
                ["SongRoot.0"] = "/songs",
                ["Key.LC"] = "26,27",
                ["MidiVelocity.Min"] = "30",
            };

            Assert.False(store.Exists);

            store.Save(entries);

            Assert.True(store.Exists);
            Assert.True(File.Exists(DbPath));

            var loaded = store.Load();

            Assert.Equal(entries.Count, loaded.Count);
            foreach (var pair in entries)
            {
                Assert.True(loaded.ContainsKey(pair.Key), $"Expected key '{pair.Key}' to be present");
                Assert.Equal(pair.Value, loaded[pair.Key]);
            }
        }

        [Fact]
        public void Save_OnNewDatabase_ShouldStampUserVersionOne()
        {
            var store = new SqliteConfigStore(DbPath);

            store.Save(new Dictionary<string, string> { ["ScrollSpeed"] = "250" });

            Assert.Equal(1L, ReadUserVersion(DbPath));
        }

        [Fact]
        public void Save_AfterPreviousSave_ShouldReplaceEntireSnapshotAndRemoveStaleRows()
        {
            var store = new SqliteConfigStore(DbPath);

            store.Save(new Dictionary<string, string>
            {
                ["Alpha"] = "1",
                ["Beta"] = "2",
                ["Gamma"] = "3",
            });
            store.Save(new Dictionary<string, string>
            {
                ["Alpha"] = "9",
                ["Delta"] = "4",
            });

            var loaded = store.Load();

            Assert.Equal(2, loaded.Count);
            Assert.Equal("9", loaded["Alpha"]);
            Assert.Equal("4", loaded["Delta"]);
            Assert.False(loaded.ContainsKey("Beta"), "Stale key 'Beta' must be removed by snapshot replacement");
            Assert.False(loaded.ContainsKey("Gamma"), "Stale key 'Gamma' must be removed by snapshot replacement");
        }

        [Fact]
        public void Load_WhenUserVersionIsUnsupported_ShouldFail()
        {
            CreateRawDatabase(
                "CREATE TABLE ConfigEntries (Key TEXT PRIMARY KEY NOT NULL, Value TEXT NOT NULL)",
                "PRAGMA user_version = 2");

            var store = new SqliteConfigStore(DbPath);

            var exception = Record.Exception(() => store.Load());

            Assert.NotNull(exception);
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("2", exception.Message);
        }

        [Fact]
        public void Load_WhenConfigEntriesTableIsMissing_ShouldFail()
        {
            CreateRawDatabase("PRAGMA user_version = 1");

            var store = new SqliteConfigStore(DbPath);

            var exception = Record.Exception(() => store.Load());

            Assert.NotNull(exception);
            Assert.IsType<InvalidOperationException>(exception);
            Assert.Contains("ConfigEntries", exception.Message);
        }

        [Fact]
        public void Save_WhenDatabasePathIsAnExistingDirectory_ShouldPropagateFailure()
        {
            var directoryPath = Path.Combine(_root, "config.db-as-directory");
            Directory.CreateDirectory(directoryPath);
            var store = new SqliteConfigStore(directoryPath);

            Assert.Throws<SqliteException>(() =>
                store.Save(new Dictionary<string, string> { ["ScrollSpeed"] = "250" }));
        }

        [Fact]
        public void SaveAndLoad_WhenPathContainsConnectionStringDelimiters_ShouldRoundTrip()
        {
            // ';' (and '"' on platforms where it is a legal path character)
            // are connection-string delimiters: raw string interpolation
            // would misparse the path and target the wrong (or no)
            // database. The store must build its connection strings so such
            // paths survive.
            var directoryName = "delim;path";
            if (!OperatingSystem.IsWindows())
                directoryName = "delim;path\"quote";
            var databasePath = Path.Combine(_root, directoryName, "config.db");
            var store = new SqliteConfigStore(databasePath);
            var entries = new Dictionary<string, string>
            {
                ["ScrollSpeed"] = "250",
                ["SkinPath"] = "Default",
            };

            store.Save(entries);

            Assert.True(store.Exists);
            Assert.True(File.Exists(databasePath));

            var loaded = store.Load();

            Assert.Equal("250", loaded["ScrollSpeed"]);
            Assert.Equal("Default", loaded["SkinPath"]);
        }

        private static long ReadUserVersion(string databasePath)
        {
            using var connection = new SqliteConnection($"Data Source={databasePath}");
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "PRAGMA user_version;";
            return (long)command.ExecuteScalar()!;
        }

        private void CreateRawDatabase(params string[] statements)
        {
            using var connection = new SqliteConnection($"Data Source={DbPath}");
            connection.Open();
            foreach (var statement in statements)
            {
                using var command = connection.CreateCommand();
                command.CommandText = statement;
                command.ExecuteNonQuery();
            }
        }

        private static void ExecuteRaw(SqliteConnection connection, string commandText)
        {
            using var command = connection.CreateCommand();
            command.CommandText = commandText;
            command.ExecuteNonQuery();
        }
    }
}

using System;
using System.Threading;
using System.Threading.Tasks;
using DTXMania.Game.Lib.Song.Entities;
using Microsoft.EntityFrameworkCore;

namespace DTXMania.Test.Song
{
    /// <summary>
    /// A database service whose schema initialization is scripted: the optional
    /// <paramref name="ensureCreated"/> callback decides per attempt whether
    /// schema creation succeeds or faults. Models a failed (or purge-then-failed)
    /// Startup initialization without touching real files; every runtime context
    /// creation faults, mirroring the established broken-database fixtures.
    /// </summary>
    internal sealed class ScriptedInitializationService : SongDatabaseService
    {
        private readonly Func<SongDbContext, int, Task> _ensureCreated;
        private readonly Func<SongDbContext> _initializationContextFactory;
        private int _ensureAttempts;

        public ScriptedInitializationService(
            DbContextOptions<SongDbContext> options,
            Func<SongDbContext, int, Task>? ensureCreated = null)
            : base(
                options,
                () => throw new InvalidOperationException("database unavailable"),
                initialized: false)
        {
            _ensureCreated = ensureCreated
                ?? ((_, _) => throw new InvalidOperationException("initialization faulted"));
            _initializationContextFactory = () => new SongDbContext(options);
        }

        internal override bool InitializationDatabaseFileExists() => false;

        internal override SongDbContext CreateInitializationContext() =>
            _initializationContextFactory();

        internal override Task EnsureCreatedForInitializationAsync(
            SongDbContext context) =>
            _ensureCreated(context, Interlocked.Increment(ref _ensureAttempts));
    }
}

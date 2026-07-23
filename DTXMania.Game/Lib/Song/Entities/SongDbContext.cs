using Microsoft.EntityFrameworkCore;

namespace DTXMania.Game.Lib.Song.Entities
{
    /// <summary>
    /// DbContext Configuration for Song Database
    /// Entity Framework Core implementation for SQLite database
    /// </summary>
    public class SongDbContext : DbContext
    {
        public DbSet<Song> Songs => Set<Song>();
        public DbSet<SongChart> SongCharts => Set<SongChart>();
        public DbSet<SongScore> SongScores => Set<SongScore>();
        public DbSet<SongHierarchy> SongHierarchy => Set<SongHierarchy>();
        public DbSet<PerformanceHistory> PerformanceHistory => Set<PerformanceHistory>();
        public DbSet<ScoreSaveReceipt> ScoreSaveReceipts => Set<ScoreSaveReceipt>();

        public SongDbContext(DbContextOptions<SongDbContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            base.OnConfiguring(optionsBuilder);

            if (!optionsBuilder.IsConfigured)
            {
                optionsBuilder.UseSqlite("Data Source=:memory:", options =>
                {
                    options.CommandTimeout(30);
                });
            }
        }
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Unicode support for text fields to ensure proper Japanese text handling
            // Use HasColumnType instead of UseCollation to avoid SQLite constraint conflicts
            modelBuilder.Entity<Song>()
                .Property(s => s.Title)
                .HasColumnType("TEXT");
            
            modelBuilder.Entity<Song>()
                .Property(s => s.Artist)
                .HasColumnType("TEXT");
            
            modelBuilder.Entity<Song>()
                .Property(s => s.Genre)
                .HasColumnType("TEXT");
            
            modelBuilder.Entity<Song>()
                .Property(s => s.Comment)
                .HasColumnType("TEXT");

            // Song indexes for performance (metadata only)
            modelBuilder.Entity<Song>()
                .HasIndex(s => s.Title);
            modelBuilder.Entity<Song>()
                .HasIndex(s => s.Artist);
            modelBuilder.Entity<Song>()
                .HasIndex(s => s.Genre);
            modelBuilder.Entity<Song>()
                .HasIndex(s => s.IsBookmarked);

            // Configure Unicode support for SongChart text fields
            modelBuilder.Entity<SongChart>()
                .Property(sc => sc.FilePath)
                .HasColumnType("TEXT");
            
            modelBuilder.Entity<SongChart>()
                .Property(sc => sc.FileFormat)
                .HasColumnType("TEXT");

            // SongChart indexes and relationships
            modelBuilder.Entity<SongChart>()
                .HasIndex(sc => sc.FilePath)
                .IsUnique();

            modelBuilder.Entity<SongChart>()
                .HasOne(sc => sc.Song)
                .WithMany(s => s.Charts)
                .HasForeignKey(sc => sc.SongId)
                .OnDelete(DeleteBehavior.Cascade);

            // SongScore relationships
            modelBuilder.Entity<SongScore>()
                .HasOne(s => s.Chart)
                .WithMany(sc => sc.Scores)
                .HasForeignKey(s => s.ChartId)
                .OnDelete(DeleteBehavior.Cascade);

            // SongScore composite unique index
            modelBuilder.Entity<SongScore>()
                .HasIndex(s => new { s.ChartId, s.Instrument, s.PlaySpeedPercent })
                .IsUnique();

            modelBuilder.Entity<SongScore>()
                .Property(s => s.PlaySpeedPercent)
                .HasDefaultValue(100);

            // SongHierarchy self-referencing relationship
            modelBuilder.Entity<SongHierarchy>()
                .HasOne(h => h.Parent)
                .WithMany(h => h.Children)
                .HasForeignKey(h => h.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<SongHierarchy>()
                .HasOne(h => h.Song)
                .WithMany()
                .HasForeignKey(h => h.SongId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<SongHierarchy>()
                .HasIndex(h => h.DisplayOrder);

            // PerformanceHistory constraints
            modelBuilder.Entity<PerformanceHistory>()
                .HasIndex(p => p.SongId);

            modelBuilder.Entity<PerformanceHistory>()
                .HasIndex(p => new { p.SongScoreId, p.DisplayOrder })
                .IsUnique()
                .HasFilter("SongScoreId IS NOT NULL");

            modelBuilder.Entity<PerformanceHistory>()
                .HasOne(p => p.Song)
                .WithMany()
                .HasForeignKey(p => p.SongId)
                .OnDelete(DeleteBehavior.Cascade);

            modelBuilder.Entity<PerformanceHistory>()
                .HasOne(p => p.SongScore)
                .WithMany(s => s.PerformanceHistory)
                .HasForeignKey(p => p.SongScoreId)
                // SetNull (not Cascade): deleting a SongScore demotes its history rows to
                // legacy song-wide rows (SongScoreId -> null) instead of destroying them.
                // This preserves imported NX play history that could be re-associated with a
                // recreated score. The difficulty-scoped display filter (SongScoreId == Id)
                // lives in SongListNode.HydrateFromPersisted — it is the only place that
                // excludes null-SongScoreId rows from the badge.
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<PerformanceHistory>()
                .Property(p => p.PitchSemitones)
                .HasDefaultValue(0);

            // Score-save receipts retain their chart/instrument/speed identity even
            // after stale chart or score cleanup. ChartId intentionally has no FK.
            modelBuilder.Entity<ScoreSaveReceipt>()
                .HasKey(r => r.RunId);

            modelBuilder.Entity<ScoreSaveReceipt>()
                .Property(r => r.RunId)
                .ValueGeneratedNever();

            modelBuilder.Entity<ScoreSaveReceipt>()
                .Property(r => r.ChartId)
                .IsRequired();

            modelBuilder.Entity<ScoreSaveReceipt>()
                .Property(r => r.Instrument)
                .HasConversion<int>()
                .IsRequired();

            modelBuilder.Entity<ScoreSaveReceipt>()
                .Property(r => r.PlaySpeedPercent)
                .HasDefaultValue(100)
                .IsRequired();

            modelBuilder.Entity<ScoreSaveReceipt>()
                .Property(r => r.SavedAtUtc)
                .IsRequired();

            modelBuilder.Entity<ScoreSaveReceipt>()
                .HasOne(r => r.SongScore)
                .WithMany()
                .HasForeignKey(r => r.SongScoreId)
                .OnDelete(DeleteBehavior.SetNull);

            modelBuilder.Entity<ScoreSaveReceipt>()
                .HasIndex(r => r.SongScoreId);

            // Enum configurations
            modelBuilder.Entity<SongScore>()
                .Property(s => s.Instrument)
                .HasConversion<int>();

            modelBuilder.Entity<SongHierarchy>()
                .Property(h => h.NodeType)
                .HasConversion<int>();
        }
    }
}

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

            // Song indexes for performance (metadata only)
            modelBuilder.Entity<Song>()
                .HasIndex(s => s.Title);
            modelBuilder.Entity<Song>()
                .HasIndex(s => s.Artist);
            modelBuilder.Entity<Song>()
                .HasIndex(s => s.Genre);

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
                .HasIndex(s => new { s.ChartId, s.Instrument })
                .IsUnique();

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
                .HasIndex(p => new { p.SongId, p.DisplayOrder })
                .IsUnique();

            modelBuilder.Entity<PerformanceHistory>()
                .HasOne(p => p.Song)
                .WithMany()
                .HasForeignKey(p => p.SongId)
                .OnDelete(DeleteBehavior.Cascade);

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

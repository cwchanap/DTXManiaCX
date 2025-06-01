# DTXMania Song Data Management System

## 📊 Overview

This document covers the song discovery, parsing, and data management components of DTXManiaCX, including file enumeration, metadata parsing, and database storage systems.

## ✅ Implementation Status

### Phase 1: Core Data Structures - **100% Complete**
- **Song Node System**: Complete hierarchical structure with NodeType support (Score, Box, BackBox, Random)
- **Song Manager**: Full database management with JSON serialization and async enumeration
- **Score Data**: Comprehensive metadata storage with performance tracking and skill calculation
- **File Parsing**: DTX file header parsing with Shift_JIS encoding for Japanese text support

### Phase 2: Song Discovery & Enumeration - **100% Complete**
- **File Enumeration**: Complete recursive directory scanning for all supported formats (.dtx, .gda, .bms, .bme, .g2d)
- **JSON-Based Caching**: Full songs.db equivalent using JSON serialization with incremental updates
- **Background Threading**: Async enumeration with progress tracking and cancellation support
- **Set.def Support**: Multi-difficulty song definition parsing with proper file path resolution
- **Box.def Support**: Folder metadata parsing including title, genre, skin path, and colors

### Phase 3: Database Migration & Advanced Features - **📋 PLANNED**
- **SQLite Migration**: Replace JSON files with SQLite database using Entity Framework Core
- **Search System**: Text-based song filtering with real-time results and database indexing
- **Sorting Options**: Multiple sort criteria with database optimization
- **Performance Optimization**: Large library support (10,000+ songs) with database pagination

## 🎵 Song Data Architecture

### Song List Node Structure
```csharp
public class CSongListNode
{
    public ENodeType eNodeType;           // SCORE, BOX, BACKBOX, RANDOM
    public CScore[] arScore = new CScore[5]; // Up to 5 difficulty levels
    public string[] arDifficultyLabel = new string[5]; // Difficulty names
    public List<CSongListNode> list子リスト; // Child nodes for BOX navigation
    public CSongListNode r親ノード;        // Parent node reference
    public string strタイトル;             // Song/folder title
    public string strジャンル;             // Genre information
    public Color col文字色;                // Text color for display
    public string strBreadcrumbs;          // Navigation path for position tracking
    public string strSkinPath;             // Custom skin path for BOX folders
}
```

### Song Manager Database System
```csharp
public class CSongManager
{
    public List<CScore> listSongsDB;        // songs.db cache
    public List<CSongListNode> listSongRoot; // Root song list
    public int nNbScoresFromSongsDB;        // Database statistics
    public int nNbScoresFound;              // Enumeration statistics
    public bool bIsSuspending;              // Thread suspension control
    public AutoResetEvent autoReset;       // Thread synchronization
}
```

## 🔍 Song Discovery & Enumeration

### File Format Support
- **DTX Files**: `.dtx` - Primary DTXMania format
- **GDA Files**: `.gda` - Guitar format
- **BMS Files**: `.bms`, `.bme` - BeatMania format
- **G2D Files**: `.g2d` - Guitar format variant
- **Set Definition**: `set.def` - Multi-difficulty song definitions
- **Box Definition**: `box.def` - Folder organization with metadata

### Folder Organization Patterns
```
Songs/
├── dtxfiles.FolderName/     # DTXFiles-style BOX (title from folder name)
│   ├── box.def              # Optional metadata override
│   ├── song1.dtx
│   └── song2.dtx
├── CustomFolder/            # box.def-style BOX
│   ├── box.def              # Required for metadata
│   ├── set.def              # Multi-difficulty definitions
│   └── songs/
└── IndividualSong.dtx       # Standalone song file
```

### File Discovery Process
```csharp
public class SongEnumerationService
{
    private readonly string[] SUPPORTED_EXTENSIONS = { ".dtx", ".gda", ".bms", ".bme", ".g2d" };

    public async Task<List<SongListNode>> EnumerateDirectoryAsync(
        string basePath,
        SongListNode parent = null,
        IProgress<EnumerationProgress> progress = null,
        CancellationToken cancellationToken = default)
    {
        var results = new List<SongListNode>();
        var directory = new DirectoryInfo(basePath);

        // Check for set.def (multi-difficulty songs)
        var setDefPath = Path.Combine(basePath, "set.def");
        if (File.Exists(setDefPath))
        {
            var setDefSongs = await ParseSetDefinitionAsync(setDefPath, parent);
            results.AddRange(setDefSongs);
            return results;
        }

        // Check for box.def (folder metadata)
        var boxDefPath = Path.Combine(basePath, "box.def");
        BoxDefinition boxDef = null;
        if (File.Exists(boxDefPath))
        {
            boxDef = await ParseBoxDefinitionAsync(boxDefPath);
        }

        // Process subdirectories as BOX folders
        foreach (var subDir in directory.GetDirectories())
        {
            cancellationToken.ThrowIfCancellationRequested();

            var boxNode = CreateBoxNode(subDir, parent, boxDef);
            boxNode.Children = await EnumerateDirectoryAsync(
                subDir.FullName, boxNode, progress, cancellationToken);

            if (boxNode.Children.Count > 0)
            {
                results.Add(boxNode);
            }
        }

        // Process individual song files
        foreach (var file in directory.GetFiles())
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (SUPPORTED_EXTENSIONS.Contains(file.Extension.ToLowerInvariant()))
            {
                var songNode = await CreateSongNodeAsync(file.FullName, parent);
                if (songNode != null)
                {
                    results.Add(songNode);
                    progress?.Report(new EnumerationProgress
                    {
                        CurrentFile = file.Name,
                        ProcessedCount = results.Count
                    });
                }
            }
        }

        return results;
    }
}
```

## 📄 DTX Metadata Parser

### DTX File Parsing
```csharp
public class DTXMetadataParser
{
    public async Task<SongMetadata> ParseMetadataAsync(string filePath)
    {
        var metadata = new SongMetadata();

        using var reader = new StreamReader(filePath, Encoding.GetEncoding("Shift_JIS"));
        string line;

        while ((line = await reader.ReadLineAsync()) != null)
        {
            if (line.StartsWith("#"))
            {
                var parts = line.Split(':', 2);
                if (parts.Length == 2)
                {
                    var command = parts[0].Trim().ToUpperInvariant();
                    var value = parts[1].Trim();

                    switch (command)
                    {
                        case "#TITLE":
                            metadata.Title = value;
                            break;
                        case "#ARTIST":
                            metadata.Artist = value;
                            break;
                        case "#GENRE":
                            metadata.Genre = value;
                            break;
                        case "#BPM":
                            if (double.TryParse(value, out var bpm))
                                metadata.BPM = bpm;
                            break;
                        case "#LEVEL":
                            ParseLevelData(value, metadata);
                            break;
                        case "#PREVIEW":
                            metadata.PreviewFile = value;
                            break;
                        case "#PREIMAGE":
                            metadata.PreviewImage = value;
                            break;
                        case "#COMMENT":
                            metadata.Comment = value;
                            break;
                    }
                }
            }

            // Stop parsing after header section
            if (line.StartsWith("*") || line.Contains("|"))
                break;
        }

        return metadata;
    }

    private void ParseLevelData(string levelData, SongMetadata metadata)
    {
        // Parse level data: "DRUMS:85,GUITAR:78,BASS:65"
        var parts = levelData.Split(',');
        foreach (var part in parts)
        {
            var instrumentLevel = part.Split(':');
            if (instrumentLevel.Length == 2)
            {
                var instrument = instrumentLevel[0].Trim().ToUpperInvariant();
                if (int.TryParse(instrumentLevel[1].Trim(), out var level))
                {
                    switch (instrument)
                    {
                        case "DRUMS":
                            metadata.DrumLevel = level;
                            break;
                        case "GUITAR":
                            metadata.GuitarLevel = level;
                            break;
                        case "BASS":
                            metadata.BassLevel = level;
                            break;
                    }
                }
            }
        }
    }
}
```

## 💾 Current Data Storage Architecture

### ✅ JSON-Based Implementation (Current)
The current implementation uses a simple but effective JSON-based storage system:

**Storage Format:**
```csharp
public class SongDatabaseData
{
    public List<SongScore> Scores { get; set; } = new();
    public List<SongListNode> RootNodes { get; set; } = new();
    public DateTime LastUpdated { get; set; } = DateTime.UtcNow;
    public string Version { get; set; } = "1.0";
}
```

**Key Features:**
- ✅ **JSON Serialization**: Human-readable format for debugging and manual editing
- ✅ **Incremental Updates**: Only processes changed files based on modification time
- ✅ **Thread-Safe Operations**: Proper locking for concurrent access
- ✅ **Cross-Platform**: Works on all MonoGame supported platforms
- ✅ **Simple Backup**: Easy to backup/restore with file copy operations

**Performance Characteristics:**
- **Load Time**: ~100-500ms for 1,000-5,000 songs
- **Memory Usage**: ~50-200MB for typical song libraries
- **Search Performance**: O(n) linear search through in-memory collections
- **Update Performance**: Fast incremental updates with modification time checking

**Limitations:**
- **Large Libraries**: Performance degrades with 10,000+ songs
- **Search Speed**: No indexing for complex queries
- **Concurrent Access**: Limited to single-process access
- **Data Integrity**: No transaction support or referential integrity

## 📋 Future Database Migration (Phase 3)

### Planned: Entity Framework Core + SQLite Implementation

**Effort Assessment: MEDIUM** ⭐⭐⭐ (1-2 weeks)

The current JSON-based system works well for medium-sized libraries, but for large libraries (10,000+ songs) and advanced features, migrating to SQLite with Entity Framework Core would provide significant benefits:

**Benefits:**
- **Type Safety**: Strongly-typed queries with compile-time checking
- **LINQ Support**: Natural C# query syntax instead of raw SQL
- **Automatic Migrations**: Schema changes handled automatically
- **Change Tracking**: Automatic dirty checking and optimized updates
- **Relationship Management**: Automatic foreign key handling and navigation properties
- **Performance**: Built-in query optimization and caching
- **Testing**: Easy to mock and unit test with InMemory provider

**Required Changes:**
1. Add EF Core NuGet packages (2 packages, cross-platform)
2. Define entity models with attributes/fluent API
3. Create DbContext with DbSets
4. Replace manual SQL with LINQ queries

**NuGet Packages:**
```xml
<PackageReference Include="Microsoft.EntityFrameworkCore.Sqlite" Version="7.0.0" />
<PackageReference Include="Microsoft.EntityFrameworkCore.Tools" Version="7.0.0" />
```

### Entity Models

**Song Entity:**
```csharp
// Song entity (replaces both songlist.db and songs.db)
public class Song
{
    public int Id { get; set; }

    [Required, MaxLength(500)]
    public string FilePath { get; set; } = "";

    public long FileSize { get; set; }
    public DateTime LastModified { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    [MaxLength(200)]
    public string? Artist { get; set; }

    [MaxLength(100)]
    public string? Genre { get; set; }

    public double? BPM { get; set; }
    public int? DrumLevel { get; set; }
    public int? GuitarLevel { get; set; }
    public int? BassLevel { get; set; }

    [MaxLength(200)]
    public string? PreviewFile { get; set; }

    [MaxLength(200)]
    public string? PreviewImage { get; set; }

    [MaxLength(1000)]
    public string? Comment { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;

    // Navigation properties
    public virtual ICollection<SongScore> Scores { get; set; } = new List<SongScore>();
    public virtual ICollection<SongHierarchy> HierarchyNodes { get; set; } = new List<SongHierarchy>();
}
```

**Song Hierarchy Entity:**
```csharp
// Song hierarchy (replaces songlist.db structure)
public class SongHierarchy
{
    public int Id { get; set; }

    public int? SongId { get; set; }
    public virtual Song? Song { get; set; }

    public int? ParentId { get; set; }
    public virtual SongHierarchy? Parent { get; set; }
    public virtual ICollection<SongHierarchy> Children { get; set; } = new List<SongHierarchy>();

    [Required]
    public NodeType NodeType { get; set; }

    [MaxLength(200)]
    public string? Title { get; set; }

    public int DisplayOrder { get; set; }

    [MaxLength(1000)]
    public string? BreadcrumbPath { get; set; }

    [MaxLength(500)]
    public string? SkinPath { get; set; }
}
```

**Performance Scores Entity:**
```csharp
// Performance scores (replaces songs.db score data)
public class SongScore
{
    public int Id { get; set; }

    public int SongId { get; set; }
    public virtual Song Song { get; set; } = null!;

    public int Difficulty { get; set; }
    public int BestScore { get; set; }
    public int BestRank { get; set; }
    public bool FullCombo { get; set; }
    public int PlayCount { get; set; }
    public DateTime? LastPlayed { get; set; }

    public double HighSkill { get; set; }
    public double SongSkill { get; set; }
}

public enum NodeType
{
    Song = 0,
    Box = 1,
    BackBox = 2,
    Random = 3
}
```

### DbContext Configuration

```csharp
public class SongDbContext : DbContext
{
    public DbSet<Song> Songs { get; set; } = null!;
    public DbSet<SongHierarchy> SongHierarchy { get; set; } = null!;
    public DbSet<SongScore> SongScores { get; set; } = null!;

    public SongDbContext(DbContextOptions<SongDbContext> options) : base(options) { }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Song entity configuration
        modelBuilder.Entity<Song>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => e.FilePath).IsUnique();
            entity.HasIndex(e => e.Title);
            entity.HasIndex(e => e.Artist);
            entity.HasIndex(e => e.Genre);
            entity.HasIndex(e => e.LastModified);

            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("CURRENT_TIMESTAMP");
        });

        // Song hierarchy configuration
        modelBuilder.Entity<SongHierarchy>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.ParentId, e.DisplayOrder });

            // Self-referencing relationship
            entity.HasOne(e => e.Parent)
                .WithMany(e => e.Children)
                .HasForeignKey(e => e.ParentId)
                .OnDelete(DeleteBehavior.Cascade);

            // Song relationship
            entity.HasOne(e => e.Song)
                .WithMany(e => e.HierarchyNodes)
                .HasForeignKey(e => e.SongId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        // Song score configuration
        modelBuilder.Entity<SongScore>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.HasIndex(e => new { e.SongId, e.Difficulty }).IsUnique();

            entity.HasOne(e => e.Song)
                .WithMany(e => e.Scores)
                .HasForeignKey(e => e.SongId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
```

### Repository Service (EF Core LINQ Queries)

```csharp
public class SongRepository
{
    private readonly SongDbContext _context;

    public SongRepository(SongDbContext context)
    {
        _context = context;
    }

    // Get song by file path (replaces manual SQL)
    public async Task<Song?> GetSongAsync(string filePath)
    {
        return await _context.Songs
            .Include(s => s.Scores)
            .Include(s => s.HierarchyNodes)
            .FirstOrDefaultAsync(s => s.FilePath == filePath);
    }

    // Search songs with LINQ (much cleaner than SQL)
    public async Task<List<Song>> SearchSongsAsync(string searchTerm, int limit = 100)
    {
        return await _context.Songs
            .Where(s => s.Title!.Contains(searchTerm) ||
                       s.Artist!.Contains(searchTerm) ||
                       s.Genre!.Contains(searchTerm))
            .OrderBy(s => s.Title)
            .Take(limit)
            .ToListAsync();
    }

    // Upsert song (EF Core handles this automatically)
    public async Task UpsertSongAsync(Song song)
    {
        var existing = await _context.Songs
            .FirstOrDefaultAsync(s => s.FilePath == song.FilePath);

        if (existing != null)
        {
            // Update existing
            _context.Entry(existing).CurrentValues.SetValues(song);
            existing.UpdatedAt = DateTime.UtcNow;
        }
        else
        {
            // Add new
            _context.Songs.Add(song);
        }

        await _context.SaveChangesAsync();
    }

    // Get songs modified after date
    public async Task<List<Song>> GetSongsModifiedAfterAsync(DateTime lastCheck)
    {
        return await _context.Songs
            .Where(s => s.LastModified > lastCheck)
            .OrderByDescending(s => s.LastModified)
            .ToListAsync();
    }

    // Get song hierarchy (replaces songlist.db loading)
    public async Task<List<SongHierarchy>> GetSongHierarchyAsync(int? parentId = null)
    {
        return await _context.SongHierarchy
            .Include(h => h.Song)
            .Include(h => h.Children)
            .Where(h => h.ParentId == parentId)
            .OrderBy(h => h.DisplayOrder)
            .ToListAsync();
    }
}
```

### Performance Comparison

| Operation | JSON File | SQLite + EF Core | Improvement |
|-----------|-----------|------------------|-------------|
| **Load All Songs** | O(n) - Read entire file | O(1) - Index lookup | 10-100x faster |
| **Search by Title** | O(n) - Linear scan | O(log n) - Index search | 100-1000x faster |
| **Update Single Song** | O(n) - Rewrite entire file | O(1) - Single UPDATE | 1000x+ faster |
| **Add New Song** | O(n) - Rewrite entire file | O(1) - Single INSERT | 1000x+ faster |
| **Memory Usage** | Load all in RAM | Stream results | 10-100x less memory |

**Real-World Impact:**
- **10,000 songs**: JSON load ~5-10 seconds → SQLite ~0.1 seconds
- **Search "Beatles"**: JSON ~2 seconds → SQLite ~0.01 seconds
- **Add new song**: JSON ~5 seconds → SQLite ~0.001 seconds
- **Memory**: JSON ~500MB → SQLite ~50MB

## 📁 Implementation Files

### Phase 1 & 2 Files (Completed)
- `DTXMania.Shared.Game/Lib/Song/SongMetadata.cs` - Song metadata with DTX parsing support
- `DTXMania.Shared.Game/Lib/Song/SongScore.cs` - Performance score tracking with skill calculation
- `DTXMania.Shared.Game/Lib/Song/SongListNode.cs` - Hierarchical song list structure
- `DTXMania.Shared.Game/Lib/Song/DTXMetadataParser.cs` - DTX file parsing with Japanese text support
- `DTXMania.Shared.Game/Lib/Song/SongManager.cs` - Song database management and enumeration
- `DTXMania.Shared.Game/Lib/Services/SongEnumerationService.cs` - File discovery and enumeration

### Unit Test Coverage
- ✅ **506 comprehensive unit tests** covering all implemented functionality
- ✅ **SongMetadataTests** (26 tests) - metadata handling, calculated properties, cloning
- ✅ **SongScoreTests** (20 tests) - score tracking, rank calculation, skill computation
- ✅ **SongListNodeTests** (25 tests) - hierarchical organization, node operations, sorting
- ✅ **DTXMetadataParserTests** (18 tests) - file parsing, encoding support, error handling
- ✅ **SongManagerTests** (22 tests) - database management, enumeration, event handling
- ✅ **SetDefParserTests** (17 tests) - set.def parsing with DTXMania format support
- ✅ **xUnit framework** with Theory/InlineData patterns following project standards

### Key Achievements
- ✅ **JSON-based song database system** with caching and incremental enumeration
- ✅ **Full DTX file parsing** with Japanese text support (Shift_JIS encoding)
- ✅ **Hierarchical song organization** supporting set.def/box.def folder structures
- ✅ **Enhanced set.def parsing** with proper DTXMania format support
- ✅ **Performance optimizations** for medium-sized libraries with background threading
- ✅ **Cross-platform compatibility** with proper file path handling
- ✅ **Thread-safe data operations** with proper locking mechanisms

## 🔧 Technical Considerations

### Performance Requirements
- **Large Libraries**: Support for 10,000+ songs
- **Memory Management**: Efficient caching and lazy loading
- **Startup Time**: Fast initial song enumeration
- **Search Performance**: Real-time filtering and sorting

### Cross-Platform Compatibility
- **File Paths**: Handle Windows/Unix path differences
- **Text Encoding**: Proper Japanese character handling (Shift_JIS)
- **Database**: SQLite works on all MonoGame supported platforms
- **Resource Loading**: MonoGame-compatible asset management

### Integration Points
- **Song Selection UI**: Provides data for song list display
- **Performance Tracking**: Stores and retrieves play statistics
- **Search System**: Enables real-time song filtering
- **Preview System**: Provides metadata for audio preview loading

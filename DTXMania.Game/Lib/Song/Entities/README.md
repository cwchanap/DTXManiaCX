# Song Database Entities

This directory contains the **fully implemented** Entity Framework Core entities for the DTXMania song database system using SQLite.

## Entity Structure

### Core Entities

1. **Song** (`Song.cs`) ✅
   - High-level song metadata (shared across all charts of the same song)
   - Contains title, artist, genre, comment, and timestamps
   - Full EF Core data annotations and navigation properties

2. **SongChart** (`SongChart.cs`) ✅
   - Represents an individual DTX file (1:1 mapping with DTX files)
   - Contains file information, chart properties, level information, and resources
   - Each DTX file has its own SongChart entity
   - Proper foreign key relationships to Song and SongScore

3. **SongScore** (`SongScore.cs`) ✅
   - Performance records and statistics for a specific chart
   - Each score is tied to a SongChart (ChartId) rather than Song
   - Tracks best scores, ranks, play counts, and input method usage
   - Uses EInstrumentPart enum for type safety

4. **SongHierarchy** (`SongHierarchy.cs`) ✅
   - Folder structure and navigation hierarchy
   - Supports nested folder organization with BOX system
   - Self-referencing parent/child relationships

5. **PerformanceHistory** (`PerformanceHistory.cs`) ✅
   - Historical performance tracking
   - Stores recent play history lines

### Supporting Types

- **Enums** (`Enums.cs`) ✅
  - `EInstrumentPart`: Instrument enumeration (DRUMS, GUITAR, BASS)
  - `ENodeType`: Node type enumeration for hierarchy (Song, Box, BackBox, Random)

### Database Context & Services

- **SongDbContext** (`SongDbContext.cs`) ✅
  - **FULLY IMPLEMENTED** Entity Framework DbContext configuration
  - Includes indexes, relationships, and constraints
  - Enum conversions and cascade delete behaviors
  - Ready for production use

- **SongDatabaseService** (`SongDatabaseService.cs`) ✅ **COMPREHENSIVE**
  - **All-in-one database service** with both low-level and high-level operations
  - Database initialization, migrations, backup/restore
  - Complete CRUD operations for songs, scores, and hierarchy
  - Search and query functionality
  - Health checks and statistics
  - Production-ready with example usage patterns built-in

## 🚀 Usage

### Initialize Database
```csharp
var databaseService = new SongDatabaseService("songs.db");
await databaseService.InitializeDatabaseAsync();
```

### Basic Operations
```csharp
// All operations are now in the single SongDatabaseService
var databaseService = new SongDatabaseService("songs.db");

// Add a song from metadata (creates Song + SongChart + initial SongScores)
int songId = await databaseService.AddSongAsync(songMetadata);

// Search songs
var results = await databaseService.SearchSongsAsync("My Song Title");

// Update scores (now uses ChartId instead of SongId + DifficultyId)
await databaseService.UpdateScoreAsync(chartId, EInstrumentPart.DRUMS, 95000, 0.95, true);

// Get top scores
var topScores = await databaseService.GetTopScoresAsync(EInstrumentPart.DRUMS, 10);

// Create folders and organize hierarchy
int folderId = await databaseService.CreateFolderAsync("J-Rock", "Rock");
await databaseService.AddSongToHierarchyAsync(songId, folderId);
```

### Direct DbContext Usage
```csharp
using var context = databaseService.CreateContext();
var topSongs = await context.Songs
    .Include(s => s.Charts)
    .ThenInclude(c => c.Scores)
    .OrderByDescending(s => s.Charts.SelectMany(c => c.Scores).Max(sc => sc.BestScore))
    .Take(10)
    .ToListAsync();
```

## Key Features

- ✅ **Type Safety**: Strongly typed entities with proper data annotations
- ✅ **Relationships**: Proper foreign key relationships between entities
- ✅ **Indexing**: Performance-optimized indexes on commonly queried fields
- ✅ **DTXMania Compatibility**: Maintains compatibility with existing DTXMania patterns
- ✅ **Migration Ready**: Supports EF Core migrations for database schema updates
- ✅ **Cross-Platform**: SQLite support for Windows and Mac
- ✅ **Production Ready**: Includes service layer and example implementations

## Database Schema

The implementation creates the following tables:
- `Songs` - Core song information
- `SongChart` - Per-difficulty/chart data
- `SongScores` - Performance records
- `SongHierarchy` - Folder structure
- `PerformanceHistory` - Play history

All tables include proper indexes, foreign keys, and constraints for optimal performance.

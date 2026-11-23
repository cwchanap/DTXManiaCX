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

- **SongDatabase** (`SongDatabase.cs`) ✅
  - Owns the database path/options, initialization lifecycle, schema versioning
    (`PRAGMA user_version`) and short-lived `SongDbContext` creation (HPA-801)
  - One shared instance backs both stores below; there is no all-in-one
    service and no singleton

- **SongCatalogStore** (`SongCatalogStore.cs`) ✅
  - Catalog-side persistence over the shared `SongDatabase`
  - Atomic bulk import/cleanup (internal, driven by the library's scan),
    chart inventory queries, per-root scan watermarks and rejected-tab counts

- **SongScoreStore** (`SongScoreStore.cs`) ✅
  - Score-side persistence over the shared `SongDatabase`
  - Transactional score saves with RunId receipt idempotency/race resolution,
    plus score and play-history reads (one row per chart/instrument/speed variant)

Higher-level composition lives outside this folder: `SongLibrary`
(`Lib/Song/SongLibrary.cs`) owns enumeration and publications, the stateless
`SongLibraryScanner` performs cold scans, `SongHierarchyBuilder` rebuilds warm
trees from the database, and `SongScoreService`/`SongQueryService` expose score
mutations and recent-plays/bookmark/search queries.

## Usage

### Initialize the database and share it across the stores
```csharp
var database = new SongDatabase("songs.db");
await database.InitializeDatabaseAsync();
var catalog = new SongCatalogStore(database);
var scores = new SongScoreStore(database);
```

### Common operations
```csharp
// Update scores (uses ChartId, not SongId + DifficultyId)
await scores.UpdateScoreAsync(chartId, EInstrumentPart.DRUMS, performanceSummary);

// Read one score variant with its play history
var score = await scores.GetScoreWithHistoryAsync(
    chartId, EInstrumentPart.DRUMS, playSpeedPercent: 100);

// Chart inventory and scan watermarks
var songs = await catalog.GetSongsAsync();
await catalog.SetEnumerationWatermarkAtomicallyAsync(watermarkUtc, canonicalRoots);
```

Song/chart seeding for tests uses the `SongDbSeeder` extension methods in
`DTXMania.Test/TestData/`; bulk import runs inside the library's scan
transaction and is not a public store API.

### Direct DbContext Usage
```csharp
using var context = database.CreateContext();
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
- ✅ **Production Ready**: Split catalog/score stores over one shared database instance

## Database Schema

The implementation creates the following tables:
- `Songs` - Core song information
- `SongChart` - Per-difficulty/chart data
- `SongScores` - Performance records
- `SongHierarchy` - Folder structure
- `PerformanceHistory` - Play history

All tables include proper indexes, foreign keys, and constraints for optimal performance.

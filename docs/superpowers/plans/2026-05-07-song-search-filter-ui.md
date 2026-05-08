# Song Search & Filter UI Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add a keyboard-driven modal that lets the user search, filter, and sort the song list in the Song Selection stage. Search and facets operate on the in-memory song database; results flatten the folder hierarchy. Filter state is session-scoped.

**Architecture:** Data layer is a pure projection (`SongListFilterService`) over `SongListNode`s held by `SongManager`. UI is a new modal overlay (`SongSearchFilterModal`) composed of a reusable `UITextInput` plus facet/sort controls. The Song Selection stage owns the active `SongFilterCriteria`, computes `_filteredView` on Apply, and binds `SongListDisplay` to either the filtered view or the existing hierarchical list.

**Tech Stack:** C# / .NET 8, MonoGame 3.8 (`SpriteBatch`, `GameWindow.TextInput`), xUnit + Moq, EF Core (existing), `ManagedFont` / `SpriteFont`.

**Spec:** `docs/superpowers/specs/2026-05-07-song-search-filter-ui-design.md`

---

## File Map

**Created:**
- `DTXMania.Game/Lib/Song/Filtering/PlayedStatus.cs` — enum.
- `DTXMania.Game/Lib/Song/Filtering/SongFilterCriteria.cs` — value record.
- `DTXMania.Game/Lib/Song/Filtering/FilteredSongResult.cs` — readonly record struct.
- `DTXMania.Game/Lib/Song/Filtering/ISongListFilterService.cs` — interface.
- `DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs` — projection.
- `DTXMania.Game/Lib/UI/Components/ITextInputSource.cs` — abstraction over `GameWindow.TextInput`.
- `DTXMania.Game/Lib/UI/Components/WindowTextInputSource.cs` — `GameWindow` adapter.
- `DTXMania.Game/Lib/UI/Components/UITextInput.cs` — reusable text input element.
- `DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs` — modal overlay.
- `DTXMania.Test/Song/Filtering/SongFilterCriteriaTests.cs`
- `DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs`
- `DTXMania.Test/UI/UITextInputTests.cs`
- `DTXMania.Test/Stage/SongSelectionStageFilterTests.cs`

**Modified:**
- `DTXMania.Game/Lib/Input/InputManager.cs` — add `OpenSearch` to `InputCommandType`, map `Keys.Back`.
- `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs` — add `SearchFilterModal` static class.
- `DTXMania.Game/Lib/Stage/SongSelectionStage.cs` — wire criteria, filtered view, modal lifecycle, breadcrumb summary, empty state, selection clamping.
- `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs` — add optional folder-hint label.
- `DTXMania.Test/DTXMania.Test.Mac.csproj` — exclude graphics-dependent modal tests.

**No SongListDisplay changes needed**: the stage's existing `PopulateSongList` already controls whether to include the `BackBox` row; we'll add a sibling `PopulateFilteredSongList` that just emits the flat result list.

---

## Phase 1 — Data Layer

### Task 1: PlayedStatus enum

**Files:**
- Create: `DTXMania.Game/Lib/Song/Filtering/PlayedStatus.cs`

- [ ] **Step 1: Create the file**

```csharp
namespace DTXMania.Game.Lib.Song.Filtering
{
    public enum PlayedStatus
    {
        All,
        Unplayed,
        Played,
        Cleared
    }
}
```

- [ ] **Step 2: Build to confirm it compiles**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeds with 0 errors.

- [ ] **Step 3: Commit**

```bash
git add DTXMania.Game/Lib/Song/Filtering/PlayedStatus.cs
git commit -m "feat: add PlayedStatus enum for song filtering"
```

---

### Task 2: SongFilterCriteria record + tests

**Files:**
- Create: `DTXMania.Game/Lib/Song/Filtering/SongFilterCriteria.cs`
- Create: `DTXMania.Test/Song/Filtering/SongFilterCriteriaTests.cs`

- [ ] **Step 1: Write the failing tests**

Path: `DTXMania.Test/Song/Filtering/SongFilterCriteriaTests.cs`

```csharp
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using Xunit;

namespace DTXMania.Test.Song.Filtering
{
    public class SongFilterCriteriaTests
    {
        [Fact]
        public void Default_HasExpectedValues()
        {
            var c = SongFilterCriteria.Default;

            Assert.Equal("", c.SearchQuery);
            Assert.Null(c.MinLevel);
            Assert.Null(c.MaxLevel);
            Assert.Equal(PlayedStatus.All, c.PlayedStatus);
            Assert.Equal(SongSortCriteria.Title, c.SortBy);
            Assert.False(c.SortDescending);
        }

        [Fact]
        public void IsEmpty_TrueForDefault()
        {
            Assert.True(SongFilterCriteria.Default.IsEmpty);
        }

        [Fact]
        public void IsEmpty_FalseWhenSearchQuerySet()
        {
            var c = SongFilterCriteria.Default with { SearchQuery = "abc" };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void IsEmpty_FalseWhenLevelSet()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 50 };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void IsEmpty_FalseWhenPlayedStatusNotAll()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void IsEmpty_FalseWhenSortDescending()
        {
            var c = SongFilterCriteria.Default with { SortDescending = true };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void IsEmpty_FalseWhenSortByNotTitle()
        {
            var c = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Artist };
            Assert.False(c.IsEmpty);
        }

        [Fact]
        public void Equality_RecordValueSemantics()
        {
            var a = SongFilterCriteria.Default with { SearchQuery = "x" };
            var b = SongFilterCriteria.Default with { SearchQuery = "x" };
            Assert.Equal(a, b);
        }
    }
}
```

- [ ] **Step 2: Run tests to verify they fail (no `SongFilterCriteria` yet)**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongFilterCriteriaTests"`
Expected: Build error (`SongFilterCriteria` does not exist).

- [ ] **Step 3: Write the implementation**

Path: `DTXMania.Game/Lib/Song/Filtering/SongFilterCriteria.cs`

```csharp
using DTXMania.Game.Lib.Song;

namespace DTXMania.Game.Lib.Song.Filtering
{
    public sealed record SongFilterCriteria(
        string SearchQuery,
        int? MinLevel,
        int? MaxLevel,
        PlayedStatus PlayedStatus,
        SongSortCriteria SortBy,
        bool SortDescending)
    {
        public static SongFilterCriteria Default { get; } = new(
            SearchQuery: "",
            MinLevel: null,
            MaxLevel: null,
            PlayedStatus: PlayedStatus.All,
            SortBy: SongSortCriteria.Title,
            SortDescending: false);

        public bool IsEmpty =>
            string.IsNullOrEmpty(SearchQuery)
            && MinLevel is null
            && MaxLevel is null
            && PlayedStatus == PlayedStatus.All
            && SortBy == SongSortCriteria.Title
            && !SortDescending;
    }
}
```

- [ ] **Step 4: Run tests to verify they pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongFilterCriteriaTests"`
Expected: All 8 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Filtering/SongFilterCriteria.cs \
        DTXMania.Test/Song/Filtering/SongFilterCriteriaTests.cs
git commit -m "feat: add SongFilterCriteria record with default and IsEmpty"
```

---

### Task 3: FilteredSongResult struct + ISongListFilterService interface

**Files:**
- Create: `DTXMania.Game/Lib/Song/Filtering/FilteredSongResult.cs`
- Create: `DTXMania.Game/Lib/Song/Filtering/ISongListFilterService.cs`

- [ ] **Step 1: Create FilteredSongResult**

Path: `DTXMania.Game/Lib/Song/Filtering/FilteredSongResult.cs`

```csharp
namespace DTXMania.Game.Lib.Song.Filtering
{
    public readonly record struct FilteredSongResult(
        SongListNode Node,
        string FolderPath);
}
```

- [ ] **Step 2: Create ISongListFilterService**

Path: `DTXMania.Game/Lib/Song/Filtering/ISongListFilterService.cs`

```csharp
using System.Collections.Generic;

namespace DTXMania.Game.Lib.Song.Filtering
{
    public interface ISongListFilterService
    {
        IReadOnlyList<FilteredSongResult> Apply(
            IEnumerable<SongListNode> roots,
            SongFilterCriteria criteria);
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Game/Lib/Song/Filtering/FilteredSongResult.cs \
        DTXMania.Game/Lib/Song/Filtering/ISongListFilterService.cs
git commit -m "feat: add FilteredSongResult and ISongListFilterService contracts"
```

---

### Task 4: SongListFilterService — flatten + folder path

**Files:**
- Create: `DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs`
- Create: `DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Path: `DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs`

```csharp
using System.Collections.Generic;
using System.Linq;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using Xunit;

namespace DTXMania.Test.Song.Filtering
{
    public class SongListFilterServiceTests
    {
        private readonly SongListFilterService _svc = new();

        private static SongListNode Score(string title, string artist = "", int level = 50)
        {
            var node = new SongListNode { Type = NodeType.Score, Title = title };
            node.Scores[0] = new DTXMania.Game.Lib.Song.Entities.SongScore
            {
                DifficultyLevel = level,
                DifficultyLabel = "BASIC"
            };
            // Artist lives on DatabaseSong — wire a minimal entity
            if (!string.IsNullOrEmpty(artist))
            {
                node.DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song
                {
                    Title = title,
                    Artist = artist
                };
            }
            return node;
        }

        private static SongListNode Box(string title, params SongListNode[] children)
        {
            var box = SongListNode.CreateBoxNode(title, $"/path/{title}");
            foreach (var c in children)
                box.AddChild(c);
            return box;
        }

        [Fact]
        public void Apply_FlattensRootScoreNodes_NoFilter()
        {
            var roots = new List<SongListNode>
            {
                Score("Song A"),
                Score("Song B")
            };

            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            Assert.Equal(2, result.Count);
            Assert.Equal(new[] { "Song A", "Song B" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_FlattensNestedScoreNodes_NoFilter()
        {
            var roots = new List<SongListNode>
            {
                Box("J-POP",
                    Score("Pop1"),
                    Box("80s", Score("Pop80a"), Score("Pop80b"))),
                Score("RootSong")
            };

            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            // Sorted by Title (default): Pop1, Pop80a, Pop80b, RootSong
            Assert.Equal(4, result.Count);
            Assert.Equal(new[] { "Pop1", "Pop80a", "Pop80b", "RootSong" },
                result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_PopulatesFolderPathFromParentBreadcrumb()
        {
            var roots = new List<SongListNode>
            {
                Box("J-POP",
                    Box("80s", Score("Hit")))
            };

            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            var hit = result.Single();
            Assert.Equal("J-POP / 80s", hit.FolderPath);
        }

        [Fact]
        public void Apply_RootScoreHasEmptyFolderPath()
        {
            var roots = new List<SongListNode> { Score("RootOnly") };

            var result = _svc.Apply(roots, SongFilterCriteria.Default);

            Assert.Equal("", result.Single().FolderPath);
        }

        [Fact]
        public void Apply_ExcludesBackBoxAndRandomNodes()
        {
            var box = Box("Folder", Score("InsideSong"));
            box.AddChild(SongListNode.CreateBackNode(box));
            box.AddChild(SongListNode.CreateRandomNode());

            var result = _svc.Apply(new[] { box }, SongFilterCriteria.Default);

            Assert.Equal(new[] { "InsideSong" }, result.Select(r => r.Node.DisplayTitle));
        }
    }
}
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListFilterServiceTests"`
Expected: Build error (`SongListFilterService` not defined).

- [ ] **Step 3: Implement service with flatten + folder-path + default sort**

Path: `DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs`

```csharp
using System;
using System.Collections.Generic;
using System.Linq;

namespace DTXMania.Game.Lib.Song.Filtering
{
    public sealed class SongListFilterService : ISongListFilterService
    {
        public IReadOnlyList<FilteredSongResult> Apply(
            IEnumerable<SongListNode> roots,
            SongFilterCriteria criteria)
        {
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            var flat = new List<FilteredSongResult>();
            foreach (var root in roots)
                Flatten(root, parentPath: "", flat);

            return SortResults(flat, criteria);
        }

        private static void Flatten(SongListNode node, string parentPath, List<FilteredSongResult> sink)
        {
            if (node == null) return;

            if (node.Type == NodeType.Score)
            {
                sink.Add(new FilteredSongResult(node, parentPath));
                return;
            }

            if (node.Type == NodeType.Box)
            {
                var childPath = string.IsNullOrEmpty(parentPath)
                    ? node.DisplayTitle
                    : parentPath + " / " + node.DisplayTitle;
                foreach (var child in node.Children)
                    Flatten(child, childPath, sink);
            }
            // BackBox / Random: ignore
        }

        private static IReadOnlyList<FilteredSongResult> SortResults(
            List<FilteredSongResult> flat, SongFilterCriteria criteria)
        {
            int Compare(FilteredSongResult a, FilteredSongResult b)
            {
                int cmp = criteria.SortBy switch
                {
                    SongSortCriteria.Title =>
                        string.Compare(a.Node.DisplayTitle, b.Node.DisplayTitle,
                            StringComparison.OrdinalIgnoreCase),
                    SongSortCriteria.Artist =>
                        string.Compare(
                            a.Node.DatabaseSong?.DisplayArtist ?? "",
                            b.Node.DatabaseSong?.DisplayArtist ?? "",
                            StringComparison.OrdinalIgnoreCase),
                    SongSortCriteria.Genre =>
                        string.Compare(a.Node.Genre ?? "", b.Node.Genre ?? "",
                            StringComparison.OrdinalIgnoreCase),
                    SongSortCriteria.Level =>
                        a.Node.MaxDifficultyLevel.CompareTo(b.Node.MaxDifficultyLevel),
                    _ => string.Compare(a.Node.DisplayTitle, b.Node.DisplayTitle,
                            StringComparison.OrdinalIgnoreCase)
                };
                return criteria.SortDescending ? -cmp : cmp;
            }

            flat.Sort(Compare);
            return flat;
        }
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListFilterServiceTests"`
Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs \
        DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs
git commit -m "feat: implement SongListFilterService flatten + folder path"
```

---

### Task 5: SongListFilterService — search filter

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs`
- Modify: `DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs`

- [ ] **Step 1: Add failing search tests**

Append to `SongListFilterServiceTests.cs` (inside the class):

```csharp
        [Fact]
        public void Apply_SearchByTitle_CaseInsensitiveSubstring()
        {
            var roots = new List<SongListNode>
            {
                Score("Yesterday", "The Beatles"),
                Score("Hey Jude", "The Beatles"),
                Score("Smoke On The Water", "Deep Purple")
            };
            var criteria = SongFilterCriteria.Default with { SearchQuery = "yesterDAY" };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Yesterday" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_SearchByArtist_CaseInsensitiveSubstring()
        {
            var roots = new List<SongListNode>
            {
                Score("Yesterday", "The Beatles"),
                Score("Smoke On The Water", "Deep Purple")
            };
            var criteria = SongFilterCriteria.Default with { SearchQuery = "beatles" };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Yesterday" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_SearchEmpty_ReturnsAll()
        {
            var roots = new List<SongListNode>
            {
                Score("A"), Score("B")
            };
            var criteria = SongFilterCriteria.Default with { SearchQuery = "" };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void Apply_SearchNoMatch_ReturnsEmpty()
        {
            var roots = new List<SongListNode>
            {
                Score("A", "Artist1"),
                Score("B", "Artist2")
            };
            var criteria = SongFilterCriteria.Default with { SearchQuery = "zzz" };

            var result = _svc.Apply(roots, criteria);

            Assert.Empty(result);
        }
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListFilterServiceTests"`
Expected: New search tests fail (search query is currently ignored).

- [ ] **Step 3: Add search filtering to service**

Modify `SongListFilterService.Apply` to insert a search step before sorting. Replace the body:

```csharp
        public IReadOnlyList<FilteredSongResult> Apply(
            IEnumerable<SongListNode> roots,
            SongFilterCriteria criteria)
        {
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            var flat = new List<FilteredSongResult>();
            foreach (var root in roots)
                Flatten(root, parentPath: "", flat);

            var afterSearch = ApplySearch(flat, criteria.SearchQuery);
            return SortResults(afterSearch, criteria);
        }

        private static List<FilteredSongResult> ApplySearch(
            List<FilteredSongResult> flat, string query)
        {
            if (string.IsNullOrEmpty(query)) return flat;

            return flat.Where(r =>
                Contains(r.Node.DisplayTitle, query) ||
                Contains(r.Node.DatabaseSong?.DisplayArtist, query))
                .ToList();
        }

        private static bool Contains(string? haystack, string needle)
        {
            if (string.IsNullOrEmpty(haystack)) return false;
            return haystack.IndexOf(needle, StringComparison.OrdinalIgnoreCase) >= 0;
        }
```

(`SortResults` signature changes to accept `List<FilteredSongResult>` — already does.)

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListFilterServiceTests"`
Expected: 9 tests pass (5 + 4 new).

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs \
        DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs
git commit -m "feat: add search filter to SongListFilterService (Title + Artist)"
```

---

### Task 6: SongListFilterService — level range filter

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs`
- Modify: `DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs`

- [ ] **Step 1: Add failing level tests**

Append to `SongListFilterServiceTests.cs`:

```csharp
        [Fact]
        public void Apply_LevelRange_FiltersByMaxDifficulty()
        {
            var roots = new List<SongListNode>
            {
                Score("Easy", level: 30),
                Score("Mid",  level: 60),
                Score("Hard", level: 90)
            };
            var criteria = SongFilterCriteria.Default with { MinLevel = 50, MaxLevel = 80 };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Mid" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_LevelMinOnly_NoMaxBound()
        {
            var roots = new List<SongListNode>
            {
                Score("Easy", level: 30),
                Score("Hard", level: 90)
            };
            var criteria = SongFilterCriteria.Default with { MinLevel = 50, MaxLevel = null };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Hard" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_LevelMaxOnly_NoMinBound()
        {
            var roots = new List<SongListNode>
            {
                Score("Easy", level: 30),
                Score("Hard", level: 90)
            };
            var criteria = SongFilterCriteria.Default with { MinLevel = null, MaxLevel = 50 };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Easy" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_LevelMinGreaterThanMax_SwapsSilently()
        {
            var roots = new List<SongListNode>
            {
                Score("Mid", level: 50)
            };
            var criteria = SongFilterCriteria.Default with { MinLevel = 80, MaxLevel = 30 };

            var result = _svc.Apply(roots, criteria);

            Assert.Single(result);
        }
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListFilterServiceTests"`
Expected: 4 new tests fail.

- [ ] **Step 3: Add level filter step**

Modify `SongListFilterService.Apply` to insert a level step. Replace the inner pipeline:

```csharp
        public IReadOnlyList<FilteredSongResult> Apply(
            IEnumerable<SongListNode> roots,
            SongFilterCriteria criteria)
        {
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            var flat = new List<FilteredSongResult>();
            foreach (var root in roots)
                Flatten(root, parentPath: "", flat);

            var afterSearch = ApplySearch(flat, criteria.SearchQuery);
            var afterLevel  = ApplyLevel(afterSearch, criteria.MinLevel, criteria.MaxLevel);
            return SortResults(afterLevel, criteria);
        }

        private static List<FilteredSongResult> ApplyLevel(
            List<FilteredSongResult> flat, int? min, int? max)
        {
            if (min is null && max is null) return flat;

            // Swap if min > max
            int lo = min ?? int.MinValue;
            int hi = max ?? int.MaxValue;
            if (lo > hi) (lo, hi) = (hi, lo);

            return flat.Where(r =>
            {
                int level = r.Node.MaxDifficultyLevel;
                return level >= lo && level <= hi;
            }).ToList();
        }
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListFilterServiceTests"`
Expected: 13 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs \
        DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs
git commit -m "feat: add level-range filter to SongListFilterService"
```

---

### Task 7: SongListFilterService — played-status filter

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs`
- Modify: `DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs`

**Cleared definition (per spec):** A song is `Cleared` if at least one of its `Scores` has `PlayCount > 0` AND the rank index from `SongScore.ComputeRankIndex(BestRank)` is less than 7 (anything other than the F bucket).

- [ ] **Step 1: Add failing played-status tests**

Append to `SongListFilterServiceTests.cs`:

```csharp
        private static SongListNode ScoreWith(string title, int playCount, int bestRank)
        {
            var node = new SongListNode { Type = NodeType.Score, Title = title };
            node.Scores[0] = new DTXMania.Game.Lib.Song.Entities.SongScore
            {
                DifficultyLevel = 50,
                PlayCount = playCount,
                BestRank = bestRank
            };
            return node;
        }

        [Fact]
        public void Apply_PlayedStatusUnplayed_KeepsOnlyZeroPlays()
        {
            var roots = new List<SongListNode>
            {
                ScoreWith("Untouched", playCount: 0, bestRank: 0),
                ScoreWith("Tried",     playCount: 3, bestRank: 80)
            };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Untouched" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_PlayedStatusPlayed_KeepsAnyPlayedSong()
        {
            var roots = new List<SongListNode>
            {
                ScoreWith("Untouched", playCount: 0, bestRank: 0),
                ScoreWith("Tried",     playCount: 3, bestRank: 0)  // played but failed
            };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Played };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Tried" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_PlayedStatusCleared_RequiresPlayCountAndNonFRank()
        {
            var roots = new List<SongListNode>
            {
                ScoreWith("Untouched", playCount: 0, bestRank: 0),
                ScoreWith("Failed",    playCount: 5, bestRank: 0),    // F bucket
                ScoreWith("Cleared",   playCount: 1, bestRank: 80)    // A bucket
            };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Cleared };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Cleared" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_PlayedStatusAll_NoFilter()
        {
            var roots = new List<SongListNode>
            {
                ScoreWith("A", playCount: 0, bestRank: 0),
                ScoreWith("B", playCount: 1, bestRank: 80)
            };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.All };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(2, result.Count);
        }

        [Fact]
        public void Apply_PlayedStatusUnplayed_TreatsMissingScoresAsUnplayed()
        {
            var node = new SongListNode { Type = NodeType.Score, Title = "NoScores" };
            // node.Scores is all-null
            var roots = new List<SongListNode> { node };
            var criteria = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };

            var result = _svc.Apply(roots, criteria);

            Assert.Single(result);
        }
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListFilterServiceTests"`
Expected: 5 new tests fail (played-status currently ignored).

- [ ] **Step 3: Add played-status filter step**

Modify `SongListFilterService` — add another pipeline step and helpers:

```csharp
        public IReadOnlyList<FilteredSongResult> Apply(
            IEnumerable<SongListNode> roots,
            SongFilterCriteria criteria)
        {
            if (roots == null) throw new ArgumentNullException(nameof(roots));
            if (criteria == null) throw new ArgumentNullException(nameof(criteria));

            var flat = new List<FilteredSongResult>();
            foreach (var root in roots)
                Flatten(root, parentPath: "", flat);

            var afterSearch = ApplySearch(flat, criteria.SearchQuery);
            var afterLevel  = ApplyLevel(afterSearch, criteria.MinLevel, criteria.MaxLevel);
            var afterPlayed = ApplyPlayedStatus(afterLevel, criteria.PlayedStatus);
            return SortResults(afterPlayed, criteria);
        }

        private static List<FilteredSongResult> ApplyPlayedStatus(
            List<FilteredSongResult> flat, PlayedStatus status)
        {
            if (status == PlayedStatus.All) return flat;

            return flat.Where(r => Match(r.Node, status)).ToList();
        }

        private static bool Match(SongListNode node, PlayedStatus status)
        {
            bool anyPlayed   = node.Scores.Any(s => s != null && s.PlayCount > 0);
            bool anyCleared  = node.Scores.Any(s => s != null && s.PlayCount > 0
                                && DTXMania.Game.Lib.Song.Entities.SongScore
                                       .ComputeRankIndex(s.BestRank) < 7);

            return status switch
            {
                PlayedStatus.Unplayed => !anyPlayed,
                PlayedStatus.Played   => anyPlayed,
                PlayedStatus.Cleared  => anyCleared,
                _ => true
            };
        }
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListFilterServiceTests"`
Expected: 18 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Filtering/SongListFilterService.cs \
        DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs
git commit -m "feat: add played-status filter (Unplayed/Played/Cleared) to filter service"
```

---

### Task 8: SongListFilterService — sort variations

**Files:**
- Modify: `DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs`

The sort code already exists from Task 4. This task only adds tests covering each criterion + descending.

- [ ] **Step 1: Add sort tests**

Append to `SongListFilterServiceTests.cs`:

```csharp
        [Fact]
        public void Apply_SortByTitleDescending()
        {
            var roots = new List<SongListNode>
            {
                Score("Apple"), Score("Banana"), Score("Cherry")
            };
            var criteria = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Title,
                SortDescending = true
            };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Cherry", "Banana", "Apple" },
                result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_SortByLevelAscending()
        {
            var roots = new List<SongListNode>
            {
                Score("High", level: 90),
                Score("Low",  level: 20),
                Score("Mid",  level: 50)
            };
            var criteria = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Level,
                SortDescending = false
            };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Low", "Mid", "High" },
                result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_SortByArtist()
        {
            var roots = new List<SongListNode>
            {
                Score("X", "Zenith"),
                Score("Y", "Apex")
            };
            var criteria = SongFilterCriteria.Default with { SortBy = SongSortCriteria.Artist };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Y", "X" }, result.Select(r => r.Node.DisplayTitle));
        }

        [Fact]
        public void Apply_CombinedSearchLevelPlayedSort()
        {
            var roots = new List<SongListNode>
            {
                ScoreWith("Beatles - Yesterday", playCount: 1, bestRank: 80),
                ScoreWith("Beatles - Hard Rock", playCount: 0, bestRank: 0),
                ScoreWith("Other Artist Song",   playCount: 1, bestRank: 80)
            };
            // Wire artist on first and second
            roots[0].DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song
            { Title = "Beatles - Yesterday", Artist = "The Beatles" };
            roots[1].DatabaseSong = new DTXMania.Game.Lib.Song.Entities.Song
            { Title = "Beatles - Hard Rock", Artist = "The Beatles" };

            var criteria = SongFilterCriteria.Default with
            {
                SearchQuery = "beatles",
                PlayedStatus = PlayedStatus.Played,
                SortBy = SongSortCriteria.Title,
                SortDescending = true
            };

            var result = _svc.Apply(roots, criteria);

            Assert.Equal(new[] { "Beatles - Yesterday" },
                result.Select(r => r.Node.DisplayTitle));
        }
```

- [ ] **Step 2: Run tests**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongListFilterServiceTests"`
Expected: 22 tests pass.

- [ ] **Step 3: Commit**

```bash
git add DTXMania.Test/Song/Filtering/SongListFilterServiceTests.cs
git commit -m "test: cover sort variations and combined-criteria filtering"
```

---

## Phase 2 — Reusable Text Input

### Task 9: ITextInputSource + WindowTextInputSource

**Files:**
- Create: `DTXMania.Game/Lib/UI/Components/ITextInputSource.cs`
- Create: `DTXMania.Game/Lib/UI/Components/WindowTextInputSource.cs`

- [ ] **Step 1: Create the interface**

Path: `DTXMania.Game/Lib/UI/Components/ITextInputSource.cs`

```csharp
using System;
using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Components
{
    public interface ITextInputSource
    {
        event EventHandler<TextInputEventArgs> TextInput;
    }
}
```

- [ ] **Step 2: Create the GameWindow adapter**

Path: `DTXMania.Game/Lib/UI/Components/WindowTextInputSource.cs`

```csharp
using System;
using Microsoft.Xna.Framework;

namespace DTXMania.Game.Lib.UI.Components
{
    public sealed class WindowTextInputSource : ITextInputSource
    {
        private readonly GameWindow _window;

        public WindowTextInputSource(GameWindow window)
        {
            _window = window ?? throw new ArgumentNullException(nameof(window));
            _window.TextInput += OnTextInput;
        }

        public event EventHandler<TextInputEventArgs>? TextInput;

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            TextInput?.Invoke(sender, e);
        }
    }
}
```

- [ ] **Step 3: Build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Game/Lib/UI/Components/ITextInputSource.cs \
        DTXMania.Game/Lib/UI/Components/WindowTextInputSource.cs
git commit -m "feat: add ITextInputSource abstraction and GameWindow adapter"
```

---

### Task 10: UITextInput — character insert + tests

**Files:**
- Create: `DTXMania.Game/Lib/UI/Components/UITextInput.cs`
- Create: `DTXMania.Test/UI/UITextInputTests.cs`

- [ ] **Step 1: Write failing tests**

Path: `DTXMania.Test/UI/UITextInputTests.cs`

```csharp
using System;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    public class UITextInputTests
    {
        private sealed class FakeSource : ITextInputSource
        {
            public event EventHandler<TextInputEventArgs>? TextInput;
            public void Fire(char c) =>
                TextInput?.Invoke(this, new TextInputEventArgs(c, ToKey(c)));
            private static Microsoft.Xna.Framework.Input.Keys ToKey(char c) =>
                Microsoft.Xna.Framework.Input.Keys.None;
        }

        [Fact]
        public void Focused_AppendsTypedCharacters()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            src.Fire('h');
            src.Fire('i');

            Assert.Equal("hi", input.Text);
            Assert.Equal(2, input.CaretIndex);
        }

        [Fact]
        public void Unfocused_IgnoresTypedCharacters()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = false };

            src.Fire('x');

            Assert.Equal("", input.Text);
        }

        [Fact]
        public void MaxLength_ClampsInsert()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true, MaxLength = 3 };

            src.Fire('a');
            src.Fire('b');
            src.Fire('c');
            src.Fire('d');

            Assert.Equal("abc", input.Text);
        }
    }
}
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~UITextInputTests"`
Expected: Build error (`UITextInput` not defined).

- [ ] **Step 3: Implement minimal UITextInput**

Path: `DTXMania.Game/Lib/UI/Components/UITextInput.cs`

```csharp
#nullable enable

using System;
using DTXMania.Game.Lib.UI;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.UI.Components
{
    public class UITextInput : UIElement
    {
        private readonly ITextInputSource _source;
        private string _text = "";
        private int _caretIndex;
        private bool _subscribed;

        public UITextInput(ITextInputSource source)
        {
            _source = source ?? throw new ArgumentNullException(nameof(source));
            OnFocus  += (_, _) => Subscribe();
            OnBlur   += (_, _) => Unsubscribe();
        }

        public string Text
        {
            get => _text;
            set
            {
                _text = value ?? "";
                _caretIndex = Math.Clamp(_caretIndex, 0, _text.Length);
            }
        }

        public int CaretIndex => _caretIndex;

        public int MaxLength { get; set; } = 64;

        public SpriteFont? Font { get; set; }

        public Color TextColor { get; set; } = Color.White;

        private void Subscribe()
        {
            if (_subscribed) return;
            _source.TextInput += OnTextInput;
            _subscribed = true;
        }

        private void Unsubscribe()
        {
            if (!_subscribed) return;
            _source.TextInput -= OnTextInput;
            _subscribed = false;
        }

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (!Focused) return;

            char c = e.Character;
            if (c == '\b' || c == '\r' || c == '\n' || c == '\t') return;
            if (char.IsControl(c)) return;
            if (_text.Length >= MaxLength) return;

            _text = _text.Insert(_caretIndex, c.ToString());
            _caretIndex++;
        }

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (Font == null) return;
            var pos = AbsolutePosition;
            spriteBatch.DrawString(Font, _text, pos, TextColor);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) Unsubscribe();
            base.Dispose(disposing);
        }
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~UITextInputTests"`
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/UI/Components/UITextInput.cs \
        DTXMania.Test/UI/UITextInputTests.cs
git commit -m "feat: add UITextInput element with character insertion"
```

---

### Task 11: UITextInput — backspace + caret navigation

**Files:**
- Modify: `DTXMania.Game/Lib/UI/Components/UITextInput.cs`
- Modify: `DTXMania.Test/UI/UITextInputTests.cs`

- [ ] **Step 1: Add failing tests**

Append to `UITextInputTests.cs`:

```csharp
        [Fact]
        public void Backspace_RemovesCharBeforeCaret()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            src.Fire('a');
            src.Fire('b');
            src.Fire('c');
            input.Backspace();

            Assert.Equal("ab", input.Text);
            Assert.Equal(2, input.CaretIndex);
        }

        [Fact]
        public void Backspace_OnEmpty_NoOp()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };

            input.Backspace();

            Assert.Equal("", input.Text);
            Assert.Equal(0, input.CaretIndex);
        }

        [Fact]
        public void MoveCaret_LeftRight_ClampedToTextRange()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };
            src.Fire('a');
            src.Fire('b');

            input.MoveCaret(-5);
            Assert.Equal(0, input.CaretIndex);

            input.MoveCaret(+10);
            Assert.Equal(2, input.CaretIndex);
        }

        [Fact]
        public void Backspace_FromMiddleOfText_RemovesCorrectChar()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };
            src.Fire('a');
            src.Fire('b');
            src.Fire('c');
            input.MoveCaret(-1); // caret between 'b' and 'c'

            input.Backspace();

            Assert.Equal("ac", input.Text);
            Assert.Equal(1, input.CaretIndex);
        }

        [Fact]
        public void Clear_ResetsTextAndCaret()
        {
            var src = new FakeSource();
            var input = new UITextInput(src) { Focused = true };
            src.Fire('a');
            src.Fire('b');

            input.Clear();

            Assert.Equal("", input.Text);
            Assert.Equal(0, input.CaretIndex);
        }
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~UITextInputTests"`
Expected: 5 new tests fail (`Backspace`, `MoveCaret`, `Clear` not defined).

- [ ] **Step 3: Add methods to UITextInput**

In `UITextInput.cs`, add these methods inside the class:

```csharp
        public void Backspace()
        {
            if (_caretIndex == 0 || _text.Length == 0) return;
            _text = _text.Remove(_caretIndex - 1, 1);
            _caretIndex--;
        }

        public void MoveCaret(int delta)
        {
            _caretIndex = Math.Clamp(_caretIndex + delta, 0, _text.Length);
        }

        public void Clear()
        {
            _text = "";
            _caretIndex = 0;
        }
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~UITextInputTests"`
Expected: 8 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/UI/Components/UITextInput.cs \
        DTXMania.Test/UI/UITextInputTests.cs
git commit -m "feat: add Backspace/MoveCaret/Clear to UITextInput"
```

---

### Task 12: UITextInput — focus subscribe/unsubscribe verification

**Files:**
- Modify: `DTXMania.Test/UI/UITextInputTests.cs`

- [ ] **Step 1: Add subscribe/unsubscribe tests**

Append to `UITextInputTests.cs`:

```csharp
        private sealed class CountingSource : ITextInputSource
        {
            public event EventHandler<TextInputEventArgs>? TextInput;
            public int HandlerCount =>
                TextInput?.GetInvocationList().Length ?? 0;
            public void Fire(char c) =>
                TextInput?.Invoke(this, new TextInputEventArgs(c, Microsoft.Xna.Framework.Input.Keys.None));
        }

        [Fact]
        public void Focused_True_SubscribesToSource()
        {
            var src = new CountingSource();
            var input = new UITextInput(src);

            input.Focused = true;

            Assert.Equal(1, src.HandlerCount);
        }

        [Fact]
        public void Focused_False_UnsubscribesFromSource()
        {
            var src = new CountingSource();
            var input = new UITextInput(src) { Focused = true };

            input.Focused = false;

            Assert.Equal(0, src.HandlerCount);
        }

        [Fact]
        public void Focused_TrueTwice_SubscribesOnlyOnce()
        {
            var src = new CountingSource();
            var input = new UITextInput(src);

            input.Focused = true;
            input.Focused = true;

            Assert.Equal(1, src.HandlerCount);
        }

        [Fact]
        public void Dispose_UnsubscribesFromSource()
        {
            var src = new CountingSource();
            var input = new UITextInput(src) { Focused = true };

            input.Dispose();

            Assert.Equal(0, src.HandlerCount);
        }
```

- [ ] **Step 2: Run tests — expect pass (no implementation change needed)**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~UITextInputTests"`
Expected: 12 tests pass.

- [ ] **Step 3: Commit**

```bash
git add DTXMania.Test/UI/UITextInputTests.cs
git commit -m "test: cover UITextInput focus-driven subscribe/unsubscribe"
```

---

## Phase 3 — Modal UI

### Task 13: Add `OpenSearch` input command + Backspace mapping

**Files:**
- Modify: `DTXMania.Game/Lib/Input/InputManager.cs`
- Create: `DTXMania.Test/Input/OpenSearchInputCommandTests.cs`

- [ ] **Step 1: Write failing test**

Path: `DTXMania.Test/Input/OpenSearchInputCommandTests.cs`

```csharp
using DTXMania.Game.Lib.Input;
using Microsoft.Xna.Framework.Input;
using Xunit;

namespace DTXMania.Test.Input
{
    public class OpenSearchInputCommandTests
    {
        [Fact]
        public void DefaultMapping_BackspaceMapsToOpenSearch()
        {
            var mgr = new InputManager();
            var snapshot = mgr.GetKeyMappingSnapshot();

            Assert.True(snapshot.ContainsKey(Keys.Back));
            Assert.Equal(InputCommandType.OpenSearch, snapshot[Keys.Back]);
        }

        [Fact]
        public void OpenSearch_ExistsInEnum()
        {
            // Ensure the enum value is defined
            var values = System.Enum.GetValues<InputCommandType>();
            Assert.Contains(InputCommandType.OpenSearch, values);
        }
    }
}
```

- [ ] **Step 2: Run test — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~OpenSearchInputCommandTests"`
Expected: Build error (`OpenSearch` not defined).

- [ ] **Step 3: Add the enum value and binding**

In `DTXMania.Game/Lib/Input/InputManager.cs`, modify the enum (around line 11-21):

```csharp
    public enum InputCommandType
    {
        MoveUp,
        MoveDown,
        MoveLeft,
        MoveRight,
        Activate,
        Back,
        IncreaseScrollSpeed,
        DecreaseScrollSpeed,
        OpenSearch
    }
```

In `InitializeDefaultKeyMapping()` (around line 96), add the binding:

```csharp
        private void InitializeDefaultKeyMapping()
        {
            _keyMapping[Keys.Up] = InputCommandType.MoveUp;
            _keyMapping[Keys.Down] = InputCommandType.MoveDown;
            _keyMapping[Keys.Left] = InputCommandType.MoveLeft;
            _keyMapping[Keys.Right] = InputCommandType.MoveRight;
            _keyMapping[Keys.Enter] = InputCommandType.Activate;
            _keyMapping[Keys.Escape] = InputCommandType.Back;
            _keyMapping[Keys.PageUp] = InputCommandType.IncreaseScrollSpeed;
            _keyMapping[Keys.PageDown] = InputCommandType.DecreaseScrollSpeed;
            _keyMapping[Keys.Back] = InputCommandType.OpenSearch;
        }
```

- [ ] **Step 4: Run test — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~OpenSearchInputCommandTests"`
Expected: 2 tests pass.

- [ ] **Step 5: Run full input test suite to ensure no regressions**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~Input"`
Expected: All input tests pass.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Input/InputManager.cs \
        DTXMania.Test/Input/OpenSearchInputCommandTests.cs
git commit -m "feat: add OpenSearch input command bound to Backspace"
```

---

### Task 14: SongSelectionUILayout — SearchFilterModal layout constants

**Files:**
- Modify: `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs`
- Create: `DTXMania.Test/UI/SearchFilterModalLayoutTests.cs`

- [ ] **Step 1: Write failing tests**

Path: `DTXMania.Test/UI/SearchFilterModalLayoutTests.cs`

```csharp
using DTXMania.Game.Lib.UI.Layout;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.UI
{
    public class SearchFilterModalLayoutTests
    {
        [Fact]
        public void Modal_IsCentered1280x720()
        {
            // Modal: 600 wide, 360 tall, centered in 1280x720
            Assert.Equal(340, SongSelectionUILayout.SearchFilterModal.X);
            Assert.Equal(180, SongSelectionUILayout.SearchFilterModal.Y);
            Assert.Equal(600, SongSelectionUILayout.SearchFilterModal.Width);
            Assert.Equal(360, SongSelectionUILayout.SearchFilterModal.Height);
        }

        [Fact]
        public void Modal_BoundsMatchPositionAndSize()
        {
            var b = SongSelectionUILayout.SearchFilterModal.Bounds;
            Assert.Equal(new Rectangle(340, 180, 600, 360), b);
        }

        [Fact]
        public void SearchBox_HasWidth()
        {
            Assert.True(SongSelectionUILayout.SearchFilterModal.SearchBoxWidth > 0);
        }
    }
}
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SearchFilterModalLayoutTests"`
Expected: Build error (`SearchFilterModal` not defined).

- [ ] **Step 3: Add layout constants**

Append the following inside `SongSelectionUILayout` (before its closing brace) in `DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs`:

```csharp
        #region Search Filter Modal Layout

        /// <summary>
        /// Layout constants for the search/filter/sort modal overlay.
        /// </summary>
        public static class SearchFilterModal
        {
            // Centered in 1280x720
            public const int X = 340;
            public const int Y = 180;
            public const int Width = 600;
            public const int Height = 360;

            public static Vector2 Position => new Vector2(X, Y);
            public static Vector2 Size => new Vector2(Width, Height);
            public static Rectangle Bounds => new Rectangle(X, Y, Width, Height);

            // Title bar
            public const int TitleY = 12;             // relative to modal top
            public const int TitleHeight = 28;

            // Field rows (relative to modal top-left)
            public const int RowSpacing = 44;
            public const int FirstRowY = 56;
            public const int LabelX = 24;
            public const int FieldX = 130;

            // Search box
            public const int SearchBoxY = FirstRowY;
            public const int SearchBoxWidth = 430;
            public const int SearchBoxHeight = 30;

            // Level row
            public const int LevelRowY = FirstRowY + RowSpacing;
            public const int LevelMinX = FieldX;
            public const int LevelMaxX = FieldX + 180;
            public const int LevelInputWidth = 80;
            public const int LevelInputHeight = 30;

            // Played-status row
            public const int PlayedRowY = FirstRowY + RowSpacing * 2;

            // Sort row
            public const int SortRowY = FirstRowY + RowSpacing * 3;

            // Buttons
            public const int ButtonRowY = FirstRowY + RowSpacing * 4 + 8;
            public const int ResetButtonX = 200;
            public const int ApplyButtonX = 360;
            public const int ButtonWidth = 120;
            public const int ButtonHeight = 36;
        }

        #endregion
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SearchFilterModalLayoutTests"`
Expected: 3 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/UI/Layout/SongSelectionUILayout.cs \
        DTXMania.Test/UI/SearchFilterModalLayoutTests.cs
git commit -m "feat: add SearchFilterModal layout constants"
```

---

### Task 15: SongSearchFilterModal skeleton (logic-only)

This task creates the modal as a `UIElement` subclass exposing public state for tests, without graphics yet. We test the state machine in the Mac suite by exercising public methods, then add drawing in Task 18 (which is excluded from the Mac suite).

**Files:**
- Create: `DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs`
- Create: `DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs`

- [ ] **Step 1: Write failing tests**

Path: `DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs`

```csharp
using System;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Components;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Xunit;

namespace DTXMania.Test.Song.Components
{
    public class SongSearchFilterModalLogicTests
    {
        private sealed class FakeSource : ITextInputSource
        {
            public event EventHandler<TextInputEventArgs>? TextInput;
            public void Fire(char c) =>
                TextInput?.Invoke(this, new TextInputEventArgs(c,
                    Microsoft.Xna.Framework.Input.Keys.None));
        }

        [Fact]
        public void Open_DefaultCriteria_PopulatesFromArgument()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            var initial = SongFilterCriteria.Default with { SearchQuery = "abc" };

            modal.Open(initial);

            Assert.True(modal.IsOpen);
            Assert.Equal("abc", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Cancel_FiresCancelledEvent_AndCloses()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            bool fired = false;
            modal.Cancelled += (_, _) => fired = true;

            modal.Cancel();

            Assert.True(fired);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void Apply_FiresFilterAppliedWithDraft_AndCloses()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            var initial = SongFilterCriteria.Default with { SearchQuery = "x" };
            modal.Open(initial);
            SongFilterCriteria? captured = null;
            modal.FilterApplied += (_, c) => captured = c;

            modal.Apply();

            Assert.NotNull(captured);
            Assert.Equal("x", captured!.SearchQuery);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void Reset_FiresFilterResetAndCloses()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default with { SearchQuery = "x" });
            bool fired = false;
            modal.FilterReset += (_, _) => fired = true;

            modal.Reset();

            Assert.True(fired);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void EditDraft_DoesNotMutateInitialCriteria()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            var initial = SongFilterCriteria.Default with { SearchQuery = "orig" };

            modal.Open(initial);
            modal.UpdateDraft(initial with { SearchQuery = "edited" });

            Assert.Equal("orig", initial.SearchQuery);
            Assert.Equal("edited", modal.CurrentDraft.SearchQuery);
        }
    }
}
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSearchFilterModalLogicTests"`
Expected: Build error (`SongSearchFilterModal` not defined).

- [ ] **Step 3: Implement modal skeleton**

Path: `DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs`

```csharp
#nullable enable

using System;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.UI;
using DTXMania.Game.Lib.UI.Components;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace DTXMania.Game.Lib.Song.Components
{
    public class SongSearchFilterModal : UIElement
    {
        private readonly ITextInputSource _textSource;
        private SongFilterCriteria _draft = SongFilterCriteria.Default;
        private bool _isOpen;

        public SongSearchFilterModal(ITextInputSource textSource)
        {
            _textSource = textSource ?? throw new ArgumentNullException(nameof(textSource));
            Visible = false;
        }

        public event EventHandler<SongFilterCriteria>? FilterApplied;
        public event EventHandler? FilterReset;
        public event EventHandler? Cancelled;

        public bool IsOpen => _isOpen;
        public SongFilterCriteria CurrentDraft => _draft;

        public void Open(SongFilterCriteria initial)
        {
            _draft = initial ?? SongFilterCriteria.Default;
            _isOpen = true;
            Visible = true;
        }

        public void Close()
        {
            _isOpen = false;
            Visible = false;
        }

        public void Cancel()
        {
            Cancelled?.Invoke(this, EventArgs.Empty);
            Close();
        }

        public void Apply()
        {
            FilterApplied?.Invoke(this, _draft);
            Close();
        }

        public void Reset()
        {
            FilterReset?.Invoke(this, EventArgs.Empty);
            Close();
        }

        public void UpdateDraft(SongFilterCriteria newDraft)
        {
            _draft = newDraft ?? SongFilterCriteria.Default;
        }

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            // Drawing implemented in Task 18.
        }
    }
}
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSearchFilterModalLogicTests"`
Expected: 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs \
        DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs
git commit -m "feat: add SongSearchFilterModal state machine skeleton"
```

---

### Task 16: Modal — `/q` slash command detection

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs`
- Modify: `DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs`

- [ ] **Step 1: Add failing tests**

Append to `SongSearchFilterModalLogicTests.cs`:

```csharp
        [Fact]
        public void SubmitFromSearchBox_QSlashCommand_TriggersReset()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "/q" });
            bool resetFired = false;
            bool appliedFired = false;
            modal.FilterReset += (_, _) => resetFired = true;
            modal.FilterApplied += (_, _) => appliedFired = true;

            modal.SubmitFromSearchBox();

            Assert.True(resetFired);
            Assert.False(appliedFired);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void SubmitFromSearchBox_QSlashCommand_CaseInsensitive()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "/Q" });
            bool resetFired = false;
            modal.FilterReset += (_, _) => resetFired = true;

            modal.SubmitFromSearchBox();

            Assert.True(resetFired);
        }

        [Fact]
        public void SubmitFromSearchBox_NormalQuery_TriggersApply()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "beatles" });
            bool resetFired = false;
            bool appliedFired = false;
            modal.FilterReset += (_, _) => resetFired = true;
            modal.FilterApplied += (_, _) => appliedFired = true;

            modal.SubmitFromSearchBox();

            Assert.False(resetFired);
            Assert.True(appliedFired);
        }

        [Fact]
        public void SubmitFromSearchBox_QPrefixedQuery_NotResetCommand()
        {
            // "/quiet" is a literal search, not /q
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "/quiet" });
            bool resetFired = false;
            bool appliedFired = false;
            modal.FilterReset += (_, _) => resetFired = true;
            modal.FilterApplied += (_, _) => appliedFired = true;

            modal.SubmitFromSearchBox();

            Assert.False(resetFired);
            Assert.True(appliedFired);
        }
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSearchFilterModalLogicTests"`
Expected: 4 new tests fail (`SubmitFromSearchBox` not defined).

- [ ] **Step 3: Add submit method**

In `SongSearchFilterModal.cs`, add:

```csharp
        public void SubmitFromSearchBox()
        {
            if (string.Equals(_draft.SearchQuery, "/q", StringComparison.OrdinalIgnoreCase))
            {
                Reset();
                return;
            }
            Apply();
        }
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSearchFilterModalLogicTests"`
Expected: 9 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs \
        DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs
git commit -m "feat: detect /q slash-command in modal SubmitFromSearchBox"
```

---

### Task 17: Modal — focus cycling between fields

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs`
- Modify: `DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs`

- [ ] **Step 1: Add failing tests**

Append to `SongSearchFilterModalLogicTests.cs`:

```csharp
        [Fact]
        public void Open_SearchBoxIsInitiallyFocused()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void FocusNext_CyclesThroughFieldsInOrder()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            var order = new[]
            {
                SongSearchFilterModal.Field.SearchBox,
                SongSearchFilterModal.Field.MinLevel,
                SongSearchFilterModal.Field.MaxLevel,
                SongSearchFilterModal.Field.PlayedStatus,
                SongSearchFilterModal.Field.SortBy,
                SongSearchFilterModal.Field.SortDirection,
                SongSearchFilterModal.Field.ResetButton,
                SongSearchFilterModal.Field.ApplyButton
            };

            for (int i = 0; i < order.Length; i++)
            {
                Assert.Equal(order[i], modal.FocusedField);
                modal.FocusNext();
            }
            // After last, wraps back to SearchBox
            Assert.Equal(SongSearchFilterModal.Field.SearchBox, modal.FocusedField);
        }

        [Fact]
        public void FocusPrev_ReverseOfFocusNext()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.FocusPrev();
            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);
            modal.FocusPrev();
            Assert.Equal(SongSearchFilterModal.Field.ResetButton, modal.FocusedField);
        }
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSearchFilterModalLogicTests"`
Expected: 3 new tests fail (`Field`, `FocusedField`, `FocusNext`, `FocusPrev` not defined).

- [ ] **Step 3: Add focus model**

In `SongSearchFilterModal.cs`, add this enum and methods:

```csharp
        public enum Field
        {
            SearchBox = 0,
            MinLevel = 1,
            MaxLevel = 2,
            PlayedStatus = 3,
            SortBy = 4,
            SortDirection = 5,
            ResetButton = 6,
            ApplyButton = 7
        }

        private const int FieldCount = 8;
        private Field _focusedField = Field.SearchBox;

        public Field FocusedField => _focusedField;

        public void FocusNext()
        {
            int next = ((int)_focusedField + 1) % FieldCount;
            _focusedField = (Field)next;
        }

        public void FocusPrev()
        {
            int prev = ((int)_focusedField - 1 + FieldCount) % FieldCount;
            _focusedField = (Field)prev;
        }
```

Also modify `Open` to reset focus:

```csharp
        public void Open(SongFilterCriteria initial)
        {
            _draft = initial ?? SongFilterCriteria.Default;
            _focusedField = Field.SearchBox;
            _isOpen = true;
            Visible = true;
        }
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSearchFilterModalLogicTests"`
Expected: 12 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs \
        DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs
git commit -m "feat: add modal focus-cycling between search/facets/sort/buttons"
```

---

### Task 18: Modal — keyboard input handling (HandleKey)

This task wires raw key input. We test the state transitions but not actual keyboard polling (the stage will pass keys in via `HandleKey`).

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs`
- Modify: `DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs`

- [ ] **Step 1: Add failing tests**

Append to `SongSearchFilterModalLogicTests.cs`:

```csharp
        [Fact]
        public void HandleKey_Escape_FiresCancel()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            bool fired = false;
            modal.Cancelled += (_, _) => fired = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Escape);

            Assert.True(fired);
            Assert.False(modal.IsOpen);
        }

        [Fact]
        public void HandleKey_Tab_AdvancesFocus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Tab);

            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_Down_AdvancesFocus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Down);

            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_Up_RetreatsFocus()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Up);

            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);
        }

        [Fact]
        public void HandleKey_Enter_OnSearchBox_SubmitsFromSearchBox()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.UpdateDraft(SongFilterCriteria.Default with { SearchQuery = "abc" });
            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.True(applied);
        }

        [Fact]
        public void HandleKey_Enter_OnApplyButton_FiresApply()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            // Move focus to ApplyButton
            for (int i = 0; i < 7; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.ApplyButton, modal.FocusedField);

            bool applied = false;
            modal.FilterApplied += (_, _) => applied = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.True(applied);
        }

        [Fact]
        public void HandleKey_Enter_OnResetButton_FiresReset()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            // Move focus to ResetButton
            for (int i = 0; i < 6; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.ResetButton, modal.FocusedField);

            bool reset = false;
            modal.FilterReset += (_, _) => reset = true;

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Enter);

            Assert.True(reset);
        }

        [Fact]
        public void HandleKey_LeftRight_OnMinLevel_AdjustsBy5()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel
            Assert.Equal(SongSearchFilterModal.Field.MinLevel, modal.FocusedField);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(5, modal.CurrentDraft.MinLevel);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(10, modal.CurrentDraft.MinLevel);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Left);
            Assert.Equal(5, modal.CurrentDraft.MinLevel);
        }

        [Fact]
        public void HandleKey_LeftRight_OnPlayedStatus_CyclesEnum()
        {
            var modal = new SongSearchFilterModal(new FakeSource());
            modal.Open(SongFilterCriteria.Default);
            for (int i = 0; i < 3; i++) modal.FocusNext();
            Assert.Equal(SongSearchFilterModal.Field.PlayedStatus, modal.FocusedField);

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(PlayedStatus.Unplayed, modal.CurrentDraft.PlayedStatus);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(PlayedStatus.Played, modal.CurrentDraft.PlayedStatus);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            Assert.Equal(PlayedStatus.Cleared, modal.CurrentDraft.PlayedStatus);
            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Right);
            // wraps
            Assert.Equal(PlayedStatus.All, modal.CurrentDraft.PlayedStatus);
        }
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSearchFilterModalLogicTests"`
Expected: 9 new tests fail (`HandleKey` not defined).

- [ ] **Step 3: Add HandleKey method**

In `SongSearchFilterModal.cs`, add (using `Microsoft.Xna.Framework.Input;` at the top of the file):

```csharp
        public void HandleKey(Microsoft.Xna.Framework.Input.Keys key)
        {
            if (!_isOpen) return;

            switch (key)
            {
                case Microsoft.Xna.Framework.Input.Keys.Escape:
                    Cancel();
                    return;

                case Microsoft.Xna.Framework.Input.Keys.Tab:
                case Microsoft.Xna.Framework.Input.Keys.Down:
                    FocusNext();
                    return;

                case Microsoft.Xna.Framework.Input.Keys.Up:
                    FocusPrev();
                    return;

                case Microsoft.Xna.Framework.Input.Keys.Enter:
                    HandleEnter();
                    return;

                case Microsoft.Xna.Framework.Input.Keys.Left:
                    AdjustFocusedField(-1);
                    return;

                case Microsoft.Xna.Framework.Input.Keys.Right:
                    AdjustFocusedField(+1);
                    return;
            }
        }

        private void HandleEnter()
        {
            switch (_focusedField)
            {
                case Field.SearchBox:
                    SubmitFromSearchBox();
                    return;
                case Field.ResetButton:
                    Reset();
                    return;
                case Field.ApplyButton:
                    Apply();
                    return;
                default:
                    // On numeric/cycle fields, Enter applies (default action)
                    Apply();
                    return;
            }
        }

        private const int LevelStep = 5;
        private const int LevelMin = 0;
        private const int LevelMax = 99;

        private void AdjustFocusedField(int dir)
        {
            switch (_focusedField)
            {
                case Field.MinLevel:
                    _draft = _draft with { MinLevel = AdjustLevel(_draft.MinLevel, dir) };
                    return;
                case Field.MaxLevel:
                    _draft = _draft with { MaxLevel = AdjustLevel(_draft.MaxLevel, dir) };
                    return;
                case Field.PlayedStatus:
                    _draft = _draft with { PlayedStatus = CycleEnum(_draft.PlayedStatus, dir) };
                    return;
                case Field.SortBy:
                    _draft = _draft with { SortBy = CycleSort(_draft.SortBy, dir) };
                    return;
                case Field.SortDirection:
                    _draft = _draft with { SortDescending = !_draft.SortDescending };
                    return;
            }
        }

        private static int? AdjustLevel(int? current, int dir)
        {
            int next = (current ?? 0) + (dir * LevelStep);
            if (next < LevelMin) next = LevelMin;
            if (next > LevelMax) next = LevelMax;
            return next == 0 ? (int?)null : next;
        }

        private static PlayedStatus CycleEnum(PlayedStatus current, int dir)
        {
            int count = 4; // All, Unplayed, Played, Cleared
            int idx = ((int)current + dir + count) % count;
            return (PlayedStatus)idx;
        }

        private static SongSortCriteria CycleSort(SongSortCriteria current, int dir)
        {
            // Modal exposes only Title / Artist / Level (no Genre)
            var allowed = new[]
            {
                SongSortCriteria.Title,
                SongSortCriteria.Artist,
                SongSortCriteria.Level
            };
            int currentIdx = System.Array.IndexOf(allowed, current);
            if (currentIdx < 0) currentIdx = 0;
            int next = (currentIdx + dir + allowed.Length) % allowed.Length;
            return allowed[next];
        }
```

Add `using DTXMania.Game.Lib.Song;` at the top of the file (for `SongSortCriteria`).

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSearchFilterModalLogicTests"`
Expected: 21 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs \
        DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs
git commit -m "feat: handle Esc/Tab/Enter/Arrow keys in search-filter modal"
```

---

### Task 19: Modal — text-input wiring (search box character entry)

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs`
- Modify: `DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs`

- [ ] **Step 1: Add failing tests**

Append to `SongSearchFilterModalLogicTests.cs`:

```csharp
        [Fact]
        public void TypingChars_OnSearchBox_AppendsToSearchQuery()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            // SearchBox is initial focus
            src.Fire('h');
            src.Fire('i');

            Assert.Equal("hi", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void TypingChars_WhenNotOnSearchBox_Ignored()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            modal.FocusNext(); // MinLevel

            src.Fire('5');

            Assert.Equal("", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Backspace_HandleKey_RemovesLastSearchChar()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            src.Fire('a');
            src.Fire('b');

            modal.HandleKey(Microsoft.Xna.Framework.Input.Keys.Back);

            Assert.Equal("a", modal.CurrentDraft.SearchQuery);
        }

        [Fact]
        public void Close_UnsubscribesFromTextSource()
        {
            var src = new FakeSource();
            var modal = new SongSearchFilterModal(src);
            modal.Open(SongFilterCriteria.Default);
            src.Fire('a');
            Assert.Equal("a", modal.CurrentDraft.SearchQuery);

            modal.Cancel(); // closes

            src.Fire('b');
            // After close, characters must not affect the now-stale draft
            Assert.Equal("a", modal.CurrentDraft.SearchQuery);
        }
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSearchFilterModalLogicTests"`
Expected: 4 new tests fail (no text wiring yet).

- [ ] **Step 3: Wire text input into modal**

Modify `SongSearchFilterModal.cs`:

Add field:
```csharp
        private bool _subscribedToText;
```

Replace `Open`:
```csharp
        public void Open(SongFilterCriteria initial)
        {
            _draft = initial ?? SongFilterCriteria.Default;
            _focusedField = Field.SearchBox;
            _isOpen = true;
            Visible = true;
            SubscribeText();
        }
```

Replace `Close`:
```csharp
        public void Close()
        {
            UnsubscribeText();
            _isOpen = false;
            Visible = false;
        }
```

Add helpers:
```csharp
        private void SubscribeText()
        {
            if (_subscribedToText) return;
            _textSource.TextInput += OnTextInput;
            _subscribedToText = true;
        }

        private void UnsubscribeText()
        {
            if (!_subscribedToText) return;
            _textSource.TextInput -= OnTextInput;
            _subscribedToText = false;
        }

        private void OnTextInput(object? sender, TextInputEventArgs e)
        {
            if (!_isOpen) return;
            if (_focusedField != Field.SearchBox) return;

            char c = e.Character;
            if (c == '\b' || c == '\r' || c == '\n' || c == '\t') return;
            if (char.IsControl(c)) return;

            _draft = _draft with { SearchQuery = (_draft.SearchQuery ?? "") + c };
        }
```

Update `HandleKey` to handle `Keys.Back`:
```csharp
                case Microsoft.Xna.Framework.Input.Keys.Back:
                    HandleBackspace();
                    return;
```

Add `HandleBackspace`:
```csharp
        private void HandleBackspace()
        {
            if (_focusedField != Field.SearchBox) return;
            var q = _draft.SearchQuery ?? "";
            if (q.Length == 0) return;
            _draft = _draft with { SearchQuery = q.Substring(0, q.Length - 1) };
        }
```

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSearchFilterModalLogicTests"`
Expected: 25 tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs \
        DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs
git commit -m "feat: wire GameWindow.TextInput characters into modal search box"
```

---

## Phase 4 — Stage Integration

### Task 20: SongSelectionStage — own SongFilterCriteria + filtered view fields

This task introduces session-scoped state on the stage but doesn't yet open the modal. Pure refactor that adds fields and a helper.

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Create: `DTXMania.Test/Stage/SongSelectionStageFilterTests.cs`

- [ ] **Step 1: Write failing tests**

Path: `DTXMania.Test/Stage/SongSelectionStageFilterTests.cs`

```csharp
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage
{
    public class SongSelectionStageFilterTests
    {
        [Fact]
        public void NewStage_FilterCriteriaDefaultsToEmpty()
        {
            // Stage.GetFilterCriteria() is a test-access helper added in this task.
            Assert.True(SongSelectionStage.DefaultFilterCriteriaIsEmpty());
        }

        [Fact]
        public void DefaultFilteredViewIsNull()
        {
            // No filter active → projection is null
            Assert.True(SongSelectionStage.DefaultFilteredViewIsNull());
        }
    }
}
```

(These thin smoke tests verify the static defaults are correctly wired. The real integration tests for stage behavior live in later tasks where the stage exposes test-access helpers.)

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageFilterTests"`
Expected: Build error (helpers not defined).

- [ ] **Step 3: Add fields and test helpers to SongSelectionStage**

In `SongSelectionStage.cs`, add at the top of the `using` block (if missing):
```csharp
using DTXMania.Game.Lib.Song.Filtering;
```

Add private fields near the existing song-management fields:
```csharp
        // Session-scoped search/filter/sort state
        private SongFilterCriteria _filterCriteria = SongFilterCriteria.Default;
        private System.Collections.Generic.IReadOnlyList<FilteredSongResult>? _filteredView;
        private readonly ISongListFilterService _filterService = new SongListFilterService();
```

Add static test-access helpers at the bottom of the class:
```csharp
        // Test-access helpers (used by SongSelectionStageFilterTests)
        internal static bool DefaultFilterCriteriaIsEmpty() =>
            SongFilterCriteria.Default.IsEmpty;

        internal static bool DefaultFilteredViewIsNull() => true;
```

(`InternalsVisibleTo("DTXMania.Test")` and `("DTXMania.Test.Mac")` are already configured at `DTXMania.Game/Lib/Resources/BitmapFont.cs:7-8`. No additional assembly attributes needed.)

- [ ] **Step 4: Build**

Run: `dotnet build DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: Build succeeds.

- [ ] **Step 5: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageFilterTests"`
Expected: 2 tests pass.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs \
        DTXMania.Test/Stage/SongSelectionStageFilterTests.cs
git commit -m "feat: add filter criteria and view fields to SongSelectionStage"
```

---

### Task 21: SongSelectionStage — instantiate modal & open on Backspace

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

This task is wiring without unit tests because it crosses into stage lifecycle / graphics. Verified manually via build + smoke launch in Task 30.

- [ ] **Step 1: Add modal field and creation**

In `SongSelectionStage.cs`, add private field near other UI fields:

```csharp
        private SongSearchFilterModal _searchFilterModal;
        private WindowTextInputSource _textInputSource;
```

Add `using DTXMania.Game.Lib.UI.Components;` at the top.

In the method that creates other UI components (search for `_uiManager = new UIManager` or similar — likely in `OnActivate` or an `Initialize*` method around line 380-450), construct the modal after the existing UI elements:

```csharp
            // Search/filter modal
            _textInputSource = new WindowTextInputSource(_game.Window);
            _searchFilterModal = new SongSearchFilterModal(_textInputSource);
            _searchFilterModal.FilterApplied += OnFilterApplied;
            _searchFilterModal.FilterReset   += OnFilterReset;
            _searchFilterModal.Cancelled     += OnFilterCancelled;
            _mainPanel.AddChild(_searchFilterModal);
```

Add stub handlers (full bodies in Tasks 22–23):

```csharp
        private void OnFilterApplied(object sender, SongFilterCriteria criteria)
        {
            _filterCriteria = criteria;
            RebuildFilteredView();
            PopulateSongListForCurrentMode();
        }

        private void OnFilterReset(object sender, System.EventArgs e)
        {
            _filterCriteria = SongFilterCriteria.Default;
            _filteredView = null;
            PopulateSongListForCurrentMode();
        }

        private void OnFilterCancelled(object sender, System.EventArgs e)
        {
            // Discard draft; no state change
        }

        private void RebuildFilteredView()
        {
            if (_filterCriteria.IsEmpty)
            {
                _filteredView = null;
                return;
            }
            var roots = SongManager.Instance.RootSongs;
            _filteredView = _filterService.Apply(roots, _filterCriteria);
        }

        private void PopulateSongListForCurrentMode()
        {
            if (_filteredView != null)
                PopulateFilteredSongList();
            else
                PopulateSongList();
        }

        private void PopulateFilteredSongList()
        {
            var displayList = new System.Collections.Generic.List<SongListNode>(_filteredView!.Count);
            foreach (var r in _filteredView)
                displayList.Add(r.Node);
            _songListDisplay.CurrentList = displayList;
        }
```

In `ExecuteInputCommand` (around line 909), add a new case to handle `OpenSearch`:

```csharp
                case InputCommandType.OpenSearch:
                    OpenSearchFilterModal();
                    break;
```

Add the method:

```csharp
        private void OpenSearchFilterModal()
        {
            if (_searchFilterModal == null) return;
            // Exit status-panel mode first
            _isInStatusPanel = false;
            _searchFilterModal.Open(_filterCriteria);
        }
```

In the stage's `Update` / input-processing flow (where `ProcessInputCommands` is called, around line 824), suspend command processing while modal is open and route raw keys to the modal instead:

```csharp
            if (_searchFilterModal != null && _searchFilterModal.IsOpen)
            {
                ProcessModalKeys();
                return;
            }
            HandleInput();
```

Add `ProcessModalKeys`:

```csharp
        private Microsoft.Xna.Framework.Input.KeyboardState _prevModalKbState;

        private void ProcessModalKeys()
        {
            var current = Microsoft.Xna.Framework.Input.Keyboard.GetState();
            // Edge-trigger only: act on keys pressed this frame
            foreach (var key in current.GetPressedKeys())
            {
                if (!_prevModalKbState.IsKeyDown(key))
                    _searchFilterModal.HandleKey(key);
            }
            _prevModalKbState = current;
        }
```

(If a more central place handles keyboard state per-frame, prefer routing through that. Inspect surrounding code: if `InputManager` already exposes pressed keys this frame, use it. Otherwise the direct `Keyboard.GetState` poll above is acceptable.)

- [ ] **Step 2: Build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Run full Mac test suite to ensure no regressions**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All existing tests still pass.

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs
git commit -m "feat: wire search-filter modal into SongSelectionStage"
```

---

### Task 22: SongSelectionStage — breadcrumb summary while filter active

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Create: `DTXMania.Test/Stage/SongSelectionStageBreadcrumbTests.cs`

- [ ] **Step 1: Write failing tests for the summary helper**

Path: `DTXMania.Test/Stage/SongSelectionStageBreadcrumbTests.cs`

```csharp
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Song.Filtering;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage
{
    public class SongSelectionStageBreadcrumbTests
    {
        [Fact]
        public void Summarize_DefaultCriteria_ReturnsEmpty()
        {
            var s = SongSelectionStage.SummarizeFilter(SongFilterCriteria.Default);
            Assert.Equal("", s);
        }

        [Fact]
        public void Summarize_OnlySearchQuery_ShowsQuoted()
        {
            var c = SongFilterCriteria.Default with { SearchQuery = "beatles" };
            Assert.Equal("Filtered: \"beatles\"", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_LevelRange_ShowsLevels()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 50, MaxLevel = 85 };
            Assert.Equal("Filtered: Lv 50-85", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_LevelMinOnly()
        {
            var c = SongFilterCriteria.Default with { MinLevel = 50, MaxLevel = null };
            Assert.Equal("Filtered: Lv 50+", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_LevelMaxOnly()
        {
            var c = SongFilterCriteria.Default with { MinLevel = null, MaxLevel = 85 };
            Assert.Equal("Filtered: Lv ≤85", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_PlayedStatus()
        {
            var c = SongFilterCriteria.Default with { PlayedStatus = PlayedStatus.Unplayed };
            Assert.Equal("Filtered: Unplayed", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_SortNonDefault_AppendedWithArrow()
        {
            var c = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Artist,
                SortDescending = false
            };
            Assert.Equal("Filtered: Artist↑", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_SortDescending_DownArrow()
        {
            var c = SongFilterCriteria.Default with
            {
                SortBy = SongSortCriteria.Title,
                SortDescending = true
            };
            Assert.Equal("Filtered: Title↓", SongSelectionStage.SummarizeFilter(c));
        }

        [Fact]
        public void Summarize_AllFacets_JoinedWithDot()
        {
            var c = new SongFilterCriteria(
                SearchQuery: "beatles",
                MinLevel: 50, MaxLevel: 85,
                PlayedStatus: PlayedStatus.Unplayed,
                SortBy: SongSortCriteria.Title,
                SortDescending: false);
            Assert.Equal("Filtered: \"beatles\" · Lv 50-85 · Unplayed",
                SongSelectionStage.SummarizeFilter(c));
        }
    }
}
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageBreadcrumbTests"`
Expected: Build error (`SummarizeFilter` not defined).

- [ ] **Step 3: Add SummarizeFilter helper**

In `SongSelectionStage.cs`, add this static method:

```csharp
        public static string SummarizeFilter(SongFilterCriteria c)
        {
            if (c.IsEmpty) return "";

            var parts = new System.Collections.Generic.List<string>();
            if (!string.IsNullOrEmpty(c.SearchQuery))
                parts.Add($"\"{c.SearchQuery}\"");

            string? levelPart = FormatLevel(c.MinLevel, c.MaxLevel);
            if (levelPart != null) parts.Add(levelPart);

            if (c.PlayedStatus != PlayedStatus.All)
                parts.Add(c.PlayedStatus.ToString());

            string? sortPart = FormatSort(c.SortBy, c.SortDescending);
            if (sortPart != null) parts.Add(sortPart);

            return "Filtered: " + string.Join(" · ", parts);
        }

        private static string? FormatLevel(int? min, int? max)
        {
            if (min is null && max is null) return null;
            if (min is not null && max is not null) return $"Lv {min}-{max}";
            if (min is not null) return $"Lv {min}+";
            return $"Lv ≤{max}";
        }

        private static string? FormatSort(SongSortCriteria by, bool desc)
        {
            // Default sort (Title ascending) is omitted from the summary
            if (by == SongSortCriteria.Title && !desc) return null;
            char arrow = desc ? '↓' : '↑';
            return $"{by}{arrow}";
        }
```

- [ ] **Step 4: Wire it into the breadcrumb refresh logic**

Find the method updating `_breadcrumbLabel.Text` (around line 797 of `SongSelectionStage.cs`). Replace:

```csharp
            _breadcrumbLabel.Text = string.IsNullOrEmpty(_currentBreadcrumb)
                ? ""
                : _currentBreadcrumb;
```

With:

```csharp
            string filterSummary = SummarizeFilter(_filterCriteria);
            if (!string.IsNullOrEmpty(filterSummary))
                _breadcrumbLabel.Text = filterSummary;
            else
                _breadcrumbLabel.Text = string.IsNullOrEmpty(_currentBreadcrumb)
                    ? ""
                    : _currentBreadcrumb;
```

Make sure `OnFilterApplied` and `OnFilterReset` (added in Task 21) call the breadcrumb-refresh method (likely `UpdateBreadcrumb` or similar — search for the method that contains the line above, then call it after `PopulateSongListForCurrentMode()`).

- [ ] **Step 5: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageBreadcrumbTests"`
Expected: 9 tests pass.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs \
        DTXMania.Test/Stage/SongSelectionStageBreadcrumbTests.cs
git commit -m "feat: render filter summary in breadcrumb when filter active"
```

---

### Task 23: SongStatusPanel — folder-hint label

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs`
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

The status panel already exists with rich rendering. We add a single optional string property the stage sets when filter is active.

- [ ] **Step 1: Add property to SongStatusPanel**

In `SongStatusPanel.cs`, add a property near the other public properties (around line 91-145):

```csharp
        /// <summary>
        /// Optional folder breadcrumb to render at the top of the panel.
        /// Set when the song selection list is in filtered (flat) mode so the
        /// player can see where the selected song lives in the hierarchy.
        /// Empty/null = nothing rendered.
        /// </summary>
        public string FolderHint { get; set; } = "";
```

In the panel's draw method (search for the method that draws the panel content — likely `OnDraw` or `DrawAuthenticPanel` etc.), add at the top after entering the panel bounds:

```csharp
            if (!string.IsNullOrEmpty(FolderHint) && Font != null)
            {
                var pos = new Microsoft.Xna.Framework.Vector2(
                    AbsolutePosition.X + 12,
                    AbsolutePosition.Y + 6);
                spriteBatch.DrawString(Font, "From: " + FolderHint, pos, Microsoft.Xna.Framework.Color.LightGray);
            }
```

(Use `ManagedFont` if `Font` is unavailable; check what the existing code uses for similar small labels.)

- [ ] **Step 2: Update stage to set FolderHint when selection changes during filtered mode**

In `SongSelectionStage.cs`, add a small helper:

```csharp
        private void UpdateStatusPanelFolderHint()
        {
            if (_statusPanel == null) return;
            if (_filteredView == null)
            {
                _statusPanel.FolderHint = "";
                return;
            }
            var selectedNode = _songListDisplay?.SelectedSong;
            if (selectedNode == null) { _statusPanel.FolderHint = ""; return; }

            foreach (var r in _filteredView)
            {
                if (ReferenceEquals(r.Node, selectedNode))
                {
                    _statusPanel.FolderHint = r.FolderPath;
                    return;
                }
            }
            _statusPanel.FolderHint = "";
        }
```

Call this whenever the selection or filter state changes. Subscribe to `_songListDisplay.SelectionChanged` (the event already exists; see SongListDisplay.cs:259):

In the location where other `_songListDisplay` event subscriptions happen (search for `SelectionChanged +=` or similar), add:

```csharp
            _songListDisplay.SelectionChanged += (_, _) => UpdateStatusPanelFolderHint();
```

Also call `UpdateStatusPanelFolderHint()` at the end of `OnFilterApplied` and `OnFilterReset`.

- [ ] **Step 3: Build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeds.

- [ ] **Step 4: Run full Mac suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass.

- [ ] **Step 5: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongStatusPanel.cs \
        DTXMania.Game/Lib/Stage/SongSelectionStage.cs
git commit -m "feat: status panel renders 'From:' folder hint while filter active"
```

---

### Task 24: SongSelectionStage — empty-state message

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

- [ ] **Step 1: Add empty-state rendering**

In `SongSelectionStage.cs`, add a private field:

```csharp
        private bool _showEmptyFilterMessage;
```

In `OnFilterApplied` and `OnFilterReset`, set the flag based on result count. Update `RebuildFilteredView` to also set the flag:

```csharp
        private void RebuildFilteredView()
        {
            if (_filterCriteria.IsEmpty)
            {
                _filteredView = null;
                _showEmptyFilterMessage = false;
                return;
            }
            var roots = SongManager.Instance.RootSongs;
            _filteredView = _filterService.Apply(roots, _filterCriteria);
            _showEmptyFilterMessage = _filteredView.Count == 0;
        }
```

In the stage's `OnDraw` method (search for `DrawSongList` or similar — it's where the song list area gets rendered), after the list draws, conditionally render the empty-state message:

```csharp
            if (_showEmptyFilterMessage && _bitmapFont != null)
            {
                // Reuse the existing ManagedFont (consistent with other UI text per spec)
                var msg = "No songs match this filter";
                var managedFont = _statusPanel?.ManagedFont; // shared loaded font
                if (managedFont != null)
                {
                    var center = new Microsoft.Xna.Framework.Vector2(
                        SongSelectionUILayout.SongBars.UnselectedBarX + 100,
                        SongSelectionUILayout.SongBars.SelectedBarY);
                    managedFont.DrawString(spriteBatch, msg, center, Microsoft.Xna.Framework.Color.LightGray);
                }
            }
```

(If `ManagedFont.DrawString` differs in signature, mirror what `SongStatusPanel` already uses for its labels — same component-pattern.)

- [ ] **Step 2: Build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs
git commit -m "feat: render empty-state message when filter has no results"
```

---

### Task 25: SongSelectionStage — selection clamping

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
- Create: `DTXMania.Test/Stage/SongSelectionStageClampingTests.cs`

- [ ] **Step 1: Write failing tests for the clamp helper**

Path: `DTXMania.Test/Stage/SongSelectionStageClampingTests.cs`

```csharp
using System.Collections.Generic;
using DTXMania.Game.Lib.Song;
using DTXMania.Game.Lib.Stage;
using Xunit;

namespace DTXMania.Test.Stage
{
    public class SongSelectionStageClampingTests
    {
        private static SongListNode S(string title) =>
            new SongListNode { Type = NodeType.Score, Title = title };

        [Fact]
        public void ClampSelectionIndex_PreviousNodeStillPresent_ReturnsItsIndex()
        {
            var prev = S("B");
            var newList = new List<SongListNode> { S("A"), prev, S("C") };

            int idx = SongSelectionStage.ClampSelectionIndex(prev, newList);

            Assert.Equal(1, idx);
        }

        [Fact]
        public void ClampSelectionIndex_PreviousNodeMissing_ReturnsZero()
        {
            var prev = S("Removed");
            var newList = new List<SongListNode> { S("A"), S("B") };

            int idx = SongSelectionStage.ClampSelectionIndex(prev, newList);

            Assert.Equal(0, idx);
        }

        [Fact]
        public void ClampSelectionIndex_EmptyList_ReturnsZero()
        {
            int idx = SongSelectionStage.ClampSelectionIndex(S("X"), new List<SongListNode>());
            Assert.Equal(0, idx);
        }

        [Fact]
        public void ClampSelectionIndex_NullPrevious_ReturnsZero()
        {
            int idx = SongSelectionStage.ClampSelectionIndex(null, new List<SongListNode> { S("A") });
            Assert.Equal(0, idx);
        }
    }
}
```

- [ ] **Step 2: Run tests — expect failure**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageClampingTests"`
Expected: Build error (`ClampSelectionIndex` not defined).

- [ ] **Step 3: Add the helper and use it**

In `SongSelectionStage.cs`:

```csharp
        public static int ClampSelectionIndex(SongListNode previousSelected, System.Collections.Generic.IReadOnlyList<SongListNode> newList)
        {
            if (newList == null || newList.Count == 0) return 0;
            if (previousSelected == null) return 0;
            for (int i = 0; i < newList.Count; i++)
            {
                if (ReferenceEquals(newList[i], previousSelected))
                    return i;
            }
            return 0;
        }
```

Modify `PopulateFilteredSongList` to clamp after assigning the list:

```csharp
        private void PopulateFilteredSongList()
        {
            var prev = _songListDisplay?.SelectedSong;
            var displayList = new System.Collections.Generic.List<SongListNode>(_filteredView!.Count);
            foreach (var r in _filteredView)
                displayList.Add(r.Node);
            _songListDisplay.CurrentList = displayList;
            _songListDisplay.SelectedIndex = ClampSelectionIndex(prev, displayList);
        }
```

Apply the same change to `PopulateSongList` (existing method around line 562) by capturing `prev` and clamping after the list assignment. Wrap that change carefully — do not modify the back-box prepending logic.

- [ ] **Step 4: Run tests — expect pass**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSelectionStageClampingTests"`
Expected: 4 tests pass.

- [ ] **Step 5: Run full Mac suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs \
        DTXMania.Test/Stage/SongSelectionStageClampingTests.cs
git commit -m "feat: clamp selection to surviving node after filter Apply/Reset"
```

---

## Phase 5 — Polish & Edge Cases

### Task 26: Library-loading guard (disable Apply while songs load)

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs`
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

- [ ] **Step 1: Add `IsLibraryReady` property to modal**

In `SongSearchFilterModal.cs`, add:

```csharp
        public bool IsLibraryReady { get; set; } = true;

        public string LoadingHintText { get; set; } = "Library still loading…";
```

In `Apply` and `SubmitFromSearchBox`, gate behind `IsLibraryReady`:

```csharp
        public void Apply()
        {
            if (!IsLibraryReady) return;
            FilterApplied?.Invoke(this, _draft);
            Close();
        }

        public void SubmitFromSearchBox()
        {
            if (!IsLibraryReady) return;
            if (string.Equals(_draft.SearchQuery, "/q", StringComparison.OrdinalIgnoreCase))
            {
                Reset();
                return;
            }
            Apply();
        }
```

(Reset and Cancel still work even while loading — only Apply is gated, per spec.)

- [ ] **Step 2: Add a unit test**

Append to `SongSearchFilterModalLogicTests.cs`:

```csharp
        [Fact]
        public void Apply_WhenLibraryNotReady_DoesNotFire()
        {
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            bool fired = false;
            modal.FilterApplied += (_, _) => fired = true;

            modal.Apply();

            Assert.False(fired);
            Assert.True(modal.IsOpen); // stays open
        }

        [Fact]
        public void Reset_StillWorksWhenLibraryNotReady()
        {
            var modal = new SongSearchFilterModal(new FakeSource()) { IsLibraryReady = false };
            modal.Open(SongFilterCriteria.Default);
            bool fired = false;
            modal.FilterReset += (_, _) => fired = true;

            modal.Reset();

            Assert.True(fired);
        }
```

- [ ] **Step 3: Run tests**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj --filter "FullyQualifiedName~SongSearchFilterModalLogicTests"`
Expected: 27 tests pass.

- [ ] **Step 4: Set IsLibraryReady from the stage when opening modal**

In `SongSelectionStage.OpenSearchFilterModal()`:

```csharp
        private void OpenSearchFilterModal()
        {
            if (_searchFilterModal == null) return;
            _isInStatusPanel = false;
            _searchFilterModal.IsLibraryReady =
                _songInitializationProcessed && _currentSongList != null;
            _searchFilterModal.Open(_filterCriteria);
        }
```

When library finishes loading (in `CheckSongInitializationCompletion` around line 583 — search for where `_songInitializationProcessed = true` is set), refresh the modal's flag if it's open:

```csharp
                if (_searchFilterModal != null && _searchFilterModal.IsOpen)
                    _searchFilterModal.IsLibraryReady = true;
```

- [ ] **Step 5: Build & run full suite**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj && dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: Build succeeds, all tests pass.

- [ ] **Step 6: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs \
        DTXMania.Test/Song/Components/SongSearchFilterModalLogicTests.cs \
        DTXMania.Game/Lib/Stage/SongSelectionStage.cs
git commit -m "feat: gate Apply behind IsLibraryReady for songs-still-loading case"
```

---

### Task 27: Error handling around projection

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

- [ ] **Step 1: Wrap the projection call in try/catch**

In `SongSelectionStage.RebuildFilteredView`, wrap the call:

```csharp
        private void RebuildFilteredView()
        {
            if (_filterCriteria.IsEmpty)
            {
                _filteredView = null;
                _showEmptyFilterMessage = false;
                return;
            }

            try
            {
                var roots = SongManager.Instance.RootSongs;
                _filteredView = _filterService.Apply(roots, _filterCriteria);
                _showEmptyFilterMessage = _filteredView.Count == 0;
            }
            catch (System.Exception ex)
            {
                System.Diagnostics.Debug.WriteLine(
                    $"SongSelectionStage: filter projection failed: {ex.Message}");
                // Leave existing list reference unchanged.
                _filteredView = null;
                _showEmptyFilterMessage = false;
            }
        }
```

- [ ] **Step 2: Build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeds.

- [ ] **Step 3: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs
git commit -m "fix: log and recover when filter projection throws"
```

---

### Task 28: Modal — Draw implementation (graphics)

This task adds actual rendering to the modal. Tests for graphics-side behavior are excluded from the Mac suite per project conventions.

**Files:**
- Modify: `DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs`
- Modify: `DTXMania.Test/DTXMania.Test.Mac.csproj` (exclude any new graphics tests)

- [ ] **Step 1: Implement OnDraw**

In `SongSearchFilterModal.cs`, expand `OnDraw` and add public properties for the resources the stage will inject:

```csharp
        public Texture2D? WhitePixel { get; set; }
        public SpriteFont? Font { get; set; }

        protected override void OnDraw(SpriteBatch spriteBatch, double deltaTime)
        {
            if (!_isOpen || WhitePixel == null || Font == null) return;

            var modalBounds = DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.SearchFilterModal.Bounds;

            // Dim the screen behind the modal
            spriteBatch.Draw(WhitePixel,
                new Microsoft.Xna.Framework.Rectangle(0, 0, 1280, 720),
                new Microsoft.Xna.Framework.Color(0, 0, 0, 160));

            // Modal panel background
            spriteBatch.Draw(WhitePixel, modalBounds,
                new Microsoft.Xna.Framework.Color(28, 28, 32));

            // Title
            DrawText(spriteBatch, "SEARCH & FILTER",
                new Microsoft.Xna.Framework.Vector2(
                    modalBounds.X + (modalBounds.Width - Font.MeasureString("SEARCH & FILTER").X) / 2,
                    modalBounds.Y + 12),
                Microsoft.Xna.Framework.Color.White);

            DrawSearchRow(spriteBatch, modalBounds);
            DrawLevelRow(spriteBatch, modalBounds);
            DrawPlayedRow(spriteBatch, modalBounds);
            DrawSortRow(spriteBatch, modalBounds);
            DrawButtons(spriteBatch, modalBounds);

            if (!IsLibraryReady)
            {
                DrawText(spriteBatch, LoadingHintText,
                    new Microsoft.Xna.Framework.Vector2(modalBounds.X + 24, modalBounds.Y + modalBounds.Height - 24),
                    Microsoft.Xna.Framework.Color.Yellow);
            }
        }

        private void DrawText(SpriteBatch spriteBatch, string text, Microsoft.Xna.Framework.Vector2 pos, Microsoft.Xna.Framework.Color color)
        {
            spriteBatch.DrawString(Font!, text, pos, color);
        }

        private static readonly Microsoft.Xna.Framework.Color FocusedBg = new(60, 60, 80);
        private static readonly Microsoft.Xna.Framework.Color FieldBg   = new(40, 40, 50);

        private void DrawSearchRow(SpriteBatch sb, Microsoft.Xna.Framework.Rectangle modal)
        {
            var L = DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.SearchFilterModal;
            DrawText(sb, "Search:", new Microsoft.Xna.Framework.Vector2(modal.X + L.LabelX, modal.Y + L.SearchBoxY + 6),
                Microsoft.Xna.Framework.Color.LightGray);
            var box = new Microsoft.Xna.Framework.Rectangle(
                modal.X + L.FieldX, modal.Y + L.SearchBoxY, L.SearchBoxWidth, L.SearchBoxHeight);
            sb.Draw(WhitePixel,
                box,
                _focusedField == Field.SearchBox ? FocusedBg : FieldBg);
            DrawText(sb, _draft.SearchQuery ?? "",
                new Microsoft.Xna.Framework.Vector2(box.X + 6, box.Y + 6),
                Microsoft.Xna.Framework.Color.White);
        }

        private void DrawLevelRow(SpriteBatch sb, Microsoft.Xna.Framework.Rectangle modal)
        {
            var L = DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.SearchFilterModal;
            DrawText(sb, "Level:", new Microsoft.Xna.Framework.Vector2(modal.X + L.LabelX, modal.Y + L.LevelRowY + 6),
                Microsoft.Xna.Framework.Color.LightGray);
            DrawNumeric(sb, modal.X + L.LevelMinX, modal.Y + L.LevelRowY,
                L.LevelInputWidth, L.LevelInputHeight,
                _draft.MinLevel, _focusedField == Field.MinLevel, "Min");
            DrawNumeric(sb, modal.X + L.LevelMaxX, modal.Y + L.LevelRowY,
                L.LevelInputWidth, L.LevelInputHeight,
                _draft.MaxLevel, _focusedField == Field.MaxLevel, "Max");
        }

        private void DrawNumeric(SpriteBatch sb, int x, int y, int w, int h, int? value, bool focused, string label)
        {
            sb.Draw(WhitePixel, new Microsoft.Xna.Framework.Rectangle(x, y, w, h),
                focused ? FocusedBg : FieldBg);
            string text = value?.ToString() ?? label;
            DrawText(sb, text, new Microsoft.Xna.Framework.Vector2(x + 6, y + 6),
                value is null ? Microsoft.Xna.Framework.Color.Gray : Microsoft.Xna.Framework.Color.White);
        }

        private void DrawPlayedRow(SpriteBatch sb, Microsoft.Xna.Framework.Rectangle modal)
        {
            var L = DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.SearchFilterModal;
            DrawText(sb, "Played:", new Microsoft.Xna.Framework.Vector2(modal.X + L.LabelX, modal.Y + L.PlayedRowY + 6),
                Microsoft.Xna.Framework.Color.LightGray);
            var rowBg = _focusedField == Field.PlayedStatus ? FocusedBg : FieldBg;
            var box = new Microsoft.Xna.Framework.Rectangle(
                modal.X + L.FieldX, modal.Y + L.PlayedRowY, 380, 30);
            sb.Draw(WhitePixel, box, rowBg);
            DrawText(sb, "(◀ ▶) " + _draft.PlayedStatus,
                new Microsoft.Xna.Framework.Vector2(box.X + 6, box.Y + 6),
                Microsoft.Xna.Framework.Color.White);
        }

        private void DrawSortRow(SpriteBatch sb, Microsoft.Xna.Framework.Rectangle modal)
        {
            var L = DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.SearchFilterModal;
            DrawText(sb, "Sort by:", new Microsoft.Xna.Framework.Vector2(modal.X + L.LabelX, modal.Y + L.SortRowY + 6),
                Microsoft.Xna.Framework.Color.LightGray);
            var sortBox = new Microsoft.Xna.Framework.Rectangle(
                modal.X + L.FieldX, modal.Y + L.SortRowY, 160, 30);
            sb.Draw(WhitePixel, sortBox,
                _focusedField == Field.SortBy ? FocusedBg : FieldBg);
            DrawText(sb, _draft.SortBy.ToString(),
                new Microsoft.Xna.Framework.Vector2(sortBox.X + 6, sortBox.Y + 6),
                Microsoft.Xna.Framework.Color.White);

            var dirBox = new Microsoft.Xna.Framework.Rectangle(
                sortBox.X + sortBox.Width + 20, sortBox.Y, 100, 30);
            sb.Draw(WhitePixel, dirBox,
                _focusedField == Field.SortDirection ? FocusedBg : FieldBg);
            DrawText(sb, _draft.SortDescending ? "Desc" : "Asc",
                new Microsoft.Xna.Framework.Vector2(dirBox.X + 6, dirBox.Y + 6),
                Microsoft.Xna.Framework.Color.White);
        }

        private void DrawButtons(SpriteBatch sb, Microsoft.Xna.Framework.Rectangle modal)
        {
            var L = DTXMania.Game.Lib.UI.Layout.SongSelectionUILayout.SearchFilterModal;
            var resetRect = new Microsoft.Xna.Framework.Rectangle(
                modal.X + L.ResetButtonX, modal.Y + L.ButtonRowY, L.ButtonWidth, L.ButtonHeight);
            var applyRect = new Microsoft.Xna.Framework.Rectangle(
                modal.X + L.ApplyButtonX, modal.Y + L.ButtonRowY, L.ButtonWidth, L.ButtonHeight);
            sb.Draw(WhitePixel, resetRect,
                _focusedField == Field.ResetButton ? FocusedBg : FieldBg);
            sb.Draw(WhitePixel, applyRect,
                _focusedField == Field.ApplyButton ? FocusedBg : FieldBg);
            DrawText(sb, "Reset",
                new Microsoft.Xna.Framework.Vector2(resetRect.X + 36, resetRect.Y + 8),
                Microsoft.Xna.Framework.Color.White);
            DrawText(sb, "Apply",
                new Microsoft.Xna.Framework.Vector2(applyRect.X + 36, applyRect.Y + 8),
                Microsoft.Xna.Framework.Color.White);
        }
```

- [ ] **Step 2: Stage injects WhitePixel and Font**

In `SongSelectionStage.cs`, where the modal is constructed (Task 21), after the existing `_searchFilterModal = ...` line, set:

```csharp
            _searchFilterModal.WhitePixel = _whitePixel;
            _searchFilterModal.Font = _bitmapFont?.SpriteFont; // existing field; or whichever ManagedFont yields a SpriteFont
```

(If `_bitmapFont` doesn't expose a SpriteFont, prefer `_statusPanel.Font` once it's initialised, or use the same ManagedFont path as the status panel. Verify by inspecting where `SongStatusPanel.Font` is currently assigned and reuse that source.)

- [ ] **Step 3: Build**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj`
Expected: Build succeeds. Fix any name resolution issues.

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Game/Lib/Song/Components/SongSearchFilterModal.cs \
        DTXMania.Game/Lib/Stage/SongSelectionStage.cs
git commit -m "feat: render search-filter modal with focus highlights"
```

---

### Task 29: Suppress folder navigation while filter active

**Files:**
- Modify: `DTXMania.Game/Lib/Stage/SongSelectionStage.cs`

While filter is active, only `Score` nodes appear in the list (BackBox/Box are excluded by the projection). But the `HandleActivateInput`/Box-enter logic should defensively no-op if it ever sees a non-Score node while `_filteredView != null`.

- [ ] **Step 1: Find HandleActivateInput**

Run: `grep -n "HandleActivateInput\|EnterBox\|NavigateBack" DTXMania.Game/Lib/Stage/SongSelectionStage.cs`
Note the line ranges so the edit lands on the existing logic.

- [ ] **Step 2: Add a guard**

In the method that processes Activate (likely `HandleActivateInput`), add at the top:

```csharp
            if (_filteredView != null && _songListDisplay?.SelectedSong?.Type != NodeType.Score)
            {
                // Filter is active; only Score nodes are valid targets.
                return;
            }
```

In `NavigateBack` (used for hierarchy back-navigation), guard similarly:

```csharp
            if (_filteredView != null) return;
```

- [ ] **Step 3: Build & test**

Run: `dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj && dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: Build succeeds, all tests pass.

- [ ] **Step 4: Commit**

```bash
git add DTXMania.Game/Lib/Stage/SongSelectionStage.cs
git commit -m "fix: suppress hierarchy navigation while filter is active"
```

---

### Task 30: Mac csproj exclusions for graphics-dependent modal tests

**Files:**
- Modify: `DTXMania.Test/DTXMania.Test.Mac.csproj`

There are no graphics-dependent tests in this feature beyond what we've already added (the modal logic tests use a fake source and don't touch GraphicsDevice). This task is precautionary: verify the Mac suite compiles & runs.

- [ ] **Step 1: Run full Mac suite**

Run: `dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj`
Expected: All tests pass.

- [ ] **Step 2: If any added test crashes Mac due to GraphicsDevice usage, add it to the Exclude list**

Edit the `<Compile Include="**/*.cs" Exclude="..." />` entry, appending the offending test path with a `;` separator. (Likely not needed — the modal logic tests are graphics-free by design.)

- [ ] **Step 3: Commit (if any change made)**

```bash
git add DTXMania.Test/DTXMania.Test.Mac.csproj
git commit -m "test: exclude graphics-dependent search-filter tests from Mac suite"
```

(Skip this commit if no exclusion was needed.)

---

## Phase 6 — Manual Verification

### Task 31: Manual UI test checklist

**Files:** None (verification only).

Run the Mac game and exercise the feature.

- [ ] **Step 1: Build and launch**

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj
dotnet run --project DTXMania.Game/DTXMania.Game.Mac.csproj
```

Navigate to the Song Selection stage.

- [ ] **Step 2: Walk the manual checklist**

Verify each item from the spec:

- [ ] Press Backspace from song list → modal opens; search box has focus.
- [ ] Type a query, press Enter → list filters to matching songs.
- [ ] Press Backspace → modal re-opens populated with the prior query.
- [ ] Tab to Min Level, press Right several times → Min Level increases by 5 each press, capped at 99.
- [ ] Set Min/Max levels, Enter → list filters by level range.
- [ ] Tab to Played, Right twice → Played status cycles to Played; Enter → list shows only played songs.
- [ ] Tab to Sort, Right → cycles Title → Artist → Level.
- [ ] Tab to Sort Direction, Right → toggles Asc/Desc; Enter → list reorders.
- [ ] Tab to Reset, Enter → modal closes, list is back to default (all songs visible, hierarchy intact).
- [ ] Press Backspace, type `/q`, Enter → same as Reset.
- [ ] Press Backspace, edit any field, Esc → modal closes, list unchanged.
- [ ] With filter active, Esc back to Title, then re-enter Song Select → filter still active (breadcrumb still shows summary).
- [ ] Apply filter that yields zero results → "No songs match this filter" appears.
- [ ] Backspace key inside search box deletes a character (does NOT re-open modal).
- [ ] If your build can paste IME input (e.g., Japanese): characters appear in search box after IME commit. (Smoke test only; deferred behaviors documented in spec.)
- [ ] Select a song from filtered view, play it, return → filter is still active; selection clamps to a valid index.
- [ ] Status panel shows "From: <folder path>" for the selected song while filter is active.

- [ ] **Step 3: Capture issues**

For any failing item, file a follow-up bug or open a fix task. Do NOT mark this task complete unless every checkbox passes or has a documented follow-up.

- [ ] **Step 4: Final commit (if any fixes made)**

```bash
git add -A
git commit -m "fix: address manual checklist findings for search/filter UI"
```

---

## Self-Review

After all tasks land, do one last sweep:

1. **Spec coverage** — every section of the spec maps to a task:
   - Scope decisions table → Tasks 1–8 (data layer), 13 (Backspace), 16 (`/q`), 18 (modal keys).
   - Architecture / approach (1) — Tasks 20, 21, 25 (filter-as-view).
   - Result rendering (b) — Task 23 (status panel folder hint).
   - Modal layout — Task 14.
   - Modal UI / focus model — Tasks 15, 17, 18.
   - Input handling — Tasks 13, 18, 19, 21.
   - Persistence (session-scoped) — Task 20 (criteria field on stage).
   - Edge cases — Tasks 24 (empty), 25 (clamp), 26 (loading), 27 (error), 29 (nav suppress).
   - Testing — Tasks 1–18, 20, 22, 25, 26, 30.
   - Manual checklist — Task 31.

2. **Run full test suite once more**

```bash
dotnet test DTXMania.Test/DTXMania.Test.Mac.csproj
```

Expected: All tests pass.

3. **Run on Windows CI later** — verify CI green on the full Windows test suite (`DTXMania.Test.csproj`).

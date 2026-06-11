# NX Score Import — Design Spec

**Date:** 2026-06-08
**Status:** Approved for implementation planning

## Summary

Import existing DTXManiaNX per-chart score files (`<dtx-name>.score.ini`) into the
DTXManiaCX SQLite score database. Import is triggered **manually** from the Config
page via a new "Import NX Scores" action, iterating over charts already present in the
song database. Scope is **drums only**. Existing CX score data is preserved via a
**best-of merge** with snapshot-delta tracking so repeated imports never inflate play
counts.

## Background

### NX score.ini format

DTXManiaNX writes one `<dtx-name>.score.ini` file beside each chart file, encoded in
Shift-JIS. Relevant sections:

- `[File]` — `Title`, `Name` (dtx filename), `Hash` (MD5 of the dtx file),
  `PlayCountDrums`, `ClearCountDrums`, `BestRankDrums` (ERANK ordinal),
  `HistoryCount`, `History0`–`History4`, `BGMAdjust`.
- `[HiScore.Drums]` — best-by-score entry: `Score`, `PlaySkill` (achievement %, 0–100),
  `Skill` (game skill), `Perfect`/`Great`/`Good`/`Poor`/`Miss`, `MaxCombo`,
  `TotalChips`, `Progress`, `DateTime`, input-method flags
  (`UseKeyboard`/`UseMIDIIN`/`UseJoypad`/`UseMouse`).
- `[HiSkill.Drums]` — best-by-skill entry, same shape.
- `[LastPlay.Drums]` — most recent play, same shape.
- Guitar/Bass equivalents exist but are **out of scope** for v1.

`ERANK` ordinal scale (from `DTXManiaNX/.../CScoreIni.cs`):
`SS=0, S=1, A=2, B=3, C=4, D=5, E=6, UNKNOWN=99`.

History line format: `"{playIndex}.{yy}/{m}/{d} {status} ({rank}: {skill})"`, e.g.
`79.26/5/15 Cleared (S: 94.37)`. The leading number is the cumulative play index, not
part of the date; the date token is `yy/m/d` with a two-digit year (`yy + 2000`).

### CX score model (target)

- `SongScore` rows keyed by `(ChartId, Instrument)` (unique index). Drums =
  `EInstrumentPart.DRUMS`.
- `SongChart` rows matched by `FilePath` (unique). `FileHash` is MD5 of the dtx file.
- `PerformanceHistory` rows keyed **per song** (`SongId`), unique on
  `(SongId, DisplayOrder)`, `DisplayOrder` 1..5.
- NX `Skill` (game skill) maps to CX `HighSkill`/`SongSkill`. NX `PlaySkill`
  (achievement %) maps to CX `BestAchievementRate`.
- Encoding: CX already registers `CodePagesEncodingProvider` and references
  `System.Text.Encoding.CodePages`, so `Encoding.GetEncoding("Shift_JIS")` is available.

### Rank translation hazard

CX's existing `SongScore.ConvertLegacyOrdinalToBucket` assumes a different legacy
`1=S … 7=F` ordinal scheme. It mishandles NX's `SS=0` (would map to F) and
`UNKNOWN=99` (would map to SS). **The importer therefore translates NX `ERANK`
explicitly into CX percentage buckets and never feeds the raw NX ordinal through the
existing converter.**

## Goals

- Manually import NX drum scores into the CX database from the Config page.
- Preserve any existing CX-native play data (best-of merge).
- Be safe to run repeatedly (idempotent counters via snapshot delta).
- Import recent-play history into `PerformanceHistory`, merged newest-first across the
  multiple charts that map to a single song.

## Non-Goals (v1)

- Guitar and Bass import.
- Automatic import during song enumeration.
- Filesystem re-scan for score.ini (only charts already in the DB are considered).
- Hash-based staleness gating (matching is purely by sibling file path).
- Exporting CX scores back to NX format.

## Architecture

### Trigger — ConfigStage action

Add a `NavigationConfigItem("Import NX Scores", action)` to
`ConfigStage.SetupConfigItems()`. Activation (Enter / Left / Right) fires the action.

Because the import is async DB work and the Config update loop is synchronous:

- The action starts the import as a fire-and-forget `Task` and sets an
  `_importRunning` re-entry guard (subsequent activations are ignored while running).
- A status string (`_importStatus`) is updated from an `IProgress<NxImportProgress>`
  callback and rendered as a live status line near the menu:
  `Importing…` → `Imported {imported} scores ({scanned} charts scanned)`. Errors/skips
  are summarized in the same line.
- Progress callbacks marshal only string/int updates; the UI thread reads the latest
  snapshot each `OnDraw`. No DB objects cross threads.

`ConfigStage` accesses the database through the `SongManager` singleton.

### Components (`DTXMania.Game/Lib/Song/`)

#### `NxScoreData` (DTO)

Plain data carrier for parsed drum values:

- Best: `BestScore`, `BestPerfect`, `BestGreat`, `BestGood`, `BestPoor`, `BestMiss`,
  `BestMaxCombo`, `TotalChips`, `BestAchievementRate` (from `[HiScore.Drums] PlaySkill`).
- `HighSkill` (from `[HiSkill.Drums] Skill`).
- `PlayCount`, `ClearCount` (from `[File]`).
- `BestRankOrdinal` (from `[File] BestRankDrums`).
- Last play: `LastScore`, `LastSkill`, `LastPlayedAt` (nullable), `LastProgress`.
- Input flags: `UsedKeyboard`, `UsedMidi`, `UsedJoypad`, `UsedMouse`.
- `IReadOnlyList<NxHistoryLine> History` — each with the raw line text and parsed
  `DateTime`.
- `bool HasDrumData` — true when there is meaningful drum data
  (`PlayCount > 0` or `BestScore > 0`).

#### `NxHistoryLine`

`{ string Text; DateTime Date; }` — `Text` is the verbatim history line (used for
dedup and storage); `Date` is parsed from the `yy/m/d` token for sorting.

#### `NxScoreIniParser`

`public static NxScoreData? Parse(string scoreIniPath)`:

- Returns `null` if the file does not exist.
- Reads with `Encoding.GetEncoding("Shift_JIS")` (every consumed field is ASCII, but
  Shift-JIS is correct and dependency is already present).
- Parses INI into a `section → key → value` map; tolerant of missing
  sections/keys/whitespace, case-insensitive keys.
- Populates `NxScoreData` from `[File]`, `[HiScore.Drums]`, `[HiSkill.Drums]`,
  `[LastPlay.Drums]`.
- Parses `DateTime` via the NX format (`M/d/yyyy h:mm:ss tt`, invariant culture);
  unparseable/empty → `null`.
- Parses `History0`–`History4` (skipping empties), extracting the `yy/m/d` date token;
  unparseable date → line is kept with `DateTime.MinValue` so it sorts last.
- Returns `null` when `HasDrumData` is false.

#### `NxScoreImporter`

`public Task MergeAsync(SongDbContext ctx, SongChart chart, NxScoreData data,
CancellationToken cancellationToken = default)`
— merges one chart's drum data and persists via `SaveChangesAsync`. Caller wraps in
the shared context/transaction. Steps:

1. Load or create the `SongScore` for `(chart.Id, DRUMS)`.
2. Apply **best-of** merge (see Merge Semantics).
3. Apply **snapshot-delta** counter merge.
4. Apply **last-play newest-wins** merge.
5. Merge history into the chart's `Song` `PerformanceHistory`.
6. `SaveChangesAsync`.

Owns `static int MapNxRankToBucket(int ordinal)`:
`0→95, 1→90, 2→80, 3→70, 4→60, 5→50, 6→40, _→-1` (`-1` = "no rank, leave unchanged").

#### `SongManager.ImportNxScoresAsync`

`public async Task<NxImportResult> ImportNxScoresAsync(IProgress<NxImportProgress>?
progress = null, CancellationToken ct = default)`:

- Loads all charts (via the database service) including their `Song` and `Scores`.
- For each chart with `HasDrumChart == true`:
  - Skips if the chart file's directory is gone / sibling score.ini absent
    (`chart.FilePath + ".score.ini"`).
  - `NxScoreIniParser.Parse(...)`; skip on `null`.
  - `NxScoreImporter.MergeAsync(...)`.
  - Increments `scanned`/`imported`/`skipped`/`errors`; reports progress.
- Returns `NxImportResult { Scanned, Imported, Skipped, Errors }`.
- Per-chart exceptions are caught, counted as errors, and logged; the loop continues.

## Merge Semantics (DRUMS `SongScore`)

Let `s` = existing/created `SongScore`, `d` = `NxScoreData`.

### Best fields (idempotent max)

- If `d.BestScore > s.BestScore`: set `s.BestScore = d.BestScore` and **adopt the
  winning run's** block: `BestPerfect/BestGreat/BestGood/BestPoor/BestMiss` from `d`,
  `s.MaxCombo = max(s.MaxCombo, d.BestMaxCombo)`,
  `s.TotalNotes = max(s.TotalNotes, d.TotalChips)`,
  `s.BestAchievementRate = max(s.BestAchievementRate, d.BestAchievementRate)`.
- `s.BestRank = max(SongScore.NormalizeStoredBestRank(s.BestRank),
  MapNxRankToBucket(d.BestRankOrdinal))`, ignoring a `-1` map result.
- `s.HighSkill = max(s.HighSkill, d.HighSkill)`.
- `s.MaxCombo = max(s.MaxCombo, d.BestMaxCombo)` (also applied independent of score win).
- `s.FullCombo = s.FullCombo || nxFullCombo`, where
  `nxFullCombo = d.BestMaxCombo > 0 && d.BestMaxCombo == d.BestPerfect + d.BestGreat +
  d.BestGood + d.BestPoor + d.BestMiss` (mirrors NX `bIsFullCombo`).
- Input flags OR-merged: `s.UsedKeyboard |= d.UsedKeyboard`, etc.

### Counters (snapshot delta)

New columns `NxImportedPlayCount`, `NxImportedClearCount` on `SongScore`:

- `s.PlayCount += max(0, d.PlayCount - s.NxImportedPlayCount)`; then
  `s.NxImportedPlayCount = d.PlayCount`.
- `s.ClearCount += max(0, d.ClearCount - s.NxImportedClearCount)`; then
  `s.NxImportedClearCount = d.ClearCount`.

Re-importing an unchanged file yields delta 0 → no inflation. If the NX file later
gains plays, only the increase is added.

### Last play (newest wins)

If `d.LastPlayedAt` is non-null and (`s.LastPlayedAt` is null or
`d.LastPlayedAt > s.LastPlayedAt`): set `s.LastScore = d.LastScore`,
`s.LastSkillPoint = d.LastSkill`, `s.LastPlayedAt = d.LastPlayedAt`, and
`s.ProgressBar = d.LastProgress` (only if non-empty). Otherwise keep CX values.

## History Merge (`PerformanceHistory`, per song, newest-wins across charts)

For the chart's `Song`:

1. Collect existing `PerformanceHistory` rows for the song (already loaded with the
   chart graph, or queried).
2. Build a candidate set = existing rows (keyed by `HistoryLine` text + `PerformedAt`)
   ∪ `d.History` entries (`Text` + `Date`).
3. Dedup by exact line text (first occurrence wins).
4. Sort by date descending; take the top 5.
5. Delete the song's existing `PerformanceHistory` rows and insert the merged top-5 with
   `DisplayOrder = 1..5` and `PerformedAt = parsed date`.

Delete-and-reinsert avoids `(SongId, DisplayOrder)` unique-constraint collisions and
converges when two charts of the same song are imported in the same run (the second
import sees the first's rows in step 1).

## Schema Migration

- Add `public int NxImportedPlayCount { get; set; }` and
  `public int NxImportedClearCount { get; set; }` to `SongScore`.
- Fresh databases receive the columns via `EnsureCreated`.
- Existing databases: additive `ALTER TABLE SongScores ADD COLUMN ... INTEGER NOT NULL
  DEFAULT 0` in `SongDatabaseService.ConfigureUtf8EncodingAsync`, following the existing
  idempotent `EnsureBookmarkColumnAsync` pattern (guard with a `pragma_table_info`
  count, treat a concurrent "duplicate column" as success, propagate genuine failures).
- Add both columns to `SongScore.Clone()` so cloned score instances carry the snapshot.

## Guards & Edge Cases

- Sibling score.ini missing → skip (counts as skipped, not error).
- `chart.HasDrumChart == false` → skip (no drum score row would be meaningful).
- `NxScoreData.HasDrumData == false` → parser returns `null` → skip.
- Chart file directory missing → skip.
- `BestRankDrums == 99` (UNKNOWN) → `MapNxRankToBucket` returns `-1` → rank left
  unchanged.
- Empty `DateTime` in `[LastPlay.Drums]` → last-play merge skipped.
- Empty `Progress` → `ProgressBar` left unchanged.
- Unparseable history date → kept with `DateTime.MinValue` (sorts last), still dedup'd.
- Per-chart exceptions are caught and counted; the bulk loop continues.
- Matching is purely by sibling path; no `[File] Hash` vs `FileHash` comparison in v1.

## Testing

All tests are non-graphics and run on both Windows and Mac suites.

### Parser tests (`NxScoreIniParserTests`)

Copy the three samples into `DTXMania.Test/TestData/NxScores/`
(`mas.dtx.score.ini`, `ext.dtx.score.ini`, `full.dtx.score.ini`).

- `mas`: `BestScore == 958247`, `BestPerfect == 2293`, `BestGreat == 271`,
  `BestGood == 11`, `BestMaxCombo == 2575`, `TotalChips == 2575`,
  `PlayCount == 79`, `ClearCount == 72`, `BestRankOrdinal == 1`,
  `History.Count == 5`, first history date `2026-05-15`, `LastPlayedAt` parsed,
  `UsedMidi == true`.
- `ext`: parses NX-version variant header (`NX 1.3.0`), `PlayCount == 1`,
  `BestScore == 811924`, `StageFailed`/cleared handling correct.
- `full`: mojibake Title is never consumed (proves the parser does not depend on
  decoding `Title`); drum stats parse (`BestScore == 707780`, `PlayCount == 9`).
- Missing file → `Parse` returns `null`.
- A score.ini with zero drum data → `null`.

### Importer tests (`NxScoreImporterTests`, in-memory SQLite)

- **Seed on first import:** empty score row → after merge, best/last/counts/skill match
  NX; `NxImportedPlayCount == d.PlayCount`.
- **Idempotent re-import:** running the same merge twice leaves `PlayCount`,
  `ClearCount`, best fields unchanged after the second run.
- **Delta on increased NX counts:** bump `d.PlayCount` by 3, re-merge → `PlayCount`
  increases by exactly 3.
- **CX-native higher score retained:** pre-seed a higher CX `BestScore` (no NX snapshot),
  import lower NX score → CX `BestScore` kept, but `PlayCount`/`ClearCount` still add the
  NX delta; `HighSkill`/`BestRank` take the max.
- **Rank mapping:** ordinal `1` → bucket `90` → `RankString == "S"`; ordinal `0` → `95`
  (`"SS"`); ordinal `99` → unchanged.
- **Last-play newest-wins:** CX last-play newer than NX → CX last-play fields retained;
  NX newer → NX fields applied.
- **History merge:** two charts of one song each contribute history lines → merged set is
  deduped, sorted newest-first, capped at 5, with `DisplayOrder` 1..5 and no constraint
  violation.

### Schema migration test

- An existing DB without the new columns gains them after
  `InitializeDatabaseAsync`/`ConfigureUtf8EncodingAsync`, and import succeeds.

### Test project wiring

- Add the new test files (and the `TestData/NxScores/` content) to
  `DTXMania.Test.csproj` and to the explicit `Compile Include` list in
  `DTXMania.Test.Mac.csproj` (non-graphics, safe on Mac).

## Decisions (carried-over defaults)

- **Last-play newest-wins:** NX last-play overwrites CX only when its `DateTime` is newer.
- **Achievement-rate adoption:** `BestAchievementRate` taken as a max, with the winning
  best-score run's value adopted on a score win.
- **No hash gating in v1:** sibling file path is the sole match key; a changed chart with
  a stale score.ini will import until replayed (accepted tradeoff).

## Open Questions

None blocking. Future enhancements (out of scope): Guitar/Bass import, optional
`[File] Hash` staleness guard, and surfacing a richer post-import summary screen.

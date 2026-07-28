# HPA-192 startup baseline

baseline_commit=5ea3f95d208ba7b15019429f63d7edd0bbf7009d

This diagnostic baseline was recorded before importer optimization from a
fixed Release output built from the instrumentation commit above. Task 8 must
rebuild this exact commit and rerun the baseline in its balanced, interleaved
comparison sequence; these measurements are not the final acceptance baseline.

## Environment

- Hardware: MacBookPro18,3 (Apple M1 Pro)
- macOS: 26.5.2
- .NET SDK: 10.0.100
- Corpus: `/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles`
- Supported chart files: 100 (`.dtx`, `.gda`, `.g2d`, `.bms`, `.bme`, `.bml`)
- Logical `SET.def` groups: 27
- Manifest inventory: 592 files
- Manifest SHA-256: `0c335aa79fd4045e77aff20494637313626729ba926f131822c40fa89778a78b`

The manifest records only relative paths, byte lengths, and SHA-256 values;
the third-party corpus itself remains machine-local.

## Diagnostic runs

Each run launched the same fixed Release output with a fresh app-data root,
fresh `Config.ini`, and isolated copied `System` skin. The runner waited for
`TitleStage` through `getGameState`, then terminated and waited for the game
process before recording the result.

```text
label=baseline-diagnostic run=1 wall_ms=7901 HPA192_STARTUP path=enumeration outcome=success total_ms=6207 db_init_ms=1048 discovery_parse_ms=1500 persistence_ms=0 cleanup_ms=0 hierarchy_ms=300 discovered=0 parsed=100 groups=24 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
label=baseline-diagnostic run=2 wall_ms=7904 HPA192_STARTUP path=enumeration outcome=success total_ms=6197 db_init_ms=1046 discovery_parse_ms=1500 persistence_ms=0 cleanup_ms=0 hierarchy_ms=300 discovered=0 parsed=100 groups=24 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
label=baseline-diagnostic run=3 wall_ms=7857 HPA192_STARTUP path=enumeration outcome=success total_ms=6186 db_init_ms=1054 discovery_parse_ms=1500 persistence_ms=0 cleanup_ms=0 hierarchy_ms=300 discovered=0 parsed=100 groups=24 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
```

Sorted external wall times are 7857 ms, 7901 ms, and 7904 ms. The median
external wall time is **7901 ms**.

`discovery_parse_ms` in this baseline includes the current interleaved parsing,
per-chart SQLite writes, cleanup, and reload work. The raw instrumentation
reports `groups=24`; this is the current importer timing counter, while the
frozen corpus has 27 logical `SET.def` groups. The discrepancy is retained as
diagnostic evidence for the implementation work rather than normalized here.

## Reproduction

```bash
rtk dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj -c Release \
  -o TestResults/hpa-192/builds/baseline
for run in 1 2 3; do
  rtk bash tools/hpa192/benchmark-startup.sh \
    TestResults/hpa-192/builds/baseline \
    "/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles" \
    baseline-diagnostic "$run"
done
```

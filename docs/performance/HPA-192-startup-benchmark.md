# HPA-192 final startup benchmark

baseline_commit=5ea3f95d208ba7b15019429f63d7edd0bbf7009d
optimized_commit=c8a3140dcbc2a29f99b829559f5618bdbc7d2f0b

## Acceptance result

**FAIL:** the optimized median fresh-start wall time is **3,888 ms**, compared
with the baseline median of **7,402 ms**. The measured improvement is
**47.47 percent**, below the required **70 percent**.

The separate absolute target passes: the optimized median is below 8 seconds.
No speculative product change was made after the acceptance miss.

## Environment

- Hardware: MacBookPro18,3, Apple M1 Pro, 10 cores, 32 GB memory
- Architecture: arm64
- macOS: 26.5.2 (25F84)
- .NET SDK: 10.0.100
- Microsoft.NETCore.App runtime for the net8.0 output: 8.0.17
- Corpus:
  `/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles`
- Supported chart files: 100 (`.dtx`, `.gda`, `.g2d`, `.bms`, `.bme`, `.bml`)
- `SET.def` files in the frozen corpus: 27
- Manifest inventory: 592 files
- Manifest SHA-256:
  `0c335aa79fd4045e77aff20494637313626729ba926f131822c40fa89778a78b`

The manifest was regenerated before the benchmark and compared byte-for-byte
with `docs/performance/HPA-192-corpus-manifest.tsv`. The comparison was clean.
The third-party corpus remains machine-local.

## Fixed outputs

Both Release outputs were built once before the comparison and were not
rebuilt between runs.

| Arm | Commit | Build output | Game DLL SHA-256 |
| --- | --- | --- | --- |
| Baseline | `5ea3f95d208ba7b15019429f63d7edd0bbf7009d` | `TestResults/hpa-192/builds/baseline-final` | `2c8dda2ab030f62c3ac70f325e6b21a160e7e827ca1f3b7ccf705da4541cc372` |
| Optimized | `c8a3140dcbc2a29f99b829559f5618bdbc7d2f0b` | `TestResults/hpa-192/builds/optimized-final` | `583f02e2bdff084952eefb1fd9cb1a4c4eb294401f372367caaddf7ee661133d` |

The baseline build completed with zero errors and 111 warnings. The optimized
build completed with zero errors and 113 warnings. The warnings are the
existing nullable-context and sandboxed NuGet vulnerability-cache warnings.

## Comparative protocol

The Task 2 runner was extended only to strengthen benchmark evidence:

- every invocation chooses a fresh loopback API port and accepts responses
  only when `/health` reports that invocation's unique launch token;
- every invocation creates a new temporary app-data root, `Config.ini`, and
  `songs.db`;
- the existing owner-aware lock serializes all invocations;
- after Title is reached and the game has stopped, the runner records fresh
  `SongCharts` and `Songs` counts, the database SHA-256, and sorted chart paths;
- each sorted database chart-path list must exactly match the frozen supported
  chart-path list before a result is accepted;
- exactly one raw `HPA192_STARTUP` line is required.

The final order, recorded in
`TestResults/hpa-192/comparative-order.txt`, was:

```text
order=1 label=baseline run=1
order=2 label=optimized run=1
order=3 label=optimized run=2
order=4 label=baseline run=2
order=5 label=baseline run=3
order=6 label=optimized run=3
```

The order artifact SHA-256 is
`c30a481ab0ba23e1160addf06256a49d7cc80f43325b8deb77a83931b99b94c5`.

## Raw final results

These are the complete one-line result artifacts in comparative order:

```text
label=baseline-final run=1 wall_ms=7979 HPA192_STARTUP path=enumeration outcome=success total_ms=6222 db_init_ms=1077 discovery_parse_ms=1500 persistence_ms=0 cleanup_ms=0 hierarchy_ms=300 discovered=0 parsed=100 groups=24 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
label=optimized-final run=1 wall_ms=3663 HPA192_STARTUP path=enumeration outcome=success total_ms=2012 db_init_ms=1082 discovery_parse_ms=134 persistence_ms=433 cleanup_ms=1 hierarchy_ms=3 discovered=100 parsed=100 groups=27 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
label=optimized-final run=2 wall_ms=3888 HPA192_STARTUP path=enumeration outcome=success total_ms=2144 db_init_ms=1079 discovery_parse_ms=168 persistence_ms=449 cleanup_ms=1 hierarchy_ms=4 discovered=100 parsed=100 groups=27 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
label=baseline-final run=2 wall_ms=7402 HPA192_STARTUP path=enumeration outcome=success total_ms=5670 db_init_ms=1091 discovery_parse_ms=1500 persistence_ms=0 cleanup_ms=0 hierarchy_ms=300 discovered=0 parsed=100 groups=24 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
label=baseline-final run=3 wall_ms=7393 HPA192_STARTUP path=enumeration outcome=success total_ms=5681 db_init_ms=1110 discovery_parse_ms=1500 persistence_ms=0 cleanup_ms=0 hierarchy_ms=300 discovered=0 parsed=100 groups=24 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
label=optimized-final run=3 wall_ms=3907 HPA192_STARTUP path=enumeration outcome=success total_ms=2154 db_init_ms=1124 discovery_parse_ms=143 persistence_ms=468 cleanup_ms=1 hierarchy_ms=4 discovered=100 parsed=100 groups=27 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
```

Every raw stdout log contains exactly one `HPA192_STARTUP` line. All six
outcomes are successful enumeration runs and all six reached Title.

## Fresh database and inventory evidence

```text
label=baseline-final run=1 charts=100 songs=24 database_sha256=190c153cbb915b28170a7f6e2882163935ad695eaed0f2fad9d03c361879e7e2
label=optimized-final run=1 charts=100 songs=27 database_sha256=e1ca04b40946cd7ce3500782f6753a96212804376140d824ed8af1dfb2d2e163
label=optimized-final run=2 charts=100 songs=27 database_sha256=fe7ee6228c9dba926ecb50d928f9ccb85713c8b1989b19debcfa08a50099b5ae
label=baseline-final run=2 charts=100 songs=24 database_sha256=753c30648fa3670f4dd909069025c32848fd7511b2edadc55de58e452d65361f
label=baseline-final run=3 charts=100 songs=24 database_sha256=4a960a78fc9f595376ca12d6affb7c07b5e5b984555b1a1d0f166f1eaf49f954
label=optimized-final run=3 charts=100 songs=27 database_sha256=faf18c22d70f4d37967c92f2072a56bff9393aab4ce5182d5d22585b4a4d0449
```

Every run's sorted imported chart-path artifact has the same SHA-256,
`66d92542e9557c31e228ebcd868dc1f1e7e5ca3dad060ee6961723f2bfc88067`,
and each was directly diffed against the 100 supported paths in the frozen
corpus.

The baseline's `songs=24` is not an inventory mismatch or a massaged result.
The baseline instrumentation commit used legacy summary semantics and legacy
title/artist grouping: `discovered=0`, `parsed=100`, and `groups=24`, and its
fresh database likewise contains 100 charts grouped into 24 songs. The fixed
filesystem input still contains 27 `SET.def` files. The optimized contract is
the Task 6-7 contract: `discovered` is the discovered supported-path count,
`parsed` is the import-candidate count, and `groups` is the pending authored
song-group count. It therefore reports `100`, `100`, and `27`, and persists
100 charts in 27 songs. Raw baseline values are retained because rewriting
them would invalidate the comparison.

## Calculation

Sorted external wall times:

- Baseline: 7,393 ms, **7,402 ms**, 7,979 ms
- Optimized: 3,663 ms, **3,888 ms**, 3,907 ms

```text
improvement_percent =
    100 * (baseline_median_ms - optimized_median_ms) / baseline_median_ms
  = 100 * (7402 - 3888) / 7402
  = 47.473656 percent
```

- Required median improvement of at least 70 percent: **FAIL**
- Optimized median at most 8 seconds: **PASS** (3,888 ms)

Because the 8-second target passes, no missed-target subphase diagnosis is
required. For reference, the optimized median instrumented durations are
1,082 ms database initialization, 143 ms discovery/parsing, 449 ms
persistence, 1 ms cleanup, and 4 ms hierarchy construction.

## Artifact hashes

All benchmark artifacts are machine-local under `TestResults/hpa-192`.

| Arm/run | stdout log SHA-256 | result artifact SHA-256 |
| --- | --- | --- |
| baseline/1 | `bfa8190987d46f76e2d931a237e621cbf76bcbfb9576563902083403d5c77ee6` | `0e6551e46d80e05c1ea21f706f4e3ad38cf30b3ac58991634f313649b3f4853e` |
| baseline/2 | `c35b91645bb1229cb756a726aa6cbab3001d17af25f4d42aab02c7ed0d7c9178` | `3e50ad0efd4b68360249dcc54df22c07920f4422a444ea35014f5cc67501cf6d` |
| baseline/3 | `b6ef111d7a0cd95b9d4cab057834a0700334d40fc9ed4e328ee3e554b9f3cd13` | `6a6d71404d90f54ce2a3cbd553a69aad73f3076a415157e479b3c08947ef9da0` |
| optimized/1 | `3087f3376040b74aa5fc63f99fabf8e3b27e0eaaa8adf53b37a75243aedae535` | `1f9d8d77807e67c6d463fecaa145aac269a39d3c6d7a85f7dd588ed78181e9fd` |
| optimized/2 | `ed50107886163a2758244705a0e82e287d8176b8e8f7c16f79d5eb55e6605340` | `43ada4eaaf9fe53c5955bb5d2b7200729b0bded4ef7ffd46b358fd28c7165e28` |
| optimized/3 | `5d7a2d3deb9fcdeb905107dab514eb43f3d2f28737d0df9f2227a6585eade59d` | `ea5cf00d03d257af638e2d3ee3bd2108eff84048472e7d6804cc8363dac73f83` |

## Regression and hot-path verification

- Focused persistence/startup slice: 122 passed, 0 failed.
- The exact unmodified full-suite command first produced 6,976 passes and 49
  `ManagedSoundTests` failures because no OpenAL hardware was available.
- The full macOS-safe suite with the repository's
  `ALSOFT_DRIVERS=null` workaround: 7,025 passed, 0 failed, 0 skipped.
- `ImportSongsAsync` retains one `SaveChangesAsync` call for the atomic import.
- Production startup calls `EnumerateAndImportSongsAsync` once and contains no
  legacy cache/save/statistics orchestration.
- `AddSongAsync`, `GetSongWithChartsAsync`, and
  `CleanupStaleChartsAsync` remain only as legacy service helpers; the
  production enumeration path does not call them.
- No title/artist grouping remains in the optimized database hierarchy path.

## Excluded diagnostic attempts

Two preliminary artifact namespaces are retained locally but excluded from
the six-run calculation:

- `TestResults/hpa-192/invalid-preflight` exposed that the plan's shell loop
  continued after a runner failure because it did not enable `set -e`.
- `TestResults/hpa-192/invalid-fixed-port-attempt` captured an optimized launch
  that completed its internal summary but could not bind the Task 2 fixed API
  port immediately after the preceding baseline process.

The final runner uses fail-fast orchestration, a fresh port, and launch-token
ownership validation. Only the six results in the recorded balanced order are
acceptance samples.

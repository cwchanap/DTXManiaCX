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

The following is the complete corrected, copy-paste reproduction of the fixed
builds, manifest verification, and final acceptance sequence. It uses the
exact product commits, evidence-runner hash, output locations, binary hashes,
result namespaces, and balanced order used for the selected measurements. The
script itself materializes and verifies the exact historical runner independently
of the current report checkout, verifies both detached product trees, and
requires clean build/result namespaces before doing any work:

```bash
rtk bash -lc '
set -euo pipefail

repo="/Users/chanwaichan/workspace/DTXmaniaCX/.worktrees/hpa-192-batched-import"
baseline_commit="5ea3f95d208ba7b15019429f63d7edd0bbf7009d"
optimized_commit="c8a3140dcbc2a29f99b829559f5618bdbc7d2f0b"
evidence_runner_commit="105c3baf91312961398c8c6cc7d57e36d5a8f742"
runner_sha256="ea5be8fe5b14f383c093de0db971e48d30fc11cd3d22052b7daed5fc981a1c03"
baseline_dll_sha256="2c8dda2ab030f62c3ac70f325e6b21a160e7e827ca1f3b7ccf705da4541cc372"
optimized_dll_sha256="583f02e2bdff084952eefb1fd9cb1a4c4eb294401f372367caaddf7ee661133d"
manifest_sha256="0c335aa79fd4045e77aff20494637313626729ba926f131822c40fa89778a78b"
chart_paths_sha256="66d92542e9557c31e228ebcd868dc1f1e7e5ca3dad060ee6961723f2bfc88067"
baseline_worktree="/private/tmp/dtxmania-hpa192-baseline"
optimized_worktree="/private/tmp/dtxmania-hpa192-optimized"
corpus="/Users/chanwaichan/Library/Application Support/DTXManiaCX/DTXFiles"
manifest_tmp="/private/tmp/HPA-192-corpus-manifest.tsv"
evidence_root="$repo/TestResults/hpa-192"
baseline_dir="$evidence_root/builds/baseline-final"
optimized_dir="$evidence_root/builds/optimized-final"
baseline_results="$evidence_root/baseline-final"
optimized_results="$evidence_root/optimized-final"
order_path="$evidence_root/comparative-order.txt"
baseline_worktree_created=false
optimized_worktree_created=false
historical_runner=""
historical_runner_created=false

cleanup() {
  exit_status=$?
  cleanup_failed=0
  set +e
  if [[ "$optimized_worktree_created" == true ]]; then
    rtk git -C "$repo" worktree remove "$optimized_worktree" ||
      cleanup_failed=1
  fi
  if [[ "$baseline_worktree_created" == true ]]; then
    rtk git -C "$repo" worktree remove "$baseline_worktree" ||
      cleanup_failed=1
  fi
  if [[ "$historical_runner_created" == true &&
        -n "$historical_runner" &&
        "$historical_runner" == "$repo/tools/hpa192/.hpa192-runner."* ]]; then
    rtk rm -f -- "$historical_runner" || cleanup_failed=1
  fi
  trap - EXIT
  if [[ "$exit_status" -eq 0 && "$cleanup_failed" -ne 0 ]]; then
    exit_status=1
  fi
  exit "$exit_status"
}
trap cleanup EXIT

cd "$repo"
test "$(rtk git rev-parse --show-toplevel)" = "$repo"
test -z "$(rtk proxy git status --porcelain)"
test "$(rtk git rev-parse "$evidence_runner_commit^{commit}")" = \
  "$evidence_runner_commit"
test ! -e "$baseline_worktree"
test ! -e "$optimized_worktree"
test ! -e "$baseline_dir"
test ! -e "$optimized_dir"
test ! -e "$baseline_results"
test ! -e "$optimized_results"
test ! -e "$order_path"

historical_runner="$(
  rtk mktemp "$repo/tools/hpa192/.hpa192-runner.XXXXXX"
)"
historical_runner_created=true
rtk git show \
  "$evidence_runner_commit:tools/hpa192/benchmark-startup.sh" \
  >"$historical_runner"
rtk chmod 700 "$historical_runner"
test "$(rtk shasum -a 256 "$historical_runner" |
  rtk awk "{print \$1}")" = "$runner_sha256"
rtk bash -n "$historical_runner"

rtk git worktree add --detach "$baseline_worktree" "$baseline_commit"
baseline_worktree_created=true
test "$(rtk git -C "$baseline_worktree" rev-parse HEAD)" = "$baseline_commit"
rtk git worktree add --detach "$optimized_worktree" "$optimized_commit"
optimized_worktree_created=true
test "$(rtk git -C "$optimized_worktree" rev-parse HEAD)" = "$optimized_commit"

rtk dotnet build \
  "$baseline_worktree/DTXMania.Game/DTXMania.Game.Mac.csproj" \
  -c Release -o "$baseline_dir"
rtk dotnet build \
  "$optimized_worktree/DTXMania.Game/DTXMania.Game.Mac.csproj" \
  -c Release -o "$optimized_dir"
test "$(rtk shasum -a 256 "$baseline_dir/DTXMania.Game.Mac.dll" |
  rtk awk "{print \$1}")" = "$baseline_dll_sha256"
test "$(rtk shasum -a 256 "$optimized_dir/DTXMania.Game.Mac.dll" |
  rtk awk "{print \$1}")" = "$optimized_dll_sha256"

rtk rg --files "$corpus" | rtk env LC_ALL=C sort |
  while IFS= read -r file; do
    relative="${file#"$corpus"/}"
    size="$(rtk stat -f "%z" "$file")"
    hash="$(rtk shasum -a 256 "$file" | rtk awk "{print \$1}")"
    rtk printf "%s\t%s\t%s\n" "$relative" "$size" "$hash"
  done >"$manifest_tmp"
rtk diff -u docs/performance/HPA-192-corpus-manifest.tsv "$manifest_tmp"
test "$(rtk shasum -a 256 "$manifest_tmp" |
  rtk awk "{print \$1}")" = "$manifest_sha256"
test "$(rtk rg --files "$corpus" |
  rtk rg -i "\\.(dtx|gda|g2d|bms|bme|bml)$" |
  rtk proxy wc -l | rtk tr -d " ")" = 100
test "$(rtk rg --files "$corpus" |
  rtk rg -i "(^|/)set\\.def$" |
  rtk proxy wc -l | rtk tr -d " ")" = 27

baseline_run=0
optimized_run=0
order=0
for label in baseline optimized optimized baseline baseline optimized; do
  order=$((order + 1))
  if [[ "$label" == baseline ]]; then
    baseline_run=$((baseline_run + 1))
    run="$baseline_run"
    game_dir="$baseline_dir"
  else
    optimized_run=$((optimized_run + 1))
    run="$optimized_run"
    game_dir="$optimized_dir"
  fi

  rtk bash "$historical_runner" \
    "$game_dir" "$corpus" "$label-final" "$run"
  rtk printf "order=%s label=%s run=%s\n" "$order" "$label" "$run" |
    rtk tee -a "$order_path"
done

rtk diff -u \
  <(rtk printf "%s\n" \
    "order=1 label=baseline run=1" \
    "order=2 label=optimized run=1" \
    "order=3 label=optimized run=2" \
    "order=4 label=baseline run=2" \
    "order=5 label=baseline run=3" \
    "order=6 label=optimized run=3") \
  "$order_path"

for arm in baseline optimized; do
  for run in 1 2 3; do
    result_root="$evidence_root/$arm-final"
    test -f "$result_root/run-$run.result.txt"
    test -f "$result_root/run-$run.database.txt"
    test "$(rtk shasum -a 256 "$result_root/run-$run.chart-paths.txt" |
      rtk awk "{print \$1}")" = "$chart_paths_sha256"
    test "$(rtk awk \
      "/^HPA192_STARTUP / { count++ } END { print count + 0 }" \
      "$result_root/run-$run.stdout.log")" = 1
  done
done
'
```

The temporary runner is created in `tools/hpa192` so its unmodified
repository-root resolution still targets the clean checkout containing this
report. Its bytes come from the pinned historical commit and must match the
recorded SHA-256 before it can run; the current checkout's `HEAD` and runner
file are intentionally not used as evidence inputs.

The outer `set -euo pipefail` is part of the acceptance protocol. The runner
is fail-fast for one invocation, but that does not make its caller's loop
fail-fast. The outer shell stops on the first non-zero runner result, and the
order line is appended only after that runner has reached Title and completed
its summary, database, and chart-path validations. Consequently a failed
sample cannot add an accepted order entry and no later sample can run. The
final result directories and order artifact must be absent at the start; a
failed attempt is retained as diagnostic evidence and the acceptance sequence
must restart in a new clean namespace rather than resume or overwrite it.

Fresh database/app-data creation, the owner-aware serialization lock, a fresh
ephemeral loopback port, and launch-token ownership validation are performed
inside each committed runner invocation.

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

### Second-wave sizing derivations

The second-wave design uses the following explicit derivations from the raw
first-wave evidence:

```text
required_external_reduction_ms = 3888 - 2221 = 1667

median_song_phase_sum_ms =
    1082 + 143 + 449 + 1 + 4
  = 1679

median_startup_residual_ms = 2144 - 1679 = 465

idealized_external_without_measured_song_phases_ms =
    3888 - 1679
  = 2209
```

The 2,209 ms value is an impossible best case that removes every measured
song-phase median. It is not an overlap measurement or a projected result; it
leaves only 12 ms below the 2,221 ms gate before scheduling, API polling, or
contention noise.

The paired optimized external-minus-summary intervals are:

```text
3663 - 2012 = 1651 ms
3888 - 2144 = 1744 ms
3907 - 2154 = 1753 ms
median = 1744 ms
```

That 1,744 ms median combines work before Startup activation with the explicit
one-second Startup-to-Title transition after the summary. Only the
configuration-loaded-to-Startup-activation portion is available for
coordinator overlap, and the first-wave benchmark did not record that split.

The current ten display phases advance through nine phase transitions. A
bounded loop still performs one update, so when song work is already terminal
it can remove at most eight extra update intervals. At 60 Hz the ceiling is
approximately `8 * 1000 / 60 = 133.33 ms`, not the full 465 ms residual.

The second-wave Task 0 timing diagnostic will use a telemetry-only commit
based on first-wave product `c8a3140dcbc2a29f99b829559f5618bdbc7d2f0b`.
It will record process entry, configuration loaded, `LoadContent` complete,
Startup activation, first Startup draw, summary/Title request, and Title
completion across three fresh Release runs before higher-risk implementation
begins.

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

## Excluded preliminary and diagnostic attempts

Three preliminary artifact namespaces are retained locally but excluded from
the six-run calculation:

- `TestResults/hpa-192/invalid-preflight` exposed that the plan's shell loop
  continued after a runner failure because it did not enable `set -e`.
- `TestResults/hpa-192/invalid-fixed-port-attempt` captured an optimized launch
  that completed its internal summary but could not bind the Task 2 fixed API
  port immediately after the preceding baseline process.
- `TestResults/hpa-192/optimized-investigation/run-1.result.txt` is a successful
  diagnostic from the same fixed optimized output
  (`583f02e2bdff084952eefb1fd9cb1a4c4eb294401f372367caaddf7ee661133d`):

  ```text
  label=optimized-investigation run=1 wall_ms=4504 HPA192_STARTUP path=enumeration outcome=success total_ms=2756 db_init_ms=1120 discovery_parse_ms=230 persistence_ms=433 cleanup_ms=2 hierarchy_ms=3 discovered=100 parsed=100 groups=27 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
  ```

  It is excluded solely because it preceded the predetermined clean final
  six-run namespace and balanced order. Its 4,504 ms timing was not a reason
  for exclusion and it was never eligible to replace a selected sample.

The corrected outer orchestration is fail-fast. Each runner invocation uses a
fresh port and launch-token ownership validation, but its per-invocation
failure handling does not substitute for the outer shell contract. Only the
six results in the recorded balanced order are acceptance samples.

## Deferred runner minor

**Deferred Minor:** `label` and `run` are still accepted as unchecked path
components by `benchmark-startup.sh`. This was already deferred in Task 2 and
remains outside Task 8's evidence-only scope. The reproduction command above
uses only the trusted literal labels `baseline-final` and `optimized-final`
and integer run IDs 1 through 3; it does not broaden the runner interface.

## 2026-07-28 Task 0 timing preflight (diagnostic only)

This is an instrumentation-only gate on first-wave product
`c8a3140dcbc2a29f99b829559f5618bdbc7d2f0b`, with committed timing
instrumentation `5569ba548b15c5cc515897d5a3ec31b5e88e01f3`. These samples
are excluded from the prior acceptance benchmark and must not be used as
acceptance measurements.

The fixed Release DLL SHA-256 was
`0e664047b6b5e7c560119cf0a38e68414dbb4dd5ea2271e9566198a6d0403242` and
the fixed runner SHA-256 was
`d2d92cb0ef58a690c83042d870da34ccdda05ec25713c550e90611465bf00ef5`.
The machine was MacBookPro18,3 (arm64), macOS 26.5.2 (25F84), using .NET SDK
10.0.100 and Microsoft.NETCore.App 8.0.17. The frozen 592-row corpus manifest
remained SHA-256
`0c335aa79fd4045e77aff20494637313626729ba926f131822c40fa89778a78b`.

| Run | External intervals (ms) | Internal intervals (ms) | Derived intervals (ms) |
| --- | --- | --- | --- |
| 1 | launch→entry 37; title-poll lag 113; wall 4050 | entry→config 502; config→LoadContent 27; LoadContent→Startup 139; Startup→first draw 1308; Startup→summary 2085; summary→Title 1144; entry→Title 3899 | entry→Startup 668; config→Startup 166; launch→Startup 705; fixed floor 3157 |
| 2 | launch→entry 43; title-poll lag 19; wall 3719 | entry→config 463; config→LoadContent 24; LoadContent→Startup 127; Startup→first draw 1202; Startup→summary 1912; summary→Title 1128; entry→Title 3656 | entry→Startup 614; config→Startup 151; launch→Startup 657; fixed floor 2987 |
| 3 | launch→entry 36; title-poll lag 24; wall 3782 | entry→config 482; config→LoadContent 22; LoadContent→Startup 133; Startup→first draw 1218; Startup→summary 1947; summary→Title 1136; entry→Title 3721 | entry→Startup 637; config→Startup 155; launch→Startup 673; fixed floor 3027 |

The fixed-floor calculation is:

```text
entry_to_startup = entry_to_config + config_to_load_content + load_content_to_startup
external_launch_to_entry = (entry_unix_us - launch_start_unix_us) / 1000
external_launch_to_startup = external_launch_to_entry + entry_to_startup
title_poll_lag = (launch_end_unix_us - title_unix_us) / 1000
config_to_startup = config_to_load_content + load_content_to_startup
fixed_floor = external_launch_to_startup + startup_to_first_draw + summary_to_title
```

The median fixed floor is **3027 ms**; median config→Startup (the overlapable
window) is **155 ms**. The other derived medians are launch→entry 37 ms,
launch→Startup 673 ms, Startup→first draw 1218 ms, summary→Title 1136 ms,
title-poll lag 24 ms, and wall 3782 ms. The all-in wall time is intentionally
not part of the fixed-floor formula because it ends only after HTTP polling
observes Title.

`HPA192_PREFLIGHT median_fixed_floor_ms=3027 target_ms=2221 decision=stop`

**Hard decision: stop.** The floor exceeds the 2,221 ms gate; a new measured
design is required before any second-wave product optimization. No Task 1
product work began.

### Excluded pre-fix attempts

All are retained locally and excluded: `timing-preflight-invalid-pre-fix`
contains three sandboxed graphics failures plus the first elevated run that
proved the missing BaseGame lifecycle forwarding; `timing-preflight-invalid-pre-rounding`
contains three runs that emitted timing correctly but preceded the
whole-millisecond arithmetic-bound repair. The accepted results are only the
three fresh samples in `TestResults/hpa-192/timing-preflight`.

### 2026-07-28 timing-preflight integrity hardening

Commit `a0e3ea92 fix: harden HPA-192 timing preflight` changes only the
summarizer and its synthetic shell tests: it rejects duplicate canonical input
paths, duplicate or substituted `label/run` identities, and mixed labels;
labels each output sample from the artifact identity; preserves input bytes;
and compares the process UTC-anchor elapsed time with the monotonic
`entry_to_title_ms` (50 ms maximum adjacency tolerance). It also proves that
the conventional acceptance-sequence sentinel is neither read nor written.

The runner and result-artifact schema did not change: existing artifacts
already contain `label`, `run`, the process UTC anchors, and the external UTC
anchors. Therefore no product rebuild or relaunch was needed. The exact final
three artifacts were re-summarized under the hardened gate and still produced:

```text
HPA192_PREFLIGHT median_fixed_floor_ms=3027 target_ms=2221 decision=stop
HPA192_PREFLIGHT median_config_to_startup_ms=155
```

Their unchanged SHA-256 values are:

| Artifact | SHA-256 |
| --- | --- |
| `timing-preflight/run-1.result.txt` | `6d60330313a19398ffab61bd9d45dfb5248cb81b90e7b641c3d9dc09211a0194` |
| `timing-preflight/run-2.result.txt` | `e4f2d9d37237ab2a796c64cb41b9db0a9ceac28b7b858ff68d88cefc595f297c` |
| `timing-preflight/run-3.result.txt` | `30e369bcb822feac269076b559b3a3ded711f0473bd7ee131bf0ca62ad4dda6c` |

### 2026-07-28 final whole-branch preflight review

Commit `277b10262f44e76cac5a3ea2f2ac7ca1aa83702f`
(`fix: harden HPA-192 timing anchors`) updates only the evidence runner,
summarizer, and synthetic shell tests. The result schema now records an
external `CLOCK_MONOTONIC` microsecond anchor immediately after the UTC launch
start capture and immediately before the UTC launch end capture. The preflight
retains the independent process UTC-to-monotonic check and its whole-millisecond
truncation rule, and additionally rejects an external UTC elapsed duration
whose monotonic duration differs by more than 50 ms.

Before any shell arithmetic, every numeric field must be a canonical unsigned
decimal and must be in range: timing and elapsed intervals are at most 300,000
ms; external UTC anchors are at most `4102444800000000` microseconds (UTC
2100-01-01); and monotonic anchors are at most `3155760000000000`
microseconds (100 years). The 50 ms capture tolerance covers adjacent Perl
clock reads without masking a material wall-clock step. Synthetic tests cover
an unsigned-64-bit wrap payload, signed text, each anchor just above its
bound, a timing interval just above its bound, and UTC steps both before
process entry and after process Title that the prior process-only clock check
would have accepted.

The prior accepted raw artifacts were left byte-for-byte unchanged and moved
solely into the superseded namespace
`TestResults/hpa-192/timing-preflight-superseded-final-review`. They are
superseded because they predate the external monotonic-anchor schema, not
because their result changed:

| Superseded artifact | SHA-256 |
| --- | --- |
| `run-1.result.txt` | `6d60330313a19398ffab61bd9d45dfb5248cb81b90e7b641c3d9dc09211a0194` |
| `run-2.result.txt` | `e4f2d9d37237ab2a796c64cb41b9db0a9ceac28b7b858ff68d88cefc595f297c` |
| `run-3.result.txt` | `30e369bcb822feac269076b559b3a3ded711f0473bd7ee131bf0ca62ad4dda6c` |

No product code was changed or rebuilt. The fixed product revision remains
`5569ba548b15c5cc515897d5a3ec31b5e88e01f3`; `DTXMania.Game.Mac.dll` retains
its embedded informational revision `1.0.0+5569ba548b15c5cc515897d5a3ec31b5e88e01f3`
and SHA-256
`0e664047b6b5e7c560119cf0a38e68414dbb4dd5ea2271e9566198a6d0403242`.
The committed runner and summarizer SHA-256 values are respectively
`f4f4c7251d2c6bf3173751c5c98c1b63f990241f7674dc91a20acde0e3d617aa` and
`0e02c22f00d02d319f0170310a9d4aef96eb526f99b59936cc14c035a050cc64`.

Exactly three fresh diagnostics were collected in the clean accepted namespace
`TestResults/hpa-192/timing-preflight-final-review` with
`HPA192_REQUIRE_TIMING=1`. All passed the updated independent clock checks:

| Run | External intervals (ms) | Internal intervals (ms) | Derived intervals (ms) |
| --- | --- | --- | --- |
| 1 | launch->entry 48; Title-poll lag 23; UTC wall 4534; monotonic wall 4519; delta 15 | entry->config 578; config->LoadContent 25; LoadContent->Startup 130; Startup->first draw 1284; Startup->summary 2012; summary->Title 1714; entry->Title 4462 | entry->Startup 733; config->Startup 155; launch->Startup 781; fixed floor 3779 |
| 2 | launch->entry 45; Title-poll lag 89; UTC wall 3765; monotonic wall 3750; delta 15 | entry->config 442; config->LoadContent 24; LoadContent->Startup 142; Startup->first draw 1201; Startup->summary 1894; summary->Title 1127; entry->Title 3630 | entry->Startup 608; config->Startup 166; launch->Startup 653; fixed floor 2981 |
| 3 | launch->entry 41; Title-poll lag 100; UTC wall 3782; monotonic wall 3767; delta 15 | entry->config 462; config->LoadContent 23; LoadContent->Startup 124; Startup->first draw 1187; Startup->summary 1879; summary->Title 1151; entry->Title 3641 | entry->Startup 609; config->Startup 147; launch->Startup 650; fixed floor 2988 |

The raw artifact and derived-summary hashes are:

| Fresh artifact | SHA-256 |
| --- | --- |
| `run-1.result.txt` | `7785f000c524e26ef0e4c0ecc952b57d36e11e8d47fdb3a333d8f91dd4ae17ab` |
| `run-2.result.txt` | `9fde9b3fe91265c3d588e00b936ffe01cecb42f6d293bf62df945e16af8e0dc7` |
| `run-3.result.txt` | `5824c0aa1230c4289daaa2e546bded6a0009ed5661220a9a48757d24a53e9c21` |
| `summary.txt` | `cd6ef4d6784eeb18f8ab21f2004355b20feee4962484fcbf8e9f5d22e2de370a` |

`HPA192_PREFLIGHT median_fixed_floor_ms=2988 target_ms=2221 decision=stop`

`HPA192_PREFLIGHT median_config_to_startup_ms=155`

**Hard decision: stop.** The new median fixed floor is 767 ms over the 2,221
ms gate. The external monotonic validation strengthens the evidence but does
not broaden Task 0; no Task 1 work started.

## 2026-07-29 HPA-192 startup critical-path diagnostic

### Scope and endpoint

This is macOS-only diagnostic evidence from the pinned benchmark machine. It
is not final HPA-192 acceptance and makes no cross-platform performance claim.
The fixed configurations set `EnableGameApi=False`; the runner neither started
nor polled the Game API and performed no HTTP, input, or screenshot operation.

The accepted endpoint is the first completed render-target-to-backbuffer copy
for a non-transitioning Title stage. It is backbuffer-composition readiness,
not presented-frame latency. It excludes `CompleteBaseDraw`, framework
`EndDraw` / `Present`, the application-side buffer swap and any vsync wait,
the platform compositor, and physical-display presentation.

The retained Task 0 timing-preflight values above used an earlier
`TitleCompleted` endpoint and an enabled, polled Game API. Those values are
useful historical context but are not arithmetically comparable with this
diagnostic and do not enter any table or savings calculation below.

The scenarios were:

- A: fresh database, the frozen 100-chart corpus, 27 logical songs;
- B: fresh database, an empty immutable song directory; and
- C: the hashed preinitialized database seed, the same frozen 100-chart
  corpus, and normal forced enumeration.

### Frozen revision, environment, and identities

The fixed diagnostic source and Release output were:

```text
source_commit=d3944225bdb5b5bb8e6764b83b98dcb45ca3a810
game_dll_sha256=ce63d361a460866a282b37443add5c03297b0e24b71fdb63feddd74e37cf9ae9
```

The exact machine and runtime identity copied from the immutable environment
record was:

```text
machine=MacBook Pro
model_identifier=MacBookPro18,3
model_number=Z15G0018PZP/A
chip=Apple M1 Pro
cores=10 (8 Performance and 2 Efficiency)
memory=32 GB
os=macOS 26.5.2 (25F84)
architecture=arm64
active_dotnet_sdk=10.0.100
active_dotnet_sdk_commit=b0f34d51fc
installed_dotnet_sdk_8=8.0.411
net8_runtime=Microsoft.NETCore.App 8.0.17
```

The fixed tools and inputs were:

| Fixed item | SHA-256 |
| --- | --- |
| `build/DTXMania.Game.Mac.dll` | `ce63d361a460866a282b37443add5c03297b0e24b71fdb63feddd74e37cf9ae9` |
| `tools/hpa192/benchmark-critical-path.sh` | `db092c5741be64503c2b8b526f8ed890a50e339a55922dd57fb5fe52c5f322f7` |
| `tools/hpa192/summarize-critical-path.sh` | `4605a908b0e451110359f76a5010331ed007ec52b40d50c8312ea48748eb2d89` |
| `tools/hpa192/test-critical-path.sh` | `e3719446aa72480d5b57ee04d0ec49468f3bf031d1c55ee408a162f60562d7f1` |
| `corpus-manifest.tsv` (592 entries) | `0c335aa79fd4045e77aff20494637313626729ba926f131822c40fa89778a78b` |
| `system-manifest.tsv` (415 entries) | `a18965305cf5c1532496da869dce22644a2a9cfc0002c5b93e22b9cc46d974b0` |
| `empty-manifest.tsv` (zero bytes) | `e3b0c44298fc1c149afbf4c8996fb92427ae41e4649b934ca495991b7852b855` |
| `seed/manifest.tsv` | `b0b41a10957cbe2b819cd7aed0e4226f7b9d445437ffc4cb746763447b5ee6ca` |
| `configs/A.Config.ini` | `c40173790c8f7d2c93f8cb1762605e2c756a21ff9a2f797313fca0650f18535a` |
| `configs/B.Config.ini` | `9aa883d44d49c5ff5ab5d0e2d38c1814ee1b6a63a4bb65110996a79e19dec6e5` |
| `configs/C.Config.ini` | `c40173790c8f7d2c93f8cb1762605e2c756a21ff9a2f797313fca0650f18535a` |
| `fixed-inputs.txt` | `4f2030c00675495ab83a4c38650ac6af3c9bb8ece55fb21c2490235128fc0e71` |
| `fixed-identities.txt` | `d774bea416bdfeb339d20e982585ad726bc3affd063bbf3af577b8c166a957da` |
| `environment.txt` | `71d478cda377caca5ed99e10fd76c351ad095e25c40ee53f495fdf2ab6a1c811` |
| `accepted-artifacts.txt` | `0f25da4ebe2915da1f7a0d5132c08b31bff39ff56a07b9ffa6ee2eaf8e0e5616` |
| `summary.txt` | `854e9ad9c7179d87597f8f73a96449b35748325a409c24334785a025950b13b4` |
| `artifact-manifest.tsv` (181 rows; excludes itself) | `0fa99753023093c2eaabaf5131abf2b43da4b5df451299f40a844ce126fe73cc` |

The corpus path set also hashed to
`66d92542e9557c31e228ebcd868dc1f1e7e5ca3dad060ee6961723f2bfc88067`;
the empty path set hashed to the empty SHA-256 above. The seed contained 100
charts and 27 songs and had no live SQLite WAL or SHM file.

### Attempt ledger

Artifact paths in this section are relative to
`TestResults/hpa-192/critical-path-final`.

| Slot | Scenario | Accepted attempt | Artifact path | Artifact SHA-256 |
| ---: | :---: | ---: | --- | --- |
| 01 | A | 1 | `slots/01-A/attempt-1/result.txt` | `4de213270d1275b3daf7ae8b7948834596f2ead7a50dfe8da7f8b321a5cad15c` |
| 02 | B | 1 | `slots/02-B/attempt-1/result.txt` | `0cde14939adc75637ef40a953d5f9691b3d0fb5fde6619dc9f1f8ce45459e7ed` |
| 03 | C | 1 | `slots/03-C/attempt-1/result.txt` | `d222c7c7a563ccdca7eb3a836128ebd40dc3e8f25c679a88b60396c1e71b5397` |
| 04 | B | 1 | `slots/04-B/attempt-1/result.txt` | `4f8e89a9ba2d0511a558fb11518b123344149b0c265a3c7e8c941ce892a45e8f` |
| 05 | C | 1 | `slots/05-C/attempt-1/result.txt` | `a31818894e931a914d1b5c48956bf8ecf6e273afa938f7ff8764b96ec3a60884` |
| 06 | A | 1 | `slots/06-A/attempt-1/result.txt` | `3982ee99b611a287cb2b10a7cf868ffd48b26989db6a774fe5dac4d603c25b95` |
| 07 | C | 1 | `slots/07-C/attempt-1/result.txt` | `a963dd456b46382ba3ca5cb01463e8ec3a819531e3f384fe94da87e349830f5e` |
| 08 | A | 2 | `slots/08-A/attempt-2/result.txt` | `fba203c06df98c43f477cc389e5d484e656f8e67f866f0e376b2d627e68561b3` |
| 09 | B | 1 | `slots/09-B/attempt-1/result.txt` | `3b6bfd1f3214199b3587a997dbde9beda35b80a94b68023234ca02e646e7080a` |
| 10 | A | 1 | `slots/10-A/attempt-1/result.txt` | `cf8eb13ba90fc8913323acf28c68cafc1c1cfeafea301a23df40ec9e7b0f85d9` |
| 11 | C | 1 | `slots/11-C/attempt-1/result.txt` | `a6ea4a2bcb865665050c1934b19cfbd9faf61884f084e40638e276faf3e3830a` |
| 12 | B | 1 | `slots/12-B/attempt-1/result.txt` | `3865cf69ed299bb8d656db0bdc9de0617e01f27be47b879861a88fda83698768` |
| 13 | C | 1 | `slots/13-C/attempt-1/result.txt` | `0b0a4739d778b65c72fece16c176d4b7006dde12fa1b46f88eeb91b522c82401` |
| 14 | B | 2 | `slots/14-B/attempt-2/result.txt` | `0ccaa59187ffd311772a50307aa42d20ec2420777c835166c9ecf5a4faf9d0d2` |
| 15 | A | 1 | `slots/15-A/attempt-1/result.txt` | `77822ee3d3edfb8a27c789969d87a21c6ae1da5e0e2580f4a9a8bd0aebaa30d2` |

The accepted order is exactly:

```text
A, B, C, B, C, A, C, A, B, A, C, B, C, B, A
```

Two attempts were retained and excluded from every timing calculation:

| Slot | Scenario | Attempt | Exact rejection reason | Artifact path | Artifact SHA-256 |
| ---: | :---: | ---: | --- | --- | --- |
| 08 | A | 1 | `load_content_rounding` | `slots/08-A/attempt-1/result.txt` | `af52e9d199d9a08e52efe5255b37b4ba117ebf4ef77968046c1e33d0a8def0d2` |
| 14 | B | 1 | `load_content_rounding` | `slots/14-B/attempt-1/result.txt` | `bc2b04db84328a6399336929826b2e442292ac5e6e843e17bf4cd14101dd3d1f` |

No slot reached a third attempt.

### Accepted raw product evidence

The lines below are copied byte-for-byte from each accepted result artifact.

<details>
<summary>Complete raw lines for 15 accepted artifacts</summary>

#### 01-A attempt-1

Artifact: `slots/01-A/attempt-1/result.txt`
SHA-256: `4de213270d1275b3daf7ae8b7948834596f2ead7a50dfe8da7f8b321a5cad15c`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=2752 db_init_ms=1203 discovery_parse_ms=159 persistence_ms=455 cleanup_ms=1 hierarchy_ms=6 discovered=100 parsed=100 groups=27 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=501 config_to_load_content_ms=26 load_content_to_startup_ms=197 startup_to_first_draw_ms=1909 startup_to_summary_ms=2639 summary_to_title_ms=1142 entry_to_title_ms=4507 entry_unix_us=1785357664328258 title_unix_us=1785357668835810
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357664328258 title_backbuffer_unix_us=1785357668837614 entry_to_title_backbuffer_ms=4509 load_content_complete_from_entry_ms=528 startup_construct_begin_from_entry_ms=533 startup_construct_end_from_entry_ms=555 startup_activate_begin_from_entry_ms=559 startup_activation_from_entry_ms=725 startup_activate_end_from_entry_ms=725 load_content_return_from_entry_ms=726 base_initialize_return_from_entry_ms=726 input_manager_begin_from_entry_ms=726 input_manager_end_from_entry_ms=1343 saved_bindings_begin_from_entry_ms=1343 saved_bindings_end_from_entry_ms=1358 graphics_initialize_begin_from_entry_ms=1358 graphics_initialize_end_from_entry_ms=1358 render_target_begin_from_entry_ms=1365 render_target_end_from_entry_ms=1366 initialize_complete_from_entry_ms=1366 post_load_unattributed_ms=17 startup_first_update_begin_from_entry_ms=1378 startup_first_update_end_from_entry_ms=1380 startup_first_draw_begin_from_entry_ms=2613 startup_first_draw_end_from_entry_ms=2635 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=44 db_invoke_from_entry_ms=1407 db_task_return_from_entry_ms=2610 db_terminal_from_entry_ms=2610 db_observed_from_entry_ms=2610 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=2650 enumeration_task_return_from_entry_ms=2672 enumeration_terminal_from_entry_ms=3326 enumeration_observed_from_entry_ms=3328 enumeration_task_returned_terminal=0 enumeration_unattributed_ms=57 db_service_setup_ms=48 db_corruption_probe_ms=735 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=202 db_encoding_pragmas_ms=5 db_version_work_ms=3 db_schema_ensures_ms=198 db_init_unattributed_ms=12 summary_request_from_entry_ms=3365 title_construct_begin_from_entry_ms=3365 title_construct_end_from_entry_ms=3365 transition_start_from_entry_ms=3365 transition_complete_from_entry_ms=4362 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=4362 startup_deactivate_end_from_entry_ms=4363 title_activate_begin_from_entry_ms=4363 title_activate_end_from_entry_ms=4505 title_first_update_begin_from_entry_ms=4505 title_first_update_end_from_entry_ms=4507 title_stage_draw_begin_from_entry_ms=4508 title_stage_draw_end_from_entry_ms=4509 title_backbuffer_blit_begin_from_entry_ms=4509 title_backbuffer_blit_end_from_entry_ms=4509 summary_to_title_unattributed_ms=1 title_gpu_setup_ms=0 title_background_ms=26 title_menu_ms=9 title_font_ms=0 title_cursor_sound_ms=104 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=3 title_backbuffer_published=1
```

#### 02-B attempt-1

Artifact: `slots/02-B/attempt-1/result.txt`
SHA-256: `0cde14939adc75637ef40a953d5f9691b3d0fb5fde6619dc9f1f8ce45459e7ed`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=3139 db_init_ms=1138 discovery_parse_ms=6 persistence_ms=150 cleanup_ms=2 hierarchy_ms=1 discovered=0 parsed=0 groups=0 added=0 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=421 config_to_load_content_ms=26 load_content_to_startup_ms=141 startup_to_first_draw_ms=2846 startup_to_summary_ms=3071 summary_to_title_ms=541 entry_to_title_ms=4203 entry_unix_us=1785357673912173 title_unix_us=1785357678115578
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357673912173 title_backbuffer_unix_us=1785357678117403 entry_to_title_backbuffer_ms=4205 load_content_complete_from_entry_ms=448 startup_construct_begin_from_entry_ms=452 startup_construct_end_from_entry_ms=467 startup_activate_begin_from_entry_ms=471 startup_activation_from_entry_ms=589 startup_activate_end_from_entry_ms=589 load_content_return_from_entry_ms=590 base_initialize_return_from_entry_ms=590 input_manager_begin_from_entry_ms=591 input_manager_end_from_entry_ms=2206 saved_bindings_begin_from_entry_ms=2206 saved_bindings_end_from_entry_ms=2222 graphics_initialize_begin_from_entry_ms=2222 graphics_initialize_end_from_entry_ms=2222 render_target_begin_from_entry_ms=2228 render_target_end_from_entry_ms=2229 initialize_complete_from_entry_ms=2229 post_load_unattributed_ms=16 startup_first_update_begin_from_entry_ms=2241 startup_first_update_end_from_entry_ms=2243 startup_first_draw_begin_from_entry_ms=3414 startup_first_draw_end_from_entry_ms=3436 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=1 db_invoke_from_entry_ms=2273 db_task_return_from_entry_ms=3411 db_terminal_from_entry_ms=3411 db_observed_from_entry_ms=3411 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=3450 enumeration_task_return_from_entry_ms=3658 enumeration_terminal_from_entry_ms=3657 enumeration_observed_from_entry_ms=3658 enumeration_task_returned_terminal=1 enumeration_unattributed_ms=52 db_service_setup_ms=23 db_corruption_probe_ms=693 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=209 db_encoding_pragmas_ms=5 db_version_work_ms=3 db_schema_ensures_ms=193 db_init_unattributed_ms=12 summary_request_from_entry_ms=3661 title_construct_begin_from_entry_ms=3661 title_construct_end_from_entry_ms=3661 transition_start_from_entry_ms=3662 transition_complete_from_entry_ms=4044 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=4044 startup_deactivate_end_from_entry_ms=4046 title_activate_begin_from_entry_ms=4046 title_activate_end_from_entry_ms=4201 title_first_update_begin_from_entry_ms=4201 title_first_update_end_from_entry_ms=4203 title_stage_draw_begin_from_entry_ms=4204 title_stage_draw_end_from_entry_ms=4205 title_backbuffer_blit_begin_from_entry_ms=4205 title_backbuffer_blit_end_from_entry_ms=4205 summary_to_title_unattributed_ms=2 title_gpu_setup_ms=0 title_background_ms=26 title_menu_ms=9 title_font_ms=0 title_cursor_sound_ms=116 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=4 title_backbuffer_published=1
```

#### 03-C attempt-1

Artifact: `slots/03-C/attempt-1/result.txt`
SHA-256: `d222c7c7a563ccdca7eb3a836128ebd40dc3e8f25c679a88b60396c1e71b5397`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=4682 db_init_ms=1211 discovery_parse_ms=166 persistence_ms=438 cleanup_ms=1 hierarchy_ms=4 discovered=100 parsed=100 groups=27 added=0 updated=0 preserved=100 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=458 config_to_load_content_ms=25 load_content_to_startup_ms=143 startup_to_first_draw_ms=3919 startup_to_summary_ms=4613 summary_to_title_ms=1135 entry_to_title_ms=6375 entry_unix_us=1785357683283578 title_unix_us=1785357689659323
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357683283578 title_backbuffer_unix_us=1785357689661170 entry_to_title_backbuffer_ms=6377 load_content_complete_from_entry_ms=484 startup_construct_begin_from_entry_ms=488 startup_construct_end_from_entry_ms=502 startup_activate_begin_from_entry_ms=506 startup_activation_from_entry_ms=626 startup_activate_end_from_entry_ms=626 load_content_return_from_entry_ms=627 base_initialize_return_from_entry_ms=627 input_manager_begin_from_entry_ms=628 input_manager_end_from_entry_ms=3246 saved_bindings_begin_from_entry_ms=3246 saved_bindings_end_from_entry_ms=3261 graphics_initialize_begin_from_entry_ms=3261 graphics_initialize_end_from_entry_ms=3261 render_target_begin_from_entry_ms=3268 render_target_end_from_entry_ms=3269 initialize_complete_from_entry_ms=3269 post_load_unattributed_ms=17 startup_first_update_begin_from_entry_ms=3282 startup_first_update_end_from_entry_ms=3284 startup_first_draw_begin_from_entry_ms=4525 startup_first_draw_end_from_entry_ms=4546 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=42 db_invoke_from_entry_ms=3311 db_task_return_from_entry_ms=4522 db_terminal_from_entry_ms=4522 db_observed_from_entry_ms=4522 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=4559 enumeration_task_return_from_entry_ms=4578 enumeration_terminal_from_entry_ms=5194 enumeration_observed_from_entry_ms=5204 enumeration_task_returned_terminal=0 enumeration_unattributed_ms=27 db_service_setup_ms=24 db_corruption_probe_ms=1106 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=8 db_encoding_pragmas_ms=8 db_version_work_ms=2 db_schema_ensures_ms=52 db_init_unattributed_ms=11 summary_request_from_entry_ms=5240 title_construct_begin_from_entry_ms=5240 title_construct_end_from_entry_ms=5240 transition_start_from_entry_ms=5240 transition_complete_from_entry_ms=6237 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=6237 startup_deactivate_end_from_entry_ms=6238 title_activate_begin_from_entry_ms=6238 title_activate_end_from_entry_ms=6373 title_first_update_begin_from_entry_ms=6373 title_first_update_end_from_entry_ms=6375 title_stage_draw_begin_from_entry_ms=6376 title_stage_draw_end_from_entry_ms=6377 title_backbuffer_blit_begin_from_entry_ms=6377 title_backbuffer_blit_end_from_entry_ms=6377 summary_to_title_unattributed_ms=1 title_gpu_setup_ms=0 title_background_ms=25 title_menu_ms=8 title_font_ms=0 title_cursor_sound_ms=97 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=5 title_backbuffer_published=1
```

#### 04-B attempt-1

Artifact: `slots/04-B/attempt-1/result.txt`
SHA-256: `4f8e89a9ba2d0511a558fb11518b123344149b0c265a3c7e8c941ce892a45e8f`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=3122 db_init_ms=1113 discovery_parse_ms=6 persistence_ms=151 cleanup_ms=1 hierarchy_ms=1 discovered=0 parsed=0 groups=0 added=0 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=451 config_to_load_content_ms=24 load_content_to_startup_ms=139 startup_to_first_draw_ms=2829 startup_to_summary_ms=3054 summary_to_title_ms=551 entry_to_title_ms=4222 entry_unix_us=1785357694933703 title_unix_us=1785357699155653
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357694933703 title_backbuffer_unix_us=1785357699157657 entry_to_title_backbuffer_ms=4224 load_content_complete_from_entry_ms=476 startup_construct_begin_from_entry_ms=481 startup_construct_end_from_entry_ms=494 startup_activate_begin_from_entry_ms=498 startup_activation_from_entry_ms=615 startup_activate_end_from_entry_ms=615 load_content_return_from_entry_ms=616 base_initialize_return_from_entry_ms=616 input_manager_begin_from_entry_ms=616 input_manager_end_from_entry_ms=2241 saved_bindings_begin_from_entry_ms=2241 saved_bindings_end_from_entry_ms=2256 graphics_initialize_begin_from_entry_ms=2256 graphics_initialize_end_from_entry_ms=2256 render_target_begin_from_entry_ms=2263 render_target_end_from_entry_ms=2264 initialize_complete_from_entry_ms=2264 post_load_unattributed_ms=17 startup_first_update_begin_from_entry_ms=2277 startup_first_update_end_from_entry_ms=2279 startup_first_draw_begin_from_entry_ms=3424 startup_first_draw_end_from_entry_ms=3445 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=1 db_invoke_from_entry_ms=2307 db_task_return_from_entry_ms=3421 db_terminal_from_entry_ms=3420 db_observed_from_entry_ms=3421 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=3457 enumeration_task_return_from_entry_ms=3667 enumeration_terminal_from_entry_ms=3667 enumeration_observed_from_entry_ms=3667 enumeration_task_returned_terminal=1 enumeration_unattributed_ms=51 db_service_setup_ms=24 db_corruption_probe_ms=681 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=197 db_encoding_pragmas_ms=5 db_version_work_ms=3 db_schema_ensures_ms=191 db_init_unattributed_ms=12 summary_request_from_entry_ms=3670 title_construct_begin_from_entry_ms=3670 title_construct_end_from_entry_ms=3670 transition_start_from_entry_ms=3670 transition_complete_from_entry_ms=4052 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=4052 startup_deactivate_end_from_entry_ms=4053 title_activate_begin_from_entry_ms=4053 title_activate_end_from_entry_ms=4219 title_first_update_begin_from_entry_ms=4220 title_first_update_end_from_entry_ms=4222 title_stage_draw_begin_from_entry_ms=4222 title_stage_draw_end_from_entry_ms=4224 title_backbuffer_blit_begin_from_entry_ms=4224 title_backbuffer_blit_end_from_entry_ms=4224 summary_to_title_unattributed_ms=1 title_gpu_setup_ms=0 title_background_ms=27 title_menu_ms=8 title_font_ms=0 title_cursor_sound_ms=127 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=4 title_backbuffer_published=1
```

#### 05-C attempt-1

Artifact: `slots/05-C/attempt-1/result.txt`
SHA-256: `a31818894e931a914d1b5c48956bf8ecf6e273afa938f7ff8764b96ec3a60884`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=4641 db_init_ms=1190 discovery_parse_ms=167 persistence_ms=422 cleanup_ms=1 hierarchy_ms=4 discovered=100 parsed=100 groups=27 added=0 updated=0 preserved=100 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=445 config_to_load_content_ms=25 load_content_to_startup_ms=146 startup_to_first_draw_ms=3890 startup_to_summary_ms=4567 summary_to_title_ms=1150 entry_to_title_ms=6335 entry_unix_us=1785357704453374 title_unix_us=1785357710788625
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357704453374 title_backbuffer_unix_us=1785357710790589 entry_to_title_backbuffer_ms=6337 load_content_complete_from_entry_ms=470 startup_construct_begin_from_entry_ms=475 startup_construct_end_from_entry_ms=489 startup_activate_begin_from_entry_ms=493 startup_activation_from_entry_ms=616 startup_activate_end_from_entry_ms=616 load_content_return_from_entry_ms=617 base_initialize_return_from_entry_ms=617 input_manager_begin_from_entry_ms=618 input_manager_end_from_entry_ms=3229 saved_bindings_begin_from_entry_ms=3229 saved_bindings_end_from_entry_ms=3244 graphics_initialize_begin_from_entry_ms=3244 graphics_initialize_end_from_entry_ms=3244 render_target_begin_from_entry_ms=3250 render_target_end_from_entry_ms=3252 initialize_complete_from_entry_ms=3252 post_load_unattributed_ms=17 startup_first_update_begin_from_entry_ms=3264 startup_first_update_end_from_entry_ms=3267 startup_first_draw_begin_from_entry_ms=4486 startup_first_draw_end_from_entry_ms=4507 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=41 db_invoke_from_entry_ms=3292 db_task_return_from_entry_ms=4483 db_terminal_from_entry_ms=4482 db_observed_from_entry_ms=4483 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=4520 enumeration_task_return_from_entry_ms=4542 enumeration_terminal_from_entry_ms=5142 enumeration_observed_from_entry_ms=5148 enumeration_task_returned_terminal=0 enumeration_unattributed_ms=31 db_service_setup_ms=22 db_corruption_probe_ms=1086 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=8 db_encoding_pragmas_ms=8 db_version_work_ms=2 db_schema_ensures_ms=51 db_init_unattributed_ms=13 summary_request_from_entry_ms=5184 title_construct_begin_from_entry_ms=5184 title_construct_end_from_entry_ms=5184 transition_start_from_entry_ms=5185 transition_complete_from_entry_ms=6182 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=6182 startup_deactivate_end_from_entry_ms=6183 title_activate_begin_from_entry_ms=6183 title_activate_end_from_entry_ms=6333 title_first_update_begin_from_entry_ms=6333 title_first_update_end_from_entry_ms=6335 title_stage_draw_begin_from_entry_ms=6336 title_stage_draw_end_from_entry_ms=6337 title_backbuffer_blit_begin_from_entry_ms=6337 title_backbuffer_blit_end_from_entry_ms=6337 summary_to_title_unattributed_ms=2 title_gpu_setup_ms=0 title_background_ms=26 title_menu_ms=8 title_font_ms=0 title_cursor_sound_ms=111 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=5 title_backbuffer_published=1
```

#### 06-A attempt-1

Artifact: `slots/06-A/attempt-1/result.txt`
SHA-256: `3982ee99b611a287cb2b10a7cf868ffd48b26989db6a774fe5dac4d603c25b95`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=2780 db_init_ms=1137 discovery_parse_ms=175 persistence_ms=555 cleanup_ms=1 hierarchy_ms=6 discovered=100 parsed=100 groups=27 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=479 config_to_load_content_ms=25 load_content_to_startup_ms=162 startup_to_first_draw_ms=1853 startup_to_summary_ms=2698 summary_to_title_ms=1160 entry_to_title_ms=4526 entry_unix_us=1785357716912260 title_unix_us=1785357721438301
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357716912260 title_backbuffer_unix_us=1785357721440426 entry_to_title_backbuffer_ms=4528 load_content_complete_from_entry_ms=505 startup_construct_begin_from_entry_ms=510 startup_construct_end_from_entry_ms=525 startup_activate_begin_from_entry_ms=530 startup_activation_from_entry_ms=667 startup_activate_end_from_entry_ms=667 load_content_return_from_entry_ms=668 base_initialize_return_from_entry_ms=668 input_manager_begin_from_entry_ms=668 input_manager_end_from_entry_ms=1291 saved_bindings_begin_from_entry_ms=1291 saved_bindings_end_from_entry_ms=1306 graphics_initialize_begin_from_entry_ms=1306 graphics_initialize_end_from_entry_ms=1306 render_target_begin_from_entry_ms=1313 render_target_end_from_entry_ms=1314 initialize_complete_from_entry_ms=1314 post_load_unattributed_ms=18 startup_first_update_begin_from_entry_ms=1327 startup_first_update_end_from_entry_ms=1329 startup_first_draw_begin_from_entry_ms=2499 startup_first_draw_end_from_entry_ms=2520 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=51 db_invoke_from_entry_ms=1359 db_task_return_from_entry_ms=2496 db_terminal_from_entry_ms=2496 db_observed_from_entry_ms=2496 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=2535 enumeration_task_return_from_entry_ms=2555 enumeration_terminal_from_entry_ms=3324 enumeration_observed_from_entry_ms=3329 enumeration_task_returned_terminal=0 enumeration_unattributed_ms=54 db_service_setup_ms=23 db_corruption_probe_ms=698 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=206 db_encoding_pragmas_ms=5 db_version_work_ms=3 db_schema_ensures_ms=191 db_init_unattributed_ms=11 summary_request_from_entry_ms=3366 title_construct_begin_from_entry_ms=3366 title_construct_end_from_entry_ms=3366 transition_start_from_entry_ms=3366 transition_complete_from_entry_ms=4362 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=4362 startup_deactivate_end_from_entry_ms=4364 title_activate_begin_from_entry_ms=4364 title_activate_end_from_entry_ms=4524 title_first_update_begin_from_entry_ms=4524 title_first_update_end_from_entry_ms=4526 title_stage_draw_begin_from_entry_ms=4526 title_stage_draw_end_from_entry_ms=4528 title_backbuffer_blit_begin_from_entry_ms=4528 title_backbuffer_blit_end_from_entry_ms=4528 summary_to_title_unattributed_ms=0 title_gpu_setup_ms=0 title_background_ms=26 title_menu_ms=8 title_font_ms=0 title_cursor_sound_ms=121 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=5 title_backbuffer_published=1
```

#### 07-C attempt-1

Artifact: `slots/07-C/attempt-1/result.txt`
SHA-256: `a963dd456b46382ba3ca5cb01463e8ec3a819531e3f384fe94da87e349830f5e`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=3671 db_init_ms=1196 discovery_parse_ms=159 persistence_ms=441 cleanup_ms=1 hierarchy_ms=4 discovered=100 parsed=100 groups=27 added=0 updated=0 preserved=100 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=451 config_to_load_content_ms=23 load_content_to_startup_ms=146 startup_to_first_draw_ms=2903 startup_to_summary_ms=3598 summary_to_title_ms=1155 entry_to_title_ms=5374 entry_unix_us=1785357726831000 title_unix_us=1785357732205094
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357726831000 title_backbuffer_unix_us=1785357732207022 entry_to_title_backbuffer_ms=5376 load_content_complete_from_entry_ms=475 startup_construct_begin_from_entry_ms=479 startup_construct_end_from_entry_ms=492 startup_activate_begin_from_entry_ms=496 startup_activation_from_entry_ms=621 startup_activate_end_from_entry_ms=621 load_content_return_from_entry_ms=621 base_initialize_return_from_entry_ms=621 input_manager_begin_from_entry_ms=622 input_manager_end_from_entry_ms=2240 saved_bindings_begin_from_entry_ms=2240 saved_bindings_end_from_entry_ms=2255 graphics_initialize_begin_from_entry_ms=2255 graphics_initialize_end_from_entry_ms=2255 render_target_begin_from_entry_ms=2261 render_target_end_from_entry_ms=2262 initialize_complete_from_entry_ms=2262 post_load_unattributed_ms=15 startup_first_update_begin_from_entry_ms=2274 startup_first_update_end_from_entry_ms=2276 startup_first_draw_begin_from_entry_ms=3504 startup_first_draw_end_from_entry_ms=3524 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=42 db_invoke_from_entry_ms=2304 db_task_return_from_entry_ms=3501 db_terminal_from_entry_ms=3501 db_observed_from_entry_ms=3501 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=3538 enumeration_task_return_from_entry_ms=3558 enumeration_terminal_from_entry_ms=4168 enumeration_observed_from_entry_ms=4182 enumeration_task_returned_terminal=0 enumeration_unattributed_ms=26 db_service_setup_ms=23 db_corruption_probe_ms=1094 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=7 db_encoding_pragmas_ms=8 db_version_work_ms=2 db_schema_ensures_ms=50 db_init_unattributed_ms=13 summary_request_from_entry_ms=4219 title_construct_begin_from_entry_ms=4219 title_construct_end_from_entry_ms=4219 transition_start_from_entry_ms=4219 transition_complete_from_entry_ms=5216 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=5216 startup_deactivate_end_from_entry_ms=5217 title_activate_begin_from_entry_ms=5217 title_activate_end_from_entry_ms=5372 title_first_update_begin_from_entry_ms=5372 title_first_update_end_from_entry_ms=5374 title_stage_draw_begin_from_entry_ms=5374 title_stage_draw_end_from_entry_ms=5376 title_backbuffer_blit_begin_from_entry_ms=5376 title_backbuffer_blit_end_from_entry_ms=5376 summary_to_title_unattributed_ms=0 title_gpu_setup_ms=0 title_background_ms=27 title_menu_ms=9 title_font_ms=0 title_cursor_sound_ms=114 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=5 title_backbuffer_published=1
```

#### 08-A attempt-2

Artifact: `slots/08-A/attempt-2/result.txt`
SHA-256: `fba203c06df98c43f477cc389e5d484e656f8e67f866f0e376b2d627e68561b3`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=3675 db_init_ms=1118 discovery_parse_ms=153 persistence_ms=501 cleanup_ms=1 hierarchy_ms=6 discovered=100 parsed=100 groups=27 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=481 config_to_load_content_ms=25 load_content_to_startup_ms=142 startup_to_first_draw_ms=2828 startup_to_summary_ms=3605 summary_to_title_ms=1141 entry_to_title_ms=5396 entry_unix_us=1785357747985825 title_unix_us=1785357753382287
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357747985825 title_backbuffer_unix_us=1785357753384176 entry_to_title_backbuffer_ms=5398 load_content_complete_from_entry_ms=507 startup_construct_begin_from_entry_ms=512 startup_construct_end_from_entry_ms=526 startup_activate_begin_from_entry_ms=530 startup_activation_from_entry_ms=649 startup_activate_end_from_entry_ms=649 load_content_return_from_entry_ms=649 base_initialize_return_from_entry_ms=649 input_manager_begin_from_entry_ms=650 input_manager_end_from_entry_ms=2270 saved_bindings_begin_from_entry_ms=2270 saved_bindings_end_from_entry_ms=2285 graphics_initialize_begin_from_entry_ms=2285 graphics_initialize_end_from_entry_ms=2285 render_target_begin_from_entry_ms=2290 render_target_end_from_entry_ms=2292 initialize_complete_from_entry_ms=2292 post_load_unattributed_ms=15 startup_first_update_begin_from_entry_ms=2305 startup_first_update_end_from_entry_ms=2307 startup_first_draw_begin_from_entry_ms=3457 startup_first_draw_end_from_entry_ms=3478 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=47 db_invoke_from_entry_ms=2336 db_task_return_from_entry_ms=3454 db_terminal_from_entry_ms=3453 db_observed_from_entry_ms=3454 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=3489 enumeration_task_return_from_entry_ms=3511 enumeration_terminal_from_entry_ms=4208 enumeration_observed_from_entry_ms=4218 enumeration_task_returned_terminal=0 enumeration_unattributed_ms=59 db_service_setup_ms=23 db_corruption_probe_ms=683 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=202 db_encoding_pragmas_ms=5 db_version_work_ms=3 db_schema_ensures_ms=190 db_init_unattributed_ms=11 summary_request_from_entry_ms=4254 title_construct_begin_from_entry_ms=4254 title_construct_end_from_entry_ms=4255 transition_start_from_entry_ms=4255 transition_complete_from_entry_ms=5251 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=5251 startup_deactivate_end_from_entry_ms=5252 title_activate_begin_from_entry_ms=5253 title_activate_end_from_entry_ms=5394 title_first_update_begin_from_entry_ms=5394 title_first_update_end_from_entry_ms=5396 title_stage_draw_begin_from_entry_ms=5397 title_stage_draw_end_from_entry_ms=5398 title_backbuffer_blit_begin_from_entry_ms=5398 title_backbuffer_blit_end_from_entry_ms=5398 summary_to_title_unattributed_ms=2 title_gpu_setup_ms=0 title_background_ms=26 title_menu_ms=9 title_font_ms=0 title_cursor_sound_ms=103 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=3 title_backbuffer_published=1
```

#### 09-B attempt-1

Artifact: `slots/09-B/attempt-1/result.txt`
SHA-256: `3b6bfd1f3214199b3587a997dbde9beda35b80a94b68023234ca02e646e7080a`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=3067 db_init_ms=1087 discovery_parse_ms=6 persistence_ms=143 cleanup_ms=2 hierarchy_ms=1 discovered=0 parsed=0 groups=0 added=0 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=427 config_to_load_content_ms=23 load_content_to_startup_ms=132 startup_to_first_draw_ms=2790 startup_to_summary_ms=3003 summary_to_title_ms=543 entry_to_title_ms=4130 entry_unix_us=1785357758595599 title_unix_us=1785357762725799
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357758595599 title_backbuffer_unix_us=1785357762727648 entry_to_title_backbuffer_ms=4132 load_content_complete_from_entry_ms=451 startup_construct_begin_from_entry_ms=456 startup_construct_end_from_entry_ms=469 startup_activate_begin_from_entry_ms=472 startup_activation_from_entry_ms=583 startup_activate_end_from_entry_ms=583 load_content_return_from_entry_ms=584 base_initialize_return_from_entry_ms=584 input_manager_begin_from_entry_ms=584 input_manager_end_from_entry_ms=2196 saved_bindings_begin_from_entry_ms=2196 saved_bindings_end_from_entry_ms=2213 graphics_initialize_begin_from_entry_ms=2213 graphics_initialize_end_from_entry_ms=2214 render_target_begin_from_entry_ms=2221 render_target_end_from_entry_ms=2223 initialize_complete_from_entry_ms=2223 post_load_unattributed_ms=16 startup_first_update_begin_from_entry_ms=2236 startup_first_update_end_from_entry_ms=2239 startup_first_draw_begin_from_entry_ms=3353 startup_first_draw_end_from_entry_ms=3374 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=1 db_invoke_from_entry_ms=2263 db_task_return_from_entry_ms=3350 db_terminal_from_entry_ms=3350 db_observed_from_entry_ms=3350 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=3386 enumeration_task_return_from_entry_ms=3583 enumeration_terminal_from_entry_ms=3583 enumeration_observed_from_entry_ms=3584 enumeration_task_returned_terminal=1 enumeration_unattributed_ms=48 db_service_setup_ms=21 db_corruption_probe_ms=670 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=190 db_encoding_pragmas_ms=5 db_version_work_ms=3 db_schema_ensures_ms=187 db_init_unattributed_ms=11 summary_request_from_entry_ms=3587 title_construct_begin_from_entry_ms=3587 title_construct_end_from_entry_ms=3587 transition_start_from_entry_ms=3587 transition_complete_from_entry_ms=3981 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=3981 startup_deactivate_end_from_entry_ms=3982 title_activate_begin_from_entry_ms=3982 title_activate_end_from_entry_ms=4128 title_first_update_begin_from_entry_ms=4128 title_first_update_end_from_entry_ms=4130 title_stage_draw_begin_from_entry_ms=4130 title_stage_draw_end_from_entry_ms=4132 title_backbuffer_blit_begin_from_entry_ms=4132 title_backbuffer_blit_end_from_entry_ms=4132 summary_to_title_unattributed_ms=0 title_gpu_setup_ms=0 title_background_ms=26 title_menu_ms=8 title_font_ms=0 title_cursor_sound_ms=109 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=3 title_backbuffer_published=1
```

#### 10-A attempt-1

Artifact: `slots/10-A/attempt-1/result.txt`
SHA-256: `cf8eb13ba90fc8913323acf28c68cafc1c1cfeafea301a23df40ec9e7b0f85d9`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=1913 db_init_ms=1049 discovery_parse_ms=137 persistence_ms=425 cleanup_ms=1 hierarchy_ms=5 discovered=100 parsed=100 groups=27 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=416 config_to_load_content_ms=24 load_content_to_startup_ms=135 startup_to_first_draw_ms=1173 startup_to_summary_ms=1848 summary_to_title_ms=1165 entry_to_title_ms=3589 entry_unix_us=1785357767576494 title_unix_us=1785357771165910
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357767576494 title_backbuffer_unix_us=1785357771167977 entry_to_title_backbuffer_ms=3591 load_content_complete_from_entry_ms=441 startup_construct_begin_from_entry_ms=445 startup_construct_end_from_entry_ms=458 startup_activate_begin_from_entry_ms=462 startup_activation_from_entry_ms=575 startup_activate_end_from_entry_ms=575 load_content_return_from_entry_ms=576 base_initialize_return_from_entry_ms=576 input_manager_begin_from_entry_ms=576 input_manager_end_from_entry_ms=616 saved_bindings_begin_from_entry_ms=616 saved_bindings_end_from_entry_ms=630 graphics_initialize_begin_from_entry_ms=630 graphics_initialize_end_from_entry_ms=630 render_target_begin_from_entry_ms=636 render_target_end_from_entry_ms=638 initialize_complete_from_entry_ms=638 post_load_unattributed_ms=15 startup_first_update_begin_from_entry_ms=650 startup_first_update_end_from_entry_ms=652 startup_first_draw_begin_from_entry_ms=1729 startup_first_draw_end_from_entry_ms=1749 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=41 db_invoke_from_entry_ms=677 db_task_return_from_entry_ms=1726 db_terminal_from_entry_ms=1726 db_observed_from_entry_ms=1726 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=1759 enumeration_task_return_from_entry_ms=1779 enumeration_terminal_from_entry_ms=2376 enumeration_observed_from_entry_ms=2388 enumeration_task_returned_terminal=0 enumeration_unattributed_ms=51 db_service_setup_ms=21 db_corruption_probe_ms=641 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=186 db_encoding_pragmas_ms=5 db_version_work_ms=3 db_schema_ensures_ms=181 db_init_unattributed_ms=12 summary_request_from_entry_ms=2424 title_construct_begin_from_entry_ms=2424 title_construct_end_from_entry_ms=2424 transition_start_from_entry_ms=2424 transition_complete_from_entry_ms=3421 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=3421 startup_deactivate_end_from_entry_ms=3423 title_activate_begin_from_entry_ms=3423 title_activate_end_from_entry_ms=3587 title_first_update_begin_from_entry_ms=3587 title_first_update_end_from_entry_ms=3589 title_stage_draw_begin_from_entry_ms=3590 title_stage_draw_end_from_entry_ms=3591 title_backbuffer_blit_begin_from_entry_ms=3591 title_backbuffer_blit_end_from_entry_ms=3591 summary_to_title_unattributed_ms=1 title_gpu_setup_ms=0 title_background_ms=26 title_menu_ms=8 title_font_ms=0 title_cursor_sound_ms=126 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=4 title_backbuffer_published=1
```

#### 11-C attempt-1

Artifact: `slots/11-C/attempt-1/result.txt`
SHA-256: `a6ea4a2bcb865665050c1934b19cfbd9faf61884f084e40638e276faf3e3830a`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=2505 db_init_ms=1149 discovery_parse_ms=149 persistence_ms=365 cleanup_ms=1 hierarchy_ms=3 discovered=100 parsed=100 groups=27 added=0 updated=0 preserved=100 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=435 config_to_load_content_ms=25 load_content_to_startup_ms=136 startup_to_first_draw_ms=1846 startup_to_summary_ms=2438 summary_to_title_ms=1158 entry_to_title_ms=4193 entry_unix_us=1785357777112962 title_unix_us=1785357781306688
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357777112962 title_backbuffer_unix_us=1785357781308612 entry_to_title_backbuffer_ms=4195 load_content_complete_from_entry_ms=460 startup_construct_begin_from_entry_ms=465 startup_construct_end_from_entry_ms=478 startup_activate_begin_from_entry_ms=481 startup_activation_from_entry_ms=596 startup_activate_end_from_entry_ms=596 load_content_return_from_entry_ms=597 base_initialize_return_from_entry_ms=597 input_manager_begin_from_entry_ms=597 input_manager_end_from_entry_ms=1208 saved_bindings_begin_from_entry_ms=1208 saved_bindings_end_from_entry_ms=1223 graphics_initialize_begin_from_entry_ms=1223 graphics_initialize_end_from_entry_ms=1223 render_target_begin_from_entry_ms=1229 render_target_end_from_entry_ms=1230 initialize_complete_from_entry_ms=1230 post_load_unattributed_ms=15 startup_first_update_begin_from_entry_ms=1242 startup_first_update_end_from_entry_ms=1244 startup_first_draw_begin_from_entry_ms=2423 startup_first_draw_end_from_entry_ms=2443 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=36 db_invoke_from_entry_ms=1271 db_task_return_from_entry_ms=2420 db_terminal_from_entry_ms=2420 db_observed_from_entry_ms=2420 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=2454 enumeration_task_return_from_entry_ms=2474 enumeration_terminal_from_entry_ms=2997 enumeration_observed_from_entry_ms=2999 enumeration_task_returned_terminal=0 enumeration_unattributed_ms=27 db_service_setup_ms=22 db_corruption_probe_ms=1050 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=7 db_encoding_pragmas_ms=8 db_version_work_ms=2 db_schema_ensures_ms=48 db_init_unattributed_ms=12 summary_request_from_entry_ms=3035 title_construct_begin_from_entry_ms=3035 title_construct_end_from_entry_ms=3035 transition_start_from_entry_ms=3035 transition_complete_from_entry_ms=4032 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=4032 startup_deactivate_end_from_entry_ms=4033 title_activate_begin_from_entry_ms=4034 title_activate_end_from_entry_ms=4191 title_first_update_begin_from_entry_ms=4192 title_first_update_end_from_entry_ms=4193 title_stage_draw_begin_from_entry_ms=4194 title_stage_draw_end_from_entry_ms=4195 title_backbuffer_blit_begin_from_entry_ms=4195 title_backbuffer_blit_end_from_entry_ms=4195 summary_to_title_unattributed_ms=3 title_gpu_setup_ms=0 title_background_ms=25 title_menu_ms=8 title_font_ms=0 title_cursor_sound_ms=121 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=3 title_backbuffer_published=1
```

#### 12-B attempt-1

Artifact: `slots/12-B/attempt-1/result.txt`
SHA-256: `3865cf69ed299bb8d656db0bdc9de0617e01f27be47b879861a88fda83698768`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=3153 db_init_ms=1135 discovery_parse_ms=6 persistence_ms=151 cleanup_ms=1 hierarchy_ms=1 discovered=0 parsed=0 groups=0 added=0 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=459 config_to_load_content_ms=25 load_content_to_startup_ms=145 startup_to_first_draw_ms=2858 startup_to_summary_ms=3082 summary_to_title_ms=562 entry_to_title_ms=4275 entry_unix_us=1785357786386868 title_unix_us=1785357790662357
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357786386868 title_backbuffer_unix_us=1785357790664567 entry_to_title_backbuffer_ms=4277 load_content_complete_from_entry_ms=485 startup_construct_begin_from_entry_ms=489 startup_construct_end_from_entry_ms=503 startup_activate_begin_from_entry_ms=507 startup_activation_from_entry_ms=629 startup_activate_end_from_entry_ms=629 load_content_return_from_entry_ms=630 base_initialize_return_from_entry_ms=630 input_manager_begin_from_entry_ms=631 input_manager_end_from_entry_ms=2263 saved_bindings_begin_from_entry_ms=2263 saved_bindings_end_from_entry_ms=2278 graphics_initialize_begin_from_entry_ms=2278 graphics_initialize_end_from_entry_ms=2278 render_target_begin_from_entry_ms=2284 render_target_end_from_entry_ms=2286 initialize_complete_from_entry_ms=2286 post_load_unattributed_ms=16 startup_first_update_begin_from_entry_ms=2298 startup_first_update_end_from_entry_ms=2301 startup_first_draw_begin_from_entry_ms=3467 startup_first_draw_end_from_entry_ms=3488 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=1 db_invoke_from_entry_ms=2329 db_task_return_from_entry_ms=3464 db_terminal_from_entry_ms=3464 db_observed_from_entry_ms=3464 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=3500 enumeration_task_return_from_entry_ms=3709 enumeration_terminal_from_entry_ms=3709 enumeration_observed_from_entry_ms=3709 enumeration_task_returned_terminal=1 enumeration_unattributed_ms=52 db_service_setup_ms=24 db_corruption_probe_ms=696 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=203 db_encoding_pragmas_ms=5 db_version_work_ms=3 db_schema_ensures_ms=191 db_init_unattributed_ms=13 summary_request_from_entry_ms=3712 title_construct_begin_from_entry_ms=3712 title_construct_end_from_entry_ms=3712 transition_start_from_entry_ms=3713 transition_complete_from_entry_ms=4095 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=4095 startup_deactivate_end_from_entry_ms=4096 title_activate_begin_from_entry_ms=4096 title_activate_end_from_entry_ms=4273 title_first_update_begin_from_entry_ms=4273 title_first_update_end_from_entry_ms=4275 title_stage_draw_begin_from_entry_ms=4276 title_stage_draw_end_from_entry_ms=4277 title_backbuffer_blit_begin_from_entry_ms=4277 title_backbuffer_blit_end_from_entry_ms=4277 summary_to_title_unattributed_ms=2 title_gpu_setup_ms=0 title_background_ms=26 title_menu_ms=8 title_font_ms=0 title_cursor_sound_ms=138 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=5 title_backbuffer_published=1
```

#### 13-C attempt-1

Artifact: `slots/13-C/attempt-1/result.txt`
SHA-256: `0b0a4739d778b65c72fece16c176d4b7006dde12fa1b46f88eeb91b522c82401`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=1940 db_init_ms=1145 discovery_parse_ms=151 persistence_ms=375 cleanup_ms=1 hierarchy_ms=4 discovered=100 parsed=100 groups=27 added=0 updated=0 preserved=100 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=409 config_to_load_content_ms=23 load_content_to_startup_ms=133 startup_to_first_draw_ms=1267 startup_to_summary_ms=1876 summary_to_title_ms=1151 entry_to_title_ms=3594 entry_unix_us=1785357795665691 title_unix_us=1785357799260247
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357795665691 title_backbuffer_unix_us=1785357799262177 entry_to_title_backbuffer_ms=3596 load_content_complete_from_entry_ms=433 startup_construct_begin_from_entry_ms=438 startup_construct_end_from_entry_ms=451 startup_activate_begin_from_entry_ms=454 startup_activation_from_entry_ms=566 startup_activate_end_from_entry_ms=566 load_content_return_from_entry_ms=566 base_initialize_return_from_entry_ms=566 input_manager_begin_from_entry_ms=567 input_manager_end_from_entry_ms=599 saved_bindings_begin_from_entry_ms=599 saved_bindings_end_from_entry_ms=614 graphics_initialize_begin_from_entry_ms=614 graphics_initialize_end_from_entry_ms=614 render_target_begin_from_entry_ms=619 render_target_end_from_entry_ms=620 initialize_complete_from_entry_ms=620 post_load_unattributed_ms=14 startup_first_update_begin_from_entry_ms=632 startup_first_update_end_from_entry_ms=635 startup_first_draw_begin_from_entry_ms=1813 startup_first_draw_end_from_entry_ms=1833 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=37 db_invoke_from_entry_ms=664 db_task_return_from_entry_ms=1810 db_terminal_from_entry_ms=1810 db_observed_from_entry_ms=1810 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=1844 enumeration_task_return_from_entry_ms=1864 enumeration_terminal_from_entry_ms=2400 enumeration_observed_from_entry_ms=2406 enumeration_task_returned_terminal=0 enumeration_unattributed_ms=26 db_service_setup_ms=21 db_corruption_probe_ms=1047 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=8 db_encoding_pragmas_ms=8 db_version_work_ms=2 db_schema_ensures_ms=47 db_init_unattributed_ms=13 summary_request_from_entry_ms=2442 title_construct_begin_from_entry_ms=2442 title_construct_end_from_entry_ms=2442 transition_start_from_entry_ms=2443 transition_complete_from_entry_ms=3440 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=3440 startup_deactivate_end_from_entry_ms=3441 title_activate_begin_from_entry_ms=3441 title_activate_end_from_entry_ms=3592 title_first_update_begin_from_entry_ms=3592 title_first_update_end_from_entry_ms=3594 title_stage_draw_begin_from_entry_ms=3595 title_stage_draw_end_from_entry_ms=3596 title_backbuffer_blit_begin_from_entry_ms=3596 title_backbuffer_blit_end_from_entry_ms=3596 summary_to_title_unattributed_ms=2 title_gpu_setup_ms=0 title_background_ms=26 title_menu_ms=8 title_font_ms=0 title_cursor_sound_ms=114 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=3 title_backbuffer_published=1
```

#### 14-B attempt-2

Artifact: `slots/14-B/attempt-2/result.txt`
SHA-256: `0ccaa59187ffd311772a50307aa42d20ec2420777c835166c9ecf5a4faf9d0d2`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=4040 db_init_ms=1032 discovery_parse_ms=6 persistence_ms=142 cleanup_ms=1 hierarchy_ms=1 discovered=0 parsed=0 groups=0 added=0 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=456 config_to_load_content_ms=25 load_content_to_startup_ms=141 startup_to_first_draw_ms=3760 startup_to_summary_ms=3971 summary_to_title_ms=553 entry_to_title_ms=5148 entry_unix_us=1785357812525575 title_unix_us=1785357817674000
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357812525575 title_backbuffer_unix_us=1785357817675750 entry_to_title_backbuffer_ms=5150 load_content_complete_from_entry_ms=482 startup_construct_begin_from_entry_ms=487 startup_construct_end_from_entry_ms=501 startup_activate_begin_from_entry_ms=505 startup_activation_from_entry_ms=623 startup_activate_end_from_entry_ms=623 load_content_return_from_entry_ms=624 base_initialize_return_from_entry_ms=624 input_manager_begin_from_entry_ms=625 input_manager_end_from_entry_ms=3267 saved_bindings_begin_from_entry_ms=3267 saved_bindings_end_from_entry_ms=3281 graphics_initialize_begin_from_entry_ms=3281 graphics_initialize_end_from_entry_ms=3281 render_target_begin_from_entry_ms=3287 render_target_end_from_entry_ms=3288 initialize_complete_from_entry_ms=3288 post_load_unattributed_ms=17 startup_first_update_begin_from_entry_ms=3300 startup_first_update_end_from_entry_ms=3302 startup_first_draw_begin_from_entry_ms=4365 startup_first_draw_end_from_entry_ms=4384 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=1 db_invoke_from_entry_ms=3330 db_task_return_from_entry_ms=4362 db_terminal_from_entry_ms=4362 db_observed_from_entry_ms=4362 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=4396 enumeration_task_return_from_entry_ms=4592 enumeration_terminal_from_entry_ms=4591 enumeration_observed_from_entry_ms=4592 enumeration_task_returned_terminal=1 enumeration_unattributed_ms=48 db_service_setup_ms=21 db_corruption_probe_ms=628 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=184 db_encoding_pragmas_ms=5 db_version_work_ms=2 db_schema_ensures_ms=181 db_init_unattributed_ms=11 summary_request_from_entry_ms=4595 title_construct_begin_from_entry_ms=4595 title_construct_end_from_entry_ms=4595 transition_start_from_entry_ms=4595 transition_complete_from_entry_ms=4991 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=4991 startup_deactivate_end_from_entry_ms=4992 title_activate_begin_from_entry_ms=4993 title_activate_end_from_entry_ms=5146 title_first_update_begin_from_entry_ms=5146 title_first_update_end_from_entry_ms=5148 title_stage_draw_begin_from_entry_ms=5149 title_stage_draw_end_from_entry_ms=5150 title_backbuffer_blit_begin_from_entry_ms=5150 title_backbuffer_blit_end_from_entry_ms=5150 summary_to_title_unattributed_ms=2 title_gpu_setup_ms=0 title_background_ms=25 title_menu_ms=8 title_font_ms=0 title_cursor_sound_ms=117 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=3 title_backbuffer_published=1
```

#### 15-A attempt-1

Artifact: `slots/15-A/attempt-1/result.txt`
SHA-256: `77822ee3d3edfb8a27c789969d87a21c6ae1da5e0e2580f4a9a8bd0aebaa30d2`

```text
HPA192_STARTUP path=enumeration outcome=success total_ms=1879 db_init_ms=1038 discovery_parse_ms=137 persistence_ms=419 cleanup_ms=1 hierarchy_ms=5 discovered=100 parsed=100 groups=27 added=100 updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none
HPA192_TIMING entry_to_config_ms=417 config_to_load_content_ms=22 load_content_to_startup_ms=131 startup_to_first_draw_ms=1157 startup_to_summary_ms=1816 summary_to_title_ms=1147 entry_to_title_ms=3535 entry_unix_us=1785357822503205 title_unix_us=1785357826038987
HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1785357822503205 title_backbuffer_unix_us=1785357826040834 entry_to_title_backbuffer_ms=3537 load_content_complete_from_entry_ms=440 startup_construct_begin_from_entry_ms=445 startup_construct_end_from_entry_ms=457 startup_activate_begin_from_entry_ms=461 startup_activation_from_entry_ms=572 startup_activate_end_from_entry_ms=572 load_content_return_from_entry_ms=572 base_initialize_return_from_entry_ms=572 input_manager_begin_from_entry_ms=573 input_manager_end_from_entry_ms=609 saved_bindings_begin_from_entry_ms=609 saved_bindings_end_from_entry_ms=623 graphics_initialize_begin_from_entry_ms=623 graphics_initialize_end_from_entry_ms=623 render_target_begin_from_entry_ms=629 render_target_end_from_entry_ms=630 initialize_complete_from_entry_ms=630 post_load_unattributed_ms=16 startup_first_update_begin_from_entry_ms=642 startup_first_update_end_from_entry_ms=644 startup_first_draw_begin_from_entry_ms=1709 startup_first_draw_end_from_entry_ms=1729 startup_updates_before_first_draw=3 startup_game_time_before_first_draw_ms=33 startup_draws_before_transition=40 db_invoke_from_entry_ms=669 db_task_return_from_entry_ms=1707 db_terminal_from_entry_ms=1706 db_observed_from_entry_ms=1707 db_task_returned_terminal=1 enumeration_invoke_from_entry_ms=1740 enumeration_task_return_from_entry_ms=1759 enumeration_terminal_from_entry_ms=2351 enumeration_observed_from_entry_ms=2352 enumeration_task_returned_terminal=0 enumeration_unattributed_ms=50 db_service_setup_ms=21 db_corruption_probe_ms=634 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=185 db_encoding_pragmas_ms=4 db_version_work_ms=2 db_schema_ensures_ms=179 db_init_unattributed_ms=12 summary_request_from_entry_ms=2388 title_construct_begin_from_entry_ms=2388 title_construct_end_from_entry_ms=2388 transition_start_from_entry_ms=2389 transition_complete_from_entry_ms=3385 transition_update_count=60 transition_game_time_ms=1000 startup_deactivate_begin_from_entry_ms=3385 startup_deactivate_end_from_entry_ms=3386 title_activate_begin_from_entry_ms=3387 title_activate_end_from_entry_ms=3534 title_first_update_begin_from_entry_ms=3534 title_first_update_end_from_entry_ms=3535 title_stage_draw_begin_from_entry_ms=3536 title_stage_draw_end_from_entry_ms=3537 title_backbuffer_blit_begin_from_entry_ms=3537 title_backbuffer_blit_end_from_entry_ms=3537 summary_to_title_unattributed_ms=3 title_gpu_setup_ms=0 title_background_ms=25 title_menu_ms=8 title_font_ms=0 title_cursor_sound_ms=110 title_decide_sound_ms=0 title_game_start_sound_ms=0 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=4 title_backbuffer_published=1
```

</details>

### Scenario medians and ranges

Every cell below is `minimum / median / maximum`. Values are milliseconds
unless the metric is explicitly a count. Each scenario has five accepted
samples. Same-source exclusive partitions reconcile exactly per raw sample.
Enumeration combines rounded `HPA192_STARTUP` child durations with the
`HPA192_CRITICAL_PATH` residual and reconciles within the design-approved
0–4 ms cross-line bound; the observed differences span 0–4 ms. Medians of
independently sorted child intervals are descriptive and are not expected to
add to the independently sorted enclosing median.

The accepted endpoint and five top-level exclusive intervals were:

| Metric | Scenario A | Scenario B | Scenario C |
| --- | ---: | ---: | ---: |
| external launch to Title backbuffer | 3580 / 4589 / 5449 | 4175 / 4275 / 5207 | 3639 / 5430 / 6430 |
| stdout observation lag (excluded from performance arithmetic) | 59 / 81 / 114 | 49 / 52 / 90 | 61 / 86 / 97 |
| external launch to process entry | 43 / 51 / 117 | 43 / 51 / 68 | 43 / 52 / 54 |
| process entry to `load_content_complete` (**external-bridge head**) | 440 / 505 / 528 | 448 / 476 / 485 | 433 / 470 / 484 |
| `load_content_complete` to initialize complete | 190 / 809 / 1785 | 1772 / 1788 / 2806 | 187 / 1787 / 2785 |
| initialize complete to summary request | 1758 / 1962 / 2052 | 1307 / 1406 / 1432 | 1805 / 1932 / 1971 |
| summary request to Title backbuffer | 1144 / 1149 / 1167 | 544 / 554 / 565 | 1137 / 1154 / 1160 |

The external-bridge head includes pre-`LoadContent` graphics-settings work.
It is not SQLite time or song-loading time.

The post-`LoadContent` initialization refinement was:

| Exclusive metric | Scenario A | Scenario B | Scenario C |
| --- | ---: | ---: | ---: |
| Startup construction | 12 / 14 / 22 | 13 / 14 / 15 | 13 / 13 / 14 |
| Startup activation | 111 / 119 / 166 | 111 / 118 / 122 | 112 / 120 / 125 |
| base-initialize return tail | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| input-manager creation | 36 / 617 / 1620 | 1612 / 1625 / 2642 | 32 / 1618 / 2618 |
| saved system bindings | 14 / 15 / 15 | 14 / 15 / 17 | 15 / 15 / 15 |
| graphics-manager initialization | 0 / 0 / 0 | 0 / 0 / 1 | 0 / 0 / 0 |
| main render-target acquisition | 1 / 1 / 2 | 1 / 1 / 2 | 1 / 1 / 2 |
| post-load unattributed | 15 / 16 / 18 | 16 / 16 / 17 | 14 / 15 / 17 |

Startup frame observations were scheduling annotations and were not used in
the savings budget:

| Annotation | Scenario A | Scenario B | Scenario C |
| --- | ---: | ---: | ---: |
| first Startup update duration | 2 / 2 / 2 | 2 / 2 / 3 | 2 / 2 / 3 |
| first Startup draw duration | 20 / 21 / 22 | 19 / 21 / 22 | 20 / 20 / 21 |
| updates before first draw (count) | 3 / 3 / 3 | 3 / 3 / 3 | 3 / 3 / 3 |
| game time before first draw | 33 / 33 / 33 | 33 / 33 / 33 | 33 / 33 / 33 |
| completed Startup draws before transition (count) | 40 / 44 / 51 | 1 / 1 / 1 | 36 / 41 / 42 |

The exclusive dispatch, operation, and observation refinement of
initialize-complete to summary-request was:

| Exclusive metric | Scenario A | Scenario B | Scenario C |
| --- | ---: | ---: | ---: |
| initialize complete to database invoke | 39 / 41 / 45 | 40 / 43 / 44 | 40 / 42 / 44 |
| database operation | 1037 / 1117 / 1203 | 1032 / 1113 / 1138 | 1146 / 1190 / 1211 |
| database terminal to observation | 0 / 0 / 1 | 0 / 0 / 1 | 0 / 0 / 1 |
| database observation to enumeration invoke | 33 / 35 / 40 | 34 / 36 / 39 | 34 / 37 / 37 |
| enumeration operation | 611 / 676 / 789 | 195 / 207 / 210 | 543 / 622 / 635 |
| enumeration terminal to observation | 1 / 5 / 12 | 0 / 1 / 1 | 2 / 6 / 14 |
| enumeration observation to summary request | 36 / 36 / 37 | 3 / 3 / 3 | 36 / 36 / 37 |

Task-return splits overlap their enclosing operations and are diagnostic
annotations, not additional exclusive savings:

| Annotation | Scenario A | Scenario B | Scenario C |
| --- | ---: | ---: | ---: |
| database invoke to task return | 1038 / 1118 / 1203 | 1032 / 1114 / 1138 | 1146 / 1191 / 1211 |
| database async after task return | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| database terminal before task return | 0 / 0 / 1 | 0 / 0 / 1 | 0 / 0 / 1 |
| enumeration invoke to task return | 19 / 20 / 22 | 196 / 208 / 210 | 19 / 20 / 22 |
| enumeration async after task return | 592 / 654 / 769 | 0 / 0 / 0 | 523 / 600 / 616 |
| enumeration terminal before task return | 0 / 0 / 0 | 0 / 0 / 1 | 0 / 0 / 0 |

The exclusive database partition was:

| Exclusive metric | Scenario A | Scenario B | Scenario C |
| --- | ---: | ---: | ---: |
| service setup | 21 / 23 / 48 | 21 / 23 / 24 | 21 / 22 / 24 |
| corruption probe | 634 / 683 / 735 | 628 / 681 / 696 | 1047 / 1086 / 1106 |
| invalid recovery | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| ensure created | 185 / 202 / 206 | 184 / 197 / 209 | 7 / 8 / 8 |
| encoding pragmas | 4 / 5 / 5 | 5 / 5 / 5 | 8 / 8 / 8 |
| version work | 2 / 3 / 3 | 2 / 3 / 3 | 2 / 2 / 2 |
| schema ensures | 179 / 190 / 198 | 181 / 191 / 193 | 47 / 50 / 52 |
| database unattributed | 11 / 12 / 12 | 11 / 12 / 13 | 11 / 13 / 13 |

Every accepted attempt had zero invalid recoveries and exactly one
`EnsureCreatedAsync` call.

The exclusive enumeration/import partition was:

| Exclusive metric | Scenario A | Scenario B | Scenario C |
| --- | ---: | ---: | ---: |
| discovery and parsing | 137 / 153 / 175 | 6 / 6 / 6 | 149 / 159 / 167 |
| persistence | 419 / 455 / 555 | 142 / 150 / 151 | 365 / 422 / 441 |
| cleanup | 1 / 1 / 1 | 1 / 1 / 2 | 1 / 1 / 1 |
| hierarchy publication | 5 / 6 / 6 | 1 / 1 / 1 | 3 / 4 / 4 |
| enumeration unattributed | 50 / 54 / 59 | 48 / 51 / 52 | 26 / 27 / 31 |

The exclusive summary-request to Title-backbuffer partition was:

| Exclusive metric | Scenario A | Scenario B | Scenario C |
| --- | ---: | ---: | ---: |
| Title construction | 0 / 0 / 1 | 0 / 0 / 0 | 0 / 0 / 0 |
| transition wall interval | 996 / 996 / 997 | 382 / 382 / 396 | 997 / 997 / 997 |
| Startup deactivation | 1 / 1 / 2 | 1 / 1 / 2 | 1 / 1 / 1 |
| Title activation | 141 / 147 / 164 | 146 / 155 / 177 | 135 / 151 / 157 |
| first Title update | 1 / 2 / 2 | 2 / 2 / 2 | 1 / 2 / 2 |
| first Title stage draw | 1 / 1 / 2 | 1 / 1 / 2 | 1 / 1 / 2 |
| Title backbuffer blit | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| summary-to-Title unattributed | 0 / 1 / 3 | 0 / 2 / 2 | 0 / 2 / 3 |

Every accepted transition recorded 60 updates and 1,000 ms of accumulated
game time. The lower Scenario B wall interval therefore did not remove the
visible transition contract.

The exclusive Title-activation partition was:

| Exclusive metric | Scenario A | Scenario B | Scenario C |
| --- | ---: | ---: | ---: |
| GPU setup | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| stage background | 25 / 26 / 26 | 25 / 26 / 27 | 25 / 26 / 27 |
| menu texture | 8 / 8 / 9 | 8 / 8 / 9 | 8 / 8 / 9 |
| font | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| cursor sound | 103 / 110 / 126 | 109 / 117 / 138 | 97 / 114 / 121 |
| decide sound | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| game-start sound | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| game-start fallback | 0 / 0 / 0 | 0 / 0 / 0 | 0 / 0 / 0 |
| Title-activation unattributed | 3 / 4 / 5 | 3 / 4 / 5 | 3 / 5 / 5 |

Each accepted Title activation attempted exactly three sound loads and did not
run the explicit game-start fallback.

### Ranked non-overlapping Scenario A savings budget

The budget starts from Scenario A's accepted external median of **4,589 ms**.
It uses a second-fastest accepted observation as the preservation floor where
possible, rather than assuming that an operation can disappear. The rows are
ranked by conservative removable amount, not execution order.

| Rank | Measured exclusive interval | Scenario A median | Conservative removable | Preserved remainder | Cumulative projected median | Contract or risk note |
| ---: | --- | ---: | ---: | ---: | ---: | --- |
| 1 | `load_content_complete` to initialize complete | 809 | 612 | 197 | 3977 | The 197 ms floor is the second-fastest accepted Scenario A observation; it retains the complete initialization path and a valid Startup draw. This parent is counted once, so Startup construction/activation, input-manager, binding, graphics, render-target, and residual children are not counted again. |
| 2 | transition start to transition complete | 996 | 600 | 396 | 3377 | The 396 ms remainder is the slowest accepted Scenario B transition wall interval. All five B samples still recorded 60 updates, 1,000 ms accumulated transition game time, and one completed Startup draw. This is cross-scenario evidence and therefore the most optimistic item retained in the conservative budget; no summary-to-backbuffer parent saving is also counted. |
| 3 | initialize complete to summary request | 1962 | 176 | 1786 | 3201 | The 1,786 ms floor is the second-fastest accepted Scenario A observation. This parent is counted once; database, enumeration, dispatch, observation, first-update, and first-draw markers are not subtracted separately. All first-wave atomicity, migration, rollback, cancellation, and publication contracts remain required. |
| 4 | process entry to `load_content_complete` (external-bridge head) | 505 | 64 | 441 | 3137 | The 441 ms floor is the second-fastest accepted Scenario A observation. This includes pre-`LoadContent` graphics-settings and broader runtime work and is not relabeled as database or song work. |
| 5 | external launch to process entry | 51 | 8 | 43 | 3129 | Two accepted Scenario A launches observed 43 ms. This is process/runtime or packaging territory, outside the measured in-process product spans. |
| 6 | Title cursor-sound load | 110 | 6 | 104 | 3123 | The 104 ms floor is the second-fastest accepted Scenario A observation. Existing load/fallback and resource semantics must remain unchanged; Title activation itself is not also counted. |
| 7 | Title-activation unattributed residual | 4 | 1 | 3 | 3122 | The 3 ms floor was observed twice in accepted Scenario A samples. It is exclusive of the cursor-sound row and other Title resources. |

The checked arithmetic is:

```text
conservative_savings_ms = 612 + 600 + 176 + 64 + 8 + 6 + 1
                        = 1467
projected_median_ms = 4589 - 1467
                    = 3122
```

The launch-to-entry, entry-to-load, load-to-initialize, and
initialize-to-summary rows are mutually exclusive top-level siblings. The
transition row refines the otherwise untouched summary-to-backbuffer section.
The final two Title rows are exclusive siblings inside the otherwise untouched
Title-activation interval. No database, enumeration, transition, frame, or
Title-resource child is subtracted after its enclosing interval has been
counted. Task-return and first-update/draw timings remain annotations because
they overlap operations.

Even this optimistic observed-floor budget remains **1,122 ms above** the
2,000 ms product-design threshold. Treating the full 1,117 ms database
operation, 676 ms enumeration operation, or their scheduling annotations as
removable would assume unmeasured overlap and would not preserve a
demonstrated fresh-100 execution floor. Such conditional arithmetic is
excluded from the decision.

### Verification and provenance

The diagnostic implementation and evidence gates recorded:

- the exact focused production filter passed 408 tests with zero failures or
  skips, including trace, host, stage, async lifecycle, SongManager, bulk
  enumeration, and database-service coverage;
- the five timing-disabled compatibility tests passed, preserving the existing
  `HPA192_STARTUP` and `HPA192_TIMING` output and the original
  `TitleCompleted` meaning, while disabled launches emit no companion prefix
  and do not auto-exit;
- the macOS-safe Release suite with `ALSOFT_DRIVERS=null` first reported
  7,137 passes and one intermittent SQLite `ObjectDisposedException`; that
  exact test passed 1/1 in isolation and the authoritative full rerun passed
  7,138 tests with zero failures or skips;
- the Mac Release build passed with zero errors, and the Windows Release build
  passed with 111 warnings and zero errors;
- the full shell suite ended with
  `critical-path shell tests passed`; Bash syntax, ShellCheck, timing
  preflight, identity, process, layout, and deadline suites also passed;
- the committed development evidence recorded 114 warnings before the
  diagnostic branch and 111 after it; a detached pre-diagnostic build had 112
  raw warnings versus the comparable current build's 111, and every warning
  in a touched production file matched a pre-existing code/message. One
  sandboxed no-incremental Mac build's additional `NU1900` was solely the
  unavailable NuGet vulnerability cache;
- whole-branch specification and code-quality review verified the 81-field
  schema, compatibility, bounded state, lock and terminal semantics, observer
  containment, SQLite and Title exclusivity, post-blit publication, and normal
  next-update exit. Review fixes bound the frozen source/DLL inputs, included
  retained rejected attempts in summary generation, made controls byte-exact,
  and removed an unnecessary public async state machine;
- runner review repairs added canonical layout boundaries, a true inclusive
  60-second monotonic deadline, rejection of terminal output first observed at
  or after that deadline, and coherent two-second PID identity stabilization.
  The final repair commit is the frozen source commit
  `d3944225bdb5b5bb8e6764b83b98dcb45ca3a810`;
- the first repaired seed attempt's sandboxed OpenGL initialization exit 134
  was preserved, not selected. With explicit GUI/WindowServer and
  LaunchServices access, the one authorized unsandboxed seed then passed and
  the fixed matrix completed. This execution detail is necessary provenance;
  it does not turn the macOS diagnostic into acceptance evidence;
- four Finder `.DS_Store` files were moved recoverably to the ignored
  quarantine before measurement. The remaining live 592-file corpus manifest
  byte-compared equal to the committed manifest, and the prior build/failure
  namespaces and quarantine remained preserved;
- all 17 attempts had validated process identity, exit code zero, no timeout,
  no forced cleanup, Game API disabled, at least one Startup draw, and one
  published Title backbuffer milestone. The two invalid attempts were retained
  solely for `load_content_rounding` and excluded from arithmetic;
- independent review re-opened all 17 result artifacts, reproduced every
  range and median above, verified the exact 15-slot order and hashes, and
  checked every one of the 181 manifest rows against its current byte length
  and SHA-256. The complete summary SHA-256 is
  `854e9ad9c7179d87597f8f73a96449b35748325a409c24334785a025950b13b4`;
  the 181-row manifest SHA-256 is
  `0fa99753023093c2eaabaf5131abf2b43da4b5df451299f40a844ce126fe73cc`;
  and
- `git diff --check` passed for the report.

This report stops at measured candidate intervals and a conservative sizing
budget. It does not authorize or describe product implementation mechanics.

decision=stop reason=broader_runtime_or_packaging_design_required

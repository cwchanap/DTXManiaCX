#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
game_dir_input="${1:?usage: benchmark-startup.sh GAME_DIR CORPUS LABEL RUN_ID}"
game_dir="$(cd "$game_dir_input" && pwd)"
corpus="${2:?usage: benchmark-startup.sh GAME_DIR CORPUS LABEL RUN_ID}"
label="${3:?usage: benchmark-startup.sh GAME_DIR CORPUS LABEL RUN_ID}"
run="${4:?usage: benchmark-startup.sh GAME_DIR CORPUS LABEL RUN_ID}"
game_dll="$game_dir/DTXMania.Game.Mac.dll"
result_root="$repo_root/TestResults/hpa-192/$label"
api_key="hpa-192-benchmark-key"
require_timing="${HPA192_REQUIRE_TIMING:-0}"
require_external_ready="${HPA192_REQUIRE_EXTERNAL_READY:-0}"
expected_persistence_path="${HPA192_EXPECT_PERSISTENCE_PATH:-}"
expected_song_count="${HPA192_EXPECT_SONG_COUNT:-}"
# Optional rtk harness prefix. When rtk is unavailable, leave this unset (or
# set to empty) so commands run directly without the wrapper.
RTK="${HPA192_RTK_PREFIX:-}"
api_port="$($RTK python3 -c \
    'import socket; s = socket.socket(); s.bind(("127.0.0.1", 0)); print(s.getsockname()[1]); s.close()')"
launch_token="hpa192-$label-$run"
lock_path="${TMPDIR:-/tmp}/hpa-192-benchmark-startup.lock"
lock_acquired=false
run_root=""
game_pid=""

release_lock() {
    if [[ "$lock_acquired" == true && "$(readlink "$lock_path" 2>/dev/null || true)" == "$$" ]]; then
        rm -f "$lock_path"
    fi
    lock_acquired=false
}

acquire_lock() {
    local owner_pid

    while true; do
        if ln -s "$$" "$lock_path" 2>/dev/null; then
            lock_acquired=true
            return 0
        fi

        owner_pid="$(readlink "$lock_path" 2>/dev/null || true)"
        if [[ "$owner_pid" =~ ^[0-9]+$ ]] && kill -0 "$owner_pid" 2>/dev/null; then
            printf 'another HPA-192 benchmark invocation holds %s (PID %s)\n' "$lock_path" "$owner_pid" >&2
            return 1
        fi

        if [[ "$(readlink "$lock_path" 2>/dev/null || true)" == "$owner_pid" ]]; then
            printf 'reclaiming stale HPA-192 benchmark lock %s (PID %s)\n' "$lock_path" "${owner_pid:-unknown}" >&2
            rm -f "$lock_path"
        fi
    done
}

cleanup() {
    local exit_status=$?

    if [[ -n "$game_pid" ]]; then
        kill "$game_pid" 2>/dev/null || true
        wait "$game_pid" 2>/dev/null || true
        game_pid=""
    fi

    if [[ -n "$run_root" && -d "$run_root" ]]; then
        rm -rf -- "$run_root"
    fi

    release_lock

    trap - EXIT HUP INT TERM
    exit "$exit_status"
}

stop_game() {
    if [[ -n "$game_pid" ]]; then
        kill "$game_pid" 2>/dev/null || true
        wait "$game_pid" 2>/dev/null || true
        game_pid=""
    fi
}

trap cleanup EXIT HUP INT TERM

if ! acquire_lock; then
    exit 1
fi

[[ "$require_timing" == 0 || "$require_timing" == 1 ]] || {
    printf 'HPA192_REQUIRE_TIMING must be 0 or 1\n' >&2
    exit 2
}
[[ "$require_external_ready" == 0 || "$require_external_ready" == 1 ]] || {
    printf 'HPA192_REQUIRE_EXTERNAL_READY must be 0 or 1\n' >&2
    exit 2
}
test -f "$game_dll"
test -d "$corpus"
mkdir -p "$result_root"

run_root="$(mktemp -d "${TMPDIR:-/tmp}/hpa-192-run.XXXXXX")"
appdata="$run_root/appdata"
mkdir -p "$appdata"
cp -R "$repo_root/System" "$run_root/System"

{
    printf '%s\n' '[System]'
    printf 'SkinPath=%s\n' "$run_root/System"
    printf 'DTXPath=%s\n' "$corpus"
    printf '%s\n' '[Skin]'
    printf 'SystemSkinRoot=%s\n' "$run_root/System"
    printf '%s\n' '[Display]' 'ScreenWidth=1280' 'ScreenHeight=720'
    printf '%s\n' 'FullScreen=False' 'VSyncWait=False'
    printf '%s\n' '[Api]' 'EnableGameApi=True'
    printf 'GameApiPort=%s\n' "$api_port"
    printf 'GameApiKey=%s\n' "$api_key"
} > "$appdata/Config.ini"

stdout="$result_root/run-$run.stdout.log"
stderr="$result_root/run-$run.stderr.log"
launch_start_unix_us="$(perl -MTime::HiRes=time -e 'printf "%.0f", time * 1000000')"
launch_start_monotonic_us="$(perl -MTime::HiRes=clock_gettime,CLOCK_MONOTONIC -e 'printf "%.0f", clock_gettime(CLOCK_MONOTONIC) * 1000000')"
(
    cd "$game_dir"
    DTXMANIA_APPDATA_ROOT="$appdata" \
        DTXMANIA_LAUNCH_TOKEN="$launch_token" \
        dotnet "$game_dll"
) >"$stdout" 2>"$stderr" &
game_pid=$!

reached_title=false
for attempt in $(seq 1 1200); do
    health="$(curl -fsS \
        "http://127.0.0.1:$api_port/health" 2>/dev/null || true)"
    if [[ "$health" != *"\"launchToken\":\"$launch_token\""* ]]; then
        sleep 0.05
        continue
    fi
    state="$(curl -fsS \
        -H "X-Api-Key: $api_key" \
        -H 'Content-Type: application/json' \
        -d '{"jsonrpc":"2.0","id":1,"method":"getGameState","params":null}' \
        "http://127.0.0.1:$api_port/jsonrpc" 2>/dev/null || true)"
    if [[ "$state" == *"TitleStage"* ]]; then
        if [[ "$require_timing" == 1 ]]; then
            timing_count_so_far="$($RTK awk '/^HPA192_TIMING / { count++ } END { print count + 0 }' "$stdout")"
            if [[ "$timing_count_so_far" != 1 ]]; then
                sleep 0.05
                continue
            fi
        fi
        reached_title=true
        break
    fi
    sleep 0.05
done

launch_end_monotonic_us="$(perl -MTime::HiRes=clock_gettime,CLOCK_MONOTONIC -e 'printf "%.0f", clock_gettime(CLOCK_MONOTONIC) * 1000000')"
launch_end_unix_us="$(perl -MTime::HiRes=time -e 'printf "%.0f", time * 1000000')"
stop_game

if [[ "$reached_title" != true ]]; then
    printf 'run %s did not reach Title\n' "$run" >&2
    exit 1
fi

database="$appdata/songs.db"
chart_paths="$result_root/run-$run.chart-paths.txt"
expected_chart_paths="$run_root/expected-chart-paths.txt"
test -f "$database"
$RTK sqlite3 -noheader "$database" \
    'SELECT FilePath FROM SongCharts;' |
    LC_ALL=C sort >"$chart_paths"
$RTK rg --files "$corpus" |
    $RTK rg -i '\.(dtx|gda|g2d|bms|bme|bml)$' |
    LC_ALL=C sort >"$expected_chart_paths"
if ! $RTK diff -u "$expected_chart_paths" "$chart_paths"; then
    printf 'run %s imported chart paths differ from the frozen corpus\n' "$run" >&2
    exit 1
fi
chart_count="$($RTK sqlite3 -noheader "$database" \
    'SELECT COUNT(*) FROM SongCharts;')"
song_count="$($RTK sqlite3 -noheader "$database" \
    'SELECT COUNT(*) FROM Songs;')"
database_hash="$($RTK shasum -a 256 "$database" | $RTK awk '{print $1}')"
printf 'label=%s run=%s charts=%s songs=%s database_sha256=%s\n' \
    "$label" "$run" "$chart_count" "$song_count" "$database_hash" |
    tee "$result_root/run-$run.database.txt"

wall_ms="$(((launch_end_unix_us - launch_start_unix_us) / 1000))"
summary_count="$($RTK awk '/^HPA192_STARTUP / { count++ } END { print count + 0 }' "$stdout")"
if [[ "$summary_count" != 1 ]]; then
    printf 'run %s emitted %s HPA192_STARTUP lines; expected exactly one\n' "$run" "$summary_count" >&2
    exit 1
fi
summary="$($RTK awk '/^HPA192_STARTUP / { print }' "$stdout")"
timing_count="$($RTK awk '/^HPA192_TIMING / { count++ } END { print count + 0 }' "$stdout")"
if [[ "$require_timing" == 1 && "$timing_count" != 1 ]]; then
    printf 'run %s emitted %s HPA192_TIMING lines; expected exactly one\n' "$run" "$timing_count" >&2
    exit 1
fi
timing="$($RTK awk '/^HPA192_TIMING / { print }' "$stdout")"
if [[ "$require_external_ready" == 1 ]]; then
    printf 'run %s requested external readiness, but Task 0 has no readiness field\n' "$run" >&2
    exit 1
fi
if [[ -n "$expected_persistence_path" || -n "$expected_song_count" ]]; then
    printf 'run %s persistence expectations are unavailable before Task 1\n' "$run" >&2
    exit 1
fi
printf 'label=%s run=%s wall_ms=%s launch_start_unix_us=%s launch_start_monotonic_us=%s launch_end_unix_us=%s launch_end_monotonic_us=%s %s %s\n' \
    "$label" "$run" "$wall_ms" "$launch_start_unix_us" "$launch_start_monotonic_us" "$launch_end_unix_us" "$launch_end_monotonic_us" "$summary" "$timing" |
    tee "$result_root/run-$run.result.txt"

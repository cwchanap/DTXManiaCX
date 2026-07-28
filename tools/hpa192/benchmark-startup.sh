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
api_port="$(rtk python3 -c \
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
start="$(perl -MTime::HiRes=time -e 'printf "%.6f", time')"
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
        reached_title=true
        break
    fi
    sleep 0.05
done

end="$(perl -MTime::HiRes=time -e 'printf "%.6f", time')"
stop_game

if [[ "$reached_title" != true ]]; then
    printf 'run %s did not reach Title\n' "$run" >&2
    exit 1
fi

database="$appdata/songs.db"
chart_paths="$result_root/run-$run.chart-paths.txt"
expected_chart_paths="$run_root/expected-chart-paths.txt"
test -f "$database"
rtk sqlite3 -noheader "$database" \
    'SELECT FilePath FROM SongCharts;' |
    LC_ALL=C sort >"$chart_paths"
rtk rg --files "$corpus" |
    rtk rg -i '\.(dtx|gda|g2d|bms|bme|bml)$' |
    LC_ALL=C sort >"$expected_chart_paths"
if ! rtk diff -u "$expected_chart_paths" "$chart_paths"; then
    printf 'run %s imported chart paths differ from the frozen corpus\n' "$run" >&2
    exit 1
fi
chart_count="$(rtk sqlite3 -noheader "$database" \
    'SELECT COUNT(*) FROM SongCharts;')"
song_count="$(rtk sqlite3 -noheader "$database" \
    'SELECT COUNT(*) FROM Songs;')"
database_hash="$(rtk shasum -a 256 "$database" | rtk awk '{print $1}')"
printf 'label=%s run=%s charts=%s songs=%s database_sha256=%s\n' \
    "$label" "$run" "$chart_count" "$song_count" "$database_hash" |
    tee "$result_root/run-$run.database.txt"

wall_ms="$(perl -e 'printf "%.0f", 1000 * ($ARGV[1] - $ARGV[0])' "$start" "$end")"
summary_count="$(rtk awk '/^HPA192_STARTUP / { count++ } END { print count + 0 }' "$stdout")"
if [[ "$summary_count" != 1 ]]; then
    printf 'run %s emitted %s HPA192_STARTUP lines; expected exactly one\n' "$run" "$summary_count" >&2
    exit 1
fi
summary="$(rtk awk '/^HPA192_STARTUP / { print }' "$stdout")"
printf 'label=%s run=%s wall_ms=%s %s\n' "$label" "$run" "$wall_ms" "$summary" |
    tee "$result_root/run-$run.result.txt"

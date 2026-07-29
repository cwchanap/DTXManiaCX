#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
summarizer="$repo_root/tools/hpa192/summarize-critical-path.sh"
runner="$repo_root/tools/hpa192/benchmark-critical-path.sh"
temp_root="$(mktemp -d "${TMPDIR:-/tmp}/hpa-192-critical-path-test.XXXXXX")"

cleanup() {
    local pid_file
    local pid

    while IFS= read -r pid_file; do
        while IFS= read -r pid; do
            if [[ "$pid" =~ ^[0-9]+$ ]]; then
                kill "$pid" 2>/dev/null || true
                wait "$pid" 2>/dev/null || true
            fi
        done <"$pid_file"
    done < <(
        find "$temp_root" \
            \( -name 'target-pids.log' -o -name 'child-pids.log' \) \
            -type f -print 2>/dev/null
    )
    rm -rf -- "$temp_root"
}
trap cleanup EXIT

fail() {
    printf 'FAIL: %s\n' "$*" >&2
    exit 1
}

hash_for() {
    local character="$1"
    printf '%064d' 0 | tr '0' "$character"
}

write_result() {
    local path="$1"
    local scenario="${2:-A}"
    local slot="${3:-1}"
    local attempt="${4:-1}"
    local parsed=100
    local groups=27
    local added=100
    local database_charts=100
    local database_songs=27
    local config_hash
    local chart_paths_hash

    case "$scenario" in
        A)
            config_hash="$(hash_for f)"
            chart_paths_hash="$(hash_for 4)"
            ;;
        B)
            parsed=0
            groups=0
            added=0
            database_charts=0
            database_songs=0
            config_hash="$(hash_for 1)"
            chart_paths_hash="$(hash_for 2)"
            ;;
        C)
            config_hash="$(hash_for f)"
            chart_paths_hash="$(hash_for 4)"
            ;;
        *)
            config_hash="$(hash_for 6)"
            chart_paths_hash="$(hash_for 7)"
            ;;
    esac

    {
        printf '%s\n' \
            "HPA192_ATTEMPT scenario=$scenario slot=$slot attempt=$attempt launch_start_unix_us=1000000 launch_start_monotonic_us=5000000 observation_unix_us=1370000 observation_monotonic_us=5370000 exit_code=0 timed_out=0 forced_cleanup=0 game_api_enabled=0 database_charts=$database_charts database_songs=$database_songs game_sha256=$(hash_for a) runner_sha256=$(hash_for b) summarizer_sha256=$(hash_for c) corpus_manifest_sha256=$(hash_for d) corpus_observed_sha256=$(hash_for d) system_manifest_sha256=$(hash_for e) config_sha256=$config_hash config_observed_sha256=$config_hash empty_manifest_sha256=$(hash_for 2) empty_observed_sha256=$(hash_for 2) seed_manifest_sha256=$(hash_for 3) seed_observed_sha256=$(hash_for 3) chart_paths_sha256=$chart_paths_hash expected_chart_paths_sha256=$chart_paths_hash"
        printf '%s\n' \
            "HPA192_STARTUP path=enumeration outcome=success total_ms=170 db_init_ms=20 discovery_parse_ms=30 persistence_ms=20 cleanup_ms=5 hierarchy_ms=10 discovered=$parsed parsed=$parsed groups=$groups added=$added updated=0 preserved=0 skipped=0 conflicts=0 stale=0 error=none"
        printf '%s\n' \
            "HPA192_TIMING entry_to_config_ms=8 config_to_load_content_ms=12 load_content_to_startup_ms=10 startup_to_first_draw_ms=24 startup_to_summary_ms=170 summary_to_title_ms=40 entry_to_title_ms=240 entry_unix_us=1100000 title_unix_us=1340000"
        printf '%s\n' \
            "HPA192_CRITICAL_PATH outcome=success error=none entry_unix_us=1100000 title_backbuffer_unix_us=1360000 entry_to_title_backbuffer_ms=260 load_content_complete_from_entry_ms=20 startup_construct_begin_from_entry_ms=21 startup_construct_end_from_entry_ms=22 startup_activate_begin_from_entry_ms=23 startup_activation_from_entry_ms=30 startup_activate_end_from_entry_ms=31 load_content_return_from_entry_ms=32 base_initialize_return_from_entry_ms=33 input_manager_begin_from_entry_ms=34 input_manager_end_from_entry_ms=35 saved_bindings_begin_from_entry_ms=36 saved_bindings_end_from_entry_ms=37 graphics_initialize_begin_from_entry_ms=38 graphics_initialize_end_from_entry_ms=39 render_target_begin_from_entry_ms=40 render_target_end_from_entry_ms=41 initialize_complete_from_entry_ms=50 post_load_unattributed_ms=16 startup_first_update_begin_from_entry_ms=51 startup_first_update_end_from_entry_ms=52 startup_first_draw_begin_from_entry_ms=53 startup_first_draw_end_from_entry_ms=54 startup_updates_before_first_draw=1 startup_game_time_before_first_draw_ms=5 startup_draws_before_transition=1 db_invoke_from_entry_ms=60 db_task_return_from_entry_ms=61 db_terminal_from_entry_ms=80 db_observed_from_entry_ms=81 db_task_returned_terminal=0 enumeration_invoke_from_entry_ms=90 enumeration_task_return_from_entry_ms=91 enumeration_terminal_from_entry_ms=160 enumeration_observed_from_entry_ms=161 enumeration_task_returned_terminal=0 enumeration_unattributed_ms=5 db_service_setup_ms=1 db_corruption_probe_ms=1 db_invalid_recovery_count=0 db_invalid_recovery_ms=0 db_ensure_created_count=1 db_ensure_created_ms=1 db_encoding_pragmas_ms=1 db_version_work_ms=1 db_schema_ensures_ms=1 db_init_unattributed_ms=14 summary_request_from_entry_ms=200 title_construct_begin_from_entry_ms=201 title_construct_end_from_entry_ms=202 transition_start_from_entry_ms=203 transition_complete_from_entry_ms=220 transition_update_count=1 transition_game_time_ms=10 startup_deactivate_begin_from_entry_ms=220 startup_deactivate_end_from_entry_ms=221 title_activate_begin_from_entry_ms=222 title_activate_end_from_entry_ms=240 title_first_update_begin_from_entry_ms=241 title_first_update_end_from_entry_ms=242 title_stage_draw_begin_from_entry_ms=243 title_stage_draw_end_from_entry_ms=250 title_backbuffer_blit_begin_from_entry_ms=251 title_backbuffer_blit_end_from_entry_ms=260 summary_to_title_unattributed_ms=6 title_gpu_setup_ms=1 title_background_ms=1 title_menu_ms=1 title_font_ms=1 title_cursor_sound_ms=1 title_decide_sound_ms=1 title_game_start_sound_ms=1 title_game_start_fallback_ran=0 title_game_start_fallback_ms=0 title_sound_load_count=3 title_activation_unattributed_ms=11 title_backbuffer_published=1"
    } >"$path"
}

replace_once() {
    local path="$1"
    local old="$2"
    local replacement="$3"
    OLD="$old" REPLACEMENT="$replacement" perl -0pi -e '
        $count = s/\Q$ENV{OLD}\E/$ENV{REPLACEMENT}/g;
        END { exit($count == 1 ? 0 : 1); }
    ' "$path"
}

copy_and_replace() {
    local name="$1"
    local source="$2"
    local old="$3"
    local replacement="$4"
    local path="$temp_root/$name.result.txt"
    cp "$source" "$path"
    replace_once "$path" "$old" "$replacement" ||
        fail "$name fixture mutation did not match exactly once"
    printf '%s\n' "$path"
}

run_validate() {
    bash "$summarizer" --validate-attempt "$1"
}

run_summary() {
    bash "$summarizer" --summarize "$@"
}

assert_contains_line() {
    local expected="$1"
    local path="$2"
    grep -Fqx "$expected" "$path" ||
        fail "missing line '$expected' in $path"
}

assert_rejected() {
    local name="$1"
    local expected_reason="$2"
    local artifact="$3"
    local stdout="$temp_root/$name.stdout"
    local stderr="$temp_root/$name.stderr"
    if run_validate "$artifact" >"$stdout" 2>"$stderr"; then
        fail "$name unexpectedly succeeded"
    fi
    grep -Eq \
        "^HPA192_CRITICAL_PATH_ATTEMPT status=rejected scenario=([A-Z]|unknown) slot=" \
        "$stdout" ||
        fail "$name did not emit a rejection record"
    grep -Fq "reason=$expected_reason" "$stdout" ||
        fail "$name rejected for the wrong reason"
}

assert_summary_rejected() {
    local name="$1"
    local expected_reason="$2"
    shift 2
    local stdout="$temp_root/$name.stdout"
    local stderr="$temp_root/$name.stderr"
    if run_summary "$@" >"$stdout" 2>"$stderr"; then
        fail "$name unexpectedly succeeded"
    fi
    grep -Fq \
        "HPA192_CRITICAL_PATH_SUMMARY status=rejected reason=$expected_reason" \
        "$stdout" ||
        fail "$name rejected for the wrong reason"
}

portable_size() {
    if stat -f '%z' "$1" >/dev/null 2>&1; then
        stat -f '%z' "$1"
    else
        stat -c '%s' "$1"
    fi
}

build_fixture_manifest() {
    local root="$1"
    local output="$2"
    local file
    local relative

    : >"$output"
    while IFS= read -r file; do
        relative="${file#"$root"/}"
        printf '%s\t%s\t%s\n' \
            "$relative" \
            "$(portable_size "$file")" \
            "$(shasum -a 256 "$file" | awk '{ print $1 }')" \
            >>"$output"
    done < <(find "$root" -type f -print | LC_ALL=C sort)
}

create_fixture_corpus() {
    local root="$1"
    local chart_count="$2"
    local set_count="$3"
    local asset_count=$((592 - chart_count - set_count))
    local index

    mkdir -p "$root/charts" "$root/groups" "$root/assets"
    for ((index = 1; index <= chart_count; index++)); do
        printf 'chart-%03d\n' "$index" \
            >"$root/charts/chart-$(printf '%03d' "$index").dtx"
    done
    for ((index = 1; index <= set_count; index++)); do
        mkdir -p "$root/groups/group-$(printf '%02d' "$index")"
        printf 'set-%02d\n' "$index" \
            >"$root/groups/group-$(printf '%02d' "$index")/SET.def"
    done
    for ((index = 1; index <= asset_count; index++)); do
        printf 'asset-%03d\n' "$index" \
            >"$root/assets/asset $(printf '%03d' "$index").bin"
    done
}

create_runner_fixture() {
    local name="$1"
    local chart_count="${2:-100}"
    local set_count="${3:-27}"
    local full_result
    local canonical_fixture_corpus
    local canonical_cache_corpus

    fixture_root="$temp_root/runner-$name"
    fixture_repo="$fixture_root/repo"
    fixture_game="$fixture_root/game"
    fixture_corpus="$fixture_root/corpus"
    fixture_result="$fixture_root/result"
    fixture_fakebin="$fixture_root/fakebin"
    fixture_raw="$fixture_root/raw"
    fixture_forbidden="$fixture_root/forbidden.log"
    fixture_launch_log="$fixture_root/launch.log"
    fixture_target_pids="$fixture_root/target-pids.log"
    fixture_child_pids="$fixture_root/child-pids.log"
    fixture_sleep_log="$fixture_root/sleep.log"
    fixture_state="$fixture_root/state"
    fixture_clock_state="$fixture_root/clock"
    fixture_cache="$temp_root/runner-default-corpus-cache"

    mkdir -p \
        "$fixture_repo/tools/hpa192" \
        "$fixture_repo/docs/performance" \
        "$fixture_repo/System" \
        "$fixture_game" \
        "$fixture_result" \
        "$fixture_fakebin" \
        "$fixture_raw"
    cp "$runner" "$fixture_repo/tools/hpa192/benchmark-critical-path.sh"
    cp "$summarizer" "$fixture_repo/tools/hpa192/summarize-critical-path.sh"
    printf 'system fixture\n' >"$fixture_repo/System/system.txt"
    : >"$fixture_game/DTXMania.Game.Mac.dll"

    if [[ "$chart_count" -eq 100 &&
          "$set_count" -eq 27 &&
          -d "$fixture_cache/corpus" ]]; then
        cp -R "$fixture_cache/corpus" "$fixture_corpus"
        canonical_fixture_corpus="$(cd "$fixture_corpus" && pwd -P)"
        canonical_cache_corpus="$(cd "$fixture_cache/corpus" && pwd -P)"
        cp \
            "$fixture_cache/corpus-manifest.tsv" \
            "$fixture_repo/docs/performance/HPA-192-corpus-manifest.tsv"
        sed "s|$canonical_cache_corpus|$canonical_fixture_corpus|" \
            "$fixture_cache/expected-chart-paths.txt" \
            >"$fixture_root/expected-chart-paths.txt"
    else
        create_fixture_corpus "$fixture_corpus" "$chart_count" "$set_count"
        canonical_fixture_corpus="$(cd "$fixture_corpus" && pwd -P)"
        build_fixture_manifest \
            "$fixture_corpus" \
            "$fixture_repo/docs/performance/HPA-192-corpus-manifest.tsv"

        find "$canonical_fixture_corpus" -type f \
            \( -iname '*.dtx' -o -iname '*.gda' -o -iname '*.g2d' \
               -o -iname '*.bms' -o -iname '*.bme' -o -iname '*.bml' \) \
            -print |
            LC_ALL=C sort >"$fixture_root/expected-chart-paths.txt"
        if [[ "$chart_count" -eq 100 && "$set_count" -eq 27 ]]; then
            mkdir -p "$fixture_cache"
            cp -R "$fixture_corpus" "$fixture_cache/corpus"
            cp \
                "$fixture_repo/docs/performance/HPA-192-corpus-manifest.tsv" \
                "$fixture_cache/corpus-manifest.tsv"
            canonical_cache_corpus="$(cd "$fixture_cache/corpus" && pwd -P)"
            sed "s|$canonical_fixture_corpus|$canonical_cache_corpus|" \
                "$fixture_root/expected-chart-paths.txt" \
                >"$fixture_cache/expected-chart-paths.txt"
        fi
    fi

    full_result="$fixture_root/raw-A.result.txt"
    write_result "$full_result" A 1 1
    awk '!/^HPA192_ATTEMPT /' "$full_result" >"$fixture_raw/A.txt"
    full_result="$fixture_root/raw-B.result.txt"
    write_result "$full_result" B 2 1
    awk '!/^HPA192_ATTEMPT /' "$full_result" >"$fixture_raw/B.txt"
    cp "$fixture_raw/A.txt" "$fixture_raw/C.txt"

    cat >"$fixture_fakebin/dotnet" <<'FAKE_DOTNET'
#!/usr/bin/env bash
set -euo pipefail

appdata="${DTXMANIA_APPDATA_ROOT:?}"
config="$appdata/Config.ini"
database="$appdata/songs.db"
count=0
if [[ -f "$HPA192_FAKE_STATE" ]]; then
    count="$(cat "$HPA192_FAKE_STATE")"
fi
count=$((count + 1))
printf '%s\n' "$count" >"$HPA192_FAKE_STATE"
printf '%s\n' "$$" >>"$HPA192_FAKE_TARGET_PIDS"
/bin/sleep 0.05

IFS=',' read -r -a modes <<<"${HPA192_FAKE_SEQUENCE:-success}"
if (( count <= ${#modes[@]} )); then
    mode="${modes[$((count - 1))]}"
else
    mode="${modes[$((${#modes[@]} - 1))]}"
fi

dtx_path="$(
    awk -F= '
        $0 == "[System]" { in_system = 1; next }
        /^\[/ { in_system = 0 }
        in_system && $1 == "DTXPath" {
            sub(/^[^=]*=/, "")
            print
            exit
        }
    ' "$config"
)"
if [[ "$dtx_path" == */empty-songs ]]; then
    scenario=B
elif [[ -f "$database" ]]; then
    scenario=C
else
    scenario=A
fi
printf '%s\n' "$scenario" >>"$HPA192_FAKE_LAUNCH_LOG"

if [[ "${HPA192_CRITICAL_PATH:-}" != 1 ||
      "${HPA192_EXIT_AFTER_CRITICAL_PATH:-}" != 1 ]]; then
    printf '%s\n' \
        'HPA192_CRITICAL_PATH_FAILURE outcome=failure error=flags last_milestone=entry'
    exit 0
fi

if [[ "$mode" == no-line-child ]]; then
    /bin/sleep 120 &
    printf '%s\n' "$!" >>"$HPA192_FAKE_CHILD_PIDS"
    /bin/sleep 120
fi
if [[ "$mode" == no-line ]]; then
    /bin/sleep 120
fi

rm -f "$database" "$database-wal" "$database-shm"
sqlite3 "$database" \
    'CREATE TABLE SongCharts (FilePath TEXT NOT NULL); CREATE TABLE Songs (Id INTEGER NOT NULL);'
if [[ "$scenario" != B ]]; then
    {
        printf '%s\n' 'BEGIN;'
        while IFS= read -r chart; do
            printf "INSERT INTO SongCharts (FilePath) VALUES ('%s');\n" "$chart"
        done <"$HPA192_FAKE_CHART_PATHS"
        index=1
        while (( index <= 27 )); do
            printf 'INSERT INTO Songs (Id) VALUES (%s);\n' "$index"
            index=$((index + 1))
        done
        printf '%s\n' 'COMMIT;'
    } | sqlite3 "$database"
fi

case "$mode" in
    failure)
        printf '%s\n' \
            'HPA192_CRITICAL_PATH_FAILURE outcome=failure error=synthetic last_milestone=database'
        ;;
    mutate-config)
        /usr/bin/perl -0pi -e \
            's/\[Api\]\nEnableGameApi=False/[Api]\nEnableGameApi=True/' \
            "$config"
        cat "$HPA192_FAKE_RAW/$scenario.txt"
        ;;
    mutate-corpus)
        printf 'mutated during launch\n' >>"$HPA192_FAKE_CORPUS_MUTATION"
        cat "$HPA192_FAKE_RAW/$scenario.txt"
        ;;
    nonzero)
        cat "$HPA192_FAKE_RAW/$scenario.txt"
        exit 7
        ;;
    missing-db)
        rm -f "$database"
        cat "$HPA192_FAKE_RAW/$scenario.txt"
        ;;
    stuck)
        cat "$HPA192_FAKE_RAW/$scenario.txt"
        /bin/sleep 120
        ;;
    stderr-only)
        cat "$HPA192_FAKE_RAW/$scenario.txt" >&2
        /bin/sleep 120
        ;;
    success)
        cat "$HPA192_FAKE_RAW/$scenario.txt"
        ;;
    *)
        printf 'unknown fake mode: %s\n' "$mode" >&2
        exit 9
        ;;
esac
FAKE_DOTNET

cat >"$fixture_fakebin/perl" <<'FAKE_PERL'
#!/usr/bin/env bash
set -euo pipefail
if [[ "$*" != *Time::HiRes* ]]; then
    exec /usr/bin/perl "$@"
fi
if [[ "$*" == *CLOCK_MONOTONIC* ]]; then
    state="$HPA192_FAKE_CLOCK_STATE.monotonic"
    first=5000000
    second=5370000
else
    state="$HPA192_FAKE_CLOCK_STATE.unix"
    first=1000000
    second=1370000
fi
count=0
if [[ -f "$state" ]]; then
    count="$(cat "$state")"
fi
count=$((count + 1))
printf '%s\n' "$count" >"$state"
if [[ "$state" == *.monotonic &&
      -n "${HPA192_FAKE_CLOCK_STEP_US:-}" ]]; then
    printf '%s' \
        "$((first + (count - 1) * HPA192_FAKE_CLOCK_STEP_US))"
elif [[ "$state" == *.monotonic ]]; then
    unix_count="$(cat "$HPA192_FAKE_CLOCK_STATE.unix")"
    if (( unix_count % 2 == 0 )); then
        printf '%s' "$second"
    else
        printf '%s' "$first"
    fi
else
    if (( count % 2 == 1 )); then
        printf '%s' "$first"
    else
        printf '%s' "$second"
    fi
fi
FAKE_PERL

    cat >"$fixture_fakebin/sleep" <<'FAKE_SLEEP'
#!/usr/bin/env bash
printf '%s\n' "${1:-}" >>"$HPA192_FAKE_SLEEP_LOG"
exec /bin/sleep 0.001
FAKE_SLEEP

    cat >"$fixture_fakebin/curl" <<'FORBIDDEN'
#!/usr/bin/env bash
printf 'curl\n' >>"$HPA192_FAKE_FORBIDDEN"
exit 97
FORBIDDEN

    cat >"$fixture_fakebin/screencapture" <<'FORBIDDEN'
#!/usr/bin/env bash
printf 'screencapture\n' >>"$HPA192_FAKE_FORBIDDEN"
exit 97
FORBIDDEN

    cat >"$fixture_fakebin/ps" <<'FAKE_PS'
#!/usr/bin/env bash
set -euo pipefail
pid=
format=
while [[ "$#" -gt 0 ]]; do
    case "$1" in
        -p)
            pid="$2"
            shift 2
            ;;
        -o)
            format="$2"
            shift 2
            ;;
        *)
            shift
            ;;
    esac
done
case "$format" in
    pid=)
        printf '%s\n' "$pid"
        ;;
    command=)
        printf 'dotnet %s\n' "$HPA192_FAKE_GAME_DLL"
        ;;
    *)
        exit 2
        ;;
esac
FAKE_PS

    chmod +x \
        "$fixture_fakebin/dotnet" \
        "$fixture_fakebin/perl" \
        "$fixture_fakebin/sleep" \
        "$fixture_fakebin/curl" \
        "$fixture_fakebin/screencapture" \
        "$fixture_fakebin/ps"
    : >"$fixture_forbidden"
    : >"$fixture_launch_log"
    : >"$fixture_target_pids"
    : >"$fixture_child_pids"
    : >"$fixture_sleep_log"
}

reset_runner_fixture() {
    rm -f \
        "$fixture_state" \
        "$fixture_clock_state.unix" \
        "$fixture_clock_state.monotonic"
    : >"$fixture_launch_log"
    : >"$fixture_target_pids"
    : >"$fixture_child_pids"
    : >"$fixture_sleep_log"
}

run_fixture_runner() {
    local command="$1"
    local sequence="$2"

    env \
        PATH="$fixture_fakebin:$PATH" \
        HPA192_FAKE_SEQUENCE="$sequence" \
        HPA192_FAKE_STATE="$fixture_state" \
        HPA192_FAKE_CLOCK_STATE="$fixture_clock_state" \
        HPA192_FAKE_CLOCK_STEP_US="${HPA192_FAKE_CLOCK_STEP_US:-}" \
        HPA192_FAKE_RAW="$fixture_raw" \
        HPA192_FAKE_CHART_PATHS="$fixture_root/expected-chart-paths.txt" \
        HPA192_FAKE_FORBIDDEN="$fixture_forbidden" \
        HPA192_FAKE_LAUNCH_LOG="$fixture_launch_log" \
        HPA192_FAKE_TARGET_PIDS="$fixture_target_pids" \
        HPA192_FAKE_CHILD_PIDS="$fixture_child_pids" \
        HPA192_FAKE_SLEEP_LOG="$fixture_sleep_log" \
        HPA192_FAKE_GAME_DLL="$(
            /usr/bin/perl -MCwd=realpath -e \
                'print realpath($ARGV[0])' \
                "$fixture_game/DTXMania.Game.Mac.dll"
        )" \
        HPA192_FAKE_CORPUS_MUTATION="$fixture_corpus/assets/asset 001.bin" \
        bash "$fixture_repo/tools/hpa192/benchmark-critical-path.sh" \
        "$command" \
        "$fixture_game" \
        "$fixture_corpus" \
        "$fixture_result"
}

assert_all_recorded_pids_dead() {
    local pid_file="$1"
    local pid

    while IFS= read -r pid; do
        [[ "$pid" =~ ^[0-9]+$ ]] ||
            fail "invalid PID recorded in $pid_file"
        if kill -0 "$pid" 2>/dev/null; then
            fail "runner left target PID $pid alive"
        fi
    done <"$pid_file"
}

assert_no_forbidden_commands() {
    [[ ! -s "$fixture_forbidden" ]] ||
        fail "runner invoked a forbidden HTTP or screenshot command"
}

layout_failures=()

layout_failure() {
    layout_failures+=("$1")
}

assert_rejected_before_launch_without_tree_changes() {
    local description="$1"
    local tree="$2"
    local before="$fixture_root/$description.before.tsv"
    local after="$fixture_root/$description.after.tsv"

    build_fixture_manifest "$tree" "$before"
    if run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr"; then
        layout_failure "$description layout was accepted"
    elif [[ -s "$fixture_launch_log" ]]; then
        layout_failure "$description layout was rejected only after launch"
    fi
    build_fixture_manifest "$tree" "$after"
    cmp -s "$before" "$after" ||
        layout_failure "$description layout was written before rejection"
}

run_runner_layout_tests() {
    local selection="${1:-all}"
    local failure

    layout_failures=()

    # Break caught: rejecting the approved Task 10/11 layout merely because
    # the immutable fixed build and control records predate seed preparation.
    if [[ "$selection" == all || "$selection" == planned ]]; then
        create_runner_fixture planned-layout
        mv "$fixture_game" "$fixture_result/build"
        fixture_game="$fixture_result/build"
        printf 'commit=fixture\nsha256=fixture\n' \
            >"$fixture_result/fixed-inputs.txt"
        printf 'machine=fixture\n' >"$fixture_result/environment.txt"
        if ! run_fixture_runner prepare-seed success \
            >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr"; then
            layout_failure "planned RESULT_ROOT/build layout was rejected"
        fi
    fi

    # Break caught: treating arbitrary preexisting result-root entries as
    # harmless even though they are outside the prepare-seed phase contract.
    if [[ "$selection" == all || "$selection" == rejections ]]; then
        create_runner_fixture unexpected-entry
        printf 'unowned\n' >"$fixture_result/unexpected.bin"
        if run_fixture_runner prepare-seed success \
            >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr"; then
            layout_failure "unexpected result-root entry was accepted"
        elif [[ -s "$fixture_launch_log" ]]; then
            layout_failure \
                "unexpected result-root entry was rejected after launch"
        fi
    fi

    # Break caught: allowing extra entries inside a phase-owned control
    # directory even though the matrix contract names its complete contents.
    if [[ "$selection" == all || "$selection" == nested ]]; then
        create_runner_fixture nested-unexpected-entry
        if ! run_fixture_runner prepare-seed success \
            >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr"; then
            layout_failure "nested-entry seed preparation failed"
        else
            printf 'unowned\n' >"$fixture_result/configs/unexpected.ini"
            reset_runner_fixture
            if run_fixture_runner matrix failure \
                >"$fixture_root/matrix.stdout" \
                2>"$fixture_root/matrix.stderr"; then
                layout_failure "unexpected nested phase entry was accepted"
            elif [[ -s "$fixture_launch_log" ]]; then
                layout_failure \
                    "unexpected nested phase entry was rejected after launch"
            fi
        fi
    fi

    # Break caught: allowing the writable result root to contain an
    # unapproved game directory instead of the exact immutable build child.
    if [[ "$selection" == all || "$selection" == rejections ]]; then
        create_runner_fixture unsafe-game-child
        mv "$fixture_game" "$fixture_result/other-build"
        fixture_game="$fixture_result/other-build"
        assert_rejected_before_launch_without_tree_changes \
            unsafe-game-child \
            "$fixture_result"

        # Break caught: writing runner outputs into the frozen corpus.
        create_runner_fixture corpus-overlap
        fixture_result="$fixture_corpus"
        assert_rejected_before_launch_without_tree_changes \
            corpus-overlap \
            "$fixture_corpus"

        # Break caught: writing runner outputs into the repository System tree.
        create_runner_fixture system-overlap
        fixture_result="$fixture_repo/System"
        assert_rejected_before_launch_without_tree_changes \
            system-overlap \
            "$fixture_repo/System"

        # Break caught: a symlink alias hiding that RESULT_ROOT is the corpus.
        create_runner_fixture symlink-alias
        ln -s "$fixture_corpus" "$fixture_root/result-alias"
        fixture_result="$fixture_root/result-alias"
        assert_rejected_before_launch_without_tree_changes \
            symlink-alias \
            "$fixture_corpus"
    fi

    if (( ${#layout_failures[@]} > 0 )); then
        for failure in "${layout_failures[@]}"; do
            printf 'LAYOUT FAIL: %s\n' "$failure" >&2
        done
        fail "runner layout contract has ${#layout_failures[@]} failures"
    fi
}

run_runner_deadline_tests() {
    local attempt_number
    local launch
    local observation
    local poll_sleeps
    local result

    # Break caught: a loop-count timeout whose polling work can extend the
    # attempt beyond launch CLOCK_MONOTONIC + 60,000,000 microseconds.
    create_runner_fixture monotonic-deadline
    run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr" ||
        fail "deadline seed preparation failed"
    reset_runner_fixture
    if HPA192_FAKE_CLOCK_STEP_US=10000000 \
        run_fixture_runner matrix no-line \
        >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
        fail "no-line deadline attempts were accepted"
    fi
    for attempt_number in 1 2 3; do
        result="$fixture_result/slots/01-A/attempt-$attempt_number/result.txt"
        launch="$(
            sed -n '1s/.* launch_start_monotonic_us=\([0-9]*\) .*/\1/p' \
                "$result"
        )"
        observation="$(
            sed -n '1s/.* observation_monotonic_us=\([0-9]*\) .*/\1/p' \
                "$result"
        )"
        [[ "$launch" =~ ^[0-9]+$ && "$observation" =~ ^[0-9]+$ ]] ||
            fail "deadline attempt $attempt_number lacks monotonic metadata"
        [[ "$((observation - launch))" -eq 60000000 ]] ||
            fail "deadline attempt $attempt_number did not stop at 60 seconds"
        grep -Eq ' timed_out=1 forced_cleanup=1 ' "$result" ||
            fail "deadline attempt $attempt_number lacks timeout cleanup flags"
    done
    poll_sleeps="$(grep -Fxc '0.05' "$fixture_sleep_log")"
    [[ "$poll_sleeps" -lt 500 ]] ||
        fail "polling overhead extended the monotonic deadline"
    assert_all_recorded_pids_dead "$fixture_target_pids"
    assert_no_forbidden_commands
}

run_runner_process_tests() {
    local expected_order='A B C B C A C A B A C B C B A'
    local actual_order
    local seed_hash_before
    local seed_hash_after
    local attempt_number
    local pid
    local accepted_path

    # Breaks caught: changing the fixed sequence, accepting malformed producer
    # artifacts, calling HTTP/screenshot commands, or mutating the clean seed.
    create_runner_fixture success-matrix
    run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr" ||
        fail "success matrix seed preparation failed"
    seed_hash_before="$(shasum -a 256 "$fixture_result/seed/manifest.tsv")"
    reset_runner_fixture
    if ! run_fixture_runner matrix success \
        >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
        tail -80 "$fixture_root/matrix.stdout" >&2
        tail -80 "$fixture_root/matrix.stderr" >&2
        find "$fixture_result/slots" -name validation.txt -type f -print \
            -exec tail -1 {} \; >&2
        find "$fixture_result/slots" -name result.txt -type f -print \
            -exec sed -n '1p' {} \; >&2
        fail "valid fixed matrix failed"
    fi
    actual_order="$(tr '\n' ' ' <"$fixture_launch_log" | sed 's/ $//')"
    [[ "$actual_order" == "$expected_order" ]] ||
        fail "matrix launch order was '$actual_order'"
    [[ "$(awk 'END { print NR + 0 }' "$fixture_result/accepted-artifacts.txt")" -eq 15 ]] ||
        fail "matrix did not retain exactly 15 accepted artifacts"
    while IFS= read -r accepted_path; do
        [[ "$(sed -n '1p' "$accepted_path")" == HPA192_ATTEMPT\ * ]] ||
            fail "attempt metadata was not the first artifact line"
        [[ "$(sed -n '2p' "$accepted_path")" == HPA192_STARTUP\ * ]] ||
            fail "startup line did not follow attempt metadata"
        [[ "$(sed -n '3p' "$accepted_path")" == HPA192_TIMING\ * ]] ||
            fail "timing line was not the second raw product line"
        [[ "$(sed -n '4p' "$accepted_path")" == HPA192_CRITICAL_PATH\ * ]] ||
            fail "critical-path line was not the third raw product line"
        grep -Fq 'HPA192_CRITICAL_PATH_ATTEMPT status=accepted' \
            "$(dirname "$accepted_path")/validation.txt" ||
            fail "accepted artifact lacks validator-derived fields"
    done <"$fixture_result/accepted-artifacts.txt"
    seed_hash_after="$(shasum -a 256 "$fixture_result/seed/manifest.tsv")"
    [[ "$seed_hash_before" == "$seed_hash_after" ]] ||
        fail "matrix mutated the clean seed manifest"
    assert_no_forbidden_commands

    # Break caught: replacing an invalid slot with a different scenario or
    # reusing/renaming attempt evidence.
    create_runner_fixture same-scenario-replacement
    run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr" ||
        fail "replacement seed preparation failed"
    reset_runner_fixture
    run_fixture_runner matrix failure,success \
        >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr" ||
        fail "same-scenario replacement matrix failed"
    actual_order="$(tr '\n' ' ' <"$fixture_launch_log" | sed 's/ $//')"
    [[ "$actual_order" == "A $expected_order" ]] ||
        fail "replacement launch order was '$actual_order'"
    [[ -f "$fixture_result/slots/01-A/attempt-1/result.txt" &&
       -f "$fixture_result/slots/01-A/attempt-2/result.txt" ]] ||
        fail "replacement attempts were not both retained"
    grep -Fq '/slots/01-A/attempt-2/result.txt' \
        "$fixture_result/accepted-artifacts.txt" ||
        fail "replacement artifact was not retained as attempt 2"
    grep -Fq 'status=rejected scenario=A slot=1 attempt=1' \
        "$fixture_result/slots/01-A/attempt-1/validation.txt" ||
        fail "invalid first attempt lacks its rejection record"
    assert_no_forbidden_commands

    # Break caught: waiting for a success line after the failure prefix or
    # launching later slots after the third rejection.
    create_runner_fixture failure-publication
    run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr" ||
        fail "failure-publication seed preparation failed"
    reset_runner_fixture
    if run_fixture_runner matrix failure \
        >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
        fail "failure publication was accepted"
    fi
    [[ "$(tr '\n' ' ' <"$fixture_launch_log")" == 'A A A ' ]] ||
        fail "failure retries changed scenario or launched later slots"
    for attempt_number in 1 2 3; do
        grep -Fq 'HPA192_CRITICAL_PATH_FAILURE ' \
            "$fixture_result/slots/01-A/attempt-$attempt_number/result.txt" ||
            fail "failure attempt $attempt_number lost its raw terminal line"
        grep -Eq ' timed_out=0 forced_cleanup=0 ' \
            "$fixture_result/slots/01-A/attempt-$attempt_number/result.txt" ||
            fail "failure publication waited until timeout"
    done
    assert_no_forbidden_commands

    # Break caught: broad cleanup that kills a child/sibling instead of only
    # the validated launched PID, or a no-line attempt without timeout flags.
    create_runner_fixture no-line-timeout
    run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr" ||
        fail "timeout seed preparation failed"
    reset_runner_fixture
    if HPA192_FAKE_CLOCK_STEP_US=1000000 \
        run_fixture_runner matrix no-line-child \
        >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
        fail "no-line attempts were accepted"
    fi
    for attempt_number in 1 2 3; do
        grep -Eq ' timed_out=1 forced_cleanup=1 ' \
            "$fixture_result/slots/01-A/attempt-$attempt_number/result.txt" ||
            fail "no-line attempt $attempt_number lost timeout metadata"
    done
    [[ "$(grep -Fxc '0.05' "$fixture_sleep_log")" -ge 177 ]] ||
        fail "no-line attempts did not enforce the fixed 60-second poll bound"
    assert_all_recorded_pids_dead "$fixture_target_pids"
    while IFS= read -r pid; do
        kill -0 "$pid" 2>/dev/null ||
            fail "PID-scoped cleanup killed child PID $pid"
        kill "$pid"
    done <"$fixture_child_pids"
    assert_no_forbidden_commands

    # Break caught: observing the companion prefix from stderr or any channel
    # other than the retained local stdout file.
    create_runner_fixture stderr-only
    run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr" ||
        fail "stderr-only seed preparation failed"
    reset_runner_fixture
    if HPA192_FAKE_CLOCK_STEP_US=1000000 \
        run_fixture_runner matrix stderr-only \
        >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
        fail "stderr-only publications were accepted"
    fi
    for attempt_number in 1 2 3; do
        grep -Eq ' timed_out=1 forced_cleanup=1 ' \
            "$fixture_result/slots/01-A/attempt-$attempt_number/result.txt" ||
            fail "stderr-only attempt $attempt_number did not time out"
        if grep -Fq 'HPA192_CRITICAL_PATH ' \
            "$fixture_result/slots/01-A/attempt-$attempt_number/result.txt"; then
            fail "stderr-only line entered result artifact"
        fi
        grep -Fq 'HPA192_CRITICAL_PATH ' \
            "$fixture_result/slots/01-A/attempt-$attempt_number/stderr.log" ||
            fail "stderr-only fixture did not emit its companion line"
    done
    assert_all_recorded_pids_dead "$fixture_target_pids"
    assert_no_forbidden_commands

    # Break caught: treating publication as permission to leave a stuck game
    # alive or misclassifying the bounded grace cleanup as a no-line timeout.
    create_runner_fixture post-publication-stuck
    run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr" ||
        fail "stuck-process seed preparation failed"
    reset_runner_fixture
    if run_fixture_runner matrix stuck \
        >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
        fail "post-publication stuck attempts were accepted"
    fi
    for attempt_number in 1 2 3; do
        grep -Eq ' timed_out=0 forced_cleanup=1 ' \
            "$fixture_result/slots/01-A/attempt-$attempt_number/result.txt" ||
            fail "stuck attempt $attempt_number has wrong cleanup metadata"
    done
    [[ "$(grep -Fxc '0.05' "$fixture_sleep_log")" -ge 300 ]] ||
        fail "stuck attempts skipped the bounded post-publication grace"
    assert_all_recorded_pids_dead "$fixture_target_pids"
    assert_no_forbidden_commands

    # Break caught: accepting a valid publication followed by nonzero exit.
    create_runner_fixture nonzero-exit
    run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr" ||
        fail "nonzero-exit seed preparation failed"
    reset_runner_fixture
    if run_fixture_runner matrix nonzero \
        >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
        fail "nonzero post-publication exits were accepted"
    fi
    for attempt_number in 1 2 3; do
        grep -Eq ' exit_code=7 timed_out=0 forced_cleanup=0 ' \
            "$fixture_result/slots/01-A/attempt-$attempt_number/result.txt" ||
            fail "nonzero attempt $attempt_number lost exit metadata"
    done
    assert_no_forbidden_commands

    # Break caught: treating a missing/unclean Scenario B database as a valid
    # empty database merely because both expected counts are zero.
    create_runner_fixture missing-empty-database
    run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr" ||
        fail "missing-database seed preparation failed"
    reset_runner_fixture
    if run_fixture_runner matrix success,missing-db \
        >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
        fail "runner accepted attempts without a closed database"
    fi
    grep -Fq \
        'decision=stop reason=diagnostic_harness slot=2 scenario=B' \
        "$fixture_result/decision.txt" ||
        fail "missing Scenario B database was not rejected in slot 2"
    for attempt_number in 1 2 3; do
        grep -Fq 'status=rejected scenario=B slot=2' \
            "$fixture_result/slots/02-B/attempt-$attempt_number/validation.txt" ||
            fail "missing database attempt $attempt_number was accepted"
    done
    assert_no_forbidden_commands

    # Break caught: relying on the command-start manifest after corpus bytes
    # change during an attempt.
    create_runner_fixture mid-launch-corpus-mutation
    run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr" ||
        fail "corpus-mutation seed preparation failed"
    reset_runner_fixture
    if run_fixture_runner matrix mutate-corpus \
        >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
        fail "runner accepted corpus bytes changed during launch"
    fi
    [[ "$(awk 'END { print NR + 0 }' "$fixture_launch_log")" -eq 1 ]] ||
        fail "runner retried after fixed corpus bytes changed"
    assert_no_forbidden_commands
}

if [[ "${1:-}" == --runner-seed-smoke ]]; then
    [[ -f "$runner" ]] || fail "critical-path runner is missing"
    create_runner_fixture seed-smoke
    mv \
        "$fixture_game/DTXMania.Game.Mac.dll" \
        "$fixture_game/DTXMania.Game.Mac.real.dll"
    ln -s \
        DTXMania.Game.Mac.real.dll \
        "$fixture_game/DTXMania.Game.Mac.dll"
    if ! run_fixture_runner prepare-seed success \
        >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr"; then
        tail -80 "$fixture_root/prepare.stderr" >&2
        fail "runner seed smoke failed"
    fi
    printf 'critical-path runner seed smoke passed\n'
    exit 0
fi

if [[ "${1:-}" == --runner-process ]]; then
    [[ -f "$runner" ]] || fail "critical-path runner is missing"
    run_runner_process_tests
    printf 'critical-path runner process tests passed\n'
    exit 0
fi

if [[ "${1:-}" == --runner-layout ]]; then
    [[ -f "$runner" ]] || fail "critical-path runner is missing"
    run_runner_layout_tests "${2:-all}"
    printf 'critical-path runner layout tests passed\n'
    exit 0
fi

if [[ "${1:-}" == --runner-deadline ]]; then
    [[ -f "$runner" ]] || fail "critical-path runner is missing"
    run_runner_deadline_tests
    printf 'critical-path runner deadline tests passed\n'
    exit 0
fi

round1_failures=()

round1_failure() {
    round1_failures+=("$1")
}

round1_expect_validate_rejected() {
    local name="$1"
    local expected_reason="$2"
    local artifact="$3"
    local stdout="$temp_root/$name.stdout"
    local stderr="$temp_root/$name.stderr"

    if run_validate "$artifact" >"$stdout" 2>"$stderr"; then
        round1_failure "$name unexpectedly succeeded"
    elif ! grep -Fq "reason=$expected_reason" "$stdout"; then
        round1_failure "$name rejected for the wrong reason"
    fi
}

round1_expect_summary_rejected() {
    local name="$1"
    local expected_reason="$2"
    local expected_attempt_records="$3"
    shift 3
    local stdout="$temp_root/$name.stdout"
    local stderr="$temp_root/$name.stderr"
    local attempt_records

    if run_summary "$@" >"$stdout" 2>"$stderr"; then
        round1_failure "$name unexpectedly succeeded"
    elif ! grep -Fq \
        "HPA192_CRITICAL_PATH_SUMMARY status=rejected reason=$expected_reason" \
        "$stdout"; then
        round1_failure "$name rejected for the wrong reason"
    fi

    attempt_records="$(
        awk '
            /^HPA192_CRITICAL_PATH_ATTEMPT / { count++ }
            END { print count + 0 }
        ' "$stdout"
    )"
    if [[ "$attempt_records" -ne "$expected_attempt_records" ]]; then
        round1_failure \
            "$name retained $attempt_records/$expected_attempt_records attempt records"
    fi
}

good="$temp_root/good.result.txt"
write_result "$good"

# Break caught: removing the validator must make a real valid artifact fail.
run_validate "$good" >"$temp_root/good.stdout"
assert_contains_line \
    "HPA192_CRITICAL_PATH_ATTEMPT status=accepted scenario=A slot=1 attempt=1 artifact_sha256=$(shasum -a 256 "$good" | awk '{ print $1 }') external_launch_to_entry_ms=100 external_launch_to_title_backbuffer_ms=360 stdout_observation_lag_ms=10 entry_to_load_content_complete_ms=20 load_content_complete_to_initialize_complete_ms=30 initialize_complete_to_summary_request_ms=150 summary_request_to_title_backbuffer_ms=60 initialize_complete_to_db_invoke_ms=10 db_operation_ms=20 db_terminal_to_observed_ms=1 db_observed_to_enumeration_invoke_ms=9 enumeration_operation_ms=70 enumeration_terminal_to_observed_ms=1 enumeration_observed_to_summary_request_ms=39 db_invoke_to_task_return_ms=1 db_async_after_task_return_ms=19 db_terminal_before_task_return_ms=0 enumeration_invoke_to_task_return_ms=1 enumeration_async_after_task_return_ms=69 enumeration_terminal_before_task_return_ms=0" \
    "$temp_root/good.stdout"

# Breaks caught: accepting a companion line with any schema mutation.
missing_field="$(copy_and_replace missing-field "$good" \
    " startup_construct_begin_from_entry_ms=21" "")"
assert_rejected missing_field critical_schema "$missing_field"

duplicate_field="$(copy_and_replace duplicate-field "$good" \
    " startup_construct_begin_from_entry_ms=21" \
    " startup_construct_begin_from_entry_ms=21 startup_construct_begin_from_entry_ms=21")"
assert_rejected duplicate_field critical_schema "$duplicate_field"

reordered_field="$(copy_and_replace reordered-field "$good" \
    " startup_construct_begin_from_entry_ms=21 startup_construct_end_from_entry_ms=22" \
    " startup_construct_end_from_entry_ms=22 startup_construct_begin_from_entry_ms=21")"
assert_rejected reordered_field critical_schema "$reordered_field"

unknown_field="$(copy_and_replace unknown-field "$good" \
    " startup_construct_begin_from_entry_ms=21" \
    " unknown_from_entry_ms=21")"
assert_rejected unknown_field critical_schema "$unknown_field"

duplicate_companion="$temp_root/duplicate-companion.result.txt"
cp "$good" "$duplicate_companion"
grep '^HPA192_CRITICAL_PATH ' "$good" >>"$duplicate_companion"
assert_rejected duplicate_companion critical_line_count "$duplicate_companion"

simultaneous_terminal="$temp_root/simultaneous-terminal.result.txt"
cp "$good" "$simultaneous_terminal"
printf '%s\n' \
    "HPA192_CRITICAL_PATH_FAILURE outcome=failure error=late last_milestone=exit" \
    >>"$simultaneous_terminal"
assert_rejected simultaneous_terminal conflicting_terminal_lines "$simultaneous_terminal"

unsafe_outcome="$(copy_and_replace unsafe-outcome "$good" \
    "HPA192_CRITICAL_PATH outcome=success" \
    "HPA192_CRITICAL_PATH outcome=suc/cess")"
assert_rejected unsafe_outcome critical_outcome "$unsafe_outcome"

unsafe_error="$(copy_and_replace unsafe-error "$good" \
    " outcome=success error=none entry_unix_us" \
    " outcome=success error=bad/error entry_unix_us")"
assert_rejected unsafe_error critical_error "$unsafe_error"

# Breaks caught: allowing shell arithmetic to see noncanonical or unbounded text.
signed_number="$(copy_and_replace signed-number "$good" \
    "entry_to_title_backbuffer_ms=260" \
    "entry_to_title_backbuffer_ms=+260")"
assert_rejected signed_number critical_number "$signed_number"

leading_zero="$(copy_and_replace leading-zero "$good" \
    "entry_to_title_backbuffer_ms=260" \
    "entry_to_title_backbuffer_ms=0260")"
assert_rejected leading_zero critical_number "$leading_zero"

overflowing_number="$(copy_and_replace overflowing-number "$good" \
    "entry_to_title_backbuffer_ms=260" \
    "entry_to_title_backbuffer_ms=18446744073709551616")"
assert_rejected overflowing_number critical_number "$overflowing_number"

out_of_range_number="$(copy_and_replace out-of-range-number "$good" \
    "entry_to_title_backbuffer_ms=260" \
    "entry_to_title_backbuffer_ms=300001")"
assert_rejected out_of_range_number critical_number "$out_of_range_number"

noncanonical_boolean="$(copy_and_replace noncanonical-boolean "$good" \
    "db_task_returned_terminal=0" \
    "db_task_returned_terminal=2")"
assert_rejected noncanonical_boolean critical_flag "$noncanonical_boolean"

# Breaks caught: accepting an artifact without all three compatibility lines.
missing_startup="$temp_root/missing-startup.result.txt"
awk '!/^HPA192_STARTUP /' "$good" >"$missing_startup"
assert_rejected missing_startup startup_line_count "$missing_startup"

missing_timing="$temp_root/missing-timing.result.txt"
awk '!/^HPA192_TIMING /' "$good" >"$missing_timing"
assert_rejected missing_timing timing_line_count "$missing_timing"

# Breaks caught: accepting malformed external metadata or a failure terminal.
reordered_attempt="$(copy_and_replace reordered-attempt "$good" \
    "scenario=A slot=1" \
    "slot=1 scenario=A")"
assert_rejected reordered_attempt attempt_schema "$reordered_attempt"

unknown_attempt="$(copy_and_replace unknown-attempt "$good" \
    "scenario=A" \
    "unknown_scenario=A")"
assert_rejected unknown_attempt attempt_schema "$unknown_attempt"

duplicate_attempt_line="$temp_root/duplicate-attempt-line.result.txt"
cp "$good" "$duplicate_attempt_line"
grep '^HPA192_ATTEMPT ' "$good" >>"$duplicate_attempt_line"
assert_rejected duplicate_attempt_line attempt_line_count "$duplicate_attempt_line"

leading_zero_slot="$(copy_and_replace leading-zero-slot "$good" \
    "scenario=A slot=1 attempt=1" \
    "scenario=A slot=01 attempt=1")"
assert_rejected leading_zero_slot attempt_number "$leading_zero_slot"

uppercase_hash="$(copy_and_replace uppercase-hash "$good" \
    "game_sha256=$(hash_for a)" \
    "game_sha256=$(hash_for A)")"
assert_rejected uppercase_hash attempt_hash "$uppercase_hash"

failure_only="$temp_root/failure-only.result.txt"
awk '!/^HPA192_CRITICAL_PATH /' "$good" >"$failure_only"
printf '%s\n' \
    "HPA192_CRITICAL_PATH_FAILURE outcome=failure error=database last_milestone=database" \
    >>"$failure_only"
assert_rejected failure_only failure_terminal "$failure_only"

# Breaks caught: accepting regressing, contradictory, or nonexclusive origins.
regressing_milestone="$(copy_and_replace regressing-milestone "$good" \
    "startup_construct_begin_from_entry_ms=21" \
    "startup_construct_begin_from_entry_ms=23")"
assert_rejected regressing_milestone milestone_order "$regressing_milestone"

inconsistent_task_flag="$(copy_and_replace inconsistent-task-flag "$good" \
    "db_task_returned_terminal=0" \
    "db_task_returned_terminal=1")"
assert_rejected inconsistent_task_flag task_return_order "$inconsistent_task_flag"

negative_post_load="$(copy_and_replace negative-post-load "$good" \
    "initialize_complete_from_entry_ms=50" \
    "initialize_complete_from_entry_ms=40")"
assert_rejected negative_post_load milestone_order "$negative_post_load"

negative_database="$(copy_and_replace negative-database "$good" \
    "db_service_setup_ms=1" \
    "db_service_setup_ms=30")"
assert_rejected negative_database database_residual "$negative_database"

negative_enumeration="$(copy_and_replace negative-enumeration "$good" \
    "discovery_parse_ms=30" \
    "discovery_parse_ms=80")"
assert_rejected negative_enumeration enumeration_residual "$negative_enumeration"

negative_title_activation="$(copy_and_replace negative-title-activation "$good" \
    "title_gpu_setup_ms=1" \
    "title_gpu_setup_ms=20")"
assert_rejected negative_title_activation title_activation_residual \
    "$negative_title_activation"

negative_summary_to_title="$(copy_and_replace negative-summary-to-title "$good" \
    "title_backbuffer_blit_begin_from_entry_ms=251" \
    "title_backbuffer_blit_begin_from_entry_ms=261")"
assert_rejected negative_summary_to_title milestone_order \
    "$negative_summary_to_title"

# Breaks caught: trusting producer residual fields without recomputing them.
post_load_off_by_one="$(copy_and_replace post-load-off-by-one "$good" \
    "post_load_unattributed_ms=16" \
    "post_load_unattributed_ms=15")"
assert_rejected post_load_off_by_one post_load_residual "$post_load_off_by_one"

database_off_by_one="$(copy_and_replace database-off-by-one "$good" \
    "db_init_unattributed_ms=14" \
    "db_init_unattributed_ms=13")"
assert_rejected database_off_by_one database_residual "$database_off_by_one"

enumeration_off_by_one="$(copy_and_replace enumeration-off-by-one "$good" \
    "enumeration_unattributed_ms=5" \
    "enumeration_unattributed_ms=4")"
run_validate "$enumeration_off_by_one" >"$temp_root/enumeration-off-by-one.stdout"
grep -Fq "status=accepted" "$temp_root/enumeration-off-by-one.stdout" ||
    fail "enumeration one-millisecond representation difference was rejected"

enumeration_off_by_five="$(copy_and_replace enumeration-off-by-five "$good" \
    "enumeration_unattributed_ms=5" \
    "enumeration_unattributed_ms=0")"
assert_rejected enumeration_off_by_five enumeration_residual \
    "$enumeration_off_by_five"

title_activation_off_by_one="$(copy_and_replace title-activation-off-by-one "$good" \
    "title_activation_unattributed_ms=11" \
    "title_activation_unattributed_ms=10")"
assert_rejected title_activation_off_by_one title_activation_residual \
    "$title_activation_off_by_one"

summary_to_title_off_by_one="$(copy_and_replace summary-to-title-off-by-one "$good" \
    "summary_to_title_unattributed_ms=6" \
    "summary_to_title_unattributed_ms=5")"
assert_rejected summary_to_title_off_by_one summary_to_title_residual \
    "$summary_to_title_off_by_one"

# Breaks caught: admitting an invalid activation, count, or publication gate.
activation_outside_span="$(copy_and_replace activation-outside-span "$good" \
    "startup_activation_from_entry_ms=30" \
    "startup_activation_from_entry_ms=32")"
assert_rejected activation_outside_span activation_span "$activation_outside_span"

wrong_sound_count="$(copy_and_replace wrong-sound-count "$good" \
    "title_sound_load_count=3" \
    "title_sound_load_count=4")"
assert_rejected wrong_sound_count title_sound_count "$wrong_sound_count"

zero_startup_draws="$(copy_and_replace zero-startup-draws "$good" \
    "startup_draws_before_transition=1" \
    "startup_draws_before_transition=0")"
assert_rejected zero_startup_draws startup_draw_count "$zero_startup_draws"

unpublished_title="$(copy_and_replace unpublished-title "$good" \
    "title_backbuffer_published=1" \
    "title_backbuffer_published=0")"
assert_rejected unpublished_title title_not_published "$unpublished_title"

recovery_ran="$(copy_and_replace recovery-ran "$good" \
    "db_invalid_recovery_count=0" \
    "db_invalid_recovery_count=1")"
assert_rejected recovery_ran database_recovery "$recovery_ran"

recovery_unrepresentable="$(copy_and_replace recovery-unrepresentable "$good" \
    "db_invalid_recovery_count=0" \
    "db_invalid_recovery_count=3")"
assert_rejected recovery_unrepresentable critical_number \
    "$recovery_unrepresentable"

ensure_created_twice="$(copy_and_replace ensure-created-twice "$good" \
    "db_ensure_created_count=1" \
    "db_ensure_created_count=2")"
assert_rejected ensure_created_twice ensure_created_count "$ensure_created_twice"

ensure_created_zero="$(copy_and_replace ensure-created-zero "$good" \
    "db_ensure_created_count=1" \
    "db_ensure_created_count=0")"
assert_rejected ensure_created_zero critical_number "$ensure_created_zero"

# Breaks caught: relaxing the approved cross-line representation bounds.
load_content_rounding="$(copy_and_replace load-content-rounding "$good" \
    "config_to_load_content_ms=12" \
    "config_to_load_content_ms=10")"
assert_rejected load_content_rounding load_content_rounding \
    "$load_content_rounding"

startup_activation_rounding="$(copy_and_replace startup-activation-rounding "$good" \
    "load_content_to_startup_ms=10" \
    "load_content_to_startup_ms=7")"
assert_rejected startup_activation_rounding startup_activation_rounding \
    "$startup_activation_rounding"

database_rounding="$(copy_and_replace database-rounding "$good" \
    "db_init_ms=20" \
    "db_init_ms=18")"
assert_rejected database_rounding database_rounding "$database_rounding"

# Break caught: rejecting values exactly on the approved rounding boundaries.
load_content_boundary="$(copy_and_replace load-content-boundary "$good" \
    "config_to_load_content_ms=12" \
    "config_to_load_content_ms=11")"
run_validate "$load_content_boundary" >"$temp_root/load-content-boundary.stdout"
grep -Fq "status=accepted" "$temp_root/load-content-boundary.stdout" ||
    fail "one-millisecond load-content rounding boundary was rejected"

startup_activation_boundary="$(copy_and_replace startup-activation-boundary "$good" \
    "load_content_to_startup_ms=10" \
    "load_content_to_startup_ms=8")"
run_validate "$startup_activation_boundary" \
    >"$temp_root/startup-activation-boundary.stdout"
grep -Fq "status=accepted" "$temp_root/startup-activation-boundary.stdout" ||
    fail "two-millisecond activation rounding boundary was rejected"

database_boundary="$(copy_and_replace database-boundary "$good" \
    "db_init_ms=20" \
    "db_init_ms=19")"
run_validate "$database_boundary" >"$temp_root/database-boundary.stdout"
grep -Fq "status=accepted" "$temp_root/database-boundary.stdout" ||
    fail "one-millisecond database rounding boundary was rejected"

enumeration_boundary="$(copy_and_replace enumeration-boundary "$good" \
    "enumeration_unattributed_ms=5" \
    "enumeration_unattributed_ms=1")"
run_validate "$enumeration_boundary" >"$temp_root/enumeration-boundary.stdout"
grep -Fq "status=accepted" "$temp_root/enumeration-boundary.stdout" ||
    fail "four-millisecond enumeration rounding boundary was rejected"

# Breaks caught: accepting stepped clocks or regressing external anchors.
process_clock_step="$temp_root/process-clock-step.result.txt"
cp "$good" "$process_clock_step"
replace_once "$process_clock_step" \
    "title_backbuffer_unix_us=1360000" \
    "title_backbuffer_unix_us=1411000"
replace_once "$process_clock_step" \
    "observation_unix_us=1370000" \
    "observation_unix_us=1420000"
assert_rejected process_clock_step process_clock_alignment "$process_clock_step"

external_clock_step="$(copy_and_replace external-clock-step "$good" \
    "observation_unix_us=1370000" \
    "observation_unix_us=1421000")"
assert_rejected external_clock_step external_clock_alignment "$external_clock_step"

process_clock_boundary="$temp_root/process-clock-boundary.result.txt"
cp "$good" "$process_clock_boundary"
replace_once "$process_clock_boundary" \
    "observation_unix_us=1370000 observation_monotonic_us=5370000" \
    "observation_unix_us=1410000 observation_monotonic_us=5410000"
replace_once "$process_clock_boundary" \
    "title_backbuffer_unix_us=1360000" \
    "title_backbuffer_unix_us=1410000"
run_validate "$process_clock_boundary" >"$temp_root/process-clock-boundary.stdout"
grep -Fq "status=accepted" "$temp_root/process-clock-boundary.stdout" ||
    fail "50-millisecond process clock boundary was rejected"
grep -Fq "external_launch_to_title_backbuffer_ms=360" \
    "$temp_root/process-clock-boundary.stdout" ||
    fail "end-to-end metric did not use the exclusive monotonic partition"

external_clock_boundary="$(copy_and_replace external-clock-boundary "$good" \
    "observation_unix_us=1370000" \
    "observation_unix_us=1420000")"
run_validate "$external_clock_boundary" >"$temp_root/external-clock-boundary.stdout"
grep -Fq "status=accepted" "$temp_root/external-clock-boundary.stdout" ||
    fail "50-millisecond external clock boundary was rejected"

entry_before_launch="$temp_root/entry-before-launch.result.txt"
cp "$good" "$entry_before_launch"
replace_once "$entry_before_launch" \
    "entry_to_title_ms=240 entry_unix_us=1100000 title_unix_us" \
    "entry_to_title_ms=240 entry_unix_us=999000 title_unix_us"
replace_once "$entry_before_launch" \
    "error=none entry_unix_us=1100000 title_backbuffer_unix_us" \
    "error=none entry_unix_us=999000 title_backbuffer_unix_us"
assert_rejected entry_before_launch external_anchor_order "$entry_before_launch"

title_before_entry="$(copy_and_replace title-before-entry "$good" \
    "title_backbuffer_unix_us=1360000" \
    "title_backbuffer_unix_us=1099000")"
assert_rejected title_before_entry process_anchor_order "$title_before_entry"

observation_before_title="$(copy_and_replace observation-before-title "$good" \
    "observation_unix_us=1370000" \
    "observation_unix_us=1359000")"
assert_rejected observation_before_title observation_anchor_order \
    "$observation_before_title"

# Breaks caught: accepting bytes or launch outcomes outside the frozen attempt.
unplanned_scenario="$(copy_and_replace unplanned-scenario "$good" \
    "HPA192_ATTEMPT scenario=A" \
    "HPA192_ATTEMPT scenario=D")"
assert_rejected unplanned_scenario unplanned_scenario "$unplanned_scenario"

changed_config="$(copy_and_replace changed-config "$good" \
    "config_observed_sha256=$(hash_for f)" \
    "config_observed_sha256=$(hash_for 6)")"
assert_rejected changed_config config_hash_mismatch "$changed_config"

changed_corpus="$(copy_and_replace changed-corpus "$good" \
    "corpus_observed_sha256=$(hash_for d)" \
    "corpus_observed_sha256=$(hash_for 6)")"
assert_rejected changed_corpus corpus_hash_mismatch "$changed_corpus"

changed_empty_directory="$(copy_and_replace changed-empty-directory "$good" \
    "empty_observed_sha256=$(hash_for 2)" \
    "empty_observed_sha256=$(hash_for 6)")"
assert_rejected changed_empty_directory empty_hash_mismatch \
    "$changed_empty_directory"

changed_seed="$(copy_and_replace changed-seed "$good" \
    "seed_observed_sha256=$(hash_for 3)" \
    "seed_observed_sha256=$(hash_for 6)")"
assert_rejected changed_seed seed_hash_mismatch "$changed_seed"

wrong_chart_paths="$(copy_and_replace wrong-chart-paths "$good" \
    " chart_paths_sha256=$(hash_for 4) expected_chart_paths_sha256" \
    " chart_paths_sha256=$(hash_for 6) expected_chart_paths_sha256")"
assert_rejected wrong_chart_paths chart_paths_hash_mismatch "$wrong_chart_paths"

wrong_database_count="$(copy_and_replace wrong-database-count "$good" \
    "database_charts=100" \
    "database_charts=99")"
assert_rejected wrong_database_count scenario_counts "$wrong_database_count"

wrong_parsed_count="$(copy_and_replace wrong-parsed-count "$good" \
    "parsed=100" \
    "parsed=99")"
assert_rejected wrong_parsed_count scenario_counts "$wrong_parsed_count"

wrong_group_count="$(copy_and_replace wrong-group-count "$good" \
    "groups=27" \
    "groups=26")"
assert_rejected wrong_group_count scenario_counts "$wrong_group_count"

nonzero_exit="$(copy_and_replace nonzero-exit "$good" \
    "exit_code=0" \
    "exit_code=1")"
assert_rejected nonzero_exit nonzero_exit "$nonzero_exit"

timed_out="$(copy_and_replace timed-out "$good" \
    "timed_out=0" \
    "timed_out=1")"
assert_rejected timed_out timed_out "$timed_out"

forced_cleanup="$(copy_and_replace forced-cleanup "$good" \
    "forced_cleanup=0" \
    "forced_cleanup=1")"
assert_rejected forced_cleanup forced_cleanup "$forced_cleanup"

game_api_enabled="$(copy_and_replace game-api-enabled "$good" \
    "game_api_enabled=0" \
    "game_api_enabled=1")"
assert_rejected game_api_enabled game_api_enabled "$game_api_enabled"

# Break caught: reading or rewriting an out-of-band acceptance sequence.
sentinel_root="$temp_root/sentinel-root"
sentinel="$sentinel_root/TestResults/hpa-192/critical-path-acceptance-sequence.txt"
mkdir -p "$(dirname "$sentinel")"
mkfifo "$sentinel"
if ! (
    cd "$sentinel_root"
    perl -e 'alarm 2; exec @ARGV' bash "$summarizer" --validate-attempt "$good"
) >"$temp_root/sentinel.stdout" 2>"$temp_root/sentinel.stderr"; then
    fail "validator touched the acceptance-sequence sentinel"
fi
test -p "$sentinel" || fail "validator changed the acceptance-sequence sentinel"

# Breaks caught: admitting anything except the fixed 15-slot acceptance set.
matrix_scenarios=(A B C B C A C A B A C B C B A)
matrix_paths=()
for matrix_index in {1..15}; do
    matrix_path="$temp_root/matrix-$matrix_index.result.txt"
    write_result \
        "$matrix_path" \
        "${matrix_scenarios[$((matrix_index - 1))]}" \
        "$matrix_index" \
        1
    matrix_paths+=("$matrix_path")
done

matrix_hashes_before="$(shasum -a 256 "${matrix_paths[@]}")"
if ! run_summary "${matrix_paths[@]}" >"$temp_root/matrix.stdout"; then
    fail "valid fixed matrix was rejected"
fi
matrix_hashes_after="$(shasum -a 256 "${matrix_paths[@]}")"
[[ "$matrix_hashes_before" == "$matrix_hashes_after" ]] ||
    fail "summarizer mutated a measured artifact"
assert_contains_line \
    "HPA192_CRITICAL_PATH_SUMMARY scenario=A samples=5 metric=external_launch_to_title_backbuffer_ms minimum_ms=360 median_ms=360 maximum_ms=360" \
    "$temp_root/matrix.stdout"
assert_contains_line \
    "HPA192_CRITICAL_PATH_SUMMARY scenario=C samples=5 metric=enumeration_observed_to_summary_request_ms minimum_ms=39 median_ms=39 maximum_ms=39" \
    "$temp_root/matrix.stdout"
if grep -Eq \
    "^HPA192_CRITICAL_PATH_SUMMARY .*metric=(db_invoke_to_task_return|db_async_after_task_return|db_terminal_before_task_return|enumeration_invoke_to_task_return|enumeration_async_after_task_return|enumeration_terminal_before_task_return)_ms " \
    "$temp_root/matrix.stdout"; then
    fail "diagnostic annotations entered the exclusive savings summary"
fi

# Fix round 1: each case exercises the real validator/summarizer and records
# every regression before failing, so one RED run proves all reported breaks.
for clock_skew_us in 50001 50999; do
    process_clock_us="$temp_root/process-clock-$clock_skew_us.result.txt"
    cp "$good" "$process_clock_us"
    process_title_unix_us=$((1360000 + clock_skew_us))
    process_observation_unix_us=$((process_title_unix_us + 10000))
    process_observation_monotonic_us=$((5000000 +
        process_observation_unix_us - 1000000))
    replace_once "$process_clock_us" \
        "observation_unix_us=1370000 observation_monotonic_us=5370000" \
        "observation_unix_us=$process_observation_unix_us observation_monotonic_us=$process_observation_monotonic_us"
    replace_once "$process_clock_us" \
        "title_backbuffer_unix_us=1360000" \
        "title_backbuffer_unix_us=$process_title_unix_us"
    round1_expect_validate_rejected \
        "process_clock_${clock_skew_us}us" \
        process_clock_alignment \
        "$process_clock_us"

    external_clock_us="$(copy_and_replace \
        "external-clock-$clock_skew_us" \
        "$good" \
        "observation_unix_us=1370000" \
        "observation_unix_us=$((1370000 + clock_skew_us))")"
    round1_expect_validate_rejected \
        "external_clock_${clock_skew_us}us" \
        external_clock_alignment \
        "$external_clock_us"
done

malformed_peer_hash="$temp_root/malformed-peer-hash.result.txt"
cp "${matrix_paths[0]}" "$malformed_peer_hash"
replace_once "$malformed_peer_hash" \
    "scenario=A slot=1 attempt=1" \
    "scenario=A slot=1 attempt=2"
replace_once "$malformed_peer_hash" \
    "game_sha256=$(hash_for a)" \
    "game_sha256=$(hash_for 6)"
replace_once "$malformed_peer_hash" \
    "runner_sha256=$(hash_for b)" \
    "runner_sha256=malformed"
round1_expect_summary_rejected \
    malformed_peer_fixed_hash \
    mixed_fixed_hashes \
    16 \
    "${matrix_paths[@]}" \
    "$malformed_peer_hash"
if ! grep -Eq \
    "^HPA192_CRITICAL_PATH_ATTEMPT status=rejected scenario=A slot=1 attempt=2 reason=attempt_hash artifact_sha256=[0-9a-f]{64}$" \
    "$temp_root/malformed_peer_fixed_hash.stdout"; then
    round1_failure \
        "malformed_peer_fixed_hash lost its readable per-attempt rejection"
fi

readable_duplicate="$temp_root/readable-duplicate.result.txt"
cp "${matrix_paths[0]}" "$readable_duplicate"
replace_once "$readable_duplicate" \
    "runner_sha256=$(hash_for b)" \
    "runner_hash=$(hash_for b)"
round1_expect_summary_rejected \
    readable_duplicate_identity \
    duplicate_attempt_identity \
    16 \
    "${matrix_paths[@]}" \
    "$readable_duplicate"
if ! grep -Eq \
    "^HPA192_CRITICAL_PATH_ATTEMPT status=rejected scenario=A slot=1 attempt=1 reason=attempt_schema artifact_sha256=[0-9a-f]{64}$" \
    "$temp_root/readable_duplicate_identity.stdout"; then
    round1_failure \
        "readable_duplicate_identity lost its readable per-attempt rejection"
fi

round1_expect_summary_rejected \
    incomplete_records_retained \
    incomplete_acceptance_sequence \
    14 \
    "${matrix_paths[@]:0:14}"

if (( ${#round1_failures[@]} > 0 )); then
    printf 'Fix round 1 RED:\n' >&2
    printf '  - %s\n' "${round1_failures[@]}" >&2
    fail "fix round 1 regressions remain"
fi
if [[ "${1:-}" == --fix-round-1 ]]; then
    printf 'critical-path fix round 1 tests passed\n'
    exit 0
fi

# Break caught: lexicographic sorting or selecting anything but sample three.
varied_paths=()
for matrix_index in {1..15}; do
    varied_path="$temp_root/varied-$matrix_index.result.txt"
    cp "${matrix_paths[$((matrix_index - 1))]}" "$varied_path"
    case "$matrix_index" in
        1)
            replace_once "$varied_path" \
                "launch_start_unix_us=1000000 launch_start_monotonic_us=5000000" \
                "launch_start_unix_us=1091000 launch_start_monotonic_us=5091000"
            ;;
        6)
            replace_once "$varied_path" \
                "launch_start_unix_us=1000000 launch_start_monotonic_us=5000000" \
                "launch_start_unix_us=1090000 launch_start_monotonic_us=5090000"
            ;;
        10)
            replace_once "$varied_path" \
                "launch_start_unix_us=1000000 launch_start_monotonic_us=5000000" \
                "launch_start_unix_us=999000 launch_start_monotonic_us=4999000"
            ;;
        15)
            replace_once "$varied_path" \
                "launch_start_unix_us=1000000 launch_start_monotonic_us=5000000" \
                "launch_start_unix_us=998000 launch_start_monotonic_us=4998000"
            ;;
    esac
    varied_paths+=("$varied_path")
done
if ! run_summary "${varied_paths[@]}" >"$temp_root/varied.stdout"; then
    fail "valid varied matrix was rejected"
fi
assert_contains_line \
    "HPA192_CRITICAL_PATH_SUMMARY scenario=A samples=5 metric=external_launch_to_title_backbuffer_ms minimum_ms=269 median_ms=360 maximum_ms=362" \
    "$temp_root/varied.stdout"
assert_contains_line \
    "HPA192_CRITICAL_PATH_SUMMARY scenario=A samples=5 metric=external_launch_to_entry_ms minimum_ms=9 median_ms=100 maximum_ms=102" \
    "$temp_root/varied.stdout"

assert_summary_rejected duplicate_canonical_artifact \
    duplicate_canonical_artifact \
    "${matrix_paths[@]}" \
    "$temp_root/./$(basename "${matrix_paths[0]}")"

duplicate_attempt="$temp_root/duplicate-attempt.result.txt"
cp "${matrix_paths[0]}" "$duplicate_attempt"
assert_summary_rejected duplicate_attempt_identity \
    duplicate_attempt_identity \
    "${matrix_paths[@]}" \
    "$duplicate_attempt"

duplicate_accepted_slot="$temp_root/duplicate-accepted-slot.result.txt"
write_result "$duplicate_accepted_slot" A 1 2
assert_summary_rejected duplicate_accepted_slot \
    duplicate_accepted_slot \
    "${matrix_paths[@]}" \
    "$duplicate_accepted_slot"

mixed_fixed_hash="$(copy_and_replace mixed-fixed-hash "${matrix_paths[5]}" \
    "game_sha256=$(hash_for a) runner_sha256" \
    "game_sha256=$(hash_for 6) runner_sha256")"
mixed_fixed_paths=("${matrix_paths[@]}")
mixed_fixed_paths[5]="$mixed_fixed_hash"
assert_summary_rejected mixed_fixed_hashes mixed_fixed_hashes \
    "${mixed_fixed_paths[@]}"

mixed_config_hash="$temp_root/mixed-config-hash.result.txt"
cp "${matrix_paths[5]}" "$mixed_config_hash"
replace_once "$mixed_config_hash" \
    "config_sha256=$(hash_for f) config_observed_sha256=$(hash_for f)" \
    "config_sha256=$(hash_for 6) config_observed_sha256=$(hash_for 6)"
mixed_config_paths=("${matrix_paths[@]}")
mixed_config_paths[5]="$mixed_config_hash"
assert_summary_rejected mixed_scenario_config_hashes \
    mixed_scenario_config_hashes \
    "${mixed_config_paths[@]}"

mixed_a_c_config="$temp_root/mixed-a-c-config.result.txt"
cp "${matrix_paths[2]}" "$mixed_a_c_config"
replace_once "$mixed_a_c_config" \
    "config_sha256=$(hash_for f) config_observed_sha256=$(hash_for f)" \
    "config_sha256=$(hash_for 6) config_observed_sha256=$(hash_for 6)"
mixed_a_c_config_paths=("${matrix_paths[@]}")
mixed_a_c_config_paths[2]="$mixed_a_c_config"
assert_summary_rejected mixed_a_c_config_hashes \
    mixed_scenario_config_hashes \
    "${mixed_a_c_config_paths[@]}"

all_c_config_paths=("${matrix_paths[@]}")
for c_slot in 3 5 7 11 13; do
    c_config_path="$temp_root/all-c-config-$c_slot.result.txt"
    cp "${matrix_paths[$((c_slot - 1))]}" "$c_config_path"
    replace_once "$c_config_path" \
        "config_sha256=$(hash_for f) config_observed_sha256=$(hash_for f)" \
        "config_sha256=$(hash_for 6) config_observed_sha256=$(hash_for 6)"
    all_c_config_paths[c_slot - 1]="$c_config_path"
done
assert_summary_rejected a_c_config_identity \
    mixed_scenario_config_hashes \
    "${all_c_config_paths[@]}"

mixed_chart_paths="$temp_root/mixed-chart-paths.result.txt"
cp "${matrix_paths[5]}" "$mixed_chart_paths"
replace_once "$mixed_chart_paths" \
    "chart_paths_sha256=$(hash_for 4) expected_chart_paths_sha256=$(hash_for 4)" \
    "chart_paths_sha256=$(hash_for 6) expected_chart_paths_sha256=$(hash_for 6)"
mixed_chart_path_set=("${matrix_paths[@]}")
mixed_chart_path_set[5]="$mixed_chart_paths"
assert_summary_rejected mixed_scenario_chart_paths \
    mixed_scenario_chart_paths \
    "${mixed_chart_path_set[@]}"

all_b_chart_paths=("${matrix_paths[@]}")
for b_slot in 2 4 9 12 14; do
    b_chart_path="$temp_root/all-b-chart-$b_slot.result.txt"
    cp "${matrix_paths[$((b_slot - 1))]}" "$b_chart_path"
    replace_once "$b_chart_path" \
        "chart_paths_sha256=$(hash_for 2) expected_chart_paths_sha256=$(hash_for 2)" \
        "chart_paths_sha256=$(hash_for 5) expected_chart_paths_sha256=$(hash_for 5)"
    all_b_chart_paths[b_slot - 1]="$b_chart_path"
done
assert_summary_rejected empty_chart_path_identity \
    mixed_scenario_chart_paths \
    "${all_b_chart_paths[@]}"

assert_summary_rejected incomplete_acceptance_sequence \
    incomplete_acceptance_sequence \
    "${matrix_paths[@]:0:14}"

# Break caught: including a retained invalid attempt in arithmetic.
invalid_attempt="$temp_root/invalid-attempt.result.txt"
cp "${matrix_paths[0]}" "$invalid_attempt"
replace_once "$invalid_attempt" \
    "config_observed_sha256=$(hash_for f)" \
    "config_observed_sha256=$(hash_for 6)"
replace_once "$invalid_attempt" \
    "launch_start_unix_us=1000000 launch_start_monotonic_us=5000000" \
    "launch_start_unix_us=1099000 launch_start_monotonic_us=5099000"
valid_replacement="$temp_root/valid-replacement.result.txt"
write_result "$valid_replacement" A 1 2
replacement_paths=(
    "$invalid_attempt"
    "$valid_replacement"
    "${matrix_paths[@]:1}"
)
if ! run_summary "${replacement_paths[@]}" >"$temp_root/replacement.stdout"; then
    fail "valid same-scenario replacement matrix was rejected"
fi
grep -Eq \
    "^HPA192_CRITICAL_PATH_ATTEMPT status=rejected scenario=A slot=1 attempt=1 reason=config_hash_mismatch artifact_sha256=[0-9a-f]{64}$" \
    "$temp_root/replacement.stdout" ||
    fail "invalid attempt was not retained with identity, reason, and hash"
assert_contains_line \
    "HPA192_CRITICAL_PATH_SUMMARY scenario=A samples=5 metric=external_launch_to_title_backbuffer_ms minimum_ms=360 median_ms=360 maximum_ms=360" \
    "$temp_root/replacement.stdout"

# Break caught: ignoring fixed-byte drift merely because another check rejects
# the same retained attempt.
invalid_mixed_fixed="$temp_root/invalid-mixed-fixed.result.txt"
cp "$invalid_attempt" "$invalid_mixed_fixed"
replace_once "$invalid_mixed_fixed" \
    "game_sha256=$(hash_for a) runner_sha256" \
    "game_sha256=$(hash_for 6) runner_sha256"
invalid_mixed_fixed_paths=(
    "$invalid_mixed_fixed"
    "$valid_replacement"
    "${matrix_paths[@]:1}"
)
assert_summary_rejected invalid_mixed_fixed_hashes \
    mixed_fixed_hashes \
    "${invalid_mixed_fixed_paths[@]}"

# Break caught: overlapping task-return diagnostics entering the savings budget.
terminal_before_return="$temp_root/terminal-before-return.result.txt"
cp "$good" "$terminal_before_return"
replace_once "$terminal_before_return" \
    "db_task_return_from_entry_ms=61" \
    "db_task_return_from_entry_ms=81"
replace_once "$terminal_before_return" \
    "db_task_returned_terminal=0" \
    "db_task_returned_terminal=1"
run_validate "$terminal_before_return" >"$temp_root/terminal-before-return.stdout"
grep -Fq \
    "db_invoke_to_task_return_ms=21 db_async_after_task_return_ms=0 db_terminal_before_task_return_ms=1" \
    "$temp_root/terminal-before-return.stdout" ||
    fail "terminal-before-return annotation was derived incorrectly"

equal_task_return="$temp_root/equal-task-return.result.txt"
cp "$good" "$equal_task_return"
replace_once "$equal_task_return" \
    "db_task_return_from_entry_ms=61" \
    "db_task_return_from_entry_ms=80"
run_validate "$equal_task_return" >"$temp_root/equal-task-return.stdout"
grep -Fq \
    "db_invoke_to_task_return_ms=20 db_async_after_task_return_ms=0 db_terminal_before_task_return_ms=0" \
    "$temp_root/equal-task-return.stdout" ||
    fail "equal task-return annotations were not both zero"

# Breaks caught: accepting incomplete arguments or unvalidated fixed inputs.
[[ -f "$runner" ]] || fail "critical-path runner is missing"
if bash "$runner" >"$temp_root/runner-missing-args.stdout" \
    2>"$temp_root/runner-missing-args.stderr"; then
    fail "runner accepted missing arguments"
fi

create_runner_fixture missing-binary
rm -f "$fixture_game/DTXMania.Game.Mac.dll"
if run_fixture_runner prepare-seed success \
    >"$fixture_root/stdout" 2>"$fixture_root/stderr"; then
    fail "runner accepted a missing game binary"
fi

create_runner_fixture missing-corpus
if env \
    PATH="$fixture_fakebin:$PATH" \
    bash "$fixture_repo/tools/hpa192/benchmark-critical-path.sh" \
    prepare-seed \
    "$fixture_game" \
    "$fixture_root/missing-corpus" \
    "$fixture_result" \
    >"$fixture_root/stdout" 2>"$fixture_root/stderr"; then
    fail "runner accepted a missing corpus"
fi

create_runner_fixture dirty-output
mkdir -p "$fixture_result/seed"
if run_fixture_runner prepare-seed success \
    >"$fixture_root/stdout" 2>"$fixture_root/stderr"; then
    fail "runner reused a dirty output namespace"
fi
[[ ! -s "$fixture_launch_log" ]] ||
    fail "dirty output validation happened after process launch"

create_runner_fixture manifest-mismatch
printf 'changed\n' >>"$fixture_corpus/assets/asset 001.bin"
if run_fixture_runner prepare-seed success \
    >"$fixture_root/stdout" 2>"$fixture_root/stderr"; then
    fail "runner accepted a corpus manifest mismatch"
fi
[[ ! -s "$fixture_launch_log" ]] ||
    fail "manifest validation happened after process launch"

create_runner_fixture wrong-chart-count 99 27
if run_fixture_runner prepare-seed success \
    >"$fixture_root/stdout" 2>"$fixture_root/stderr"; then
    fail "runner accepted a corpus with 99 supported charts"
fi
[[ ! -s "$fixture_launch_log" ]] ||
    fail "chart-count validation happened after process launch"

create_runner_fixture wrong-set-count 100 26
if run_fixture_runner prepare-seed success \
    >"$fixture_root/stdout" 2>"$fixture_root/stderr"; then
    fail "runner accepted a corpus with 26 SET.def files"
fi
[[ ! -s "$fixture_launch_log" ]] ||
    fail "SET.def validation happened after process launch"

create_runner_fixture nonempty-empty
if ! run_fixture_runner prepare-seed success \
    >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr"; then
    tail -40 "$fixture_root/prepare.stderr" >&2
    fail "valid seed preparation failed"
fi
printf 'not empty\n' >"$fixture_result/empty-songs/unexpected.txt"
reset_runner_fixture
if run_fixture_runner matrix success \
    >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
    fail "runner accepted a nonempty empty-song fixture"
fi
[[ ! -s "$fixture_launch_log" ]] ||
    fail "empty-directory validation happened after process launch"

# Break caught: cloning a seed whose content no longer matches its immutable
# app-data manifest.
create_runner_fixture changed-seed
if ! run_fixture_runner prepare-seed success \
    >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr"; then
    fail "changed-seed fixture preparation failed"
fi
printf 'drift\n' >"$fixture_result/seed/appdata/unexpected.txt"
reset_runner_fixture
if run_fixture_runner matrix success \
    >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
    fail "runner accepted changed seed bytes"
fi
[[ ! -s "$fixture_launch_log" ]] ||
    fail "seed validation happened after process launch"

# Break caught: trusting a config whose exact API value changes after launch.
create_runner_fixture api-enabled
if ! run_fixture_runner prepare-seed success \
    >"$fixture_root/prepare.stdout" 2>"$fixture_root/prepare.stderr"; then
    tail -40 "$fixture_root/prepare.stderr" >&2
    fail "API config fixture seed preparation failed"
fi
reset_runner_fixture
if run_fixture_runner matrix mutate-config \
    >"$fixture_root/matrix.stdout" 2>"$fixture_root/matrix.stderr"; then
    fail "runner accepted API-enabled attempts"
fi
grep -Fq \
    'HPA192_CRITICAL_PATH_DECISION decision=stop reason=diagnostic_harness slot=1 scenario=A' \
    "$fixture_result/decision.txt" ||
    fail "API-enabled attempts did not stop after the bounded retries"
for attempt_number in 1 2 3; do
    grep -Eq \
        "^HPA192_ATTEMPT scenario=A slot=1 attempt=$attempt_number .* game_api_enabled=1 " \
        "$fixture_result/slots/01-A/attempt-$attempt_number/result.txt" ||
        fail "API-enabled attempt $attempt_number lost its fail-closed metadata"
done

run_runner_process_tests
run_runner_layout_tests
run_runner_deadline_tests

printf 'critical-path shell tests passed\n'

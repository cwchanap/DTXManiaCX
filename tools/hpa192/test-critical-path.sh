#!/usr/bin/env bash
set -euo pipefail
export LC_ALL=C

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd -P)"
summarizer="$repo_root/tools/hpa192/summarize-critical-path.sh"
temp_root="$(mktemp -d "${TMPDIR:-/tmp}/hpa-192-critical-path-test.XXXXXX")"

cleanup() {
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

printf 'critical-path shell tests passed\n'

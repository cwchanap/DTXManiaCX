#!/usr/bin/env bash
# shellcheck disable=SC2034,SC2154 # Fixed-schema parser assigns names dynamically.
set -euo pipefail
export LC_ALL=C

script_path="$(
    cd "$(dirname "${BASH_SOURCE[0]}")"
    printf '%s/%s\n' "$(pwd -P)" "$(basename "${BASH_SOURCE[0]}")"
)"

readonly max_utc_microseconds=4102444800000000
readonly max_milliseconds=300000
readonly max_clock_skew_microseconds=50000
readonly max_counter=100000
readonly max_exit_code=255
readonly max_slot=15
readonly max_attempt=3

attempt_field_names=(
    scenario
    slot
    attempt
    launch_start_unix_us
    launch_start_monotonic_us
    observation_unix_us
    observation_monotonic_us
    exit_code
    timed_out
    forced_cleanup
    game_api_enabled
    database_charts
    database_songs
    game_sha256
    runner_sha256
    summarizer_sha256
    corpus_manifest_sha256
    corpus_observed_sha256
    system_manifest_sha256
    config_sha256
    config_observed_sha256
    empty_manifest_sha256
    empty_observed_sha256
    seed_manifest_sha256
    seed_observed_sha256
    chart_paths_sha256
    expected_chart_paths_sha256
)

startup_field_names=(
    path
    outcome
    total_ms
    db_init_ms
    discovery_parse_ms
    persistence_ms
    cleanup_ms
    hierarchy_ms
    discovered
    parsed
    groups
    added
    updated
    preserved
    skipped
    conflicts
    stale
    error
)

timing_field_names=(
    entry_to_config_ms
    config_to_load_content_ms
    load_content_to_startup_ms
    startup_to_first_draw_ms
    startup_to_summary_ms
    summary_to_title_ms
    entry_to_title_ms
    entry_unix_us
    title_unix_us
)

# This literal copy is deliberately independent of the product source. Each
# token is checked at its fixed index before any value is used.
critical_field_names=(
    outcome
    error
    entry_unix_us
    title_backbuffer_unix_us
    entry_to_title_backbuffer_ms
    load_content_complete_from_entry_ms
    startup_construct_begin_from_entry_ms
    startup_construct_end_from_entry_ms
    startup_activate_begin_from_entry_ms
    startup_activation_from_entry_ms
    startup_activate_end_from_entry_ms
    load_content_return_from_entry_ms
    base_initialize_return_from_entry_ms
    input_manager_begin_from_entry_ms
    input_manager_end_from_entry_ms
    saved_bindings_begin_from_entry_ms
    saved_bindings_end_from_entry_ms
    graphics_initialize_begin_from_entry_ms
    graphics_initialize_end_from_entry_ms
    render_target_begin_from_entry_ms
    render_target_end_from_entry_ms
    initialize_complete_from_entry_ms
    post_load_unattributed_ms
    startup_first_update_begin_from_entry_ms
    startup_first_update_end_from_entry_ms
    startup_first_draw_begin_from_entry_ms
    startup_first_draw_end_from_entry_ms
    startup_updates_before_first_draw
    startup_game_time_before_first_draw_ms
    startup_draws_before_transition
    db_invoke_from_entry_ms
    db_task_return_from_entry_ms
    db_terminal_from_entry_ms
    db_observed_from_entry_ms
    db_task_returned_terminal
    enumeration_invoke_from_entry_ms
    enumeration_task_return_from_entry_ms
    enumeration_terminal_from_entry_ms
    enumeration_observed_from_entry_ms
    enumeration_task_returned_terminal
    enumeration_unattributed_ms
    db_service_setup_ms
    db_corruption_probe_ms
    db_invalid_recovery_count
    db_invalid_recovery_ms
    db_ensure_created_count
    db_ensure_created_ms
    db_encoding_pragmas_ms
    db_version_work_ms
    db_schema_ensures_ms
    db_init_unattributed_ms
    summary_request_from_entry_ms
    title_construct_begin_from_entry_ms
    title_construct_end_from_entry_ms
    transition_start_from_entry_ms
    transition_complete_from_entry_ms
    transition_update_count
    transition_game_time_ms
    startup_deactivate_begin_from_entry_ms
    startup_deactivate_end_from_entry_ms
    title_activate_begin_from_entry_ms
    title_activate_end_from_entry_ms
    title_first_update_begin_from_entry_ms
    title_first_update_end_from_entry_ms
    title_stage_draw_begin_from_entry_ms
    title_stage_draw_end_from_entry_ms
    title_backbuffer_blit_begin_from_entry_ms
    title_backbuffer_blit_end_from_entry_ms
    summary_to_title_unattributed_ms
    title_gpu_setup_ms
    title_background_ms
    title_menu_ms
    title_font_ms
    title_cursor_sound_ms
    title_decide_sound_ms
    title_game_start_sound_ms
    title_game_start_fallback_ran
    title_game_start_fallback_ms
    title_sound_load_count
    title_activation_unattributed_ms
    title_backbuffer_published
)

accepted_output_field_names=(
    status
    scenario
    slot
    attempt
    artifact_sha256
    external_launch_to_entry_ms
    external_launch_to_title_backbuffer_ms
    stdout_observation_lag_ms
    entry_to_load_content_complete_ms
    load_content_complete_to_initialize_complete_ms
    initialize_complete_to_summary_request_ms
    summary_request_to_title_backbuffer_ms
    initialize_complete_to_db_invoke_ms
    db_operation_ms
    db_terminal_to_observed_ms
    db_observed_to_enumeration_invoke_ms
    enumeration_operation_ms
    enumeration_terminal_to_observed_ms
    enumeration_observed_to_summary_request_ms
    db_invoke_to_task_return_ms
    db_async_after_task_return_ms
    db_terminal_before_task_return_ms
    enumeration_invoke_to_task_return_ms
    enumeration_async_after_task_return_ms
    enumeration_terminal_before_task_return_ms
)

summary_metric_names=(
    external_launch_to_title_backbuffer_ms
    external_launch_to_entry_ms
    entry_to_load_content_complete_ms
    load_content_complete_to_initialize_complete_ms
    initialize_complete_to_summary_request_ms
    summary_request_to_title_backbuffer_ms
    initialize_complete_to_db_invoke_ms
    db_operation_ms
    db_terminal_to_observed_ms
    db_observed_to_enumeration_invoke_ms
    enumeration_operation_ms
    enumeration_terminal_to_observed_ms
    enumeration_observed_to_summary_request_ms
)

expected_scenarios=(
    A B C B C A C A B A C B C B A
)

scenario=unknown
slot=unknown
attempt=unknown
artifact_sha256=unknown
summary_attempt_outputs=()
summary_attempt_outputs_emitted=0
summary_set_failure_reason=

reject() {
    local reason="$1"
    printf 'HPA192_CRITICAL_PATH_ATTEMPT status=rejected scenario=%s slot=%s attempt=%s reason=%s artifact_sha256=%s\n' \
        "$scenario" "$slot" "$attempt" "$reason" "$artifact_sha256"
    exit 1
}

decimal_at_most() {
    local value="$1"
    local maximum="$2"

    [[ "$value" =~ ^(0|[1-9][0-9]*)$ ]] || return 1
    if [[ "${#value}" -lt "${#maximum}" ]]; then
        return 0
    fi
    if [[ "${#value}" -gt "${#maximum}" ]]; then
        return 1
    fi
    [[ "$value" < "$maximum" || "$value" == "$maximum" ]]
}

safe_token() {
    [[ "$1" =~ ^[A-Za-z0-9._-]+$ ]]
}

sha256_token() {
    [[ "$1" =~ ^[0-9a-f]{64}$ ]]
}

line_count() {
    local path="$1"
    local prefix="$2"
    awk -v prefix="$prefix " '
        index($0, prefix) == 1 { count++ }
        END { print count + 0 }
    ' "$path"
}

only_line() {
    local path="$1"
    local prefix="$2"
    awk -v prefix="$prefix " '
        index($0, prefix) == 1 { line = $0 }
        END { print line }
    ' "$path"
}

parse_ordered_line() {
    local line="$1"
    local prefix="$2"
    local namespace="$3"
    local names_variable="$4"
    local -a tokens=()
    local -a names=()
    local index
    local key
    local token
    local value

    local -n names_ref="$names_variable"
    names=("${names_ref[@]}")
    IFS=' ' read -r -a tokens <<<"$line"
    [[ "${tokens[0]:-}" == "$prefix" ]] || return 1
    [[ "${#tokens[@]}" -eq "$((${#names[@]} + 1))" ]] || return 1

    for ((index = 0; index < ${#names[@]}; index++)); do
        key="${names[$index]}"
        token="${tokens[$((index + 1))]}"
        [[ "$token" == "$key="* ]] || return 1
        value="${token#"$key="}"
        [[ -n "$value" && "$value" != *"="* ]] || return 1
        printf -v "${namespace}_${key}" '%s' "$value"
    done
}

read_expected_field() {
    local line="$1"
    local prefix="$2"
    local names_variable="$3"
    local expected_name="$4"
    local output_variable="$5"
    local -a tokens=()
    local -a names=()
    local index
    local token
    local value

    local -n names_ref="$names_variable"
    names=("${names_ref[@]}")
    IFS=' ' read -r -a tokens <<<"$line"
    [[ "${tokens[0]:-}" == "$prefix" ]] || return 1

    for ((index = 0; index < ${#names[@]}; index++)); do
        [[ "${names[$index]}" == "$expected_name" ]] || continue
        token="${tokens[$((index + 1))]:-}"
        [[ "$token" == "$expected_name="* ]] || return 1
        value="${token#"$expected_name="}"
        [[ -n "$value" && "$value" != *"="* ]] || return 1
        printf -v "$output_variable" '%s' "$value"
        return 0
    done
    return 1
}

assign_readable_attempt_identity() {
    local line="$1"
    local readable_scenario
    local readable_slot
    local readable_attempt

    if read_expected_field \
        "$line" HPA192_ATTEMPT attempt_field_names scenario readable_scenario &&
       safe_token "$readable_scenario"; then
        scenario="$readable_scenario"
    fi
    if read_expected_field \
        "$line" HPA192_ATTEMPT attempt_field_names slot readable_slot &&
       decimal_at_most "$readable_slot" "$max_slot" &&
       [[ "$readable_slot" -ge 1 ]]; then
        slot="$readable_slot"
    fi
    if read_expected_field \
        "$line" HPA192_ATTEMPT attempt_field_names attempt readable_attempt &&
       decimal_at_most "$readable_attempt" "$max_attempt" &&
       [[ "$readable_attempt" -ge 1 ]]; then
        attempt="$readable_attempt"
    fi
}

validate_attempt_schema() {
    local path="$1"
    local line
    local key

    [[ "$(line_count "$path" HPA192_ATTEMPT)" == 1 ]] ||
        reject attempt_line_count
    line="$(only_line "$path" HPA192_ATTEMPT)"
    assign_readable_attempt_identity "$line"
    parse_ordered_line "$line" HPA192_ATTEMPT attempt attempt_field_names ||
        reject attempt_schema

    safe_token "$attempt_scenario" || reject attempt_scenario
    scenario="$attempt_scenario"
    decimal_at_most "$attempt_slot" "$max_slot" ||
        reject attempt_number
    [[ "$attempt_slot" -ge 1 ]] || reject attempt_number
    slot="$attempt_slot"
    decimal_at_most "$attempt_attempt" "$max_attempt" ||
        reject attempt_number
    [[ "$attempt_attempt" -ge 1 ]] || reject attempt_number
    attempt="$attempt_attempt"

    decimal_at_most "$attempt_launch_start_unix_us" "$max_utc_microseconds" ||
        reject attempt_number
    decimal_at_most "$attempt_launch_start_monotonic_us" "$max_utc_microseconds" ||
        reject attempt_number
    decimal_at_most "$attempt_observation_unix_us" "$max_utc_microseconds" ||
        reject attempt_number
    decimal_at_most "$attempt_observation_monotonic_us" "$max_utc_microseconds" ||
        reject attempt_number
    decimal_at_most "$attempt_exit_code" "$max_exit_code" ||
        reject attempt_number
    decimal_at_most "$attempt_database_charts" "$max_counter" ||
        reject attempt_number
    decimal_at_most "$attempt_database_songs" "$max_counter" ||
        reject attempt_number

    for key in timed_out forced_cleanup game_api_enabled; do
        eval "value=\$attempt_${key}"
        [[ "$value" == 0 || "$value" == 1 ]] || reject attempt_flag
    done

    for key in \
        game_sha256 \
        runner_sha256 \
        summarizer_sha256 \
        corpus_manifest_sha256 \
        corpus_observed_sha256 \
        system_manifest_sha256 \
        config_sha256 \
        config_observed_sha256 \
        empty_manifest_sha256 \
        empty_observed_sha256 \
        seed_manifest_sha256 \
        seed_observed_sha256 \
        chart_paths_sha256 \
        expected_chart_paths_sha256
    do
        eval "value=\$attempt_${key}"
        sha256_token "$value" || reject attempt_hash
    done
}

validate_startup_line() {
    local path="$1"
    local line
    local key

    [[ "$(line_count "$path" HPA192_STARTUP)" == 1 ]] ||
        reject startup_line_count
    line="$(only_line "$path" HPA192_STARTUP)"
    parse_ordered_line "$line" HPA192_STARTUP startup startup_field_names ||
        reject startup_schema
    [[ "$startup_path" == enumeration ]] || reject startup_path
    [[ "$startup_outcome" == success ]] || reject startup_outcome
    [[ "$startup_error" == none ]] || reject startup_error

    for key in \
        total_ms db_init_ms discovery_parse_ms persistence_ms cleanup_ms hierarchy_ms
    do
        eval "value=\$startup_${key}"
        decimal_at_most "$value" "$max_milliseconds" ||
            reject startup_number
    done
    for key in \
        discovered parsed groups added updated preserved skipped conflicts stale
    do
        eval "value=\$startup_${key}"
        decimal_at_most "$value" "$max_counter" ||
            reject startup_number
    done
}

validate_timing_line() {
    local path="$1"
    local line
    local key

    [[ "$(line_count "$path" HPA192_TIMING)" == 1 ]] ||
        reject timing_line_count
    line="$(only_line "$path" HPA192_TIMING)"
    parse_ordered_line "$line" HPA192_TIMING timing timing_field_names ||
        reject timing_schema

    for key in \
        entry_to_config_ms \
        config_to_load_content_ms \
        load_content_to_startup_ms \
        startup_to_first_draw_ms \
        startup_to_summary_ms \
        summary_to_title_ms \
        entry_to_title_ms
    do
        eval "value=\$timing_${key}"
        decimal_at_most "$value" "$max_milliseconds" ||
            reject timing_number
    done
    for key in entry_unix_us title_unix_us; do
        eval "value=\$timing_${key}"
        decimal_at_most "$value" "$max_utc_microseconds" ||
            reject timing_number
    done
}

is_critical_flag() {
    case "$1" in
        db_task_returned_terminal|enumeration_task_returned_terminal|\
title_game_start_fallback_ran|title_backbuffer_published)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

is_critical_counter() {
    case "$1" in
        startup_updates_before_first_draw|startup_draws_before_transition|\
transition_update_count|db_invalid_recovery_count|db_ensure_created_count|\
title_sound_load_count)
            return 0
            ;;
        *)
            return 1
            ;;
    esac
}

validate_critical_line() {
    local path="$1"
    local line
    local key
    local value
    local index
    local success_count
    local failure_count

    success_count="$(line_count "$path" HPA192_CRITICAL_PATH)"
    failure_count="$(line_count "$path" HPA192_CRITICAL_PATH_FAILURE)"
    if [[ "$success_count" != 0 && "$failure_count" != 0 ]]; then
        reject conflicting_terminal_lines
    fi
    [[ "$failure_count" == 0 ]] || reject failure_terminal
    [[ "$success_count" == 1 ]] || reject critical_line_count

    line="$(only_line "$path" HPA192_CRITICAL_PATH)"
    parse_ordered_line \
        "$line" \
        HPA192_CRITICAL_PATH \
        critical \
        critical_field_names ||
        reject critical_schema
    [[ "$critical_outcome" == success ]] || reject critical_outcome
    [[ "$critical_error" == none ]] || reject critical_error

    for ((index = 2; index < ${#critical_field_names[@]}; index++)); do
        key="${critical_field_names[$index]}"
        eval "value=\$critical_${key}"
        if [[ "$key" == entry_unix_us ||
              "$key" == title_backbuffer_unix_us ]]; then
            decimal_at_most "$value" "$max_utc_microseconds" ||
                reject critical_number
        elif [[ "$key" == db_invalid_recovery_count ]]; then
            decimal_at_most "$value" 2 ||
                reject critical_number
        elif [[ "$key" == db_ensure_created_count ]]; then
            decimal_at_most "$value" 2 ||
                reject critical_number
            [[ "$value" -ge 1 ]] || reject critical_number
        elif is_critical_flag "$key"; then
            [[ "$value" == 0 || "$value" == 1 ]] ||
                reject critical_flag
        elif is_critical_counter "$key"; then
            decimal_at_most "$value" "$max_counter" ||
                reject critical_number
        else
            decimal_at_most "$value" "$max_milliseconds" ||
                reject critical_number
        fi
    done
}

require_non_decreasing() {
    local reason="$1"
    shift
    local previous=
    local key
    local value

    for key in "$@"; do
        eval "value=\$critical_${key}"
        if [[ -n "$previous" && "$previous" -gt "$value" ]]; then
            reject "$reason"
        fi
        previous="$value"
    done
}

absolute_difference() {
    local left="$1"
    local right="$2"
    local difference=$((left - right))
    if [[ "$difference" -lt 0 ]]; then
        difference=$((-difference))
    fi
    printf '%s\n' "$difference"
}

validate_temporal_contract() {
    local expected
    local enumeration_parent
    local enumeration_children_and_residual
    local difference

    [[ "$critical_startup_activation_from_entry_ms" -ge "$critical_startup_activate_begin_from_entry_ms" &&
       "$critical_startup_activation_from_entry_ms" -le "$critical_startup_activate_end_from_entry_ms" ]] ||
        reject activation_span

    require_non_decreasing milestone_order \
        load_content_complete_from_entry_ms \
        startup_construct_begin_from_entry_ms \
        startup_construct_end_from_entry_ms \
        startup_activate_begin_from_entry_ms \
        startup_activation_from_entry_ms \
        startup_activate_end_from_entry_ms \
        load_content_return_from_entry_ms \
        base_initialize_return_from_entry_ms \
        input_manager_begin_from_entry_ms \
        input_manager_end_from_entry_ms \
        saved_bindings_begin_from_entry_ms \
        saved_bindings_end_from_entry_ms \
        graphics_initialize_begin_from_entry_ms \
        graphics_initialize_end_from_entry_ms \
        render_target_begin_from_entry_ms \
        render_target_end_from_entry_ms \
        initialize_complete_from_entry_ms \
        startup_first_update_begin_from_entry_ms \
        startup_first_update_end_from_entry_ms \
        startup_first_draw_begin_from_entry_ms \
        startup_first_draw_end_from_entry_ms
    require_non_decreasing milestone_order \
        initialize_complete_from_entry_ms \
        db_invoke_from_entry_ms \
        db_terminal_from_entry_ms \
        db_observed_from_entry_ms \
        enumeration_invoke_from_entry_ms \
        enumeration_terminal_from_entry_ms \
        enumeration_observed_from_entry_ms \
        summary_request_from_entry_ms
    require_non_decreasing milestone_order \
        db_invoke_from_entry_ms \
        db_task_return_from_entry_ms \
        db_observed_from_entry_ms
    require_non_decreasing milestone_order \
        enumeration_invoke_from_entry_ms \
        enumeration_task_return_from_entry_ms \
        enumeration_observed_from_entry_ms
    require_non_decreasing milestone_order \
        summary_request_from_entry_ms \
        title_construct_begin_from_entry_ms \
        title_construct_end_from_entry_ms \
        transition_start_from_entry_ms \
        transition_complete_from_entry_ms \
        startup_deactivate_begin_from_entry_ms \
        startup_deactivate_end_from_entry_ms \
        title_activate_begin_from_entry_ms \
        title_activate_end_from_entry_ms \
        title_first_update_begin_from_entry_ms \
        title_first_update_end_from_entry_ms \
        title_stage_draw_begin_from_entry_ms \
        title_stage_draw_end_from_entry_ms \
        title_backbuffer_blit_begin_from_entry_ms \
        title_backbuffer_blit_end_from_entry_ms

    if [[ "$critical_db_task_returned_terminal" == 0 ]]; then
        [[ "$critical_db_task_return_from_entry_ms" -le "$critical_db_terminal_from_entry_ms" ]] ||
            reject task_return_order
    else
        [[ "$critical_db_terminal_from_entry_ms" -le "$critical_db_task_return_from_entry_ms" ]] ||
            reject task_return_order
    fi
    if [[ "$critical_enumeration_task_returned_terminal" == 0 ]]; then
        [[ "$critical_enumeration_task_return_from_entry_ms" -le "$critical_enumeration_terminal_from_entry_ms" ]] ||
            reject task_return_order
    else
        [[ "$critical_enumeration_terminal_from_entry_ms" -le "$critical_enumeration_task_return_from_entry_ms" ]] ||
            reject task_return_order
    fi

    [[ "$critical_entry_to_title_backbuffer_ms" == "$critical_title_backbuffer_blit_end_from_entry_ms" ]] ||
        reject endpoint_mismatch

    expected=$((
        critical_initialize_complete_from_entry_ms -
        critical_load_content_complete_from_entry_ms -
        (critical_startup_construct_end_from_entry_ms -
         critical_startup_construct_begin_from_entry_ms) -
        (critical_startup_activate_end_from_entry_ms -
         critical_startup_activate_begin_from_entry_ms) -
        (critical_base_initialize_return_from_entry_ms -
         critical_load_content_return_from_entry_ms) -
        (critical_input_manager_end_from_entry_ms -
         critical_input_manager_begin_from_entry_ms) -
        (critical_saved_bindings_end_from_entry_ms -
         critical_saved_bindings_begin_from_entry_ms) -
        (critical_graphics_initialize_end_from_entry_ms -
         critical_graphics_initialize_begin_from_entry_ms) -
        (critical_render_target_end_from_entry_ms -
         critical_render_target_begin_from_entry_ms)
    ))
    [[ "$expected" -ge 0 &&
       "$critical_post_load_unattributed_ms" -eq "$expected" ]] ||
        reject post_load_residual

    expected=$((
        critical_db_terminal_from_entry_ms -
        critical_db_invoke_from_entry_ms -
        critical_db_service_setup_ms -
        critical_db_corruption_probe_ms -
        critical_db_invalid_recovery_ms -
        critical_db_ensure_created_ms -
        critical_db_encoding_pragmas_ms -
        critical_db_version_work_ms -
        critical_db_schema_ensures_ms
    ))
    [[ "$expected" -ge 0 &&
       "$critical_db_init_unattributed_ms" -eq "$expected" ]] ||
        reject database_residual

    enumeration_parent=$((
        critical_enumeration_terminal_from_entry_ms -
        critical_enumeration_invoke_from_entry_ms
    ))
    enumeration_children_and_residual=$((
        startup_discovery_parse_ms +
        startup_persistence_ms +
        startup_cleanup_ms +
        startup_hierarchy_ms +
        critical_enumeration_unattributed_ms
    ))
    difference="$(
        absolute_difference \
            "$enumeration_parent" \
            "$enumeration_children_and_residual"
    )"
    [[ "$enumeration_parent" -ge 0 &&
       "$critical_enumeration_unattributed_ms" -ge 0 &&
       "$difference" -le 4 ]] ||
        reject enumeration_residual

    expected=$((
        critical_title_activate_end_from_entry_ms -
        critical_title_activate_begin_from_entry_ms -
        critical_title_gpu_setup_ms -
        critical_title_background_ms -
        critical_title_menu_ms -
        critical_title_font_ms -
        critical_title_cursor_sound_ms -
        critical_title_decide_sound_ms -
        critical_title_game_start_sound_ms -
        critical_title_game_start_fallback_ms
    ))
    [[ "$expected" -ge 0 &&
       "$critical_title_activation_unattributed_ms" -eq "$expected" ]] ||
        reject title_activation_residual

    expected=$((
        critical_title_backbuffer_blit_end_from_entry_ms -
        critical_summary_request_from_entry_ms -
        (critical_title_construct_end_from_entry_ms -
         critical_title_construct_begin_from_entry_ms) -
        (critical_transition_complete_from_entry_ms -
         critical_transition_start_from_entry_ms) -
        (critical_startup_deactivate_end_from_entry_ms -
         critical_startup_deactivate_begin_from_entry_ms) -
        (critical_title_activate_end_from_entry_ms -
         critical_title_activate_begin_from_entry_ms) -
        (critical_title_first_update_end_from_entry_ms -
         critical_title_first_update_begin_from_entry_ms) -
        (critical_title_stage_draw_end_from_entry_ms -
         critical_title_stage_draw_begin_from_entry_ms) -
        (critical_title_backbuffer_blit_end_from_entry_ms -
         critical_title_backbuffer_blit_begin_from_entry_ms)
    ))
    [[ "$expected" -ge 0 &&
       "$critical_summary_to_title_unattributed_ms" -eq "$expected" ]] ||
        reject summary_to_title_residual

    [[ "$critical_title_sound_load_count" -eq $((3 + critical_title_game_start_fallback_ran)) ]] ||
        reject title_sound_count
    if [[ "$critical_title_game_start_fallback_ran" == 0 ]]; then
        [[ "$critical_title_game_start_fallback_ms" == 0 ]] ||
            reject title_fallback
    fi
    [[ "$critical_startup_draws_before_transition" -gt 0 ]] ||
        reject startup_draw_count
    [[ "$critical_title_backbuffer_published" == 1 ]] ||
        reject title_not_published
    [[ "$critical_db_invalid_recovery_count" == 0 ]] ||
        reject database_recovery
    [[ "$critical_db_ensure_created_count" == 1 ]] ||
        reject ensure_created_count
}

validate_cross_line_and_clocks() {
    local expected
    local difference
    local critical_process_wall_us
    local critical_process_wall_ms
    local timing_process_wall_us
    local timing_process_wall_ms
    local external_wall_us
    local external_wall_ms
    local external_monotonic_us
    local external_monotonic_ms
    local external_launch_to_entry_ms
    local external_launch_to_title_ms
    local accepted_launch_to_title_ms
    local observation_lag_ms

    [[ "$critical_entry_unix_us" == "$timing_entry_unix_us" ]] ||
        reject entry_anchor_mismatch
    [[ "$critical_entry_unix_us" -ge "$attempt_launch_start_unix_us" ]] ||
        reject external_anchor_order
    [[ "$critical_title_backbuffer_unix_us" -ge "$critical_entry_unix_us" &&
       "$timing_title_unix_us" -ge "$timing_entry_unix_us" ]] ||
        reject process_anchor_order
    [[ "$attempt_observation_unix_us" -ge "$critical_title_backbuffer_unix_us" ]] ||
        reject observation_anchor_order
    [[ "$attempt_observation_monotonic_us" -ge "$attempt_launch_start_monotonic_us" ]] ||
        reject external_anchor_order

    expected=$((timing_entry_to_config_ms + timing_config_to_load_content_ms))
    difference="$(
        absolute_difference \
            "$critical_load_content_complete_from_entry_ms" \
            "$expected"
    )"
    [[ "$difference" -le 1 ]] || reject load_content_rounding

    expected=$((
        timing_entry_to_config_ms +
        timing_config_to_load_content_ms +
        timing_load_content_to_startup_ms
    ))
    difference="$(
        absolute_difference \
            "$critical_startup_activation_from_entry_ms" \
            "$expected"
    )"
    [[ "$difference" -le 2 ]] || reject startup_activation_rounding

    expected=$((
        critical_db_terminal_from_entry_ms -
        critical_db_invoke_from_entry_ms
    ))
    difference="$(absolute_difference "$startup_db_init_ms" "$expected")"
    [[ "$difference" -le 1 ]] || reject database_rounding

    critical_process_wall_us=$((
        critical_title_backbuffer_unix_us - critical_entry_unix_us
    ))
    difference="$(
        absolute_difference \
            "$critical_process_wall_us" \
            "$((critical_entry_to_title_backbuffer_ms * 1000))"
    )"
    [[ "$difference" -le "$max_clock_skew_microseconds" ]] ||
        reject process_clock_alignment
    critical_process_wall_ms=$((critical_process_wall_us / 1000))

    timing_process_wall_us=$((timing_title_unix_us - timing_entry_unix_us))
    difference="$(
        absolute_difference \
            "$timing_process_wall_us" \
            "$((timing_entry_to_title_ms * 1000))"
    )"
    [[ "$difference" -le "$max_clock_skew_microseconds" ]] ||
        reject process_clock_alignment
    timing_process_wall_ms=$((timing_process_wall_us / 1000))

    external_wall_us=$((
        attempt_observation_unix_us - attempt_launch_start_unix_us
    ))
    external_monotonic_us=$((
        attempt_observation_monotonic_us -
        attempt_launch_start_monotonic_us
    ))
    difference="$(
        absolute_difference "$external_wall_us" "$external_monotonic_us"
    )"
    [[ "$difference" -le "$max_clock_skew_microseconds" ]] ||
        reject external_clock_alignment
    external_wall_ms=$((external_wall_us / 1000))
    external_monotonic_ms=$((external_monotonic_us / 1000))

    external_launch_to_entry_ms=$((
        (critical_entry_unix_us - attempt_launch_start_unix_us) / 1000
    ))
    external_launch_to_title_ms=$((
        (critical_title_backbuffer_unix_us -
         attempt_launch_start_unix_us) / 1000
    ))
    accepted_launch_to_title_ms=$((
        external_launch_to_entry_ms +
        critical_entry_to_title_backbuffer_ms
    ))
    observation_lag_ms=$((
        (attempt_observation_unix_us -
         critical_title_backbuffer_unix_us) / 1000
    ))
    [[ "$critical_process_wall_ms" -le "$max_milliseconds" &&
       "$timing_process_wall_ms" -le "$max_milliseconds" &&
       "$external_wall_ms" -le "$max_milliseconds" &&
       "$external_monotonic_ms" -le "$max_milliseconds" &&
       "$external_launch_to_entry_ms" -le "$max_milliseconds" &&
       "$external_launch_to_title_ms" -le "$max_milliseconds" &&
       "$accepted_launch_to_title_ms" -le "$max_milliseconds" &&
       "$observation_lag_ms" -le "$max_milliseconds" ]] ||
        reject elapsed_out_of_range
}

validate_attempt_identity_and_outcome() {
    local expected_parsed
    local expected_groups
    local expected_database_charts
    local expected_database_songs
    local expected_scenario

    case "$scenario" in
        A|C)
            expected_parsed=100
            expected_groups=27
            expected_database_charts=100
            expected_database_songs=27
            ;;
        B)
            expected_parsed=0
            expected_groups=0
            expected_database_charts=0
            expected_database_songs=0
            ;;
        *)
            reject unplanned_scenario
            ;;
    esac

    expected_scenario="${expected_scenarios[$((slot - 1))]}"
    [[ "$scenario" == "$expected_scenario" ]] || reject scenario_slot
    [[ "$startup_discovered" == "$expected_parsed" &&
       "$startup_parsed" == "$expected_parsed" &&
       "$startup_groups" == "$expected_groups" &&
       "$attempt_database_charts" == "$expected_database_charts" &&
       "$attempt_database_songs" == "$expected_database_songs" ]] ||
        reject scenario_counts

    [[ "$attempt_exit_code" == 0 ]] || reject nonzero_exit
    [[ "$attempt_timed_out" == 0 ]] || reject timed_out
    [[ "$attempt_forced_cleanup" == 0 ]] || reject forced_cleanup
    [[ "$attempt_game_api_enabled" == 0 ]] || reject game_api_enabled

    [[ "$attempt_corpus_manifest_sha256" == "$attempt_corpus_observed_sha256" ]] ||
        reject corpus_hash_mismatch
    [[ "$attempt_config_sha256" == "$attempt_config_observed_sha256" ]] ||
        reject config_hash_mismatch
    [[ "$attempt_empty_manifest_sha256" == "$attempt_empty_observed_sha256" ]] ||
        reject empty_hash_mismatch
    [[ "$attempt_seed_manifest_sha256" == "$attempt_seed_observed_sha256" ]] ||
        reject seed_hash_mismatch
    [[ "$attempt_chart_paths_sha256" == "$attempt_expected_chart_paths_sha256" ]] ||
        reject chart_paths_hash_mismatch
}

emit_accepted_attempt() {
    local external_launch_to_entry_ms
    local external_launch_to_title_backbuffer_ms
    local stdout_observation_lag_ms
    local entry_to_load_content_complete_ms
    local load_content_complete_to_initialize_complete_ms
    local initialize_complete_to_summary_request_ms
    local summary_request_to_title_backbuffer_ms
    local initialize_complete_to_db_invoke_ms
    local db_operation_ms
    local db_terminal_to_observed_ms
    local db_observed_to_enumeration_invoke_ms
    local enumeration_operation_ms
    local enumeration_terminal_to_observed_ms
    local enumeration_observed_to_summary_request_ms
    local db_invoke_to_task_return_ms
    local db_async_after_task_return_ms=0
    local db_terminal_before_task_return_ms=0
    local enumeration_invoke_to_task_return_ms
    local enumeration_async_after_task_return_ms=0
    local enumeration_terminal_before_task_return_ms=0

    external_launch_to_entry_ms=$((
        (critical_entry_unix_us - attempt_launch_start_unix_us) / 1000
    ))
    external_launch_to_title_backbuffer_ms=$((
        external_launch_to_entry_ms +
        critical_entry_to_title_backbuffer_ms
    ))
    stdout_observation_lag_ms=$((
        (attempt_observation_unix_us - critical_title_backbuffer_unix_us) / 1000
    ))
    entry_to_load_content_complete_ms="$critical_load_content_complete_from_entry_ms"
    load_content_complete_to_initialize_complete_ms=$((
        critical_initialize_complete_from_entry_ms -
        critical_load_content_complete_from_entry_ms
    ))
    initialize_complete_to_summary_request_ms=$((
        critical_summary_request_from_entry_ms -
        critical_initialize_complete_from_entry_ms
    ))
    summary_request_to_title_backbuffer_ms=$((
        critical_title_backbuffer_blit_end_from_entry_ms -
        critical_summary_request_from_entry_ms
    ))
    initialize_complete_to_db_invoke_ms=$((
        critical_db_invoke_from_entry_ms -
        critical_initialize_complete_from_entry_ms
    ))
    db_operation_ms=$((
        critical_db_terminal_from_entry_ms -
        critical_db_invoke_from_entry_ms
    ))
    db_terminal_to_observed_ms=$((
        critical_db_observed_from_entry_ms -
        critical_db_terminal_from_entry_ms
    ))
    db_observed_to_enumeration_invoke_ms=$((
        critical_enumeration_invoke_from_entry_ms -
        critical_db_observed_from_entry_ms
    ))
    enumeration_operation_ms=$((
        critical_enumeration_terminal_from_entry_ms -
        critical_enumeration_invoke_from_entry_ms
    ))
    enumeration_terminal_to_observed_ms=$((
        critical_enumeration_observed_from_entry_ms -
        critical_enumeration_terminal_from_entry_ms
    ))
    enumeration_observed_to_summary_request_ms=$((
        critical_summary_request_from_entry_ms -
        critical_enumeration_observed_from_entry_ms
    ))
    db_invoke_to_task_return_ms=$((
        critical_db_task_return_from_entry_ms -
        critical_db_invoke_from_entry_ms
    ))
    if [[ "$critical_db_task_return_from_entry_ms" -lt "$critical_db_terminal_from_entry_ms" ]]; then
        db_async_after_task_return_ms=$((
            critical_db_terminal_from_entry_ms -
            critical_db_task_return_from_entry_ms
        ))
    elif [[ "$critical_db_terminal_from_entry_ms" -lt "$critical_db_task_return_from_entry_ms" ]]; then
        db_terminal_before_task_return_ms=$((
            critical_db_task_return_from_entry_ms -
            critical_db_terminal_from_entry_ms
        ))
    fi
    enumeration_invoke_to_task_return_ms=$((
        critical_enumeration_task_return_from_entry_ms -
        critical_enumeration_invoke_from_entry_ms
    ))
    if [[ "$critical_enumeration_task_return_from_entry_ms" -lt "$critical_enumeration_terminal_from_entry_ms" ]]; then
        enumeration_async_after_task_return_ms=$((
            critical_enumeration_terminal_from_entry_ms -
            critical_enumeration_task_return_from_entry_ms
        ))
    elif [[ "$critical_enumeration_terminal_from_entry_ms" -lt "$critical_enumeration_task_return_from_entry_ms" ]]; then
        enumeration_terminal_before_task_return_ms=$((
            critical_enumeration_task_return_from_entry_ms -
            critical_enumeration_terminal_from_entry_ms
        ))
    fi

    printf 'HPA192_CRITICAL_PATH_ATTEMPT status=accepted scenario=%s slot=%s attempt=%s artifact_sha256=%s' \
        "$scenario" "$slot" "$attempt" "$artifact_sha256"
    printf ' external_launch_to_entry_ms=%s' "$external_launch_to_entry_ms"
    printf ' external_launch_to_title_backbuffer_ms=%s' \
        "$external_launch_to_title_backbuffer_ms"
    printf ' stdout_observation_lag_ms=%s' "$stdout_observation_lag_ms"
    printf ' entry_to_load_content_complete_ms=%s' \
        "$entry_to_load_content_complete_ms"
    printf ' load_content_complete_to_initialize_complete_ms=%s' \
        "$load_content_complete_to_initialize_complete_ms"
    printf ' initialize_complete_to_summary_request_ms=%s' \
        "$initialize_complete_to_summary_request_ms"
    printf ' summary_request_to_title_backbuffer_ms=%s' \
        "$summary_request_to_title_backbuffer_ms"
    printf ' initialize_complete_to_db_invoke_ms=%s' \
        "$initialize_complete_to_db_invoke_ms"
    printf ' db_operation_ms=%s' "$db_operation_ms"
    printf ' db_terminal_to_observed_ms=%s' "$db_terminal_to_observed_ms"
    printf ' db_observed_to_enumeration_invoke_ms=%s' \
        "$db_observed_to_enumeration_invoke_ms"
    printf ' enumeration_operation_ms=%s' "$enumeration_operation_ms"
    printf ' enumeration_terminal_to_observed_ms=%s' \
        "$enumeration_terminal_to_observed_ms"
    printf ' enumeration_observed_to_summary_request_ms=%s' \
        "$enumeration_observed_to_summary_request_ms"
    printf ' db_invoke_to_task_return_ms=%s' "$db_invoke_to_task_return_ms"
    printf ' db_async_after_task_return_ms=%s' "$db_async_after_task_return_ms"
    printf ' db_terminal_before_task_return_ms=%s' \
        "$db_terminal_before_task_return_ms"
    printf ' enumeration_invoke_to_task_return_ms=%s' \
        "$enumeration_invoke_to_task_return_ms"
    printf ' enumeration_async_after_task_return_ms=%s' \
        "$enumeration_async_after_task_return_ms"
    printf ' enumeration_terminal_before_task_return_ms=%s\n' \
        "$enumeration_terminal_before_task_return_ms"
}

validate_attempt() {
    local path="$1"

    [[ -f "$path" ]] || reject missing_artifact
    artifact_sha256="$(shasum -a 256 "$path" | awk '{ print $1 }')"
    sha256_token "$artifact_sha256" || reject artifact_hash

    validate_attempt_schema "$path"
    validate_startup_line "$path"
    validate_timing_line "$path"
    validate_critical_line "$path"
    validate_temporal_contract
    validate_cross_line_and_clocks
    validate_attempt_identity_and_outcome
    emit_accepted_attempt
}

emit_summary_attempt_outputs() {
    if [[ "$summary_attempt_outputs_emitted" == 0 &&
          "${#summary_attempt_outputs[@]}" -gt 0 ]]; then
        printf '%s\n' "${summary_attempt_outputs[@]}"
        summary_attempt_outputs_emitted=1
    fi
}

summary_fail() {
    emit_summary_attempt_outputs
    printf 'HPA192_CRITICAL_PATH_SUMMARY status=rejected reason=%s\n' "$1"
    exit 1
}

summary_record_failure() {
    if [[ -z "$summary_set_failure_reason" ]]; then
        summary_set_failure_reason="$1"
    fi
}

canonical_path() {
    perl -MCwd=abs_path -e '
        my $path = abs_path($ARGV[0]);
        exit 1 unless defined $path;
        print $path;
    ' "$1"
}

summary_require_fixed_hash() {
    local name="$1"
    local value="$2"
    local variable="summary_fixed_$name"
    local previous

    eval "previous=\${$variable:-}"
    if [[ -z "$previous" ]]; then
        printf -v "$variable" '%s' "$value"
    elif [[ "$value" != "$previous" ]]; then
        summary_record_failure mixed_fixed_hashes
    fi
}

summary_require_scenario_value() {
    local family="$1"
    local current_scenario="$2"
    local value="$3"
    local reason="$4"
    local variable="summary_${family}_${current_scenario}"
    local previous

    eval "previous=\${$variable:-}"
    if [[ -z "$previous" ]]; then
        printf -v "$variable" '%s' "$value"
    elif [[ "$value" != "$previous" ]]; then
        summary_record_failure "$reason"
    fi
}

summarize_attempts() {
    local -a paths=("$@")
    local -a canonical_paths=()
    local -a attempt_identities=()
    local -a accepted_slot_identities=()
    local -a accepted_outputs=()
    local path
    local canonical
    local previous
    local metadata_line
    local identity
    local accepted_slot_identity
    local output
    local count_a=0
    local count_b=0
    local count_c=0
    local current_scenario
    local metric
    local -a values=()
    local accepted_line
    local value
    local sorted
    local minimum
    local median
    local maximum
    local metadata_scenario
    local metadata_slot
    local metadata_attempt
    local hash_key
    local hash_family
    local hash_value

    summary_attempt_outputs=()
    summary_attempt_outputs_emitted=0
    summary_set_failure_reason=

    [[ "${#paths[@]}" -gt 0 ]] || summary_fail missing_artifacts

    for path in "${paths[@]}"; do
        [[ -f "$path" ]] || summary_fail missing_artifact
        canonical="$(canonical_path "$path")" ||
            summary_fail canonical_path
        if (( ${#canonical_paths[@]} > 0 )); then
            for previous in "${canonical_paths[@]}"; do
                [[ "$canonical" != "$previous" ]] ||
                    summary_record_failure duplicate_canonical_artifact
            done
        fi
        canonical_paths+=("$canonical")

        if [[ "$(line_count "$path" HPA192_ATTEMPT)" == 1 ]]; then
            metadata_line="$(only_line "$path" HPA192_ATTEMPT)"
            metadata_scenario=
            metadata_slot=
            metadata_attempt=
            read_expected_field \
                "$metadata_line" \
                HPA192_ATTEMPT \
                attempt_field_names \
                scenario \
                metadata_scenario || true
            read_expected_field \
                "$metadata_line" \
                HPA192_ATTEMPT \
                attempt_field_names \
                slot \
                metadata_slot || true
            read_expected_field \
                "$metadata_line" \
                HPA192_ATTEMPT \
                attempt_field_names \
                attempt \
                metadata_attempt || true

            if [[ "$metadata_scenario" =~ ^[ABC]$ ]] &&
               decimal_at_most "$metadata_slot" "$max_slot" &&
               [[ "$metadata_slot" -ge 1 ]] &&
               decimal_at_most "$metadata_attempt" "$max_attempt" &&
               [[ "$metadata_attempt" -ge 1 ]]; then
                identity="$metadata_scenario/$metadata_slot/$metadata_attempt"
                if (( ${#attempt_identities[@]} > 0 )); then
                    for previous in "${attempt_identities[@]}"; do
                        [[ "$identity" != "$previous" ]] ||
                            summary_record_failure duplicate_attempt_identity
                    done
                fi
                attempt_identities+=("$identity")
            fi

            for hash_key in \
                game_sha256 \
                runner_sha256 \
                summarizer_sha256 \
                corpus_manifest_sha256 \
                system_manifest_sha256 \
                empty_manifest_sha256 \
                seed_manifest_sha256
            do
                hash_value=
                if read_expected_field \
                    "$metadata_line" \
                    HPA192_ATTEMPT \
                    attempt_field_names \
                    "$hash_key" \
                    hash_value &&
                   sha256_token "$hash_value"; then
                    case "$hash_key" in
                        game_sha256) hash_family=game ;;
                        runner_sha256) hash_family=runner ;;
                        summarizer_sha256) hash_family=summarizer ;;
                        corpus_manifest_sha256) hash_family=corpus ;;
                        system_manifest_sha256) hash_family=system ;;
                        empty_manifest_sha256) hash_family=empty ;;
                        seed_manifest_sha256) hash_family=seed ;;
                    esac
                    summary_require_fixed_hash "$hash_family" "$hash_value"
                fi
            done

            if [[ "$metadata_scenario" =~ ^[ABC]$ ]]; then
                hash_value=
                if read_expected_field \
                    "$metadata_line" \
                    HPA192_ATTEMPT \
                    attempt_field_names \
                    config_sha256 \
                    hash_value &&
                   sha256_token "$hash_value"; then
                    summary_require_scenario_value \
                        config \
                        "$metadata_scenario" \
                        "$hash_value" \
                        mixed_scenario_config_hashes
                fi

                hash_value=
                if read_expected_field \
                    "$metadata_line" \
                    HPA192_ATTEMPT \
                    attempt_field_names \
                    expected_chart_paths_sha256 \
                    hash_value &&
                   sha256_token "$hash_value"; then
                    summary_require_scenario_value \
                        chart_paths \
                        "$metadata_scenario" \
                        "$hash_value" \
                        mixed_scenario_chart_paths
                fi
            fi
        fi

        if output="$(bash "$script_path" --validate-attempt "$path")"; then
            summary_attempt_outputs+=("$output")
            parse_ordered_line \
                "$output" \
                HPA192_CRITICAL_PATH_ATTEMPT \
                accepted \
                accepted_output_field_names ||
                summary_fail internal_accepted_schema
            [[ "$accepted_status" == accepted ]] ||
                summary_fail internal_accepted_schema

            metadata_line="$(only_line "$path" HPA192_ATTEMPT)"
            parse_ordered_line \
                "$metadata_line" \
                HPA192_ATTEMPT \
                summary \
                attempt_field_names ||
                summary_fail internal_attempt_schema

            accepted_slot_identity="$summary_scenario/$summary_slot"
            if (( ${#accepted_slot_identities[@]} > 0 )); then
                for previous in "${accepted_slot_identities[@]}"; do
                    [[ "$accepted_slot_identity" != "$previous" ]] ||
                        summary_record_failure duplicate_accepted_slot
                done
            fi
            accepted_slot_identities+=("$accepted_slot_identity")
            accepted_outputs+=("$output")

            case "$summary_scenario" in
                A) count_a=$((count_a + 1)) ;;
                B) count_b=$((count_b + 1)) ;;
                C) count_c=$((count_c + 1)) ;;
            esac
        else
            [[ -n "$output" ]] || summary_fail missing_rejection_record
            summary_attempt_outputs+=("$output")
        fi
    done

    emit_summary_attempt_outputs
    if [[ -n "$summary_set_failure_reason" ]]; then
        summary_fail "$summary_set_failure_reason"
    fi

    [[ "${#accepted_outputs[@]}" -eq 15 &&
       "$count_a" -eq 5 &&
       "$count_b" -eq 5 &&
       "$count_c" -eq 5 ]] ||
        summary_fail incomplete_acceptance_sequence

    # Under set -u, explicitly reject empty required values before comparing.
    # These variables are assigned during acceptance; if any is empty the
    # scenario data is incomplete and we fail closed.
    local _cfg_a="${summary_config_A:-}"
    local _cfg_c="${summary_config_C:-}"
    local _paths_a="${summary_chart_paths_A:-}"
    local _paths_b="${summary_chart_paths_B:-}"
    local _paths_c="${summary_chart_paths_C:-}"
    local _fixed_empty="${summary_fixed_empty:-}"
    [[ -n "$_cfg_a" && -n "$_cfg_c" ]] ||
        summary_fail missing_scenario_config_hashes
    [[ -n "$_paths_a" && -n "$_paths_b" && -n "$_paths_c" && -n "$_fixed_empty" ]] ||
        summary_fail missing_scenario_chart_paths
    [[ "$_cfg_a" == "$_cfg_c" ]] ||
        summary_fail mixed_scenario_config_hashes
    [[ "$_paths_a" == "$_paths_c" ]] ||
        summary_fail mixed_scenario_chart_paths
    [[ "$_paths_b" == "$_fixed_empty" ]] ||
        summary_fail mixed_scenario_chart_paths

    for current_scenario in A B C; do
        for metric in "${summary_metric_names[@]}"; do
            values=()
            for accepted_line in "${accepted_outputs[@]}"; do
                parse_ordered_line \
                    "$accepted_line" \
                    HPA192_CRITICAL_PATH_ATTEMPT \
                    metric_row \
                    accepted_output_field_names ||
                    summary_fail internal_accepted_schema
                if [[ "$metric_row_scenario" == "$current_scenario" ]]; then
                    eval "value=\$metric_row_${metric}"
                    values+=("$value")
                fi
            done
            [[ "${#values[@]}" -eq 5 ]] ||
                summary_fail incomplete_acceptance_sequence
            sorted="$(printf '%s\n' "${values[@]}" | sort -n)"
            minimum="$(printf '%s\n' "$sorted" | sed -n '1p')"
            median="$(printf '%s\n' "$sorted" | sed -n '3p')"
            maximum="$(printf '%s\n' "$sorted" | sed -n '5p')"
            printf 'HPA192_CRITICAL_PATH_SUMMARY scenario=%s samples=5 metric=%s minimum_ms=%s median_ms=%s maximum_ms=%s\n' \
                "$current_scenario" \
                "$metric" \
                "$minimum" \
                "$median" \
                "$maximum"
        done
    done
}

if [[ "$#" -eq 2 && "$1" == --validate-attempt ]]; then
    validate_attempt "$2"
    exit 0
fi

if [[ "$#" -ge 2 && "$1" == --summarize ]]; then
    shift
    summarize_attempts "$@"
    exit 0
fi

printf 'usage: summarize-critical-path.sh --validate-attempt ARTIFACT\n' >&2
printf '       summarize-critical-path.sh --summarize ARTIFACT...\n' >&2
exit 2

#!/usr/bin/env bash
set -euo pipefail

if [[ "$#" -ne 3 ]]; then
    printf 'usage: summarize-timing-preflight.sh RUN_1 RUN_2 RUN_3\n' >&2
    exit 2
fi

canonical_results=()
for result in "$@"; do
    test -f "$result" || {
        printf 'missing result artifact: %s\n' "$result" >&2
        exit 1
    }

    canonical_result="$(cd "$(dirname "$result")" && pwd -P)/$(basename "$result")"
    if (( ${#canonical_results[@]} > 0 )); then
        for previous_result in "${canonical_results[@]}"; do
            [[ "$canonical_result" != "$previous_result" ]] || {
                printf 'duplicate canonical result artifact: %s\n' "$canonical_result" >&2
                exit 1
            }
        done
    fi
    canonical_results+=("$canonical_result")
done

field_value() {
    local file="$1"
    local key="$2"
    local value
    value="$(awk -v key="$key" '
        {
            for (i = 1; i <= NF; i++) {
                split($i, pair, "=")
                if (pair[1] == key) {
                    count++
                    value = pair[2]
                }
            }
        }
        END {
            if (count != 1) exit 1
            print value
        }' "$file")" || {
        printf 'expected exactly one %s in %s\n' "$key" "$file" >&2
        exit 1
    }
    [[ "$value" =~ ^[0-9]+$ ]] || {
        printf 'expected nonnegative integer %s in %s\n' "$key" "$file" >&2
        exit 1
    }
    printf '%s\n' "$value"
}

text_field_value() {
    local file="$1"
    local key="$2"
    local value
    value="$(awk -v key="$key" '
        {
            for (i = 1; i <= NF; i++) {
                split($i, pair, "=")
                if (pair[1] == key) {
                    count++
                    value = pair[2]
                }
            }
        }
        END {
            if (count != 1) exit 1
            print value
        }' "$file")" || {
        printf 'expected exactly one %s in %s\n' "$key" "$file" >&2
        exit 1
    }
    [[ "$value" =~ ^[A-Za-z0-9._-]+$ ]] || {
        printf 'expected safe text %s in %s\n' "$key" "$file" >&2
        exit 1
    }
    printf '%s\n' "$value"
}

timing_line() {
    local file="$1"
    local count
    count="$(awk '/(^|[[:space:]])HPA192_TIMING[[:space:]]/ { count++ } END { print count + 0 }' "$file")"
    [[ "$count" == 1 ]] || {
        printf 'expected exactly one HPA192_TIMING line in %s, found %s\n' "$file" "$count" >&2
        exit 1
    }
}

median_of_three() {
    printf '%s\n' "$1" "$2" "$3" | sort -n | sed -n '2p'
}

readonly max_cross_process_delta_ms=300000
readonly max_process_clock_alignment_difference_ms=50
fixed_floors=()
config_to_startups=()
artifact_identities=()
expected_label=""

for index in 1 2 3; do
    result="${!index}"
    timing_line "$result"

    label="$(text_field_value "$result" label)"
    run="$(field_value "$result" run)"
    case "$run" in
        1|2|3) ;;
        *)
            printf 'unexpected artifact run: %s in %s\n' "$run" "$result" >&2
            exit 1
            ;;
    esac
    artifact_identity="$label/$run"
    if (( ${#artifact_identities[@]} > 0 )); then
        for previous_identity in "${artifact_identities[@]}"; do
            [[ "$artifact_identity" != "$previous_identity" ]] || {
                printf 'duplicate artifact identity: %s\n' "$artifact_identity" >&2
                exit 1
            }
        done
    fi
    artifact_identities+=("$artifact_identity")
    if [[ -z "$expected_label" ]]; then
        expected_label="$label"
    elif [[ "$label" != "$expected_label" ]]; then
        printf 'mixed artifact labels: %s and %s\n' "$expected_label" "$label" >&2
        exit 1
    fi

    entry_to_config="$(field_value "$result" entry_to_config_ms)"
    config_to_load_content="$(field_value "$result" config_to_load_content_ms)"
    load_content_to_startup="$(field_value "$result" load_content_to_startup_ms)"
    startup_to_first_draw="$(field_value "$result" startup_to_first_draw_ms)"
    startup_to_summary="$(field_value "$result" startup_to_summary_ms)"
    summary_to_title="$(field_value "$result" summary_to_title_ms)"
    entry_to_title="$(field_value "$result" entry_to_title_ms)"
    entry_unix_us="$(field_value "$result" entry_unix_us)"
    title_unix_us="$(field_value "$result" title_unix_us)"
    launch_start_unix_us="$(field_value "$result" launch_start_unix_us)"
    launch_end_unix_us="$(field_value "$result" launch_end_unix_us)"
    wall_ms="$(field_value "$result" wall_ms)"

    entry_to_startup=$((entry_to_config + config_to_load_content + load_content_to_startup))
    config_to_startup=$((config_to_load_content + load_content_to_startup))
    contiguous_entry_to_title_sum=$((entry_to_startup + startup_to_summary + summary_to_title))
    maximum_entry_to_title=$((contiguous_entry_to_title_sum + 4))
    [[ "$entry_to_title" -ge "$contiguous_entry_to_title_sum" &&
       "$entry_to_title" -le "$maximum_entry_to_title" ]] || {
        printf 'inconsistent entry_to_title_ms in %s\n' "$result" >&2
        exit 1
    }
    [[ "$startup_to_first_draw" -le "$startup_to_summary" ]] || {
        printf 'first draw follows summary in %s\n' "$result" >&2
        exit 1
    }
    [[ "$title_unix_us" -ge "$entry_unix_us" ]] || {
        printf 'process UTC anchors regress in %s\n' "$result" >&2
        exit 1
    }
    [[ "$entry_unix_us" -ge "$launch_start_unix_us" &&
       "$launch_end_unix_us" -ge "$title_unix_us" &&
       "$launch_end_unix_us" -ge "$launch_start_unix_us" ]] || {
        printf 'cross-process UTC anchors regress in %s\n' "$result" >&2
        exit 1
    }

    external_launch_to_entry_ms=$(((entry_unix_us - launch_start_unix_us) / 1000))
    external_launch_to_startup_ms=$((external_launch_to_entry_ms + entry_to_startup))
    title_poll_lag_ms=$(((launch_end_unix_us - title_unix_us) / 1000))
    external_wall_ms=$(((launch_end_unix_us - launch_start_unix_us) / 1000))
    process_wall_elapsed_ms=$(((title_unix_us - entry_unix_us) / 1000))
    process_clock_difference_ms=$((process_wall_elapsed_ms - entry_to_title))
    if [[ "$process_clock_difference_ms" -lt 0 ]]; then
        process_clock_difference_ms=$((-process_clock_difference_ms))
    fi
    fixed_floor_ms=$((external_launch_to_startup_ms + startup_to_first_draw + summary_to_title))
    wall_difference_ms=$((external_wall_ms - wall_ms))
    if [[ "$wall_difference_ms" -lt 0 ]]; then
        wall_difference_ms=$((-wall_difference_ms))
    fi

    [[ "$external_launch_to_entry_ms" -ge 0 && "$external_launch_to_entry_ms" -le "$max_cross_process_delta_ms" ]] || {
        printf 'launch-to-entry UTC delta is invalid in %s\n' "$result" >&2
        exit 1
    }
    [[ "$title_poll_lag_ms" -ge 0 && "$title_poll_lag_ms" -le "$max_cross_process_delta_ms" ]] || {
        printf 'title-poll UTC delta is invalid in %s\n' "$result" >&2
        exit 1
    }
    [[ "$wall_difference_ms" -le 1 ]] || {
        printf 'wall clock changed during %s\n' "$result" >&2
        exit 1
    }
    [[ "$process_clock_difference_ms" -le "$max_process_clock_alignment_difference_ms" ]] || {
        printf 'process clock changed during %s\n' "$result" >&2
        exit 1
    }

    printf 'sample=%s entry_to_config_ms=%s config_to_load_content_ms=%s load_content_to_startup_ms=%s startup_to_first_draw_ms=%s startup_to_summary_ms=%s summary_to_title_ms=%s entry_to_title_ms=%s\n' \
        "$artifact_identity" "$entry_to_config" "$config_to_load_content" "$load_content_to_startup" "$startup_to_first_draw" "$startup_to_summary" "$summary_to_title" "$entry_to_title"
    printf 'sample=%s entry_to_startup_ms=%s config_to_startup_ms=%s external_launch_to_entry_ms=%s external_launch_to_startup_ms=%s\n' \
        "$artifact_identity" "$entry_to_startup" "$config_to_startup" "$external_launch_to_entry_ms" "$external_launch_to_startup_ms"
    printf 'sample=%s fixed_floor_ms=%s title_poll_lag_ms=%s wall_ms=%s\n' \
        "$artifact_identity" "$fixed_floor_ms" "$title_poll_lag_ms" "$wall_ms"

    fixed_floors+=("$fixed_floor_ms")
    config_to_startups+=("$config_to_startup")
done

median_fixed_floor_ms="$(median_of_three "${fixed_floors[@]}")"
median_config_to_startup_ms="$(median_of_three "${config_to_startups[@]}")"
if [[ "$median_fixed_floor_ms" -ge 2221 ]]; then
    decision=stop
else
    decision=continue
fi

printf 'HPA192_PREFLIGHT median_fixed_floor_ms=%s target_ms=2221 decision=%s\n' \
    "$median_fixed_floor_ms" "$decision"
printf 'HPA192_PREFLIGHT median_config_to_startup_ms=%s\n' "$median_config_to_startup_ms"

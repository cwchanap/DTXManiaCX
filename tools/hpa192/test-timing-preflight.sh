#!/usr/bin/env bash
set -euo pipefail

repo_root="$(cd "$(dirname "${BASH_SOURCE[0]}")/../.." && pwd)"
summarizer="$repo_root/tools/hpa192/summarize-timing-preflight.sh"
temp_root="$(mktemp -d "${TMPDIR:-/tmp}/hpa-192-timing-test.XXXXXX")"

cleanup() {
    rm -rf -- "$temp_root"
}
trap cleanup EXIT

fail() {
    printf 'FAIL: %s\n' "$*" >&2
    exit 1
}

# Portable in-place replace: replaces all occurrences of $old with $new in $path.
# Uses perl to avoid BSD/GNU sed incompatibility (sed -i '' vs sed -i).
replace_inplace() {
    local path="$1" old="$2" new="$3"
    OLD="$old" NEW="$new" perl -pi -e 's/\Q$ENV{OLD}\E/$ENV{NEW}/g' "$path"
}

# Portable in-place line append: appends $suffix to every line in $path.
append_inplace() {
    local path="$1" suffix="$2"
    SUFFIX="$suffix" perl -pi -e 's/$/$ENV{SUFFIX}/' "$path"
}

write_result() {
    local path="$1"
    local run="$2"
    local entry_to_config="$3"
    local entry_unix_us="$4"
    local launch_start_unix_us="$5"
    local launch_end_unix_us="$6"
    local title_unix_us="$7"
    local wall_ms="$8"
    local external_monotonic_elapsed_ms="${9:-$wall_ms}"
    local launch_start_monotonic_us=1000000
    local launch_end_monotonic_us=$((launch_start_monotonic_us + external_monotonic_elapsed_ms * 1000))

    cat >"$path" <<EOF
label=synthetic run=$run wall_ms=$wall_ms launch_start_unix_us=$launch_start_unix_us launch_start_monotonic_us=$launch_start_monotonic_us launch_end_unix_us=$launch_end_unix_us launch_end_monotonic_us=$launch_end_monotonic_us HPA192_TIMING entry_to_config_ms=$entry_to_config config_to_load_content_ms=20 load_content_to_startup_ms=30 startup_to_first_draw_ms=40 startup_to_summary_ms=100 summary_to_title_ms=2020 entry_to_title_ms=2180 entry_unix_us=$entry_unix_us title_unix_us=$title_unix_us
EOF
}

run_summary() {
    bash "$summarizer" "$@"
}

assert_fails() {
    local name="$1"
    shift
    if "$@" >"$temp_root/$name.stdout" 2>"$temp_root/$name.stderr"; then
        fail "$name unexpectedly succeeded"
    fi
}

assert_contains() {
    local expected="$1"
    local file="$2"
    grep -Fqx "$expected" "$file" || fail "missing '$expected' in $file"
}

good_one="$temp_root/good-1.result.txt"
good_two="$temp_root/good-2.result.txt"
good_three="$temp_root/good-3.result.txt"
write_result "$good_one" 1 10 1100000 1000000 3310000 3280000 2310
write_result "$good_two" 2 10 1100000 1000000 3410000 3280000 2410
write_result "$good_three" 3 10 1100000 1000000 3510000 3280000 2510

input_hashes_before="$(shasum -a 256 "$good_one" "$good_two" "$good_three")"
run_summary "$good_one" "$good_two" "$good_three" >"$temp_root/good.out"
input_hashes_after="$(shasum -a 256 "$good_one" "$good_two" "$good_three")"
[[ "$input_hashes_before" == "$input_hashes_after" ]] || fail "summarizer mutated an input artifact"
assert_contains "sample=synthetic/1 fixed_floor_ms=2220 title_poll_lag_ms=30 wall_ms=2310" "$temp_root/good.out"
assert_contains "sample=synthetic/2 fixed_floor_ms=2220 title_poll_lag_ms=130 wall_ms=2410" "$temp_root/good.out"
assert_contains "sample=synthetic/3 fixed_floor_ms=2220 title_poll_lag_ms=230 wall_ms=2510" "$temp_root/good.out"
assert_contains "HPA192_PREFLIGHT median_fixed_floor_ms=2220 target_ms=2221 decision=continue" "$temp_root/good.out"

assert_fails duplicate_canonical_path run_summary "$good_one" "$temp_root/./$(basename "$good_one")" "$good_three"

duplicate_identity="$temp_root/duplicate-identity.result.txt"
cp "$good_one" "$duplicate_identity"
assert_fails duplicate_artifact_identity run_summary "$good_one" "$duplicate_identity" "$good_three"

substituted_run="$temp_root/substituted-run.result.txt"
write_result "$substituted_run" 4 10 1100000 1000000 3310000 3280000 2310
assert_fails substituted_artifact_run run_summary "$substituted_run" "$good_two" "$good_three"

rounded_one="$temp_root/rounded-1.result.txt"
rounded_two="$temp_root/rounded-2.result.txt"
rounded_three="$temp_root/rounded-3.result.txt"
write_result "$rounded_one" 1 10 1100000 1000000 3310000 3280000 2310
write_result "$rounded_two" 2 10 1100000 1000000 3410000 3280000 2410
write_result "$rounded_three" 3 10 1100000 1000000 3510000 3280000 2510
replace_inplace "$rounded_one" "entry_to_title_ms=2180" "entry_to_title_ms=2184"
replace_inplace "$rounded_two" "entry_to_title_ms=2180" "entry_to_title_ms=2184"
replace_inplace "$rounded_three" "entry_to_title_ms=2180" "entry_to_title_ms=2184"
run_summary "$rounded_one" "$rounded_two" "$rounded_three" >"$temp_root/rounded.out"
assert_contains "HPA192_PREFLIGHT median_fixed_floor_ms=2220 target_ms=2221 decision=continue" "$temp_root/rounded.out"

stop_one="$temp_root/stop-1.result.txt"
stop_two="$temp_root/stop-2.result.txt"
stop_three="$temp_root/stop-3.result.txt"
write_result "$stop_one" 1 10 1100000 999000 3310000 3280000 2311
write_result "$stop_two" 2 10 1100000 999000 3310000 3280000 2311
write_result "$stop_three" 3 10 1100000 999000 3310000 3280000 2311
run_summary "$stop_one" "$stop_two" "$stop_three" >"$temp_root/stop.out"
assert_contains "HPA192_PREFLIGHT median_fixed_floor_ms=2221 target_ms=2221 decision=stop" "$temp_root/stop.out"

missing="$temp_root/missing.result.txt"
printf 'label=missing run=1 wall_ms=1 launch_start_unix_us=1 launch_end_unix_us=2\n' >"$missing"
assert_fails missing run_summary "$missing" "$good_two" "$good_three"

duplicate="$temp_root/duplicate.result.txt"
cp "$good_one" "$duplicate"
append_inplace "$duplicate" " HPA192_TIMING entry_to_config_ms=10"
assert_fails duplicate run_summary "$duplicate" "$good_two" "$good_three"

nonnumeric="$temp_root/nonnumeric.result.txt"
cp "$good_one" "$nonnumeric"
replace_inplace "$nonnumeric" "entry_to_config_ms=10" "entry_to_config_ms=ten"
assert_fails nonnumeric run_summary "$nonnumeric" "$good_two" "$good_three"

negative="$temp_root/negative.result.txt"
cp "$good_one" "$negative"
replace_inplace "$negative" "summary_to_title_ms=2020" "summary_to_title_ms=-1"
assert_fails negative run_summary "$negative" "$good_two" "$good_three"

above_timing_bound="$temp_root/above-timing-bound.result.txt"
cp "$good_one" "$above_timing_bound"
replace_inplace "$above_timing_bound" "entry_to_config_ms=10" "entry_to_config_ms=300001"
replace_inplace "$above_timing_bound" "entry_to_title_ms=2180" "entry_to_title_ms=302171"
replace_inplace "$above_timing_bound" "launch_end_unix_us=3310000" "launch_end_unix_us=303301000"
replace_inplace "$above_timing_bound" "title_unix_us=3280000" "title_unix_us=303271000"
replace_inplace "$above_timing_bound" "wall_ms=2310" "wall_ms=302301"
assert_fails above_timing_bound run_summary "$above_timing_bound" "$good_two" "$good_three"

above_unix_anchor_bound="$temp_root/above-unix-anchor-bound.result.txt"
cp "$good_one" "$above_unix_anchor_bound"
replace_inplace "$above_unix_anchor_bound" "entry_unix_us=1100000" "entry_unix_us=4102444800000001"
assert_fails above_unix_anchor_bound run_summary "$above_unix_anchor_bound" "$good_two" "$good_three"

above_monotonic_anchor_bound="$temp_root/above-monotonic-anchor-bound.result.txt"
cp "$good_one" "$above_monotonic_anchor_bound"
replace_inplace "$above_monotonic_anchor_bound" "launch_start_monotonic_us=1000000" "launch_start_monotonic_us=3155760000000001"
assert_fails above_monotonic_anchor_bound run_summary "$above_monotonic_anchor_bound" "$good_two" "$good_three"

wraparound="$temp_root/wraparound.result.txt"
cp "$good_one" "$wraparound"
replace_inplace "$wraparound" "entry_to_config_ms=10" "entry_to_config_ms=18446744073709551616"
replace_inplace "$wraparound" "entry_to_title_ms=2180" "entry_to_title_ms=2170"
assert_fails wraparound_numeric run_summary "$wraparound" "$good_two" "$good_three"

signed="$temp_root/signed.result.txt"
cp "$good_one" "$signed"
replace_inplace "$signed" "entry_to_config_ms=10" "entry_to_config_ms=+10"
assert_fails signed_numeric run_summary "$signed" "$good_two" "$good_three"

inconsistent="$temp_root/inconsistent.result.txt"
cp "$good_one" "$inconsistent"
replace_inplace "$inconsistent" "entry_to_title_ms=2180" "entry_to_title_ms=2179"
assert_fails underflow run_summary "$inconsistent" "$good_two" "$good_three"

overflow="$temp_root/overflow.result.txt"
cp "$good_one" "$overflow"
replace_inplace "$overflow" "entry_to_title_ms=2180" "entry_to_title_ms=2185"
assert_fails overflow run_summary "$overflow" "$good_two" "$good_three"

clock_step="$temp_root/clock-step.result.txt"
write_result "$clock_step" 1 10 1100000 1000000 4310000 4280000 3310
assert_fails process_clock_step run_summary "$clock_step" "$good_two" "$good_three"

pre_entry_clock_step="$temp_root/pre-entry-clock-step.result.txt"
write_result "$pre_entry_clock_step" 1 10 1100000 500000 3310000 3280000 2810 2310
assert_fails pre_entry_clock_step run_summary "$pre_entry_clock_step" "$good_two" "$good_three"

post_title_clock_step="$temp_root/post-title-clock-step.result.txt"
write_result "$post_title_clock_step" 1 10 1100000 1000000 3810000 3280000 2810 2310
assert_fails post_title_clock_step run_summary "$post_title_clock_step" "$good_two" "$good_three"

sequence_root="$temp_root/sequence-sentinel-root"
sequence_sentinel="$sequence_root/TestResults/hpa-192/comparative-order.txt"
mkdir -p "$(dirname "$sequence_sentinel")"
mkfifo "$sequence_sentinel"
if ! (
    cd "$sequence_root"
    perl -e 'alarm 2; exec @ARGV' bash "$summarizer" "$good_one" "$good_two" "$good_three"
) >"$temp_root/sequence-sentinel.out" 2>"$temp_root/sequence-sentinel.err"; then
    fail "summarizer touched the acceptance-sequence sentinel"
fi
test -p "$sequence_sentinel" || fail "summarizer changed the acceptance-sequence sentinel"

printf 'timing preflight shell tests passed\n'

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

write_result() {
    local path="$1"
    local run="$2"
    local entry_to_config="$3"
    local entry_unix_us="$4"
    local launch_start_unix_us="$5"
    local launch_end_unix_us="$6"
    local title_unix_us="$7"
    local wall_ms="$8"

    cat >"$path" <<EOF
label=synthetic run=$run wall_ms=$wall_ms launch_start_unix_us=$launch_start_unix_us launch_end_unix_us=$launch_end_unix_us HPA192_TIMING entry_to_config_ms=$entry_to_config config_to_load_content_ms=20 load_content_to_startup_ms=30 startup_to_first_draw_ms=40 startup_to_summary_ms=100 summary_to_title_ms=2020 entry_to_title_ms=2180 entry_unix_us=$entry_unix_us title_unix_us=$title_unix_us
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
sed -i '' 's/entry_to_title_ms=2180/entry_to_title_ms=2184/' "$rounded_one" "$rounded_two" "$rounded_three"
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
sed -i '' 's/$/ HPA192_TIMING entry_to_config_ms=10/' "$duplicate"
assert_fails duplicate run_summary "$duplicate" "$good_two" "$good_three"

nonnumeric="$temp_root/nonnumeric.result.txt"
cp "$good_one" "$nonnumeric"
sed -i '' 's/entry_to_config_ms=10/entry_to_config_ms=ten/' "$nonnumeric"
assert_fails nonnumeric run_summary "$nonnumeric" "$good_two" "$good_three"

negative="$temp_root/negative.result.txt"
cp "$good_one" "$negative"
sed -i '' 's/summary_to_title_ms=2020/summary_to_title_ms=-1/' "$negative"
assert_fails negative run_summary "$negative" "$good_two" "$good_three"

inconsistent="$temp_root/inconsistent.result.txt"
cp "$good_one" "$inconsistent"
sed -i '' 's/entry_to_title_ms=2180/entry_to_title_ms=2179/' "$inconsistent"
assert_fails underflow run_summary "$inconsistent" "$good_two" "$good_three"

overflow="$temp_root/overflow.result.txt"
cp "$good_one" "$overflow"
sed -i '' 's/entry_to_title_ms=2180/entry_to_title_ms=2185/' "$overflow"
assert_fails overflow run_summary "$overflow" "$good_two" "$good_three"

clock_step="$temp_root/clock-step.result.txt"
write_result "$clock_step" 1 10 1100000 1000000 4310000 4280000 3310
assert_fails process_clock_step run_summary "$clock_step" "$good_two" "$good_three"

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

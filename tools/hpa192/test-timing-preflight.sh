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
    local entry_to_config="$2"
    local entry_unix_us="$3"
    local launch_start_unix_us="$4"
    local launch_end_unix_us="$5"
    local title_unix_us="$6"
    local wall_ms="$7"

    cat >"$path" <<EOF
label=synthetic run=$(basename "$path") wall_ms=$wall_ms launch_start_unix_us=$launch_start_unix_us launch_end_unix_us=$launch_end_unix_us HPA192_TIMING entry_to_config_ms=$entry_to_config config_to_load_content_ms=20 load_content_to_startup_ms=30 startup_to_first_draw_ms=40 startup_to_summary_ms=100 summary_to_title_ms=2020 entry_to_title_ms=2180 entry_unix_us=$entry_unix_us title_unix_us=$title_unix_us
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
write_result "$good_one" 10 1100000 1000000 3310000 3280000 2310
write_result "$good_two" 10 1100000 1000000 3410000 3280000 2410
write_result "$good_three" 10 1100000 1000000 3510000 3280000 2510

run_summary "$good_one" "$good_two" "$good_three" >"$temp_root/good.out"
assert_contains "run=1 fixed_floor_ms=2220 title_poll_lag_ms=30 wall_ms=2310" "$temp_root/good.out"
assert_contains "run=2 fixed_floor_ms=2220 title_poll_lag_ms=130 wall_ms=2410" "$temp_root/good.out"
assert_contains "run=3 fixed_floor_ms=2220 title_poll_lag_ms=230 wall_ms=2510" "$temp_root/good.out"
assert_contains "HPA192_PREFLIGHT median_fixed_floor_ms=2220 target_ms=2221 decision=continue" "$temp_root/good.out"

rounded="$temp_root/rounded.result.txt"
cp "$good_one" "$rounded"
sed -i '' 's/entry_to_title_ms=2180/entry_to_title_ms=2184/' "$rounded"
run_summary "$rounded" "$rounded" "$rounded" >"$temp_root/rounded.out"
assert_contains "HPA192_PREFLIGHT median_fixed_floor_ms=2220 target_ms=2221 decision=continue" "$temp_root/rounded.out"

stop_one="$temp_root/stop-1.result.txt"
stop_two="$temp_root/stop-2.result.txt"
stop_three="$temp_root/stop-3.result.txt"
write_result "$stop_one" 10 1100000 999000 3310000 3280000 2311
write_result "$stop_two" 10 1100000 999000 3310000 3280000 2311
write_result "$stop_three" 10 1100000 999000 3310000 3280000 2311
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

printf 'timing preflight shell tests passed\n'

#!/usr/bin/env bash
# Deterministic regression check for the shared FFmpeg cache lock. It combines
# static ordering assertions against the builder with a FIFO-controlled model
# of two invocations: the second invocation may replace the cache only after
# the first has copied every output file.

set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"
BUILDER="$SCRIPT_DIR/build-runtime.sh"
TMP="$(mktemp -d "${TMPDIR:-/tmp}/dtx-ffmpeg-cache-lock-test.XXXXXX")"
CACHE_ROOT="$TMP/cache-root"
CACHE_DIR="$CACHE_ROOT/7.0.2/osx-arm64"
LOCK="$CACHE_ROOT/.build-lock"
REPLACEMENT_DIR="$TMP/replacement"
A_OUTPUT="$TMP/output-a"
B_OUTPUT="$TMP/output-b"
A_COPIED_MARKER="$TMP/a-copied"
A_PID=""
B_PID=""

cleanup() {
  if [[ -n "$A_PID" ]]; then kill "$A_PID" 2>/dev/null || true; fi
  if [[ -n "$B_PID" ]]; then kill "$B_PID" 2>/dev/null || true; fi
  wait "$A_PID" 2>/dev/null || true
  wait "$B_PID" 2>/dev/null || true
  rm -rf "$TMP"
}

trap cleanup EXIT

assert_builder_lock_order() {
  local acquire_count cache_line install_line release_line acquire_line

  acquire_count="$(grep -c '^acquire_cache_lock || exit 1$' "$BUILDER" || true)"
  acquire_line="$(grep -n '^acquire_cache_lock || exit 1$' "$BUILDER" | cut -d: -f1)"
  cache_line="$(grep -n '^if cache_metadata_valid "\$cache_dir"; then$' "$BUILDER" | cut -d: -f1)"
  install_line="$(grep -n 'install -m 644 "\$cache_dir/COPYING\.LGPLv2\.1"' "$BUILDER" | cut -d: -f1)"
  release_line="$(grep -n '^release_cache_lock$' "$BUILDER" | tail -n1 | cut -d: -f1)"

  if (( acquire_count != 1 )) || [[ -z "$acquire_line" || -z "$cache_line" || -z "$install_line" || -z "$release_line" ]]; then
    echo "FAIL: expected one lock acquisition and complete cache-copy ordering in $BUILDER" >&2
    exit 1
  fi
  if (( acquire_line >= cache_line || cache_line >= install_line || install_line >= release_line )); then
    echo "FAIL: cache lock is not held through validation and output copies" >&2
    exit 1
  fi
}

acquire_test_lock() {
  until mkdir "$LOCK" 2>/dev/null; do
    :
  done
}

release_test_lock() {
  rmdir "$LOCK"
}

copy_cache() {
  local source_root="$1"
  local output_root="$2"
  mkdir -p "$output_root"
  cp "$source_root/ffmpeg" "$output_root/ffmpeg"
  cp "$source_root/ffprobe" "$output_root/ffprobe"
  cp "$source_root/COPYING.LGPLv2.1" "$output_root/COPYING.LGPLv2.1"
}

wait_event() {
  local fifo="$1"
  local event
  IFS= read -r event < "$fifo"
  printf '%s' "$event"
}

assert_builder_lock_order

mkdir -p "$CACHE_DIR" "$REPLACEMENT_DIR"
for file in ffmpeg ffprobe COPYING.LGPLv2.1; do
  printf 'cache-v1-%s\n' "$file" > "$CACHE_DIR/$file"
  printf 'cache-v2-%s\n' "$file" > "$REPLACEMENT_DIR/$file"
done

A_LOCKED="$TMP/a-locked"
A_COPY="$TMP/a-copy"
A_COPIED="$TMP/a-copied-event"
B_ATTEMPTED="$TMP/b-attempted"
B_STATE="$TMP/b-state"
B_REPLACE="$TMP/b-replace"
B_DONE="$TMP/b-done"
mkfifo "$A_LOCKED" "$A_COPY" "$A_COPIED" "$B_ATTEMPTED" "$B_STATE" "$B_REPLACE" "$B_DONE"

worker_a() {
  acquire_test_lock
  printf 'locked\n' > "$A_LOCKED"
  IFS= read -r _ < "$A_COPY"
  copy_cache "$CACHE_DIR" "$A_OUTPUT"
  : > "$A_COPIED_MARKER"
  printf 'copied\n' > "$A_COPIED"
  release_test_lock
}

worker_b() {
  printf 'attempted\n' > "$B_ATTEMPTED"
  acquire_test_lock
  if [[ -f "$A_COPIED_MARKER" ]]; then
    printf 'locked-after-copy\n' > "$B_STATE"
  else
    printf 'locked-before-copy\n' > "$B_STATE"
  fi
  IFS= read -r _ < "$B_REPLACE"
  rm -rf "$CACHE_DIR"
  mv "$REPLACEMENT_DIR" "$CACHE_DIR"
  copy_cache "$CACHE_DIR" "$B_OUTPUT"
  release_test_lock
  printf 'done\n' > "$B_DONE"
}

worker_a &
A_PID=$!
[[ "$(wait_event "$A_LOCKED")" == locked ]]

worker_b &
B_PID=$!
[[ "$(wait_event "$B_ATTEMPTED")" == attempted ]]

printf 'copy\n' > "$A_COPY"
[[ "$(wait_event "$A_COPIED")" == copied ]]
[[ "$(wait_event "$B_STATE")" == locked-after-copy ]]

for file in ffmpeg ffprobe COPYING.LGPLv2.1; do
  test -f "$A_OUTPUT/$file"
  grep -Fxq "cache-v1-$file" "$A_OUTPUT/$file"
  grep -Fxq "cache-v1-$file" "$CACHE_DIR/$file"
done

printf 'replace\n' > "$B_REPLACE"
[[ "$(wait_event "$B_DONE")" == done ]]
wait "$A_PID"
wait "$B_PID"

for file in ffmpeg ffprobe COPYING.LGPLv2.1; do
  test -f "$B_OUTPUT/$file"
  grep -Fxq "cache-v2-$file" "$B_OUTPUT/$file"
done

echo "PASS: cache lock held through output copy; replacement waited for complete copy"

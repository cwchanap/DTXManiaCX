#!/usr/bin/env bash
set -euo pipefail

FFMPEG_VERSION="7.0.2"
FFMPEG_TARBALL_SHA256="8646515b638a3ad303e23af6a3587734447cb8fc0a0c064ecdb8e95c4fd8b389"
FFMPEG_URL="https://ffmpeg.org/releases/ffmpeg-${FFMPEG_VERSION}.tar.xz"

if [[ "$(uname -s)" != "Darwin" || "$(uname -m)" != "arm64" ]]; then
  echo "Native CX macOS runtime requires Apple Silicon (arm64); Intel/Rosetta is not supported." >&2
  exit 1
fi

if [[ "$#" -ne 2 ]]; then
  echo "Usage: $0 <runtime-output-dir> <license-output-dir>" >&2
  exit 2
fi

runtime_output_dir="$1"
license_output_dir="$2"
cache_root="${DTXMANIA_FFMPEG_CACHE_ROOT:-$HOME/Library/Caches/DTXManiaCX/ffmpeg}"
cache_dir="$cache_root/$FFMPEG_VERSION/osx-arm64"

# The builder only ever replaces the exact version/architecture cache leaf.
# Keep this guard close to the rm below so an invalid override cannot turn a
# cache refresh into a broad deletion.
case "$cache_dir" in
  */"$FFMPEG_VERSION"/osx-arm64) ;;
  *)
    echo "Invalid FFmpeg cache path: $cache_dir" >&2
    exit 1
    ;;
esac

work_root=""

cache_lock="$cache_root/.build-lock"
cache_lock_held=false

cleanup() {
  if [[ -n "$work_root" && -d "$work_root" ]]; then
    rm -rf "$work_root"
  fi
  if [[ "$cache_lock_held" == true ]]; then
    rmdir "$cache_lock" 2>/dev/null || true
  fi
}

trap cleanup EXIT

acquire_cache_lock() {
  mkdir -p "$cache_root"
  local attempt=0
  while (( attempt < 300 )); do
    if mkdir "$cache_lock" 2>/dev/null; then
      return 0
    fi
    attempt=$((attempt + 1))
    sleep 1
  done
  echo "Timed out waiting for FFmpeg cache lock: $cache_lock" >&2
  return 1
}

release_cache_lock() {
  rmdir "$cache_lock" 2>/dev/null || true
  cache_lock_held=false
}

cache_metadata_valid() {
  local root="$1"

  [[ -x "$root/ffmpeg" ]] || return 1
  [[ -x "$root/ffprobe" ]] || return 1
  [[ -f "$root/COPYING.LGPLv2.1" ]] || return 1
  [[ -f "$root/source.sha256" ]] || return 1
  grep -Fq -- "$FFMPEG_TARBALL_SHA256" "$root/source.sha256"
}

require_capability() {
  local binary="$1"
  local listing_option="$2"
  local capability="$3"

  if ! "$binary" -hide_banner "$listing_option" 2>/dev/null | grep -qw "$capability"; then
    echo "FFmpeg validation failed: $binary is missing $listing_option capability '$capability'." >&2
    return 1
  fi
}

validate_system_dependencies() {
  local binary="$1"
  local dependency_output
  local dependency

  if ! dependency_output="$(otool -L "$binary")"; then
    echo "FFmpeg validation failed: could not inspect dynamic dependencies for $binary." >&2
    return 1
  fi
  while IFS= read -r dependency; do
    [[ -z "$dependency" ]] && continue
    case "$dependency" in
      /usr/lib/*|/System/Library/*) ;;
      *)
        echo "FFmpeg validation failed: non-system dependency '$dependency' in $binary." >&2
        return 1
        ;;
    esac
  done < <(printf '%s\n' "$dependency_output" | awk 'NR > 1 && NF { print $1 }')
}

validate_runtime() {
  local root="$1"
  local binary
  local filter
  local decoder
  local demuxer
  local protocol

  [[ -f "$root/COPYING.LGPLv2.1" ]] || {
    echo "FFmpeg validation failed: missing LGPL license file in $root." >&2
    return 1
  }

  for binary in "$root/ffmpeg" "$root/ffprobe"; do
    if ! test -x "$binary"; then
      echo "FFmpeg validation failed: missing executable $binary." >&2
      return 1
    fi
    if ! file -b "$binary" | grep -qi arm64; then
      echo "FFmpeg validation failed: $binary is not an arm64 executable." >&2
      file "$binary" >&2
      return 1
    fi
    if ! "$binary" -version >/dev/null; then
      echo "FFmpeg validation failed: $binary did not respond to -version." >&2
      return 1
    fi
    validate_system_dependencies "$binary" || return 1
  done

  for filter in atempo apad atrim aformat aresample; do
    require_capability "$root/ffmpeg" -filters "$filter" || return 1
  done

  for decoder in mp3float vorbis pcm_s16le adpcm_ima_wav adpcm_ms; do
    require_capability "$root/ffmpeg" -decoders "$decoder" || return 1
  done

  for demuxer in mp3 wav ogg s16le; do
    require_capability "$root/ffmpeg" -demuxers "$demuxer" || return 1
  done

  for protocol in file pipe unix; do
    require_capability "$root/ffmpeg" -protocols "$protocol" || return 1
  done

  require_capability "$root/ffmpeg" -encoders pcm_s16le || return 1
  require_capability "$root/ffmpeg" -muxers s16le || return 1
}

cache_ready=false
acquire_cache_lock || exit 1
cache_lock_held=true
# Keep the lock through cache validation, a possible replacement, and the
# output copies below. A waiting invocation must revalidate after it acquires
# the lock so it can reuse a runtime produced by the previous invocation.
if cache_metadata_valid "$cache_dir"; then
  # Revalidate the full capability surface on every cache hit so a runtime
  # built before a capability amendment (such as the unix protocol) cannot
  # remain accepted solely because its source hash is still current.
  if validate_runtime "$cache_dir"; then
    echo "Using validated cached FFmpeg runtime: $cache_dir"
    cache_ready=true
  else
    echo "Cached FFmpeg runtime failed validation; rebuilding." >&2
  fi
fi
if [[ "$cache_ready" != true ]]; then
  work_root="$(mktemp -d "${TMPDIR:-/tmp}/dtx-ffmpeg.XXXXXX")"
  source_tarball="$work_root/ffmpeg-${FFMPEG_VERSION}.tar.xz"
  source_dir="$work_root/ffmpeg-${FFMPEG_VERSION}"
  install_dir="$work_root/install"
  cache_stage="$work_root/cache"

  echo "Downloading FFmpeg ${FFMPEG_VERSION} source..."
  curl -fsSL "$FFMPEG_URL" -o "$source_tarball"
  printf '%s  %s\n' "$FFMPEG_TARBALL_SHA256" "$source_tarball" > "$source_tarball.sha256"
  shasum -a 256 -c "$source_tarball.sha256"

  tar -xJf "$source_tarball" -C "$work_root"
  mkdir -p "$install_dir"

  (
    cd "$source_dir"
    ./configure \
      --prefix="$install_dir" \
      --enable-static --disable-shared \
      --disable-doc --disable-htmlpages --disable-manpages \
      --disable-podpages --disable-txtpages \
      --disable-ffplay \
      --disable-everything \
      --disable-autodetect \
      --enable-decoder=mp3float \
      --enable-decoder=vorbis \
      --enable-decoder=pcm_s16le,pcm_s24le,pcm_f32le,pcm_u8,pcm_alaw,pcm_mulaw \
      --enable-decoder=adpcm_ima_wav,adpcm_ms \
      --enable-demuxer=mp3 \
      --enable-demuxer=wav,ogg,pcm_s16le \
      --enable-parser=mpegaudio,vorbis \
      --enable-protocol=file,pipe,unix \
      --enable-muxer=pcm_s16le \
      --enable-encoder=pcm_s16le \
      --enable-filter=aformat,anull,aresample,atempo,apad,atrim
    make -j"$(sysctl -n hw.ncpu)"
    make install
  )

  mkdir -p "$cache_stage"
  install -m 755 "$install_dir/bin/ffmpeg" "$cache_stage/ffmpeg"
  install -m 755 "$install_dir/bin/ffprobe" "$cache_stage/ffprobe"
  install -m 644 "$source_dir/COPYING.LGPLv2.1" "$cache_stage/COPYING.LGPLv2.1"

  # Validate the staged output before it can replace a previously valid cache.
  validate_runtime "$cache_stage"

  # The provenance marker is written only after the freshly built runtime has
  # passed every validation gate.
  printf '%s\n' "$FFMPEG_TARBALL_SHA256" > "$cache_stage/source.sha256"
  cache_metadata_valid "$cache_stage"

  mkdir -p "$(dirname "$cache_dir")"
  rm -rf "$cache_dir"
  mv "$cache_stage" "$cache_dir"
  cache_ready=true
  echo "Cached validated FFmpeg runtime at: $cache_dir"
fi

mkdir -p "$runtime_output_dir" "$license_output_dir"
install -m 755 "$cache_dir/ffmpeg" "$runtime_output_dir/ffmpeg"
install -m 755 "$cache_dir/ffprobe" "$runtime_output_dir/ffprobe"
install -m 644 "$cache_dir/COPYING.LGPLv2.1" "$license_output_dir/FFmpeg-LGPL-2.1.txt"
release_cache_lock

echo "FFmpeg runtime copied to: $runtime_output_dir"
echo "FFmpeg LGPL license copied to: $license_output_dir/FFmpeg-LGPL-2.1.txt"

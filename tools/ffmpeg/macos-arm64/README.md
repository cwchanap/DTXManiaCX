# Native Apple Silicon FFmpeg runtime

`build-runtime.sh` builds and stages the small, audio-only FFmpeg runtime used
by the macOS arm64 package. It produces this layout:

```text
build-runtime.sh <runtime-output-dir> <license-output-dir>

runtime-output-dir/
  ffmpeg
  ffprobe

license-output-dir/
  FFmpeg-LGPL-2.1.txt
```

This builder is intentionally scoped to native Apple Silicon macOS. It does
not add a production dependency provider, commit native binaries, or use a
bootstrap framework.

## Provenance

- FFmpeg version: `7.0.2`
- Official source: <https://ffmpeg.org/releases/ffmpeg-7.0.2.tar.xz>
- Pinned source tarball SHA-256:
  `8646515b638a3ad303e23af6a3587734447cb8fc0a0c064ecdb8e95c4fd8b389`

The script downloads with `curl -fsSL` and verifies this SHA-256 with
`shasum -a 256 -c` before extracting the source. The source archive's
`COPYING.LGPLv2.1` is carried into the validated cache and is staged as
`FFmpeg-LGPL-2.1.txt` with mode `0644`.

## Configure surface

The builder uses the existing release configure surface and adds only
`--disable-autodetect`:

```bash
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
  --enable-protocol=file,pipe \
  --enable-muxer=pcm_s16le \
  --enable-encoder=pcm_s16le \
  --enable-filter=aformat,anull,aresample,atempo,apad,atrim
```

The required runtime surface is:

- Filters: `atempo`, `apad`, `atrim`, `aformat`, `aresample`
- Decoders: `mp3float`, `vorbis`, `pcm_s16le`, `adpcm_ima_wav`, `adpcm_ms`
- Demuxers: `mp3`, `wav`, `ogg`, `s16le`
- Encoder: `pcm_s16le`
- Muxer: `s16le`

The ADPCM decoders are load-bearing: non-default playback profiles route WAV
sources through FFmpeg, so IMA-ADPCM and Microsoft ADPCM WAV files must remain
playable. No `--enable-lib*`, `libmp3lame`, or `libvorbis` options are used.

`--disable-gpl` and `--disable-nonfree` are intentionally omitted. The
audio-only surface above is selected after `--disable-everything` with
autodetection disabled, and it does not enable GPL or nonfree components.
The approved release configure surface therefore remains explicit about what
is enabled without adding unrelated policy switches.

## Cache and replacement policy

The versioned cache path is:

```text
${DTXMANIA_FFMPEG_CACHE_ROOT:-$HOME/Library/Caches/DTXManiaCX/ffmpeg}/7.0.2/osx-arm64
```

Set `DTXMANIA_FFMPEG_CACHE_ROOT` to relocate the cache root. A cache is used
only when both executables are present and executable, `COPYING.LGPLv2.1`
exists, and `source.sha256` contains the pinned source hash. Cached binaries
then pass the same architecture, capability, version, and dynamic dependency
checks as a fresh build; an invalid cache is rebuilt.

On a cold cache, the script creates a `mktemp -d` work root, downloads,
verifies, configures, compiles, and installs into that temporary root. It
validates the staged runtime before replacing the exact versioned cache leaf,
then writes `source.sha256`. A warm cache copies the validated binaries and
license without downloading or compiling FFmpeg. No cache locking is used.

The clean-cache command targets only this builder's exact version and
architecture directory:

```bash
rm -rf "$HOME/Library/Caches/DTXManiaCX/ffmpeg/7.0.2/osx-arm64"
```

## Validation policy

Both `ffmpeg` and `ffprobe` must be executable arm64 Mach-O binaries and must
respond to `-version`. For each binary, `otool -L` is checked after its header
line; every listed dependency must be under `/usr/lib/` or
`/System/Library/`. Homebrew (`/opt/homebrew`), Intel Homebrew
(`/usr/local`), temporary build paths, and every other non-system dylib path
are rejected.

The host guard hard-fails on Intel Macs and under Rosetta with:

```text
Native CX macOS runtime requires Apple Silicon (arm64); Intel/Rosetta is not supported.
```

## Version and checksum updates

When updating FFmpeg, change `FFMPEG_VERSION`, `FFMPEG_TARBALL_SHA256`, and
`FFMPEG_URL` together in `build-runtime.sh`; update the provenance and
configure-surface notes here; verify the checksum against the official source
tarball; and clear the old versioned cache before a cold build. Re-run the
capability, architecture, dylib, license, and cold/warm checks below before
committing the update.

## Cold and warm verification

Run these commands from the repository root on an Apple Silicon Mac:

```bash
rm -rf "$HOME/Library/Caches/DTXManiaCX/ffmpeg/7.0.2/osx-arm64"
rm -rf /tmp/dtx-ffmpeg-runtime /tmp/dtx-ffmpeg-licenses

bash tools/ffmpeg/macos-arm64/build-runtime.sh \
  /tmp/dtx-ffmpeg-runtime \
  /tmp/dtx-ffmpeg-licenses

test -x /tmp/dtx-ffmpeg-runtime/ffmpeg
test -x /tmp/dtx-ffmpeg-runtime/ffprobe
file -b /tmp/dtx-ffmpeg-runtime/ffmpeg | grep -qi arm64
file -b /tmp/dtx-ffmpeg-runtime/ffprobe | grep -qi arm64
test -f /tmp/dtx-ffmpeg-licenses/FFmpeg-LGPL-2.1.txt
```

Repeat the `bash` command without deleting the cache. The second run should
report the validated cache path and should not download or compile source.

## Startup timing risk

A clean runtime-cache miss during `dotnet run` compiles FFmpeg before the game
launches and may exceed an E2E startup timeout. CI must build or otherwise warm
the runtime before launching E2E; do not add background bootstrap machinery to
hide this first-run cost.

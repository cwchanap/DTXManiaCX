# macOS OBS setup for the DTXManiaCX video recorder

This runbook covers manual, one-time operator setup for running the
`dtx-video` recorder on Apple Silicon macOS. The `dtx-video` CLI commands
(`dtx-video doctor`, `dtx-video record --chart <chart.dtx> --output <dir>`)
are unchanged from Windows, but the recorder now performs environment
reading, target resolution, and platform preflight (arm64, macOS 13+,
bundled FFmpeg pair) before recording.

## Prerequisites

- Apple Silicon Mac (`arm64`) running macOS 13 or newer. The bundled
  application-audio path (ScreenCaptureKit / macOS Audio Capture) requires
  macOS 13+, so older systems are rejected before any recording starts.
- .NET SDK 8+ (`dotnet --info`).
- OBS Studio 30.2 or newer with obs-websocket 5.x.

## Build the exact artifacts the recorder uses

Run all build commands from the repository root. The commands below use
relative paths (`DTXMania.Game/DTXMania.Game.Mac.csproj`) that only resolve
from that location.

The recorder launches the game project in-place with
`--no-build --configuration Debug`. Build the current source first:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
```

The `dtx-video` recorder is a separate project; building the game alone
does not produce it. Build it too:

```bash
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj --configuration Debug
```

Preflight (`doctor` and `record` both run it) checks that the recorder
process runs arm64, that the game project is the Mac project, and that the
bundled FFmpeg pair exists and is executable:

- `DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffmpeg`
- `DTXMania.Game/bin/Debug/net8.0/runtimes/osx-arm64/MMTools/ffprobe`

Preflight does not gate the apphost binary itself.
`DTXMania.Game/bin/Debug/net8.0/DTXMania.Game.Mac` is a native arm64
apphost; manually inspecting it (including the `file` check recorded in the
verification record) is acceptance evidence, not a preflight gate.

The HPA-512 `osx-arm64` FFmpeg pair is required for recorder certification:
a working FFmpeg on `PATH` is not accepted as native evidence, even though
the game itself may fall back to other runtimes.

## Why the recorder uses `--no-build`

Preflight checks the Debug output above (runtime pair and project
identity), and the recorder then launches that same output with
`dotnet run --project ... --no-build --configuration
Debug`. This pins the checked artifact to the launched artifact: the
recorder never silently rebuilds between certification and recording. Build
immediately before capturing evidence so source cannot drift after the
check.

If the runtime pair is missing or not executable, preflight fails before
creating any sandbox or contacting OBS, naming the recovery command:

```bash
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug
```

## OBS manual setup

Configure once in OBS Studio 30.2+:

1. Create a dedicated DTXManiaCX profile, scene collection, and scene.
2. Add a ScreenCaptureKit application/window capture source scoped to the
   DTXManiaCX window. Do not use full-display capture.
3. Configure DTXManiaCX application audio via the capture source or one
   dedicated macOS Audio Capture source (macOS 13+).
4. Disable Desktop Audio on the recorded track.
5. Disable the microphone on the recorded track.
6. Set output to Hybrid MP4.
7. Enable the obs-websocket 5.x server with a password
   (Tools → WebSocket Server Settings).
8. Grant OBS Screen Recording permission in System Settings → Privacy &
   Security when prompted, then restart OBS.
9. Leave OBS idle (not recording) before the recorder starts; the recorder
   owns start/stop via WebSocket.

The recorder does not verify source selection or privacy state; `doctor`
prints these as manual prerequisites only.

## Environment variables

```bash
export DTXMANIA_VIDEO_OBS_URL=ws://127.0.0.1:4455      # loopback only
# Replace the quoted values below before running.
export DTXMANIA_VIDEO_OBS_PASSWORD="your-obs-websocket-password"
export DTXMANIA_VIDEO_OBS_OUTPUT_DIR="/path/to/obs-raw-mp4-output"
# optional: record against an app-data copy other than the default root
export DTXMANIA_APPDATA_ROOT="/path/to/app-data-root"
```

The default source app-data root mirrors the game's own resolution:
`~/Library/Application Support/DTXManiaCX` on macOS.

## Commands

```bash
# build once, immediately before recording
dotnet build DTXMania.Game/DTXMania.Game.Mac.csproj --configuration Debug

# build the dtx-video recorder itself
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj --configuration Debug

# check host, project, runtime pair, config, and OBS connectivity
DTXMania.VideoRecorder/bin/Debug/net8.0/dtx-video doctor

# record one chart
DTXMania.VideoRecorder/bin/Debug/net8.0/dtx-video record \
  --chart <chart.dtx> --output <publish directory>
```

## Artifacts

- Diagnostics (`run.json`, game logs) land under the `--output` directory in
  a per-run subdirectory.
- The raw OBS MP4 lands in `DTXMANIA_VIDEO_OBS_OUTPUT_DIR`.
- The published MP4 lands in the `--output` directory.
- On failure the sandbox (copied app data) is retained for diagnosis; on
  success it is deleted.

## Troubleshooting

- `Recorder platform preflight failed ... Bundled ffmpeg/ffprobe` — run the
  Debug build command above; a PATH FFmpeg does not satisfy certification.
- `OBS auth/status: FAILED` — OBS not running, websocket disabled, or wrong
  password. Check the URL is loopback and matches the websocket server
  settings.
- Black or silent capture — re-check the ScreenCaptureKit source is scoped
  to the DTXManiaCX window and Screen Recording permission is granted.
- No application audio — confirm macOS 13+, the macOS Audio Capture source
  exists, and Desktop Audio/microphone tracks are disabled.
- Recorder launches but no video appears — confirm OBS is idle before the
  recorder starts and the raw output directory matches
  `DTXMANIA_VIDEO_OBS_OUTPUT_DIR`.

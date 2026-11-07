# Windows OBS setup for the DTXManiaCX video recorder

This runbook covers the one-time setup for running the `dtx-video` recorder
on Windows x64, plus the commands for each recording. The recorder launches
CX in a throwaway sandbox (AutoPlay on every lane, NoFail, 1280x720
windowed), drives it from Song Select through the chart to the Result
screen, and tells OBS Studio when to start and stop recording over
obs-websocket. OBS does the capture; the recorder never touches your normal
CX settings, scores, or song database.

## Prerequisites

- Windows 10 2004 or newer, or Windows 11, on x64. Per-application audio
  capture in OBS needs Windows 10 2004+.
- .NET 10 SDK (`dotnet --list-sdks` must show a 10.x SDK; the game and
  recorder target `net10.0`).
- OBS Studio 30.2 or newer (tested with 33.0.1). obs-websocket 5.x ships
  inside OBS; nothing extra to install.
- The chart you want to record must be in CX's song library. The recorder
  rejects any chart that is not under one of CX's song folders (see
  [Choose a chart](#choose-a-chart)).

## Install OBS

Either install it system-wide:

```powershell
winget install --id OBSProject.OBSStudio -e
```

…or use the portable zip from the official
[OBS releases page](https://github.com/obsproject/obs-studio/releases)
(`OBS-Studio-<version>-Windows-x64.zip`). Extract it anywhere, then create an
empty `portable_mode.txt` next to its `bin` folder. Portable OBS keeps all of
its settings in `<extract folder>\config\obs-studio`, so a recording-only
setup never mixes with an OBS you use for streaming. Start it from
`bin\64bit\obs64.exe` (the working directory must be `bin\64bit`).

## OBS setup

Configure once in OBS. Skip the auto-configuration wizard.

1. **Profile and scene collection.** Create a dedicated `DTXManiaCX` profile
   (Profile → New) and scene collection (Scene Collection → New), and select
   both. Add one scene, for example `CX`.
2. **Video.** Settings → Video: Base (Canvas) and Output resolution
   `1280x720`, Common FPS `60`. The recorder always runs CX at 1280x720.
3. **Recording output.** Settings → Output, Output Mode `Simple`:
   - Recording Path: a dedicated folder, for example
     `%USERPROFILE%\Videos\DTXManiaCX\obs-raw`. This must be the same folder
     you export as `DTXMANIA_VIDEO_OBS_OUTPUT_DIR`.
   - Recording Format: `Hybrid MP4`.
   - Recording Quality: `High Quality, Medium File Size` is enough.
   - Encoder: a hardware encoder (NVENC, QuickSync, AMF) if you have one,
     otherwise x264.
4. **No desktop or microphone audio.** Settings → Audio → Global Audio
   Devices: set Desktop Audio, Desktop Audio 2, and every Mic/Auxiliary
   Audio device to `Disabled`. Only CX's own audio should reach the file.
5. **Video source.** In the `CX` scene add **Window Capture**:
   - Window: any `DTXMania.Game.Windows.exe` window (launch CX once so it
     appears in the list).
   - Capture Method: `Windows 10 (1903 and up)`.
   - Window Match Priority: `Match title, otherwise find window of same
     executable`. The recorder's CX window title is not guaranteed to match
     the one you picked, so the executable fallback is what makes it find
     the sandboxed game.
   - Capture Cursor: off. Client Area: on.
   - Right-click the source → Transform → Fit to screen, so the window fills
     the 1280x720 canvas even with display scaling.
6. **Audio source.** In the same scene add **Application Audio Capture
   (BETA)**, pick `DTXMania.Game.Windows.exe`, and set the same Window Match
   Priority as above.
7. **WebSocket.** Tools → WebSocket Server Settings: enable the server, keep
   port `4455`, enable authentication, and set a password. Turn off the
   system tray alerts if you like.
8. Leave OBS open and idle (not recording) before each recorder run. The
   recorder starts and stops only the recording it owns; it refuses to run
   while OBS is already recording.

The recorder does not inspect scenes or sources. `doctor` lists these as
manual prerequisites, and the only real check is watching the output.

## Choose a chart

`--chart` takes an absolute path to a `.dtx` (or other supported chart
file) that sits under one of CX's configured song folders. CX's song
folders are the `SongRoot.N` rows in `%LOCALAPPDATA%\DTXManiaCX\config.db`.
To add a folder, open CX → CONFIG → Song Folders, add it, and apply.

Each recorder run starts with a fresh song database, so every run
re-enumerates all song folders. With a large library this takes a while
before Song Select appears; the recorder allows five minutes for setup.

The chart must declare a `#PREVIEW` sound that exists; the recording opens
with at least ten seconds of it playing on Song Select.

## Recording with different gameplay settings

The sandbox inherits your normal CX settings (skin, scroll speed, song
folders, and so on) from `%LOCALAPPDATA%\DTXManiaCX\config.db`. The recorder
only forces AutoPlay, NoFail, the GameApi, and 1280x720 windowed. To record
with other settings, for example x4.0 scroll speed, without changing how CX
plays normally, keep a separate copy of the config database and point
`DTXMANIA_APPDATA_ROOT` at it:

1. Close CX, then copy `%LOCALAPPDATA%\DTXManiaCX\config.db` into a new
   folder such as `%LOCALAPPDATA%\DTXManiaCX-recording`. Only `config.db` is
   needed there.
2. Change the rows you want in the copy's `ConfigEntries(Key, Value)` table
   with any SQLite tool. Scroll speed is the `ScrollSpeed` row in percent,
   50 to 400 in steps of 50 (`400` is x4.0).
3. Export `DTXMANIA_APPDATA_ROOT` (see below) before running `doctor` and
   `record`.

The copy does not follow later changes to your normal config, so make it
again after you add song folders or change the skin.

## Environment variables

Set these in the PowerShell window that will run the recorder:

```powershell
$env:DTXMANIA_VIDEO_OBS_URL = 'ws://127.0.0.1:4455'   # loopback only
$env:DTXMANIA_VIDEO_OBS_OUTPUT_DIR = "$env:USERPROFILE\Videos\DTXManiaCX\obs-raw"
# optional: read settings from a recording-only config copy instead of %LOCALAPPDATA%\DTXManiaCX
# $env:DTXMANIA_APPDATA_ROOT = "$env:LOCALAPPDATA\DTXManiaCX-recording"
```

Set the WebSocket password without putting it on the command line. Either
prompt for it:

```powershell
$secure = Read-Host 'OBS WebSocket password' -AsSecureString
$env:DTXMANIA_VIDEO_OBS_PASSWORD = [Net.NetworkCredential]::new('', $secure).Password
```

…or keep it in a file under your user profile (never in the repository)
and read it:

```powershell
$env:DTXMANIA_VIDEO_OBS_PASSWORD = (Get-Content "$env:LOCALAPPDATA\dtx-video\obs-websocket-password.txt" -Raw).Trim()
```

`ffprobe` is optional: without it the recorder skips media validation and
prints a warning. The CX Debug build already ships one, so put it on `PATH`
for the session to get the check:

```powershell
# run from the repository root after building the game
$mmtools = (Resolve-Path 'DTXMania.Game\bin\Debug\net*-windows\runtimes\win-x64\MMTools').Path
$env:PATH = "$mmtools;$env:PATH"
```

## Commands

Run everything from the repository root.

```powershell
# build the exact game output the recorder launches (it uses --no-build)
dotnet build DTXMania.Game/DTXMania.Game.Windows.csproj --configuration Debug

# build the recorder itself
dotnet build DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj --configuration Debug

# check project, Debug output, source config, raw output dir, ffprobe, and OBS auth/status
dotnet run --project DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj --configuration Debug --no-build -- doctor

# record one chart
dotnet run --project DTXMania.VideoRecorder/DTXMania.VideoRecorder.csproj --configuration Debug --no-build -- `
  record --chart 'E:\path\to\song\mas.dtx' --output "$env:USERPROFILE\Videos\DTXManiaCX\published"
```

`doctor` must end with `dtx-video doctor: all gates passed`. Rebuild the
game right before recording so the launched build matches your source.

A successful `record` exits with code `0` and prints:

```text
OBS raw output: '<raw folder>\<OBS file name>.mp4'.
Published output: '<published folder>\<OBS file name>.mp4'.
```

Don't use the mouse or keyboard on the CX window during a run. The recorder
drives it, and the capture includes whatever the window shows.

## Artifacts

- **Raw MP4:** written by OBS into `DTXMANIA_VIDEO_OBS_OUTPUT_DIR`. Kept.
- **Published MP4:** a copy of the raw file in the `--output` folder.
- **Diagnostics:** `<output>\diagnostics\<run id>\run.json` plus
  `cx-stdout.log` and `cx-stderr.log`. `run.json` lists each stage the run
  reached (`SongSelectReady`, `PreviewReady`, `PerformanceReady`,
  `ResultCompleted`, …) with game telemetry, and the OBS
  connect/status/start/stop outcomes. The API key and OBS password are
  redacted.
- **Sandbox:** `%TEMP%\DTXManiaCX-video\<run id>`. Deleted on success, kept
  on failure for diagnosis; delete it yourself when done.

## Troubleshooting

- `OBS auth/status: FAILED (Unable to connect to the remote server)`: OBS
  is not running, or the WebSocket server is off or on another port.
- `OBS auth/status: FAILED` with an authentication error: the password in
  `DTXMANIA_VIDEO_OBS_PASSWORD` doesn't match Tools → WebSocket Server
  Settings.
- `OBS is already recording`: stop the existing OBS recording first. The
  recorder never stops a recording it didn't start.
- `OBS StartRecord was sent but its recording state could not be confirmed`:
  OBS accepted the start but never reported an active recording within ten
  seconds (encoder failure, unwritable recording path). Check the newest
  OBS log (Help → Log Files, or `config\obs-studio\logs` for portable OBS),
  and make sure OBS isn't still recording before the next run.
- `The requested chart is not available in the active song library`: the
  chart is not under any CX song folder. Add its folder in CONFIG → Song
  Folders, or point `DTXMANIA_APPDATA_ROOT` at a copy of the app-data
  folder whose `config.db` includes it.
- `The requested chart does not declare a preview file` / `preview file was
  not found`: fix the chart's `#PREVIEW` line or pick another chart.
- `Source config database ... is not normalized`: open CX once, exit
  normally, and retry.
- Black video: the Window Capture source isn't matching the sandboxed CX
  window. Use the `Windows 10 (1903 and up)` method and the `Match title,
  otherwise find window of same executable` priority.
- Silent video, or other apps audible: re-check the Application Audio
  Capture source targets `DTXMania.Game.Windows.exe` and that every global
  audio device is disabled.
- `ffprobe unavailable on PATH; media stream validation skipped.`: harmless;
  add the MMTools folder to `PATH` as shown above to get the check.
